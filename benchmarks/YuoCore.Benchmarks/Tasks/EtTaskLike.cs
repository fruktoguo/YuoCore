using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace YuoCore.Benchmarks.Tasks;

/// <summary>
/// 只用于基准测试的极简 ETTask-like awaitable。
/// 这不是 ET10 原始实现，只用来对比“自定义 awaitable + 池化完成源”的基础成本。
/// </summary>
internal sealed class EtTaskLike<T> : ICriticalNotifyCompletion
{
    private static readonly ConcurrentQueue<EtTaskLike<T>> Pool = new();

    private AwaiterStatus _status;
    private object? _callbackOrException;
    private T? _result;
    private int _returned;

    private EtTaskLike()
    {
    }

    public static EtTaskLike<T> Rent()
    {
        if (!Pool.TryDequeue(out var task))
        {
            task = new EtTaskLike<T>();
        }

        task._status = AwaiterStatus.Pending;
        task._callbackOrException = null;
        task._result = default;
        task._returned = 0;
        return task;
    }

    public EtTaskLike<T> GetAwaiter() => this;

    public bool IsCompleted => _status != AwaiterStatus.Pending;

    public void SetResult(T result)
    {
        if (_status != AwaiterStatus.Pending)
        {
            throw new InvalidOperationException("Task already completed.");
        }

        _result = result;
        _status = AwaiterStatus.Succeeded;
        var continuation = _callbackOrException as Action;
        _callbackOrException = null;
        continuation?.Invoke();
    }

    public void SetException(Exception exception)
    {
        if (_status != AwaiterStatus.Pending)
        {
            throw new InvalidOperationException("Task already completed.");
        }

        _status = AwaiterStatus.Faulted;
        var continuation = _callbackOrException as Action;
        _callbackOrException = ExceptionDispatchInfo.Capture(exception);
        continuation?.Invoke();
    }

    public T GetResult()
    {
        try
        {
            return _status switch
            {
                AwaiterStatus.Succeeded => _result!,
                AwaiterStatus.Faulted => ThrowCaptured(),
                _ => throw new NotSupportedException("Task is not completed.")
            };
        }
        finally
        {
            Return();
        }
    }

    public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

    public void UnsafeOnCompleted(Action continuation)
    {
        if (_status != AwaiterStatus.Pending)
        {
            continuation();
            return;
        }

        _callbackOrException = continuation;
    }

    private T ThrowCaptured()
    {
        var exception = (ExceptionDispatchInfo?)_callbackOrException;
        _callbackOrException = null;
        exception?.Throw();
        throw new InvalidOperationException("Missing captured exception.");
    }

    private void Return()
    {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
        {
            return;
        }

        _callbackOrException = null;
        _result = default;
        Pool.Enqueue(this);
    }

    private enum AwaiterStatus : byte
    {
        Pending,
        Succeeded,
        Faulted
    }
}
