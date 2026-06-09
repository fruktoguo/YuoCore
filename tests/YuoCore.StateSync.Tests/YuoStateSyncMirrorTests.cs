namespace YuoCore.StateSync.Tests;

public sealed class YuoStateSyncMirrorTests
{
    [Fact]
    public void Snapshot_and_matching_delta_are_applied()
    {
        var mirror = new YuoStateSyncMirror();
        var snapshot = new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]);

        var snapshotResult = mirror.ApplySnapshot(snapshot);
        var deltaResult = mirror.ApplyDelta(new YuoStateDelta(
            11,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, new byte[] { 2 }), 1)],
            BaseTick: 10));

        Assert.True(snapshotResult.IsApplied);
        Assert.True(deltaResult.IsApplied);
        Assert.False(mirror.IsSnapshotRequired);
        Assert.Equal(11, mirror.Table.LastTick);
        Assert.True(mirror.Table.TryGet(1, out var state));
        Assert.Equal(2u, state.Version);
    }

    [Fact]
    public void Gap_marks_snapshot_required_and_blocks_following_deltas()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));

        var gap = mirror.ApplyDelta(new YuoStateDelta(
            12,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, ReadOnlyMemory<byte>.Empty), 1)],
            BaseTick: 11));
        var blocked = mirror.ApplyDelta(new YuoStateDelta(
            13,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 3, ReadOnlyMemory<byte>.Empty), 1)],
            BaseTick: 12));

        Assert.True(gap.RequiresSnapshot);
        Assert.Equal(YuoStateSyncApplyReason.GapDetected, gap.Reason);
        Assert.True(mirror.IsSnapshotRequired);
        Assert.NotNull(mirror.LastGap);
        Assert.True(blocked.RequiresSnapshot);
        Assert.Equal(YuoStateSyncApplyReason.SnapshotAlreadyRequired, blocked.Reason);
        Assert.Equal(10, mirror.Table.LastTick);
    }

    [Fact]
    public void Snapshot_after_gap_recovers_delta_application()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));
        mirror.ApplyDelta(new YuoStateDelta(
            12,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, ReadOnlyMemory<byte>.Empty), 1)],
            BaseTick: 11));

        var recovered = mirror.ApplySnapshot(new YuoStateSnapshot(
            12,
            [new YuoEntityState(1, "unit", 2, ReadOnlyMemory<byte>.Empty)]));
        var next = mirror.ApplyDelta(new YuoStateDelta(
            13,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 3, ReadOnlyMemory<byte>.Empty), 1)],
            BaseTick: 12));

        Assert.True(recovered.IsApplied);
        Assert.False(mirror.IsSnapshotRequired);
        Assert.Null(mirror.LastGap);
        Assert.True(next.IsApplied);
        Assert.Equal(13, mirror.Table.LastTick);
    }

    [Fact]
    public void Stale_snapshot_is_rejected_without_rolling_back_state()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 10 })]));
        mirror.ApplyDelta(new YuoStateDelta(
            11,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, new byte[] { 11 }), 1)],
            BaseTick: 10));

        var stale = mirror.ApplySnapshot(new YuoStateSnapshot(
            9,
            [new YuoEntityState(1, "unit", 3, new byte[] { 9 })]));

        Assert.Equal(YuoStateSyncApplyStatus.Rejected, stale.Status);
        Assert.Equal(YuoStateSyncApplyReason.StaleSnapshot, stale.Reason);
        Assert.Equal(11, mirror.Table.LastTick);
        Assert.True(mirror.Table.TryGet(1, out var state));
        Assert.Equal(2u, state.Version);
    }

    [Fact]
    public void Invalid_snapshot_is_rejected_without_mutating_state()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 10 })]));

        var result = mirror.ApplySnapshot(new YuoStateSnapshot(
            11,
            [
                new YuoEntityState(2, "unit", 1, new byte[] { 20 }),
                new YuoEntityState(2, "unit", 2, new byte[] { 21 })
            ]));

        Assert.Equal(YuoStateSyncApplyStatus.Rejected, result.Status);
        Assert.Equal(YuoStateSyncApplyReason.InvalidSnapshot, result.Reason);
        Assert.Equal(10, mirror.Table.LastTick);
        Assert.True(mirror.Table.TryGet(1, out var state));
        Assert.Equal(1u, state.Version);
        Assert.False(mirror.Table.TryGet(2, out _));
    }

    [Fact]
    public void Invalid_snapshot_does_not_clear_snapshot_required_state()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));
        mirror.ApplyDelta(new YuoStateDelta(
            12,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, ReadOnlyMemory<byte>.Empty), 1)],
            BaseTick: 11));

        var result = mirror.ApplySnapshot(new YuoStateSnapshot(
            12,
            [
                new YuoEntityState(1, "unit", 2, ReadOnlyMemory<byte>.Empty),
                new YuoEntityState(1, "unit", 3, ReadOnlyMemory<byte>.Empty)
            ]));

        Assert.Equal(YuoStateSyncApplyStatus.Rejected, result.Status);
        Assert.Equal(YuoStateSyncApplyReason.InvalidSnapshot, result.Reason);
        Assert.True(mirror.IsSnapshotRequired);
        Assert.NotNull(mirror.LastGap);
        Assert.Equal(10, mirror.Table.LastTick);
    }

    [Fact]
    public void Snapshot_with_null_entities_is_returned_as_invalid_snapshot()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));

        var result = mirror.ApplySnapshot(new YuoStateSnapshot(11, null!));

        Assert.Equal(YuoStateSyncApplyStatus.Rejected, result.Status);
        Assert.Equal(YuoStateSyncApplyReason.InvalidSnapshot, result.Reason);
        Assert.Equal(10, mirror.Table.LastTick);
        Assert.True(mirror.Table.TryGet(1, out _));
    }

    [Fact]
    public void Snapshot_required_state_rejects_snapshot_that_does_not_advance_past_gap_point()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));
        mirror.ApplyDelta(new YuoStateDelta(
            12,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, ReadOnlyMemory<byte>.Empty), 1)],
            BaseTick: 11));

        var staleRecovery = mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));

        Assert.True(staleRecovery.RequiresSnapshot);
        Assert.Equal(YuoStateSyncApplyReason.StaleSnapshot, staleRecovery.Reason);
        Assert.True(mirror.IsSnapshotRequired);
        Assert.NotNull(mirror.LastGap);
        Assert.Equal(10, mirror.Table.LastTick);
    }

    [Fact]
    public void Stale_delta_is_rejected_without_requiring_snapshot()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));

        var result = mirror.ApplyDelta(new YuoStateDelta(
            10,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, ReadOnlyMemory<byte>.Empty), 1)]));

        Assert.Equal(YuoStateSyncApplyStatus.Rejected, result.Status);
        Assert.Equal(YuoStateSyncApplyReason.StaleDelta, result.Reason);
        Assert.False(mirror.IsSnapshotRequired);
        Assert.Equal(10, mirror.Table.LastTick);
    }

    [Fact]
    public void Delta_with_null_changes_is_returned_as_invalid_delta()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, ReadOnlyMemory<byte>.Empty)]));

        var result = mirror.ApplyDelta(new YuoStateDelta(11, null!, BaseTick: 10));

        Assert.Equal(YuoStateSyncApplyStatus.Rejected, result.Status);
        Assert.Equal(YuoStateSyncApplyReason.InvalidDelta, result.Reason);
        Assert.Equal(10, mirror.Table.LastTick);
        Assert.True(mirror.Table.TryGet(1, out _));
    }
}
