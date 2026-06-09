namespace YuoCore.StateSync;

/// <summary>
/// 网格 AOI stream 所在的 cell。
/// 它是纯坐标结果，不包含观察者、连接或 Entity 引用。
/// </summary>
public readonly record struct YuoAoiStreamCell(long MapId, int X, int Z, string Phase);

/// <summary>
/// 把地图位置映射为稳定 StateSync streamId。
/// 正式 MMO 可以用它把同一 AOI cell 内的观察者订阅到同一条 map/aoi stream；
/// 当前 Backend 示例仍使用玩家观察流，避免在服务端尚未按 stream 聚合事件前误共享历史。
/// </summary>
public sealed class YuoGridAoiStreamResolver
{
    private readonly float _cellSize;

    public YuoGridAoiStreamResolver(float cellSize)
    {
        if (!YuoAoiMath.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cell size must be a finite value greater than zero.");
        }

        _cellSize = cellSize;
    }

    public float CellSize => _cellSize;

    public YuoAoiStreamCell ResolveCell(long mapId, in YuoAoiPosition position, string phase = "")
    {
        if (mapId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mapId), mapId, "Map id cannot be negative.");
        }

        return new YuoAoiStreamCell(
            mapId,
            (int)MathF.Floor(position.X / _cellSize),
            (int)MathF.Floor(position.Z / _cellSize),
            NormalizePhase(phase));
    }

    public string ResolveStreamId(long mapId, in YuoAoiPosition position, string phase = "")
    {
        var cell = ResolveCell(mapId, position, phase);
        return YuoStateSyncStreamIds.ForMapAoi(
            cell.MapId,
            $"{cell.Phase}.cell_{cell.X}_{cell.Z}");
    }

    public string ResolveStreamId(long mapId, YuoAoiObserver observer)
    {
        return ResolveStreamId(mapId, observer.Position, observer.Phase);
    }

    /// <summary>
    /// 尝试把本类生成的 AOI streamId 还原为网格 cell。
    /// 这只解析稳定命名，不访问 AOI 索引或运行时地图状态。
    /// </summary>
    public static bool TryParseStreamCell(string? streamId, out YuoAoiStreamCell cell)
    {
        cell = default;
        if (!YuoStateSyncStreamIds.TryParseMapAoi(streamId, out var mapId, out var aoiId))
        {
            return false;
        }

        const string marker = ".cell_";
        var index = aoiId.LastIndexOf(marker, StringComparison.Ordinal);
        if (index <= 0)
        {
            return false;
        }

        var phase = aoiId[..index];
        var coordinates = aoiId[(index + marker.Length)..].Split('_', 2);
        if (coordinates.Length != 2 ||
            !int.TryParse(coordinates[0], out var x) ||
            !int.TryParse(coordinates[1], out var z))
        {
            return false;
        }

        cell = new YuoAoiStreamCell(mapId, x, z, NormalizePhase(phase));
        return true;
    }

    private static string NormalizePhase(string phase)
    {
        return string.IsNullOrWhiteSpace(phase)
            ? "default"
            : phase.Trim();
    }
}
