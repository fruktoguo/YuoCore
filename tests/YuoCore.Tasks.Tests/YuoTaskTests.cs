using YuoCore.Tasks;

namespace YuoCore.Tasks.Tests;

public class YuoTaskTests
{
    [Fact]
    public async Task Completed_task_can_be_awaited()
    {
        await YuoTask.CompletedTask;
    }

    [Fact]
    public async Task Result_task_can_be_awaited()
    {
        var result = await YuoTask<int>.FromResult(42);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Async_yuotask_builder_can_complete_synchronously()
    {
        await CompleteSynchronouslyAsync();
    }

    [Fact]
    public async Task Async_yuotask_builder_can_return_result_synchronously()
    {
        var result = await ReturnSynchronouslyAsync();

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Async_yuotask_builder_can_resume_after_pending_source()
    {
        var source = YuoTaskCompletionSource<int>.Rent();
        var pending = AwaitAndAddAsync(source.Task, 1);

        Assert.False(pending.IsCompleted);
        source.SetResult(41);

        var result = await pending;
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Completion_source_returns_result()
    {
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;

        source.SetResult(7);
        var result = await task;

        Assert.Equal(7, result);
    }

    [Fact]
    public async Task Non_generic_completion_source_can_complete()
    {
        var source = YuoTaskCompletionSource.Rent();
        var task = source.Task;

        source.SetResult();
        await task;
    }

    [Fact]
    public async Task Pending_continuation_runs_when_source_completes()
    {
        var source = YuoTaskCompletionSource.Rent();
        var observed = false;
        var task = ObserveAsync(source.Task, () => observed = true);

        Assert.False(task.IsCompleted);
        Assert.False(observed);

        source.SetResult();
        await task;

        Assert.True(observed);
    }

    [Fact]
    public async Task Completion_source_propagates_exception()
    {
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;
        var expected = new InvalidOperationException("boom");

        source.SetException(expected);
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Completion_source_propagates_cancellation()
    {
        var source = YuoTaskCompletionSource.Rent();
        var task = source.Task;
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();
        source.SetCanceled(cancellation.Token);

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public void Try_set_methods_return_false_after_completion()
    {
        var source = YuoTaskCompletionSource<int>.Rent();

        Assert.True(source.TrySetResult(1));
        Assert.False(source.TrySetResult(2));
        Assert.False(source.TrySetException(new InvalidOperationException("late")));
        Assert.False(source.TrySetCanceled());
    }

    [Fact]
    public async Task Async_yuotask_builder_propagates_exception()
    {
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () => await ThrowAsync());

        Assert.Equal("builder", actual.Message);
    }

    [Fact]
    public async Task Forget_routes_exceptions_to_global_handler()
    {
        var previous = YuoTaskExceptionHandler.UnhandledException;
        var captured = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("forget");
        YuoTaskExceptionHandler.UnhandledException = captured.SetResult;

        try
        {
            YuoTask.FromException(expected).Forget();
            var actual = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(expected, actual);
        }
        finally
        {
            YuoTaskExceptionHandler.UnhandledException = previous;
        }
    }

    [Fact]
    public void Forget_ignores_exceptions_thrown_by_global_handler()
    {
        var previous = YuoTaskExceptionHandler.UnhandledException;
        YuoTaskExceptionHandler.UnhandledException = _ => throw new InvalidOperationException("handler failed");

        try
        {
            YuoTask.FromException(new InvalidOperationException("source failed")).Forget();
        }
        finally
        {
            YuoTaskExceptionHandler.UnhandledException = previous;
        }
    }

    [Fact]
    public async Task Forget_uses_local_exception_handler_when_provided()
    {
        var previous = YuoTaskExceptionHandler.UnhandledException;
        var captured = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("local");
        YuoTaskExceptionHandler.UnhandledException = _ => throw new InvalidOperationException("global should not run");

        try
        {
            YuoTask.FromException(expected).Forget(captured.SetResult);
            var actual = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(expected, actual);
        }
        finally
        {
            YuoTaskExceptionHandler.UnhandledException = previous;
        }
    }

    [Fact]
    public async Task Source_pool_can_reuse_completed_source()
    {
        var firstSource = YuoTaskCompletionSource<int>.Rent();
        var firstTask = firstSource.Task;
        firstSource.SetResult(1);
        Assert.Equal(1, await firstTask);

        var secondSource = YuoTaskCompletionSource<int>.Rent();
        Assert.Same(firstSource, secondSource);

        var secondTask = secondSource.Task;
        secondSource.SetResult(2);
        Assert.Equal(2, await secondTask);
    }

    [Fact]
    public async Task Reused_source_invalidates_old_task_token()
    {
        var firstSource = YuoTaskCompletionSource<int>.Rent();
        var oldTask = firstSource.Task;
        firstSource.SetResult(1);
        Assert.Equal(1, await oldTask);

        var secondSource = YuoTaskCompletionSource<int>.Rent();
        Assert.Same(firstSource, secondSource);
        var newTask = secondSource.Task;
        secondSource.SetResult(2);
        Assert.Equal(2, await newTask);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await oldTask);
    }

    [Fact]
    public async Task Pooled_task_cannot_be_awaited_twice()
    {
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;
        source.SetResult(1);

        Assert.Equal(1, await task);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
    }

    [Fact]
    public async Task Asynchronous_continuation_option_does_not_inline_continuation()
    {
        var source = YuoTaskCompletionSource.Rent(runContinuationsAsynchronously: true);
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completingThread = Environment.CurrentManagedThreadId;
        _ = ObserveThreadAsync(source.Task, completed);

        source.SetResult();
        var continuationThread = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(completingThread, continuationThread);
    }

    [Fact]
    public async Task Continuation_exception_is_routed_to_global_handler_without_breaking_set_result()
    {
        var previous = YuoTaskExceptionHandler.UnhandledException;
        var captured = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("continuation");
        YuoTaskExceptionHandler.UnhandledException = captured.SetResult;

        try
        {
            var source = YuoTaskCompletionSource.Rent();
            var task = source.Task;
            task.GetAwaiter().OnCompleted(() => throw expected);

            source.SetResult();
            await task;

            var actual = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(expected, actual);
        }
        finally
        {
            YuoTaskExceptionHandler.UnhandledException = previous;
        }
    }

    [Fact]
    public async Task Asynchronous_continuation_exception_is_routed_to_global_handler()
    {
        var previous = YuoTaskExceptionHandler.UnhandledException;
        var captured = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("async-continuation");
        YuoTaskExceptionHandler.UnhandledException = captured.SetResult;

        try
        {
            var source = YuoTaskCompletionSource<int>.Rent(runContinuationsAsynchronously: true);
            var task = source.Task;
            task.GetAwaiter().UnsafeOnCompleted(() => throw expected);

            source.SetResult(7);
            Assert.Equal(7, await task);

            var actual = await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(expected, actual);
        }
        finally
        {
            YuoTaskExceptionHandler.UnhandledException = previous;
        }
    }

    [Fact]
    public async Task Concurrent_sources_complete_with_their_own_results()
    {
        const int count = 512;
        var tasks = Enumerable.Range(0, count).Select(static async i =>
        {
            var source = YuoTaskCompletionSource<int>.Rent();
            var task = source.Task;
            source.SetResult(i);
            return await task;
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(Enumerable.Range(0, count), results.Order());
    }

    [Fact]
    public async Task Try_set_result_reports_duplicate_completion()
    {
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;

        Assert.True(source.TrySetResult(1));
        Assert.False(source.TrySetResult(2));
        Assert.Equal(1, await task);
    }

    [Fact]
    public async Task Try_set_exception_reports_duplicate_completion()
    {
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;
        var expected = new InvalidOperationException("try");

        Assert.True(source.TrySetException(expected));
        Assert.False(source.TrySetException(new InvalidOperationException("ignored")));
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Set_canceled_propagates_operation_canceled_exception()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;

        source.SetCanceled(cancellationTokenSource.Token);
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);

        Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task From_canceled_propagates_operation_canceled_exception()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await YuoTask.FromCanceled());
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await YuoTask<int>.FromCanceled());
    }

    [Fact]
    public async Task Wait_all_completes_all_tasks()
    {
        var first = YuoTaskCompletionSource.Rent();
        var second = YuoTaskCompletionSource.Rent();
        var wait = YuoTask.WaitAll(first.Task, second.Task);

        Assert.False(wait.IsCompleted);
        first.SetResult();
        Assert.False(wait.IsCompleted);
        second.SetResult();

        await wait;
    }

    [Fact]
    public async Task Wait_all_generic_returns_results()
    {
        var results = await YuoTask.WaitAll(YuoTask<int>.FromResult(1), YuoTask<int>.FromResult(2));

        Assert.Equal([1, 2], results);
    }

    [Fact]
    public async Task Wait_all_propagates_first_exception_after_consuming_tasks()
    {
        var expected = new InvalidOperationException("wait-all");
        var successful = YuoTaskCompletionSource<int>.Rent();
        var failed = YuoTaskCompletionSource<int>.Rent();
        successful.SetResult(1);
        failed.SetException(expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () => await YuoTask.WaitAll(successful.Task, failed.Task));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Wait_any_returns_first_completed_index()
    {
        var first = YuoTaskCompletionSource<int>.Rent();
        var second = YuoTaskCompletionSource<int>.Rent();
        var wait = YuoTask.WaitAny(first.Task, second.Task);

        second.SetResult(2);
        var index = await wait;
        first.SetResult(1);

        Assert.Equal(1, index);
    }

    [Fact]
    public async Task Wait_any_late_completion_does_not_complete_unrelated_rented_source()
    {
        var first = YuoTaskCompletionSource<int>.Rent();
        var second = YuoTaskCompletionSource<int>.Rent();
        var wait = YuoTask.WaitAny(first.Task, second.Task);

        first.SetResult(1);
        Assert.Equal(0, await wait);

        var unrelated = YuoTaskCompletionSource<int>.Rent();
        var unrelatedTask = unrelated.Task;

        second.SetResult(2);
        await Task.Delay(50);

        Assert.False(unrelatedTask.IsCompleted);
        unrelated.SetResult(42);
        Assert.Equal(42, await unrelatedTask);
    }

    [Fact]
    public async Task From_task_and_as_task_bridge_standard_tasks()
    {
        await YuoTask.FromTask(Task.CompletedTask);
        Assert.Equal(42, await YuoTask<int>.FromTask(Task.FromResult(42)));
        await YuoTask.CompletedTask.AsTask();
        Assert.Equal(7, await YuoTask<int>.FromResult(7).AsTask());
    }

    [Fact]
    public async Task Forget_can_use_local_exception_handler()
    {
        var previous = YuoTaskExceptionHandler.UnhandledException;
        var local = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var global = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("local");
        YuoTaskExceptionHandler.UnhandledException = global.SetResult;

        try
        {
            YuoTask.FromException(expected).Forget(local.SetResult);
            var actual = await local.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(expected, actual);
            Assert.False(global.Task.IsCompleted);
        }
        finally
        {
            YuoTaskExceptionHandler.UnhandledException = previous;
        }
    }

    // 以下用例刻意直接调用 GetResult 验证同步取结果的池化边界（await 未完成任务会挂起而非抛错），
    // 非真实 await 路径，抑制 xUnit1031 阻塞告警。
#pragma warning disable xUnit1031
    [Fact]
    public void Pending_task_getresult_does_not_return_source_to_pool()
    {
        // 未完成取结果属于误用，但完成方仍持有该 source；
        // 若此时还池，完成方随后的 SetResult 会写穿复用后的新租户。
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;

        Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());

        var next = YuoTaskCompletionSource<int>.Rent();
        Assert.NotSame(source, next);

        source.SetResult(42);
        next.SetResult(7);
        Assert.Equal(7, next.Task.GetAwaiter().GetResult());
    }

    [Fact]
    public void Pending_nongeneric_task_getresult_does_not_return_source_to_pool()
    {
        var source = YuoTaskCompletionSource.Rent();
        var task = source.Task;

        Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());

        var next = YuoTaskCompletionSource.Rent();
        Assert.NotSame(source, next);

        source.SetResult();
    }

    [Fact]
    public void Consumed_completion_source_returns_to_pool()
    {
        var source = YuoTaskCompletionSource<int>.Rent();
        var task = source.Task;

        source.SetResult(3);
        Assert.Equal(3, task.GetAwaiter().GetResult());

        var next = YuoTaskCompletionSource<int>.Rent();
        Assert.Same(source, next);

        next.SetResult(9);
        Assert.Equal(9, next.Task.GetAwaiter().GetResult());
    }
#pragma warning restore xUnit1031

    private static async YuoTask CompleteSynchronouslyAsync()
    {
        await YuoTask.CompletedTask;
    }

    private static async YuoTask<int> ReturnSynchronouslyAsync()
    {
        await YuoTask.CompletedTask;
        return 42;
    }

    private static async YuoTask<int> AwaitAndAddAsync(YuoTask<int> task, int value)
    {
        return await task + value;
    }

    private static async YuoTask ObserveAsync(YuoTask task, Action continuation)
    {
        await task;
        continuation();
    }

    private static async YuoTask ObserveThreadAsync(YuoTask task, TaskCompletionSource<int> completed)
    {
        await task;
        completed.SetResult(Environment.CurrentManagedThreadId);
    }

    private static async YuoTask ThrowAsync()
    {
        await YuoTask.CompletedTask;
        throw new InvalidOperationException("builder");
    }
}
