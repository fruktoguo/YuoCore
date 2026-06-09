using YuoCore.Ecs;

namespace YuoCore.StateSync;

public interface IYuoStateSnapshotBuilder
{
    YuoStateSnapshot BuildFullSnapshot(long tick, IEnumerable<YuoEntity> entities);
}

public interface IYuoStateDeltaBuilder
{
    YuoStateDelta BuildDirtyDelta(long tick, IEnumerable<YuoEntity> entities, bool clearDirty = true);
}

/// <summary>
/// 最小可用的全量快照构建器。
/// 它只读取 NetworkIdentity + NetworkState 两个同步组件，因此不会依赖任何具体游戏业务组件。
/// 这个边界很重要：StateSync 负责“同步形状”，Game 负责“业务状态怎么写入 Payload”。
/// </summary>
public sealed class YuoFullStateSnapshotBuilder : IYuoStateSnapshotBuilder
{
    public YuoStateSnapshot BuildFullSnapshot(long tick, IEnumerable<YuoEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var states = new List<YuoEntityState>();
        foreach (var entity in entities)
        {
            if (!entity.TryGetComponent<YuoNetworkIdentityComponent>(out var identity))
            {
                continue;
            }

            if (!entity.TryGetComponent<YuoNetworkStateComponent>(out var state))
            {
                continue;
            }

            states.Add(new YuoEntityState(
                identity.NetworkId,
                identity.Kind,
                state.Version,
                state.Payload));
        }

        return new YuoStateSnapshot(tick, states);
    }
}

/// <summary>
/// 最小可用的 Dirty Delta 构建器。
/// 它只发送被标记为 dirty 的 NetworkState，适合先把“全量快照”和“增量更新”的职责分开；
/// AOI、压缩、字段级差分后续作为可选能力继续扩展。
/// </summary>
public sealed class YuoDirtyStateDeltaBuilder : IYuoStateDeltaBuilder
{
    public YuoStateDelta BuildDirtyDelta(long tick, IEnumerable<YuoEntity> entities, bool clearDirty = true)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var changes = new List<YuoStateChange>();
        foreach (var entity in entities)
        {
            if (!entity.TryGetComponent<YuoNetworkIdentityComponent>(out var identity))
            {
                continue;
            }

            if (!entity.TryGetComponent<YuoNetworkStateComponent>(out var state) || !state.IsDirty)
            {
                continue;
            }

            var entityState = new YuoEntityState(
                identity.NetworkId,
                identity.Kind,
                state.Version,
                state.Payload);
            changes.Add(new YuoStateChange(YuoStateChangeKind.Update, entityState, identity.NetworkId));

            if (clearDirty)
            {
                state.ClearDirty();
            }
        }

        return new YuoStateDelta(tick, changes);
    }
}
