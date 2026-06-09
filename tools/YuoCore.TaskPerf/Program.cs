using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using YuoCore.Tasks;

const int SyncIterations = 2_000_000;
const int ParallelIterations = 200_000;
const int WarmupIterations = 100_000;

Console.WriteLine($"Runtime: {Environment.Version}");
Console.WriteLine($"ProcessorCount: {Environment.ProcessorCount}");
Console.WriteLine($"SyncIterations: {SyncIterations:N0}");
Console.WriteLine($"ParallelIterations: {ParallelIterations:N0}");
Console.WriteLine();

Warmup();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

RunSync("Task.FromResult", SyncIterations, static i => Task.FromResult(i).GetAwaiter().GetResult());
RunSync("ValueTask.FromResult", SyncIterations, static i => new ValueTask<int>(i).GetAwaiter().GetResult());
RunSync("YuoTask.FromResult", SyncIterations, static i => YuoTask<int>.FromResult(i).GetAwaiter().GetResult());
Console.WriteLine();
RunSync("TaskCompletionSource", SyncIterations, static i =>
{
    var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    source.SetResult(i);
    return source.Task.GetAwaiter().GetResult();
});
RunSync("ETTask-like pooled", SyncIterations, static i =>
{
    var source = EtTaskLike<int>.Rent();
    source.SetResult(i);
    return source.GetAwaiter().GetResult();
});
RunSync("YuoTask pooled", SyncIterations, static i =>
{
    var source = YuoTaskCompletionSource<int>.Rent();
    var task = source.Task;
    source.SetResult(i);
    return task.GetAwaiter().GetResult();
});
RunSync("YuoTask TrySetResult", SyncIterations, static i =>
{
    var source = YuoTaskCompletionSource<int>.Rent();
    var task = source.Task;
    source.TrySetResult(i);
    return task.GetAwaiter().GetResult();
});
RunSync("YuoTask FromTask", SyncIterations, static i => YuoTask<int>.FromTask(Task.FromResult(i)).GetAwaiter().GetResult());
RunSync("YuoTask async sync", SyncIterations, static i => ReturnYuoTaskSynchronously(i).GetAwaiter().GetResult());
RunSync("YuoTask WaitAll small", SyncIterations, static i =>
{
    var results = YuoTask.WaitAll(YuoTask<int>.FromResult(i), YuoTask<int>.FromResult(1)).GetAwaiter().GetResult();
    return results[0] + results[1];
});
RunSync("YuoTask SetCanceled", SyncIterations / 16, static i =>
{
    var source = YuoTaskCompletionSource<int>.Rent();
    var task = source.Task;
    source.SetCanceled();
    try
    {
        return task.GetAwaiter().GetResult();
    }
    catch (OperationCanceledException)
    {
        return i;
    }
});
Console.WriteLine();
RunParallel("Parallel TaskCompletionSource", ParallelIterations, static i =>
{
    var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    source.SetResult(i);
    return source.Task.GetAwaiter().GetResult();
});
RunParallel("Parallel ETTask-like pooled", ParallelIterations, static i =>
{
    var source = EtTaskLike<int>.Rent();
    source.SetResult(i);
    return source.GetAwaiter().GetResult();
});
RunParallel("Parallel YuoTask pooled", ParallelIterations, static i =>
{
    var source = YuoTaskCompletionSource<int>.Rent();
    var task = source.Task;
    source.SetResult(i);
    return task.GetAwaiter().GetResult();
});

static void Warmup()
{
    for (var i = 0; i < WarmupIterations; i++)
    {
        _ = Task.FromResult(i).GetAwaiter().GetResult();
        _ = new ValueTask<int>(i).GetAwaiter().GetResult();
        _ = YuoTask<int>.FromResult(i).GetAwaiter().GetResult();

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(i);
        _ = tcs.Task.GetAwaiter().GetResult();

        var et = EtTaskLike<int>.Rent();
        et.SetResult(i);
        _ = et.GetAwaiter().GetResult();

        var yuo = YuoTaskCompletionSource<int>.Rent();
        var task = yuo.Task;
        yuo.SetResult(i);
        _ = task.GetAwaiter().GetResult();
    }
}

static void RunSync(string name, int iterations, Func<int, int> operation)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
    var beforeGen0 = GC.CollectionCount(0);
    var stopwatch = Stopwatch.StartNew();
    var sum = 0;

    for (var i = 0; i < iterations; i++)
    {
        sum += operation(i);
    }

    stopwatch.Stop();
    var afterAllocated = GC.GetAllocatedBytesForCurrentThread();
    var afterGen0 = GC.CollectionCount(0);
    Print(name, iterations, stopwatch.Elapsed, afterAllocated - beforeAllocated, afterGen0 - beforeGen0, sum);
}

static void RunParallel(string name, int iterations, Func<int, int> operation)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAllocated = GC.GetTotalAllocatedBytes(true);
    var beforeGen0 = GC.CollectionCount(0);
    var stopwatch = Stopwatch.StartNew();
    var sum = 0;

    Parallel.For(0, iterations, () => 0, (i, _, local) => local + operation(i), local => Interlocked.Add(ref sum, local));

    stopwatch.Stop();
    var afterAllocated = GC.GetTotalAllocatedBytes(true);
    var afterGen0 = GC.CollectionCount(0);
    Print(name, iterations, stopwatch.Elapsed, afterAllocated - beforeAllocated, afterGen0 - beforeGen0, sum);
}

static void Print(string name, int iterations, TimeSpan elapsed, long allocatedBytes, int gen0, int sum)
{
    var nsPerOp = elapsed.TotalNanoseconds / iterations;
    var bytesPerOp = (double)allocatedBytes / iterations;
    Console.WriteLine($"{name,-32} {nsPerOp,10:N2} ns/op  {bytesPerOp,10:N2} B/op  Gen0={gen0,3}  checksum={sum}");
}

static async YuoTask<int> ReturnYuoTaskSynchronously(int value)
{
    await YuoTask.CompletedTask;
    return value;
}

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
        _result = result;
        _status = AwaiterStatus.Succeeded;
        var continuation = _callbackOrException as Action;
        _callbackOrException = null;
        continuation?.Invoke();
    }

    public void SetException(Exception exception)
    {
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
