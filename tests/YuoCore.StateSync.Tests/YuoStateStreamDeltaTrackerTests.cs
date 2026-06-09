namespace YuoCore.StateSync.Tests;

public sealed class YuoStateStreamDeltaTrackerTests
{
    [Fact]
    public void Build_delta_emits_enter_update_and_leave_for_one_stream()
    {
        var tracker = new YuoStateStreamDeltaTracker();

        var first = tracker.BuildDelta(
            "map:1001:aoi:cell_1",
            10,
            [State(1001, version: 1), State(1002, version: 1)]);
        var second = tracker.BuildDelta(
            "map:1001:aoi:cell_1",
            11,
            [State(1001, version: 2), State(1003, version: 1)]);

        Assert.Null(first.BaseTick);
        Assert.Equal([YuoStateChangeKind.Enter, YuoStateChangeKind.Enter], first.Changes.Select(change => change.Kind));
        Assert.Equal(10, second.BaseTick);
        Assert.Contains(second.Changes, change =>
            change.Kind == YuoStateChangeKind.Update &&
            change.NetworkId == 1001 &&
            change.State?.Version == 2);
        Assert.Contains(second.Changes, change =>
            change.Kind == YuoStateChangeKind.Enter &&
            change.NetworkId == 1003);
        Assert.Contains(second.Changes, change =>
            change.Kind == YuoStateChangeKind.Leave &&
            change.NetworkId == 1002 &&
            change.State is null);
    }

    [Fact]
    public void Streams_keep_independent_base_ticks()
    {
        var tracker = new YuoStateStreamDeltaTracker();
        var first = tracker.BuildDelta("map:1001:aoi:cell_1", 10, [State(1001, version: 1)]);
        var second = tracker.BuildDelta("map:1001:aoi:cell_2", 3, [State(2001, version: 1)]);
        var secondNext = tracker.BuildDelta("map:1001:aoi:cell_2", 4, [State(2001, version: 2)]);

        Assert.Null(first.BaseTick);
        Assert.Null(second.BaseTick);
        Assert.Equal(3, secondNext.BaseTick);
    }

    [Fact]
    public void Duplicate_network_id_is_rejected()
    {
        var tracker = new YuoStateStreamDeltaTracker();

        Assert.Throws<InvalidOperationException>(() =>
            tracker.BuildDelta("map:1001:aoi:cell_1", 10, [State(1001, 1), State(1001, 2)]));
    }

    [Fact]
    public void Remove_stream_resets_tick_and_versions()
    {
        var tracker = new YuoStateStreamDeltaTracker();
        _ = tracker.BuildDelta("map:1001:aoi:cell_1", 10, [State(1001, version: 1)]);

        Assert.True(tracker.RemoveStream("map:1001:aoi:cell_1"));
        var reset = tracker.BuildDelta("map:1001:aoi:cell_1", 1, [State(1001, version: 1)]);

        Assert.Null(reset.BaseTick);
        Assert.Equal(YuoStateChangeKind.Enter, Assert.Single(reset.Changes).Kind);
    }

    [Fact]
    public void Tick_must_increase_per_stream()
    {
        var tracker = new YuoStateStreamDeltaTracker();
        _ = tracker.BuildDelta("map:1001:aoi:cell_1", 10, [State(1001, version: 1)]);

        Assert.Throws<InvalidOperationException>(() =>
            tracker.BuildDelta("map:1001:aoi:cell_1", 10, [State(1001, version: 2)]));
    }

    [Fact]
    public void Dirty_delta_does_not_advance_tick_when_stream_is_unchanged()
    {
        var tracker = new YuoStateStreamDeltaTracker();

        Assert.True(tracker.TryBuildDirtyDelta(
            "map:1001:aoi:cell_1",
            10,
            [State(1001, version: 1)],
            out var enter));
        Assert.False(tracker.TryBuildDirtyDelta(
            "map:1001:aoi:cell_1",
            11,
            [State(1001, version: 1)],
            out _));
        Assert.True(tracker.TryBuildDirtyDelta(
            "map:1001:aoi:cell_1",
            12,
            [State(1001, version: 2)],
            out var update));

        Assert.Null(enter.BaseTick);
        Assert.Equal(10, update.BaseTick);
        Assert.Equal(12, update.Tick);
        var change = Assert.Single(update.Changes);
        Assert.Equal(YuoStateChangeKind.Update, change.Kind);
        Assert.Equal(2U, change.State?.Version);
    }

    private static YuoEntityState State(long networkId, uint version)
    {
        return new YuoEntityState(
            networkId,
            "player",
            version,
            new byte[] { checked((byte)version) });
    }
}
