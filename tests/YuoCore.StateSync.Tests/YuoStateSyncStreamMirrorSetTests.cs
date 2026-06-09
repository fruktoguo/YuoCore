namespace YuoCore.StateSync.Tests;

public sealed class YuoStateSyncStreamMirrorSetTests
{
    [Fact]
    public void Streams_keep_independent_ticks_and_gap_state()
    {
        var set = new YuoStateSyncStreamMirrorSet();
        var firstStream = YuoStateSyncStreamIds.ForMapAoi(1001, "cell_1");
        var secondStream = YuoStateSyncStreamIds.ForMapAoi(1001, "cell_2");

        var firstSnapshot = set.ApplySnapshot(firstStream, MakeSnapshot(10, 1001, version: 1));
        var secondSnapshot = set.ApplySnapshot(secondStream, MakeSnapshot(3, 2001, version: 1));
        var secondDelta = set.ApplyDelta(secondStream, MakeDelta(4, baseTick: 3, 2001, version: 2));

        Assert.True(firstSnapshot.IsApplied);
        Assert.True(secondSnapshot.IsApplied);
        Assert.True(secondDelta.IsApplied);
        Assert.Equal(10, set.GetCursor(firstStream).LastTick);
        Assert.Equal(4, set.GetCursor(secondStream).LastTick);

        var gap = set.ApplyDelta(firstStream, MakeDelta(12, baseTick: 11, 1001, version: 2));

        Assert.True(gap.RequiresSnapshot);
        Assert.True(set.GetCursor(firstStream).IsSnapshotRequired);
        Assert.False(set.GetCursor(secondStream).IsSnapshotRequired);
        Assert.Equal(4, set.GetCursor(secondStream).LastTick);
    }

    [Fact]
    public void Remove_stream_drops_only_that_stream()
    {
        var set = new YuoStateSyncStreamMirrorSet();
        var playerStream = YuoStateSyncStreamIds.ForPlayer(Guid.Parse("4e58d475-32b1-4f8d-81a5-62f23ca4f401"));
        var mapStream = YuoStateSyncStreamIds.ForMap(1001);
        set.ApplySnapshot(playerStream, MakeSnapshot(1, 1001, version: 1));
        set.ApplySnapshot(mapStream, MakeSnapshot(2, 2001, version: 1));

        Assert.True(set.Remove(playerStream));

        Assert.False(set.TryGet(playerStream, out _));
        Assert.True(set.TryGet(mapStream, out var mapMirror));
        Assert.Equal(2, mapMirror.Table.LastTick);
        Assert.Equal([mapStream], set.StreamIds);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_stream_id_is_rejected(string streamId)
    {
        var set = new YuoStateSyncStreamMirrorSet();

        Assert.Throws<ArgumentException>(() => set.GetOrCreate(streamId));
        Assert.Throws<ArgumentException>(() => set.GetCursor(streamId));
        Assert.Throws<ArgumentException>(() => set.Remove(streamId));
    }

    private static YuoStateSnapshot MakeSnapshot(long tick, long networkId, uint version)
    {
        return new YuoStateSnapshot(
            tick,
            [new YuoEntityState(networkId, "player", version, new byte[] { checked((byte)version) })]);
    }

    private static YuoStateDelta MakeDelta(long tick, long baseTick, long networkId, uint version)
    {
        return new YuoStateDelta(
            tick,
            [
                new YuoStateChange(
                    YuoStateChangeKind.Update,
                    new YuoEntityState(networkId, "player", version, new byte[] { checked((byte)version) }),
                    networkId)
            ],
            baseTick);
    }
}
