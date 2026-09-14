using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Ecs;
using YuoCore.Game;
using YuoCore.StateSync;

namespace YuoCore.Backend.Tests;

public sealed class GameRuntimeTests
{
    [Fact]
    public async Task Persist_player_command_saves_online_snapshot()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.LoginPlayer("persist-target", null, "session-p");
        var repository = new RecordingSnapshotRepository();
        var processor = new GameCommandProcessor(
            runtime,
            new SnapshotPersistenceService(repository, new NoopPlayerSnapshotCache()));

        var result = await processor.ApplyAsync(new YuoPersistPlayerCommand(player.PlayerId));

        Assert.True(result.Success);
        var saved = Assert.IsType<YuoPlayerSnapshotDto>(repository.LastSaved);
        Assert.Equal(player.PlayerId, saved.PlayerId);
    }

    [Fact]
    public async Task Persist_player_command_without_persistence_reports_unsupported()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.CreatePlayer("persist-unsupported");
        var processor = new GameCommandProcessor(runtime);

        var result = await processor.ApplyAsync(new YuoPersistPlayerCommand(player.PlayerId));

        Assert.False(result.Success);
        Assert.Equal("unsupported_command", result.ErrorCode);
    }

    [Fact]
    public void Shard_selection_is_deterministic_across_processes()
    {
        var playerId = Guid.Parse("12345678-1234-1234-1234-1234567890ab");

        Assert.Equal(
            GameRuntime.SelectShardIndex(playerId, 4),
            (int)(IDGenerate.GetID(playerId.ToString("N")) % 4));
        Assert.InRange(GameRuntime.SelectShardIndex(playerId, 7), 0, 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameRuntime.SelectShardIndex(playerId, 0));
    }

    [Fact]
    public void Runtime_can_seed_10k_players_and_tick()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        runtime.Start();

        runtime.SeedPlayers(10_000);
        for (var i = 0; i < 10; i++) runtime.Tick();

        var stats = runtime.Stats();
        Assert.Equal(10_000, stats.Players);
        Assert.True(stats.TickCount >= 10);
    }

    [Fact]
    public void Click_command_changes_snapshot()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        runtime.Start();
        var player = runtime.CreatePlayer("test");

        Assert.True(runtime.TryApplyClick(player.PlayerId, 5));
        Assert.True(runtime.TryGetSnapshot(player.PlayerId, out var snapshot));
        Assert.True(snapshot.Gold > player.Gold);
        Assert.Equal(5, snapshot.Clicks);
    }

    [Fact]
    public void Queued_click_command_is_visible_to_actor_snapshot_read()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 4 }));
        runtime.Start();
        var player = runtime.CreatePlayer("actor-click");

        Assert.True(runtime.TryEnqueueClick(player.PlayerId, 9));
        Assert.True(runtime.TryGetSnapshot(player.PlayerId, out var snapshot));
        Assert.Equal(9, snapshot.Clicks);
        Assert.True(snapshot.Gold > player.Gold);
    }

    [Fact]
    public void Queued_move_command_is_visible_to_actor_aoi_observer_read()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 4 }));
        runtime.Start();
        var player = runtime.LoginPlayer("actor-observer", null, "session-a");

        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnqueueMove(player.PlayerId, 6, 0, 7, 1));

        Assert.True(runtime.TryGetAoiObserverState(player.PlayerId, out var observer));
        Assert.Equal(player.PlayerId, observer.PlayerId);
        Assert.Equal(1001, observer.MapId);
        Assert.Equal(6, observer.X);
        Assert.Equal(0, observer.Y);
        Assert.Equal(7, observer.Z);
    }

    [Fact]
    public async Task Command_queue_execute_async_completes_after_processor_applies_command()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.LoginPlayer("queued-enter", null, "session-a");
        var queue = new GameCommandQueue(Options.Create(new GameOptions { ChannelCapacity = 8 }));
        var processor = new GameCommandProcessor(runtime);

        var pending = queue.ExecuteAsync(new YuoEnterMapCommand(player.PlayerId, 1001)).AsTask();
        Assert.False(pending.IsCompleted);

        var queued = await queue.Reader.ReadAsync();
        Assert.True(queued.ExpectsResult);
        var result = await processor.ApplyAsync(queued.Command);
        Assert.True(queued.TryComplete(result));

        var completed = await pending;
        Assert.True(completed.Success);
        Assert.NotNull(completed.World);
        Assert.Equal(1001, completed.World.MapId);
        Assert.Contains(completed.World.Players, item => item.PlayerId == player.PlayerId);
    }

    [Fact]
    public async Task Command_queue_fire_and_forget_keeps_no_result_completion()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var player = runtime.CreatePlayer("queued-click");
        var queue = new GameCommandQueue(Options.Create(new GameOptions { ChannelCapacity = 8 }));
        var processor = new GameCommandProcessor(runtime);

        await queue.EnqueueAsync(new YuoClickCommand(player.PlayerId, 3));

        var queued = await queue.Reader.ReadAsync();
        Assert.False(queued.ExpectsResult);
        var result = await processor.ApplyAsync(queued.Command);
        Assert.False(queued.TryComplete(result));

        runtime.Tick();
        Assert.True(runtime.TryGetSnapshot(player.PlayerId, out var snapshot));
        Assert.Equal(3, snapshot.Clicks);
    }

    [Fact]
    public void Mmo_loop_supports_login_enter_move_broadcast_disconnect_reconnect_and_restore()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();

        var first = runtime.LoginPlayer("hero", null, "session-a");
        var second = runtime.LoginPlayer("observer", null, "session-b");

        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out var firstEnter));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out var secondEnter));
        Assert.Equal(1001, firstEnter.MapId);
        Assert.Contains(firstEnter.Players, player => player.PlayerId == first.PlayerId);
        Assert.Contains(secondEnter.Events, item => item.Kind == "enter" && item.Player.PlayerId == second.PlayerId);

        Assert.True(runtime.TryMove(first.PlayerId, 10, 0, 20, 1, out var moved));
        Assert.Contains(moved.Players, player =>
            player.PlayerId == first.PlayerId &&
            Math.Abs(player.X - 10) < 0.001f &&
            Math.Abs(player.Z - 20) < 0.001f);

        var observerEvents = runtime.DrainEvents(second.PlayerId);
        Assert.Contains(observerEvents, item => item.Kind == "move" && item.Player.PlayerId == first.PlayerId);

        Assert.False(runtime.TryDisconnect(first.PlayerId, "wrong-session"));
        Assert.True(runtime.TryGetSnapshot(first.PlayerId, out var stillOnline));
        Assert.True(stillOnline.Online);
        Assert.Empty(runtime.DrainEvents(second.PlayerId));

        Assert.True(runtime.TryDisconnect(first.PlayerId, "session-a"));
        Assert.True(runtime.TryGetSnapshot(first.PlayerId, out var disconnected));
        Assert.False(disconnected.Online);
        var leaveEvents = runtime.DrainEvents(second.PlayerId);
        Assert.Contains(leaveEvents, item => item.Kind == "leave" && item.Player.PlayerId == first.PlayerId);

        var reconnected = runtime.LoginPlayer("hero", first.PlayerId, "session-c");
        Assert.True(reconnected.Online);
        Assert.Equal(1001, reconnected.MapId);
        Assert.Equal(10, reconnected.X);
        Assert.Equal(20, reconnected.Z);

        var saved = reconnected with { Online = false };
        using var restoredRuntime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        restoredRuntime.Start();

        Assert.True(restoredRuntime.TryRestorePlayer(saved));
        Assert.True(restoredRuntime.TryGetSnapshot(first.PlayerId, out var restored));
        Assert.Equal(saved.PlayerId, restored.PlayerId);
        Assert.Equal(saved.MapId, restored.MapId);
        Assert.Equal(saved.UnitId, restored.UnitId);
        Assert.Equal(saved.X, restored.X);
        Assert.Equal(saved.Z, restored.Z);
        Assert.False(restored.Online);
    }

    [Fact]
    public void Runtime_can_group_current_map_players_by_aoi_stream_snapshot()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 2 }));
        runtime.Start();
        var first = runtime.LoginPlayer("first", null, "session-a");
        var second = runtime.LoginPlayer("second", null, "session-b");
        var third = runtime.LoginPlayer("third", null, "session-c");
        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(third.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(first.PlayerId, 5, 0, 5, 1, out _));
        Assert.True(runtime.TryMove(second.PlayerId, 9, 0, 9, 1, out _));
        Assert.True(runtime.TryMove(third.PlayerId, 15, 0, 0, 1, out _));

        var streams = runtime.BuildAoiStreamSnapshots(1001, cellSize: 10);

        Assert.Equal(2, streams.Count);
        var firstCell = Assert.Single(streams, stream => stream.StreamId == "map:1001:aoi:default.cell_0_0");
        Assert.Contains(firstCell.Players, player => player.PlayerId == first.PlayerId);
        Assert.Contains(firstCell.Players, player => player.PlayerId == second.PlayerId);
        Assert.Equal(2, firstCell.Players.Count);
        var secondCell = Assert.Single(streams, stream => stream.StreamId == "map:1001:aoi:default.cell_1_0");
        Assert.Equal(third.PlayerId, Assert.Single(secondCell.Players).PlayerId);
    }

    [Fact]
    public void Runtime_updates_indexed_aoi_stream_snapshots_when_player_moves_between_cells()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = 2,
            StateSyncAoiCellSize = 10
        }));
        runtime.Start();
        var player = runtime.LoginPlayer("indexed", null, "session-a");
        Assert.True(runtime.TryEnterMap(player.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(player.PlayerId, 5, 0, 5, 1, out _));

        var initial = runtime.BuildAoiStreamSnapshots(
            1001,
            cellSize: 10,
            ["map:1001:aoi:default.cell_0_0", "map:1001:aoi:default.cell_1_0"]);
        Assert.Equal(player.PlayerId, Assert.Single(
            Assert.Single(initial, stream => stream.StreamId == "map:1001:aoi:default.cell_0_0").Players).PlayerId);
        Assert.Empty(Assert.Single(initial, stream => stream.StreamId == "map:1001:aoi:default.cell_1_0").Players);

        Assert.True(runtime.TryMove(player.PlayerId, 15, 0, 5, 2, out _));

        var moved = runtime.BuildAoiStreamSnapshots(
            1001,
            cellSize: 10,
            ["map:1001:aoi:default.cell_0_0", "map:1001:aoi:default.cell_1_0"]);
        Assert.Empty(Assert.Single(moved, stream => stream.StreamId == "map:1001:aoi:default.cell_0_0").Players);
        Assert.Equal(player.PlayerId, Assert.Single(
            Assert.Single(moved, stream => stream.StreamId == "map:1001:aoi:default.cell_1_0").Players).PlayerId);
    }

    [Fact]
    public void Aoi_stream_delta_aggregator_emits_leave_and_enter_when_player_moves_between_cells()
    {
        using var runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10, ShardCount = 1 }));
        runtime.Start();
        var aggregator = new GameAoiStreamDeltaAggregator();
        var first = runtime.LoginPlayer("first", null, "session-a");
        var second = runtime.LoginPlayer("second", null, "session-b");
        Assert.True(runtime.TryEnterMap(first.PlayerId, 1001, out _));
        Assert.True(runtime.TryEnterMap(second.PlayerId, 1001, out _));
        Assert.True(runtime.TryMove(first.PlayerId, 5, 0, 5, 1, out _));
        Assert.True(runtime.TryMove(second.PlayerId, 15, 0, 0, 1, out _));

        var initial = aggregator.BuildDeltas(runtime.BuildAoiStreamSnapshots(1001, cellSize: 10));
        var firstCellInitial = Assert.Single(initial, delta => delta.StreamId == "map:1001:aoi:default.cell_0_0");
        Assert.Contains(firstCellInitial.Delta.Changes, change =>
            change.Kind == YuoStateChangeKind.Enter &&
            change.NetworkId == first.UnitId);
        var secondCellInitial = Assert.Single(initial, delta => delta.StreamId == "map:1001:aoi:default.cell_1_0");
        Assert.Contains(secondCellInitial.Delta.Changes, change =>
            change.Kind == YuoStateChangeKind.Enter &&
            change.NetworkId == second.UnitId);

        Assert.True(runtime.TryMove(first.PlayerId, 15, 0, 0, 2, out _));

        var moved = aggregator.BuildDeltas(runtime.BuildAoiStreamSnapshots(1001, cellSize: 10));
        var oldCell = Assert.Single(moved, delta => delta.StreamId == "map:1001:aoi:default.cell_0_0");
        Assert.Contains(oldCell.Delta.Changes, change =>
            change.Kind == YuoStateChangeKind.Leave &&
            change.NetworkId == first.UnitId);

        var newCell = Assert.Single(moved, delta => delta.StreamId == "map:1001:aoi:default.cell_1_0");
        Assert.Contains(newCell.Delta.Changes, change =>
            change.Kind == YuoStateChangeKind.Enter &&
            change.NetworkId == first.UnitId);
        Assert.DoesNotContain(newCell.Delta.Changes, change =>
            change.NetworkId == second.UnitId &&
            change.Kind != YuoStateChangeKind.Leave);
        Assert.Equal(firstCellInitial.Delta.Tick, oldCell.Delta.BaseTick);
        Assert.Equal(secondCellInitial.Delta.Tick, newCell.Delta.BaseTick);
    }
}

file sealed class RecordingSnapshotRepository : IPlayerSnapshotRepository
{
    public YuoPlayerSnapshotDto? LastSaved { get; private set; }

    public Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        LastSaved = snapshot;
        return Task.CompletedTask;
    }

    public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(LastSaved is { } saved && saved.PlayerId == playerId ? saved : null);
    }
}
