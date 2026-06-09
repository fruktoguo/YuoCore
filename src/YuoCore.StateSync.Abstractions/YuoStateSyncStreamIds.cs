namespace YuoCore.StateSync;

/// <summary>
/// StateSync 可靠流的稳定命名工具。
/// streamId 会出现在 Snapshot、Delta、Recover 和 ACK 中，用来区分客户端正在确认的是哪一条同步流。
/// 这里保持为纯字符串规则，避免基础 StateSync 模块依赖玩家对象、地图对象、Gateway 或网络连接。
/// </summary>
public static class YuoStateSyncStreamIds
{
    public const string PlayerPrefix = "player";
    public const string MapPrefix = "map";
    public const string AoiPrefix = "aoi";
    public const string InterestPrefix = "interest";

    /// <summary>
    /// 单玩家观察流。当前 Backend 示例使用该格式，后续可以平滑迁移到 AOI/地图流。
    /// </summary>
    public static string ForPlayer(Guid playerId)
    {
        return $"{PlayerPrefix}:{playerId:N}";
    }

    /// <summary>
    /// 整张地图的广播流。适合副本房间、低人数地图或服务端内部验证。
    /// </summary>
    public static string ForMap(long mapId)
    {
        if (mapId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId), mapId, "Map id cannot be negative.");
        }

        return $"{MapPrefix}:{mapId}";
    }

    /// <summary>
    /// 地图内 AOI/兴趣分区流。aoiId 由业务或 AOI 索引生成，但必须是稳定、可比较的短标识。
    /// </summary>
    public static string ForMapAoi(long mapId, string aoiId)
    {
        if (mapId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId), mapId, "Map id cannot be negative.");
        }

        return $"{MapPrefix}:{mapId}:{AoiPrefix}:{NormalizeSegment(aoiId, nameof(aoiId))}";
    }

    /// <summary>
    /// 通用兴趣组流。用于队伍、战场、排行榜频道等不一定能归属到单张地图的同步范围。
    /// </summary>
    public static string ForInterestGroup(string scope, string groupId)
    {
        return $"{InterestPrefix}:{NormalizeSegment(scope, nameof(scope))}:{NormalizeSegment(groupId, nameof(groupId))}";
    }

    /// <summary>
    /// 客户端未携带 streamId 时允许按默认流处理；一旦携带，则必须和服务端期望完全一致。
    /// </summary>
    public static bool MatchesOrEmpty(string expectedStreamId, string? requestedStreamId)
    {
        if (string.IsNullOrWhiteSpace(expectedStreamId))
        {
            throw new ArgumentException("Expected stream id cannot be empty.", nameof(expectedStreamId));
        }

        return string.IsNullOrWhiteSpace(requestedStreamId) ||
               string.Equals(expectedStreamId, requestedStreamId, StringComparison.Ordinal);
    }

    /// <summary>
    /// 尝试解析地图 AOI streamId。
    /// 只做字符串结构解析，不访问地图、AOI 索引或任何运行时状态。
    /// </summary>
    public static bool TryParseMapAoi(string? streamId, out long mapId, out string aoiId)
    {
        mapId = 0;
        aoiId = "";
        if (string.IsNullOrWhiteSpace(streamId))
        {
            return false;
        }

        var parts = streamId.Trim().Split(':', 4);
        if (parts.Length != 4 ||
            !string.Equals(parts[0], MapPrefix, StringComparison.Ordinal) ||
            !string.Equals(parts[2], AoiPrefix, StringComparison.Ordinal) ||
            !long.TryParse(parts[1], out mapId) ||
            mapId < 0 ||
            string.IsNullOrWhiteSpace(parts[3]))
        {
            mapId = 0;
            aoiId = "";
            return false;
        }

        aoiId = parts[3];
        return true;
    }

    private static string NormalizeSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("StateSync stream id segment cannot be empty.", parameterName);
        }

        var trimmed = value.Trim();
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')
            {
                continue;
            }

            throw new ArgumentException(
                "StateSync stream id segment can only contain letters, digits, '_', '-' or '.'.",
                parameterName);
        }

        return trimmed;
    }
}
