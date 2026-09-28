using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// ComponentStore 稀疏集的直接不变量验证：
/// sparse[entityIndex] -> densePos+1（0=不存在），dense 数组 swap-remove。
///  invariant：∀ i ∈ [0,Count)  ⇒  Get(DenseEntities[i]) == Dense[i] 且 GetSlot(DenseEntities[i]) == i+1。
/// </summary>
public sealed class YuoComponentStoreTests
{
    private static void AssertDenseSparseConsistent(ComponentStore store)
    {
        for (var i = 0; i < store.Count; i++)
        {
            var entityIndex = store.DenseEntities[i];
            Assert.Equal(i + 1, store.GetSlot(entityIndex));
            Assert.Same(store.Dense[i], store.Get(entityIndex));
            Assert.True(store.Has(entityIndex));
        }
    }

    [Fact]
    public void Set_get_has_roundtrip()
    {
        var store = new ComponentStore(0, typeof(EcsPosComp));
        var component = new EcsPosComp { X = 7 };

        Assert.Equal(0, store.Count);
        Assert.Null(store.Get(3));
        Assert.False(store.Has(3));
        Assert.Equal(0, store.GetSlot(3));

        store.Set(3, component);

        Assert.Equal(1, store.Count);
        Assert.Same(component, store.Get(3));
        Assert.True(store.Has(3));
        Assert.Equal(1, store.GetSlot(3));
        AssertDenseSparseConsistent(store);
    }

