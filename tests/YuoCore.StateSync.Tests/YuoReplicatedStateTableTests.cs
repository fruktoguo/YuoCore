namespace YuoCore.StateSync.Tests;

public sealed class YuoReplicatedStateTableTests
{
    [Fact]
    public void Snapshot_replaces_existing_state_table()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            1,
            [new YuoEntityState(1, "old", 1, new byte[] { 1 })]));

        table.ApplySnapshot(new YuoStateSnapshot(
            2,
            [new YuoEntityState(2, "unit", 1, new byte[] { 2 })]));

        Assert.Equal(2, table.LastTick);
        Assert.False(table.TryGet(1, out _));
        Assert.True(table.TryGet(2, out var state));
        Assert.Equal("unit", state.Kind);
    }

    [Fact]
    public void Snapshot_with_duplicate_network_id_is_rejected_before_mutating_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplySnapshot(new YuoStateSnapshot(
                11,
                [
                    new YuoEntityState(2, "unit", 1, new byte[] { 2 }),
                    new YuoEntityState(2, "unit", 2, new byte[] { 3 })
                ])));

        Assert.Contains("duplicate network id", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, table.LastTick);
        Assert.True(table.TryGet(1, out var state));
        Assert.Equal(1u, state.Version);
        Assert.False(table.TryGet(2, out _));
    }

    [Fact]
    public void Snapshot_with_null_entities_is_rejected_before_mutating_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplySnapshot(new YuoStateSnapshot(11, null!)));

        Assert.Contains("entities are required", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, table.LastTick);
        Assert.True(table.TryGet(1, out _));
    }

    [Fact]
    public void Snapshot_with_null_entity_state_is_rejected_before_mutating_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplySnapshot(new YuoStateSnapshot(11, [null!])));

        Assert.Contains("entity state is required", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, table.LastTick);
        Assert.True(table.TryGet(1, out _));
    }

    [Fact]
    public void Delta_enter_update_and_leave_mutate_state_table()
    {
        var table = new YuoReplicatedStateTable();
        var first = new YuoEntityState(10, "unit", 1, new byte[] { 1 });
        var updated = new YuoEntityState(10, "unit", 2, new byte[] { 2 });

        table.ApplyDelta(new YuoStateDelta(
            1,
            [new YuoStateChange(YuoStateChangeKind.Enter, first, 10)]));
        Assert.Equal(1, table.LastTick);
        Assert.True(table.TryGet(10, out var entered));
        Assert.Equal(1u, entered.Version);

        table.ApplyDelta(new YuoStateDelta(
            2,
            [new YuoStateChange(YuoStateChangeKind.Update, updated, 10)]));
        Assert.Equal(2, table.LastTick);
        Assert.True(table.TryGet(10, out var changed));
        Assert.Equal(2u, changed.Version);

        table.ApplyDelta(new YuoStateDelta(
            3,
            [new YuoStateChange(YuoStateChangeKind.Leave, null, 10)]));
        Assert.Equal(3, table.LastTick);
        Assert.False(table.TryGet(10, out _));
    }

    [Fact]
    public void Enter_or_update_without_state_is_rejected()
    {
        var table = new YuoReplicatedStateTable();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplyDelta(new YuoStateDelta(
                1,
                [new YuoStateChange(YuoStateChangeKind.Enter, null, 10)])));

        Assert.Contains("must carry entity state", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_delta_is_rejected_before_mutating_any_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplyDelta(new YuoStateDelta(
                11,
                [
                    new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1, "unit", 2, new byte[] { 2 }), 1),
                    new YuoStateChange(YuoStateChangeKind.Update, null, 2)
                ],
                BaseTick: 10)));

        Assert.Contains("must carry entity state", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, table.LastTick);
        Assert.True(table.TryGet(1, out var state));
        Assert.Equal(1u, state.Version);
        Assert.False(table.TryGet(2, out _));
    }

    [Fact]
    public void Delta_with_null_changes_is_rejected_before_mutating_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplyDelta(new YuoStateDelta(11, null!, BaseTick: 10)));

        Assert.Contains("changes are required", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, table.LastTick);
        Assert.True(table.TryGet(1, out _));
    }

    [Fact]
    public void Delta_with_null_change_is_rejected_before_mutating_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplyDelta(new YuoStateDelta(11, [null!], BaseTick: 10)));

        Assert.Contains("change is required", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, table.LastTick);
        Assert.True(table.TryGet(1, out _));
    }

    [Fact]
    public void Delta_state_network_id_mismatch_is_rejected_before_mutating_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplyDelta(new YuoStateDelta(
                11,
                [
                    new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(2, "unit", 2, new byte[] { 2 }), 1)
                ],
                BaseTick: 10)));

        Assert.Contains("network id mismatch", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, table.LastTick);
        Assert.True(table.TryGet(1, out var state));
        Assert.Equal(1u, state.Version);
        Assert.False(table.TryGet(2, out _));
    }

    [Fact]
    public void Delta_with_matching_base_tick_updates_state_table_cursor()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(10, "unit", 1, new byte[] { 1 })]));

        table.ApplyDelta(new YuoStateDelta(
            11,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(10, "unit", 2, new byte[] { 2 }), 10)],
            BaseTick: 10));

        Assert.Equal(11, table.LastTick);
        Assert.True(table.TryGet(10, out var state));
        Assert.Equal(2u, state.Version);
    }

    [Fact]
    public void Delta_with_mismatched_base_tick_is_rejected_before_mutating_state()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(10, "unit", 1, new byte[] { 1 })]));

        var exception = Assert.Throws<YuoStateSyncGapException>(() =>
            table.ApplyDelta(new YuoStateDelta(
                12,
                [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(10, "unit", 2, new byte[] { 2 }), 10)],
                BaseTick: 11)));

        Assert.Equal(10, exception.LastAppliedTick);
        Assert.Equal(11, exception.ExpectedBaseTick);
        Assert.Equal(12, exception.DeltaTick);
        Assert.True(table.TryGet(10, out var state));
        Assert.Equal(1u, state.Version);
        Assert.Equal(10, table.LastTick);
    }

    [Fact]
    public void Stale_delta_tick_is_rejected()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(10, "unit", 1, ReadOnlyMemory<byte>.Empty)]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            table.ApplyDelta(new YuoStateDelta(
                10,
                [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(10, "unit", 2, ReadOnlyMemory<byte>.Empty), 10)])));

        Assert.Contains("must increase", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Clear_removes_state_and_tick_cursor()
    {
        var table = new YuoReplicatedStateTable();
        table.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(10, "unit", 1, ReadOnlyMemory<byte>.Empty)]));

        table.Clear();

        Assert.Empty(table.States);
        Assert.Null(table.LastTick);
    }
}
