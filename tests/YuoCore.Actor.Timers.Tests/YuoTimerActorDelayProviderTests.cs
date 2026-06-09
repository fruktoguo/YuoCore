using YuoCore.Abstractions;
using YuoCore.Actor.Timers;
using YuoCore.Timers;

namespace YuoCore.Actor.Timers.Tests;

public sealed class YuoTimerActorDelayProviderTests
{
    [Fact]
    public async Task DelayAsync_completes_when_scheduler_reaches_due_time()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        var provider = new YuoTimerActorDelayProvider(scheduler);

        var wait = provider.DelayAsync(TimeSpan.FromMilliseconds(100)).AsTask();

        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(99));
        Assert.False(wait.IsCompleted);

        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(100));
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DelayAsync_honors_cancellation()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        var provider = new YuoTimerActorDelayProvider(scheduler);
        using var cts = new CancellationTokenSource();

        var wait = provider.DelayAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => wait);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task DelayAsync_returns_immediately_for_non_positive_delay()
    {
        var scheduler = new YuoTimerScheduler();
        var provider = new YuoTimerActorDelayProvider(scheduler);

        await provider.DelayAsync(TimeSpan.Zero);

        Assert.Equal(0, scheduler.Count);
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : IYuoClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
