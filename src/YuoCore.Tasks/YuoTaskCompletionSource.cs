using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace YuoCore.Tasks;

/// <summary>
/// 非泛型 YuoTask 的池化完成源。
/// 它避免依赖 ValueTaskSource 的通用状态机，专门服务 YuoTask 的一次性消费语义。
/// </summary>
public sealed class YuoTaskCompletionSource
{
    private const int Pending = 0;
    private const int Succeeded = 1;
    private const int Faulted = 2;
    private const int Canceled = 3;
    private const int Completing = 4;
    private const int LocalPoolLimit = 1024;

    private static readonly object CompletedSentinel = new();
    private static readonly ConcurrentQueue<YuoTaskCompletionSource> GlobalPool = new();

    [ThreadStatic]
    private static Stack<YuoTaskCompletionSource>? localPool;

    private int _version;
    private int _status;
    private object? _continuation;
    private ExceptionDispatchInfo? _exception;
    private int _consumed;
    private int _returned;
    private bool _runContinuationsAsynchronously;

    private YuoTaskCompletionSource()
    {
    }

    public static YuoTaskCompletionSource Rent(bool runContinuationsAsynchronously = false)
    {
        var pool = localPool;
        if (pool is null)
        {
            pool = new Stack<YuoTaskCompletionSource>();
            localPool = pool;
        }

        if (!pool.TryPop(out var source) && !GlobalPool.TryDequeue(out source))
        {
            source = new YuoTaskCompletionSource();
        }

        source.Reset(runContinuationsAsynchronously);
        return source;
    }

    public YuoTask Task => new(this, _version);

    public void SetResult()
    {
        if (!TrySetResult())
        {
            ThrowAlreadyCompleted();
        }
    }

    public bool TrySetResult() => TryComplete(Succeeded, null);

    public void SetException(Exception exception)
    {
        if (!TrySetException(exception))
        {
            ThrowAlreadyCompleted();
        }
    }

    public bool TrySetException(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        return TryComplete(Faulted, ExceptionDispatchInfo.Capture(exception));
    }

    public void SetCanceled(CancellationToken cancellationToken = default)
    {
        if (!TrySetCanceled(cancellationToken))
        {
            ThrowAlreadyCompleted();
        }
    }

    public bool TrySetCanceled(CancellationToken cancellationToken = default)
    {
        return TryComplete(Canceled, ExceptionDispatchInfo.Capture(new OperationCanceledException(cancellationToken)));
    }

    internal bool IsCompleted(int version)
    {
        ValidateVersion(version);
        var status = Volatile.Read(ref _status);
        return status is Succeeded or Faulted or Canceled;
    }

    internal void OnCompleted(int version, Action continuation)
    {
        if (continuation is null)
        {
            throw new ArgumentNullException(nameof(continuation));
        }

        ValidateVersion(version);

        while (true)
        {
            var current = Volatile.Read(ref _continuation);
            if (ReferenceEquals(current, CompletedSentinel))
            {
                InvokeContinuation(continuation);
                return;
            }

            if (current is not null)
            {
                throw new InvalidOperationException("YuoTask can only register one continuation.");
            }

            if (Interlocked.CompareExchange(ref _continuation, continuation, null) is null)
            {
                return;
            }
        }
    }

    internal void GetResult(int version)
    {
        ValidateVersion(version);
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
        {
            throw new InvalidOperationException("YuoTask can only be consumed once.");
        }

        var status = Volatile.Read(ref _status);
        if (status is Pending or Completing)
        {
            // 未完成取结果属于误用：完成方仍持有本 source，此处绝不能还池，
            // 否则完成方随后的 TrySet 会写穿复用后的新租户任务。
            throw new InvalidOperationException("YuoTask has not completed.");
        }

        try
        {
            if (status is Faulted or Canceled)
            {
                _exception?.Throw();
                throw new InvalidOperationException("YuoTask completed without a stored exception.");
            }
        }
        finally
        {
            Return();
        }
    }

