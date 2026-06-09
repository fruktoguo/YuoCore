using YuoCore.Ecs;

namespace YuoCore.StateSync;

/// <summary>
/// ECS 场景里的 AOI 投影组件。
/// 它不是移动组件本身，只是 StateSync 查询需要的最小同步视图。
/// </summary>
public sealed class YuoAoiComponent : YuoComponent
{
    public float X;
    public float Y;
    public float Z;
    public float ViewRadius;
    public string Phase = string.Empty;
    public bool CanObserve = true;

    public YuoAoiPosition Position => new(X, Y, Z);

    public void SetPosition(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// <summary>
/// 从 YuoEntity 中读取 NetworkIdentity + AoiComponent，并委托给底层 AOI 查询器。
/// 业务系统可以直接传入当前 MapFiber 内的一组实体，不需要 Gateway 或协议层参与。
/// </summary>
public sealed class YuoEntityAoiQuery(IYuoAoiQuery inner)
{
    public IReadOnlyList<YuoAoiVisibleEntity> QueryVisible(
        YuoEntity observer,
        IEnumerable<YuoEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(entities);

        if (!TryCreateObserver(observer, out var aoiObserver))
        {
            return [];
        }

        return inner.QueryVisible(aoiObserver, EnumerateTargets(entities));
    }

    private static bool TryCreateObserver(YuoEntity entity, out YuoAoiObserver observer)
    {
        observer = default;

        if (!entity.TryGetComponent<YuoNetworkIdentityComponent>(out var identity))
        {
            return false;
        }

        if (!entity.TryGetComponent<YuoAoiComponent>(out var aoi) || !aoi.CanObserve)
        {
            return false;
        }

        observer = new YuoAoiObserver(identity.NetworkId, aoi.Position, aoi.ViewRadius, aoi.Phase);
        return true;
    }

    private static IEnumerable<YuoAoiTarget> EnumerateTargets(IEnumerable<YuoEntity> entities)
    {
        foreach (var entity in entities)
        {
            if (!entity.TryGetComponent<YuoNetworkIdentityComponent>(out var identity))
            {
                continue;
            }

            if (!entity.TryGetComponent<YuoAoiComponent>(out var aoi))
            {
                continue;
            }

            yield return new YuoAoiTarget(identity.NetworkId, aoi.Position, aoi.Phase);
        }
    }
}
