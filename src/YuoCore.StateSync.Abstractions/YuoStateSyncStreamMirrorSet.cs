namespace YuoCore.StateSync;

/// <summary>
/// 单条 StateSync stream 的客户端游标。
/// 它只描述本地镜像已应用到哪里，不包含网络连接、订阅请求或服务端地址。
/// </summary>
public sealed record YuoStateSyncStreamCursor(
    string StreamId,
    long? LastTick,
    bool IsSnapshotRequired);

/// <summary>
/// 多 StateSync stream 的镜像集合。
/// 正式 MMO 中，一个客户端可能同时订阅玩家自身流、地图 AOI 流、队伍/战场兴趣流；
/// 每条流的 Tick、BaseTick 和缺口状态必须独立维护，不能混用单个 Mirror 的 LastTick。
/// </summary>
public sealed class YuoStateSyncStreamMirrorSet
{
    private readonly Dictionary<string, YuoStateSyncMirror> _mirrorsByStreamId =
        new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> StreamIds => _mirrorsByStreamId.Keys.ToArray();

    public int Count => _mirrorsByStreamId.Count;

    public YuoStateSyncMirror GetOrCreate(string streamId)
    {
        var normalized = NormalizeStreamId(streamId);
        if (!_mirrorsByStreamId.TryGetValue(normalized, out var mirror))
        {
            mirror = new YuoStateSyncMirror();
            _mirrorsByStreamId.Add(normalized, mirror);
        }

        return mirror;
    }

    public bool TryGet(string streamId, out YuoStateSyncMirror mirror)
    {
        var normalized = NormalizeStreamId(streamId);
        return _mirrorsByStreamId.TryGetValue(normalized, out mirror!);
    }

    public YuoStateSyncApplyResult ApplySnapshot(string streamId, YuoStateSnapshot snapshot)
    {
        return GetOrCreate(streamId).ApplySnapshot(snapshot);
    }

    public YuoStateSyncApplyResult ApplyDelta(string streamId, YuoStateDelta delta)
    {
        return GetOrCreate(streamId).ApplyDelta(delta);
    }

    public YuoStateSyncStreamCursor GetCursor(string streamId)
    {
        var normalized = NormalizeStreamId(streamId);
        if (!_mirrorsByStreamId.TryGetValue(normalized, out var mirror))
        {
            return new YuoStateSyncStreamCursor(normalized, null, IsSnapshotRequired: false);
        }

        return new YuoStateSyncStreamCursor(
            normalized,
            mirror.Table.LastTick,
            mirror.IsSnapshotRequired);
    }

    public bool Remove(string streamId)
    {
        return _mirrorsByStreamId.Remove(NormalizeStreamId(streamId));
    }

    public void Clear()
    {
        _mirrorsByStreamId.Clear();
    }

    private static string NormalizeStreamId(string streamId)
    {
        if (string.IsNullOrWhiteSpace(streamId))
        {
            throw new ArgumentException("StateSync stream id cannot be empty.", nameof(streamId));
        }

        return streamId.Trim();
    }
}
