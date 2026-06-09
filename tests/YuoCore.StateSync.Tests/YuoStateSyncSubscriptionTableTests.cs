namespace YuoCore.StateSync.Tests;

public sealed class YuoStateSyncSubscriptionTableTests
{
    [Fact]
    public void Safe_prune_tick_requires_all_active_subscribers_to_ack()
    {
        var table = new YuoStateSyncSubscriptionTable();
        table.Subscribe("session-a", "map:1001:aoi:cell_1");
        table.Subscribe("session-b", "map:1001:aoi:cell_1");

        Assert.False(table.TryGetSafePruneTick("map:1001:aoi:cell_1", out _));

        Assert.Equal(12, table.Acknowledge("session-a", "map:1001:aoi:cell_1", 12));
        Assert.False(table.TryGetSafePruneTick("map:1001:aoi:cell_1", out _));

        Assert.Equal(10, table.Acknowledge("session-b", "map:1001:aoi:cell_1", 10));

        Assert.True(table.TryGetSafePruneTick("map:1001:aoi:cell_1", out var pruneTick));
        Assert.Equal(10, pruneTick);
    }

    [Fact]
    public void Ack_tick_is_monotonic_per_subscription()
    {
        var table = new YuoStateSyncSubscriptionTable();

        Assert.Equal(10, table.Acknowledge("session-a", "player:1", 10));
        Assert.Equal(10, table.Acknowledge("session-a", "player:1", 8));

        Assert.True(table.TryGetAckTick("session-a", "player:1", out var ackTick));
        Assert.Equal(10, ackTick);
    }

    [Fact]
    public void Removing_subscriber_unblocks_remaining_stream_prune()
    {
        var table = new YuoStateSyncSubscriptionTable();
        table.Subscribe("session-a", "player:1");
        table.Subscribe("session-b", "player:1");
        table.Acknowledge("session-a", "player:1", 20);

        Assert.False(table.TryGetSafePruneTick("player:1", out _));
        Assert.Equal(1, table.RemoveSubscriber("session-b"));

        Assert.True(table.TryGetSafePruneTick("player:1", out var pruneTick));
        Assert.Equal(20, pruneTick);
        Assert.Equal(["session-a"], table.GetSubscribers("player:1"));
    }

    [Fact]
    public void Subscriber_can_track_multiple_streams_independently()
    {
        var table = new YuoStateSyncSubscriptionTable();
        table.Subscribe("session-a", "player:1");
        table.Subscribe("session-a", "map:1001:aoi:cell_1");
        table.Acknowledge("session-a", "player:1", 5);

        Assert.Equal(["map:1001:aoi:cell_1", "player:1"], table.GetStreams("session-a").Order());
        Assert.True(table.TryGetSafePruneTick("player:1", out var playerPruneTick));
        Assert.Equal(5, playerPruneTick);
        Assert.False(table.TryGetSafePruneTick("map:1001:aoi:cell_1", out _));
    }

    [Fact]
    public void Set_subscriber_streams_replaces_streams_and_clears_removed_ack()
    {
        var table = new YuoStateSyncSubscriptionTable();
        table.Subscribe("session-a", "map:1001:aoi:cell_1");
        table.Subscribe("session-a", "map:1001:aoi:cell_2");
        table.Acknowledge("session-a", "map:1001:aoi:cell_1", 10);
        table.Acknowledge("session-a", "map:1001:aoi:cell_2", 20);

        var change = table.SetSubscriberStreams(
            "session-a",
            ["map:1001:aoi:cell_2", "map:1001:aoi:cell_3"]);

        Assert.True(change.HasChanges);
        Assert.Equal(["map:1001:aoi:cell_3"], change.AddedStreams);
        Assert.Equal(["map:1001:aoi:cell_1"], change.RemovedStreams);
        Assert.Equal(
            ["map:1001:aoi:cell_2", "map:1001:aoi:cell_3"],
            table.GetStreams("session-a").Order());
        Assert.False(table.TryGetAckTick("session-a", "map:1001:aoi:cell_1", out _));
        Assert.True(table.TryGetAckTick("session-a", "map:1001:aoi:cell_2", out var retainedAck));
        Assert.Equal(20, retainedAck);
        Assert.False(table.TryGetSafePruneTick("map:1001:aoi:cell_3", out _));
    }

    [Fact]
    public void Set_subscriber_streams_returns_no_change_for_same_stream_set()
    {
        var table = new YuoStateSyncSubscriptionTable();
        table.SetSubscriberStreams(
            "session-a",
            ["map:1001:aoi:cell_1", "map:1001:aoi:cell_2"]);
        table.Acknowledge("session-a", "map:1001:aoi:cell_1", 10);

        var change = table.SetSubscriberStreams(
            "session-a",
            ["map:1001:aoi:cell_2", "map:1001:aoi:cell_1"]);

        Assert.False(change.HasChanges);
        Assert.Empty(change.AddedStreams);
        Assert.Empty(change.RemovedStreams);
        Assert.Equal(
            ["map:1001:aoi:cell_1", "map:1001:aoi:cell_2"],
            table.GetStreams("session-a").Order());
        Assert.True(table.TryGetAckTick("session-a", "map:1001:aoi:cell_1", out var retainedAck));
        Assert.Equal(10, retainedAck);
    }

    [Fact]
    public void Set_subscriber_streams_duplicate_list_falls_back_to_distinct_semantics()
    {
        var table = new YuoStateSyncSubscriptionTable();
        table.SetSubscriberStreams(
            "session-a",
            ["map:1001:aoi:cell_1", "map:1001:aoi:cell_2"]);

        var change = table.SetSubscriberStreams(
            "session-a",
            ["map:1001:aoi:cell_1", "map:1001:aoi:cell_1"]);

        Assert.True(change.HasChanges);
        Assert.Empty(change.AddedStreams);
        Assert.Equal(["map:1001:aoi:cell_2"], change.RemovedStreams);
        Assert.Equal(["map:1001:aoi:cell_1"], table.GetStreams("session-a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_ids_are_rejected(string value)
    {
        var table = new YuoStateSyncSubscriptionTable();

        Assert.Throws<ArgumentException>(() => table.Subscribe(value, "player:1"));
        Assert.Throws<ArgumentException>(() => table.Subscribe("session-a", value));
        Assert.Throws<ArgumentException>(() => table.GetStreams(value));
        Assert.Throws<ArgumentException>(() => table.GetSubscribers(value));
    }
}
