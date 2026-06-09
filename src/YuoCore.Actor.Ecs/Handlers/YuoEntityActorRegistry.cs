using System.Collections.Concurrent;
using YuoCore.Ecs;

namespace YuoCore.Actor;

/// <summary>
/// 进程内 ActorId 到 YuoEntity 的映射。
/// 它只保存当前进程内可直接访问的实体；跨进程目标应由 Location/Transport 路由，不应注册到这里。
/// </summary>
public sealed class YuoEntityActorRegistry
{
    private readonly ConcurrentDictionary<ActorId, YuoEntity> _entities = new();

    public void Register(ActorId actorId, YuoEntity entity)
    {
        // 只注册当前进程当前 Runtime 内的实体引用。
        // 远端实体必须通过 Location + Transport 定位，不能把对象引用塞进这个表。
        if (actorId.IsEmpty) throw new ArgumentException("Actor id cannot be empty.", nameof(actorId));
        ArgumentNullException.ThrowIfNull(entity);
        _entities[actorId] = entity;
    }

    public bool TryResolve(ActorId actorId, out YuoEntity entity)
    {
        if (_entities.TryGetValue(actorId, out entity!) && !entity.IsDisposed)
        {
            return true;
        }

        entity = null!;
        return false;
    }

    public bool Unregister(ActorId actorId)
    {
        return _entities.TryRemove(actorId, out _);
    }
}
