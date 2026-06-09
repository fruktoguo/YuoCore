namespace YuoCore.StateSync.Tests;

public sealed class YuoStateDeltaCompressorTests
{
    [Fact]
    public void Enter_then_update_becomes_enter_with_latest_state()
    {
        var compressor = new YuoStateDeltaCompressor();
        var first = new YuoEntityState(10, "unit", 1, new byte[] { 1 });
        var latest = new YuoEntityState(10, "unit", 2, new byte[] { 2 });

        var compressed = compressor.Compress(new YuoStateDelta(
            1,
            [
                new YuoStateChange(YuoStateChangeKind.Enter, first, 10),
                new YuoStateChange(YuoStateChangeKind.Update, latest, 10)
            ]));

        var change = Assert.Single(compressed.Changes);
        Assert.Equal(YuoStateChangeKind.Enter, change.Kind);
        Assert.Equal(2u, change.State!.Version);
        Assert.Equal(new byte[] { 2 }, change.State.Payload.ToArray());
    }

    [Fact]
    public void Enter_then_leave_is_removed_from_delta()
    {
        var compressor = new YuoStateDeltaCompressor();
        var state = new YuoEntityState(11, "unit", 1, new byte[] { 1 });

        var compressed = compressor.Compress(new YuoStateDelta(
            1,
            [
                new YuoStateChange(YuoStateChangeKind.Enter, state, 11),
                new YuoStateChange(YuoStateChangeKind.Leave, null, 11)
            ]));

        Assert.Empty(compressed.Changes);
    }

    [Fact]
    public void Update_then_leave_keeps_leave()
    {
        var compressor = new YuoStateDeltaCompressor();
        var state = new YuoEntityState(12, "unit", 2, new byte[] { 2 });

        var compressed = compressor.Compress(new YuoStateDelta(
            1,
            [
                new YuoStateChange(YuoStateChangeKind.Update, state, 12),
                new YuoStateChange(YuoStateChangeKind.Leave, null, 12)
            ]));

        var change = Assert.Single(compressed.Changes);
        Assert.Equal(YuoStateChangeKind.Leave, change.Kind);
        Assert.Null(change.State);
    }

    [Fact]
    public void Changes_keep_first_seen_entity_order()
    {
        var compressor = new YuoStateDeltaCompressor();
        var state20 = new YuoEntityState(20, "unit", 1, new byte[] { 20 });
        var state21 = new YuoEntityState(21, "unit", 1, new byte[] { 21 });

        var compressed = compressor.Compress(new YuoStateDelta(
            1,
            [
                new YuoStateChange(YuoStateChangeKind.Update, state20, 20),
                new YuoStateChange(YuoStateChangeKind.Update, state21, 21),
                new YuoStateChange(YuoStateChangeKind.Update, state20 with { Version = 2 }, 20)
            ]));

        Assert.Equal([20L, 21L], compressed.Changes.Select(change => change.NetworkId));
    }

    [Fact]
    public void Compression_preserves_base_tick_for_gap_detection()
    {
        var compressor = new YuoStateDeltaCompressor();
        var state = new YuoEntityState(30, "unit", 1, new byte[] { 30 });

        var compressed = compressor.Compress(new YuoStateDelta(
            101,
            [new YuoStateChange(YuoStateChangeKind.Update, state, 30)],
            BaseTick: 100));

        Assert.Equal(101, compressed.Tick);
        Assert.Equal(100, compressed.BaseTick);
    }

    [Fact]
    public void Compress_rejects_null_change_collection()
    {
        var compressor = new YuoStateDeltaCompressor();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            compressor.Compress(new YuoStateDelta(1, null!)));

        Assert.Contains("changes are required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compress_rejects_null_change_item()
    {
        var compressor = new YuoStateDeltaCompressor();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            compressor.Compress(new YuoStateDelta(1, [null!])));

        Assert.Contains("change is required", exception.Message, StringComparison.Ordinal);
    }
}
