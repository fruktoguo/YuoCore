using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using YuoCore.Actor;
using YuoCore.Ecs;
using YuoCore.Game;
using YuoCore.Runtime;
using YuoCore.StateSync;

namespace YuoCore.Backend.Game;

public sealed class GameRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly object _eventGate = new();
    private readonly object _aoiIndexGate = new();
    private readonly object _worldStateGate = new();
    private readonly Dictionary<Guid, PlayerRef> _players = new();
    private readonly Dictionary<Guid, YuoPlayerWorldStateDto> _worldStates = new();
    private readonly Dictionary<long, HashSet<Guid>> _onlinePlayerIdsByMap = new();
    private readonly Dictionary<Guid, Queue<YuoWorldEventDto>> _outboundEvents = new();
    private readonly Dictionary<long, Dictionary<string, HashSet<Guid>>> _aoiPlayerIdsByMapStream = new();
    private readonly Dictionary<Guid, (long MapId, string StreamId)> _aoiStreamByPlayer = new();
    private readonly ConcurrentQueue<YuoAoiDirtyPlayerDto> _aoiDirtyPlayers = new();
    private readonly GameOptions _options;
    private readonly float _aoiIndexCellSize;
    private readonly YuoGridAoiStreamResolver _aoiIndexResolver;
    private readonly YuoCoreRuntime _runtime = new();
    private readonly List<GameShard> _shards = new();
    private readonly InMemoryActorLocationRegistry _actorLocation = new();
    private readonly InMemoryActorTransport _actorTransport = new();
    private readonly YuoEntityActorRegistry _actorEntities = new();
    private readonly YuoActorHandlerRegistry _actorHandlers = new();
    private readonly ActorRouter _actorRouter;
    private bool _started;

    public GameRuntime(IOptions<GameOptions> options)
    {
        _options = options.Value;
        _aoiIndexCellSize = NormalizeAoiCellSize(_options.StateSyncAoiCellSize);
        _aoiIndexResolver = new YuoGridAoiStreamResolver(_aoiIndexCellSize);
        _actorHandlers.AddFromAssembly(typeof(GameRuntime).Assembly, CreateActorHandler);
        _actorRouter = new ActorRouter(_actorLocation, _actorTransport);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            var shardCount = Math.Clamp(_options.ShardCount, 1, Math.Max(1, Environment.ProcessorCount * 2));
            for (var i = 0; i < shardCount; i++)
            {
                var fiber = _runtime.Fibers.CreateFiber(
                    new YuoFiberOptions
                    {
                        Id = i + 1,
                        Name = $"GameShard-{i}",
                        SetWorldAsDefault = i == 0,
                        MaxMailboxMessagesPerTick = 100_000
                    });
                var stats = fiber.World.WorldMain.AddComponent<YuoRuntimeStatsComponent>();
                var dispatcher = new YuoEntityActorDispatcher(_actorEntities, _actorHandlers, fiber.World);
                var endpoint = new YuoFiberActorEndpoint(fiber, dispatcher);
                _shards.Add(new GameShard(i, fiber, stats, endpoint));
            }
            _started = true;
        }
    }

    public YuoPlayerSnapshotDto CreatePlayer(string? name = null, Guid? requestedId = null)
    {
        lock (_gate)
        {
            EnsureStarted();
            var id = requestedId ?? Guid.NewGuid();
            if (_players.TryGetValue(id, out var existing)) return Snapshot(existing.Entity);

            var shard = SelectShard(id);
            var entity = new YuoEntity(shard.World, IDGenerate.GetID(id.ToString("N")));
            entity.EntityName = $"player:{id:N}";
            entity.AddComponent<YuoIdlePlayerComponent>().PlayerId = id;
            entity.GetComponent<YuoIdlePlayerComponent>().Name = string.IsNullOrWhiteSpace(name) ? $"Player-{id.ToString("N")[..8]}" : name.Trim();
            entity.AddComponent<YuoIdleWalletComponent>();
            entity.AddComponent<YuoIdleProgressComponent>();
            entity.AddComponent<YuoPlayerPresenceComponent>().UnitId = entity.EntityData.Id;
            entity.AddComponent<YuoPlayerTransformComponent>();
            var playerRef = RegisterPlayerActor(id, shard, entity);
            _players[id] = playerRef;
            UpdateWorldStateProjection(entity);
            return Snapshot(entity);
        }
    }

    public YuoPlayerSnapshotDto LoginPlayer(string? name, Guid? requestedId, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_gate)
        {
            EnsureStarted();
            var snapshot = CreatePlayerUnsafeIfMissing(name, requestedId);
            var playerRef = _players[snapshot.PlayerId];
            var presence = playerRef.Entity.GetComponent<YuoPlayerPresenceComponent>();
            presence.Online = true;
            presence.SessionId = sessionId;
            presence.LastSeenAt = DateTimeOffset.UtcNow;
            UpdateWorldStateProjection(playerRef.Entity);
            EnsureEventQueue(snapshot.PlayerId);
            return Snapshot(playerRef.Entity);
        }
    }

    public bool TryEnterMap(Guid playerId, long mapId, out YuoWorldSnapshotDto snapshot)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (!_players.TryGetValue(playerId, out var playerRef))
            {
                snapshot = default!;
                return false;
            }

            AwaitSync(_actorRouter.SendAsync(new EnterMapActorMessage(playerRef.ActorId, mapId)).AsTask());
            TickShard(playerRef.Shard);
            snapshot = BuildMapSnapshot(mapId, DrainEvents(playerId));
            return true;
        }
    }

    public bool TryMove(Guid playerId, float x, float y, float z, ulong inputSeq, out YuoWorldSnapshotDto snapshot)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (!_players.TryGetValue(playerId, out var playerRef))
            {
                snapshot = default!;
                return false;
            }

            var presence = playerRef.Entity.GetComponent<YuoPlayerPresenceComponent>();
            AwaitSync(_actorRouter.SendAsync(new MoveActorMessage(playerRef.ActorId, x, y, z, inputSeq)).AsTask());
            TickShard(playerRef.Shard);
            snapshot = BuildMapSnapshot(presence.MapId, DrainEvents(playerId));
            return true;
        }
    }

    public bool TryDisconnect(Guid playerId, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        lock (_gate)
        {
            EnsureStarted();
            if (!_players.TryGetValue(playerId, out var playerRef)) return false;

            // 断线同样走目标 Fiber 的 Actor request。
            // 这样 Gateway / HTTP / 停服清理都不会绕过 Fiber 所有权直接改 ECS 组件。
            var disconnectTask = _actorRouter
                .InvokeAsync<bool>(new DisconnectActorMessage(playerRef.ActorId, sessionId))
                .AsTask();
            TickShard(playerRef.Shard);
            return AwaitSync(disconnectTask);
        }
    }

    public IReadOnlyList<YuoWorldEventDto> DrainEvents(Guid playerId, int maxCount = 256)
    {
        lock (_gate)
        {
            lock (_eventGate)
            {
                if (!_outboundEvents.TryGetValue(playerId, out var queue) || queue.Count == 0)
                {
                    return [];
                }

                var count = Math.Clamp(maxCount, 1, queue.Count);
                var events = new List<YuoWorldEventDto>(count);
                while (events.Count < count && queue.TryDequeue(out var item))
                {
                    events.Add(item);
                }

                return events;
            }
        }
    }

    public IReadOnlyList<YuoAoiDirtyPlayerDto> DrainAoiDirtyPlayers(int maxCount = 4096)
    {
        lock (_gate)
        {
            if (_aoiDirtyPlayers.Count == 0)
            {
                return [];
            }

            var count = Math.Clamp(maxCount, 1, _aoiDirtyPlayers.Count);
            var dirty = new List<YuoAoiDirtyPlayerDto>(count);
            while (dirty.Count < count && _aoiDirtyPlayers.TryDequeue(out var item))
            {
                dirty.Add(item);
            }

            return dirty;
        }
    }

    public bool TryRestorePlayer(YuoPlayerSnapshotDto snapshot)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (_players.ContainsKey(snapshot.PlayerId)) return false;

            var shard = SelectShard(snapshot.PlayerId);
            var entity = new YuoEntity(shard.World, IDGenerate.GetID(snapshot.PlayerId.ToString("N")));
            entity.EntityName = $"player:{snapshot.PlayerId:N}";
            entity.AddComponent<YuoIdlePlayerComponent>().PlayerId = snapshot.PlayerId;
            entity.GetComponent<YuoIdlePlayerComponent>().Name = string.IsNullOrWhiteSpace(snapshot.Name)
                ? $"Player-{snapshot.PlayerId.ToString("N")[..8]}"
                : snapshot.Name;
            entity.AddComponent<YuoIdleWalletComponent>().Gold = snapshot.Gold;
            entity.GetComponent<YuoIdleWalletComponent>().Exp = snapshot.Exp;
            entity.GetComponent<YuoIdleWalletComponent>().Clicks = snapshot.Clicks;
            entity.AddComponent<YuoIdleProgressComponent>().Level = Math.Max(1, snapshot.Level);
            entity.GetComponent<YuoIdleProgressComponent>().Power = Math.Max(1, snapshot.Power);
            entity.GetComponent<YuoIdleProgressComponent>().UpdatedAt = snapshot.UpdatedAt;
            entity.GetComponent<YuoIdleProgressComponent>().LastTickAt = snapshot.UpdatedAt;
            entity.AddComponent<YuoPlayerPresenceComponent>().MapId = snapshot.MapId;
            entity.GetComponent<YuoPlayerPresenceComponent>().UnitId = snapshot.UnitId == 0 ? entity.EntityData.Id : snapshot.UnitId;
            entity.GetComponent<YuoPlayerPresenceComponent>().Online = snapshot.Online;
            entity.GetComponent<YuoPlayerPresenceComponent>().LastInputSeq = snapshot.LastInputSeq;
            entity.GetComponent<YuoPlayerPresenceComponent>().LastSeenAt = snapshot.UpdatedAt;
            entity.AddComponent<YuoPlayerTransformComponent>().X = snapshot.X;
            entity.GetComponent<YuoPlayerTransformComponent>().Y = snapshot.Y;
            entity.GetComponent<YuoPlayerTransformComponent>().Z = snapshot.Z;
            _players[snapshot.PlayerId] = RegisterPlayerActor(snapshot.PlayerId, shard, entity);
            UpdateWorldStateProjection(entity);
            UpdateAoiIndex(snapshot.PlayerId, snapshot.MapId, snapshot.X, snapshot.Y, snapshot.Z);
            EnsureEventQueue(snapshot.PlayerId);
            return true;
        }
    }

    public bool TryApplyClick(Guid playerId, long amount)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (!_players.TryGetValue(playerId, out var playerRef)) return false;
            var wallet = playerRef.Entity.GetComponent<YuoIdleWalletComponent>();
            var progress = playerRef.Entity.GetComponent<YuoIdleProgressComponent>();
            var gain = Math.Clamp(amount, 1, 1_000) * Math.Max(1, progress.Power);
            wallet.Gold += gain;
            wallet.Clicks += amount;
            progress.UpdatedAt = DateTimeOffset.UtcNow;
            playerRef.Shard.Stats.ProcessedCommands++;
            return true;
        }
    }

    public bool TryEnqueueClick(Guid playerId, long amount)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (!_players.TryGetValue(playerId, out var playerRef)) return false;

            // 命令队列入口只负责投递消息，不直接改组件。
            // 真正的组件修改会在目标 GameShard 的 YuoFiber mailbox 中执行，避免跨线程碰 ECS 状态。
            AwaitSync(_actorRouter.SendAsync(new ApplyClickActorMessage(playerRef.ActorId, amount)).AsTask());
            return true;
        }
    }

    public bool TryEnqueueMove(Guid playerId, float x, float y, float z, ulong inputSeq)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (!_players.TryGetValue(playerId, out var playerRef)) return false;

            AwaitSync(_actorRouter.SendAsync(new MoveActorMessage(playerRef.ActorId, x, y, z, inputSeq)).AsTask());
            return true;
        }
    }

    public bool TryGetSnapshot(Guid playerId, out YuoPlayerSnapshotDto snapshot)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (_players.TryGetValue(playerId, out var playerRef))
            {
                // 对外读快照也通过目标 Actor request。
                // 这会先 drain 目标 Fiber mailbox，返回同一执行所有权内生成的 DTO，
                // 避免 Gateway / HTTP / 保存流程为了读取状态而跨线程直接摸 ECS 组件。
                var readTask = _actorRouter
                    .InvokeAsync<YuoPlayerSnapshotDto>(new ReadPlayerSnapshotActorMessage(playerRef.ActorId))
                    .AsTask();
                TickShard(playerRef.Shard);
                snapshot = AwaitSync(readTask);
                return true;
            }
        }

        snapshot = default!;
        return false;
    }

    public bool TryGetAoiObserverState(Guid playerId, out GameAoiObserverState state)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (_players.TryGetValue(playerId, out var playerRef))
            {
                // AOI observer state 是 Gateway push 的读路径。
                // 它必须和玩家组件写入同属一个 Fiber，否则弱网重连或并发 Move 时容易读到半旧状态。
                var readTask = _actorRouter
                    .InvokeAsync<GameAoiObserverState>(new ReadAoiObserverStateActorMessage(playerRef.ActorId))
                    .AsTask();
                TickShard(playerRef.Shard);
                state = AwaitSync(readTask);
                return true;
            }
        }

        state = default;
        return false;
    }

    public bool TryGetProjectedAoiObserverState(Guid playerId, out GameAoiObserverState state)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (TryGetProjectedWorldState(playerId, out var projected))
            {
                state = new GameAoiObserverState(
                    projected.PlayerId,
                    projected.MapId,
                    projected.X,
                    projected.Y,
                    projected.Z);
                return true;
            }
        }

        state = default;
        return false;
    }

    public bool TryGetWorldSnapshot(Guid playerId, out YuoWorldSnapshotDto snapshot)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (!_players.TryGetValue(playerId, out var playerRef))
            {
                snapshot = default!;
                return false;
            }

            var readTask = _actorRouter
                .InvokeAsync<GameAoiObserverState>(new ReadAoiObserverStateActorMessage(playerRef.ActorId))
                .AsTask();
            TickShard(playerRef.Shard);
            var observer = AwaitSync(readTask);
            if (observer.MapId == 0)
            {
                snapshot = default!;
                return false;
            }

            snapshot = BuildMapSnapshot(observer.MapId, []);
            return true;
        }
    }

    public IReadOnlyList<YuoAoiStreamSnapshotDto> BuildAoiStreamSnapshots(
        long mapId,
        float cellSize)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (TryBuildAoiStreamSnapshotsFromIndex(mapId, cellSize, null, out var indexedSnapshots))
            {
                return indexedSnapshots;
            }

            var resolver = new YuoGridAoiStreamResolver(cellSize);
            var playersByStream = SnapshotWorldStateProjection()
                .Where(state => state.MapId == mapId)
                .GroupBy(
                    state => resolver.ResolveStreamId(
                        mapId,
                        new YuoAoiPosition(state.X, state.Y, state.Z)),
                    StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToArray();
            var tick = CurrentTickUnsafe();
            return playersByStream
                .Select(group => new YuoAoiStreamSnapshotDto(
                    group.Key,
                    mapId,
                    tick,
                    group.OrderBy(state => state.UnitId).ToArray()))
                .ToArray();
        }
    }

    public IReadOnlyList<YuoAoiStreamSnapshotDto> BuildAoiStreamSnapshots(
        long mapId,
        float cellSize,
        IEnumerable<string> streamIds)
    {
        ArgumentNullException.ThrowIfNull(streamIds);

        lock (_gate)
        {
            EnsureStarted();
            var requestedStreams = streamIds
                .Where(static streamId => !string.IsNullOrWhiteSpace(streamId))
                .Select(static streamId => streamId.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (requestedStreams.Length == 0)
            {
                return [];
            }

            if (TryBuildAoiStreamSnapshotsFromIndex(mapId, cellSize, requestedStreams, out var indexedSnapshots))
            {
                return indexedSnapshots;
            }

            var requested = requestedStreams.ToHashSet(StringComparer.Ordinal);
            var resolver = new YuoGridAoiStreamResolver(cellSize);
            var playersByStream = new Dictionary<string, List<YuoPlayerWorldStateDto>>(StringComparer.Ordinal);
            foreach (var state in SnapshotWorldStateProjection())
            {
                if (state.MapId != mapId)
                {
                    continue;
                }

                var streamId = resolver.ResolveStreamId(
                    mapId,
                    new YuoAoiPosition(state.X, state.Y, state.Z));
                if (!requested.Contains(streamId))
                {
                    continue;
                }

                if (!playersByStream.TryGetValue(streamId, out var states))
                {
                    states = [];
                    playersByStream.Add(streamId, states);
                }

                states.Add(state);
            }

            var tick = CurrentTickUnsafe();
            return requestedStreams
                .Select(streamId => new YuoAoiStreamSnapshotDto(
                    streamId,
                    mapId,
                    tick,
                    playersByStream.TryGetValue(streamId, out var players)
                        ? players.OrderBy(static state => state.UnitId).ToArray()
                        : []))
                .ToArray();
        }
    }

    public IReadOnlyList<YuoPlayerSnapshotDto> SeedPlayers(int count)
    {
        count = Math.Clamp(count, 1, 200_000);
        var result = new List<YuoPlayerSnapshotDto>(Math.Min(count, 1_000));
        lock (_gate)
        {
            EnsureStarted();
            for (var i = 0; i < count; i++)
            {
                var snapshot = CreatePlayerUnsafe($"Bot-{_players.Count + 1}");
                if (result.Count < 1_000) result.Add(snapshot);
            }
        }

        return result;
    }

    public void Tick()
    {
        lock (_gate)
        {
            EnsureStarted();
            if (_shards.Count == 1)
            {
                AwaitSync(_runtime.TickAsync().AsTask());
            }
            else
            {
                AwaitSync(_runtime.TickParallelAsync(_options.MaxTickConcurrency).AsTask());
            }
        }
    }

    public YuoGameStatsDto Stats()
    {
        lock (_gate)
        {
            EnsureStarted();
            var tickCount = _shards.Count == 0 ? 0 : _shards.Min(x => x.Stats.TickCount);
            var processedCommands = _shards.Sum(x => x.Stats.ProcessedCommands);
            return new YuoGameStatsDto(_players.Count, tickCount, processedCommands, _options.TickRate);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_started) return;
            _runtime.Dispose();
            foreach (var playerRef in _players.Values)
            {
                _actorTransport.UnregisterEndpoint(playerRef.Address);
                AwaitSync(_actorLocation.UnregisterAsync(playerRef.ActorId).AsTask());
                _actorEntities.Unregister(playerRef.ActorId);
            }
            _players.Clear();
            lock (_worldStateGate)
            {
                _worldStates.Clear();
                _onlinePlayerIdsByMap.Clear();
            }
            lock (_eventGate)
            {
                _outboundEvents.Clear();
            }
            lock (_aoiIndexGate)
            {
                _aoiPlayerIdsByMapStream.Clear();
                _aoiStreamByPlayer.Clear();
            }

            _aoiDirtyPlayers.Clear();
            _shards.Clear();
            _started = false;
        }
    }

    private YuoPlayerSnapshotDto CreatePlayerUnsafe(string name)
    {
        var id = Guid.NewGuid();
        var shard = SelectShard(id);
        var entity = new YuoEntity(shard.World, IDGenerate.GetID(id.ToString("N")));
        entity.EntityName = $"player:{id:N}";
        entity.AddComponent<YuoIdlePlayerComponent>().PlayerId = id;
        entity.GetComponent<YuoIdlePlayerComponent>().Name = name;
        entity.AddComponent<YuoIdleWalletComponent>();
        entity.AddComponent<YuoIdleProgressComponent>();
        entity.AddComponent<YuoPlayerPresenceComponent>().UnitId = entity.EntityData.Id;
        entity.AddComponent<YuoPlayerTransformComponent>();
        _players[id] = RegisterPlayerActor(id, shard, entity);
        UpdateWorldStateProjection(entity);
        return Snapshot(entity);
    }

    private YuoPlayerSnapshotDto CreatePlayerUnsafeIfMissing(string? name, Guid? requestedId)
    {
        var id = requestedId ?? Guid.NewGuid();
        if (_players.TryGetValue(id, out var existing)) return Snapshot(existing.Entity);

        return CreatePlayer(name, id);
    }

    private YuoPlayerWorldStateDto UpdateWorldStateProjection(YuoEntity entity)
    {
        // Backend 示例先用 DTO 投影承接跨玩家读模型。
        // 这样世界快照和 AOI stream 聚合可以只读稳定 DTO，
        // 后续真正拆 Map/Room Fiber 时，再把这张表迁移到 Map/Room 所有者内。
        var state = WorldState(entity);
        lock (_worldStateGate)
        {
            UpdateOnlinePlayerIndexUnsafe(state.PlayerId, state);
            _worldStates[state.PlayerId] = state;
        }

        return state;
    }

    private YuoPlayerWorldStateDto[] SnapshotWorldStateProjection()
    {
        lock (_worldStateGate)
        {
            return _worldStates.Values.ToArray();
        }
    }

    private Guid[] SnapshotOnlinePlayerIdsByMap(long mapId)
    {
        lock (_worldStateGate)
        {
            return _onlinePlayerIdsByMap.TryGetValue(mapId, out var players)
                ? players.ToArray()
                : [];
        }
    }

    private YuoPlayerWorldStateDto[] SnapshotProjectedWorldStates(IReadOnlyList<Guid> playerIds)
    {
        lock (_worldStateGate)
        {
            var states = new List<YuoPlayerWorldStateDto>(playerIds.Count);
            foreach (var playerId in playerIds)
            {
                if (_worldStates.TryGetValue(playerId, out var state))
                {
                    states.Add(state);
                }
            }

            return states.ToArray();
        }
    }

    private bool TryGetProjectedWorldState(Guid playerId, out YuoPlayerWorldStateDto state)
    {
        lock (_worldStateGate)
        {
            return _worldStates.TryGetValue(playerId, out state!);
        }
    }

    private void UpdateOnlinePlayerIndexUnsafe(Guid playerId, YuoPlayerWorldStateDto next)
    {
        if (_worldStates.TryGetValue(playerId, out var previous) &&
            previous.Online &&
            previous.MapId > 0 &&
            _onlinePlayerIdsByMap.TryGetValue(previous.MapId, out var previousPlayers))
        {
            previousPlayers.Remove(playerId);
            if (previousPlayers.Count == 0)
            {
                _onlinePlayerIdsByMap.Remove(previous.MapId);
            }
        }

        if (!next.Online || next.MapId <= 0)
        {
            return;
        }

        if (!_onlinePlayerIdsByMap.TryGetValue(next.MapId, out var nextPlayers))
        {
            nextPlayers = new HashSet<Guid>();
            _onlinePlayerIdsByMap.Add(next.MapId, nextPlayers);
        }

        nextPlayers.Add(playerId);
    }

    private PlayerRef RegisterPlayerActor(Guid playerId, GameShard shard, YuoEntity entity)
    {
        var actorId = new ActorId(entity.EntityData.Id);
        var address = new ActorAddress("local", checked((int)shard.Fiber.Id), actorId);
        _actorEntities.Register(actorId, entity);
        _actorTransport.RegisterEndpoint(address, shard.ActorEndpoint);
        AwaitSync(_actorLocation.RegisterAsync(actorId, address).AsTask());
        return new PlayerRef(shard, entity, actorId, address);
    }

    private static YuoPlayerSnapshotDto Snapshot(YuoEntity entity)
    {
        var player = entity.GetComponent<YuoIdlePlayerComponent>();
        var wallet = entity.GetComponent<YuoIdleWalletComponent>();
        var progress = entity.GetComponent<YuoIdleProgressComponent>();
        var presence = entity.GetComponent<YuoPlayerPresenceComponent>();
        var transform = entity.GetComponent<YuoPlayerTransformComponent>();
        return new YuoPlayerSnapshotDto(
            player.PlayerId,
            player.Name,
            wallet.Gold,
            wallet.Exp,
            progress.Level,
            progress.Power,
            wallet.Clicks,
            progress.UpdatedAt,
            presence.MapId,
            presence.UnitId,
            transform.X,
            transform.Y,
            transform.Z,
            presence.Online,
            presence.LastInputSeq);
    }

    private YuoWorldSnapshotDto BuildMapSnapshot(long mapId, IReadOnlyList<YuoWorldEventDto> events)
    {
        var players = SnapshotWorldStateProjection()
            .Where(state => state.MapId == mapId)
            .OrderBy(state => state.UnitId)
            .ToArray();
        return new YuoWorldSnapshotDto(mapId, CurrentTickUnsafe(), players, events);
    }

    private long CurrentTickUnsafe()
    {
        return _shards.Count == 0 ? 0 : _shards.Min(x => x.Stats.TickCount);
    }

    private static YuoPlayerWorldStateDto WorldState(YuoEntity entity)
    {
        var player = entity.GetComponent<YuoIdlePlayerComponent>();
        var progress = entity.GetComponent<YuoIdleProgressComponent>();
        var presence = entity.GetComponent<YuoPlayerPresenceComponent>();
        var transform = entity.GetComponent<YuoPlayerTransformComponent>();
        return new YuoPlayerWorldStateDto(
            player.PlayerId,
            player.Name,
            presence.MapId,
            presence.UnitId,
            transform.X,
            transform.Y,
            transform.Z,
            presence.Online,
            presence.LastInputSeq,
            progress.UpdatedAt);
    }

    private void TickShard(GameShard shard)
    {
        AwaitSync(shard.Fiber.TickAsync().AsTask());
    }

    private static readonly TimeSpan SyncWaitTimeout = TimeSpan.FromSeconds(30);

    // 同步 facade 在持锁状态下阻塞等待异步结果。若异步完成依赖某个未被本线程驱动的
    // Fiber tick（例如 Actor 被迁移/派发到其他 shard），裸 GetResult 会永久持锁冻结
    // 整个 GameRuntime；有界等待把该故障变成可见的 TimeoutException。
    private static T AwaitSync<T>(Task<T> task)
    {
        return task.WaitAsync(SyncWaitTimeout).GetAwaiter().GetResult();
    }

    private static void AwaitSync(Task task)
    {
        task.WaitAsync(SyncWaitTimeout).GetAwaiter().GetResult();
    }

    private void EnsureEventQueue(Guid playerId)
    {
        lock (_eventGate)
        {
            if (!_outboundEvents.ContainsKey(playerId))
            {
                _outboundEvents[playerId] = new Queue<YuoWorldEventDto>();
            }
        }
    }

    private IYuoActorHandler? CreateActorHandler(Type type)
    {
        if (type == typeof(ApplyClickActorHandler))
        {
            return new ApplyClickActorHandler();
        }

        if (type == typeof(EnterMapActorHandler))
        {
            return new EnterMapActorHandler(this);
        }

        if (type == typeof(MoveActorHandler))
        {
            return new MoveActorHandler(this);
        }

        if (type == typeof(DisconnectActorHandler))
        {
            return new DisconnectActorHandler(this);
        }

        if (type == typeof(ReadPlayerSnapshotActorHandler))
        {
            return new ReadPlayerSnapshotActorHandler();
        }

        if (type == typeof(ReadAoiObserverStateActorHandler))
        {
            return new ReadAoiObserverStateActorHandler();
        }

        return null;
    }

    private void EnqueueMapEvent(long mapId, YuoWorldEventDto item)
    {
        if (!_options.WorldEventsEnabled)
        {
            return;
        }

        lock (_eventGate)
        {
            foreach (var playerId in SnapshotOnlinePlayerIdsByMap(mapId))
            {
                if (!_outboundEvents.TryGetValue(playerId, out var queue))
                {
                    queue = new Queue<YuoWorldEventDto>();
                    _outboundEvents[playerId] = queue;
                }

                queue.Enqueue(item);
            }
        }
    }

    private void EnqueueAoiDirty(
        YuoEntity entity,
        long previousMapId,
        long currentMapId,
        float previousX,
        float previousY,
        float previousZ,
        float currentX,
        float currentY,
        float currentZ)
    {
        var player = entity.GetComponent<YuoIdlePlayerComponent>();
        UpdateAoiIndex(player.PlayerId, currentMapId, currentX, currentY, currentZ);
        _aoiDirtyPlayers.Enqueue(new YuoAoiDirtyPlayerDto(
            player.PlayerId,
            previousMapId,
            currentMapId,
            previousX,
            previousY,
            previousZ,
            currentX,
            currentY,
            currentZ,
            DateTimeOffset.UtcNow));
    }

    private bool TryBuildAoiStreamSnapshotsFromIndex(
        long mapId,
        float cellSize,
        IReadOnlyList<string>? requestedStreams,
        out IReadOnlyList<YuoAoiStreamSnapshotDto> snapshots)
    {
        snapshots = [];
        if (!IsAoiIndexCompatible(cellSize))
        {
            return false;
        }

        KeyValuePair<string, Guid[]>[] indexedStreams;
        lock (_aoiIndexGate)
        {
            if (!_aoiPlayerIdsByMapStream.TryGetValue(mapId, out var streams))
            {
                indexedStreams = requestedStreams is null
                    ? []
                    : requestedStreams
                        .Select(static streamId => new KeyValuePair<string, Guid[]>(streamId, []))
                        .ToArray();
            }
            else if (requestedStreams is null)
            {
                indexedStreams = streams
                    .Where(static pair => pair.Value.Count > 0)
                    .Select(static pair => new KeyValuePair<string, Guid[]>(pair.Key, pair.Value.ToArray()))
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .ToArray();
            }
            else
            {
                indexedStreams = requestedStreams
                    .Select(streamId => new KeyValuePair<string, Guid[]>(
                        streamId,
                        streams.TryGetValue(streamId, out var players) ? players.ToArray() : []))
                    .ToArray();
            }
        }

        if (requestedStreams is null && indexedStreams.Length == 0)
        {
            snapshots = [];
            return true;
        }

        var tick = CurrentTickUnsafe();
        snapshots = indexedStreams
            .Select(pair => new YuoAoiStreamSnapshotDto(
                pair.Key,
                mapId,
                tick,
                SnapshotProjectedWorldStates(pair.Value)
                    .OrderBy(static state => state.UnitId)
                    .ToArray()))
            .ToArray();
        return true;
    }

    private void UpdateAoiIndex(Guid playerId, long mapId, float x, float y, float z)
    {
        var nextStreamId = mapId > 0
            ? ResolveAoiStreamId(mapId, x, y, z)
            : null;

        lock (_aoiIndexGate)
        {
            if (_aoiStreamByPlayer.TryGetValue(playerId, out var previous))
            {
                if (mapId > 0 &&
                    previous.MapId == mapId &&
                    string.Equals(previous.StreamId, nextStreamId, StringComparison.Ordinal))
                {
                    return;
                }

                if (_aoiPlayerIdsByMapStream.TryGetValue(previous.MapId, out var previousMap) &&
                    previousMap.TryGetValue(previous.StreamId, out var previousPlayers))
                {
                    previousPlayers.Remove(playerId);
                    if (previousPlayers.Count == 0)
                    {
                        previousMap.Remove(previous.StreamId);
                    }

                    if (previousMap.Count == 0)
                    {
                        _aoiPlayerIdsByMapStream.Remove(previous.MapId);
                    }
                }

                _aoiStreamByPlayer.Remove(playerId);
            }

            if (mapId <= 0)
            {
                return;
            }

            if (!_aoiPlayerIdsByMapStream.TryGetValue(mapId, out var mapStreams))
            {
                mapStreams = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
                _aoiPlayerIdsByMapStream.Add(mapId, mapStreams);
            }

            if (!mapStreams.TryGetValue(nextStreamId!, out var players))
            {
                players = new HashSet<Guid>();
                mapStreams.Add(nextStreamId!, players);
            }

            players.Add(playerId);
            _aoiStreamByPlayer[playerId] = (mapId, nextStreamId!);
        }
    }

    private bool IsAoiIndexCompatible(float cellSize)
    {
        return MathF.Abs(cellSize - _aoiIndexCellSize) <= 0.0001f;
    }

    private string ResolveAoiStreamId(long mapId, float x, float y, float z)
    {
        return _aoiIndexResolver.ResolveStreamId(
            mapId,
            new YuoAoiPosition(x, y, z));
    }

    private static float NormalizeAoiCellSize(float cellSize)
    {
        return float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0
            ? 10
            : cellSize;
    }

    private void EnsureStarted()
    {
        if (!_started) throw new InvalidOperationException("Game runtime has not started.");
    }

    private GameShard SelectShard(Guid playerId)
    {
        if (_shards.Count == 0) throw new InvalidOperationException("Game runtime has not started.");

        // Guid.GetHashCode 在同一进程内足够用于压测分布；持久化分片规则后续应换成稳定 hash。
        var shardIndex = Math.Abs(playerId.GetHashCode() % _shards.Count);
        return _shards[shardIndex];
    }

    private sealed record GameShard(
        int Id,
        YuoFiber Fiber,
        YuoRuntimeStatsComponent Stats,
        YuoFiberActorEndpoint ActorEndpoint)
    {
        public YuoWorld World => Fiber.World;
    }

    private sealed record PlayerRef(GameShard Shard, YuoEntity Entity, ActorId ActorId, ActorAddress Address);

    private sealed record ApplyClickActorMessage(ActorId TargetActorId, long Amount) : IActorMessage;

    private sealed record EnterMapActorMessage(ActorId TargetActorId, long MapId) : IActorMessage;

    private sealed record MoveActorMessage(
        ActorId TargetActorId,
        float X,
        float Y,
        float Z,
        ulong InputSeq) : IActorMessage;

    private sealed record DisconnectActorMessage(
        ActorId TargetActorId,
        string SessionId) : IActorRequest<bool>;

    private sealed record ReadPlayerSnapshotActorMessage(
        ActorId TargetActorId) : IActorRequest<YuoPlayerSnapshotDto>;

    private sealed record ReadAoiObserverStateActorMessage(
        ActorId TargetActorId) : IActorRequest<GameAoiObserverState>;

    private sealed class ApplyClickActorHandler : YuoActorHandler<ApplyClickActorMessage>
    {
        protected override ValueTask RunAsync(
            YuoEntity entity,
            ApplyClickActorMessage message,
            CancellationToken cancellationToken)
        {
            var wallet = entity.GetComponent<YuoIdleWalletComponent>();
            var progress = entity.GetComponent<YuoIdleProgressComponent>();
            var gain = Math.Clamp(message.Amount, 1, 1_000) * Math.Max(1, progress.Power);
            wallet.Gold += gain;
            wallet.Clicks += message.Amount;
            progress.UpdatedAt = DateTimeOffset.UtcNow;

            if (entity.World.WorldMain.GetComponent<YuoRuntimeStatsComponent>() is { } stats)
            {
                stats.ProcessedCommands++;
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class EnterMapActorHandler(GameRuntime owner) : YuoActorHandler<EnterMapActorMessage>
    {
        protected override ValueTask RunAsync(
            YuoEntity entity,
            EnterMapActorMessage message,
            CancellationToken cancellationToken)
        {
            var presence = entity.GetComponent<YuoPlayerPresenceComponent>();
            var progress = entity.GetComponent<YuoIdleProgressComponent>();
            var transform = entity.GetComponent<YuoPlayerTransformComponent>();
            var previousMapId = presence.MapId;
            var previousX = transform.X;
            var previousY = transform.Y;
            var previousZ = transform.Z;
            presence.MapId = message.MapId;
            presence.Online = true;
            presence.LastSeenAt = DateTimeOffset.UtcNow;
            progress.UpdatedAt = presence.LastSeenAt;

            owner.EnqueueAoiDirty(
                entity,
                previousMapId,
                presence.MapId,
                previousX,
                previousY,
                previousZ,
                transform.X,
                transform.Y,
                transform.Z);
            var state = owner.UpdateWorldStateProjection(entity);
            owner.EnqueueMapEvent(message.MapId, new YuoWorldEventDto("enter", state, DateTimeOffset.UtcNow));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MoveActorHandler(GameRuntime owner) : YuoActorHandler<MoveActorMessage>
    {
        protected override ValueTask RunAsync(
            YuoEntity entity,
            MoveActorMessage message,
            CancellationToken cancellationToken)
        {
            var presence = entity.GetComponent<YuoPlayerPresenceComponent>();
            if (message.InputSeq <= presence.LastInputSeq)
            {
                return ValueTask.CompletedTask;
            }

            var transform = entity.GetComponent<YuoPlayerTransformComponent>();
            var progress = entity.GetComponent<YuoIdleProgressComponent>();
            var previousX = transform.X;
            var previousY = transform.Y;
            var previousZ = transform.Z;
            transform.X = message.X;
            transform.Y = message.Y;
            transform.Z = message.Z;
            presence.LastInputSeq = message.InputSeq;
            presence.LastSeenAt = DateTimeOffset.UtcNow;
            progress.UpdatedAt = presence.LastSeenAt;

            owner.EnqueueAoiDirty(
                entity,
                presence.MapId,
                presence.MapId,
                previousX,
                previousY,
                previousZ,
                transform.X,
                transform.Y,
                transform.Z);
            var state = owner.UpdateWorldStateProjection(entity);
            owner.EnqueueMapEvent(presence.MapId, new YuoWorldEventDto("move", state, DateTimeOffset.UtcNow));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisconnectActorHandler(GameRuntime owner) : YuoActorRequestHandler<DisconnectActorMessage, bool>
    {
        protected override ValueTask<bool> RunAsync(
            YuoEntity entity,
            DisconnectActorMessage request,
            CancellationToken cancellationToken)
        {
            var presence = entity.GetComponent<YuoPlayerPresenceComponent>();
            if (!string.Equals(presence.SessionId, request.SessionId, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            var transform = entity.GetComponent<YuoPlayerTransformComponent>();
            presence.Online = false;
            presence.SessionId = "";
            presence.LastSeenAt = DateTimeOffset.UtcNow;

            owner.EnqueueAoiDirty(
                entity,
                presence.MapId,
                presence.MapId,
                transform.X,
                transform.Y,
                transform.Z,
                transform.X,
                transform.Y,
                transform.Z);
            var state = owner.UpdateWorldStateProjection(entity);
            owner.EnqueueMapEvent(presence.MapId, new YuoWorldEventDto("leave", state, DateTimeOffset.UtcNow));
            return ValueTask.FromResult(true);
        }
    }

    private sealed class ReadPlayerSnapshotActorHandler
        : YuoActorRequestHandler<ReadPlayerSnapshotActorMessage, YuoPlayerSnapshotDto>
    {
        protected override ValueTask<YuoPlayerSnapshotDto> RunAsync(
            YuoEntity entity,
            ReadPlayerSnapshotActorMessage request,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(Snapshot(entity));
        }
    }

    private sealed class ReadAoiObserverStateActorHandler
        : YuoActorRequestHandler<ReadAoiObserverStateActorMessage, GameAoiObserverState>
    {
        protected override ValueTask<GameAoiObserverState> RunAsync(
            YuoEntity entity,
            ReadAoiObserverStateActorMessage request,
            CancellationToken cancellationToken)
        {
            var player = entity.GetComponent<YuoIdlePlayerComponent>();
            var presence = entity.GetComponent<YuoPlayerPresenceComponent>();
            var transform = entity.GetComponent<YuoPlayerTransformComponent>();
            return ValueTask.FromResult(new GameAoiObserverState(
                player.PlayerId,
                presence.MapId,
                transform.X,
                transform.Y,
                transform.Z));
        }
    }
}
