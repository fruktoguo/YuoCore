using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using YuoCore.Tasks;

namespace YuoCore.Benchmarks.Tasks;

[MemoryDiagnoser]
[ThreadingDiagnoser]
public class TaskPrimitiveBenchmarks
{
    private static readonly InvalidOperationException CachedException = new("benchmark exception");

    [Params(1, 64, 1024)]
    public int Operations { get; set; }

    [Benchmark(Baseline = true)]
    public int Task_FromResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += Task.FromResult(i).GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int ValueTask_FromResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += new ValueTask<int>(i).GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int YuoTask_FromResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += YuoTask<int>.FromResult(i).GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int TaskCompletionSource_SetResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(i);
            sum += source.Task.GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int ValueTask_FromTaskCompletionSource_SetResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = new ValueTask<int>(source.Task);
            source.SetResult(i);
            sum += task.GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int EtTaskLike_SetResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = EtTaskLike<int>.Rent();
            source.SetResult(i);
            sum += source.GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int YuoTask_SetResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = YuoTaskCompletionSource<int>.Rent();
            var task = source.Task;
            source.SetResult(i);
            sum += task.GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int YuoTask_TrySetResult_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = YuoTaskCompletionSource<int>.Rent();
            var task = source.Task;
            source.TrySetResult(i);
            sum += task.GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int YuoTask_FromTask_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += YuoTask<int>.FromTask(Task.FromResult(i)).GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int YuoTask_AsyncSync_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            sum += ReturnYuoTaskSynchronously(i).GetAwaiter().GetResult();
        }

        return sum;
    }

    [Benchmark]
    public int YuoTask_WaitAll_Small_GetResult()
    {
        var sum = 0;
        for (var i = 0; i < Operations; i++)
        {
            var results = YuoTask.WaitAll(YuoTask<int>.FromResult(i), YuoTask<int>.FromResult(1)).GetAwaiter().GetResult();
            sum += results[0] + results[1];
        }

        return sum;
    }

    [Benchmark]
    public int TaskCompletionSource_SetException_Catch()
    {
        var count = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetException(CachedException);
            try
            {
                _ = source.Task.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public int EtTaskLike_SetException_Catch()
    {
        var count = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = EtTaskLike<int>.Rent();
            source.SetException(CachedException);
            try
            {
                _ = source.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public int YuoTask_SetException_Catch()
    {
        var count = 0;
        for (var i = 0; i < Operations; i++)
        {
            var source = YuoTaskCompletionSource<int>.Rent();
            var task = source.Task;
            source.SetException(CachedException);
            try
            {
                _ = task.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public int Parallel_TaskCompletionSource_SetResult_GetResult()
    {
        return ParallelSum(i =>
        {
            var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(i);
            return source.Task.GetAwaiter().GetResult();
        });
    }

    [Benchmark]
    public int Parallel_EtTaskLike_SetResult_GetResult()
    {
        return ParallelSum(i =>
        {
            var source = EtTaskLike<int>.Rent();
            source.SetResult(i);
            return source.GetAwaiter().GetResult();
        });
    }

    [Benchmark]
    public int Parallel_YuoTask_SetResult_GetResult()
    {
        return ParallelSum(i =>
        {
            var source = YuoTaskCompletionSource<int>.Rent();
            var task = source.Task;
            source.SetResult(i);
            return task.GetAwaiter().GetResult();
        });
    }

    private static async YuoTask<int> ReturnYuoTaskSynchronously(int value)
    {
        await YuoTask.CompletedTask;
        return value;
    }

    private int ParallelSum(Func<int, int> operation)
    {
        var sum = 0;
        Parallel.For(0, Operations, () => 0, (i, _, local) => local + operation(i), local => Interlocked.Add(ref sum, local));
        return sum;
    }
}
