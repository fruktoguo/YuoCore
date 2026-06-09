using System.Collections.Concurrent;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Game;
using YuoCore.Gateway;
using YuoCore.Protocol;
using YuoCore.StateSync;
using YuoCore.StateSync.Protocol;

namespace YuoCore.Backend.WebSockets;

/// <summary>
/// Backend 示例的状态广播桥。
/// 它只把 GameRuntime 内已经排好的玩家事件转换为通用 StateDelta 推送；
/// 不持有 WebSocket，不直接访问 ECS Entity，也不承担 AOI/业务判定。
/// </summary>
public sealed class GameStateSyncPushDispatcher(
    GameRuntime runtime,
    IOptions<GameOptions>? options = null,
    IGameAoiVisibilityPolicy? aoiVisibilityPolicy = null)
{
    private const int DefaultReplayCapacity = 256;
    private const int LinearStreamLookupThreshold = 16;
    private static readonly DefaultGameAoiVisibilityPolicy DefaultAoiVisibilityPolicy = new();

    private readonly ConcurrentDictionary<Guid, long> _lastSnapshotMapBySession = new();
    private readonly ConcurrentDictionary<Guid, long> _lastStateTickBySession = new();
    private readonly ConcurrentDictionary<string, long> _nextStateTickByStream = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, YuoStateDeltaReplayBuffer> _deltaHistoryByStream = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, float> _aoiCellSizeByStream = new(StringComparer.Ordinal);
    private readonly float _defaultAoiCellSize = NormalizeAoiCellSize(options?.Value.StateSyncAoiCellSize ?? 10);
    private readonly int _aoiRecoverRadiusCells = Math.Max(0, options?.Value.StateSyncAoiRecoverRadiusCells ?? 1);
    private readonly IGameAoiVisibilityPolicy _aoiVisibilityPolicy = aoiVisibilityPolicy ?? DefaultAoiVisibilityPolicy;
    private readonly IGameAoiVisibilityQueryPolicy? _aoiVisibilityQueryPolicy =
        (aoiVisibilityPolicy ?? DefaultAoiVisibilityPolicy) as IGameAoiVisibilityQueryPolicy;
    private readonly YuoStateSyncSubscriptionTable _subscriptions = new();

    public IReadOnlyList<YuoGatewayOutboundMessage> DrainPushes(YuoGatewaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.PlayerId is not { } playerId)
        {
            return [];
        }
        var streamId = BindSessionStream(session, playerId);

        List<YuoGatewayOutboundMessage>? messages = null;
        if (TryCreateSnapshotPush(session, playerId, force: false, requestId: 0, out var snapshotPush))
        {
            messages = [snapshotPush];
        }

        var events = runtime.DrainEvents(playerId);
        if (events.Count == 0)
        {
            return messages ?? [];
        }

        var serverSeq = session.NextServerSeq();
        _lastStateTickBySession.TryGetValue(session.SessionId, out var baseTick);
        var delta = new StateDeltaMessage
        {
            // StateSync tick 是同步流的版本，不等于网络 envelope 的 serverSeq。
            // 这样断线重连后仍可用客户端携带的 knownTick 从短历史中 replay。
            Tick = AllocateStateTick(streamId),
            StreamId = streamId
        };
        if (baseTick > 0)
        {
            delta.BaseTick = baseTick;
        }

        foreach (var item in events)
        {
            delta.Changes.Add(ToStateChange(item));
        }
        _lastStateTickBySession[session.SessionId] = delta.Tick;
        GetReplayBuffer(streamId).Add(YuoStateSyncProtocolMapper.ToStateDelta(delta));

        var deltaPush = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.StateDelta,
            0,
            serverSeq,
            delta.ToByteArray());

        if (messages is null)
        {
            return [deltaPush];
        }

        messages.Add(deltaPush);
        return messages;
    }

    public IReadOnlyList<YuoGatewayOutboundMessage> DrainAoiStreamPushes(
        YuoGatewaySession session,
        long mapId,
        float cellSize,
        GameAoiStreamDeltaAggregator aggregator)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(aggregator);
        if (session.PlayerId is null)
        {
            return [];
        }

        var snapshots = runtime.BuildAoiStreamSnapshots(mapId, cellSize);
        var deltas = aggregator.BuildDeltas(snapshots);
        var frame = PublishAoiStreamFrame(mapId, cellSize, snapshots, deltas);
        return CreateAoiStreamSessionPushes(session, frame);
    }

    public GameAoiStreamPushFrame PublishAoiStreamFrame(
        long mapId,
        float cellSize,
        IReadOnlyList<YuoAoiStreamSnapshotDto> snapshots,
        IReadOnlyList<YuoAoiStreamDeltaDto> deltas)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(deltas);

        foreach (var snapshot in snapshots)
        {
            _aoiCellSizeByStream[snapshot.StreamId] = cellSize;
        }

        var activeStreamIds = snapshots
            .Select(static snapshot => snapshot.StreamId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var items = new List<GameAoiStreamPushItem>(deltas.Count);
        foreach (var item in deltas)
        {
            var message = YuoStateSyncProtocolMapper.ToProtocol(item.Delta);
            message.StreamId = item.StreamId;
            ObserveStateTick(item.StreamId, item.Delta.Tick);
            GetReplayBuffer(item.StreamId).Add(item.Delta);
            items.Add(new GameAoiStreamPushItem(
                item.StreamId,
                item.MapId,
                item.Delta.Tick,
                message.ToByteArray()));
        }

        var tick = deltas.Count > 0
            ? deltas.Max(static item => item.Delta.Tick)
            : snapshots.Count > 0
                ? snapshots.Max(static item => item.Tick)
                : 0;
        return new GameAoiStreamPushFrame(
            mapId,
            cellSize,
            tick,
            activeStreamIds,
            GameAoiStreamPushFrame.CreateActiveStreams(activeStreamIds),
            items);
    }

    public IReadOnlyList<YuoGatewayOutboundMessage> CreateAoiStreamSessionPushes(
        YuoGatewaySession session,
        GameAoiStreamPushFrame frame,
        bool filterToObservableStreams = false)
    {
        var pushes = new List<YuoGatewayOutboundMessage>();
        var count = CreateAoiStreamSessionPushes(session, frame, pushes, filterToObservableStreams);
        return count == 0 ? [] : pushes;
    }

    public int CreateAoiStreamSessionPushes(
        YuoGatewaySession session,
        GameAoiStreamPushFrame frame,
        List<YuoGatewayOutboundMessage> destination,
        bool filterToObservableStreams = false)
    {
        return CreateAoiStreamSessionPushes(
            session,
            frame,
            destination,
            observableStreamBuffer: null,
            filterToObservableStreams);
    }

    public int CreateAoiStreamSessionPushes(
        YuoGatewaySession session,
        GameAoiStreamPushFrame frame,
        List<YuoGatewayOutboundMessage> destination,
        List<string>? observableStreamBuffer,
        bool filterToObservableStreams = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(destination);
        var startCount = destination.Count;
        if (session.PlayerId is not { } playerId)
        {
            return 0;
        }

        IReadOnlyList<string> activeStreamIds;
        if (filterToObservableStreams)
        {
            observableStreamBuffer?.Clear();
            if (_aoiVisibilityQueryPolicy is not null)
            {
                if (!runtime.TryGetProjectedAoiObserverState(playerId, out var observerState))
                {
                    return 0;
                }

                // 默认 AOI 策略只需要 mapId 和坐标。
                // session drain 热路径读投影，不触发目标 Actor request / Fiber tick；
                // 否则 256 个 observer 会在同一帧重复 flush 多个 GameShard。
                activeStreamIds = observableStreamBuffer is null
                    ? ResolveObservableAoiStreamIds(observerState, frame, _aoiVisibilityQueryPolicy)
                    : ResolveObservableAoiStreamIds(observerState, frame, observableStreamBuffer, _aoiVisibilityQueryPolicy);
            }
            else if (runtime.TryGetSnapshot(playerId, out var playerSnapshot))
            {
                // 自定义旧策略仍然可以基于完整玩家快照做复杂业务判定。
                activeStreamIds = observableStreamBuffer is null
                    ? ResolveObservableAoiStreamIds(playerId, playerSnapshot, frame)
                    : ResolveObservableAoiStreamIds(playerId, playerSnapshot, frame, observableStreamBuffer);
            }
            else
            {
                return 0;
            }
        }
        else
        {
            activeStreamIds = frame.ActiveStreamIds;
        }

        var activeStreamSet = filterToObservableStreams && activeStreamIds.Count > LinearStreamLookupThreshold
            ? activeStreamIds.ToHashSet(StringComparer.Ordinal)
            : null;

        // 这个旁路入口先把 session 订阅到当前活跃 AOI stream。
        // 消失的旧 stream 仍会收到本次 Leave delta，但不会继续保留在活跃订阅集合里。
        var subscriptionChange = BindSessionStreams(session, activeStreamIds);

        var expectedDeltaPushes = filterToObservableStreams ? activeStreamIds.Count : frame.Items.Count;
        destination.EnsureCapacity(destination.Count + expectedDeltaPushes + (subscriptionChange.HasChanges ? 1 : 0));
        if (subscriptionChange.HasChanges)
        {
            var subscription = new StateSubscriptionMessage
            {
                Reason = "aoi_streams_changed",
                ResetMissing = true
            };
            subscription.StreamIds.Add(activeStreamIds);
            destination.Add(new YuoGatewayOutboundMessage(
                1,
                YuoCoreProtocolOpCodes.StateSubscription,
                0,
                session.NextServerSeq(),
                subscription.ToByteArray()));
        }

        foreach (var item in frame.Items)
        {
            if (filterToObservableStreams && !ContainsObservableStream(activeStreamIds, activeStreamSet, item.StreamId))
            {
                continue;
            }

            destination.Add(new YuoGatewayOutboundMessage(
                1,
                YuoCoreProtocolOpCodes.StateDelta,
                0,
                session.NextServerSeq(),
                item.Payload));
        }

        return destination.Count - startCount;
    }

    private IReadOnlyList<string> ResolveObservableAoiStreamIds(
        Guid playerId,
        YuoPlayerSnapshotDto playerSnapshot,
        GameAoiStreamPushFrame frame)
    {
        List<string>? streams = null;
        foreach (var stream in frame.ActiveStreams)
        {
            if (CanObserveAoiStream(playerId, playerSnapshot, stream))
            {
                streams ??= new List<string>(Math.Min(frame.ActiveStreams.Count, 16));
                streams.Add(stream.StreamId);
            }
        }

        return streams ?? [];
    }

    private IReadOnlyList<string> ResolveObservableAoiStreamIds(
        GameAoiObserverState observerState,
        GameAoiStreamPushFrame frame,
        IGameAoiVisibilityQueryPolicy visibilityPolicy)
    {
        List<string>? streams = null;
        foreach (var stream in frame.ActiveStreams)
        {
            if (CanObserveAoiStream(observerState, stream, visibilityPolicy))
            {
                streams ??= new List<string>(Math.Min(frame.ActiveStreams.Count, 16));
                streams.Add(stream.StreamId);
            }
        }

        return streams ?? [];
    }

    private IReadOnlyList<string> ResolveObservableAoiStreamIds(
        Guid playerId,
        YuoPlayerSnapshotDto playerSnapshot,
        GameAoiStreamPushFrame frame,
        List<string> destination)
    {
        destination.Clear();
        foreach (var stream in frame.ActiveStreams)
        {
            if (CanObserveAoiStream(playerId, playerSnapshot, stream))
            {
                destination.Add(stream.StreamId);
            }
        }

        return destination;
    }

    private IReadOnlyList<string> ResolveObservableAoiStreamIds(
        GameAoiObserverState observerState,
        GameAoiStreamPushFrame frame,
        List<string> destination,
        IGameAoiVisibilityQueryPolicy visibilityPolicy)
    {
        destination.Clear();
        foreach (var stream in frame.ActiveStreams)
        {
            if (CanObserveAoiStream(observerState, stream, visibilityPolicy))
            {
                destination.Add(stream.StreamId);
            }
        }

        return destination;
    }

    private static bool ContainsObservableStream(
        IReadOnlyList<string> activeStreamIds,
        HashSet<string>? activeStreamSet,
        string streamId)
    {
        if (activeStreamSet is not null)
        {
            return activeStreamSet.Contains(streamId);
        }

        for (var i = 0; i < activeStreamIds.Count; i++)
        {
            if (string.Equals(activeStreamIds[i], streamId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public void ClearSession(Guid sessionId)
    {
        _lastSnapshotMapBySession.TryRemove(sessionId, out _);
        _lastStateTickBySession.TryRemove(sessionId, out _);
        _subscriptions.RemoveSubscriber(ToSubscriberId(sessionId));
    }

    public bool TryGetAcknowledgedTick(Guid sessionId, out long ackTick)
    {
        var subscriberId = ToSubscriberId(sessionId);
        foreach (var streamId in _subscriptions.GetStreams(subscriberId))
        {
            if (_subscriptions.TryGetAckTick(subscriberId, streamId, out ackTick))
            {
                return true;
            }
        }

        ackTick = 0;
        return false;
    }

    public bool TryCreateAckResponse(
        YuoGatewaySession session,
        long ackTick,
        string? streamId,
        ulong requestId,
        out YuoGatewayOutboundMessage response)
    {
        ArgumentNullException.ThrowIfNull(session);

        response = default!;
        if (session.PlayerId is not { } playerId)
        {
            return false;
        }
        var expectedStreamId = ResolveRequestStreamId(session, playerId, streamId);
        if (expectedStreamId is null)
        {
            var rejectedStreamId = string.IsNullOrWhiteSpace(streamId)
                ? ResolveStreamId(playerId)
                : streamId.Trim();
            response = new YuoGatewayOutboundMessage(
                1,
                YuoCoreProtocolOpCodes.StateSyncAck,
                requestId,
                session.NextServerSeq(),
                new StateSyncAckResponse
                {
                    AckTick = ackTick,
                    StreamId = rejectedStreamId,
                    Accepted = false,
                    Reason = "stream_id_mismatch"
                }.ToByteArray());
            return true;
        }

        _nextStateTickByStream.TryGetValue(expectedStreamId, out var latestTick);
        var message = new StateSyncAckResponse
        {
            AckTick = ackTick,
            LatestTick = latestTick,
            StreamId = expectedStreamId
        };

        if (ackTick <= 0)
        {
            message.Accepted = false;
            message.Reason = "invalid_ack_tick";
        }
        else if (latestTick > 0 && ackTick > latestTick)
        {
            message.Accepted = false;
            message.Reason = "ack_tick_is_newer_than_server_stream";
        }
        else
        {
            message.Accepted = true;
            message.Reason = "accepted";
            _subscriptions.Acknowledge(ToSubscriberId(session.SessionId), expectedStreamId, ackTick);
            PruneReplayHistoryIfAllActiveSessionsAcked(expectedStreamId);
        }

        response = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.StateSyncAck,
            requestId,
            session.NextServerSeq(),
            message.ToByteArray());
        return true;
    }

    public bool TryCreateRecoverResponse(
        YuoGatewaySession session,
        long knownTick,
        string? streamId,
        ulong requestId,
        out YuoGatewayOutboundMessage response)
    {
        ArgumentNullException.ThrowIfNull(session);

        response = default!;
        if (session.PlayerId is not { } playerId)
        {
            return false;
        }
        var expectedStreamId = ResolveRequestStreamId(session, playerId, streamId);
        if (expectedStreamId is null)
        {
            var rejectedStreamId = string.IsNullOrWhiteSpace(streamId)
                ? ResolveStreamId(playerId)
                : streamId.Trim();
            response = new YuoGatewayOutboundMessage(
                1,
                YuoCoreProtocolOpCodes.StateSyncRecover,
                requestId,
                session.NextServerSeq(),
                new StateSyncRecoverResponse
                {
                    StreamId = rejectedStreamId,
                    Replayed = false,
                    Reason = "stream_not_observable"
                }.ToByteArray());
            return true;
        }

        var recover = new StateSyncRecoverResponse
        {
            StreamId = expectedStreamId
        };
        var replay = knownTick > 0
            ? GetReplayBuffer(expectedStreamId).ReplayAfter(knownTick)
            : new YuoStateDeltaReplayResult(
                YuoStateDeltaReplayStatus.MissingHistory,
                [],
                knownTick,
                null,
                null,
                "known tick is empty; full snapshot is required.");
        if (replay.CanReplay)
        {
            recover.Replayed = true;
            recover.Reason = replay.Status.ToString();
            foreach (var delta in replay.Deltas)
            {
                var message = YuoStateSyncProtocolMapper.ToProtocol(delta);
                message.StreamId = expectedStreamId;
                recover.Deltas.Add(message);
            }

            if (replay.Deltas.Count > 0)
            {
                _lastStateTickBySession[session.SessionId] = replay.Deltas[^1].Tick;
            }
            else if (knownTick > 0)
            {
                _lastStateTickBySession[session.SessionId] = knownTick;
            }

            response = new YuoGatewayOutboundMessage(
                1,
                YuoCoreProtocolOpCodes.StateSyncRecover,
                requestId,
                session.NextServerSeq(),
                recover.ToByteArray());
            return true;
        }

        var playerStreamId = ResolveStreamId(playerId);
        if (!string.Equals(expectedStreamId, playerStreamId, StringComparison.Ordinal))
        {
            recover.Replayed = false;
            recover.Reason = replay.Message ?? replay.Status.ToString();
            if (TryCreateAoiStreamSnapshot(expectedStreamId, out var aoiSnapshot))
            {
                recover.Snapshot = aoiSnapshot;
                _lastStateTickBySession[session.SessionId] = aoiSnapshot.Tick;
            }

            response = new YuoGatewayOutboundMessage(
                1,
                YuoCoreProtocolOpCodes.StateSyncRecover,
                requestId,
                session.NextServerSeq(),
                recover.ToByteArray());
            return true;
        }

        if (!TryCreateSnapshotPush(session, playerId, force: true, requestId, out var snapshotPush))
        {
            return false;
        }

        recover.Replayed = false;
        recover.Reason = replay.Message ?? replay.Status.ToString();
        recover.Snapshot = StateSnapshotMessage.Parser.ParseFrom(snapshotPush.Payload.Span);
        recover.Snapshot.StreamId = expectedStreamId;
        response = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.StateSyncRecover,
            requestId,
            snapshotPush.ServerSeq,
            recover.ToByteArray());
        return true;
    }

    private bool TryCreateAoiStreamSnapshot(string streamId, out StateSnapshotMessage snapshot)
    {
        snapshot = default!;
        if (!YuoStateSyncStreamIds.TryParseMapAoi(streamId, out var mapId, out _))
        {
            return false;
        }

        var cellSize = _aoiCellSizeByStream.TryGetValue(streamId, out var observedCellSize)
            ? observedCellSize
            : _defaultAoiCellSize;
        _aoiCellSizeByStream.TryAdd(streamId, cellSize);
        var streamSnapshot = runtime
            .BuildAoiStreamSnapshots(mapId, cellSize)
            .FirstOrDefault(item => string.Equals(item.StreamId, streamId, StringComparison.Ordinal));
        snapshot = new StateSnapshotMessage
        {
            Tick = AllocateStateTick(streamId),
            StreamId = streamId
        };
        if (streamSnapshot is not null)
        {
            foreach (var player in streamSnapshot.Players)
            {
                snapshot.Entities.Add(GameStateSyncProjection.ToNetworkEntityState(player));
            }
        }

        return true;
    }

    public bool TryCreateSnapshotPush(
        YuoGatewaySession session,
        out YuoGatewayOutboundMessage push,
        bool force = false,
        ulong requestId = 0)
    {
        ArgumentNullException.ThrowIfNull(session);

        push = default!;
        return session.PlayerId is { } playerId &&
               TryCreateSnapshotPush(session, playerId, force, requestId, out push);
    }

    private bool TryCreateSnapshotPush(
        YuoGatewaySession session,
        Guid playerId,
        bool force,
        ulong requestId,
        out YuoGatewayOutboundMessage push)
    {
        push = default!;
        if (!runtime.TryGetWorldSnapshot(playerId, out var snapshot))
        {
            return false;
        }
        var streamId = BindSessionStream(session, playerId);

        if (!force &&
            _lastSnapshotMapBySession.TryGetValue(session.SessionId, out var mapId) &&
            mapId == snapshot.MapId)
        {
            return false;
        }

        _lastSnapshotMapBySession[session.SessionId] = snapshot.MapId;
        var serverSeq = session.NextServerSeq();
        var message = new StateSnapshotMessage
        {
            Tick = AllocateStateTick(streamId),
            StreamId = streamId
        };
        _lastStateTickBySession[session.SessionId] = message.Tick;

        foreach (var player in snapshot.Players)
        {
            message.Entities.Add(GameStateSyncProjection.ToNetworkEntityState(player));
        }

        push = new YuoGatewayOutboundMessage(
            1,
            YuoCoreProtocolOpCodes.StateSnapshot,
            requestId,
            serverSeq,
            message.ToByteArray());
        return true;
    }

    private static StateChangeMessage ToStateChange(YuoWorldEventDto item)
    {
        var networkId = item.Player.UnitId;
        var kind = item.Kind switch
        {
            "enter" => StateChangeKind.Enter,
            "move" => StateChangeKind.Update,
            "leave" => StateChangeKind.Leave,
            _ => StateChangeKind.Update
        };

        return new StateChangeMessage
        {
            Kind = kind,
            NetworkId = networkId,
            State = kind is StateChangeKind.Leave or StateChangeKind.Destroy
                ? null
                : GameStateSyncProjection.ToNetworkEntityState(item.Player)
        };
    }

    private long AllocateStateTick(string streamId)
    {
        return _nextStateTickByStream.AddOrUpdate(streamId, 1, static (_, current) => checked(current + 1));
    }

    private void ObserveStateTick(string streamId, long tick)
    {
        _nextStateTickByStream.AddOrUpdate(streamId, tick, (_, current) => Math.Max(current, tick));
    }

    private YuoStateDeltaReplayBuffer GetReplayBuffer(string streamId)
    {
        return _deltaHistoryByStream.GetOrAdd(streamId, static _ => new YuoStateDeltaReplayBuffer(DefaultReplayCapacity));
    }

    public bool TryGetReplayHistoryStats(
        Guid playerId,
        out int count,
        out long? oldestTick,
        out long? newestTick)
    {
        return TryGetReplayHistoryStats(ResolveStreamId(playerId), out count, out oldestTick, out newestTick);
    }

    public bool TryGetReplayHistoryStats(
        string streamId,
        out int count,
        out long? oldestTick,
        out long? newestTick)
    {
        if (!_deltaHistoryByStream.TryGetValue(streamId, out var buffer))
        {
            count = 0;
            oldestTick = null;
            newestTick = null;
            return false;
        }

        count = buffer.Count;
        oldestTick = buffer.OldestTick;
        newestTick = buffer.NewestTick;
        return true;
    }

    private string BindSessionStream(YuoGatewaySession session, Guid playerId)
    {
        var streamId = ResolveStreamId(playerId);
        BindSessionStreams(session, [streamId]);
        return streamId;
    }

    private YuoStateSyncSubscriptionChange BindSessionStreams(YuoGatewaySession session, IEnumerable<string> streamIds)
    {
        return _subscriptions.SetSubscriberStreams(ToSubscriberId(session.SessionId), streamIds);
    }

    private void PruneReplayHistoryIfAllActiveSessionsAcked(string streamId)
    {
        if (!_subscriptions.TryGetSafePruneTick(streamId, out var pruneTick))
        {
            return;
        }

        GetReplayBuffer(streamId).PruneThrough(pruneTick);
    }

    private string? ResolveRequestStreamId(
        YuoGatewaySession session,
        Guid playerId,
        string? requestedStreamId)
    {
        var playerStreamId = ResolveStreamId(playerId);
        if (string.IsNullOrWhiteSpace(requestedStreamId))
        {
            return BindSessionStream(session, playerId);
        }

        var requested = requestedStreamId.Trim();
        if (string.Equals(requested, playerStreamId, StringComparison.Ordinal))
        {
            _subscriptions.Subscribe(ToSubscriberId(session.SessionId), playerStreamId);
            return playerStreamId;
        }

        var subscriberId = ToSubscriberId(session.SessionId);
        if (_subscriptions.GetStreams(subscriberId).Contains(requested, StringComparer.Ordinal))
        {
            return requested;
        }

        if (CanRecoverAoiStream(playerId, requested))
        {
            _subscriptions.Subscribe(subscriberId, requested);
            return requested;
        }

        return null;
    }

    private bool CanRecoverAoiStream(Guid playerId, string streamId)
    {
        return CanObserveAoiStream(playerId, streamId);
    }

    private bool CanObserveAoiStream(Guid playerId, string streamId)
    {
        if (!YuoGridAoiStreamResolver.TryParseStreamCell(streamId, out var requestedCell) ||
            !runtime.TryGetSnapshot(playerId, out var snapshot))
        {
            return false;
        }

        var cellSize = _aoiCellSizeByStream.TryGetValue(streamId, out var observedCellSize)
            ? observedCellSize
            : _defaultAoiCellSize;
        var context = new GameAoiVisibilityContext(
            playerId,
            snapshot,
            streamId,
            requestedCell,
            cellSize,
            _aoiRecoverRadiusCells);
        return _aoiVisibilityPolicy.CanRecoverStream(context);
    }

    private bool CanObserveAoiStream(Guid playerId, YuoPlayerSnapshotDto snapshot, string streamId)
    {
        if (!YuoGridAoiStreamResolver.TryParseStreamCell(streamId, out var requestedCell))
        {
            return false;
        }

        var cellSize = _aoiCellSizeByStream.TryGetValue(streamId, out var observedCellSize)
            ? observedCellSize
            : _defaultAoiCellSize;
        var context = new GameAoiVisibilityContext(
            playerId,
            snapshot,
            streamId,
            requestedCell,
            cellSize,
            _aoiRecoverRadiusCells);
        return _aoiVisibilityPolicy.CanRecoverStream(context);
    }

    private bool CanObserveAoiStream(
        Guid playerId,
        YuoPlayerSnapshotDto snapshot,
        GameAoiStreamPushActiveStream stream)
    {
        var cellSize = _aoiCellSizeByStream.TryGetValue(stream.StreamId, out var observedCellSize)
            ? observedCellSize
            : _defaultAoiCellSize;
        var context = new GameAoiVisibilityContext(
            playerId,
            snapshot,
            stream.StreamId,
            stream.Cell,
            cellSize,
            _aoiRecoverRadiusCells);
        return _aoiVisibilityPolicy.CanRecoverStream(context);
    }

    private bool CanObserveAoiStream(
        GameAoiObserverState observerState,
        GameAoiStreamPushActiveStream stream,
        IGameAoiVisibilityQueryPolicy visibilityPolicy)
    {
        var cellSize = _aoiCellSizeByStream.TryGetValue(stream.StreamId, out var observedCellSize)
            ? observedCellSize
            : _defaultAoiCellSize;
        var context = new GameAoiVisibilityQueryContext(
            observerState,
            stream.StreamId,
            stream.Cell,
            cellSize,
            _aoiRecoverRadiusCells);
        return visibilityPolicy.CanRecoverStream(context);
    }

    private static float NormalizeAoiCellSize(float cellSize)
    {
        if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0)
        {
            return 10;
        }

        return cellSize;
    }

    private static string ResolveStreamId(Guid playerId)
    {
        return YuoStateSyncStreamIds.ForPlayer(playerId);
    }

    private static string ToSubscriberId(Guid sessionId)
    {
        return sessionId.ToString("N");
    }
}
