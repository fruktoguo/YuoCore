using System.Collections.Concurrent;

namespace YuoCore.Actor.Tests;

/// <summary>
/// 回归：fire-and-forget Actor 消息的处理异常必须经 onUnhandledError 出口可见，
/// 不能被静默吞掉。
/// </summary>
public sealed class ActorFiberErrorHandlingTests
{
    [Fact]
    public async Task SendFailureReachesUnhandledErrorCallback()
    {
        var errors = new ConcurrentQueue<(ActorEnvelope Envelope, Exception Error)>();
        var fiber = new ActorFiber(
            7,
            new DelegateActorMessageDispatcher((_, _) =>
                throw new InvalidOperationException("expected dispatch failure.")),
            onUnhandledError: (envelope, error) => errors.Enqueue((envelope, error)));
        var envelope = ActorEnvelope.ForMessage(new Ping(new ActorId(7_001)));
        await using (fiber)
        {
            fiber.Start();
            await fiber.SendAsync(envelope);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (errors.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }

        Assert.True(errors.TryDequeue(out var failure));
        Assert.Same(envelope, failure.Envelope);
        Assert.IsType<InvalidOperationException>(failure.Error);
    }

    [Fact]
    public async Task SameFiberInvokeFailsFastInsteadOfDeadlocking()
    {
        string? marker = null;
        ActorFiber? self = null;
        var dispatcher = new DelegateActorMessageDispatcher((envelope, ct) =>
        {
            try
            {
                _ = self!.InvokeAsync(envelope, ct);
            }
            catch (InvalidOperationException)
            {
                marker = "guarded";
            }

            return ValueTask.FromResult<object?>(null);
        });
        self = new ActorFiber(9, dispatcher);
        await using (self)
        {
            self.Start();
            await self.SendAsync(ActorEnvelope.ForMessage(new Ping(new ActorId(9_001))));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (marker is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }

        Assert.Equal("guarded", marker);
    }

    [Fact]
    public async Task Start_after_dispose_throws_instead_of_restarting()
    {
        var fiber = new ActorFiber(
            11,
            new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(null)));
        await fiber.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => fiber.Start());
    }

    private sealed record Ping(ActorId TargetActorId) : IActorMessage;
}
