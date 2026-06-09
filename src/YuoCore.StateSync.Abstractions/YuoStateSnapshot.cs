namespace YuoCore.StateSync;

/// <summary>
/// 单个实体的网络同步状态快照。
/// 这一层只保存稳定网络标识和已序列化 payload，不依赖 ECS、Unity 表现层或具体游戏组件。
/// </summary>
public sealed record YuoEntityState(
    long NetworkId,
    string Kind,
    uint Version,
    ReadOnlyMemory<byte> Payload);

/// <summary>
/// 指定 Tick 的全量状态快照。
/// 客户端初次进入、断线重连或 Delta 缺口恢复时，都应该先应用 Snapshot 再继续应用增量。
/// </summary>
public sealed record YuoStateSnapshot(
    long Tick,
    IReadOnlyList<YuoEntityState> Entities);

/// <summary>
/// 状态变化类型。
/// </summary>
public enum YuoStateChangeKind
{
    /// <summary>实体进入观察范围。</summary>
    Enter = 1,

    /// <summary>实体状态更新。</summary>
    Update = 2,

    /// <summary>实体离开观察范围。</summary>
    Leave = 3,

    /// <summary>实体被销毁。</summary>
    Destroy = 4
}

/// <summary>
/// 增量状态更新。
/// BaseTick 表示该增量依赖的本地基础 Tick；客户端镜像发现不匹配时应请求新的 Snapshot。
/// </summary>
public sealed record YuoStateDelta(
    long Tick,
    IReadOnlyList<YuoStateChange> Changes,
    long? BaseTick = null);

/// <summary>
/// Delta 历史回放结果。
/// 这层只描述纯 StateSync 数据是否可连续补齐，不绑定任何网络协议或 Backend 连接。
/// </summary>
public enum YuoStateDeltaReplayStatus
{
    /// <summary>已找到从 KnownTick 之后开始的连续增量。</summary>
    Replayed = 1,

    /// <summary>KnownTick 已经是当前最新 tick，没有需要回放的增量。</summary>
    UpToDate = 2,

    /// <summary>KnownTick 太旧，历史窗口已经覆盖不到，只能请求完整快照。</summary>
    MissingHistory = 3,

    /// <summary>请求 tick 非法，例如小于 0。</summary>
    InvalidKnownTick = 4
}

public sealed record YuoStateDeltaReplayResult(
    YuoStateDeltaReplayStatus Status,
    IReadOnlyList<YuoStateDelta> Deltas,
    long KnownTick,
    long? OldestTick,
    long? NewestTick,
    string? Message = null)
{
    public bool CanReplay => Status is YuoStateDeltaReplayStatus.Replayed or YuoStateDeltaReplayStatus.UpToDate;
}

/// <summary>
/// 单个实体的状态变化描述。
/// Enter/Update 必须携带 State，Leave/Destroy 只需要 NetworkId。
/// </summary>
public sealed record YuoStateChange(
    YuoStateChangeKind Kind,
    YuoEntityState? State,
    long NetworkId);
