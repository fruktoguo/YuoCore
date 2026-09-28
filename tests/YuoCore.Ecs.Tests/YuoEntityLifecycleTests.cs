using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// 实体生命周期：create→destroy→create 不带旧组件残留；
/// EntityIndex 复用不串数据；销毁语义的边界行为确定。
/// </summary>
public sealed class YuoEntityLifecycleTests
{
    [Fact]
    public void New_entity_registers_and_carries_entity_data()
    {
        var world = EcsWorlds.Create();
        try
        {
            var baseline = world.Entities.Count;
            var entity = new YuoEntity(world);

            Assert.Equal(baseline + 1, world.Entities.Count);
            Assert.Same(entity, world.GetEntity(entity.ID));
            Assert.NotNull(entity.EntityData);
            Assert.Same(entity, entity.EntityData.Entity);
            Assert.True(entity.EntityIndex >= 0);
            Assert.False(entity.IsDisposed);
            Assert.False(entity.IsDestroy);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Deferred_destroy_removes_entity_at_next_update()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();

            entity.Destroy();
            Assert.True(entity.IsDestroy);
            Assert.False(entity.IsDisposed);          // 已标记未回放
            Assert.Same(entity, world.GetEntity(entity.ID)); // 仍在世界字典
            Assert.Same(pos, entity.GetComponent<EcsPosComp>()); // 回放前组件仍可访问

            world.Update();

            Assert.True(entity.IsDisposed);
            Assert.True(pos.IsDisposed);
            Assert.Null(world.GetEntity(entity.ID));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Destroy_recreate_leaves_no_component_residue()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            var index = e1.EntityIndex;
            e1.AddComponent<EcsPosComp>();
            e1.AddComponent<EcsVelComp>();
            var destroy = EcsWorlds.System<EcsPosDestroySystem>(world);

            e1.Destroy();
            world.Update();

            var e2 = new YuoEntity(world);
            Assert.Equal(index, e2.EntityIndex); // 回收索引被复用
            Assert.Empty(e2.Components);
            Assert.Null(e2.GetComponent<EcsPosComp>());
            Assert.False(e2.HasComponent<EcsVelComp>());
            Assert.Equal(1, destroy.Count); // 旧实体的销毁已结算且未重复派发
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Double_destroy_and_double_dispose_are_idempotent()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            var destroy = EcsWorlds.System<EcsPosDestroySystem>(world);

            entity.Destroy();
            entity.Destroy(); // IsDestroy 幂等
            world.Update();
            Assert.True(entity.IsDisposed);
            Assert.Equal(1, destroy.Count);

            entity.Dispose();  // IsDisposed 幂等
            entity.Dispose();
            Assert.Equal(1, destroy.Count);
            Assert.Null(world.GetEntity(entity.ID));

            YuoWorld.DestroyEntity(null!);   // null 安全 no-op
            YuoWorld.DestroyComponent(null!);
            entity.Destroy(); // 已销毁再标记也是 no-op
            world.Update();
            Assert.Equal(1, destroy.Count);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>销毁不存在/其他世界的实体：DestroyEntity 只把实体推进其所属世界的缓冲。</summary>
    [Fact]
    public void Destroy_targets_owning_world_regardless_of_default_instance()
    {
        var world1 = EcsWorlds.Create();
        var world2 = EcsWorlds.Create(setAsDefault: false);
        try
        {
            var foreign = new YuoEntity(world2);
            Assert.Same(world2, foreign.World);

            foreign.Destroy(); // 静态入口 → entity.World 的缓冲
            world1.Update();   // world1 回放不处理 world2 的实体
            Assert.False(foreign.IsDisposed);

            world2.Update();   // world2 回放生效
            Assert.True(foreign.IsDisposed);
            Assert.Null(world2.GetEntity(foreign.ID));
        }
        finally
        {
            world2.OnDestroy();
            world1.OnDestroy();
        }
    }

    [Fact]
    public void Parent_dispose_cascades_to_children()
    {
        var world = EcsWorlds.Create();
        try
        {
            LifecycleTrace.Reset();
            var parent = new YuoEntity(world);
            var child = parent.AddChild();
            var grandchild = child.AddChild();
            child.AddComponent<EcsPosComp>();
            grandchild.AddComponent<EcsVelComp>();
            var posDestroy = EcsWorlds.System<EcsPosDestroySystem>(world);
            var velDestroy = EcsWorlds.System<EcsVelDestroySystem>(world);

            parent.Dispose();

            Assert.True(child.IsDisposed);
            Assert.True(grandchild.IsDisposed);
            Assert.Equal(1, posDestroy.Count);
            Assert.Equal(1, velDestroy.Count);
            Assert.Null(world.GetEntity(child.ID));
            Assert.Null(world.GetEntity(grandchild.ID));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>子实体组件移除时同步更新父级 ChildComponents 视图。</summary>
    [Fact]
    public void Child_components_stay_consistent_with_entity_components()
    {
        var world = EcsWorlds.Create();
        try
        {
            var parent = new YuoEntity(world);
            var child = parent.AddChild<EcsPosComp>();

            Assert.Contains(child.Entity, parent.Children);
            Assert.Same(child, parent.GetChild<EcsPosComp>());

            child.Entity.RemoveComponent<EcsPosComp>();
            Assert.Null(parent.GetChild<EcsPosComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>父实体已销毁（ChildComponents 置空）时，存活的孤儿实体 ReplaceComponent 不得 NRE。</summary>
    [Fact]
    public void Replace_component_with_disposed_parent_does_not_throw()
    {
        var world = EcsWorlds.Create();
        try
        {
            var parent = new YuoEntity(world);
            parent.Dispose();
            Assert.True(parent.IsDisposed);

            var child = new YuoEntity(world);
            // 把存活子实体挂到已释放父实体上（stale Parent 指针场景）
            child.SetParent(parent);
            Assert.False(child.IsDisposed);
            Assert.Same(parent, child.Parent);

            // 修复前：Parent.ChildComponents 已置空 → NullReferenceException
            child.ReplaceComponent(new EcsVelComp());
            Assert.NotNull(child.GetComponent<EcsVelComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>已释放实体上继续写入组件：索引已失效，不得向稀疏存储越界写入。</summary>
    [Fact]
    public void Set_component_on_disposed_entity_does_not_touch_store()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.Dispose();
            Assert.True(entity.IsDisposed);

            entity.SetComponent(typeof(EcsPosComp), new EcsPosComp());
            Assert.Null(entity.GetComponent<EcsPosComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>迭代期间加入后销毁的实体，压实/重建时不得因失效索引抛异常。</summary>
    [Fact]
    public void Disposed_member_is_cleaned_during_compaction()
    {
        var world = EcsWorlds.Create();
        try
        {
            LifecycleTrace.Reset();
            var system = EcsWorlds.System<EcsPosDestroySystem>(world);
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();

            // 直接杀掉成员实体后驱动系统更新：死成员留在成员集直到压实
            entity.Dispose();
            world.Update();
            world.Update();

            Assert.DoesNotContain(entity, system.Entities);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>组件 IDestroy 回调内重入 Dispose 实体：内层直接退出，外层完成完整清理不 NRE。</summary>
    [Fact]
    public void Reentrant_dispose_inside_component_destroy_is_safe()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsReentrantDisposeComp>();

            var ex = Record.Exception(() => entity.Dispose());
            Assert.Null(ex);
            Assert.True(entity.IsDisposed);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>实体 id 全局唯一：静态计数器保证跨世界/同秒内不重复。</summary>
    [Fact]
    public void Entity_ids_are_unique_across_entities_and_worlds()
    {
        var world1 = EcsWorlds.Create();
        var world2 = EcsWorlds.Create(setAsDefault: false);
        try
        {
            var ids = Enumerable.Range(0, 20)
                .SelectMany(i => new[]
                {
                    new YuoEntity(world1).ID,
                    new YuoEntity(world2).ID
                })
                .ToArray();

            Assert.Equal(ids.Length, ids.Distinct().Count());
        }
        finally
        {
            world2.OnDestroy();
            world1.OnDestroy();
        }
    }
}
