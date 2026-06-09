namespace YuoCore.Backend.Game;

public sealed class GameOptions
{
    public int TickRate { get; init; } = 10;
    public int ShardCount { get; init; } = 4;
    public int MaxTickConcurrency { get; init; } = 0;
    public int ChannelCapacity { get; init; } = 262_144;
    public long BaseGoldPerSecond { get; init; } = 10;
    public long BaseExpPerSecond { get; init; } = 5;
    public int PersistEveryTicks { get; init; } = 50;
    public bool GatewayRequestsUseCommandQueue { get; init; } = true;
    public bool WorldEventsEnabled { get; init; } = true;
    public float StateSyncAoiCellSize { get; init; } = 10;
    public int StateSyncAoiRecoverRadiusCells { get; init; } = 1;
}
