using YuoCore.Ecs;

namespace YuoCore.Game;

public sealed class YuoIdlePlayerComponent : YuoComponent
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class YuoPlayerPresenceComponent : YuoComponent
{
    public long MapId { get; set; }
    public long UnitId { get; set; }
    public bool Online { get; set; }
    public string SessionId { get; set; } = "";
    public ulong LastInputSeq { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class YuoPlayerTransformComponent : YuoComponent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

public sealed class YuoIdleWalletComponent : YuoComponent
{
    public long Gold { get; set; }
    public long Exp { get; set; }
    public long Clicks { get; set; }
}

public sealed class YuoIdleProgressComponent : YuoComponent
{
    public int Level { get; set; } = 1;
    public long Power { get; set; } = 1;
    public DateTimeOffset LastTickAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class YuoRuntimeStatsComponent : YuoComponent
{
    public long TickCount { get; set; }
    public long ProcessedCommands { get; set; }
}