    [Fact]
    public void Remove_clears_slot_and_miss_is_consistent()
    {
        var store = new ComponentStore(0, typeof(EcsPosComp));
        store.Set(2, new EcsPosComp());
        store.Remove(2);

        Assert.Equal(0, store.Count);
        Assert.Null(store.Get(2));
        Assert.False(store.Has(2));
        Assert.Equal(0, store.GetSlot(2));

        // 对已移除/从未存在/越界的索引再次移除：约定为静默 no-op
        store.Remove(2);
        store.Remove(99);
        store.Remove(5000);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Remove_then_readd_reuses_storage_cleanly()
    {
        var store = new ComponentStore(0, typeof(EcsPosComp));
        var first = new EcsPosComp { X = 1 };
        var second = new EcsPosComp { X = 2 };

        store.Set(4, first);
        store.Remove(4);
        store.Set(4, second);

        Assert.Equal(1, store.Count);
        Assert.Same(second, store.Get(4));
        AssertDenseSparseConsistent(store);
    }

    /// <summary>swap-remove 重灾区：移除中间元素后，其余实体的组件必须仍可正确寻址。</summary>
    [Fact]
    public void Swap_remove_preserves_all_remaining_slots()
    {
        var store = new ComponentStore(0, typeof(EcsPosComp));
        var components = Enumerable.Range(0, 9).Select(i => new EcsPosComp { X = i }).ToArray();

        for (var i = 0; i < components.Length; i++)
        {
            store.Set(i, components[i]);
        }

        // 摘掉中间、头部、尾部三种位置
        store.Remove(4);
        store.Remove(0);
        store.Remove(8);

        Assert.Equal(6, store.Count);
        AssertDenseSparseConsistent(store);

        foreach (var removed in new[] { 0, 4, 8 })
        {
            Assert.Null(store.Get(removed));
            Assert.False(store.Has(removed));
            Assert.Equal(0, store.GetSlot(removed));
        }

        foreach (var i in new[] { 1, 2, 3, 5, 6, 7 })
        {
            Assert.Same(components[i], store.Get(i));
        }
    }

    /// <summary>同一 entityIndex 重复 Set 是覆盖语义（等价字典索引器），不增加 Count。</summary>
    [Fact]
    public void Set_on_existing_slot_overwrites_without_growing()
    {
        var store = new ComponentStore(0, typeof(EcsPosComp));
        var first = new EcsPosComp { X = 1 };
        var second = new EcsPosComp { X = 2 };

        store.Set(5, first);
        store.Set(5, second);

        Assert.Equal(1, store.Count);
        Assert.Same(second, store.Get(5));
        AssertDenseSparseConsistent(store);
    }

    /// <summary>entityIndex 超出 sparse 初始容量（64）时自动扩容且语义不变。</summary>
    [Fact]
    public void Sparse_array_grows_for_high_entity_index()
    {
        var store = new ComponentStore(0, typeof(EcsPosComp));
        var far = new EcsPosComp { X = 42 };

        store.Set(300, far);

        Assert.Same(far, store.Get(300));
        Assert.True(store.Has(300));
        AssertDenseSparseConsistent(store);

        store.Remove(300);
        Assert.Null(store.Get(300));
    }

    /// <summary>dense 初始容量 16，超出后扩容且全部条目保持可寻址。</summary>
    [Fact]
    public void Dense_array_grows_and_stays_consistent()
    {
        var store = new ComponentStore(0, typeof(EcsPosComp));
        var components = Enumerable.Range(0, 40).Select(i => new EcsPosComp { X = i }).ToArray();

        for (var i = 0; i < components.Length; i++)
        {
            store.Set(i * 2, components[i]); // 交错 index，同时撑大 sparse
        }

        Assert.Equal(40, store.Count);
        AssertDenseSparseConsistent(store);

        for (var i = 0; i < components.Length; i += 2)
        {
            store.Remove(i * 2);
        }

        Assert.Equal(20, store.Count);
        AssertDenseSparseConsistent(store);
    }

    /// <summary>世界级验证：AddComponent/RemoveComponent/ReplaceComponent 三个写点与 entity.Components 严格同点位。</summary>
    [Fact]
    public void World_store_mirrors_entity_components_dict()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();
            var store = world.GetComponentStore(ComponentTypeRegistry.GetId(typeof(EcsPosComp)));

            Assert.NotNull(store);
            Assert.Same(pos, store!.Get(entity.EntityIndex));
            Assert.Equal(1, store.Count);

            // ReplaceComponent：同一 key 覆盖到 store
            var replacement = new EcsPosComp { X = 9 };
            entity.ReplaceComponent(replacement);
            Assert.Same(replacement, store.Get(entity.EntityIndex));
            Assert.Equal(1, store.Count);

            entity.RemoveComponent<EcsPosComp>();
            Assert.Null(store.Get(entity.EntityIndex));
            Assert.Equal(0, store.Count);
            AssertDenseSparseConsistent(store);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>实体销毁后 entityIndex 回收复用，稀疏槽位必须先清扫再交接，不得串数据。</summary>
    [Fact]
    public void Entity_index_reuse_sweeps_stale_store_slots()
    {
        var world = EcsWorlds.Create();
        try
        {
            var first = new YuoEntity(world);
            var index = first.EntityIndex;
            var pos = first.AddComponent<EcsPosComp>();
            var vel = first.AddComponent<EcsVelComp>();
            var posStore = world.GetComponentStore(ComponentTypeRegistry.GetId(typeof(EcsPosComp)))!;
            var velStore = world.GetComponentStore(ComponentTypeRegistry.GetId(typeof(EcsVelComp)))!;

            Assert.Same(pos, posStore.Get(index));
            Assert.Same(vel, velStore.Get(index));

            first.Dispose();

            Assert.Null(posStore.Get(index));
            Assert.Null(velStore.Get(index));
            AssertDenseSparseConsistent(posStore);
            AssertDenseSparseConsistent(velStore);

            var second = new YuoEntity(world);
            Assert.Equal(index, second.EntityIndex); // LIFO 复用
            Assert.Null(second.GetComponent<EcsPosComp>());
            Assert.False(second.HasComponent<EcsPosComp>());

            var fresh = second.AddComponent<EcsPosComp>();
            Assert.Same(fresh, posStore.Get(index));
            Assert.NotSame(pos, posStore.Get(index));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>销毁路径（Destroy→Update 回放）与 Dispose 同步路径同样清扫 store。</summary>
    [Fact]
    public void Deferred_entity_destroy_also_sweeps_store()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            var index = entity.EntityIndex;
            var store = world.GetComponentStore(ComponentTypeRegistry.GetId(typeof(EcsPosComp)))!;

            entity.Destroy();
            // 销毁命令在下一帧 Update 开头回放，此间 store 仍是旧数据但实体已标记
            Assert.NotNull(store.Get(index));

            world.Update();

            Assert.Null(store.Get(index));
            var reused = new YuoEntity(world);
            Assert.Equal(index, reused.EntityIndex);
            Assert.Null(reused.GetComponent<EcsPosComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
