using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Gateway;
using YuoCore.Protocol;
using YuoCore.StateSync;

namespace YuoCore.Backend.WebSockets;

/// <summary>
/// Backend 示例的 AOI 推送协调器。
/// 它把“每个 map/tick 只生成一次 AOI delta 并写入 replay history”和“多个 session 复用该 frame 下发”分开。
/// 该类只处理 DTO 和 Gateway message，不持有 WebSocket，也不直接触碰 ECS Entity。
/// </summary>
public sealed class GameAoiStreamPushCoordinator
{
    private readonly object _gate = new();
    private readonly GameRuntime _runtime;
    private readonly GameStateSyncPushDispatcher _dispatcher;
    private readonly GameAoiStreamDeltaAggregator _aggregator = new();
    private readonly Dictionary<FrameKey, GameAoiStreamPushFrame> _lastFrameByMap = [];
    private readonly Dictionary<FrameKey, HashSet<string>> _dirtyStreamsByMap = [];
    private readonly Dictionary<SessionFrameKey, long> _lastSentFrameTickBySession = [];
    private readonly List<string> _observableStreamBuffer = new(16);
    private readonly float _cellSize;
    private readonly YuoGridAoiStreamResolver _streamResolver;
    private long _diagnosticDirtyEntriesConsumed;
    private long _diagnosticFullSnapshotPasses;
    private long _diagnosticDirtySnapshotPasses;
    private long _diagnosticSnapshotsBuilt;
    private long _diagnosticFramesPublished;
    private long _diagnosticDeltasPublished;
    private long _diagnosticPayloadBytesPublished;
    private long _diagnosticSessionDrainCalls;
    private long _diagnosticSameFrameSkips;
    private long _diagnosticSessionPushesCreated;
    private long _diagnosticSessionDeltaPushesCreated;
    private long _diagnosticSessionSubscriptionPushesCreated;

    public GameAoiStreamPushCoordinator(
        GameRuntime runtime,
        GameStateSyncPushDispatcher dispatcher,
        IOptions<GameOptions>? options = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _cellSize = NormalizeCellSize(options?.Value.StateSyncAoiCellSize ?? 10);
        _streamResolver = new YuoGridAoiStreamResolver(_cellSize);
    }

    public IReadOnlyList<YuoGatewayOutboundMessage> DrainPushes(YuoGatewaySession session)
    {
        var pushes = new List<YuoGatewayOutboundMessage>();
        var count = DrainPushes(session, pushes);
        return count == 0 ? [] : pushes;
    }

