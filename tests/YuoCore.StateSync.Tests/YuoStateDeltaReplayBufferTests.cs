namespace YuoCore.StateSync.Tests;

public sealed class YuoStateDeltaReplayBufferTests
{
    [Fact]
    public void Replay_after_known_tick_returns_contiguous_deltas()
    {
        var buffer = new YuoStateDeltaReplayBuffer(capacity: 8);
        buffer.Add(MakeDelta(11, 10));
        buffer.Add(MakeDelta(12, 11));

        var replay = buffer.ReplayAfter(10);

        Assert.Equal(YuoStateDeltaReplayStatus.Replayed, replay.Status);
        Assert.True(replay.CanReplay);
        Assert.Equal([11L, 12L], replay.Deltas.Select(delta => delta.Tick));
    }

    [Fact]
    public void Replay_after_newest_tick_is_up_to_date()
    {
        var buffer = new YuoStateDeltaReplayBuffer(capacity: 8);
        buffer.Add(MakeDelta(11, 10));

        var replay = buffer.ReplayAfter(11);

        Assert.Equal(YuoStateDeltaReplayStatus.UpToDate, replay.Status);
        Assert.Empty(replay.Deltas);
        Assert.True(replay.CanReplay);
    }

    [Fact]
    public void Replay_returns_missing_history_when_first_delta_depends_on_newer_base_tick()
    {
        var buffer = new YuoStateDeltaReplayBuffer(capacity: 1);
        buffer.Add(MakeDelta(11, 10));
        buffer.Add(MakeDelta(12, 11));

        var replay = buffer.ReplayAfter(10);

        Assert.Equal(YuoStateDeltaReplayStatus.MissingHistory, replay.Status);
        Assert.False(replay.CanReplay);
        Assert.Contains("depends on tick 11", replay.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prune_through_removes_acknowledged_deltas_and_preserves_newest_available_tick()
    {
        var buffer = new YuoStateDeltaReplayBuffer(capacity: 8);
        buffer.Add(MakeDelta(11, 10));
        buffer.Add(MakeDelta(12, 11));
        buffer.Add(MakeDelta(13, 12));

        var removed = buffer.PruneThrough(12);

        Assert.Equal(2, removed);
        Assert.Equal(1, buffer.Count);
        Assert.Equal(13, buffer.OldestTick);
        Assert.Equal(13, buffer.NewestTick);
        var replay = buffer.ReplayAfter(11);
        Assert.Equal(YuoStateDeltaReplayStatus.MissingHistory, replay.Status);
    }

    private static YuoStateDelta MakeDelta(long tick, long baseTick)
    {
        return new YuoStateDelta(
            tick,
            [
                new YuoStateChange(
                    YuoStateChangeKind.Update,
                    new YuoEntityState(
                        tick,
                        "test",
                        1,
                        new byte[] { checked((byte)tick) }),
                    tick)
            ],
            baseTick);
    }
}
