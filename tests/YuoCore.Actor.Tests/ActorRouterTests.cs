using System.Reflection;
using YuoCore.Actor;
using YuoCore.Abstractions;
using YuoCore.Ecs;
using YuoCore.Location;

namespace YuoCore.Actor.Tests;

public sealed class ActorRouterTests
{
    [Fact]
    public async Task Router_sends_fire_and_forget_message_through_in_memory_fiber()
    {
        var actorId = new ActorId(1001);
        var address = new ActorAddress("local", 1, actorId);
        var location = new InMemoryActorLocationRegistry();
        var transport = new InMemoryActorTransport();
        var gold = 0L;

        await using var fiber = new ActorFiber(
            1,
            new DelegateActorMessageDispatcher((envelope, _) =>
            {
                if (envelope.Message is AddGold addGold)
                {
                    gold += addGold.Amount;
                    return ValueTask.FromResult<object?>(null);
                }

                return envelope.Message is GetGold
                    ? ValueTask.FromResult<object?>(gold)
                    : throw new InvalidOperationException("Unknown actor message.");
            }));

        transport.RegisterEndpoint(address, fiber);
        await location.RegisterAsync(actorId, address);
        fiber.Start();

        var router = new ActorRouter(location, transport);
        await router.SendAsync(new AddGold(actorId, 10));
        await router.InvokeAsync<long>(new GetGold(actorId));

        Assert.Equal(10, gold);
    }

    [Fact]
    public async Task Router_invokes_actor_request_and_returns_response()
    {
        var actorId = new ActorId(2001);
        var address = new ActorAddress("local", 2, actorId);
        var location = new InMemoryActorLocationRegistry();
        var transport = new InMemoryActorTransport();
        var gold = 25L;

        await using var fiber = new ActorFiber(
            2,
            new DelegateActorMessageDispatcher((envelope, _) =>
            {
                return envelope.Message switch
                {
                    AddGold addGold => Add(addGold.Amount),
                    GetGold => ValueTask.FromResult<object?>(gold),
                    _ => throw new InvalidOperationException("Unknown actor message.")
                };

                ValueTask<object?> Add(long amount)
                {
                    gold += amount;
                    return ValueTask.FromResult<object?>(null);
                }
            }));

        transport.RegisterEndpoint(address, fiber);
        await location.RegisterAsync(actorId, address);
        fiber.Start();

        var router = new ActorRouter(location, transport);

        await router.SendAsync(new AddGold(actorId, 17));
        var result = await router.InvokeAsync<long>(new GetGold(actorId));

        Assert.Equal(42, result);
    }

    [Fact]
    public void ActorEnvelopeKeepsRpcIdAcrossResponse()
    {
        var actorId = new ActorId(2501);
        var request = ActorEnvelope.ForRequest(new GetGold(actorId));
        var response = ActorEnvelope.Success(request, 1L);
        var failure = ActorEnvelope.Failure(request, ActorError.Timeout());

        Assert.True(request.RpcId > 0);
        Assert.Equal(request.RpcId, response.RpcId);
        Assert.Equal(request.RpcId, failure.RpcId);
    }

    [Fact]
    public void ActorEnvelopeFactoriesRejectNullInputs()
    {
        var actorId = new ActorId(2502);
        var request = ActorEnvelope.ForRequest(new GetGold(actorId));

        Assert.Throws<ArgumentNullException>(() => ActorEnvelope.ForMessage(null!));
        Assert.Throws<ArgumentNullException>(() => ActorEnvelope.ForRequest<long>(null!));
        Assert.Throws<ArgumentNullException>(() => ActorEnvelope.Success(null!, 1L));
        Assert.Throws<ArgumentNullException>(() => ActorEnvelope.Failure(request, null!));
    }

    [Fact]
    public void ActorErrorFactoriesUseStableBusinessCodes()
    {
        Assert.Equal(400, ActorError.BadRequest().Code);
        Assert.Equal(ActorErrorCodes.BadRequest, ActorError.BadRequest().ErrorCode);
        Assert.Equal(404, ActorError.NotFound().Code);
        Assert.Equal(ActorErrorCodes.NotFound, ActorError.NotFound().ErrorCode);
        Assert.Equal(408, ActorError.Timeout().Code);
        Assert.Equal(ActorErrorCodes.Timeout, ActorError.Timeout().ErrorCode);
        Assert.Equal(409, ActorError.Conflict().Code);
        Assert.Equal(ActorErrorCodes.Conflict, ActorError.Conflict().ErrorCode);
        Assert.Equal(500, ActorError.HandlerFailed(new InvalidOperationException("boom")).Code);
        Assert.Equal(ActorErrorCodes.HandlerFailed, ActorError.HandlerFailed(new InvalidOperationException("boom")).ErrorCode);

        var business = ActorError.Business("wallet.insufficient_gold", "Insufficient gold.");
        Assert.Equal(400, business.Code);
        Assert.Equal("wallet.insufficient_gold", business.ErrorCode);

        Assert.Throws<ArgumentException>(() => ActorError.Business("", "bad"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ActorError.Business("bad", "bad", code: 500));
    }

    [Fact]
    public void RouterRejectsNullDependencies()
    {
        var location = new InMemoryActorLocationRegistry();
        var transport = new InMemoryActorTransport();

        Assert.Throws<ArgumentNullException>(() => new ActorRouter(null!, transport));
        Assert.Throws<ArgumentNullException>(() => new ActorRouter(location, null!));
    }

    [Fact]
    public async Task DelegateActorMessageDispatcherRejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new DelegateActorMessageDispatcher(null!));

