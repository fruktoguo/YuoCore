using YuoCore.Ecs;

namespace YuoCore.Game.Tests;

/// <summary>
/// 回归：命名实体 ID 使用确定性 FNV-1a 而非进程随机的 string.GetHashCode。
/// 期望值独立计算自 FNV-1a 定义，用于钉死跨进程/跨机器一致性。
/// </summary>
public sealed class YuoNamedEntityIdTests
{
    [Fact]
    public void GetID_uses_deterministic_fnv1a()
    {
        Assert.Equal(6205769878347417795L, IDGenerate.GetID("test-entity"));
        Assert.Equal(6024388413860486132L, IDGenerate.GetID("market-zone-1"));
    }

    [Fact]
    public void Named_entity_roundtrips_by_name_lookup()
    {
        var world = new YuoWorld();
        world.OnInit();

        try
        {
            var named = new YuoEntity(world, "market-zone-1");
            Assert.Equal(IDGenerate.GetID("market-zone-1"), named.EntityData.Id);
            Assert.Same(named, world.GetEntity("market-zone-1"));

            var child = named.AddChild("child-a");
            Assert.Equal(IDGenerate.GetID("child-a"), child.EntityData.Id);
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
