using YuoCore.StateSync.Protocol;

namespace YuoCore.StateSync.Protocol.Tests;

public sealed class YuoStateSyncProtocolMapperTests
{
    [Fact]
    public void Snapshot_round_trips_between_statesync_and_protocol()
    {
        var snapshot = new YuoStateSnapshot(
            10,
            [new YuoEntityState(1001, "player", 7, new byte[] { 1, 2, 3 })]);

        var protocol = YuoStateSyncProtocolMapper.ToProtocol(snapshot);
        var restored = YuoStateSyncProtocolMapper.ToStateSnapshot(protocol);

        Assert.Equal(10, protocol.Tick);
        Assert.Single(protocol.Entities);
        Assert.Equal(snapshot.Tick, restored.Tick);
        var entity = Assert.Single(restored.Entities);
        Assert.Equal(1001, entity.NetworkId);
        Assert.Equal("player", entity.Kind);
        Assert.Equal(7u, entity.Version);
        Assert.Equal([1, 2, 3], entity.Payload.ToArray());
    }

    [Fact]
    public void Delta_round_trips_base_tick_and_change_kinds()
    {
        var delta = new YuoStateDelta(
            12,
            [
                new YuoStateChange(
                    YuoStateChangeKind.Enter,
                    new YuoEntityState(1001, "player", 1, new byte[] { 9 }),
                    1001),
                new YuoStateChange(YuoStateChangeKind.Destroy, null, 1002)
            ],
            BaseTick: 11);

        var protocol = YuoStateSyncProtocolMapper.ToProtocol(delta);
        var restored = YuoStateSyncProtocolMapper.ToStateDelta(protocol);

        Assert.True(protocol.HasBaseTick);
        Assert.Equal(11, protocol.BaseTick);
        Assert.Equal(delta.Tick, restored.Tick);
        Assert.Equal(delta.BaseTick, restored.BaseTick);
        Assert.Collection(
            restored.Changes,
            enter =>
            {
                Assert.Equal(YuoStateChangeKind.Enter, enter.Kind);
                Assert.Equal(1001, enter.NetworkId);
                Assert.NotNull(enter.State);
            },
            destroy =>
            {
                Assert.Equal(YuoStateChangeKind.Destroy, destroy.Kind);
                Assert.Equal(1002, destroy.NetworkId);
                Assert.Null(destroy.State);
            });
    }

    [Fact]
    public void Protocol_delta_can_be_applied_to_state_sync_mirror()
    {
        var mirror = new YuoStateSyncMirror();
        mirror.ApplySnapshot(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1001, "player", 1, new byte[] { 1 })]));
        var protocol = YuoStateSyncProtocolMapper.ToProtocol(new YuoStateDelta(
            11,
            [new YuoStateChange(YuoStateChangeKind.Update, new YuoEntityState(1001, "player", 2, new byte[] { 2 }), 1001)],
            BaseTick: 10));

        var result = mirror.ApplyDelta(YuoStateSyncProtocolMapper.ToStateDelta(protocol));

        Assert.True(result.IsApplied);
        Assert.Equal(11, mirror.Table.LastTick);
        Assert.True(mirror.Table.TryGet(1001, out var state));
        Assert.Equal(2u, state.Version);
    }
}
