namespace YuoCore.Actor;

/// <summary>
/// Actor 内部重试等待的抽象。
/// 默认实现使用 Task.Delay；Fiber/Unity 场景可以注入基于 YuoCore.Timers 的实现，避免 Actor.Core 直接依赖 Timers。
/// </summary>
public interface IActorDelayProvider
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

public sealed class TaskActorDelayProvider : IActorDelayProvider
{
    public static TaskActorDelayProvider Instance { get; } = new();

    private TaskActorDelayProvider()
    {
    }

    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }
}
