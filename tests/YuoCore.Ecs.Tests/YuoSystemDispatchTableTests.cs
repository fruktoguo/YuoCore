using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// 三张稠密派发表（_systemsByComponentId / _systemsByBaseTypeId / _systemsByTagId）
/// 是组件→系统、基类→系统、标签→系统的唯一真相源：命中面必须完整、互不串键。
/// </summary>
public sealed class YuoSystemDispatchTableTests
{
    [Fact]
    public void Component_dispatches_to_every_registered_system()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();

            // 同一组件类型下注册的所有 IAwake 系统都必须命中
            Assert.Equal(1, EcsWorlds.System<EcsPosAwakeSystem>(world).Count);
            Assert.Equal(1, EcsWorlds.System<EcsPosAwakeSecondSystem>(world).Count);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Component_dispatch_is_scoped_to_its_own_key()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            entity.AddComponent<EcsVelComp>();

            // VelComp 的添加不触发 PosComp 键下系统的 Awake（各自只计自己的 1 次）
            Assert.Equal(1, EcsWorlds.System<EcsPosAwakeSystem>(world).Count);
            Assert.Equal(0, EcsWorlds.System<EcsBaseAwakeSystem>(world).Count);

            world.Update();
            var posSeen = EcsWorlds.System<EcsPosUpdateSystem>(world).Seen;
            var velSeen = EcsWorlds.System<EcsVelUpdateSystem>(world).Seen;
            Assert.Equal(new[] { entity.ID }, posSeen);
            Assert.Equal(new[] { entity.ID }, velSeen);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>同时监听多组件的系统：任一组件 add 都会做匹配检查，集齐后才真正派发。</summary>
    [Fact]
    public void Multi_influence_system_runs_only_when_all_present()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var both = EcsWorlds.System<EcsPosVelAwakeSystem>(world);

            entity.AddComponent<EcsPosComp>();
            Assert.Equal(0, both.Count); // 缺 VelComp：检查失败、不派发、不入成员

            entity.AddComponent<EcsVelComp>();
            Assert.Equal(1, both.Count); // 集齐瞬间命中一次
            Assert.Equal(new[] { entity.ID }, both.Seen);
            Assert.Contains(entity, both.Entities);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>tag 键派发：只有实现了该 tag 的系统在对应阶段被调度。</summary>
    [Fact]
    public void Tag_keyed_dispatch_only_runs_systems_with_that_tag()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            entity.AddComponent<EcsTagComp>();

            var update = EcsWorlds.System<EcsPosUpdateSystem>(world);
            var tagUpdate = EcsWorlds.System<EcsTagUpdateOnlySystem>(world);
            var probe = EcsWorlds.System<EcsProbeSystem>(world);

            world.Update();
            Assert.Equal(1, update.RunCount);
            Assert.Equal(1, tagUpdate.RunCount);
            Assert.Equal(0, probe.Count); // IEcsProbeTag 不在 Update 阶段派发

            // LateUpdate/FixedUpdate 不得触发 IUpdate 系统
            world.LateUpdate();
            world.FixedUpdate();
            Assert.Equal(1, update.RunCount);
            Assert.Equal(1, tagUpdate.RunCount);

            // 自定义 tag 走 _systemsByTagId 直达
            YuoWorld.RunSystemForAll<IEcsProbeTag>();
            Assert.Equal(1, probe.Count);
            Assert.Equal(new[] { entity.ID }, probe.Seen);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>没有任何系统实现的标签：tagId 未注册，派发是空操作而不是报错。</summary>
    [Fact]
    public void Unregistered_tag_dispatch_is_a_noop()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();

            Assert.Equal(-1, ComponentTypeRegistry.GetId(typeof(IEcsNoSystemTag)));

            // 单实体与全体两种入口都必须安全空跑
            YuoWorld.RunSystem(typeof(IEcsNoSystemTag), entity);
            YuoWorld.RunSystemForAll(typeof(IEcsNoSystemTag));

            Assert.Equal(0, EcsWorlds.System<EcsPosUpdateSystem>(world).RunCount);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>稠密表内容与 MultiMap 持有同一 List 实例：内部直查口径与字典口径一致。</summary>
    [Fact]
    public void Dense_index_tables_match_declared_system_sets()
    {
        var world = EcsWorlds.Create();
        try
        {
            var posId = ComponentTypeRegistry.GetId(typeof(EcsPosComp));
            var byComponent = world.GetSystemsByComponentId(posId)!;
            var expected = world.GetAllSystemOfComponent()[typeof(EcsPosComp)];

            Assert.Same(byComponent, expected); // 同一 List 实例，非快照拷贝
            Assert.Contains(EcsWorlds.System<EcsPosAwakeSystem>(world), byComponent);
            Assert.Contains(EcsWorlds.System<EcsPosAwakeSecondSystem>(world), byComponent);
            Assert.Contains(EcsWorlds.System<EcsPosDestroySystem>(world), byComponent);
            Assert.Contains(EcsWorlds.System<EcsPosVelAwakeSystem>(world), byComponent);

            var updateId = ComponentTypeRegistry.GetId(typeof(IUpdate));
            var byTag = world.GetSystemsByTagId(updateId)!;
            Assert.Same(byTag, world.GetAllSystemOfTag()[typeof(IUpdate)]);
            Assert.Contains(EcsWorlds.System<EcsPosUpdateSystem>(world), byTag);
            Assert.Contains(EcsWorlds.System<EcsTagUpdateOnlySystem>(world), byTag);
            Assert.DoesNotContain(EcsWorlds.System<EcsPosAwakeSystem>(world), byTag);

            var baseId = ComponentTypeRegistry.GetId(typeof(EcsBaseComp));
            var byBase = world.GetSystemsByBaseTypeId(baseId)!;
            Assert.Contains(EcsWorlds.System<EcsBaseAwakeSystem>(world), byBase);
            Assert.Contains(EcsWorlds.System<EcsBaseUpdateSystem>(world), byBase);
            Assert.Contains(EcsWorlds.System<EcsBaseDestroySystem>(world), byBase);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>重复 SystemSort（刷新路径）重建索引后派发行为保持一致。</summary>
    [Fact]
    public void Dispatch_survives_system_resort()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();

            world.SystemSort(); // 重建三张稠密表 + 重新匹配已有实体

            var update = EcsWorlds.System<EcsPosUpdateSystem>(world);
            Assert.Contains(entity, update.Entities);

            var e2 = new YuoEntity(world);
            e2.AddComponent<EcsPosComp>();

            world.Update();
            Assert.Equal(2, update.RunCount);
            // 稠密集合按加入顺序迭代，先创建的实体先跑
            Assert.Equal(new[] { entity.ID, e2.ID }, update.Seen);
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