    private bool TryComplete(int status, ExceptionDispatchInfo? exception)
    {
        if (Interlocked.CompareExchange(ref _status, Completing, Pending) != Pending)
        {
            return false;
        }

        _exception = exception;
        Volatile.Write(ref _status, status);
        var continuation = Interlocked.Exchange(ref _continuation, CompletedSentinel);
        if (continuation is Action action)
        {
            InvokeContinuation(action);
        }

        return true;
    }

    private static void ThrowAlreadyCompleted()
    {
        throw new InvalidOperationException("YuoTaskCompletionSource has already completed.");
    }

    private void Reset(bool runContinuationsAsynchronously)
    {
        unchecked
        {
            _version++;
            if (_version == 0)
            {
                _version = 1;
            }
        }

        _status = Pending;
        _continuation = null;
        _exception = null;
        _consumed = 0;
        _returned = 0;
        _runContinuationsAsynchronously = runContinuationsAsynchronously;
    }

    private void ValidateVersion(int version)
    {
        if (version != Volatile.Read(ref _version))
        {
            throw new InvalidOperationException("YuoTask source token is no longer valid.");
        }
    }

    private void InvokeContinuation(Action continuation)
    {
        if (!_runContinuationsAsynchronously)
        {
            InvokeContinuationSafely(continuation);
            return;
        }

        ThreadPool.QueueUserWorkItem(static state => InvokeContinuationSafely((Action)state!), continuation);
    }

    private static void InvokeContinuationSafely(Action continuation)
    {
        try
        {
            continuation();
        }
        catch (Exception exception)
        {
            YuoTaskExceptionHandler.Publish(exception);
        }
    }

    private void Return()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
        {
            return;
        }

        _exception = null;
        _continuation = CompletedSentinel;

        var pool = localPool;
        if (pool is null)
        {
            pool = new Stack<YuoTaskCompletionSource>();
            localPool = pool;
        }

        if (pool.Count < LocalPoolLimit)
        {
            pool.Push(this);
            return;
        }

        GlobalPool.Enqueue(this);
    }
}

/// <summary>
/// 泛型 YuoTask 的池化完成源。
/// 同一个 Task 结果被 await 后会归还池，旧 token 在 source 复用后会失效。
/// </summary>
public sealed class YuoTaskCompletionSource<T>
{
    private const int Pending = 0;
    private const int Succeeded = 1;
    private const int Faulted = 2;
    private const int Canceled = 3;
    private const int Completing = 4;
    private const int LocalPoolLimit = 1024;

    private static readonly object CompletedSentinel = new();
    private static readonly ConcurrentQueue<YuoTaskCompletionSource<T>> GlobalPool = new();

    [ThreadStatic]
    private static Stack<YuoTaskCompletionSource<T>>? localPool;

    private int _version;
    private int _status;
    private object? _continuation;
    private ExceptionDispatchInfo? _exception;
    private int _consumed;
    private int _returned;
    private bool _runContinuationsAsynchronously;
    private T? _result;

    private YuoTaskCompletionSource()
    {
    }

    public static YuoTaskCompletionSource<T> Rent(bool runContinuationsAsynchronously = false)
    {
        var pool = localPool;
        if (pool is null)
        {
            pool = new Stack<YuoTaskCompletionSource<T>>();
            localPool = pool;
        }

        if (!pool.TryPop(out var source) && !GlobalPool.TryDequeue(out source))
        {
            source = new YuoTaskCompletionSource<T>();
        }

        source.Reset(runContinuationsAsynchronously);
        return source;
    }

    public YuoTask<T> Task => new(this, _version);

    public void SetResult(T result)
    {
        if (!TrySetResult(result))
        {
            ThrowAlreadyCompleted();
        }
    }

    public bool TrySetResult(T result)
    {
        if (Interlocked.CompareExchange(ref _status, Completing, Pending) != Pending)
        {
            return false;
        }

        _result = result;
        Volatile.Write(ref _status, Succeeded);
        var continuation = Interlocked.Exchange(ref _continuation, CompletedSentinel);
        if (continuation is Action action)
        {
            InvokeContinuation(action);
        }

        return true;
    }

    public void SetException(Exception exception)
    {
        if (!TrySetException(exception))
        {
            ThrowAlreadyCompleted();
        }
    }

