using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using YuoCore.Abstractions;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Backend.WebSockets;
using YuoCore.Game;
using YuoCore.Gateway;
using YuoCore.Persistence;
using YuoCore.Protocol;

namespace YuoCore.Backend.Tests;

public sealed class GameClientMessageDispatcherTests
{
    [Fact]
    public async Task Hello_creates_player_and_returns_welcome()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));

        var response = await dispatcher.DispatchAsync(new ClientEnvelope
        {
            RequestId = 42,
            Hello = new HelloRequest { PlayerId = string.Empty }
        });

        Assert.Equal(42UL, response.RequestId);
        Assert.Equal(ServerEnvelope.PayloadOneofCase.Welcome, response.PayloadCase);
        Assert.True(Guid.TryParse(response.Welcome.PlayerId, out _));
    }

    [Fact]
    public async Task Invalid_click_returns_error()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));

        var response = await dispatcher.DispatchAsync(new ClientEnvelope
        {
            RequestId = 7,
            Click = new ClickRequest { PlayerId = "bad", Amount = 1 }
        });

        Assert.Equal(7UL, response.RequestId);
        Assert.Equal(ServerEnvelope.PayloadOneofCase.Error, response.PayloadCase);
        Assert.Equal("invalid_player", response.Error.Code);
    }

    [Fact]
    public async Task Gateway_bridge_preserves_legacy_protobuf_response()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));
        var session = new YuoGatewaySession();

        var request = new ClientEnvelope
        {
            RequestId = 88,
            Hello = new HelloRequest { PlayerId = string.Empty }
        };

        var outbound = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(1, 0, request.RequestId, 0, request.ToByteArray()));
        var response = ServerEnvelope.Parser.ParseFrom(outbound.Payload.Span);

        Assert.Null(outbound.Error);
        Assert.Equal(1UL, outbound.ServerSeq);
        Assert.Equal(88UL, response.RequestId);
        Assert.Equal(ServerEnvelope.PayloadOneofCase.Welcome, response.PayloadCase);
    }

    [Fact]
    public async Task Gateway_bridge_accepts_v1_payload_and_returns_v1_payload()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));
        var session = new YuoGatewaySession();
        var request = new HelloRequest { PlayerId = string.Empty };

        var outbound = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(
                1,
                YuoCoreProtocolOpCodes.HelloRequest,
                89,
                1,
                request.ToByteArray()));
        var welcome = Welcome.Parser.ParseFrom(outbound.Payload.Span);

        Assert.Null(outbound.Error);
        Assert.Equal(YuoCoreProtocolOpCodes.Welcome, outbound.OpCode);
        Assert.Equal(1UL, outbound.ServerSeq);
        Assert.Equal(89UL, outbound.RequestId);
        Assert.True(Guid.TryParse(welcome.PlayerId, out _));
    }

    [Fact]
    public async Task Gateway_bridge_accepts_enter_and_move_v1_payloads()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));
        var session = new YuoGatewaySession();

        var hello = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(
                1,
                YuoCoreProtocolOpCodes.HelloRequest,
                201,
                1,
                new HelloRequest { PlayerId = string.Empty }.ToByteArray()));
        var welcome = Welcome.Parser.ParseFrom(hello.Payload.Span);

        var enter = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(
                1,
                YuoCoreProtocolOpCodes.EnterMapRequest,
                202,
                2,
                new EnterMapRequest { PlayerId = welcome.PlayerId, MapId = 1001 }.ToByteArray()));
        var enterWorld = WorldSnapshot.Parser.ParseFrom(enter.Payload.Span);

        var move = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(
                1,
                YuoCoreProtocolOpCodes.MoveRequest,
                203,
                3,
                new MoveRequest { PlayerId = welcome.PlayerId, InputSeq = 1, X = 3, Y = 0, Z = 4 }.ToByteArray()));
        var movedWorld = WorldSnapshot.Parser.ParseFrom(move.Payload.Span);

        Assert.Equal(YuoCoreProtocolOpCodes.WorldSnapshot, enter.OpCode);
        Assert.Equal(1001, enterWorld.MapId);
        Assert.Contains(enterWorld.Players, player => player.PlayerId == welcome.PlayerId);
        Assert.Equal(YuoCoreProtocolOpCodes.WorldSnapshot, move.OpCode);
        Assert.Contains(movedWorld.Players, player =>
            player.PlayerId == welcome.PlayerId &&
            Math.Abs(player.X - 3) < 0.001f &&
            Math.Abs(player.Z - 4) < 0.001f);
    }

    [Fact]
    public async Task Gateway_bridge_can_route_enter_and_move_through_game_command_queue()
    {
        var options = Options.Create(new GameOptions
        {
            TickRate = 1_000,
            ShardCount = 2,
            ChannelCapacity = 32,
            GatewayRequestsUseCommandQueue = true
        });
        using var runtime = new GameRuntime(options);
        runtime.Start();
        var queue = new GameCommandQueue(options);
        var processor = new GameCommandProcessor(runtime);
        var loop = new GameLoopService(
            runtime,
            queue,
            processor,
            options,
            NullLogger<GameLoopService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await loop.StartAsync(cts.Token);
        try
        {
            var dispatcher = new GameClientMessageDispatcher(runtime, queue, null, options);
            var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));
            var session = new YuoGatewaySession();

            var hello = await pipeline.DispatchAsync(
                session,
                new YuoGatewayInboundMessage(
                    1,
                    YuoCoreProtocolOpCodes.HelloRequest,
                    211,
                    1,
                    new HelloRequest { PlayerId = string.Empty }.ToByteArray()),
                cts.Token);
            var welcome = Welcome.Parser.ParseFrom(hello.Payload.Span);

            var click = await pipeline.DispatchAsync(
                session,
                new YuoGatewayInboundMessage(
                    1,
                    YuoCoreProtocolOpCodes.ClickRequest,
                    214,
                    2,
                    new ClickRequest { PlayerId = welcome.PlayerId, Amount = 2 }.ToByteArray()),
                cts.Token);
            var clicked = PlayerSnapshot.Parser.ParseFrom(click.Payload.Span);

            var enter = await pipeline.DispatchAsync(
                session,
                new YuoGatewayInboundMessage(
                    1,
                    YuoCoreProtocolOpCodes.EnterMapRequest,
                    212,
                    3,
                    new EnterMapRequest { PlayerId = welcome.PlayerId, MapId = 1001 }.ToByteArray()),
                cts.Token);
            var enterWorld = WorldSnapshot.Parser.ParseFrom(enter.Payload.Span);

            var move = await pipeline.DispatchAsync(
                session,
                new YuoGatewayInboundMessage(
                    1,
                    YuoCoreProtocolOpCodes.MoveRequest,
                    213,
                    4,
                    new MoveRequest { PlayerId = welcome.PlayerId, InputSeq = 1, X = 6, Y = 0, Z = 9 }.ToByteArray()),
                cts.Token);
            var movedWorld = WorldSnapshot.Parser.ParseFrom(move.Payload.Span);

            Assert.Equal(YuoCoreProtocolOpCodes.PlayerSnapshot, click.OpCode);
            Assert.Equal(2, clicked.Clicks);
            Assert.True(clicked.Gold > 0);
            Assert.Equal(YuoCoreProtocolOpCodes.WorldSnapshot, enter.OpCode);
            Assert.Equal(1001, enterWorld.MapId);
            Assert.Equal(YuoCoreProtocolOpCodes.WorldSnapshot, move.OpCode);
            Assert.Contains(movedWorld.Players, player =>
                player.PlayerId == welcome.PlayerId &&
                Math.Abs(player.X - 6) < 0.001f &&
                Math.Abs(player.Z - 9) < 0.001f);
        }
        finally
        {
            await loop.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Gateway_bridge_binds_session_player_after_hello()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));
        var session = new YuoGatewaySession();

        var outbound = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(
                1,
                YuoCoreProtocolOpCodes.HelloRequest,
                301,
                1,
                new HelloRequest { PlayerId = string.Empty }.ToByteArray()));
        var welcome = Welcome.Parser.ParseFrom(outbound.Payload.Span);

        Assert.True(Guid.TryParse(welcome.PlayerId, out var playerId));
        Assert.Equal(playerId, session.PlayerId);
    }

    [Fact]
    public void StateSync_push_dispatcher_drains_observer_events_as_delta()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var mover = runtime.LoginPlayer("mover", null, "session-a");
        var observer = runtime.LoginPlayer("observer", null, "session-b");
        var session = new YuoGatewaySession();
        session.BindPlayer(observer.PlayerId);

        Assert.True(runtime.TryEnterMap(mover.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(observer.PlayerId, 1001, out _));

        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        _ = pushDispatcher.DrainPushes(session);

        Assert.True(runtime.TryMove(mover.PlayerId, 3, 0, 4, 1, out _));
        var pushes = pushDispatcher.DrainPushes(session);

        Assert.Single(pushes);
        var push = pushes[0];
        Assert.Equal(YuoCoreProtocolOpCodes.StateDelta, push.OpCode);
        Assert.Equal(0UL, push.RequestId);
        Assert.True(push.ServerSeq > 0);

        var delta = StateDeltaMessage.Parser.ParseFrom(push.Payload.Span);
        Assert.Equal((long)push.ServerSeq, delta.Tick);
        Assert.True(delta.HasBaseTick);
        Assert.True(delta.BaseTick < delta.Tick);
        var change = Assert.Single(delta.Changes);
        Assert.Equal(StateChangeKind.Update, change.Kind);
        Assert.Equal(mover.UnitId, change.NetworkId);
        Assert.Equal("player", change.State.Kind);

        var state = PlayerWorldState.Parser.ParseFrom(change.State.Payload);
        Assert.Equal(mover.PlayerId.ToString(), state.PlayerId);
        Assert.Equal(3, state.X);
        Assert.Equal(4, state.Z);
    }

    [Fact]
    public void StateSync_push_dispatcher_sends_initial_snapshot_once_per_session_map()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var mover = runtime.LoginPlayer("mover", null, "session-a");
        var observer = runtime.LoginPlayer("observer", null, "session-b");
        var session = new YuoGatewaySession();
        session.BindPlayer(observer.PlayerId);

        Assert.True(runtime.TryEnterMap(mover.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(observer.PlayerId, 1001, out _));

        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var initialPushes = pushDispatcher.DrainPushes(session);

        var initial = Assert.Single(initialPushes);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSnapshot, initial.OpCode);
        var snapshot = StateSnapshotMessage.Parser.ParseFrom(initial.Payload.Span);
        Assert.Equal((long)initial.ServerSeq, snapshot.Tick);
        Assert.Contains(snapshot.Entities, entity => entity.NetworkId == mover.UnitId);
        Assert.Contains(snapshot.Entities, entity => entity.NetworkId == observer.UnitId);

        Assert.Empty(pushDispatcher.DrainPushes(session));

        Assert.True(runtime.TryMove(mover.PlayerId, 7, 0, 8, 1, out _));
        var deltaPushes = pushDispatcher.DrainPushes(session);

        var deltaPush = Assert.Single(deltaPushes);
        Assert.Equal(YuoCoreProtocolOpCodes.StateDelta, deltaPush.OpCode);
        var delta = StateDeltaMessage.Parser.ParseFrom(deltaPush.Payload.Span);
        Assert.True(delta.HasBaseTick);
        Assert.Equal(snapshot.Tick, delta.BaseTick);
        var change = Assert.Single(delta.Changes);
        Assert.Equal(StateChangeKind.Update, change.Kind);
        Assert.Equal(mover.UnitId, change.NetworkId);
    }

    [Fact]
    public void StateSync_push_dispatcher_can_drain_aoi_stream_deltas_as_optional_multi_stream_path()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 1 }));
        runtime.Start();
        var first = runtime.LoginPlayer("first", null, "session-a");
        var second = runtime.LoginPlayer("second", null, "session-b");
        var session = new YuoGatewaySession();
        session.BindPlayer(first.PlayerId);
        var aggregator = new GameAoiStreamDeltaAggregator();
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(first.PlayerId, 5, 0, 5, 1, out _));
        Assert.True(runtime.TryMove(second.PlayerId, 15, 0, 0, 1, out _));

        var initialPushes = pushDispatcher.DrainAoiStreamPushes(session, 1001, 10, aggregator);

        Assert.Equal(3, initialPushes.Count);
        var initialSubscription = StateSubscriptionMessage.Parser.ParseFrom(initialPushes[0].Payload.Span);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSubscription, initialPushes[0].OpCode);
        Assert.True(initialSubscription.ResetMissing);
        Assert.Equal("aoi_streams_changed", initialSubscription.Reason);
        Assert.Contains("map:1001:aoi:default.cell_0_0", initialSubscription.StreamIds);
        Assert.Contains("map:1001:aoi:default.cell_1_0", initialSubscription.StreamIds);
        var initialByStream = initialPushes
            .Where(push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta)
            .Select(push => StateDeltaMessage.Parser.ParseFrom(push.Payload.Span))
            .ToDictionary(delta => delta.StreamId, StringComparer.Ordinal);
        Assert.Contains(initialByStream["map:1001:aoi:default.cell_0_0"].Changes, change =>
            change.Kind == StateChangeKind.Enter &&
            change.NetworkId == first.UnitId);
        Assert.Contains(initialByStream["map:1001:aoi:default.cell_1_0"].Changes, change =>
            change.Kind == StateChangeKind.Enter &&
            change.NetworkId == second.UnitId);

        Assert.True(runtime.TryMove(first.PlayerId, 15, 0, 0, 2, out _));

        var movedPushes = pushDispatcher.DrainAoiStreamPushes(session, 1001, 10, aggregator);
        var movedSubscription = StateSubscriptionMessage.Parser.ParseFrom(movedPushes[0].Payload.Span);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSubscription, movedPushes[0].OpCode);
        Assert.True(movedSubscription.ResetMissing);
        Assert.DoesNotContain("map:1001:aoi:default.cell_0_0", movedSubscription.StreamIds);
        Assert.Contains("map:1001:aoi:default.cell_1_0", movedSubscription.StreamIds);
        var movedByStream = movedPushes
            .Where(push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta)
            .Select(push => StateDeltaMessage.Parser.ParseFrom(push.Payload.Span))
            .ToDictionary(delta => delta.StreamId, StringComparer.Ordinal);
        var oldCell = movedByStream["map:1001:aoi:default.cell_0_0"];
        var newCell = movedByStream["map:1001:aoi:default.cell_1_0"];

        Assert.Contains(oldCell.Changes, change =>
            change.Kind == StateChangeKind.Leave &&
            change.NetworkId == first.UnitId);
        Assert.Contains(newCell.Changes, change =>
            change.Kind == StateChangeKind.Enter &&
            change.NetworkId == first.UnitId);
        Assert.Equal(initialByStream[oldCell.StreamId].Tick, oldCell.BaseTick);
        Assert.Equal(initialByStream[newCell.StreamId].Tick, newCell.BaseTick);
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(oldCell.StreamId, out var oldCount, out _, out _));
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(newCell.StreamId, out var newCount, out _, out _));
        Assert.True(oldCount > 0);
        Assert.True(newCount > 0);

        Assert.True(pushDispatcher.TryCreateAckResponse(
            session,
            newCell.Tick,
            newCell.StreamId,
            701,
            out var ackPush));
        var ack = StateSyncAckResponse.Parser.ParseFrom(ackPush.Payload.Span);

        Assert.True(ack.Accepted);
        Assert.Equal(newCell.StreamId, ack.StreamId);
        Assert.Equal(newCell.Tick, ack.AckTick);
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(newCell.StreamId, out newCount, out var oldest, out var newest));
        Assert.Equal(0, newCount);
        Assert.Null(oldest);
        Assert.Null(newest);

        var reconnectedSession = new YuoGatewaySession();
        reconnectedSession.BindPlayer(first.PlayerId);
        Assert.True(pushDispatcher.TryCreateRecoverResponse(
            reconnectedSession,
            initialByStream[newCell.StreamId].Tick,
            newCell.StreamId,
            702,
            out var recoverPush));
        var recover = StateSyncRecoverResponse.Parser.ParseFrom(recoverPush.Payload.Span);

        Assert.False(recover.Replayed);
        Assert.Equal(newCell.StreamId, recover.StreamId);
        Assert.NotNull(recover.Snapshot);
        Assert.Equal(newCell.StreamId, recover.Snapshot.StreamId);
        Assert.True(recover.Snapshot.Tick > newCell.Tick);
        Assert.Empty(recover.Deltas);
        Assert.Contains(recover.Snapshot.Entities, entity => entity.NetworkId == first.UnitId);
        Assert.Contains(recover.Snapshot.Entities, entity => entity.NetworkId == second.UnitId);
    }

    [Fact]
    public void StateSync_push_dispatcher_can_recover_unseen_aoi_stream_with_configured_cell_size()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 1 }));
        runtime.Start();
        var player = runtime.LoginPlayer("first", null, "session-a");
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(player.PlayerId, 15, 0, 0, 1, out _));
        var pushDispatcher = new GameStateSyncPushDispatcher(
            runtime,
            Options.Create(new GameOptions { StateSyncAoiCellSize = 10 }));

        Assert.True(pushDispatcher.TryCreateRecoverResponse(
            session,
            knownTick: 0,
            streamId: "map:1001:aoi:default.cell_1_0",
            requestId: 703,
            out var recoverPush));
        var recover = StateSyncRecoverResponse.Parser.ParseFrom(recoverPush.Payload.Span);

        Assert.False(recover.Replayed);
        Assert.Equal("map:1001:aoi:default.cell_1_0", recover.StreamId);
        Assert.NotNull(recover.Snapshot);
        Assert.Equal("map:1001:aoi:default.cell_1_0", recover.Snapshot.StreamId);
        Assert.Contains(recover.Snapshot.Entities, entity => entity.NetworkId == player.UnitId);

        Assert.True(pushDispatcher.TryCreateRecoverResponse(
            session,
            knownTick: 0,
            streamId: "map:1001:aoi:default.cell_9_0",
            requestId: 704,
            out var rejectedRecoverPush));
        var rejectedRecover = StateSyncRecoverResponse.Parser.ParseFrom(rejectedRecoverPush.Payload.Span);

        Assert.False(rejectedRecover.Replayed);
        Assert.Null(rejectedRecover.Snapshot);
        Assert.Empty(rejectedRecover.Deltas);
        Assert.Equal("map:1001:aoi:default.cell_9_0", rejectedRecover.StreamId);
        Assert.Equal("stream_not_observable", rejectedRecover.Reason);
    }

    [Fact]
    public void StateSync_push_dispatcher_uses_game_visibility_policy_for_aoi_recover()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 1 }));
        runtime.Start();
        var player = runtime.LoginPlayer("first", null, "session-a");
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(player.PlayerId, 15, 0, 0, 1, out _));
        var pushDispatcher = new GameStateSyncPushDispatcher(
            runtime,
            Options.Create(new GameOptions { StateSyncAoiCellSize = 10 }),
            new DenyAllAoiVisibilityPolicy());

        Assert.True(pushDispatcher.TryCreateRecoverResponse(
            session,
            knownTick: 0,
            streamId: "map:1001:aoi:default.cell_1_0",
            requestId: 705,
            out var recoverPush));
        var recover = StateSyncRecoverResponse.Parser.ParseFrom(recoverPush.Payload.Span);

        Assert.False(recover.Replayed);
        Assert.Null(recover.Snapshot);
        Assert.Empty(recover.Deltas);
        Assert.Equal("map:1001:aoi:default.cell_1_0", recover.StreamId);
        Assert.Equal("stream_not_observable", recover.Reason);
    }

    [Fact]
    public void Aoi_push_coordinator_reuses_published_frame_for_multiple_sessions_on_same_tick()
    {
        var options = Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = 1,
            StateSyncAoiCellSize = 10
        });
        using var runtime = new GameRuntime(options);
        runtime.Start();
        var first = runtime.LoginPlayer("first", null, "session-a");
        var second = runtime.LoginPlayer("second", null, "session-b");
        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out _));
        var dispatcher = new GameStateSyncPushDispatcher(runtime, options);
        var coordinator = new GameAoiStreamPushCoordinator(runtime, dispatcher, options);
        var firstSession = new YuoGatewaySession();
        firstSession.BindPlayer(first.PlayerId);
        var secondSession = new YuoGatewaySession();
        secondSession.BindPlayer(second.PlayerId);

        var firstPushes = coordinator.DrainPushes(firstSession);
        var secondPushes = coordinator.DrainPushes(secondSession);

        var firstDelta = Assert.Single(firstPushes
            .Where(static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta)
            .Select(static push => StateDeltaMessage.Parser.ParseFrom(push.Payload.Span)));
        var secondDelta = Assert.Single(secondPushes
            .Where(static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta)
            .Select(static push => StateDeltaMessage.Parser.ParseFrom(push.Payload.Span)));
        Assert.Equal(firstDelta.Tick, secondDelta.Tick);
        Assert.Equal(firstDelta.StreamId, secondDelta.StreamId);
        Assert.Equal("map:1001:aoi:default.cell_0_0", firstDelta.StreamId);
        Assert.True(dispatcher.TryGetReplayHistoryStats(firstDelta.StreamId, out var count, out var oldest, out var newest));
        Assert.Equal(1, count);
        Assert.Equal(firstDelta.Tick, oldest);
        Assert.Equal(firstDelta.Tick, newest);
    }

    [Fact]
    public void Aoi_push_coordinator_exposes_diagnostics_without_changing_push_semantics()
    {
        var options = Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = 1,
            StateSyncAoiCellSize = 10
        });
        using var runtime = new GameRuntime(options);
        runtime.Start();
        var first = runtime.LoginPlayer("first", null, "session-a");
        var second = runtime.LoginPlayer("second", null, "session-b");
        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out _));
        var dispatcher = new GameStateSyncPushDispatcher(runtime, options);
        var coordinator = new GameAoiStreamPushCoordinator(runtime, dispatcher, options);
        var firstSession = new YuoGatewaySession();
        firstSession.BindPlayer(first.PlayerId);
        var secondSession = new YuoGatewaySession();
        secondSession.BindPlayer(second.PlayerId);

        var firstPushes = coordinator.DrainPushes(firstSession);
        var secondPushes = coordinator.DrainPushes(secondSession);
        var diagnostics = coordinator.GetDiagnostics();

        Assert.Contains(firstPushes, static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);
        Assert.Contains(secondPushes, static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);
        Assert.Equal(2, diagnostics.SessionDrainCalls);
        Assert.Equal(1, diagnostics.FullSnapshotPasses);
        Assert.Equal(1, diagnostics.FramesPublished);
        Assert.True(diagnostics.DeltasPublished > 0);
        Assert.True(diagnostics.PayloadBytesPublished > 0);
        Assert.Equal(2, diagnostics.SessionDeltaPushesCreated);
        Assert.True(diagnostics.CachedFrameCount > 0);

        coordinator.Clear();

        diagnostics = coordinator.GetDiagnostics();
        Assert.Equal(0, diagnostics.SessionDrainCalls);
        Assert.Equal(0, diagnostics.FramesPublished);
        Assert.Equal(0, diagnostics.PayloadBytesPublished);
        Assert.Equal(0, diagnostics.CachedFrameCount);
    }

    [Fact]
    public void Aoi_push_coordinator_does_not_resend_same_frame_to_same_session()
    {
        var options = Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = 1,
            StateSyncAoiCellSize = 10
        });
        using var runtime = new GameRuntime(options);
        runtime.Start();
        var player = runtime.LoginPlayer("first", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        var dispatcher = new GameStateSyncPushDispatcher(runtime, options);
        var coordinator = new GameAoiStreamPushCoordinator(runtime, dispatcher, options);
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);

        Assert.Contains(
            coordinator.DrainPushes(session),
            static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);

        Assert.Empty(coordinator.DrainPushes(session));
    }

    [Fact]
    public void Runtime_records_aoi_dirty_entries_for_enter_move_and_disconnect()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 1 }));
        runtime.Start();
        var player = runtime.LoginPlayer("first", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(player.PlayerId, 5, 0, 0, 1, out _));
        Assert.True(runtime.TryDisconnect(player.PlayerId, "session-a"));

        var dirty = runtime.DrainAoiDirtyPlayers();

        Assert.True(dirty.Count >= 3);
        Assert.Contains(dirty, item => item.CurrentMapId == 1001);
        Assert.Contains(dirty, item => item.PreviousMapId == 1001 && Math.Abs(item.CurrentX - 5) < 0.001f);
        Assert.Empty(runtime.DrainAoiDirtyPlayers());
    }

    [Fact]
    public void Aoi_push_coordinator_filters_streams_by_player_visibility()
    {
        var options = Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = 1,
            StateSyncAoiCellSize = 10,
            StateSyncAoiRecoverRadiusCells = 1
        });
        using var runtime = new GameRuntime(options);
        runtime.Start();
        var first = runtime.LoginPlayer("first", null, "session-a");
        var second = runtime.LoginPlayer("second", null, "session-b");
        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(first.PlayerId, 5, 0, 0, 1, out _));
        Assert.True(runtime.TryMove(second.PlayerId, 25, 0, 0, 1, out _));
        var dispatcher = new GameStateSyncPushDispatcher(runtime, options);
        var coordinator = new GameAoiStreamPushCoordinator(runtime, dispatcher, options);
        var firstSession = new YuoGatewaySession();
        firstSession.BindPlayer(first.PlayerId);
        var secondSession = new YuoGatewaySession();
        secondSession.BindPlayer(second.PlayerId);

        var firstPushes = coordinator.DrainPushes(firstSession);
        var secondPushes = coordinator.DrainPushes(secondSession);

        var firstSubscription = StateSubscriptionMessage.Parser.ParseFrom(
            Assert.Single(firstPushes, static push => push.OpCode == YuoCoreProtocolOpCodes.StateSubscription).Payload.Span);
        var secondSubscription = StateSubscriptionMessage.Parser.ParseFrom(
            Assert.Single(secondPushes, static push => push.OpCode == YuoCoreProtocolOpCodes.StateSubscription).Payload.Span);
        Assert.Contains("map:1001:aoi:default.cell_0_0", firstSubscription.StreamIds);
        Assert.DoesNotContain("map:1001:aoi:default.cell_2_0", firstSubscription.StreamIds);
        Assert.Contains("map:1001:aoi:default.cell_2_0", secondSubscription.StreamIds);
        Assert.DoesNotContain("map:1001:aoi:default.cell_0_0", secondSubscription.StreamIds);
        Assert.All(
            firstPushes
                .Where(static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta)
                .Select(static push => StateDeltaMessage.Parser.ParseFrom(push.Payload.Span)),
            delta => Assert.NotEqual("map:1001:aoi:default.cell_2_0", delta.StreamId));
        Assert.All(
            secondPushes
                .Where(static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta)
                .Select(static push => StateDeltaMessage.Parser.ParseFrom(push.Payload.Span)),
            delta => Assert.NotEqual("map:1001:aoi:default.cell_0_0", delta.StreamId));
    }

    [Fact]
    public void Aoi_push_coordinator_skips_unchanged_stream_without_breaking_next_base_tick()
    {
        var options = Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = 1,
            StateSyncAoiCellSize = 10
        });
        using var runtime = new GameRuntime(options);
        runtime.Start();
        var player = runtime.LoginPlayer("first", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(player.PlayerId, 5, 0, 0, 1, out _));
        var dispatcher = new GameStateSyncPushDispatcher(runtime, options);
        var coordinator = new GameAoiStreamPushCoordinator(runtime, dispatcher, options);
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);

        var initialDelta = StateDeltaMessage.Parser.ParseFrom(
            Assert.Single(
                coordinator.DrainPushes(session),
                static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta).Payload.Span);
        runtime.Tick();

        Assert.DoesNotContain(
            coordinator.DrainPushes(session),
            static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);

        Assert.True(runtime.TryMove(player.PlayerId, 6, 0, 0, 2, out _));
        var movedDelta = StateDeltaMessage.Parser.ParseFrom(
            Assert.Single(
                coordinator.DrainPushes(session),
                static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta).Payload.Span);

        Assert.Equal(initialDelta.Tick, movedDelta.BaseTick);
        Assert.True(movedDelta.Tick > initialDelta.Tick);
        Assert.Contains(movedDelta.Changes, change =>
            change.Kind == StateChangeKind.Update &&
            change.NetworkId == player.UnitId);
    }

    [Fact]
    public void Aoi_push_coordinator_updates_only_dirty_streams_after_initial_frame()
    {
        var options = Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = 1,
            StateSyncAoiCellSize = 10,
            StateSyncAoiRecoverRadiusCells = 1
        });
        using var runtime = new GameRuntime(options);
        runtime.Start();
        var first = runtime.LoginPlayer("first", null, "session-a");
        var second = runtime.LoginPlayer("second", null, "session-b");
        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(first.PlayerId, 5, 0, 0, 1, out _));
        Assert.True(runtime.TryMove(second.PlayerId, 25, 0, 0, 1, out _));
        var dispatcher = new GameStateSyncPushDispatcher(runtime, options);
        var coordinator = new GameAoiStreamPushCoordinator(runtime, dispatcher, options);
        var firstSession = new YuoGatewaySession();
        firstSession.BindPlayer(first.PlayerId);
        var secondSession = new YuoGatewaySession();
        secondSession.BindPlayer(second.PlayerId);

        _ = coordinator.DrainPushes(firstSession);
        var secondInitial = Assert.Single(
            coordinator.DrainPushes(secondSession),
            static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);
        var secondInitialDelta = StateDeltaMessage.Parser.ParseFrom(secondInitial.Payload.Span);
        Assert.Equal("map:1001:aoi:default.cell_2_0", secondInitialDelta.StreamId);
        Assert.True(dispatcher.TryGetReplayHistoryStats(secondInitialDelta.StreamId, out var initialCount, out _, out _));

        Assert.True(runtime.TryMove(first.PlayerId, 6, 0, 0, 2, out _));
        var firstMoved = Assert.Single(
            coordinator.DrainPushes(firstSession),
            static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);
        var firstMovedDelta = StateDeltaMessage.Parser.ParseFrom(firstMoved.Payload.Span);

        Assert.Equal("map:1001:aoi:default.cell_0_0", firstMovedDelta.StreamId);
        Assert.DoesNotContain(
            coordinator.DrainPushes(secondSession),
            static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);
        Assert.True(dispatcher.TryGetReplayHistoryStats(secondInitialDelta.StreamId, out var afterMoveCount, out _, out _));
        Assert.Equal(initialCount, afterMoveCount);
    }

    [Fact]
    public async Task Gateway_bridge_v1_state_snapshot_request_returns_current_snapshot()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.LoginPlayer("observer", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher, pushDispatcher));
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);

        var outbound = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(
                1,
                YuoCoreProtocolOpCodes.StateSnapshotRequest,
                501,
                1,
                new StateSnapshotRequest
                {
                    PlayerId = player.PlayerId.ToString(),
                    KnownTick = 0
                }.ToByteArray()));

        Assert.Null(outbound.Error);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSnapshot, outbound.OpCode);
        Assert.Equal(501UL, outbound.RequestId);
        Assert.True(outbound.ServerSeq > 0);
        var snapshot = StateSnapshotMessage.Parser.ParseFrom(outbound.Payload.Span);
        Assert.Equal((long)outbound.ServerSeq, snapshot.Tick);
        Assert.Contains(snapshot.Entities, entity => entity.NetworkId == player.UnitId);
    }

    [Fact]
    public async Task Gateway_bridge_v1_state_recover_replays_delta_history_for_reconnected_session()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var mover = runtime.LoginPlayer("mover", null, "session-a");
        var observer = runtime.LoginPlayer("observer", null, "session-b");
        Assert.True(runtime.TryEnterMap(mover.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(observer.PlayerId, 1001, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher, pushDispatcher));
        var oldSession = new YuoGatewaySession();
        oldSession.BindPlayer(observer.PlayerId);

        var initial = Assert.Single(pushDispatcher.DrainPushes(oldSession));
        var snapshot = StateSnapshotMessage.Parser.ParseFrom(initial.Payload.Span);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.StreamId));

        Assert.True(runtime.TryMove(mover.PlayerId, 3, 0, 4, 1, out _));
        var liveDeltaPush = Assert.Single(pushDispatcher.DrainPushes(oldSession));
        var liveDelta = StateDeltaMessage.Parser.ParseFrom(liveDeltaPush.Payload.Span);
        Assert.Equal(snapshot.Tick, liveDelta.BaseTick);
        Assert.Equal(snapshot.StreamId, liveDelta.StreamId);

        var reconnectedSession = new YuoGatewaySession();
        reconnectedSession.BindPlayer(observer.PlayerId);
        var outbound = await DispatchV1Async(
            pipeline,
            reconnectedSession,
            YuoCoreProtocolOpCodes.StateSyncRecoverRequest,
            601,
            1,
            new StateSyncRecoverRequest
            {
                PlayerId = observer.PlayerId.ToString(),
                KnownTick = snapshot.Tick
            });

        Assert.Null(outbound.Error);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSyncRecover, outbound.OpCode);
        Assert.Equal(601UL, outbound.RequestId);
        var recover = StateSyncRecoverResponse.Parser.ParseFrom(outbound.Payload.Span);
        Assert.Equal(snapshot.StreamId, recover.StreamId);
        Assert.True(recover.Replayed);
        Assert.Null(recover.Snapshot);
        var replayedDelta = Assert.Single(recover.Deltas);
        Assert.Equal(liveDelta.Tick, replayedDelta.Tick);
        Assert.Equal(snapshot.Tick, replayedDelta.BaseTick);
        Assert.Equal(snapshot.StreamId, replayedDelta.StreamId);
        Assert.Equal(mover.UnitId, Assert.Single(replayedDelta.Changes).NetworkId);
    }

    [Fact]
    public async Task Gateway_bridge_v1_state_recover_falls_back_to_snapshot_when_history_is_missing()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.LoginPlayer("observer", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher, pushDispatcher));
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);

        var outbound = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.StateSyncRecoverRequest,
            602,
            1,
            new StateSyncRecoverRequest
            {
                PlayerId = player.PlayerId.ToString(),
                KnownTick = 0
            });

        Assert.Null(outbound.Error);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSyncRecover, outbound.OpCode);
        var recover = StateSyncRecoverResponse.Parser.ParseFrom(outbound.Payload.Span);
        Assert.False(recover.Replayed);
        Assert.NotNull(recover.Snapshot);
        Assert.Equal(recover.StreamId, recover.Snapshot.StreamId);
        Assert.Empty(recover.Deltas);
        Assert.Contains(recover.Snapshot.Entities, entity => entity.NetworkId == player.UnitId);
    }

    [Fact]
    public async Task Gateway_bridge_v1_state_ack_prunes_replay_history_when_all_active_sessions_ack()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var mover = runtime.LoginPlayer("mover", null, "session-a");
        var observer = runtime.LoginPlayer("observer", null, "session-b");
        Assert.True(runtime.TryEnterMap(mover.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(observer.PlayerId, 1001, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher, pushDispatcher));
        var session = new YuoGatewaySession();
        session.BindPlayer(observer.PlayerId);

        var snapshotPush = Assert.Single(pushDispatcher.DrainPushes(session));
        var snapshot = StateSnapshotMessage.Parser.ParseFrom(snapshotPush.Payload.Span);
        Assert.True(runtime.TryMove(mover.PlayerId, 3, 0, 4, 1, out _));
        var deltaPush = Assert.Single(pushDispatcher.DrainPushes(session));
        var delta = StateDeltaMessage.Parser.ParseFrom(deltaPush.Payload.Span);

        Assert.True(pushDispatcher.TryGetReplayHistoryStats(observer.PlayerId, out var count, out _, out _));
        Assert.Equal(1, count);

        var ack = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.StateSyncAckRequest,
            606,
            1,
            new StateSyncAckRequest
            {
                PlayerId = observer.PlayerId.ToString(),
                AckTick = delta.Tick,
                StreamId = delta.StreamId
            });
        var ackResponse = StateSyncAckResponse.Parser.ParseFrom(ack.Payload.Span);

        Assert.True(ackResponse.Accepted);
        Assert.Equal(delta.StreamId, ackResponse.StreamId);
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(observer.PlayerId, out count, out var oldest, out var newest));
        Assert.Equal(0, count);
        Assert.Null(oldest);
        Assert.Null(newest);

        var reconnected = new YuoGatewaySession();
        reconnected.BindPlayer(observer.PlayerId);
        var recover = await DispatchV1Async(
            pipeline,
            reconnected,
            YuoCoreProtocolOpCodes.StateSyncRecoverRequest,
            607,
            1,
            new StateSyncRecoverRequest
            {
                PlayerId = observer.PlayerId.ToString(),
                KnownTick = snapshot.Tick,
                StreamId = snapshot.StreamId
            });
        var recoverResponse = StateSyncRecoverResponse.Parser.ParseFrom(recover.Payload.Span);

        Assert.False(recoverResponse.Replayed);
        Assert.NotNull(recoverResponse.Snapshot);
    }

    [Fact]
    public async Task Gateway_bridge_v1_state_ack_prunes_only_matching_stream_history()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var mover = runtime.LoginPlayer("mover", null, "session-a");
        var firstObserver = runtime.LoginPlayer("observer-a", null, "session-b");
        var secondObserver = runtime.LoginPlayer("observer-b", null, "session-c");
        Assert.True(runtime.TryEnterMap(mover.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(firstObserver.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(secondObserver.PlayerId, 1001, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher, pushDispatcher));
        var firstSession = new YuoGatewaySession();
        firstSession.BindPlayer(firstObserver.PlayerId);
        var secondSession = new YuoGatewaySession();
        secondSession.BindPlayer(secondObserver.PlayerId);

        _ = pushDispatcher.DrainPushes(firstSession);
        _ = pushDispatcher.DrainPushes(secondSession);
        Assert.True(runtime.TryMove(mover.PlayerId, 3, 0, 4, 1, out _));
        var firstDelta = LastStateDelta(pushDispatcher.DrainPushes(firstSession));
        var secondDelta = LastStateDelta(pushDispatcher.DrainPushes(secondSession));

        Assert.NotEqual(firstDelta.StreamId, secondDelta.StreamId);
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(firstDelta.StreamId, out var firstCount, out _, out _));
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(secondDelta.StreamId, out var secondCount, out _, out _));
        Assert.True(firstCount > 0);
        Assert.True(secondCount > 0);
        var secondCountBeforeAck = secondCount;

        var ack = await DispatchV1Async(
            pipeline,
            firstSession,
            YuoCoreProtocolOpCodes.StateSyncAckRequest,
            608,
            1,
            new StateSyncAckRequest
            {
                PlayerId = firstObserver.PlayerId.ToString(),
                AckTick = firstDelta.Tick,
                StreamId = firstDelta.StreamId
            });
        var ackResponse = StateSyncAckResponse.Parser.ParseFrom(ack.Payload.Span);

        Assert.True(ackResponse.Accepted);
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(firstDelta.StreamId, out firstCount, out _, out _));
        Assert.True(pushDispatcher.TryGetReplayHistoryStats(secondDelta.StreamId, out secondCount, out _, out _));
        Assert.Equal(0, firstCount);
        Assert.Equal(secondCountBeforeAck, secondCount);
    }

    [Fact]
    public async Task Gateway_bridge_v1_state_ack_records_monotonic_session_ack()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.LoginPlayer("observer", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher, pushDispatcher));
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);

        var snapshotPush = Assert.Single(pushDispatcher.DrainPushes(session));
        var snapshot = StateSnapshotMessage.Parser.ParseFrom(snapshotPush.Payload.Span);
        var ack = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.StateSyncAckRequest,
            603,
            1,
            new StateSyncAckRequest
            {
                PlayerId = player.PlayerId.ToString(),
                AckTick = snapshot.Tick,
                StreamId = snapshot.StreamId
            });
        var ackResponse = StateSyncAckResponse.Parser.ParseFrom(ack.Payload.Span);

        Assert.Null(ack.Error);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSyncAck, ack.OpCode);
        Assert.True(ackResponse.Accepted);
        Assert.Equal(snapshot.StreamId, ackResponse.StreamId);
        Assert.Equal(snapshot.Tick, ackResponse.AckTick);
        Assert.True(pushDispatcher.TryGetAcknowledgedTick(session.SessionId, out var acknowledged));
        Assert.Equal(snapshot.Tick, acknowledged);

        var olderAck = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.StateSyncAckRequest,
            604,
            2,
            new StateSyncAckRequest
            {
                PlayerId = player.PlayerId.ToString(),
                AckTick = snapshot.Tick - 1,
                StreamId = snapshot.StreamId
            });
        _ = StateSyncAckResponse.Parser.ParseFrom(olderAck.Payload.Span);

        Assert.True(pushDispatcher.TryGetAcknowledgedTick(session.SessionId, out acknowledged));
        Assert.Equal(snapshot.Tick, acknowledged);
    }

    [Fact]
    public async Task Gateway_bridge_v1_state_ack_rejects_tick_newer_than_server_stream()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.LoginPlayer("observer", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pushDispatcher = new GameStateSyncPushDispatcher(runtime);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher, pushDispatcher));
        var session = new YuoGatewaySession();
        session.BindPlayer(player.PlayerId);

        var snapshotPush = Assert.Single(pushDispatcher.DrainPushes(session));
        var snapshot = StateSnapshotMessage.Parser.ParseFrom(snapshotPush.Payload.Span);
        var ack = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.StateSyncAckRequest,
            605,
            1,
            new StateSyncAckRequest
            {
                PlayerId = player.PlayerId.ToString(),
                AckTick = snapshot.Tick + 100,
                StreamId = snapshot.StreamId
            });
        var ackResponse = StateSyncAckResponse.Parser.ParseFrom(ack.Payload.Span);

        Assert.Null(ack.Error);
        Assert.False(ackResponse.Accepted);
        Assert.Equal("ack_tick_is_newer_than_server_stream", ackResponse.Reason);
        Assert.False(pushDispatcher.TryGetAcknowledgedTick(session.SessionId, out _));
    }

    [Fact]
    public async Task Hello_restores_existing_player_snapshot_before_login()
    {
        var store = new InMemoryPlayerSnapshotStore();
        var persistence = new SnapshotPersistenceService(store, store);
        var playerId = Guid.NewGuid();
        await store.SaveAsync(new YuoPlayerSnapshotDto(
            playerId,
            "stored",
            10,
            20,
            2,
            3,
            4,
            DateTimeOffset.UtcNow,
            MapId: 1001,
            UnitId: 2002,
            X: 5,
            Y: 0,
            Z: 6,
            Online: false,
            LastInputSeq: 9));

        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())),
            persistence);

        var response = await dispatcher.DispatchAsync(new ClientEnvelope
        {
            RequestId = 401,
            Hello = new HelloRequest { PlayerId = playerId.ToString() }
        }, "session-restored");

        Assert.Equal(ServerEnvelope.PayloadOneofCase.Welcome, response.PayloadCase);
        Assert.True(runtime.TryGetSnapshot(playerId, out var live));
        Assert.True(live.Online);
        Assert.Equal(1001, live.MapId);
        Assert.Equal(2002, live.UnitId);
        Assert.Equal(5, live.X);
        Assert.Equal(6, live.Z);
        Assert.Equal(9UL, live.LastInputSeq);
    }

    [Fact]
    public async Task Disconnect_persists_offline_live_snapshot()
    {
        var store = new InMemoryPlayerSnapshotStore();
        var persistence = new SnapshotPersistenceService(store, store);
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.LoginPlayer("disconnect-save", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(player.PlayerId, 7, 0, 8, 1, out _));

        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())),
            persistence);

        var response = await dispatcher.DispatchAsync(new ClientEnvelope
        {
            RequestId = 402,
            Disconnect = new DisconnectRequest
            {
                PlayerId = player.PlayerId.ToString(),
                SessionId = "session-a"
            }
        }, "session-a");

        Assert.Equal(ServerEnvelope.PayloadOneofCase.Snapshot, response.PayloadCase);
        var saved = await store.GetAsync(player.PlayerId);
        Assert.NotNull(saved);
        Assert.False(saved.Online);
        Assert.Equal(1001, saved.MapId);
        Assert.Equal(7, saved.X);
        Assert.Equal(8, saved.Z);
        Assert.Equal(1UL, saved.LastInputSeq);
    }

    [Fact]
    public async Task Gateway_bridge_v1_loop_persists_disconnect_and_restores_on_new_runtime()
    {
        var store = new InMemoryPlayerSnapshotStore();
        var persistence = new SnapshotPersistenceService(store, store);
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())),
            persistence);
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));
        var session = new YuoGatewaySession();

        var hello = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.HelloRequest,
            901,
            1,
            new HelloRequest { PlayerId = string.Empty });
        var welcome = Welcome.Parser.ParseFrom(hello.Payload.Span);
        var playerId = Guid.Parse(welcome.PlayerId);

        var enter = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.EnterMapRequest,
            902,
            2,
            new EnterMapRequest { PlayerId = welcome.PlayerId, MapId = 1001 });
        Assert.Equal(YuoCoreProtocolOpCodes.WorldSnapshot, enter.OpCode);

        var move = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.MoveRequest,
            903,
            3,
            new MoveRequest { PlayerId = welcome.PlayerId, InputSeq = 7, X = 12, Y = 0, Z = 34 });
        var moved = WorldSnapshot.Parser.ParseFrom(move.Payload.Span);
        Assert.Contains(moved.Players, player =>
            player.PlayerId == welcome.PlayerId &&
            Math.Abs(player.X - 12) < 0.001f &&
            Math.Abs(player.Z - 34) < 0.001f);

        var disconnect = await DispatchV1Async(
            pipeline,
            session,
            YuoCoreProtocolOpCodes.DisconnectRequest,
            904,
            4,
            new DisconnectRequest
            {
                PlayerId = welcome.PlayerId,
                SessionId = session.SessionId.ToString("N")
            });
        var disconnected = PlayerSnapshot.Parser.ParseFrom(disconnect.Payload.Span);
        var saved = await store.GetAsync(playerId);

        Assert.Equal(YuoCoreProtocolOpCodes.PlayerSnapshot, disconnect.OpCode);
        Assert.False(disconnected.Online);
        Assert.NotNull(saved);
        Assert.False(saved.Online);
        Assert.Equal(1001, saved.MapId);
        Assert.Equal(12, saved.X);
        Assert.Equal(34, saved.Z);
        Assert.Equal(7UL, saved.LastInputSeq);

        using var restoredRuntime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        restoredRuntime.Start();
        var restoredDispatcher = new GameClientMessageDispatcher(
            restoredRuntime,
            new GameCommandQueue(Options.Create(new GameOptions())),
            persistence);
        var restoredPipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(restoredDispatcher));
        var restoredSession = new YuoGatewaySession();

        var restoredHello = await DispatchV1Async(
            restoredPipeline,
            restoredSession,
            YuoCoreProtocolOpCodes.HelloRequest,
            905,
            1,
            new HelloRequest { PlayerId = welcome.PlayerId });
        var restoredWelcome = Welcome.Parser.ParseFrom(restoredHello.Payload.Span);

        Assert.Equal(welcome.PlayerId, restoredWelcome.PlayerId);
        Assert.True(restoredRuntime.TryGetSnapshot(playerId, out var live));
        Assert.True(live.Online);
        Assert.Equal(1001, live.MapId);
        Assert.Equal(12, live.X);
        Assert.Equal(34, live.Z);
        Assert.Equal(7UL, live.LastInputSeq);
    }

    [Fact]
    public void Player_snapshot_persistence_codec_round_trips_snapshot()
    {
        var snapshot = new YuoPlayerSnapshotDto(
            Guid.NewGuid(),
            "codec",
            1,
            2,
            3,
            4,
            5,
            DateTimeOffset.UtcNow,
            MapId: 1001,
            UnitId: 2002,
            X: 1,
            Y: 2,
            Z: 3,
            Online: true,
            LastInputSeq: 9);

        var record = PlayerSnapshotPersistenceCodec.ToRecord(snapshot);
        var restored = PlayerSnapshotPersistenceCodec.FromRecord(record);

        Assert.Equal("player", record.Kind);
        Assert.Equal(snapshot.PlayerId.ToString("N"), record.Id);
        Assert.Equal(snapshot, restored);
    }

    [Fact]
    public async Task Snapshot_persistence_service_can_enqueue_durable_snapshot_command()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"yuocore-outbox-{Guid.NewGuid():N}");
        try
        {
            var queue = new YuoPersistenceQueue(capacity: 16);
            var outbox = new YuoFilePersistenceOutbox(tempDir, "test");
            var durable = new YuoDurablePersistenceQueue(queue, outbox);
            var store = new InMemoryPlayerSnapshotStore();
            var repository = new CountingPlayerSnapshotRepository(store);
            var persistence = new SnapshotPersistenceService(repository, store, durable);
            var snapshot = new YuoPlayerSnapshotDto(
                Guid.NewGuid(),
                "queued",
                10,
                20,
                2,
                3,
                4,
                DateTimeOffset.UtcNow,
                MapId: 1001);

            await persistence.SaveAsync(snapshot);

            Assert.Equal(0, repository.SaveCount);
            Assert.NotNull(await store.GetAsync(snapshot.PlayerId));
            var command = await queue.DequeueAsync();
            var outboxCommand = Assert.IsType<YuoOutboxPersistenceCommand>(command);
            var save = Assert.IsType<SaveSnapshotCommand>(outboxCommand.Inner);
            var restored = PlayerSnapshotPersistenceCodec.FromRecord(save.Snapshot);
            Assert.Equal(snapshot.PlayerId, restored.PlayerId);
            Assert.Equal(snapshot.MapId, restored.MapId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void WebSocket_endpoint_decodes_v1_client_envelope()
    {
        var envelope = new YuoClientEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.PingRequest,
            RequestId = 90,
            ClientSeq = 11,
            Payload = ByteString.CopyFrom(new PingRequest { UnixMs = 123 }.ToByteArray())
        };

        var decoded = GameWebSocketEndpoint.DecodeClientMessage(envelope.ToByteArray());

        Assert.True(decoded.UsesEnvelopeV1);
        Assert.Equal(1U, decoded.Inbound.Version);
        Assert.Equal(YuoCoreProtocolOpCodes.PingRequest, decoded.Inbound.OpCode);
        Assert.Equal(90UL, decoded.Inbound.RequestId);
        Assert.Equal(11UL, decoded.Inbound.ClientSeq);
        Assert.Equal(123, PingRequest.Parser.ParseFrom(decoded.Inbound.Payload.Span).UnixMs);
    }

    [Fact]
    public void WebSocket_endpoint_encodes_v1_server_envelope()
    {
        var outbound = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.PongResponse,
            90,
            12,
            new PongResponse { UnixMs = 456 }.ToByteArray());

        var bytes = GameWebSocketEndpoint.EncodeServerMessage(outbound, useEnvelopeV1: true);
        var envelope = YuoServerEnvelopeV1.Parser.ParseFrom(bytes);

        Assert.Equal(1U, envelope.Version);
        Assert.Equal((uint)YuoCoreProtocolOpCodes.PongResponse, envelope.Opcode);
        Assert.Equal(90UL, envelope.RequestId);
        Assert.Equal(12UL, envelope.ServerSeq);
        Assert.Null(envelope.Error);
        Assert.Equal(456, PongResponse.Parser.ParseFrom(envelope.Payload).UnixMs);
    }

    [Fact]
    public void WebSocket_endpoint_rents_v1_server_envelope_without_losing_fields()
    {
        var outbound = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.PongResponse,
            90,
            12,
            new PongResponse { UnixMs = 456 }.ToByteArray());

        using var encoded = GameWebSocketEndpoint.RentServerMessage(outbound, useEnvelopeV1: true);
        var envelope = YuoServerEnvelopeV1.Parser.ParseFrom(encoded.Memory.Span);

        Assert.True(encoded.Length > 0);
        Assert.Equal(1U, envelope.Version);
        Assert.Equal((uint)YuoCoreProtocolOpCodes.PongResponse, envelope.Opcode);
        Assert.Equal(90UL, envelope.RequestId);
        Assert.Equal(12UL, envelope.ServerSeq);
        Assert.Null(envelope.Error);
        Assert.Equal(456, PongResponse.Parser.ParseFrom(envelope.Payload).UnixMs);
    }

    [Fact]
    public void WebSocket_endpoint_rented_v1_error_envelope_keeps_error_contract()
    {
        var outbound = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.PongResponse,
            90,
            12,
            ReadOnlyMemory<byte>.Empty,
            new YuoError("failed", "bad payload"));

        using var encoded = GameWebSocketEndpoint.RentServerMessage(outbound, useEnvelopeV1: true);
        var envelope = YuoServerEnvelopeV1.Parser.ParseFrom(encoded.Memory.Span);

        Assert.Equal(1U, envelope.Version);
        Assert.Equal((uint)YuoCoreProtocolOpCodes.ErrorResponse, envelope.Opcode);
        Assert.Equal(90UL, envelope.RequestId);
        Assert.Equal(12UL, envelope.ServerSeq);
        Assert.NotNull(envelope.Error);
        Assert.Equal("failed", envelope.Error.Code);
        Assert.Equal("bad payload", envelope.Error.Message);
        Assert.True(envelope.Payload.IsEmpty);
    }

    [Fact]
    public void WebSocket_endpoint_rented_legacy_message_keeps_legacy_envelope()
    {
        var legacy = new ServerEnvelope
        {
            RequestId = 90,
            Pong = new PongResponse { UnixMs = 456 }
        };
        var outbound = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.PongResponse,
            90,
            12,
            legacy.ToByteArray());

        using var encoded = GameWebSocketEndpoint.RentServerMessage(outbound, useEnvelopeV1: false);
        var envelope = ServerEnvelope.Parser.ParseFrom(encoded.Memory.Span);

        Assert.Equal(90UL, envelope.RequestId);
        Assert.Equal(ServerEnvelope.PayloadOneofCase.Pong, envelope.PayloadCase);
        Assert.Equal(456, envelope.Pong.UnixMs);
    }

    [Fact]
    public async Task WebSocket_push_pump_closes_slow_client_when_queue_overflows()
    {
        var pump = new GameWebSocketPushPump(new GameWebSocketPushOptions
        {
            PollIntervalMs = 1,
            MaxQueuedMessages = 1,
            SendTimeoutMs = 5_000
        }, NullLogger.Instance);
        var session = new YuoGatewaySession();
        var connection = new BlockingClientConnection();
        var closeReasons = new List<string>();
        var drained = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await pump.RunAsync(
            connection,
            session,
            (_, destination) =>
            {
                if (drained)
                {
                    return 0;
                }

                drained = true;
                destination.Add(CreatePush(1));
                destination.Add(CreatePush(2));
                destination.Add(CreatePush(3));
                return 3;
            },
            () => true,
            (reason, _) =>
            {
                closeReasons.Add(reason);
                return ValueTask.CompletedTask;
            },
            cts.Token);

        var reason = Assert.Single(closeReasons);
        Assert.Contains("queue overflow", reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(drained);
    }

    [Fact]
    public async Task WebSocket_push_pump_closes_slow_client_when_send_times_out()
    {
        var pump = new GameWebSocketPushPump(new GameWebSocketPushOptions
        {
            PollIntervalMs = 1,
            MaxQueuedMessages = 8,
            SendTimeoutMs = 10
        }, NullLogger.Instance);
        var session = new YuoGatewaySession();
        var connection = new BlockingClientConnection();
        var closeReasons = new List<string>();
        var drained = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await pump.RunAsync(
            connection,
            session,
            (_, destination) =>
            {
                if (drained)
                {
                    return 0;
                }

                drained = true;
                destination.Add(CreatePush(1));
                return 1;
            },
            () => true,
            (reason, _) =>
            {
                closeReasons.Add(reason);
                return ValueTask.CompletedTask;
            },
            cts.Token);

        var reason = Assert.Single(closeReasons);
        Assert.Contains("send timeout", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, connection.SendAttempts);
    }

    [Fact]
    public async Task WebSocket_v1_bad_payload_returns_v1_error_envelope()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));
        var request = new YuoClientEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.ClickRequest,
            RequestId = 91,
            ClientSeq = 1,
            Payload = ByteString.CopyFrom([0xff, 0x01])
        };

        var decoded = GameWebSocketEndpoint.DecodeClientMessage(request.ToByteArray());
        var outbound = await pipeline.DispatchAsync(new YuoGatewaySession(), decoded.Inbound);
        var responseBytes = GameWebSocketEndpoint.EncodeServerMessage(outbound, decoded.UsesEnvelopeV1);
        var response = YuoServerEnvelopeV1.Parser.ParseFrom(responseBytes);

        Assert.True(decoded.UsesEnvelopeV1);
        Assert.Equal(1U, response.Version);
        Assert.Equal((uint)YuoCoreProtocolOpCodes.ErrorResponse, response.Opcode);
        Assert.Equal(91UL, response.RequestId);
        Assert.Equal(1UL, response.ServerSeq);
        Assert.NotNull(response.Error);
        Assert.Equal("failed", response.Error.Code);
        Assert.True(response.Payload.IsEmpty);
    }

    [Fact]
    public async Task Gateway_bridge_turns_bad_protobuf_into_pipeline_error()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var pipeline = new YuoGatewayDispatchPipeline(new GameGatewayProtobufDispatcher(dispatcher));

        var outbound = await pipeline.DispatchAsync(
            new YuoGatewaySession(),
            new YuoGatewayInboundMessage(1, 0, 99, 0, new byte[] { 0xff, 0x01 }));

        Assert.NotNull(outbound.Error);
        Assert.Equal("failed", outbound.Error!.Code);
        Assert.Equal(99UL, outbound.RequestId);
        Assert.Equal(1UL, outbound.ServerSeq);
        Assert.True(outbound.Payload.IsEmpty);
    }

    [Fact]
    public async Task Game_dispatcher_honors_cancellation_before_creating_player()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await dispatcher.DispatchAsync(new ClientEnvelope
            {
                RequestId = 123,
                Hello = new HelloRequest { PlayerId = string.Empty }
            }, cts.Token));

        Assert.Equal(0, runtime.Stats().Players);
    }

    [Fact]
    public async Task Gateway_bridge_honors_cancellation_before_parsing_bad_protobuf()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        runtime.Start();
        var dispatcher = new GameClientMessageDispatcher(
            runtime,
            new GameCommandQueue(Options.Create(new GameOptions())));
        var bridge = new GameGatewayProtobufDispatcher(dispatcher);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await bridge.DispatchAsync(
                new YuoGatewaySession(),
                new YuoGatewayInboundMessage(1, 0, 124, 0, new byte[] { 0xff, 0x01 }),
                cts.Token));
    }

    private sealed class InMemoryPlayerSnapshotStore : IPlayerSnapshotRepository, IPlayerSnapshotCache
    {
        private readonly Dictionary<Guid, YuoPlayerSnapshotDto> _snapshots = new();

        public Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            _snapshots[snapshot.PlayerId] = snapshot;
            return Task.CompletedTask;
        }

        public Task SetAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            _snapshots[snapshot.PlayerId] = snapshot;
            return Task.CompletedTask;
        }

        public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
        {
            _snapshots.TryGetValue(playerId, out var snapshot);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class CountingPlayerSnapshotRepository(IPlayerSnapshotRepository inner) : IPlayerSnapshotRepository
    {
        public int SaveCount { get; private set; }

        public async Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            await inner.SaveAsync(snapshot, cancellationToken);
        }

        public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
        {
            return inner.GetAsync(playerId, cancellationToken);
        }
    }

    private static ValueTask<YuoGatewayOutboundMessage> DispatchV1Async(
        YuoGatewayDispatchPipeline pipeline,
        YuoGatewaySession session,
        int opCode,
        ulong requestId,
        ulong clientSeq,
        IMessage payload)
    {
        return pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(
                1,
                opCode,
                requestId,
                clientSeq,
                payload.ToByteArray()));
    }

    private static YuoGatewayOutboundMessage CreatePush(ulong seq)
    {
        return new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.StateDelta,
            0,
            seq,
            new StateDeltaMessage { Tick = checked((long)seq) }.ToByteArray());
    }

    private static StateDeltaMessage LastStateDelta(IReadOnlyList<YuoGatewayOutboundMessage> messages)
    {
        var message = messages.LastOrDefault(static item => item.OpCode == YuoCoreProtocolOpCodes.StateDelta);
        Assert.Equal(YuoCoreProtocolOpCodes.StateDelta, message.OpCode);
        return StateDeltaMessage.Parser.ParseFrom(message.Payload.Span);
    }

    private sealed class DenyAllAoiVisibilityPolicy : IGameAoiVisibilityPolicy
    {
        public bool CanRecoverStream(GameAoiVisibilityContext context)
        {
            return false;
        }
    }

    private sealed class BlockingClientConnection : IYuoClientConnection
    {
        public Guid ConnectionId { get; } = Guid.NewGuid();

        public int SendAttempts { get; private set; }

        public async ValueTask SendAsync(YuoGatewayOutboundMessage message, CancellationToken cancellationToken = default)
        {
            SendAttempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
