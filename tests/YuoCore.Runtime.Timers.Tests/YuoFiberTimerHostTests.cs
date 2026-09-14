using YuoCore.Ecs;
using YuoCore.Abstractions;
using YuoCore.Runtime;
using YuoCore.Runtime.Timers;

namespace YuoCore.Runtime.Timers.Tests;

public sealed class YuoFiberTimerHostTests
{
    [Fact]
    public async Task Timer_callback_runs_before_fiber_world_update()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 101, Name = "timer-host" });
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var host = new YuoFiberTimerHost(fiber, clock);
        var counter = new YuoEntity(fiber.World).AddComponent<TimerCounterComponent>();

        host.ScheduleOnce(
            TimeSpan.FromMilliseconds(100),
            (_, _) =>
            {
                counter.Pending += 5;
                return ValueTask.CompletedTask;
            });

        await host.TickAsync(DateTimeOffset.UnixEpoch.AddMilliseconds(99));
        Assert.Equal(0, counter.Applied);

        await host.TickAsync(DateTimeOffset.UnixEpoch.AddMilliseconds(100));
        Assert.Equal(5, counter.Applied);
    }

    [Fact]
    public async Task Timer_callback_can_post_to_fiber_mailbox_for_same_tick()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 102, Name = "timer-mailbox" });
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var host = new YuoFiberTimerHost(fiber, clock);
        var counter = new YuoEntity(fiber.World).AddComponent<TimerCounterComponent>();

        host.ScheduleOnce(
            TimeSpan.Zero,
            async (targetFiber, token) =>
            {
                await targetFiber.PostAsync(_ => counter.Pending += 3, token);
            });

        await host.TickAsync(DateTimeOffset.UnixEpoch);

        Assert.Equal(3, counter.Applied);
    }

    public sealed class TimerCounterComponent : YuoComponent
    {
        public int Pending;
        public int Applied;
    }

    public sealed class TimerCounterSystem : YuoSystem<TimerCounterComponent>, IUpdate
    {
        public override void Run(TimerCounterComponent component)
        {
            component.Applied += component.Pending;
            component.Pending = 0;
        }
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : IYuoClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}

// 追加回归：Fiber 执行上下文内 await 本 Fiber 驱动的 Timer 必须快速失败而非死锁
public sealed class YuoFiberTimerDeadlockGuardTests
{
    [Fact]
    public async Task Awaiting_timer_inside_owning_fiber_fails_fast()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 105, Name = "timer-deadlock-guard" });
        var host = new YuoFiberTimerHost(fiber);
        InvalidOperationException? observed = null;

        await fiber.PostAsync(f =>
        {
            try
            {
                _ = host.DelayAsync(TimeSpan.FromSeconds(1));
            }
            catch (InvalidOperationException e)
            {
                observed = e;
            }
        });
        await fiber.TickAsync();

        Assert.NotNull(observed);
    }
}
