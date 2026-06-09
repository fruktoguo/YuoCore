using System.Runtime.CompilerServices;

namespace YuoCore.Tasks;

/// <summary>
/// YuoCore 内部高频路径可选使用的轻量 awaitable。
/// 池化创建的 YuoTask 实例只能被消费一次，调用方必须通过 await、Forget、Coroutine、return 或显式保存来接住结果。
/// </summary>
[AsyncMethodBuilder(typeof(YuoTaskMethodBuilder))]
public readonly struct YuoTask
{
    private readonly YuoTaskCompletionSource? _source;
    private readonly int _version;

    internal YuoTask(YuoTaskCompletionSource source, int version)
    {
        _source = source;
        _version = version;
    }

    public static YuoTask CompletedTask => default;

    public bool IsCompleted => _source is null || _source.IsCompleted(_version);

    public Awaiter GetAwaiter() => new(_source, _version);

    public Task AsTask() => AwaitAsTask(this);

    public static YuoTask FromTask(Task task)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        return AwaitTaskAsync(task);
    }

    public static YuoTask FromValueTask(ValueTask task) => AwaitValueTaskAsync(task);

    public static YuoTask FromException(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var source = YuoTaskCompletionSource.Rent();
        var task = source.Task;
        source.SetException(exception);
        return task;
    }

    public static YuoTask FromCanceled(CancellationToken cancellationToken = default)
    {
        var source = YuoTaskCompletionSource.Rent();
        var task = source.Task;
        source.SetCanceled(cancellationToken);
        return task;
    }

    public static YuoTask WaitAll(params YuoTask[] tasks) => YuoTaskCombinators.WaitAll(tasks);

    public static YuoTask WaitAll(IReadOnlyList<YuoTask> tasks) => YuoTaskCombinators.WaitAll(tasks);

    public static YuoTask<T[]> WaitAll<T>(params YuoTask<T>[] tasks) => YuoTaskCombinators.WaitAll(tasks);

    public static YuoTask<T[]> WaitAll<T>(IReadOnlyList<YuoTask<T>> tasks) => YuoTaskCombinators.WaitAll(tasks);

    public static YuoTask<int> WaitAny(params YuoTask[] tasks) => YuoTaskCombinators.WaitAny(tasks);

    public static YuoTask<int> WaitAny(IReadOnlyList<YuoTask> tasks) => YuoTaskCombinators.WaitAny(tasks);

    public static YuoTask<int> WaitAny<T>(params YuoTask<T>[] tasks) => YuoTaskCombinators.WaitAny(tasks);

    public static YuoTask<int> WaitAny<T>(IReadOnlyList<YuoTask<T>> tasks) => YuoTaskCombinators.WaitAny(tasks);

    public void Forget(Action<Exception>? exceptionHandler = null) => ForgetAsync(this, exceptionHandler);

    public void Coroutine(Action<Exception>? exceptionHandler = null) => Forget(exceptionHandler);

    private static async void ForgetAsync(YuoTask task, Action<Exception>? exceptionHandler)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            if (exceptionHandler is not null)
            {
                try
                {
                    exceptionHandler(exception);
                }
                catch
                {
                }

                return;
            }

            YuoTaskExceptionHandler.Publish(exception);
        }
    }

    private static async Task AwaitAsTask(YuoTask task)
    {
        await task;
    }

    private static async YuoTask AwaitTaskAsync(Task task)
    {
        await task;
    }

    private static async YuoTask AwaitValueTaskAsync(ValueTask task)
    {
        await task;
    }

    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        private readonly YuoTaskCompletionSource? _source;
        private readonly int _version;

        internal Awaiter(YuoTaskCompletionSource? source, int version)
        {
            _source = source;
            _version = version;
        }

        public bool IsCompleted => _source is null || _source.IsCompleted(_version);

        public void GetResult()
        {
            _source?.GetResult(_version);
        }

        public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

        public void UnsafeOnCompleted(Action continuation)
        {
            if (_source is null)
            {
                continuation();
                return;
            }

            _source.OnCompleted(_version, continuation);
        }
    }
}

/// <summary>
/// 带返回值的轻量 awaitable。
/// 它面向 Actor/Fiber 等内部热路径，公共 API 边界仍优先使用 Task 或 ValueTask。
/// </summary>
[AsyncMethodBuilder(typeof(YuoTaskMethodBuilder<>))]
public readonly struct YuoTask<T>
{
    private readonly YuoTaskCompletionSource<T>? _source;
    private readonly int _version;
    private readonly T? _result;

    internal YuoTask(YuoTaskCompletionSource<T> source, int version)
    {
        _source = source;
        _version = version;
        _result = default;
    }

    private YuoTask(T result)
    {
        _source = null;
        _version = 0;
        _result = result;
    }

    public bool IsCompleted => _source is null || _source.IsCompleted(_version);

    public Awaiter GetAwaiter() => new(_source, _version, _result);

    public Task<T> AsTask() => AwaitAsTask(this);

    public static YuoTask<T> FromResult(T result) => new(result);

    public static YuoTask<T> FromTask(Task<T> task)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        return AwaitTaskAsync(task);
    }

    public static YuoTask<T> FromValueTask(ValueTask<T> task) => AwaitValueTaskAsync(task);

    public static YuoTask<T> FromException(Exception exception)
    {
        if (exception is null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var source = YuoTaskCompletionSource<T>.Rent();
        var task = source.Task;
        source.SetException(exception);
        return task;
    }

    public static YuoTask<T> FromCanceled(CancellationToken cancellationToken = default)
    {
        var source = YuoTaskCompletionSource<T>.Rent();
        var task = source.Task;
        source.SetCanceled(cancellationToken);
        return task;
    }

    public void Forget(Action<Exception>? exceptionHandler = null) => ForgetAsync(this, exceptionHandler);

    public void Coroutine(Action<Exception>? exceptionHandler = null) => Forget(exceptionHandler);

    private static async void ForgetAsync(YuoTask<T> task, Action<Exception>? exceptionHandler)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            if (exceptionHandler is not null)
            {
                try
                {
                    exceptionHandler(exception);
                }
                catch
                {
                }

                return;
            }

            YuoTaskExceptionHandler.Publish(exception);
        }
    }

    private static async Task<T> AwaitAsTask(YuoTask<T> task)
    {
        return await task;
    }

    private static async YuoTask<T> AwaitTaskAsync(Task<T> task)
    {
        return await task;
    }

    private static async YuoTask<T> AwaitValueTaskAsync(ValueTask<T> task)
    {
        return await task;
    }

    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        private readonly YuoTaskCompletionSource<T>? _source;
        private readonly int _version;
        private readonly T? _result;

        internal Awaiter(YuoTaskCompletionSource<T>? source, int version, T? result)
        {
            _source = source;
            _version = version;
            _result = result;
        }

        public bool IsCompleted => _source is null || _source.IsCompleted(_version);

        public T GetResult()
        {
            return _source is null ? _result! : _source.GetResult(_version);
        }

        public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);

        public void UnsafeOnCompleted(Action continuation)
        {
            if (_source is null)
            {
                continuation();
                return;
            }

            _source.OnCompleted(_version, continuation);
        }
    }
}
