namespace YuoCore.Backend.WebSockets;

using YuoCore.StateSync;

/// <summary>
/// 一条已经发布到 replay history 的 AOI stream push。
/// Payload 是协议层 StateDeltaMessage 的序列化结果；发送给不同 session 时只需要重新分配 serverSeq。
/// </summary>
public sealed record GameAoiStreamPushItem(
    string StreamId,
    long MapId,
    long Tick,
    ReadOnlyMemory<byte> Payload);

/// <summary>
/// 当前 AOI frame 中可被观察的一条 active stream。
/// Cell 在 frame 发布时只解析一次，避免每个 session drain 时反复拆 streamId 字符串。
/// </summary>
public sealed record GameAoiStreamPushActiveStream(
    string StreamId,
    YuoAoiStreamCell Cell);

/// <summary>
/// 某个 map/tick 的 AOI 推送帧。
/// 它把“该 tick 已经生成并写入历史的 delta”和“当前活跃 stream 集合”一起缓存，供多个 session 复用。
/// </summary>
public sealed record GameAoiStreamPushFrame(
    long MapId,
    float CellSize,
    long Tick,
    IReadOnlyList<string> ActiveStreamIds,
    IReadOnlyList<GameAoiStreamPushActiveStream> ActiveStreams,
    IReadOnlyList<GameAoiStreamPushItem> Items)
{
    public static IReadOnlyList<GameAoiStreamPushActiveStream> CreateActiveStreams(
        IReadOnlyList<string> activeStreamIds)
    {
        ArgumentNullException.ThrowIfNull(activeStreamIds);

        var streams = new List<GameAoiStreamPushActiveStream>(activeStreamIds.Count);
        for (var i = 0; i < activeStreamIds.Count; i++)
        {
            var streamId = activeStreamIds[i];
            if (!YuoGridAoiStreamResolver.TryParseStreamCell(streamId, out var cell))
            {
                continue;
            }

            streams.Add(new GameAoiStreamPushActiveStream(streamId, cell));
        }

        return streams;
    }
}
