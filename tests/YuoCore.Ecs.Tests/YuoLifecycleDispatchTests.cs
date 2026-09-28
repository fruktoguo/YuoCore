using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// 生命周期语义红线：IAwake/IStart/IDestroy 的触发时机、次数、顺序、对象身份。
/// </summary>
public sealed class YuoLifecycleDispatchTests
{
    [Fact]
    public void Add_component_fires_awake_exactly_once()
    {
        var world = EcsWorlds.Create();
        try
        {
            LifecycleTrace.Reset();
            var entity = new YuoEntity(world);

            var awake = EcsWorlds.System<EcsPosAwakeSystem>(world);
            var awake2 = EcsWorlds.System<EcsPosAwakeSecondSystem>(world);
            Assert.Equal(0, awake.Count);

            var pos = entity.AddComponent<EcsPosComp>();
            Assert.Equal(1, awake.Count);
            Assert.Equal(1, awake2.Count);
            Assert.Equal(new[] { entity.ID }, awake.Seen);

            // 重复添加同类型组件是幂等获取，不再触发 Awake
            var again = entity.AddComponent<EcsPosComp>();
            Assert.Same(pos, again);
            Assert.Equal(1, awake.Count);
            Assert.Equal(1, awake2.Count);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Remove_and_readd_replays_awake_each_time()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var awake = EcsWorlds.System<EcsPosAwakeSystem>(world);
            var destroy = EcsWorlds.System<EcsPosDestroySystem>(world);

            entity.AddComponent<EcsPosComp>();
            entity.RemoveComponent<EcsPosComp>();
            entity.AddComponent<EcsPosComp>();

            // add→remove→add：Awake 两次、Destroy 一次，各自恰好一次
            Assert.Equal(2, awake.Count);
            Assert.Equal(1, destroy.Count);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Start_fires_once_at_next_update_not_at_add()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var start = EcsWorlds.System<EcsPosStartSystem>(world);

            entity.AddComponent<EcsPosComp>();
            Assert.Equal(0, start.Count); // IStart 是帧末统一回调，不在 add 现场

            world.Update();
            Assert.Equal(1, start.Count);
            Assert.Equal(new[] { entity.ID }, start.Seen);

            world.Update();
            Assert.Equal(1, start.Count); // 每组件一生只 Start 一次
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>RemoveComponent 是同步路径：立刻移除+派发 IDestroy，不等命令缓冲。</summary>
    [Fact]
    public void Remove_component_dispatches_destroy_immediately_once()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            var destroy = EcsWorlds.System<EcsPosDestroySystem>(world);

            entity.RemoveComponent<EcsPosComp>();
            Assert.Equal(1, destroy.Count);

            world.Update();
            Assert.Equal(1, destroy.Count); // 同步移除后不再有任何回放
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// BUG: DestroyComponent 走命令缓冲：标记当下不派发，下一帧 Update 开头统一回放——
    /// 但回放时组件 IsDestroy 已置位，YuoSystem&lt;T&gt;.RunEntity 的 IsUsable() 过滤
    /// 直接把 IDestroy 派发吞掉（destroy.Count 恒为 0）。同步 RemoveComponent/Dispose
    /// 路径派发正常，三条销毁入口语义不一致。
    /// </summary>
    [Fact]
    public void Destroy_component_is_deferred_to_next_update()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();
            var destroy = EcsWorlds.System<EcsPosDestroySystem>(world);

            pos.DestroyComponent();
            Assert.True(pos.IsDestroy);
            Assert.False(pos.IsDisposed);
            Assert.Equal(0, destroy.Count); // 标记后并未立即派发

            world.Update();
            Assert.Equal(1, destroy.Count);
            Assert.True(pos.IsDisposed);

            world.Update();
            Assert.Equal(1, destroy.Count); // 缓冲消费后不再重复派发
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// BUG: 系统 Run 内标记销毁的组件在下一帧开头回放时同样被 IsUsable() 过滤吞掉
    /// IDestroy 派发——根因同 Destroy_component_is_deferred_to_next_update。
    /// </summary>
    [Fact]
    public void Destroy_issued_inside_run_replays_at_next_update()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var tag = entity.AddComponent<EcsTagComp>();
            var selfDestruct = EcsWorlds.System<EcsSelfDestructSystem>(world);
            var destroy = EcsWorlds.System<EcsTagDestroySystem>(world);
            selfDestruct.Armed = true;

            world.Update(); // Run 内调用 DestroyComponent → 进缓冲
            Assert.Equal(1, selfDestruct.RunCount);
            Assert.Equal(0, destroy.Count);
            Assert.True(tag.IsDestroy);
            Assert.False(tag.IsDisposed);

            world.Update(); // 帧首命令缓冲回放
            Assert.Equal(1, destroy.Count);
            Assert.True(tag.IsDisposed);
            Assert.Equal(new[] { entity.ID }, destroy.Seen);

            world.Update();
            Assert.Equal(1, destroy.Count);
            Assert.Equal(1, selfDestruct.RunCount); // 实体已离开该系统成员集
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// BUG: 多个组件同一帧标记销毁时回放完整且各自恰好一次（缓冲是集合语义）——
    /// 当前实现全部被 RunEntity 的 IsUsable() 过滤吞掉 IDestroy 派发，根因同上。
    /// </summary>
    [Fact]
    public void Deferred_destroys_replay_completely_once_each()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var pos = entity.AddComponent<EcsPosComp>();
            var vel = entity.AddComponent<EcsVelComp>();
            var tag = entity.AddComponent<EcsTagComp>();
            var posDestroy = EcsWorlds.System<EcsPosDestroySystem>(world);
            var velDestroy = EcsWorlds.System<EcsVelDestroySystem>(world);
            var tagDestroy = EcsWorlds.System<EcsTagDestroySystem>(world);

            pos.DestroyComponent();
            vel.DestroyComponent();
            tag.DestroyComponent();

            Assert.Equal(0, posDestroy.Count + velDestroy.Count + tagDestroy.Count);
            world.Update();

            Assert.Equal(1, posDestroy.Count);
            Assert.Equal(1, velDestroy.Count);
            Assert.Equal(1, tagDestroy.Count);
            Assert.True(pos.IsDisposed && vel.IsDisposed && tag.IsDisposed);
            Assert.True(entity.Components.Count == 0);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// 实体销毁时其全部组件各自恰好一次 IDestroy，顺序跟随 Components 写入顺序。
    /// 派发当下组件仍能被 GetComponent 取回（世界映射先删、实体字典后删的点位语义）。
    /// </summary>
    [Fact]
    public void Entity_dispose_fires_destroy_per_component_in_add_order()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            entity.AddComponent<EcsVelComp>();
            var posDestroy = EcsWorlds.System<EcsPosDestroySystem>(world);
            var velDestroy = EcsWorlds.System<EcsVelDestroySystem>(world);

            LifecycleTrace.Reset(); // 只观察销毁序列，屏蔽 add 阶段记录
            entity.Dispose();

            Assert.Equal(1, posDestroy.Count);
            Assert.Equal(1, velDestroy.Count);
            Assert.Equal(new[] { "destroy:pos", "destroy:vel" }, LifecycleTrace.Entries);

            // 派发时实体仍能取回组件，但世界级组件集已先行摘除
            Assert.True(posDestroy.ComponentReadableDuringDestroy);
            Assert.True(posDestroy.RemovedFromWorldSetBeforeDispatch);
            Assert.Empty(entity.Components);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// 派生组件销毁时先走基础组件系统（_systemsByBaseTypeId），再走具体组件系统
    /// （_systemsByComponentId）——World.RemoveComponent 里的固定顺序。
    /// </summary>
    [Fact]
    public void Base_system_destroy_dispatch_precedes_concrete_dispatch()
    {
        var world = EcsWorlds.Create();
        try
        {
            LifecycleTrace.Reset();
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsDerivedComp>();

            Assert.Equal(new[] { "awake:base", "awake:derived" }, LifecycleTrace.Entries);

            LifecycleTrace.Reset();
            entity.RemoveComponent<EcsDerivedComp>();

            // 基础组件系统的 IDestroy 先于具体组件路径派发；EcsDerivedComp 上没有 IDestroy 系统
            Assert.Equal(new[] { "destroy:base" }, LifecycleTrace.Entries);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>ReplaceComponent 走 ISwitchComponent 语义：不重演 Awake/Destroy，组件身份即刻替换。</summary>
    [Fact]
    public void Replace_component_fires_switch_once_without_reawake()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var original = entity.AddComponent<EcsPosComp>();
            var awake = EcsWorlds.System<EcsPosAwakeSystem>(world);
            var destroy = EcsWorlds.System<EcsPosDestroySystem>(world);
            var switcher = EcsWorlds.System<EcsPosSwitchSystem>(world);
            Assert.Equal(1, awake.Count);
            Assert.Equal(0, switcher.Count);

            var replacement = new EcsPosComp { X = 99 };
            entity.ReplaceComponent(replacement);

            Assert.Equal(1, switcher.Count);
            Assert.Equal(1, awake.Count);   // replace 不补 Awake
            Assert.Equal(0, destroy.Count); // replace 不补 Destroy
            Assert.Same(replacement, entity.GetComponent<EcsPosComp>());
            Assert.False(replacement.IsDisposed);

            // 再次替换仍各自触发一次 Switch
            var third = new EcsPosComp { X = 7 };
            entity.ReplaceComponent(third);
            Assert.Equal(2, switcher.Count);
            Assert.Same(third, entity.GetComponent<EcsPosComp>());
            Assert.Equal(1, awake.Count);
            Assert.Equal(0, destroy.Count);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// Run 中给实体挂上匹配"本系统"的组件：membership 延迟并入，下一帧才进入迭代；
    /// 与此同时 IAwake 派发被 CheckValidEntity 的 pendingJoin 吞掉。
    /// </summary>
    [Fact]
    public void Component_added_during_run_joins_next_iteration()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsSpawnComp>();
            var spawn = EcsWorlds.System<EcsSpawnSystem>(world);
            spawn.Host = world;
            Assert.Equal(1, spawn.AwakeCount);

            world.Update(); // 父实体 Run 中创建子实体并挂同款组件

            Assert.True(spawn.Spawned);
            var runsAfterFirstTick = spawn.UpdateRunCount;
            Assert.Equal(1, runsAfterFirstTick);  // 本轮只跑了父实体
            Assert.Equal(2, spawn.EntityCount);   // 子实体 pendingJoin 已并入

            world.Update();
            Assert.Equal(2, spawn.UpdateRunCount - runsAfterFirstTick); // 第二轮父子都跑
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>系统在 Run 内摘除组件：本轮剩余迭代安全，membership 在迭代结束后移除。</summary>
    [Fact]
    public void Component_removed_during_run_leaves_after_iteration()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            e1.AddComponent<EcsFlagComp>();
            e1.AddComponent<EcsTempComp>();
            e2.AddComponent<EcsFlagComp>();
            e2.AddComponent<EcsTempComp>();

            var dual = EcsWorlds.System<EcsFlagTempUpdateSystem>(world);
            var flagOnly = EcsWorlds.System<EcsFlagUpdateSystem>(world);

            world.Update();

            // 本轮两个实体都完整跑完双组件系统（Run 内移除 Temp 不影响本轮）
            Assert.Equal(2, dual.RunCount);
            // 迭代结束后 pendingLeave 生效，双组件系统清空成员
            Assert.DoesNotContain(e1, dual.Entities);
            Assert.DoesNotContain(e2, dual.Entities);
            // 单组件系统不受影响，下一帧仍正常推进
            world.Update();
            Assert.Equal(4, flagOnly.RunCount);
            Assert.Equal(2, dual.RunCount); // 不再增长
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
