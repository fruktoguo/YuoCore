using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// 世界/实体级集合枚举与组件集合口径：
/// 缓冲销毁期间枚举不抛、不重复、不遗漏；回放后集合收敛。
/// </summary>
public sealed class YuoQueryEnumerationTests
{
    [Fact]
    public void World_entities_enumeration_is_complete_and_stable_under_buffered_destroy()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entities = Enumerable.Range(0, 5).Select(_ => new YuoEntity(world)).ToArray();
            var baseline = world.Entities.Keys.Order().ToArray();
            Assert.Equal(5 + 2, baseline.Length); // WorldMain + 默认场景 + 5 个新建

            var visited = new List<long>();
            var doomed = new[] { entities[1], entities[3] };
            foreach (var pair in world.Entities)
            {
                visited.Add(pair.Key);
                if (doomed.Contains(pair.Value))
                {
                    pair.Value.Destroy(); // 只进命令缓冲，不改字典 → 枚举安全
                }
            }

            // 不重复、不遗漏：与枚举前快照完全一致
            Assert.Equal(baseline, visited.Order().ToArray());
            Assert.All(doomed, e => Assert.True(e.IsDestroy));
            Assert.All(doomed, e => Assert.False(e.IsDisposed));

            // 注意：entity.ID 在 Dispose 后降级为 -1，必须在回放前捕获
            var doomedIds = doomed.Select(e => e.ID).ToArray();
            world.Update(); // 回放后字典收敛
            Assert.Equal(baseline.Except(doomedIds).Order().ToArray(),
                world.Entities.Keys.Order().ToArray());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void World_component_set_tracks_add_remove_and_deferred_destroy()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            var c1 = e1.AddComponent<EcsPosComp>();
            var c2 = e2.AddComponent<EcsPosComp>();

            var all = world.GetAllComponents<EcsPosComp>();
            Assert.Equal(2, all.Count);
            Assert.Contains<YuoComponent>(c1, all);
            Assert.Contains<YuoComponent>(c2, all);

            // 缓冲销毁期间集合仍持该组件（待回放），枚举安全
            c1.DestroyComponent();
            var seen = all.ToArray();
            Assert.Equal(2, seen.Length);
            Assert.Contains(c1, seen);

            world.Update();
            all = world.GetAllComponents<EcsPosComp>();
            Assert.Single(all);
            Assert.Same(c2, all.Single());

            e2.RemoveComponent<EcsPosComp>();
            Assert.Empty(world.GetAllComponents<EcsPosComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>GetAllComponents 对未知组件类型返回空集而不是抛异常。</summary>
    [Fact]
    public void GetAllComponents_for_untouched_type_is_empty_set()
    {
        var world = EcsWorlds.Create();
        try
        {
            var all = world.GetAllComponents<EcsExtraComp>();
            Assert.NotNull(all);
            Assert.Empty(all);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>entity.Components 字典视图与逐类型 Get/Has 口径一致。</summary>
    [Fact]
    public void Entity_components_dict_mirrors_get_has_semantics()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();
            var vel = entity.AddComponent<EcsVelComp>();

            // EntityData 不进入组件字典（注释约定：不在组件列表显示）
            Assert.Equal(2, entity.Components.Count);
            Assert.False(entity.Components.ContainsKey(typeof(EntityComponent)));
            Assert.False(entity.HasComponent<EntityComponent>());

            foreach (var pair in entity.Components)
            {
                Assert.Same(pair.Value, entity.GetComponent(pair.Key));
                Assert.True(entity.HasComponent(pair.Key));
            }
            Assert.Contains(typeof(EcsPosComp), entity.Components.Keys);
            Assert.Contains(typeof(EcsVelComp), entity.Components.Keys);
            Assert.Same(pos, entity.Components[typeof(EcsPosComp)]);
            Assert.Same(vel, entity.Components[typeof(EcsVelComp)]);

            // 缓冲销毁不改字典视图，回放后收敛
            pos.DestroyComponent();
            Assert.Equal(2, entity.Components.Count);
            world.Update();
            Assert.Single(entity.Components);
            Assert.False(entity.Components.ContainsKey(typeof(EcsPosComp)));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// [SingleComponent] 的口径：per-entity 唯一，跨实体各挂各的；
    /// 同实体二次 SetComponent 同类型实例触发旧实例销毁。
    /// </summary>
    [Fact]
    public void Single_component_is_unique_per_entity_not_globally()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            var s1 = e1.AddComponent<EcsSingleComp>();
            var s2 = e2.AddComponent<EcsSingleComp>();

            Assert.Equal(2, world.GetAllComponents<EcsSingleComp>().Count);
            Assert.False(s1.IsDestroy);
            Assert.False(s2.IsDestroy);

            // 同实体二次挂载触发旧实例销毁（缓冲路径），集合收敛为最新实例
            var s3 = new EcsSingleComp { V = 3 };
            e1.SetComponent(typeof(EcsSingleComp), s3);
            Assert.True(s1.IsDestroy);

            world.Update();
            var remaining = world.GetAllComponents<EcsSingleComp>();
            Assert.Equal(2, remaining.Count);
            Assert.Contains<YuoComponent>(s2, remaining);
            Assert.Contains<YuoComponent>(s3, remaining);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// BUG: 单例组件同实体二次 SetComponent 后实体视图与世界视图分叉——
    /// 旧实例销毁时把 Components[键] 与 store 槽位一并清掉，新实例 s3 只在世界集合里，
    /// entity.GetComponent&lt;EcsSingleComp&gt;() 返回 null 而 s3.Entity 仍指向该实体。
    /// 期望语义：实体应能通过声明类型取回当前生效的单例组件（s3）。
    /// </summary>
    [Fact]
    public void Single_component_readd_keeps_entity_and_world_views_consistent()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var s1 = new EcsSingleComp { V = 1 };
            entity.SetComponent(typeof(EcsSingleComp), s1);

            var s3 = new EcsSingleComp { V = 3 };
            entity.SetComponent(typeof(EcsSingleComp), s3);
            world.Update(); // 回放旧实例销毁

            // BUG: Components.TryAdd 幂等失败导致实体字典仍指向 s1，
            // s1 销毁流程随带删除了声明类型键；两侧口径分裂。
            Assert.Same(s3, entity.GetComponent<EcsSingleComp>());
            Assert.True(entity.HasComponent<EcsSingleComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
