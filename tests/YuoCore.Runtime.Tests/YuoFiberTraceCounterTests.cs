using System.Collections.Concurrent;
using System.Diagnostics;
using YuoCore.Ecs;
using YuoCore.Runtime;

namespace YuoCore.Runtime.Tests;

public sealed class YuoFiberTraceCounterTests
{
    private static readonly AsyncLocal<bool> Recording = new();

    [Fact]
    public async Task Full_mailbox_counts_one_logical_enqueue_per_post()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 5001,
            MaxMailboxCapacity = 1,
            MaxMailboxMessagesPerTick = 1
        });
        var previousBegin = YuoCoreTrace.BeginHandler;
        var previousEnd = YuoCoreTrace.EndHandler;
        var previousCount = YuoCoreTrace.CountHandler;
        var counts = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        var timings = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        try
        {
            YuoCoreTrace.Configure(
                _ => Recording.Value ? Stopwatch.GetTimestamp() : 0,
                (name, _) =>
                {
                    if (Recording.Value) timings.AddOrUpdate(name, 1, (_, previous) => previous + 1);
                },
                (name, count) =>
                {
                    if (Recording.Value) counts.AddOrUpdate(name, count, (_, previous) => previous + count);
                });
            Recording.Value = true;

            await fiber.PostAsync(_ => { });
            var blocked = fiber.PostAsync(_ => { }).AsTask();
            Assert.False(blocked.IsCompleted);

            await fiber.TickAsync();
            await blocked;

            Assert.Equal(2, counts.GetValueOrDefault("yuocore.fiber.enqueue.count"));
            Assert.Equal(2, timings.GetValueOrDefault("yuocore.fiber.enqueue"));
            Assert.Equal(1, counts.GetValueOrDefault("yuocore.fiber.enqueue.try_write.success"));
            Assert.Equal(1, counts.GetValueOrDefault("yuocore.fiber.enqueue.try_write.fallback"));
            Assert.Equal(1, counts.GetValueOrDefault("yuocore.fiber.enqueue.write.success"));
        }
        finally
        {
            Recording.Value = false;
            YuoCoreTrace.Configure(previousBegin, previousEnd, previousCount);
        }
    }
}