    public bool TrySetException(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        return TryComplete(Faulted, ExceptionDispatchInfo.Capture(exception));
    }

    public void SetCanceled(CancellationToken cancellationToken = default)
    {
        if (!TrySetCanceled(cancellationToken))
        {
            ThrowAlreadyCompleted();
        }
    }

    public bool TrySetCanceled(CancellationToken cancellationToken = default)
    {
        return TryComplete(Canceled, ExceptionDispatchInfo.Capture(new OperationCanceledException(cancellationToken)));
    }

    internal bool IsCompleted(int version)
    {
        ValidateVersion(version);
        var status = Volatile.Read(ref _status);
        return status is Succeeded or Faulted or Canceled;
    }

    internal void OnCompleted(int version, Action continuation)
    {
        if (continuation is null)
        {
            throw new ArgumentNullException(nameof(continuation));
        }

        ValidateVersion(version);

        while (true)
        {
            var current = Volatile.Read(ref _continuation);
            if (ReferenceEquals(current, CompletedSentinel))
            {
                InvokeContinuation(continuation);
                return;
            }

            if (current is not null)
            {
                throw new InvalidOperationException("YuoTask can only register one continuation.");
            }

            if (Interlocked.CompareExchange(ref _continuation, continuation, null) is null)
            {
                return;
            }
        }
    }

    internal T GetResult(int version)
    {
        ValidateVersion(version);
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
        {
            throw new InvalidOperationException("YuoTask can only be consumed once.");
        }

        var status = Volatile.Read(ref _status);
        if (status is Pending or Completing)
        {
            // 未完成取结果属于误用：完成方仍持有本 source，此处绝不能还池，
            // 否则完成方随后的 TrySet 会写穿复用后的新租户任务。
            throw new InvalidOperationException("YuoTask has not completed.");
        }

        try
        {
            if (status is Faulted or Canceled)
            {
                _exception?.Throw();
                throw new InvalidOperationException("YuoTask completed without a stored exception.");
            }

            return _result!;
        }
        finally
        {
            Return();
        }
    }

    private bool TryComplete(int status, ExceptionDispatchInfo? exception)
    {
        if (Interlocked.CompareExchange(ref _status, Completing, Pending) != Pending)
        {
            return false;
        }

        _exception = exception;
        Volatile.Write(ref _status, status);
        var continuation = Interlocked.Exchange(ref _continuation, CompletedSentinel);
        if (continuation is Action action)
        {
            InvokeContinuation(action);
        }

        return true;
    }

    private static void ThrowAlreadyCompleted()
    {
        throw new InvalidOperationException("YuoTaskCompletionSource has already completed.");
    }

    private void Reset(bool runContinuationsAsynchronously)
    {
        unchecked
        {
            _version++;
            if (_version == 0)
            {
                _version = 1;
            }
        }

        _status = Pending;
        _continuation = null;
        _exception = null;
        _consumed = 0;
        _returned = 0;
        _runContinuationsAsynchronously = runContinuationsAsynchronously;
        _result = default;
    }

    private void ValidateVersion(int version)
    {
        if (version != Volatile.Read(ref _version))
        {
            throw new InvalidOperationException("YuoTask source token is no longer valid.");
        }
    }

    private void InvokeContinuation(Action continuation)
    {
        if (!_runContinuationsAsynchronously)
        {
            InvokeContinuationSafely(continuation);
            return;
        }

        ThreadPool.QueueUserWorkItem(static state => InvokeContinuationSafely((Action)state!), continuation);
    }

    private static void InvokeContinuationSafely(Action continuation)
    {
        try
        {
            continuation();
        }
        catch (Exception exception)
        {
            YuoTaskExceptionHandler.Publish(exception);
        }
    }

    private void Return()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
        {
            return;
        }

        _exception = null;
        _continuation = CompletedSentinel;
        _result = default;

        var pool = localPool;
        if (pool is null)
        {
            pool = new Stack<YuoTaskCompletionSource<T>>();
            localPool = pool;
        }

        if (pool.Count < LocalPoolLimit)
        {
            pool.Push(this);
            return;
        }

        GlobalPool.Enqueue(this);
    }
}
