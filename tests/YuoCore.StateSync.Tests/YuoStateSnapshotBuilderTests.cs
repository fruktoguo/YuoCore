using YuoCore.Ecs;

namespace YuoCore.StateSync.Tests;

public sealed class YuoStateSnapshotBuilderTests
{
    [Fact]
    public void Network_state_set_payload_copies_input()
    {
        var component = new YuoNetworkStateComponent();
        var payload = new byte[] { 1, 2, 3 };

        component.SetPayload(payload);
        payload[0] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, component.Payload);
        Assert.Equal(1U, component.Version);
        Assert.True(component.IsDirty);
    }

    [Fact]
    public void Network_state_set_payload_owned_takes_array_ownership()
    {
        var component = new YuoNetworkStateComponent();
        var payload = new byte[] { 1, 2, 3 };

        component.SetPayloadOwned(payload);

        Assert.Same(payload, component.Payload);
        Assert.Equal(1U, component.Version);
        Assert.True(component.IsDirty);
    }

    [Fact]
    public void Full_snapshot_reads_only_network_synced_entities()
    {
        var world = new YuoWorld();
        world.OnInit(setAsDefault: false);

        var synced = new YuoEntity(world);
        synced.AddComponent<YuoNetworkIdentityComponent>().NetworkId = 100;
        synced.GetComponent<YuoNetworkIdentityComponent>().Kind = "unit";
        synced.AddComponent<YuoNetworkStateComponent>().SetPayload([1, 2, 3]);

        _ = new YuoEntity(world);

        var builder = new YuoFullStateSnapshotBuilder();
        var snapshot = builder.BuildFullSnapshot(12, new[] { synced });

        Assert.Equal(12, snapshot.Tick);
        var entity = Assert.Single(snapshot.Entities);
        Assert.Equal(100, entity.NetworkId);
        Assert.Equal("unit", entity.Kind);
        Assert.Equal(new byte[] { 1, 2, 3 }, entity.Payload.ToArray());
    }

    [Fact]
    public void Dirty_delta_reads_dirty_entities_and_clears_dirty_flag()
    {
        var world = new YuoWorld();
        world.OnInit(setAsDefault: false);

        var dirty = new YuoEntity(world);
        dirty.AddComponent<YuoNetworkIdentityComponent>().NetworkId = 200;
        dirty.GetComponent<YuoNetworkIdentityComponent>().Kind = "unit";
        dirty.AddComponent<YuoNetworkStateComponent>().SetPayload([9]);

        var clean = new YuoEntity(world);
        clean.AddComponent<YuoNetworkIdentityComponent>().NetworkId = 201;
        clean.GetComponent<YuoNetworkIdentityComponent>().Kind = "unit";
        clean.AddComponent<YuoNetworkStateComponent>().SetPayload([8]);
        clean.GetComponent<YuoNetworkStateComponent>().ClearDirty();

        var delta = new YuoDirtyStateDeltaBuilder().BuildDirtyDelta(13, [dirty, clean]);

        Assert.Equal(13, delta.Tick);
        var change = Assert.Single(delta.Changes);
        Assert.Equal(YuoStateChangeKind.Update, change.Kind);
        Assert.Equal(200, change.NetworkId);
        Assert.Equal(new byte[] { 9 }, change.State!.Payload.ToArray());
        Assert.False(dirty.GetComponent<YuoNetworkStateComponent>().IsDirty);
        Assert.False(clean.GetComponent<YuoNetworkStateComponent>().IsDirty);
    }
}
