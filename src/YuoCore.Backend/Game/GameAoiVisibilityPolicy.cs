using YuoCore.Game;
using YuoCore.StateSync;

namespace YuoCore.Backend.Game;

/// <summary>
/// AOI stream 可见性判定上下文。
/// 这里使用 Backend 示例的 DTO 和纯 StateSync cell，不暴露 WebSocket、Session 或 ECS Entity。
/// 正式 MMO 可以在这一层追加地图配置、相位、阵营、隐身、权限等业务输入。
/// </summary>
public readonly record struct GameAoiVisibilityContext(
    Guid ObserverPlayerId,
    YuoPlayerSnapshotDto ObserverSnapshot,
    string RequestedStreamId,
    YuoAoiStreamCell RequestedCell,
    float CellSize,
    int RecoverRadiusCells);

/// <summary>
/// AOI 可见性热路径使用的轻量观察者状态。
/// 默认策略只需要地图和坐标，不需要完整玩家快照；保留 DTO 版本是为了兼容自定义策略。
/// </summary>
public readonly record struct GameAoiObserverState(
    Guid PlayerId,
    long MapId,
    float X,
    float Y,
    float Z);

/// <summary>
/// 不依赖完整玩家快照的 AOI stream 可见性判定上下文。
/// </summary>
public readonly record struct GameAoiVisibilityQueryContext(
    GameAoiObserverState Observer,
    string RequestedStreamId,
    YuoAoiStreamCell RequestedCell,
    float CellSize,
    int RecoverRadiusCells);

/// <summary>
/// Backend/Map 侧 AOI stream 可见性策略。
/// StateSync 核心只负责流、快照和 delta；是否允许某玩家恢复某条 AOI stream 属于游戏业务规则。
/// </summary>
public interface IGameAoiVisibilityPolicy
{
    bool CanRecoverStream(GameAoiVisibilityContext context);
}

/// <summary>
/// AOI session drain 热路径的轻量策略接口。
/// 实现该接口的策略可以避免为每个 session drain 分配完整 YuoPlayerSnapshotDto。
/// </summary>
public interface IGameAoiVisibilityQueryPolicy : IGameAoiVisibilityPolicy
{
    bool CanRecoverStream(GameAoiVisibilityQueryContext context);
}

/// <summary>
/// 默认网格 AOI 可见性策略。
/// 它完整保留当前示例行为：玩家只能恢复同地图、当前 cell 附近 recover 半径内的 AOI stream。
/// </summary>
public sealed class DefaultGameAoiVisibilityPolicy : IGameAoiVisibilityQueryPolicy
{
    public bool CanRecoverStream(GameAoiVisibilityContext context)
    {
        return CanRecoverStream(new GameAoiVisibilityQueryContext(
            new GameAoiObserverState(
                context.ObserverPlayerId,
                context.ObserverSnapshot.MapId,
                context.ObserverSnapshot.X,
                context.ObserverSnapshot.Y,
                context.ObserverSnapshot.Z),
            context.RequestedStreamId,
            context.RequestedCell,
            context.CellSize,
            context.RecoverRadiusCells));
    }

    public bool CanRecoverStream(GameAoiVisibilityQueryContext context)
    {
        if (context.Observer.MapId != context.RequestedCell.MapId)
        {
            return false;
        }

        // 这个默认策略位于 AOI session drain 热路径。
        // 直接按 cellSize 计算当前格子，避免为每个 observer/stream 判定创建 resolver 对象。
        var currentX = ResolveAxis(context.Observer.X, context.CellSize);
        var currentZ = ResolveAxis(context.Observer.Z, context.CellSize);
        return Math.Abs((long)currentX - context.RequestedCell.X) <= context.RecoverRadiusCells &&
               Math.Abs((long)currentZ - context.RequestedCell.Z) <= context.RecoverRadiusCells;
    }

    private static int ResolveAxis(float value, float cellSize)
    {
        return (int)MathF.Floor(value / cellSize);
    }
}
