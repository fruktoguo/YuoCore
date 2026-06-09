namespace YuoCore.StateSync;

/// <summary>
/// AOI 查询使用的轻量坐标。
/// StateSync 不依赖 Game 的 TransformComponent，因为客户端、单机和服务端可能有不同的移动实现。
/// 业务层只需要在同步前把当前位置投影到这里即可。
/// </summary>
public readonly record struct YuoAoiPosition(float X, float Y, float Z)
{
    public float DistanceSquaredTo(in YuoAoiPosition other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        var dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}

/// <summary>
/// 观察者描述。
/// Phase 用于副本、位面、隐身层等隔离规则；不同 Phase 默认互不可见。
/// </summary>
public readonly record struct YuoAoiObserver(
    long NetworkId,
    YuoAoiPosition Position,
    float ViewRadius,
    string Phase = "");

/// <summary>
/// 可被观察的同步目标。
/// 它只包含 AOI 必需数据，不包含 Entity 引用，便于未来客户端 SDK 或非 ECS 运行时复用。
/// </summary>
public readonly record struct YuoAoiTarget(
    long NetworkId,
    YuoAoiPosition Position,
    string Phase = "");

public readonly record struct YuoAoiVisibleEntity(long NetworkId, float DistanceSquared);

public interface IYuoAoiQuery
{
    IReadOnlyList<YuoAoiVisibleEntity> QueryVisible(
        YuoAoiObserver observer,
        IEnumerable<YuoAoiTarget> targets);
}

/// <summary>
/// 最小可用的网格 AOI 查询器。
/// 它吸收 ET AOI 的 Cell 思想，但保持为无状态查询：不维护订阅表，不发布 enter/leave 事件。
/// 这样 StateSync 可以先拥有稳定的可见性算法，后续 MapFiber 再决定是否缓存 cell 和生成事件。
/// </summary>
public sealed class YuoGridAoiQuery : IYuoAoiQuery
{
    private readonly float _cellSize;

    public YuoGridAoiQuery(float cellSize)
    {
        if (!YuoAoiMath.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cell size must be a finite value greater than zero.");
        }

        _cellSize = cellSize;
    }

    public IReadOnlyList<YuoAoiVisibleEntity> QueryVisible(
        YuoAoiObserver observer,
        IEnumerable<YuoAoiTarget> targets)
    {
        return YuoGridAoiTargetIndex.Create(_cellSize, targets).QueryVisible(observer);
    }
}

/// <summary>
/// 单帧 AOI target 网格索引。
/// 多个观察者查询同一批地图实体时，可以先构建一次索引再重复 QueryVisible，避免每个 observer 都重建 cell 字典。
/// 它是不可变快照，不维护订阅关系；MapFiber 可以在每帧移动结算后按需创建。
/// </summary>
public sealed class YuoGridAoiTargetIndex
{
    private readonly float _cellSize;
    private readonly Dictionary<YuoAoiCell, List<YuoAoiTarget>> _targetsByCell;

    private YuoGridAoiTargetIndex(
        float cellSize,
        Dictionary<YuoAoiCell, List<YuoAoiTarget>> targetsByCell)
    {
        _cellSize = cellSize;
        _targetsByCell = targetsByCell;
    }

    public static YuoGridAoiTargetIndex Create(float cellSize, IEnumerable<YuoAoiTarget> targets)
    {
        if (!YuoAoiMath.IsFinite(cellSize) || cellSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize), "cell size must be a finite value greater than zero.");
        }

        if (targets is null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        var index = new Dictionary<YuoAoiCell, List<YuoAoiTarget>>();
        foreach (var target in targets)
        {
            var cell = GetCell(target.Position, cellSize);
            if (!index.TryGetValue(cell, out var cellTargets))
            {
                cellTargets = [];
                index.Add(cell, cellTargets);
            }

            cellTargets.Add(target);
        }

        return new YuoGridAoiTargetIndex(cellSize, index);
    }

    public IReadOnlyList<YuoAoiVisibleEntity> QueryVisible(YuoAoiObserver observer)
    {
        if (!YuoAoiMath.IsFinite(observer.ViewRadius) || observer.ViewRadius <= 0)
        {
            return [];
        }

        var observerCell = GetCell(observer.Position, _cellSize);
        var cellRadius = (int)MathF.Ceiling(observer.ViewRadius / _cellSize);
        var radiusSquared = observer.ViewRadius * observer.ViewRadius;
        var visible = new List<YuoAoiVisibleEntity>();

        for (var x = observerCell.X - cellRadius; x <= observerCell.X + cellRadius; x++)
        {
            for (var y = observerCell.Y - cellRadius; y <= observerCell.Y + cellRadius; y++)
            {
                if (!_targetsByCell.TryGetValue(new YuoAoiCell(x, y), out var cellTargets))
                {
                    continue;
                }

                foreach (var target in cellTargets)
                {
                    if (target.NetworkId == observer.NetworkId)
                    {
                        continue;
                    }

                    if (!string.Equals(target.Phase, observer.Phase, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var distanceSquared = observer.Position.DistanceSquaredTo(target.Position);
                    if (distanceSquared <= radiusSquared)
                    {
                        visible.Add(new YuoAoiVisibleEntity(target.NetworkId, distanceSquared));
                    }
                }
            }
        }

        visible.Sort(static (a, b) =>
        {
            var distanceCompare = a.DistanceSquared.CompareTo(b.DistanceSquared);
            return distanceCompare != 0
                ? distanceCompare
                : a.NetworkId.CompareTo(b.NetworkId);
        });

        return visible;
    }

    /// <summary>
    /// 只统计可见目标数量，不创建结果列表，也不做排序。
    /// 适合 AOI 阈值判断、负载统计、订阅表预检查等场景；真正要下发实体列表时仍应使用 QueryVisible。
    /// </summary>
    public int CountVisible(YuoAoiObserver observer)
    {
        if (!YuoAoiMath.IsFinite(observer.ViewRadius) || observer.ViewRadius <= 0)
        {
            return 0;
        }

        var observerCell = GetCell(observer.Position, _cellSize);
        var cellRadius = (int)MathF.Ceiling(observer.ViewRadius / _cellSize);
        var radiusSquared = observer.ViewRadius * observer.ViewRadius;
        var count = 0;

        for (var x = observerCell.X - cellRadius; x <= observerCell.X + cellRadius; x++)
        {
            for (var y = observerCell.Y - cellRadius; y <= observerCell.Y + cellRadius; y++)
            {
                if (!_targetsByCell.TryGetValue(new YuoAoiCell(x, y), out var cellTargets))
                {
                    continue;
                }

                foreach (var target in cellTargets)
                {
                    if (target.NetworkId == observer.NetworkId)
                    {
                        continue;
                    }

                    if (!string.Equals(target.Phase, observer.Phase, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (observer.Position.DistanceSquaredTo(target.Position) <= radiusSquared)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static YuoAoiCell GetCell(in YuoAoiPosition position, float cellSize)
    {
        return new YuoAoiCell(
            (int)MathF.Floor(position.X / cellSize),
            (int)MathF.Floor(position.Z / cellSize));
    }

    private readonly record struct YuoAoiCell(int X, int Y);
}

internal static class YuoAoiMath
{
    public static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
