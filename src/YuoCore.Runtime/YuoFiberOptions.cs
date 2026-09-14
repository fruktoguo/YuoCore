namespace YuoCore.Runtime;

/// <summary>
/// YuoFiber 的创建选项。
/// </summary>
public sealed record YuoFiberOptions
{
    /// <summary>Fiber 唯一标识符。在单个 FiberManager 生命周期内不可重复。</summary>
    public long Id { get; init; }

    /// <summary>Fiber 的可读名称，用于日志和调试。</summary>
    public string Name { get; init; } = "Fiber";

    /// <summary>是否将此 Fiber 的 World 设为 YuoWorld.Instance 默认实例。</summary>
    public bool SetWorldAsDefault { get; init; } = false;

    /// <summary>单次 Tick 中最多处理 mailbox 消息数，防止突发消息淹没帧循环。</summary>
    public int MaxMailboxMessagesPerTick { get; init; } = 10_000;

    /// <summary>
    /// fire-and-forget 消息抛错时的兜底出口。为 null 时记录错误日志；
    /// 无论是否设置都不会打穿 Tick 循环。
    /// </summary>
    public Action<YuoFiber, Exception>? UnhandledException { get; init; }
}
