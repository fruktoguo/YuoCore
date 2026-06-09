namespace YuoCore.StateSync.Tests;

public sealed class YuoAoiVisibilityTrackerTests
{
    [Fact]
    public void First_visible_entities_are_enter_changes()
    {
        var tracker = new YuoAoiVisibilityTracker();
        var states = CreateStates(
            new YuoEntityState(2, "unit", 1, new byte[] { 2 }),
            new YuoEntityState(3, "unit", 1, new byte[] { 3 }));

        var delta = tracker.BuildDelta(
            tick: 10,
            observerNetworkId: 1,
            currentVisible:
            [
                new YuoAoiVisibleEntity(2, 1),
                new YuoAoiVisibleEntity(3, 4)
            ],
            states);

        Assert.Equal(10, delta.Tick);
        Assert.Null(delta.BaseTick);
        Assert.Equal([YuoStateChangeKind.Enter, YuoStateChangeKind.Enter], delta.Changes.Select(change => change.Kind));
        Assert.Equal([2L, 3L], delta.Changes.Select(change => change.NetworkId));
        Assert.Equal([2L, 3L], tracker.GetVisibleNetworkIds(1).Order());
    }

    [Fact]
    public void Tracker_emits_update_for_version_change_and_leave_for_lost_visibility()
    {
        var tracker = new YuoAoiVisibilityTracker();

        tracker.BuildDelta(
            tick: 1,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(2, 1)],
            CreateStates(new YuoEntityState(2, "unit", 1, new byte[] { 1 })));

        var delta = tracker.BuildDelta(
            tick: 2,
            observerNetworkId: 1,
            currentVisible:
            [
                new YuoAoiVisibleEntity(2, 1),
                new YuoAoiVisibleEntity(3, 2)
            ],
            CreateStates(
                new YuoEntityState(2, "unit", 2, new byte[] { 2 }),
                new YuoEntityState(3, "unit", 1, new byte[] { 3 })));

        Assert.Equal(
            [YuoStateChangeKind.Update, YuoStateChangeKind.Enter],
            delta.Changes.Select(change => change.Kind));
        Assert.Equal(1, delta.BaseTick);
        Assert.Equal([2L, 3L], delta.Changes.Select(change => change.NetworkId));

        var leaveDelta = tracker.BuildDelta(
            tick: 3,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(3, 1)],
            CreateStates(new YuoEntityState(3, "unit", 1, new byte[] { 3 })));

        var leave = Assert.Single(leaveDelta.Changes);
        Assert.Equal(2, leaveDelta.BaseTick);
        Assert.Equal(YuoStateChangeKind.Leave, leave.Kind);
        Assert.Equal(2, leave.NetworkId);
        Assert.Null(leave.State);
    }

    [Fact]
    public void Missing_state_is_treated_as_not_syncable()
    {
        var tracker = new YuoAoiVisibilityTracker();
        tracker.BuildDelta(
            tick: 1,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(2, 1)],
            CreateStates(new YuoEntityState(2, "unit", 1, new byte[] { 1 })));

        var delta = tracker.BuildDelta(
            tick: 2,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(2, 1)],
            CreateStates());

        var leave = Assert.Single(delta.Changes);
        Assert.Equal(YuoStateChangeKind.Leave, leave.Kind);
        Assert.Equal(2, leave.NetworkId);
    }

    [Fact]
    public void RemoveObserver_resets_base_tick_cursor()
    {
        var tracker = new YuoAoiVisibilityTracker();
        tracker.BuildDelta(
            tick: 10,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(2, 1)],
            CreateStates(new YuoEntityState(2, "unit", 1, ReadOnlyMemory<byte>.Empty)));

        Assert.True(tracker.RemoveObserver(1));
        var delta = tracker.BuildDelta(
            tick: 20,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(2, 1)],
            CreateStates(new YuoEntityState(2, "unit", 1, ReadOnlyMemory<byte>.Empty)));

        Assert.Null(delta.BaseTick);
    }

    [Fact]
    public void Non_increasing_tick_is_rejected_without_mutating_observer_state()
    {
        var tracker = new YuoAoiVisibilityTracker();
        tracker.BuildDelta(
            tick: 10,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(2, 1)],
            CreateStates(new YuoEntityState(2, "unit", 1, ReadOnlyMemory<byte>.Empty)));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            tracker.BuildDelta(
                tick: 9,
                observerNetworkId: 1,
                currentVisible: [new YuoAoiVisibleEntity(3, 1)],
                CreateStates(new YuoEntityState(3, "unit", 1, ReadOnlyMemory<byte>.Empty))));
        var next = tracker.BuildDelta(
            tick: 11,
            observerNetworkId: 1,
            currentVisible: [new YuoAoiVisibleEntity(2, 1)],
            CreateStates(new YuoEntityState(2, "unit", 1, ReadOnlyMemory<byte>.Empty)));

        Assert.Contains("must increase", exception.Message, StringComparison.Ordinal);
        Assert.Equal(10, next.BaseTick);
        Assert.Empty(next.Changes);
        Assert.Equal([2L], tracker.GetVisibleNetworkIds(1));
    }

    private static IReadOnlyDictionary<long, YuoEntityState> CreateStates(params YuoEntityState[] states)
    {
        return states.ToDictionary(state => state.NetworkId);
    }
}
