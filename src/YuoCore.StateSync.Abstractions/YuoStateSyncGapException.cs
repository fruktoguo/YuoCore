namespace YuoCore.StateSync;

/// <summary>
/// 状态同步 Tick 缺口异常。
/// 当镜像侧收到的 Delta.BaseTick 与本地最新 Tick 不匹配时抛出，调用方应请求新的全量快照。
/// </summary>
public sealed class YuoStateSyncGapException(
    long? lastAppliedTick,
    long expectedBaseTick,
    long deltaTick) : InvalidOperationException(
    $"State delta base tick mismatch. Local last tick: {lastAppliedTick?.ToString() ?? "none"}, delta base tick: {expectedBaseTick}, delta tick: {deltaTick}.")
{
    /// <summary>本地最新应用的 Tick。</summary>
    public long? LastAppliedTick { get; } = lastAppliedTick;

    /// <summary>Delta 期望的基础 Tick。</summary>
    public long ExpectedBaseTick { get; } = expectedBaseTick;

    /// <summary>Delta 自身的 Tick。</summary>
    public long DeltaTick { get; } = deltaTick;
}
