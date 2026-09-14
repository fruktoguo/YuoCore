using YuoCore.Abstractions;
using YuoCore.Timers;

namespace YuoCore.Runtime.Timers;

/// <summary>
/// Fiber 与手动 TimerScheduler 的可选桥接。
/// Runtime 基础包不强依赖 Timers；需要 Fiber 内计时器的服务器 shard 或 Unity 模拟环境再引入本包。
/// </summary>
public sealed class YuoFiberTimerHost
{
    private readonly IYuoClock _clock;

    public YuoFiberTimerHost(
        YuoFiber fiber,
        IYuoClock? clock = null,
        YuoTimerSchedulerOptions? timerOptions = null)
    {
        Fiber = fiber ?? throw new ArgumentNullException(nameof(fiber));
        _clock = clock ?? new SystemYuoClock();
        Timers = new YuoTimerScheduler(_clock, timerOptions);
    }

    public YuoFiber Fiber { get; }

    public YuoTimerScheduler Timers { get; }

    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ThrowIfInsideOwningFiber();
        return Timers.DelayAsync(delay, cancellationToken);
    }

    public ValueTask WaitUntilAsync(DateTimeOffset dueAt, CancellationToken cancellationToken = default)
    {
        ThrowIfInsideOwningFiber();
        return Timers.WaitUntilAsync(dueAt, cancellationToken);
    }

    // Fiber 执行上下文内 await 本 Fiber 驱动的 Timer 必然死锁：
    // 完成 Timer 需要宿主再次 TickAsync，而 TickAsync 正被本动作占住。
    private void ThrowIfInsideOwningFiber()
    {
        if (ReferenceEquals(YuoFiber.Current, Fiber))
        {
            throw new InvalidOperationException(
                $"YuoFiber '{Fiber.Name}': awaiting a timer driven by the executing fiber would deadlock. Schedule a callback or await an external clock instead.");
        }
    }

    public YuoTimerId ScheduleOnce(
        TimeSpan delay,
        Func<YuoFiber, CancellationToken, ValueTask> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Timers.ScheduleOnce(delay, token => callback(Fiber, token), cancellationToken);
    }

    public YuoTimerId ScheduleRepeated(
        TimeSpan interval,
        Func<YuoFiber, CancellationToken, ValueTask> callback,
        bool fireImmediately = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return Timers.ScheduleRepeated(interval, token => callback(Fiber, token), fireImmediately, cancellationToken);
    }

    public async ValueTask TickAsync(CancellationToken cancellationToken = default)
    {
        await TickAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask TickAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Timer 先于 Fiber.Tick 执行。timer 回调如果 Post 到 Fiber mailbox，可以在同一帧被消费。
        await Timers.UpdateAsync(now, cancellationToken).ConfigureAwait(false);
        await Fiber.TickAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask FixedTickAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await Timers.UpdateAsync(now, cancellationToken).ConfigureAwait(false);
        await Fiber.FixedTickAsync(cancellationToken).ConfigureAwait(false);
    }
}