        var dispatcher = new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(null));

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await dispatcher.DispatchAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ActorFiberRejectsNullInputs()
    {
        var dispatcher = new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(null));

        Assert.Throws<ArgumentNullException>(() => new ActorFiber(1, null!));
        Assert.Throws<ArgumentNullException>(() => new ActorFiber(1, dispatcher, null!));

        await using var fiber = new ActorFiber(1, dispatcher);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await fiber.SendAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await fiber.InvokeAsync(null!));
    }

    [Fact]
    public async Task RouterRejectsNullMessageBeforeLocationResolve()
    {
        var location = new CountingActorLocationRegistry();
        var router = new ActorRouter(location, new InMemoryActorTransport());

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await router.SendAsync(null!));

        Assert.Equal(0, location.ResolveCount);
    }

    [Fact]
    public async Task RouterRejectsNullRequestBeforeLocationResolve()
    {
        var location = new CountingActorLocationRegistry();
        var router = new ActorRouter(location, new InMemoryActorTransport());

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await router.InvokeAsync<long>(null!));

        Assert.Equal(0, location.ResolveCount);
    }

    [Fact]
    public async Task Router_throws_actor_exception_when_location_is_missing()
    {
        var router = new ActorRouter(
            new InMemoryActorLocationRegistry(),
            new InMemoryActorTransport());

        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await router.SendAsync(new AddGold(new ActorId(404), 1)));

        Assert.Equal(404, exception.Error.Code);
    }

    [Fact]
    public async Task InMemoryActorLocationRegistry_honors_cancellation_before_mutation()
    {
        var registry = new InMemoryActorLocationRegistry();
        var actorId = new ActorId(4101);
        var address = new ActorAddress("local", 41, actorId);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await registry.RegisterAsync(actorId, address, cts.Token));

        Assert.Null(await registry.ResolveAsync(actorId));
    }

    [Fact]
    public void InMemoryActorTransport_rejects_null_endpoint()
    {
        var transport = new InMemoryActorTransport();
        var actorId = new ActorId(4103);
        var address = new ActorAddress("local", 43, actorId);

        Assert.Throws<ArgumentNullException>(() => transport.RegisterEndpoint(address, null!));
    }

    [Fact]
    public async Task InMemoryActorTransport_rejects_null_envelope_before_endpoint_lookup()
    {
        var transport = new InMemoryActorTransport();
        var actorId = new ActorId(4104);
        var address = new ActorAddress("local", 44, actorId);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await transport.SendAsync(address, null!));
    }

    [Fact]
    public async Task InMemoryActorTransport_honors_cancellation_before_endpoint_dispatch()
    {
        var actorId = new ActorId(4102);
        var address = new ActorAddress("local", 42, actorId);
        var endpoint = new RecordingActorEndpoint();
        var transport = new InMemoryActorTransport();
        transport.RegisterEndpoint(address, endpoint);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await transport.SendAsync(address, ActorEnvelope.ForMessage(new AddGold(actorId, 1)), cts.Token));

        Assert.Equal(0, endpoint.SendCount);
    }

    [Fact]
    public async Task EntityActorDispatcher_routes_request_to_target_entity_handler()
    {
        var world = new YuoWorld();
        world.OnInit(false);

        try
        {
            var actorId = new ActorId(3001);
            var address = new ActorAddress("local", 3, actorId);
            var entity = new YuoEntity(world);
            entity.AddComponent<WalletComponent>().Gold = 10;

            var entities = new YuoEntityActorRegistry();
            entities.Register(actorId, entity);

            var handlers = new YuoActorHandlerRegistry()
                .Add(new AddWalletGoldHandler());

            var location = new InMemoryActorLocationRegistry();
            var transport = new InMemoryActorTransport();
            await using var fiber = new ActorFiber(3, new YuoEntityActorDispatcher(entities, handlers));

            transport.RegisterEndpoint(address, fiber);
            await location.RegisterAsync(actorId, address);
            fiber.Start();

            var router = new ActorRouter(location, transport);
            var response = await router.InvokeAsync<WalletGoldChanged>(new AddWalletGold(actorId, 32));

            Assert.Equal(42, response.Gold);
            Assert.Equal(42, entity.GetComponent<WalletComponent>().Gold);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public async Task EntityActorDispatcherPreservesBusinessActorError()
    {
        var world = new YuoWorld();
        world.OnInit(false);

        try
        {
            var actorId = new ActorId(3004);
            var address = new ActorAddress("local", 4, actorId);
            var entity = new YuoEntity(world);
            entity.AddComponent<WalletComponent>().Gold = 10;

            var entities = new YuoEntityActorRegistry();
            entities.Register(actorId, entity);

            var handlers = new YuoActorHandlerRegistry()
                .Add(new SpendWalletGoldHandler());

            var location = new InMemoryActorLocationRegistry();
            var transport = new InMemoryActorTransport();
            await using var fiber = new ActorFiber(4, new YuoEntityActorDispatcher(entities, handlers));

            transport.RegisterEndpoint(address, fiber);
            await location.RegisterAsync(actorId, address);
            fiber.Start();

            var router = new ActorRouter(location, transport);
            var exception = await Assert.ThrowsAsync<ActorException>(async () =>
                await router.InvokeAsync<WalletGoldChanged>(new SpendWalletGold(actorId, 99)));

            Assert.Equal(400, exception.Error.Code);
            Assert.Equal("wallet.insufficient_gold", exception.Error.ErrorCode);
            Assert.Equal(10, entity.GetComponent<WalletComponent>().Gold);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public async Task EntityActorDispatcherRejectsNullInputs()
    {
        var entities = new YuoEntityActorRegistry();
        var handlers = new YuoActorHandlerRegistry();

        Assert.Throws<ArgumentNullException>(() => new YuoEntityActorDispatcher(null!, handlers));
        Assert.Throws<ArgumentNullException>(() => new YuoEntityActorDispatcher(entities, null!));

        var dispatcher = new YuoEntityActorDispatcher(entities, handlers);
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await dispatcher.DispatchAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task EntityActorHandlersRejectNullInputs()
    {
        var handler = new AddWalletGoldHandler();
        var world = new YuoWorld();
        world.OnInit(false);

        try
        {
            var entity = new YuoEntity(world);

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await handler.HandleAsync(null!, new AddWalletGold(new ActorId(3002), 1), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await handler.HandleAsync(entity, null!, CancellationToken.None));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void ActorHandlerRegistryRejectsNullReflectionInputs()
    {
        var registry = new YuoActorHandlerRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.AddFromAssembly(null!));
        Assert.Throws<ArgumentNullException>(() => registry.AddFromAssembly(typeof(ActorRouterTests).Assembly, null!));
        Assert.Throws<ArgumentNullException>(() => registry.AddFromAssemblies((Assembly[])null!));
        Assert.Throws<ArgumentNullException>(() => registry.AddFromAssemblies((IEnumerable<Assembly>)null!));
        Assert.Throws<ArgumentNullException>(() => registry.AddFromAssembliesWithReport(null!));
        Assert.Throws<ArgumentNullException>(() => registry.AddFromAssemblyWithReport(null!));
        Assert.Throws<ArgumentNullException>(() => registry.AddFromAssemblyWithReport(typeof(ActorRouterTests).Assembly, null!));
        Assert.Throws<ArgumentNullException>(() => registry.TryGet(null!, out _));
    }

    [Fact]
    public void ActorHandlerRegistryReportsParameterlessScanResults()
    {
        var registry = new YuoActorHandlerRegistry();

        var report = registry.AddFromAssemblyWithReport(typeof(ActorRouterTests).Assembly);

        Assert.True(report.CandidateHandlerTypes >= 2);
        Assert.True(report.RegisteredHandlers >= 1);
        Assert.Contains(typeof(FactoryConstructedAddGoldHandler), report.SkippedHandlerTypes);
        Assert.True(registry.TryGet(typeof(AddWalletGold), out _));
    }

    [Fact]
    public void ActorHandlerRegistryRejectsInvalidCustomHandlerContracts()
    {
        var registry = new YuoActorHandlerRegistry();

        var nonActorMessage = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new NonActorMessageHandler(0)));
        var responseOnMessage = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new MessageWithResponseHandler(0)));
        var missingResponse = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new RequestWithoutResponseHandler(0)));
        var wrongResponse = Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new RequestWrongResponseHandler(0)));

        Assert.Contains("must implement", nonActorMessage.Message);
        Assert.Contains("is not an actor request", responseOnMessage.Message);
        Assert.Contains("must declare response type", missingResponse.Message);
        Assert.Contains("response type mismatch", wrongResponse.Message);
    }

    [Fact]
    public void ActorHandlerRegistryAcceptsValidCustomHandlerContract()
    {
        var registry = new YuoActorHandlerRegistry();

        registry.Add(new CustomGetGoldHandler(0));

        Assert.True(registry.TryGet(typeof(GetGold), out var handler));
        Assert.Equal(typeof(long), handler.ResponseType);
    }

    [Fact]
    public void ActorHandlerRegistryScansMultipleAssembliesWithReport()
    {
        var registry = new YuoActorHandlerRegistry();
        var counter = new HandlerCounter();

        var reports = registry.AddFromAssembliesWithReport(
            [typeof(ActorRouterTests).Assembly],
            type => type == typeof(FactoryConstructedAddGoldHandler)
                ? new FactoryConstructedAddGoldHandler(counter)
                : null);

        var report = Assert.Single(reports);
        Assert.True(report.CandidateHandlerTypes >= 2);
        Assert.Equal(1, report.RegisteredHandlers);
        Assert.Contains(typeof(AddWalletGoldHandler), report.SkippedHandlerTypes);
        Assert.True(registry.TryGet(typeof(FactoryConstructedAddGold), out _));
    }

    [Fact]
    public async Task ActorHandlerRegistryScansAssemblyWithFactoryConstructedHandlers()
    {
        var counter = new HandlerCounter();
        var registry = new YuoActorHandlerRegistry()
            .AddFromAssembly(
                typeof(ActorRouterTests).Assembly,
                type => type == typeof(FactoryConstructedAddGoldHandler)
                    ? new FactoryConstructedAddGoldHandler(counter)
                    : null);

        Assert.True(registry.TryGet(typeof(FactoryConstructedAddGold), out var handler));

        var world = new YuoWorld();
        world.OnInit(false);
        try
        {
            var entity = new YuoEntity(world);
            await handler.HandleAsync(entity, new FactoryConstructedAddGold(new ActorId(1), 7), CancellationToken.None);

            Assert.Equal(7, counter.Value);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public async Task Router_converts_invoke_timeout_to_actor_exception()
    {
        var actorId = new ActorId(4001);
        var address = new ActorAddress("local", 4, actorId);
        var location = new InMemoryActorLocationRegistry();
        var transport = new InMemoryActorTransport();

        await using var fiber = new ActorFiber(
            4,
            new DelegateActorMessageDispatcher(async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return 1;
            }));

        transport.RegisterEndpoint(address, fiber);
        await location.RegisterAsync(actorId, address);
        fiber.Start();

        var router = new ActorRouter(location, transport);
        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await router.InvokeAsync<int>(new SlowRequest(actorId), TimeSpan.FromMilliseconds(20)));

        Assert.Equal(408, exception.Error.Code);
    }

    [Fact]
    public async Task Router_invoke_timeout_covers_location_resolve()
    {
        var actorId = new ActorId(4002);
        var location = new SlowActorLocationRegistry(new ActorAddress("local", 4, actorId));
        var router = new ActorRouter(location, new InMemoryActorTransport());

        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await router.InvokeAsync<int>(new SlowRequest(actorId), TimeSpan.FromMilliseconds(20)));

        Assert.Equal(408, exception.Error.Code);
        Assert.Equal(1, location.ResolveCount);
    }

    [Fact]
    public async Task Router_invoke_timeout_covers_stale_retry_resolve()
    {
        var actorId = new ActorId(4003);
        var staleAddress = new ActorAddress("local", 40, actorId);
        var currentAddress = new ActorAddress("local", 41, actorId);
        var location = new SlowSecondResolveActorLocationRegistry(staleAddress, currentAddress);
        var transport = new InMemoryActorTransport();
        transport.RegisterEndpoint(staleAddress, new NotFoundActorEndpoint());
        var router = new ActorRouter(location, transport);

        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await router.InvokeAsync<int>(new SlowRequest(actorId), TimeSpan.FromMilliseconds(200)));

        Assert.Equal(408, exception.Error.Code);
        Assert.Equal(2, location.ResolveCount);
    }

    [Fact]
    public async Task Router_invalidates_cached_location_and_retries_once_when_endpoint_is_stale()
    {
        var actorId = new ActorId(5001);
        var staleAddress = new ActorAddress("local", 50, actorId);
        var currentAddress = new ActorAddress("local", 51, actorId);
        var innerLocation = new InMemoryYuoLocationRegistry();
        var cachedLocation = new CachingYuoLocationRegistry(innerLocation);
        var location = new YuoLocationActorRegistry(cachedLocation);
        var transport = new InMemoryActorTransport();
        var gold = 0L;

        await innerLocation.RegisterAsync(
            new YuoLocationKey("actor", actorId.Value.ToString()),
            new YuoActorAddress("local", "default", staleAddress.FiberId, actorId.Value));

        Assert.Equal(staleAddress, await location.ResolveAsync(actorId));

        await innerLocation.RegisterAsync(
            new YuoLocationKey("actor", actorId.Value.ToString()),
            new YuoActorAddress("local", "default", currentAddress.FiberId, actorId.Value));

        await using var fiber = new ActorFiber(
            currentAddress.FiberId,
            new DelegateActorMessageDispatcher((envelope, _) =>
            {
                if (envelope.Message is AddGold addGold)
                {
                    gold += addGold.Amount;
                    return ValueTask.FromResult<object?>(null);
                }

                return ValueTask.FromResult<object?>(gold);
            }));

        transport.RegisterEndpoint(currentAddress, fiber);
        fiber.Start();

        var router = new ActorRouter(location, transport);
        await router.SendAsync(new AddGold(actorId, 12));
        var result = await router.InvokeAsync<long>(new GetGold(actorId));

        Assert.Equal(12, result);
        Assert.Equal(12, gold);
        Assert.Equal(currentAddress, await location.ResolveAsync(actorId));
    }

    [Fact]
    public async Task Router_invalidates_cached_location_when_actor_fiber_mailbox_is_closed()
    {
        var actorId = new ActorId(5101);
        var staleAddress = new ActorAddress("local", 52, actorId);
        var currentAddress = new ActorAddress("local", 53, actorId);
        var innerLocation = new InMemoryYuoLocationRegistry();
        var cachedLocation = new CachingYuoLocationRegistry(innerLocation);
        var location = new YuoLocationActorRegistry(cachedLocation);
        var transport = new InMemoryActorTransport();
        var gold = 0L;

        await innerLocation.RegisterAsync(
            new YuoLocationKey("actor", actorId.Value.ToString()),
            new YuoActorAddress("local", "default", staleAddress.FiberId, actorId.Value));

        Assert.Equal(staleAddress, await location.ResolveAsync(actorId));

        var staleFiber = new ActorFiber(
            staleAddress.FiberId,
            new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(null)));
        staleFiber.Start();
        transport.RegisterEndpoint(staleAddress, staleFiber);
        await staleFiber.DisposeAsync();

        await innerLocation.RegisterAsync(
            new YuoLocationKey("actor", actorId.Value.ToString()),
            new YuoActorAddress("local", "default", currentAddress.FiberId, actorId.Value));

        await using var currentFiber = new ActorFiber(
            currentAddress.FiberId,
            new DelegateActorMessageDispatcher((envelope, _) =>
            {
                if (envelope.Message is AddGold addGold)
                {
                    gold += addGold.Amount;
                    return ValueTask.FromResult<object?>(null);
                }

                return ValueTask.FromResult<object?>(gold);
            }));

        transport.RegisterEndpoint(currentAddress, currentFiber);
        currentFiber.Start();

        var router = new ActorRouter(location, transport);
        await router.SendAsync(new AddGold(actorId, 6));
        var result = await router.InvokeAsync<long>(new GetGold(actorId));

        Assert.Equal(6, result);
        Assert.Equal(currentAddress, await location.ResolveAsync(actorId));
    }

    [Fact]
    public async Task LocationSender_invokes_by_business_key_and_retries_stale_cached_address()
    {
        var actorId = new ActorId(6001);
        var key = new YuoLocationKey("Player", "p6001");
        var staleAddress = new YuoActorAddress("local", "game", 60, actorId.Value);
        var currentAddress = new YuoActorAddress("local", "game", 61, actorId.Value);
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();
        var gold = 0L;

        await locations.RegisterAsync(key, staleAddress);
        await using (var staleFiber = new ActorFiber(
                         60,
                         new DelegateActorMessageDispatcher((envelope, _) =>
                         {
                             if (envelope.Message is AddGold addGold)
                             {
                                 gold += addGold.Amount;
                                 return ValueTask.FromResult<object?>(null);
                             }

                             return ValueTask.FromResult<object?>(gold);
                         })))
        {
            transport.RegisterEndpoint(new ActorAddress("local", 60, actorId), staleFiber);
            staleFiber.Start();

            var sender = new YuoActorLocationSender(locations, transport);
            await sender.SendAsync(key, target => new AddGold(target, 5));
            Assert.Equal(5, await sender.InvokeAsync<long>(key, target => new GetGold(target)));

            transport.UnregisterEndpoint(new ActorAddress("local", 60, actorId));
            await locations.RegisterAsync(key, currentAddress);

            await using var currentFiber = new ActorFiber(
                61,
                new DelegateActorMessageDispatcher((envelope, _) =>
                {
                    if (envelope.Message is AddGold addGold)
                    {
                        gold += addGold.Amount;
                        return ValueTask.FromResult<object?>(null);
                    }

                    return ValueTask.FromResult<object?>(gold);
                }));

            transport.RegisterEndpoint(new ActorAddress("local", 61, actorId), currentFiber);
            currentFiber.Start();

            await sender.SendAsync(key, target => new AddGold(target, 7));
            var result = await sender.InvokeAsync<long>(key, target => new GetGold(target));

            Assert.Equal(12, result);
        }
    }

    [Fact]
    public void LocationSender_rejects_null_dependencies()
    {
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();

        Assert.Throws<ArgumentNullException>(() => new YuoActorLocationSender(null!, transport));
        Assert.Throws<ArgumentNullException>(() => new YuoActorLocationSender(locations, null!));
    }

    [Fact]
    public async Task LocationSender_rejects_null_message_from_factory()
    {
        var key = new YuoLocationKey("Player", "null-message");
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();
        await locations.RegisterAsync(key, new YuoActorAddress("local", "game", 60, 6001));

        var sender = new YuoActorLocationSender(locations, transport);
        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await sender.SendAsync(key, _ => null!));

        Assert.Equal(400, exception.Error.Code);
        Assert.Contains("Message factory returned null", exception.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocationSender_rejects_null_request_from_factory()
    {
        var key = new YuoLocationKey("Player", "null-request");
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();
        await locations.RegisterAsync(key, new YuoActorAddress("local", "game", 60, 6001));

        var sender = new YuoActorLocationSender(locations, transport);
        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await sender.InvokeAsync<long>(key, _ => null!));

        Assert.Equal(400, exception.Error.Code);
        Assert.Contains("Request factory returned null", exception.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocationSender_can_retry_more_than_once_during_location_migration()
    {
        var key = new YuoLocationKey("Player", "migrating");
        var staleAddress1 = new YuoActorAddress("local", "game", 70, 7001);
        var staleAddress2 = new YuoActorAddress("local", "game", 71, 7002);
        var currentAddress = new YuoActorAddress("local", "game", 72, 7003);
        var locations = new SequenceLocationRegistry(key, [staleAddress1, staleAddress2, currentAddress]);
        var transport = new InMemoryActorTransport();

        await using var currentFiber = new ActorFiber(
            72,
            new DelegateActorMessageDispatcher((envelope, _) =>
            {
                Assert.Equal(new ActorId(currentAddress.ActorId), envelope.TargetActorId);
                return ValueTask.FromResult<object?>(9L);
            }));

        transport.RegisterEndpoint(new ActorAddress("local", 72, new ActorId(currentAddress.ActorId)), currentFiber);
        currentFiber.Start();

        var sender = new YuoActorLocationSender(
            locations,
            transport,
            new YuoActorLocationSenderOptions(MaxStaleRetries: 2));

        var result = await sender.InvokeAsync<long>(key, target => new GetGold(target));

        Assert.Equal(9, result);
        Assert.Equal(3, locations.ResolveCount);
    }

    [Fact]
    public async Task LocationSender_uses_injected_delay_provider_for_stale_retry_wait()
    {
        var key = new YuoLocationKey("Player", "delay-provider");
        var staleAddress = new YuoActorAddress("local", "game", 73, 7301);
        var currentAddress = new YuoActorAddress("local", "game", 74, 7302);
        var locations = new SequenceLocationRegistry(key, [staleAddress, currentAddress]);
        var transport = new InMemoryActorTransport();
        var delayProvider = new RecordingDelayProvider();

        await using var currentFiber = new ActorFiber(
            74,
            new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(11L)));

        transport.RegisterEndpoint(new ActorAddress("local", 74, new ActorId(currentAddress.ActorId)), currentFiber);
        currentFiber.Start();

        var sender = new YuoActorLocationSender(
            locations,
            transport,
            new YuoActorLocationSenderOptions(
                MaxStaleRetries: 1,
                RetryDelay: TimeSpan.FromMilliseconds(25),
                DelayProvider: delayProvider));

        var result = await sender.InvokeAsync<long>(key, target => new GetGold(target));

        Assert.Equal(11, result);
        Assert.Equal([TimeSpan.FromMilliseconds(25)], delayProvider.Delays);
    }

    [Fact]
    public async Task LocationSender_can_use_exponential_retry_delay_policy()
    {
        var key = new YuoLocationKey("Player", "retry-policy");
        var staleAddress1 = new YuoActorAddress("local", "game", 75, 7501);
        var staleAddress2 = new YuoActorAddress("local", "game", 76, 7502);
        var currentAddress = new YuoActorAddress("local", "game", 77, 7503);
        var locations = new SequenceLocationRegistry(key, [staleAddress1, staleAddress2, currentAddress]);
        var transport = new InMemoryActorTransport();
        var delayProvider = new RecordingDelayProvider();

        await using var currentFiber = new ActorFiber(
            77,
            new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(17L)));

        transport.RegisterEndpoint(new ActorAddress("local", 77, new ActorId(currentAddress.ActorId)), currentFiber);
        currentFiber.Start();

        var sender = new YuoActorLocationSender(
            locations,
            transport,
            new YuoActorLocationSenderOptions(
                MaxStaleRetries: 2,
                RetryDelay: TimeSpan.FromMilliseconds(10),
                DelayProvider: delayProvider,
                RetryDelayPolicy: new ExponentialActorRetryDelayPolicy()));

        var result = await sender.InvokeAsync<long>(key, target => new GetGold(target));

        Assert.Equal(17, result);
        Assert.Equal([TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)], delayProvider.Delays);
    }

    [Fact]
    public void Exponential_retry_delay_policy_caps_delay()
    {
        var policy = new ExponentialActorRetryDelayPolicy(maxDelay: TimeSpan.FromMilliseconds(25));
        var key = new YuoLocationKey("Player", "policy");

        Assert.Equal(
            TimeSpan.FromMilliseconds(10),
            policy.GetDelay(new ActorRetryDelayContext(key, ActorRetryDelayReason.StaleAddress, 1, TimeSpan.FromMilliseconds(10))));
        Assert.Equal(
            TimeSpan.FromMilliseconds(20),
            policy.GetDelay(new ActorRetryDelayContext(key, ActorRetryDelayReason.StaleAddress, 2, TimeSpan.FromMilliseconds(10))));
        Assert.Equal(
            TimeSpan.FromMilliseconds(25),
            policy.GetDelay(new ActorRetryDelayContext(key, ActorRetryDelayReason.StaleAddress, 3, TimeSpan.FromMilliseconds(10))));
    }

    [Fact]
    public void Exponential_retry_delay_policy_validates_jitter_ratio()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialActorRetryDelayPolicy(jitterRatio: -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialActorRetryDelayPolicy(jitterRatio: 1.1));
    }

    [Fact]
    public async Task LocationSender_rejects_locked_location_without_sending_to_old_address()
    {
        var actorId = new ActorId(8001);
        var key = new YuoLocationKey("Player", "locked");
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();
        var dispatched = false;

        await locations.RegisterAsync(key, new YuoActorAddress("local", "game", 80, actorId.Value));
        await locations.LockAsync(key, "move");
        await using var fiber = new ActorFiber(
            80,
            new DelegateActorMessageDispatcher((_, _) =>
            {
                dispatched = true;
                return ValueTask.FromResult<object?>(1L);
            }));
        transport.RegisterEndpoint(new ActorAddress("local", 80, actorId), fiber);
        fiber.Start();

        var sender = new YuoActorLocationSender(locations, transport);
        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await sender.InvokeAsync<long>(key, target => new GetGold(target)));

        Assert.Equal(409, exception.Error.Code);
        Assert.False(dispatched);
    }

    [Fact]
    public async Task LocationSender_rejects_locked_location_even_when_old_address_is_cached()
    {
        var actorId = new ActorId(8002);
        var key = new YuoLocationKey("Player", "locked-cached");
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();
        var dispatched = 0;

        await locations.RegisterAsync(key, new YuoActorAddress("local", "game", 80, actorId.Value));
        await using var fiber = new ActorFiber(
            80,
            new DelegateActorMessageDispatcher((_, _) =>
            {
                dispatched++;
                return ValueTask.FromResult<object?>(1L);
            }));
        transport.RegisterEndpoint(new ActorAddress("local", 80, actorId), fiber);
        fiber.Start();

        var sender = new YuoActorLocationSender(locations, transport);
        Assert.Equal(1, await sender.InvokeAsync<long>(key, target => new GetGold(target)));
        Assert.Equal(1, dispatched);

        await locations.LockAsync(key, "move");
        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await sender.InvokeAsync<long>(key, target => new GetGold(target)));

        Assert.Equal(409, exception.Error.Code);
        Assert.Equal(1, dispatched);
    }

    [Fact]
    public async Task LocationSender_retries_locked_location_until_migration_completes()
    {
        var oldActorId = new ActorId(8101);
        var newActorId = new ActorId(8102);
        var key = new YuoLocationKey("Player", "locked-migrate");
        var oldAddress = new YuoActorAddress("local", "game", 81, oldActorId.Value);
        var newAddress = new YuoActorAddress("local", "game", 82, newActorId.Value);
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();

        await locations.RegisterAsync(key, oldAddress);
        await locations.LockAsync(key, "move");

        await using var newFiber = new ActorFiber(
            82,
            new DelegateActorMessageDispatcher((envelope, _) =>
            {
                Assert.Equal(newActorId, envelope.TargetActorId);
                return ValueTask.FromResult<object?>(13L);
            }));
        transport.RegisterEndpoint(new ActorAddress("local", 82, newActorId), newFiber);
        newFiber.Start();

        var migration = Task.Run(async () =>
        {
            await Task.Delay(50);
            var unlocked = await locations.UnlockAsync(key, "move");
            var moved = await locations.RegisterAsync(key, newAddress, unlocked.Value!.Version);
            Assert.True(moved.IsSuccess);
        });

        var sender = new YuoActorLocationSender(
            locations,
            transport,
            new YuoActorLocationSenderOptions(
                MaxLockedResolveRetries: 10,
                LockedResolveRetryDelay: TimeSpan.FromMilliseconds(10)));

        var result = await sender.InvokeAsync<long>(key, target => new GetGold(target));
        await migration;

        Assert.Equal(13, result);
    }

    [Fact]
    public async Task LocationSender_caller_cancellation_is_not_reported_as_timeout()
    {
        var key = new YuoLocationKey("Player", "caller-canceled");
        var sender = new YuoActorLocationSender(new InMemoryYuoLocationRegistry(), new InMemoryActorTransport());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sender.InvokeAsync<long>(
                key,
                target => new GetGold(target),
                TimeSpan.FromSeconds(10),
                cts.Token));
    }

    [Fact]
    public async Task LocationSender_honors_cancellation_before_cached_address_factory()
    {
        var actorId = new ActorId(8301);
        var key = new YuoLocationKey("Player", "cached-cancel");
        var address = new YuoActorAddress("local", "game", 83, actorId.Value);
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();
        var factoryCalls = 0;

        await locations.RegisterAsync(key, address);
        await using var fiber = new ActorFiber(
            83,
            new DelegateActorMessageDispatcher((_, _) => ValueTask.FromResult<object?>(21L)));
        transport.RegisterEndpoint(new ActorAddress("local", 83, actorId), fiber);
        fiber.Start();

        var sender = new YuoActorLocationSender(locations, transport);
        Assert.Equal(21, await sender.InvokeAsync<long>(key, target => new GetGold(target)));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await sender.InvokeAsync<long>(
                key,
                target =>
                {
                    factoryCalls++;
                    return new GetGold(target);
                },
                cancellationToken: cts.Token));
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task LocationSender_timeout_covers_locked_location_retry_wait()
    {
        var actorId = new ActorId(8201);
        var key = new YuoLocationKey("Player", "locked-timeout");
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();

        await locations.RegisterAsync(key, new YuoActorAddress("local", "game", 82, actorId.Value));
        await locations.LockAsync(key, "move");

        var sender = new YuoActorLocationSender(
            locations,
            transport,
            new YuoActorLocationSenderOptions(
                MaxLockedResolveRetries: 10,
                LockedResolveRetryDelay: TimeSpan.FromSeconds(30)));

        var exception = await Assert.ThrowsAsync<ActorException>(async () =>
            await sender.InvokeAsync<long>(
                key,
                target => new GetGold(target),
                TimeSpan.FromMilliseconds(20)));

        Assert.Equal(408, exception.Error.Code);
    }

    private sealed record AddGold(ActorId TargetActorId, long Amount) : IActorMessage;

    private sealed record GetGold(ActorId TargetActorId) : IActorRequest<long>;

    private sealed record SlowRequest(ActorId TargetActorId) : IActorRequest<int>;

    private sealed record AddWalletGold(ActorId TargetActorId, long Amount) : IActorRequest<WalletGoldChanged>;

    private sealed record SpendWalletGold(ActorId TargetActorId, long Amount) : IActorRequest<WalletGoldChanged>;

    private sealed record FactoryConstructedAddGold(ActorId TargetActorId, long Amount) : IActorMessage;

    private sealed record WalletGoldChanged(long Gold);

    private sealed class WalletComponent : YuoComponent
    {
        public long Gold;
    }

    private sealed class AddWalletGoldHandler : YuoActorRequestHandler<AddWalletGold, WalletGoldChanged>
    {
        protected override ValueTask<WalletGoldChanged> RunAsync(
            YuoEntity entity,
            AddWalletGold request,
            CancellationToken cancellationToken)
        {
            var wallet = entity.GetComponent<WalletComponent>();
            wallet.Gold += request.Amount;
            return ValueTask.FromResult(new WalletGoldChanged(wallet.Gold));
        }
    }

    private sealed class SpendWalletGoldHandler : YuoActorRequestHandler<SpendWalletGold, WalletGoldChanged>
    {
        protected override ValueTask<WalletGoldChanged> RunAsync(
            YuoEntity entity,
            SpendWalletGold request,
            CancellationToken cancellationToken)
        {
            var wallet = entity.GetComponent<WalletComponent>();
            if (wallet.Gold < request.Amount)
            {
                throw new ActorException(ActorError.Business(
                    "wallet.insufficient_gold",
                    "Wallet does not have enough gold."));
            }

            wallet.Gold -= request.Amount;
            return ValueTask.FromResult(new WalletGoldChanged(wallet.Gold));
        }
    }

    private sealed class FactoryConstructedAddGoldHandler(HandlerCounter counter)
        : YuoActorHandler<FactoryConstructedAddGold>
    {
        protected override ValueTask RunAsync(
            YuoEntity entity,
            FactoryConstructedAddGold message,
            CancellationToken cancellationToken)
        {
            counter.Value += message.Amount;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NonActorMessageHandler(int marker) : IYuoActorHandler
    {
        public Type MessageType => PickType(marker, typeof(string));
        public Type? ResponseType => null;

        public ValueTask<object?> HandleAsync(
            YuoEntity entity,
            object message,
            CancellationToken cancellationToken) => ValueTask.FromResult<object?>(null);
    }

    private sealed class MessageWithResponseHandler(int marker) : IYuoActorHandler
    {
        public Type MessageType => PickType(marker, typeof(AddGold));
        public Type? ResponseType => typeof(long);

        public ValueTask<object?> HandleAsync(
            YuoEntity entity,
            object message,
            CancellationToken cancellationToken) => ValueTask.FromResult<object?>(0L);
    }

    private sealed class RequestWithoutResponseHandler(int marker) : IYuoActorHandler
    {
        public Type MessageType => PickType(marker, typeof(GetGold));
        public Type? ResponseType => null;

        public ValueTask<object?> HandleAsync(
            YuoEntity entity,
            object message,
            CancellationToken cancellationToken) => ValueTask.FromResult<object?>(0L);
    }

    private sealed class RequestWrongResponseHandler(int marker) : IYuoActorHandler
    {
        public Type MessageType => PickType(marker, typeof(GetGold));
        public Type? ResponseType => typeof(string);

        public ValueTask<object?> HandleAsync(
            YuoEntity entity,
            object message,
            CancellationToken cancellationToken) => ValueTask.FromResult<object?>("bad");
    }

    private sealed class CustomGetGoldHandler(int marker) : IYuoActorHandler
    {
        public Type MessageType => PickType(marker, typeof(GetGold));
        public Type? ResponseType => typeof(long);

        public ValueTask<object?> HandleAsync(
            YuoEntity entity,
            object message,
            CancellationToken cancellationToken) => ValueTask.FromResult<object?>(0L);
    }

    private static Type PickType(int marker, Type type)
    {
        return marker == int.MinValue ? typeof(object) : type;
    }

    private sealed class HandlerCounter
    {
        public long Value;
    }

    private sealed class SequenceLocationRegistry(
        YuoLocationKey expectedKey,
        IReadOnlyList<YuoActorAddress> addresses) : IYuoLocationRegistry
    {
        private int _resolveCount;

        public int ResolveCount => _resolveCount;

        public ValueTask<YuoResult<YuoLocationEntry>> RegisterAsync(
            YuoLocationKey key,
            YuoActorAddress address,
            long expectedVersion = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoLocationEntry?> ResolveAsync(YuoLocationKey key, CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedKey, key);
            var index = Math.Min(_resolveCount, addresses.Count - 1);
            _resolveCount++;
            return ValueTask.FromResult<YuoLocationEntry?>(new YuoLocationEntry(
                key,
                addresses[index],
                _resolveCount,
                null,
                null,
                DateTimeOffset.UtcNow));
        }

        public ValueTask<YuoResult<YuoLocationEntry>> LockAsync(
            YuoLocationKey key,
            string owner,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
            YuoLocationKey key,
            string owner,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
            YuoLocationKey key,
            string owner,
            string lockToken,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
            YuoLocationKey key,
            string owner,
            YuoActorAddress expectedAddress,
            YuoActorAddress newAddress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
            YuoLocationKey key,
            string owner,
            string lockToken,
            YuoActorAddress expectedAddress,
            YuoActorAddress newAddress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<bool> RemoveAsync(
            YuoLocationKey key,
            long expectedVersion = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RecordingDelayProvider : IActorDelayProvider
    {
        public List<TimeSpan> Delays { get; } = [];

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            Delays.Add(delay);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingActorEndpoint : IActorEndpoint
    {
        public int SendCount { get; private set; }

        public ValueTask SendAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            SendCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<ActorEnvelope> InvokeAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class CountingActorLocationRegistry : IActorLocationRegistry
    {
        public int ResolveCount { get; private set; }

        public ValueTask RegisterAsync(
            ActorId actorId,
            ActorAddress address,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<ActorAddress?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            return ValueTask.FromResult<ActorAddress?>(null);
        }

        public ValueTask UnregisterAsync(ActorId actorId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SlowActorLocationRegistry(ActorAddress address) : IActorLocationRegistry
    {
        public int ResolveCount { get; private set; }

        public ValueTask RegisterAsync(
            ActorId actorId,
            ActorAddress actorAddress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async ValueTask<ActorAddress?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return address;
        }

        public ValueTask UnregisterAsync(ActorId actorId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class SlowSecondResolveActorLocationRegistry(
        ActorAddress firstAddress,
        ActorAddress secondAddress) : IActorLocationRegistry
    {
        public int ResolveCount { get; private set; }

        public ValueTask RegisterAsync(
            ActorId actorId,
            ActorAddress actorAddress,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public async ValueTask<ActorAddress?> ResolveAsync(
            ActorId actorId,
            CancellationToken cancellationToken = default)
        {
            ResolveCount++;
            if (ResolveCount == 1)
            {
                return firstAddress;
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return secondAddress;
        }

        public ValueTask UnregisterAsync(ActorId actorId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NotFoundActorEndpoint : IActorEndpoint
    {
        public ValueTask SendAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new ActorException(ActorError.NotFound("stale endpoint."));
        }

        public ValueTask<ActorEnvelope> InvokeAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default)
        {
            throw new ActorException(ActorError.NotFound("stale endpoint."));
        }
    }
}
