using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// Get/Has/TryGet 三条读取路径在组件存在、不存在、已移除、实体已销毁
/// 四种状态下的返回值口径必须一致；ComponentIdCache&lt;T&gt; 静态缓存跨世界不串。
/// </summary>
public sealed class YuoGetComponentPathTests
{
    [Fact]
    public void Get_has_try_agree_when_component_absent()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);

            Assert.Null(entity.GetComponent<EcsPosComp>());
            Assert.Null(entity.GetComponent(typeof(EcsPosComp)));
            Assert.False(entity.HasComponent<EcsPosComp>());
            Assert.False(entity.HasComponent(typeof(EcsPosComp)));
            Assert.False(entity.TryGetComponent<EcsPosComp>(out var viaGenericTry));
            Assert.Null(viaGenericTry);
            Assert.False(entity.TryGetComponent(typeof(EcsPosComp), out var viaTypeTry));
            Assert.Null(viaTypeTry);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Get_has_try_agree_when_component_present()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();

            Assert.Same(pos, entity.GetComponent<EcsPosComp>());
            Assert.Same(pos, entity.GetComponent(typeof(EcsPosComp)));
            Assert.True(entity.HasComponent<EcsPosComp>());
            Assert.True(entity.HasComponent(typeof(EcsPosComp)));
            Assert.True(entity.TryGetComponent<EcsPosComp>(out var viaGenericTry));
            Assert.Same(pos, viaGenericTry);
            Assert.True(entity.TryGetComponent(typeof(EcsPosComp), out var viaTypeTry));
            Assert.Same(pos, viaTypeTry);
            Assert.Same(pos, world.GetComponent<EcsPosComp>(entity.ID)); // 世界入口同口径
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Get_has_try_agree_after_component_removed()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();
            entity.RemoveComponent<EcsPosComp>();

            Assert.Null(entity.GetComponent<EcsPosComp>());
            Assert.False(entity.HasComponent<EcsPosComp>());
            Assert.False(entity.TryGetComponent<EcsPosComp>(out _));
            Assert.False(entity.HasComponent(typeof(EcsPosComp)));

            // 被摘下的组件对象自身仍存活（同步 RemoveComponent 不 Dispose），
            // 但实体侧三条路径均已不可见——直到 Dispose 才真正失效。
            Assert.False(pos.IsDisposed);
            Assert.True(pos.IsUsable());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Get_has_try_agree_on_disposed_entity()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            entity.Dispose();

            Assert.Null(entity.GetComponent<EcsPosComp>());
            Assert.Null(entity.GetComponent(typeof(EcsPosComp)));
            Assert.False(entity.HasComponent<EcsPosComp>());
            Assert.False(entity.TryGetComponent<EcsPosComp>(out _));
            Assert.False(entity.HasBaseComponent<EcsBaseComp>());
            Assert.Null(entity.GetBaseComponent<EcsBaseComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>缓冲销毁窗口期：IsDestroy=true 但未 Dispose，组件仍可读（销毁尚未结算）。</summary>
    [Fact]
    public void Pending_destroy_component_still_readable_until_replay()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();

            pos.DestroyComponent();
            Assert.True(pos.IsDestroy);
            Assert.Same(pos, entity.GetComponent<EcsPosComp>()); // IsDisposed 未置位，仍可见
            Assert.True(entity.HasComponent<EcsPosComp>());

            world.Update();
            Assert.Null(entity.GetComponent<EcsPosComp>());
            Assert.False(entity.HasComponent<EcsPosComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// 绕过 API 直接写 entity.Components 字典的边角情况：
    /// GetComponentById 的 store miss 后走字典兜底，读取口径仍成立。
    /// </summary>
    [Fact]
    public void Dictionary_fallback_covers_direct_components_write()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var rogue = new EcsPosComp { X = 5, Entity = entity };
            entity.Components[typeof(EcsPosComp)] = rogue; // 旁路写入：跳过 SetComponent/掩码/store

            Assert.Same(rogue, entity.GetComponent<EcsPosComp>());
            Assert.True(entity.HasComponent<EcsPosComp>());
            Assert.True(entity.TryGetComponent<EcsPosComp>(out var got));
            Assert.Same(rogue, got);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>实体 id 入口与组件入口一致：world.GetComponent&lt;T&gt;(id) 直达实体组件。</summary>
    [Fact]
    public void World_level_get_component_by_id_matches_entity_view()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();

            Assert.Same(pos, world.GetComponent<EcsPosComp>(entity.ID));
            Assert.Null(world.GetComponent<EcsPosComp>(long.MaxValue)); // 不存在的实体
            entity.RemoveComponent<EcsPosComp>();
            Assert.Null(world.GetComponent<EcsPosComp>(entity.ID));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// GetComponent&lt;T&gt; 走 ComponentIdCache&lt;T&gt; 静态缓存：两个世界交替使用同一缓存 id，
    /// 各自解析到本世界的 ComponentStore——跨 world 不串数据。
    /// </summary>
    [Fact]
    public void ComponentIdCache_static_path_isolates_worlds()
    {
        var world1 = EcsWorlds.Create();
        var world2 = EcsWorlds.Create(setAsDefault: false);
        try
        {
            var e1 = new YuoEntity(world1);
            var e2 = new YuoEntity(world2);

            var c1 = e1.AddComponent<EcsPosComp>();
            Assert.Same(c1, e1.GetComponent<EcsPosComp>());
            Assert.Null(e2.GetComponent<EcsPosComp>());

            var c2 = e2.AddComponent<EcsPosComp>();

            // 交替读取：静态缓存命中同一 id，但 store 按 world 解析
            Assert.Same(c1, e1.GetComponent<EcsPosComp>());
            Assert.Same(c2, e2.GetComponent<EcsPosComp>());
            Assert.Same(c1, e1.GetComponent<EcsPosComp>());
            Assert.False(e1.GetComponent<EcsPosComp>() == c2);
        }
        finally
        {
            world2.OnDestroy();
            world1.OnDestroy();
        }
    }

    /// <summary>RemoveComponent(Type) 与 RemoveComponent(组件实例) 语义一致：指定键不存在时静默 no-op。</summary>
    [Fact]
    public void Remove_component_on_missing_key_is_noop()
    {
        var world = EcsWorlds.Create();
        try
        {
            LifecycleTrace.Reset();
            var entity = new YuoEntity(world);
            var destroy = EcsWorlds.System<EcsPosDestroySystem>(world);

            entity.RemoveComponent<EcsPosComp>();              // 不存在 → no-op
            entity.RemoveComponent(typeof(EcsPosComp));        // 不存在 → no-op
            entity.RemoveComponent<EcsPosComp>(null!);         // null → no-op
            Assert.Equal(0, destroy.Count);

            entity.AddComponent<EcsPosComp>();
            entity.RemoveComponent(typeof(EcsPosComp));        // 存在 → 正常移除
            Assert.Equal(1, destroy.Count);
            Assert.False(entity.HasComponent<EcsPosComp>());
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
