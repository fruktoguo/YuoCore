using YuoCore.Runtime;

namespace YuoCore.Runtime.Tests;

/// <summary>
/// 回归：fire-and-forget 消息异常必须经错误出口可见，且不得中断 Tick；
/// mailbox 支持可选容量上限提供背压。
/// </summary>
public sealed class YuoFiberErrorHandlingTests
{
    [Fact]
    public async Task PostFailureIsReportedAndDoesNotKillTick()
    {
        var errors = new List<Exception>();
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 41,
            Name = "error-egress",
            UnhandledException = (_, e) => errors.Add(e)
        });
        var ran = 0;

        await fiber.PostAsync(_ => throw new InvalidOperationException("expected failure."));
        await fiber.PostAsync(_ => ran++);
        await fiber.TickAsync();

        Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(errors[0]);
        Assert.Equal(1, ran);
        Assert.Equal(0, fiber.PendingMessageCount);
    }

    [Fact]
    public async Task BoundedMailboxAppliesBackpressure()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 42,
            Name = "bounded-mailbox",
            MaxMailboxCapacity = 1,
            MaxMailboxMessagesPerTick = 1
        });

        await fiber.PostAsync(_ => { });
        var blocked = fiber.PostAsync(_ => { }).AsTask();
        Assert.False(blocked.IsCompleted);
        // 阻塞中的 writer 也计入 pending，反映真实投递压力
        Assert.Equal(2, fiber.PendingMessageCount);

        await fiber.TickAsync();
        await blocked;
        Assert.Equal(1, fiber.PendingMessageCount);

        await fiber.TickAsync();
        Assert.Equal(0, fiber.PendingMessageCount);
    }
}
