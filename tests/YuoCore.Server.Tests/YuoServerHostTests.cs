using YuoCore.Actor;
using YuoCore.Config;
using YuoCore.Ecs;
using YuoCore.Location;
using YuoCore.Server;

namespace YuoCore.Server.Tests;

public sealed class YuoServerHostTests
{
    [Fact]
    public async Task HostStartsConfiguredFibersAndTicksThem()
    {
        var config = new YuoStartConfig
        {
            NodeId = "local",
            ProcessId = "test",
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "GameShard-0", SetWorldAsDefault = true },
                new YuoFiberConfig { Id = 2, Name = "GameShard-1" }
            ]
        };

        await using var host = new YuoServerHost(config);
        host.Start();
        await host.TickAsync();

        Assert.Equal(2, host.Fibers.Count);
        Assert.All(host.Fibers, fiber => Assert.True(fiber.IsStarted));
    }

    [Fact]
    public async Task HostRegistersLocalEntityActorAndBusinessLocation()
    {
        var config = new YuoStartConfig
        {
            NodeId = "node-a",
            ProcessId = "game-a",
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "GameShard-0", SetWorldAsDefault = true }
            ]
        };

        await using var host = new YuoServerHost(config);
        host.ActorHandlers.Add(new AddServerCounterHandler());
        host.Start();

        var fiber = host.Fibers[0];
        var actorId = new ActorId(9001);
        var entity = new YuoEntity(fiber.World);
        entity.AddComponent<ServerCounterComponent>();
        var locationKey = new YuoLocationKey("Player", "9001");
        await host.RegisterLocalEntityActorAsync(actorId, entity, locationKey);

        var responseTask = host.ActorRouter.InvokeAsync<int>(new AddServerCounter(actorId, 6)).AsTask();
        await host.TickAsync();
        var value = await responseTask;

        var location = await host.Location.ResolveAsync(locationKey);
        Assert.Equal(6, value);
        Assert.NotNull(location);
        Assert.Equal("game-a", location!.Address.ProcessId);
        Assert.Equal(actorId.Value, location.Address.ActorId);
    }

    [Fact]
    public async Task HostScansEntityActorHandlersBeforeStart()
    {
        var config = new YuoStartConfig
        {
            NodeId = "node-a",
            ProcessId = "game-a",
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "GameShard-0", SetWorldAsDefault = true }
            ]
        };

        await using var host = new YuoServerHost(config);
        var report = host.AddActorHandlersFromAssembly(typeof(YuoServerHostTests).Assembly);
        host.Start();

        var fiber = host.Fibers[0];
        var actorId = new ActorId(9003);
        var entity = new YuoEntity(fiber.World);
        entity.AddComponent<ServerCounterComponent>();
        await host.RegisterLocalEntityActorAsync(actorId, entity);

        var responseTask = host.ActorRouter.InvokeAsync<int>(new AddServerCounter(actorId, 9)).AsTask();
        await host.TickAsync();
        var value = await responseTask;

        Assert.True(report.RegisteredHandlers >= 1);
        Assert.Contains(report, host.ActorHandlerScanReports);
        Assert.Equal(9, value);
    }

    [Fact]
    public void HostRejectsActorHandlerScanAfterStart()
    {
        var config = new YuoStartConfig
        {
            NodeId = "node-a",
            ProcessId = "game-a",
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "GameShard-0", SetWorldAsDefault = true }
            ]
        };

        using var host = new YuoServerHost(config);
        host.Start();

        Assert.Throws<InvalidOperationException>(() =>
            host.AddActorHandlersFromAssembly(typeof(YuoServerHostTests).Assembly));
        Assert.Throws<InvalidOperationException>(() =>
            host.AddActorHandlersFromAssemblies([typeof(YuoServerHostTests).Assembly]));
    }

    [Fact]
    public async Task HostRollsBackLocalActorRegistrationWhenBusinessLocationFails()
    {
        var config = new YuoStartConfig
        {
            NodeId = "node-a",
            ProcessId = "game-a",
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "GameShard-0", SetWorldAsDefault = true }
            ]
        };
        var locations = new InMemoryYuoLocationRegistry();
        var locationKey = new YuoLocationKey("Player", "locked");
        await locations.RegisterAsync(
            locationKey,
            new YuoActorAddress("node-a", "old-game", 99, 9901));
        await locations.LockAsync(locationKey, "migration");

        await using var host = new YuoServerHost(config, locations);
        host.Start();
        var fiber = host.Fibers[0];
        var actorId = new ActorId(9002);
        var entity = new YuoEntity(fiber.World);
        entity.AddComponent<ServerCounterComponent>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.RegisterLocalEntityActorAsync(actorId, entity, locationKey));

        Assert.Contains("locked", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(host.ActorEntities.TryResolve(actorId, out _));
        Assert.Null(await host.ActorLocations.ResolveAsync(actorId));
        var actorException = await Assert.ThrowsAsync<ActorException>(async () =>
            await host.ActorRouter.InvokeAsync<int>(new AddServerCounter(actorId, 1)));
        Assert.Equal(404, actorException.Error.Code);
    }

    [Fact]
    public async Task DispatcherRejectsEntityOwnedByOtherFiberWorld()
    {
        var config = new YuoStartConfig
        {
            NodeId = "node-a",
            ProcessId = "game-a",
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "GameShard-0", SetWorldAsDefault = true },
                new YuoFiberConfig { Id = 2, Name = "GameShard-1" }
            ]
        };

        await using var host = new YuoServerHost(config);
        host.ActorHandlers.Add(new AddServerCounterHandler());
        host.Start();

        // 实体属于 fiber2 的 World，registry 是宿主级共享表；
        // fiber1 的 dispatcher 必须拒绝，防止跨 Fiber 直接改 ECS 状态。
        var foreignEntity = new YuoEntity(host.Fibers[1].World);
        foreignEntity.AddComponent<ServerCounterComponent>();
        var actorId = new ActorId(9010);
        host.ActorEntities.Register(actorId, foreignEntity);

        var dispatcher = new YuoEntityActorDispatcher(host.ActorEntities, host.ActorHandlers, host.Fibers[0].World);
        var ex = await Assert.ThrowsAsync<ActorException>(async () =>
            await dispatcher.DispatchAsync(
                ActorEnvelope.ForRequest(new AddServerCounter(actorId, 1)), default));
        Assert.Equal(404, ex.Error.Code);
    }

    [Fact]
    public async Task UnregisterLocalEntityActorRemovesAllRegistrations()
    {
        var config = new YuoStartConfig
        {
            NodeId = "node-a",
            ProcessId = "game-a",
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "GameShard-0", SetWorldAsDefault = true }
            ]
        };

        await using var host = new YuoServerHost(config);
        host.ActorHandlers.Add(new AddServerCounterHandler());
        host.Start();

        var actorId = new ActorId(9011);
        var entity = new YuoEntity(host.Fibers[0].World);
        entity.AddComponent<ServerCounterComponent>();
        var locationKey = new YuoLocationKey("Player", "9011");
        var address = await host.RegisterLocalEntityActorAsync(actorId, entity, locationKey);

        await host.UnregisterLocalEntityActorAsync(actorId);

        Assert.False(host.ActorEntities.TryResolve(actorId, out _));
        Assert.Null(await host.ActorLocations.ResolveAsync(actorId));
        Assert.Null(await host.Location.ResolveAsync(locationKey));
        var ex = await Assert.ThrowsAsync<ActorException>(async () =>
            await host.ActorTransport.InvokeAsync(address, ActorEnvelope.ForRequest(new AddServerCounter(actorId, 1))));
        Assert.Equal(404, ex.Error.Code);
    }

    private sealed record AddServerCounter(ActorId TargetActorId, int Value) : IActorRequest<int>;

    private sealed class ServerCounterComponent : YuoComponent
    {
        public int Value;
    }

    private sealed class AddServerCounterHandler : YuoActorRequestHandler<AddServerCounter, int>
    {
        protected override ValueTask<int> RunAsync(
            YuoEntity entity,
            AddServerCounter request,
            CancellationToken cancellationToken)
        {
            var counter = entity.GetComponent<ServerCounterComponent>();
            counter.Value += request.Value;
            return ValueTask.FromResult(counter.Value);
        }
    }
}
