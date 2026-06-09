namespace YuoCore.StateSync;

/// <summary>
/// Snapshot/Delta 在镜像侧的应用状态。
/// 这里不直接发网络请求，只把“是否需要重拉 snapshot”稳定暴露给 Web、Unity 或 Gateway SDK。
/// </summary>
public enum YuoStateSyncApplyStatus
{
    Applied = 1,
    SnapshotRequired = 2,
    Rejected = 3
}

public enum YuoStateSyncApplyReason
{
    None = 0,
    GapDetected = 1,
    SnapshotAlreadyRequired = 2,
    StaleDelta = 3,
    InvalidDelta = 4,
    StaleSnapshot = 5,
    InvalidSnapshot = 6
}

public sealed record YuoStateSyncApplyResult(
    YuoStateSyncApplyStatus Status,
    YuoStateSyncApplyReason Reason,
    long? LastAppliedTick,
    long? IncomingTick,
    string? Message = null)
{
    public bool IsApplied => Status == YuoStateSyncApplyStatus.Applied;

    public bool RequiresSnapshot => Status == YuoStateSyncApplyStatus.SnapshotRequired;

    public static YuoStateSyncApplyResult Applied(long? lastAppliedTick, long? incomingTick)
    {
        return new YuoStateSyncApplyResult(
            YuoStateSyncApplyStatus.Applied,
            YuoStateSyncApplyReason.None,
            lastAppliedTick,
            incomingTick);
    }

    public static YuoStateSyncApplyResult SnapshotRequired(
        YuoStateSyncApplyReason reason,
        long? lastAppliedTick,
        long? incomingTick,
        string? message = null)
    {
        return new YuoStateSyncApplyResult(
            YuoStateSyncApplyStatus.SnapshotRequired,
            reason,
            lastAppliedTick,
            incomingTick,
            message);
    }

    public static YuoStateSyncApplyResult Rejected(
        YuoStateSyncApplyReason reason,
        long? lastAppliedTick,
        long? incomingTick,
        string? message = null)
    {
        return new YuoStateSyncApplyResult(
            YuoStateSyncApplyStatus.Rejected,
            reason,
            lastAppliedTick,
            incomingTick,
            message);
    }
}

/// <summary>
/// 客户端/镜像侧的状态同步协调器。
/// 它只负责 snapshot/delta 的应用顺序和缺口状态，不绑定 WebSocket、Unity GameObject 或服务端 ECS。
/// 一旦检测到 BaseTick 缺口，后续 delta 会被拒绝，直到调用方应用新的完整 snapshot。
/// </summary>
public sealed class YuoStateSyncMirror
{
    public YuoStateSyncMirror(YuoReplicatedStateTable? table = null)
    {
        Table = table ?? new YuoReplicatedStateTable();
    }

    public YuoReplicatedStateTable Table { get; }

    public bool IsSnapshotRequired { get; private set; }

    public YuoStateSyncGapException? LastGap { get; private set; }

    public YuoStateSyncApplyResult ApplySnapshot(YuoStateSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (Table.LastTick is { } lastTick && snapshot.Tick < lastTick)
        {
            return YuoStateSyncApplyResult.Rejected(
                YuoStateSyncApplyReason.StaleSnapshot,
                lastTick,
                snapshot.Tick,
                "state snapshot tick is older than local mirror tick.");
        }

        if (IsSnapshotRequired && LastGap is { } gap && snapshot.Tick < gap.ExpectedBaseTick)
        {
            return YuoStateSyncApplyResult.SnapshotRequired(
                YuoStateSyncApplyReason.StaleSnapshot,
                Table.LastTick,
                snapshot.Tick,
                "state snapshot does not advance past the gap point.");
        }

        try
        {
            Table.ApplySnapshot(snapshot);
        }
        catch (InvalidOperationException exception)
        {
            return YuoStateSyncApplyResult.Rejected(
                YuoStateSyncApplyReason.InvalidSnapshot,
                Table.LastTick,
                snapshot.Tick,
                exception.Message);
        }

        IsSnapshotRequired = false;
        LastGap = null;

        return YuoStateSyncApplyResult.Applied(Table.LastTick, snapshot.Tick);
    }

    public YuoStateSyncApplyResult ApplyDelta(YuoStateDelta delta)
    {
        if (delta is null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        if (IsSnapshotRequired)
        {
            return YuoStateSyncApplyResult.SnapshotRequired(
                YuoStateSyncApplyReason.SnapshotAlreadyRequired,
                Table.LastTick,
                delta.Tick,
                "state snapshot is required before applying more deltas.");
        }

        try
        {
            Table.ApplyDelta(delta);
            return YuoStateSyncApplyResult.Applied(Table.LastTick, delta.Tick);
        }
        catch (YuoStateSyncGapException exception)
        {
            IsSnapshotRequired = true;
            LastGap = exception;

            return YuoStateSyncApplyResult.SnapshotRequired(
                YuoStateSyncApplyReason.GapDetected,
                Table.LastTick,
                delta.Tick,
                exception.Message);
        }
        catch (InvalidOperationException exception) when (IsStaleDelta(delta))
        {
            return YuoStateSyncApplyResult.Rejected(
                YuoStateSyncApplyReason.StaleDelta,
                Table.LastTick,
                delta.Tick,
                exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return YuoStateSyncApplyResult.Rejected(
                YuoStateSyncApplyReason.InvalidDelta,
                Table.LastTick,
                delta.Tick,
                exception.Message);
        }
    }

    public void Clear()
    {
        Table.Clear();
        IsSnapshotRequired = false;
        LastGap = null;
    }

    private bool IsStaleDelta(YuoStateDelta delta)
    {
        return Table.LastTick is { } lastTick && delta.Tick <= lastTick;
    }
}
