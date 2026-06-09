namespace YuoCore.Tasks;

public readonly struct YuoTaskWaitAnyResult
{
    public YuoTaskWaitAnyResult(int index)
    {
        Index = index;
    }

    public int Index { get; }
}

public static class YuoTaskCombinators
{
    public static YuoTask WaitAll(params YuoTask[] tasks) => WaitAll((IReadOnlyList<YuoTask>)tasks);

    public static async YuoTask WaitAll(IReadOnlyList<YuoTask> tasks)
    {
        if (tasks is null)
        {
            throw new ArgumentNullException(nameof(tasks));
        }

        Exception? firstException = null;
        for (var i = 0; i < tasks.Count; i++)
        {
            try
            {
                await tasks[i];
            }
            catch (Exception exception) when (firstException is null)
            {
                firstException = exception;
            }
            catch
            {
            }
        }

        if (firstException is not null)
        {
            throw firstException;
        }
    }

    public static YuoTask<T[]> WaitAll<T>(params YuoTask<T>[] tasks) => WaitAll((IReadOnlyList<YuoTask<T>>)tasks);

    public static async YuoTask<T[]> WaitAll<T>(IReadOnlyList<YuoTask<T>> tasks)
    {
        if (tasks is null)
        {
            throw new ArgumentNullException(nameof(tasks));
        }

        var results = new T[tasks.Count];
        Exception? firstException = null;
        for (var i = 0; i < tasks.Count; i++)
        {
            try
            {
                results[i] = await tasks[i];
            }
            catch (Exception exception) when (firstException is null)
            {
                firstException = exception;
            }
            catch
            {
            }
        }

        if (firstException is not null)
        {
            throw firstException;
        }

        return results;
    }

    public static YuoTask<int> WaitAny(params YuoTask[] tasks) => WaitAny((IReadOnlyList<YuoTask>)tasks);

    public static YuoTask<int> WaitAny(IReadOnlyList<YuoTask> tasks)
    {
        if (tasks is null)
        {
            throw new ArgumentNullException(nameof(tasks));
        }

        if (tasks.Count == 0)
        {
            throw new ArgumentException("WaitAny requires at least one task.", nameof(tasks));
        }

        return WaitAnyAsync(tasks);
    }

    public static YuoTask<int> WaitAny<T>(params YuoTask<T>[] tasks) => WaitAny((IReadOnlyList<YuoTask<T>>)tasks);

    public static YuoTask<int> WaitAny<T>(IReadOnlyList<YuoTask<T>> tasks)
    {
        if (tasks is null)
        {
            throw new ArgumentNullException(nameof(tasks));
        }

        if (tasks.Count == 0)
        {
            throw new ArgumentException("WaitAny requires at least one task.", nameof(tasks));
        }

        return WaitAnyAsync(tasks);
    }

    private static async YuoTask<int> WaitAnyAsync(IReadOnlyList<YuoTask> tasks)
    {
        // WaitAny 不能把池化 completion source 长期交给多个后台观察协程。
        // 第一个任务完成后 source 可能被归还池，较晚完成的观察协程再写入会污染后续复用者。
        var standardTasks = new Task[tasks.Count];
        for (var i = 0; i < tasks.Count; i++)
        {
            standardTasks[i] = tasks[i].AsTask();
        }

        var completed = await Task.WhenAny(standardTasks).ConfigureAwait(false);
        var index = Array.IndexOf(standardTasks, completed);
        await completed.ConfigureAwait(false);
        return index;
    }

    private static async YuoTask<int> WaitAnyAsync<T>(IReadOnlyList<YuoTask<T>> tasks)
    {
        // 同上：用标准 Task.WhenAny 管理观察生命周期，避免池化 source 被迟到观察者写穿。
        var standardTasks = new Task<T>[tasks.Count];
        for (var i = 0; i < tasks.Count; i++)
        {
            standardTasks[i] = tasks[i].AsTask();
        }

        var completed = await Task.WhenAny(standardTasks).ConfigureAwait(false);
        var index = Array.IndexOf(standardTasks, completed);
        _ = await completed.ConfigureAwait(false);
        return index;
    }
}
