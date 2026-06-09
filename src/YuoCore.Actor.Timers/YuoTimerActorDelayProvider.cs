using YuoCore.Timers;

namespace YuoCore.Actor.Timers;

/// <summary>
/// 将 Actor 重试等待接到手动驱动的 TimerScheduler。
/// 该类型放在可选适配包中，避免 YuoCore.Actor 为了 LocationSender 重试而强制依赖 Timers。
/// </summary>
public sealed class YuoTimerActorDelayProvider(YuoTimerScheduler scheduler) : IActorDelayProvider
{
    private readonly YuoTimerScheduler _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));

    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        return _scheduler.DelayAsync(delay, cancellationToken);
    }
}
