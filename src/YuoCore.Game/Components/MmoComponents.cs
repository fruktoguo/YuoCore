using YuoCore.Ecs;

namespace YuoCore.Game;

/// <summary>
/// 玩家长期身份组件，适合挂在 PlayerActor 管理的 Entity 上。
/// 它不包含 WebSocket 连接信息，因为一个玩家可能断线重连，也可能从 Web 换到 Unity。
/// </summary>
public sealed class YuoPlayerComponent : YuoComponent
{
    public Guid PlayerId;
    public Guid AccountId;
    public string Name = string.Empty;
    public long CurrentMapId;
    public long CurrentUnitId;
}

/// <summary>
/// 地图组件，适合挂在 MapActor / MapFiber 的根实体上。
/// MapFiber 负责实时单位、AOI 和状态同步，不应该被 PlayerActor 直接跨线程修改。
/// </summary>
public sealed class YuoMapComponent : YuoComponent
{
    public long MapId;
    public string Name = string.Empty;
    public int ShardId;
}

/// <summary>
/// 场景实时单位组件，玩家、怪物、NPC 都可以通过 Unit 表达。
/// 长期角色数据仍在 PlayerActor，Unit 只表达当前地图里的实时状态。
/// </summary>
public sealed class YuoUnitComponent : YuoComponent
{
    public long UnitId;
    public Guid? PlayerId;
    public long MapId;
}

public sealed class YuoTransformComponent : YuoComponent
{
    public float X;
    public float Y;
    public float Z;
    public float RotationY;
}