    public int DrainPushes(YuoGatewaySession session, List<YuoGatewayOutboundMessage> destination)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destination);
        if (session.PlayerId is not { } playerId ||
            !_runtime.TryGetProjectedAoiObserverState(playerId, out var observer) ||
            observer.MapId == 0)
        {
            return 0;
        }

        lock (_gate)
        {
            var startCount = destination.Count;
            _diagnosticSessionDrainCalls++;
            ConsumeDirtyPlayers();
            var frame = GetOrPublishFrame(observer.MapId);
            var sessionKey = new SessionFrameKey(session.SessionId, frame.MapId);
            if (_lastSentFrameTickBySession.TryGetValue(sessionKey, out var sentTick) &&
                sentTick == frame.Tick)
            {
                _diagnosticSameFrameSkips++;
                return 0;
            }

            var count = _dispatcher.CreateAoiStreamSessionPushes(
                session,
                frame,
                destination,
                _observableStreamBuffer,
                filterToObservableStreams: true);
            _lastSentFrameTickBySession[sessionKey] = frame.Tick;
            ObserveSessionPushes(destination, startCount, count);
            return count;
        }
    }

    public GameAoiStreamPushDiagnostics GetDiagnostics()
    {
        lock (_gate)
        {
            var dirtyStreamCount = 0;
            foreach (var streams in _dirtyStreamsByMap.Values)
            {
                dirtyStreamCount += streams.Count;
            }

            return new GameAoiStreamPushDiagnostics(
                DirtyEntriesConsumed: _diagnosticDirtyEntriesConsumed,
                FullSnapshotPasses: _diagnosticFullSnapshotPasses,
                DirtySnapshotPasses: _diagnosticDirtySnapshotPasses,
                SnapshotsBuilt: _diagnosticSnapshotsBuilt,
                FramesPublished: _diagnosticFramesPublished,
                DeltasPublished: _diagnosticDeltasPublished,
                PayloadBytesPublished: _diagnosticPayloadBytesPublished,
                SessionDrainCalls: _diagnosticSessionDrainCalls,
                SameFrameSkips: _diagnosticSameFrameSkips,
                SessionPushesCreated: _diagnosticSessionPushesCreated,
                SessionDeltaPushesCreated: _diagnosticSessionDeltaPushesCreated,
                SessionSubscriptionPushesCreated: _diagnosticSessionSubscriptionPushesCreated,
                CachedFrameCount: _lastFrameByMap.Count,
                DirtyStreamCount: dirtyStreamCount,
                SessionFrameTrackCount: _lastSentFrameTickBySession.Count);
        }
    }

    public void ClearSession(Guid sessionId)
    {
        lock (_gate)
        {
            foreach (var key in _lastSentFrameTickBySession.Keys
                         .Where(key => key.SessionId == sessionId)
                         .ToArray())
            {
                _lastSentFrameTickBySession.Remove(key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lastFrameByMap.Clear();
            _dirtyStreamsByMap.Clear();
            _lastSentFrameTickBySession.Clear();
            _aggregator.Clear();
            _diagnosticDirtyEntriesConsumed = 0;
            _diagnosticFullSnapshotPasses = 0;
            _diagnosticDirtySnapshotPasses = 0;
            _diagnosticSnapshotsBuilt = 0;
            _diagnosticFramesPublished = 0;
            _diagnosticDeltasPublished = 0;
            _diagnosticPayloadBytesPublished = 0;
            _diagnosticSessionDrainCalls = 0;
            _diagnosticSameFrameSkips = 0;
            _diagnosticSessionPushesCreated = 0;
            _diagnosticSessionDeltaPushesCreated = 0;
            _diagnosticSessionSubscriptionPushesCreated = 0;
        }
    }

    private GameAoiStreamPushFrame GetOrPublishFrame(long mapId)
    {
        var key = new FrameKey(mapId, _cellSize);
        var hasCachedFrame = _lastFrameByMap.TryGetValue(key, out var cached);
        var hasDirtyStreams = _dirtyStreamsByMap.TryGetValue(key, out var dirtyStreams) &&
                              dirtyStreams.Count > 0;
        if (hasCachedFrame && !hasDirtyStreams)
        {
            return cached!;
        }

        if (cached is not null && dirtyStreams is { Count: > 0 })
        {
            var dirtyStreamIds = dirtyStreams.Order(StringComparer.Ordinal).ToArray();
            var dirtySnapshots = _runtime.BuildAoiStreamSnapshots(mapId, _cellSize, dirtyStreamIds);
            _diagnosticDirtySnapshotPasses++;
            _diagnosticSnapshotsBuilt += dirtySnapshots.Count;
            if (dirtySnapshots.Count == 0)
            {
                _dirtyStreamsByMap.Remove(key);
                return cached;
            }

            var dirtyTick = dirtySnapshots.Max(static snapshot => snapshot.Tick);
            if (cached.Tick == dirtyTick)
            {
                _dirtyStreamsByMap.Remove(key);
                return cached;
            }

            var dirtyDeltas = _aggregator.BuildDirtyDeltasForStreams(dirtyTick, dirtySnapshots);
            var activeStreamIds = cached.ActiveStreamIds
                .Where(streamId => !dirtyStreamIds.Contains(streamId, StringComparer.Ordinal))
                .Concat(dirtySnapshots
                    .Where(static snapshot => snapshot.Players.Count > 0)
                    .Select(static snapshot => snapshot.StreamId))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var dirtyFrame = _dispatcher.PublishAoiStreamFrame(
                mapId,
                _cellSize,
                dirtySnapshots.Where(static snapshot => snapshot.Players.Count > 0).ToArray(),
                dirtyDeltas);
            dirtyFrame = dirtyFrame with
            {
                ActiveStreamIds = activeStreamIds,
                ActiveStreams = GameAoiStreamPushFrame.CreateActiveStreams(activeStreamIds)
            };
            _lastFrameByMap[key] = dirtyFrame;
            _dirtyStreamsByMap.Remove(key);
            ObservePublishedFrame(dirtyFrame);
            return dirtyFrame;
        }

        var snapshots = _runtime.BuildAoiStreamSnapshots(mapId, _cellSize);
        _diagnosticFullSnapshotPasses++;
        _diagnosticSnapshotsBuilt += snapshots.Count;
        if (snapshots.Count == 0)
        {
            var empty = new GameAoiStreamPushFrame(mapId, _cellSize, 0, [], [], []);
            _lastFrameByMap[key] = empty;
            _dirtyStreamsByMap.Remove(key);
            return empty;
        }

        var tick = snapshots.Max(static snapshot => snapshot.Tick);
        if (_lastFrameByMap.TryGetValue(key, out var sameTickFrame) && sameTickFrame.Tick == tick)
        {
            return sameTickFrame;
        }

        var deltas = _aggregator.BuildDirtyDeltas(tick, snapshots);
        var frame = _dispatcher.PublishAoiStreamFrame(mapId, _cellSize, snapshots, deltas);
        _lastFrameByMap[key] = frame;
        _dirtyStreamsByMap.Remove(key);
        ObservePublishedFrame(frame);
        return frame;
    }

    private void ConsumeDirtyPlayers()
    {
        foreach (var dirty in _runtime.DrainAoiDirtyPlayers(int.MaxValue))
        {
            _diagnosticDirtyEntriesConsumed++;
            if (dirty.PreviousMapId > 0)
            {
                AddDirtyStream(
                    new FrameKey(dirty.PreviousMapId, _cellSize),
                    _streamResolver.ResolveStreamId(
                        dirty.PreviousMapId,
                        new YuoAoiPosition(dirty.PreviousX, dirty.PreviousY, dirty.PreviousZ)));
            }

            if (dirty.CurrentMapId > 0)
            {
                AddDirtyStream(
                    new FrameKey(dirty.CurrentMapId, _cellSize),
                    _streamResolver.ResolveStreamId(
                        dirty.CurrentMapId,
                        new YuoAoiPosition(dirty.CurrentX, dirty.CurrentY, dirty.CurrentZ)));
            }
        }
    }

    private void AddDirtyStream(FrameKey key, string streamId)
    {
        if (!_dirtyStreamsByMap.TryGetValue(key, out var streams))
        {
            streams = new HashSet<string>(StringComparer.Ordinal);
            _dirtyStreamsByMap.Add(key, streams);
        }

        streams.Add(streamId);
    }

    private void ObservePublishedFrame(GameAoiStreamPushFrame frame)
    {
        _diagnosticFramesPublished++;
        _diagnosticDeltasPublished += frame.Items.Count;
        foreach (var item in frame.Items)
        {
            _diagnosticPayloadBytesPublished += item.Payload.Length;
        }
    }

    private void ObserveSessionPushes(IReadOnlyList<YuoGatewayOutboundMessage> pushes)
    {
        ObserveSessionPushes(pushes, 0, pushes.Count);
    }

    private void ObserveSessionPushes(
        IReadOnlyList<YuoGatewayOutboundMessage> pushes,
        int startIndex,
        int count)
    {
        _diagnosticSessionPushesCreated += count;
        var endIndex = startIndex + count;
        for (var i = startIndex; i < endIndex; i++)
        {
            var push = pushes[i];
            if (push.OpCode == YuoCoreProtocolOpCodes.StateDelta)
            {
                _diagnosticSessionDeltaPushesCreated++;
            }
            else if (push.OpCode == YuoCoreProtocolOpCodes.StateSubscription)
            {
                _diagnosticSessionSubscriptionPushesCreated++;
            }
        }
    }

    private static float NormalizeCellSize(float cellSize)
    {
        if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0)
        {
            return 10;
        }

        return cellSize;
    }

    private readonly record struct FrameKey(long MapId, float CellSize);

    private readonly record struct SessionFrameKey(Guid SessionId, long MapId);
}

/// <summary>
/// AOI stream 推送链路的只读诊断快照。
/// 这些计数只用于压测和问题定位，不参与业务判定，避免把性能观测和同步语义耦合在一起。
/// </summary>
public sealed record GameAoiStreamPushDiagnostics(
    long DirtyEntriesConsumed,
    long FullSnapshotPasses,
    long DirtySnapshotPasses,
    long SnapshotsBuilt,
    long FramesPublished,
    long DeltasPublished,
    long PayloadBytesPublished,
    long SessionDrainCalls,
    long SameFrameSkips,
    long SessionPushesCreated,
    long SessionDeltaPushesCreated,
    long SessionSubscriptionPushesCreated,
    int CachedFrameCount,
    int DirtyStreamCount,
    int SessionFrameTrackCount);
