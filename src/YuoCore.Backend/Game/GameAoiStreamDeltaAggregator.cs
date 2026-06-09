using YuoCore.Game;
using YuoCore.StateSync;

namespace YuoCore.Backend.Game;

public sealed record YuoAoiStreamDeltaDto(
    string StreamId,
    long MapId,
    YuoStateDelta Delta);

/// <summary>
/// Backend 侧的 AOI stream delta 聚合器。
/// 它只消费 GameRuntime 暴露的快照 DTO，不持有连接，也不直接触碰 ECS Entity。
/// </summary>
public sealed class GameAoiStreamDeltaAggregator
{
    private readonly YuoStateStreamDeltaTracker _tracker = new();
    private readonly Dictionary<string, long> _activeStreamMapIds = new(StringComparer.Ordinal);

    public IReadOnlyList<YuoAoiStreamDeltaDto> BuildDeltas(
        IReadOnlyList<YuoAoiStreamSnapshotDto> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0)
        {
            if (_activeStreamMapIds.Count > 0)
            {
                throw new InvalidOperationException("Use BuildDeltas(tick, snapshots) when all AOI streams are empty.");
            }

            return [];
        }

        return BuildDeltas(snapshots.Max(static snapshot => snapshot.Tick), snapshots);
    }

    public IReadOnlyList<YuoAoiStreamDeltaDto> BuildDeltas(
        long tick,
        IReadOnlyList<YuoAoiStreamSnapshotDto> snapshots)
    {
        return BuildDeltasCore(tick, snapshots, emitUnchangedStreams: true);
    }

    public IReadOnlyList<YuoAoiStreamDeltaDto> BuildDirtyDeltas(
        long tick,
        IReadOnlyList<YuoAoiStreamSnapshotDto> snapshots)
    {
        return BuildDeltasCore(tick, snapshots, emitUnchangedStreams: false);
    }

    public IReadOnlyList<YuoAoiStreamDeltaDto> BuildDirtyDeltasForStreams(
        long tick,
        IReadOnlyList<YuoAoiStreamSnapshotDto> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "AOI stream delta tick must be positive.");
        }

        var deltas = new List<YuoAoiStreamDeltaDto>(snapshots.Count);
        foreach (var snapshot in snapshots.OrderBy(static item => item.StreamId, StringComparer.Ordinal))
        {
            if (snapshot.Tick != tick)
            {
                throw new InvalidOperationException("All AOI stream snapshots in one dirty pass must use the same tick.");
            }

            if (TryBuildDelta(snapshot, emitUnchangedStreams: false, out var delta))
            {
                deltas.Add(delta);
            }

            if (snapshot.Players.Count == 0)
            {
                _activeStreamMapIds.Remove(snapshot.StreamId);
                _tracker.RemoveStream(snapshot.StreamId);
            }
            else
            {
                _activeStreamMapIds[snapshot.StreamId] = snapshot.MapId;
            }
        }

        return deltas
            .OrderBy(delta => delta.StreamId, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<YuoAoiStreamDeltaDto> BuildDeltasCore(
        long tick,
        IReadOnlyList<YuoAoiStreamSnapshotDto> snapshots,
        bool emitUnchangedStreams)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "AOI stream delta tick must be positive.");
        }

        var snapshotsByStream = new Dictionary<string, YuoAoiStreamSnapshotDto>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots.OrderBy(static item => item.StreamId, StringComparer.Ordinal))
        {
            if (snapshot.Tick != tick)
            {
                throw new InvalidOperationException("All AOI stream snapshots in one aggregation pass must use the same tick.");
            }

            if (!snapshotsByStream.TryAdd(snapshot.StreamId, snapshot))
            {
                throw new InvalidOperationException($"Duplicate AOI stream snapshot: {snapshot.StreamId}.");
            }
        }

        var missingStreams = _activeStreamMapIds.Keys
            .Where(streamId => !snapshotsByStream.ContainsKey(streamId))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var deltas = new List<YuoAoiStreamDeltaDto>(snapshotsByStream.Count + missingStreams.Length);
        foreach (var snapshot in snapshotsByStream.Values)
        {
            if (TryBuildDelta(snapshot, emitUnchangedStreams, out var delta))
            {
                deltas.Add(delta);
            }
        }

        // 当前帧已经没有实体的旧 stream 也要跑一次空集合，
        // 否则客户端永远收不到这个 stream 内实体的 Leave。
        foreach (var streamId in missingStreams)
        {
            if (emitUnchangedStreams)
            {
                var delta = _tracker.BuildDelta(streamId, tick, []);
                deltas.Add(new YuoAoiStreamDeltaDto(streamId, _activeStreamMapIds[streamId], delta));
            }
            else if (_tracker.TryBuildDirtyDelta(streamId, tick, [], out var delta))
            {
                deltas.Add(new YuoAoiStreamDeltaDto(streamId, _activeStreamMapIds[streamId], delta));
            }

            _tracker.RemoveStream(streamId);
        }

        _activeStreamMapIds.Clear();
        foreach (var snapshot in snapshotsByStream.Values)
        {
            _activeStreamMapIds.Add(snapshot.StreamId, snapshot.MapId);
        }

        return deltas
            .OrderBy(delta => delta.StreamId, StringComparer.Ordinal)
            .ToArray();
    }

    public bool RemoveStream(string streamId)
    {
        _activeStreamMapIds.Remove(streamId);
        return _tracker.RemoveStream(streamId);
    }

    public void Clear()
    {
        _activeStreamMapIds.Clear();
        _tracker.Clear();
    }

    private bool TryBuildDelta(
        YuoAoiStreamSnapshotDto snapshot,
        bool emitUnchangedStreams,
        out YuoAoiStreamDeltaDto delta)
    {
        var states = snapshot.Players
            .Select(GameStateSyncProjection.ToEntityState)
            .ToArray();
        if (emitUnchangedStreams)
        {
            delta = new YuoAoiStreamDeltaDto(
                snapshot.StreamId,
                snapshot.MapId,
                _tracker.BuildDeltaOrdered(snapshot.StreamId, snapshot.Tick, states));
            return true;
        }

        if (_tracker.TryBuildDirtyDeltaOrdered(snapshot.StreamId, snapshot.Tick, states, out var stateDelta))
        {
            delta = new YuoAoiStreamDeltaDto(snapshot.StreamId, snapshot.MapId, stateDelta);
            return true;
        }

        delta = default!;
        return false;
    }
}
