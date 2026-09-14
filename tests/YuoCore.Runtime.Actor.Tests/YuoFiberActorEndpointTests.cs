using YuoCore.Actor;
using YuoCore.Ecs;
using YuoCore.Runtime;

namespace YuoCore.Runtime.Actor.Tests;

public sealed class YuoFiberActorEndpointTests
{
    [Fact]
    public void FiberActorEndpointRejectsNullDependencies()
    {
        using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 20, Name = "actor-endpoint-null" });
        var dispatcher = new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(null));

        Assert.Throws<ArgumentNullException>(() => new YuoFiberActorEndpoint(null!, dispatcher));
        Assert.Throws<ArgumentNullException>(() => new YuoFiberActorEndpoint(fiber, null!));
    }

    [Fact]
    public async Task FiberActorEndpointRejectsNullEnvelopeBeforeMailbox()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 23, Name = "actor-endpoint-null-envelope" });
        var endpoint = new YuoFiberActorEndpoint(
            fiber,
            new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(null)));

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await endpoint.SendAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await endpoint.InvokeAsync(null!));
    }

    [Fact]
    public async Task FiberActorEndpointDispatchesOnOwnedFiberMailboxBeforeTick()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 21, Name = "actor-endpoint" });
        var actorId = new ActorId(21_001);
        var entity = new YuoEntity(fiber.World);
        var counter = entity.AddComponent<TestCounterComponent>();

        var entities = new YuoEntityActorRegistry();
        entities.Register(actorId, entity);
        var handlers = new YuoActorHandlerRegistry().Add(new AddPendingHandler());
        var endpoint = new YuoFiberActorEndpoint(fiber, new YuoEntityActorDispatcher(entities, handlers, fiber.World));

        var responseTask = endpoint.InvokeAsync(ActorEnvelope.ForRequest(new AddPending(actorId, 7))).AsTask();
        await fiber.TickAsync();
        var response = await responseTask;

        Assert.Null(response.Error);
        Assert.Equal(7, counter.Applied);
    }

    [Fact]
    public async Task FiberActorEndpointSendExceptionDoesNotBreakMailboxDrain()
    {
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions { Id = 22, Name = "actor-send-exception" });
        var actorId = new ActorId(22_001);
        var applied = 0;
        var endpoint = new YuoFiberActorEndpoint(
            fiber,
            new DelegateActorMessageDispatcher((envelope, _) =>
            {
                switch (envelope.Message)
                {
                    case FailSend:
                        throw new InvalidOperationException("expected send failure.");
                    case AddPending add:
                        applied += add.Value;
                        return ValueTask.FromResult<object?>(applied);
                    default:
                        throw new InvalidOperationException("Unknown message.");
                }
            }));

        await endpoint.SendAsync(ActorEnvelope.ForMessage(new FailSend(actorId)));
        await endpoint.SendAsync(ActorEnvelope.ForMessage(new AddPending(actorId, 5)));

        await fiber.TickAsync();

        Assert.Equal(5, applied);
    }

    [Fact]
    public async Task FiberActorEndpointSendFailureReachesUnhandledException()
    {
        var errors = new List<Exception>();
        await using var runtime = new YuoCoreRuntime();
        var fiber = runtime.Fibers.CreateFiber(new YuoFiberOptions
        {
            Id = 24,
            Name = "actor-send-error-egress",
            UnhandledException = (_, e) => errors.Add(e)
        });
        var endpoint = new YuoFiberActorEndpoint(
            fiber,
            new DelegateActorMessageDispatcher((_, _) =>
                throw new InvalidOperationException("expected send failure.")));

        await endpoint.SendAsync(ActorEnvelope.ForMessage(new FailSend(new ActorId(24_001))));
        await fiber.TickAsync();

        Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(errors[0]);
    }

    private sealed class TestCounterComponent : YuoComponent
    {
        public int Pending;
        public int Applied;
        public int Updates;
    }

    private sealed class TestCounterSystem : YuoSystem<TestCounterComponent>, IUpdate
    {
        public override void Run(TestCounterComponent component)
        {
            component.Updates++;
            component.Applied += component.Pending;
            component.Pending = 0;
        }
    }

    private sealed record AddPending(ActorId TargetActorId, int Value) : IActorRequest<int>;

    private sealed record FailSend(ActorId TargetActorId) : IActorMessage;

    private sealed class AddPendingHandler : YuoActorRequestHandler<AddPending, int>
    {
        protected override ValueTask<int> RunAsync(
            YuoEntity entity,
            AddPending request,
            CancellationToken cancellationToken)
        {
            var counter = entity.GetComponent<TestCounterComponent>();
            counter.Pending += request.Value;
            return ValueTask.FromResult(counter.Pending);
        }
    }
}
