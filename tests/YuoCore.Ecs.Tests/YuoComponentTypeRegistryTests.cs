using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// ComponentTypeRegistry 契约：进程内稳定 id、注册只发生在写入/注册路径、
/// 查询路径（GetId）不污染注册表、id ↔ Type 可互查、多 World 共享编号但存储隔离。
/// </summary>
public sealed class YuoComponentTypeRegistryTests
{
    [Fact]
    public void GetOrAddId_is_stable_and_monotonic()
    {
        var id1 = ComponentTypeRegistry.GetOrAddId(typeof(EcsPosComp));
        var id2 = ComponentTypeRegistry.GetOrAddId(typeof(EcsPosComp));
        var other = ComponentTypeRegistry.GetOrAddId(typeof(EcsVelComp));

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, other);
        Assert.True(id1 >= 0);
        Assert.True(other >= 0);
        Assert.True(ComponentTypeRegistry.MaxId > Math.Max(id1, other));
    }

    [Fact]
    public void GetId_for_unregistered_type_returns_minus_one_without_polluting()
    {
        // 非组件、非标签、从未被任何路径触碰的类型
        var before = ComponentTypeRegistry.MaxId;
        var id = ComponentTypeRegistry.GetId(typeof(Dictionary<string, int>));

        Assert.Equal(-1, id);
        Assert.Equal(before, ComponentTypeRegistry.MaxId); // 纯查询不得分配新 id
        Assert.Null(ComponentTypeRegistry.GetType(id));
    }

    [Fact]
    public void GetType_roundtrips_registered_id()
    {
        var id = ComponentTypeRegistry.GetOrAddId(typeof(EcsTagComp));

        Assert.Equal(typeof(EcsTagComp), ComponentTypeRegistry.GetType(id));
        Assert.Null(ComponentTypeRegistry.GetType(-1));
        Assert.Null(ComponentTypeRegistry.GetType(int.MaxValue));
    }

    [Fact]
    public void Scanned_component_and_tag_types_hold_registered_ids()
    {
        var world = EcsWorlds.Create();
        try
        {
            // OnInit 扫描注册组件类型；SystemSort 注册系统标签类型
            Assert.True(ComponentTypeRegistry.GetId(typeof(EcsPosComp)) >= 0);
            Assert.True(ComponentTypeRegistry.GetId(typeof(IAwake)) >= 0);
            Assert.True(ComponentTypeRegistry.GetId(typeof(IUpdate)) >= 0);
            Assert.True(ComponentTypeRegistry.GetId(typeof(IEcsProbeTag)) >= 0);

            // 没有系统实现的标签不产生注册
            Assert.Equal(-1, ComponentTypeRegistry.GetId(typeof(IEcsNoSystemTag)));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// ComponentIdCache&lt;T&gt; 是进程级静态缓存：id 跨世界共享是对的，
    /// 但两个世界的 ComponentStore 必须完全隔离、互不串数据。
    /// </summary>
    [Fact]
    public void Shared_type_ids_do_not_bleed_across_worlds()
    {
        var world1 = EcsWorlds.Create();
        var world2 = EcsWorlds.Create(setAsDefault: false);
        try
        {
            var e1 = new YuoEntity(world1);
            var e2 = new YuoEntity(world2);
            Assert.Equal(e1.EntityIndex, e2.EntityIndex); // 两个世界的稠密索引各自从同一水位起步

            var c1 = e1.AddComponent<EcsPosComp>();
            c1.X = 11;

            // world2 同 index 实体没有该组件
            Assert.Null(e2.GetComponent<EcsPosComp>());
            Assert.False(e2.HasComponent<EcsPosComp>());

            var c2 = e2.AddComponent<EcsPosComp>();
            c2.X = 22;

            Assert.Same(c1, e1.GetComponent<EcsPosComp>());
            Assert.Same(c2, e2.GetComponent<EcsPosComp>());
            Assert.Equal(11, e1.GetComponent<EcsPosComp>().X);
            Assert.Equal(22, e2.GetComponent<EcsPosComp>().X);

            // 两个世界的存储对象不是同一个
            var store1 = world1.GetComponentStore(ComponentTypeRegistry.GetId(typeof(EcsPosComp)));
            var store2 = world2.GetComponentStore(ComponentTypeRegistry.GetId(typeof(EcsPosComp)));
            Assert.NotSame(store1, store2);
            Assert.Same(c1, store1!.Get(e1.EntityIndex));
            Assert.Same(c2, store2!.Get(e2.EntityIndex));
        }
        finally
        {
            world2.OnDestroy();
            world1.OnDestroy();
        }
    }
}
