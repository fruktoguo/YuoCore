using YuoCore.Abstractions;

namespace YuoCore.Timers.Tests;

public sealed class YuoTimerSchedulerTests
{
    [Fact]
    public async Task DelayAsync_completes_only_after_manual_update_reaches_due_time()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);

        var wait = scheduler.DelayAsync(TimeSpan.FromMilliseconds(100)).AsTask();

        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(99));
        Assert.False(wait.IsCompleted);

        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(100));
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task WaitUntilAsync_returns_immediately_for_past_time()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch.AddSeconds(10));
        var scheduler = new YuoTimerScheduler(clock);

        var wait = scheduler.WaitUntilAsync(DateTimeOffset.UnixEpoch);

        Assert.True(wait.IsCompletedSuccessfully);
        await wait;
    }

    [Fact]
    public async Task ScheduleRepeated_runs_once_per_elapsed_interval_and_can_be_removed()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        var count = 0;

        var timerId = scheduler.ScheduleRepeated(
            TimeSpan.FromMilliseconds(50),
            _ =>
            {
                count++;
                return ValueTask.CompletedTask;
            });

        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(50));
        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(100));
        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(150));

        Assert.Equal(3, count);
        Assert.True(scheduler.Remove(timerId));

        await scheduler.UpdateAsync(clock.UtcNow.AddSeconds(1));
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Relative_timer_uses_last_manual_update_time_as_base()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        var triggered = false;

        await scheduler.UpdateAsync(DateTimeOffset.UnixEpoch.AddSeconds(10));
        scheduler.ScheduleOnce(
            TimeSpan.FromMilliseconds(100),
            _ =>
            {
                triggered = true;
                return ValueTask.CompletedTask;
            });

        await scheduler.UpdateAsync(DateTimeOffset.UnixEpoch.AddSeconds(10).AddMilliseconds(99));
        Assert.False(triggered);

        await scheduler.UpdateAsync(DateTimeOffset.UnixEpoch.AddSeconds(10).AddMilliseconds(100));
        Assert.True(triggered);
    }

    [Fact]
    public async Task Cancellation_cancels_pending_delay()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        using var cts = new CancellationTokenSource();

        var wait = scheduler.DelayAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => wait);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task UpdateAsync_honors_cancellation_before_advancing_time()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await scheduler.UpdateAsync(clock.UtcNow.AddSeconds(1), cts.Token));

        Assert.Equal(DateTimeOffset.UnixEpoch, scheduler.CurrentTime);
    }

    [Fact]
    public async Task Callback_error_is_reported_and_does_not_block_following_timer_by_default()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        var completed = false;
        var failures = new List<Exception>();
        scheduler.CallbackFailed += (_, args) => failures.Add(args.Exception);

        scheduler.ScheduleOnce(TimeSpan.FromMilliseconds(10), _ => throw new InvalidOperationException("expected timer failure."));
        scheduler.ScheduleOnce(
            TimeSpan.FromMilliseconds(10),
            _ =>
            {
                completed = true;
                return ValueTask.CompletedTask;
            });

        await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(10));

        Assert.True(completed);
        Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failures[0]);
    }

    [Fact]
    public async Task Repeated_timer_is_removed_when_stop_on_callback_error_throws()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(
            clock,
            new YuoTimerSchedulerOptions(StopOnCallbackError: true));

        scheduler.ScheduleRepeated(
            TimeSpan.FromMilliseconds(10),
            _ => throw new InvalidOperationException("expected repeated failure."));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scheduler.UpdateAsync(clock.UtcNow.AddMilliseconds(10)));

        Assert.Equal("expected repeated failure.", exception.Message);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task Callback_cannot_await_future_delay_on_same_scheduler()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        var failures = new List<Exception>();
        scheduler.CallbackFailed += (_, args) => failures.Add(args.Exception);

        scheduler.ScheduleOnce(
            TimeSpan.Zero,
            async _ => await scheduler.DelayAsync(TimeSpan.FromMilliseconds(1)));

        await scheduler.UpdateAsync(clock.UtcNow);

        var failure = Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task MaxDueTimersPerUpdate_splits_large_due_batch()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(
            clock,
            new YuoTimerSchedulerOptions(MaxDueTimersPerUpdate: 2));
        var fired = 0;

        for (var i = 0; i < 5; i++)
        {
            scheduler.ScheduleOnce(
                TimeSpan.Zero,
                _ =>
                {
                    fired++;
                    return ValueTask.CompletedTask;
                });
        }

        await scheduler.UpdateAsync(clock.UtcNow);
        Assert.Equal(2, fired);
        Assert.Equal(3, scheduler.Count);

        await scheduler.UpdateAsync(clock.UtcNow);
        Assert.Equal(4, fired);
        Assert.Equal(1, scheduler.Count);

        await scheduler.UpdateAsync(clock.UtcNow);
        Assert.Equal(5, fired);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task Update_completes_due_batch_even_if_callback_cancels_update_token()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var scheduler = new YuoTimerScheduler(clock);
        using var cts = new CancellationTokenSource();
        var fired = 0;

        scheduler.ScheduleOnce(
            TimeSpan.Zero,
            _ =>
            {
                fired++;
                cts.Cancel();
                return ValueTask.CompletedTask;
            });
        scheduler.ScheduleOnce(
            TimeSpan.Zero,
            _ =>
            {
                fired++;
                return ValueTask.CompletedTask;
            });

        await scheduler.UpdateAsync(clock.UtcNow, cts.Token);

        Assert.Equal(2, fired);
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public void MaxDueTimersPerUpdate_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new YuoTimerScheduler(options: new YuoTimerSchedulerOptions(MaxDueTimersPerUpdate: 0)));
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : IYuoClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
