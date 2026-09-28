using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// SetActiveEntities/ClearActiveEntities 契约：只收敛本次 tick 的系统遍历集，
/// 不改变任何组件的真实状态、生命周期或可见性语义。
/// </summary>
public sealed class YuoActiveEntitiesTests
{
    [Fact]
    public void Active_set_limits_iteration_to_listed_members()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            var e3 = new YuoEntity(world);
            var e4 = new YuoEntity(world);
            e1.AddComponent<EcsPosComp>();
            e2.AddComponent<EcsPosComp>();
            e3.AddComponent<EcsVelComp>();
            e4.AddComponent<EcsVelComp>(); // VelComp 成员，但不进活动集

            var pos = EcsWorlds.System<EcsPosUpdateSystem>(world);
            var vel = EcsWorlds.System<EcsVelUpdateSystem>(world);

            // e2 在前但 e1 先加入系统：活动数组按系统加入顺序而非传入顺序
            world.SetActiveEntities(new[] { e2, e1, e3, e2 });
            world.Update();

            Assert.Equal(new[] { e1.ID, e2.ID }, pos.Seen); // 去重 + 保序
            Assert.Equal(2, pos.RunCount);
            // e3 是 VelComp 成员且在活动集内 → 正常跑；e4 同为成员但未入活动集 → 跳过
            Assert.Equal(new[] { e3.ID }, vel.Seen);
            Assert.Equal(1, vel.RunCount);
            Assert.Equal(2, pos.EntityCount);               // 活动集不影响真实成员关系
            Assert.Equal(2, vel.EntityCount);
        }
        finally
        {
            world.ClearActiveEntities();
            world.OnDestroy();
        }
    }

    [Fact]
    public void Clear_restores_full_iteration()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            e1.AddComponent<EcsPosComp>();
            e2.AddComponent<EcsPosComp>();
            var pos = EcsWorlds.System<EcsPosUpdateSystem>(world);

            world.SetActiveEntities(new[] { e1 });
            world.Update();
            Assert.Equal(new[] { e1.ID }, pos.Seen);
            var runsBeforeClear = pos.RunCount;

            pos.Seen.Clear();
            world.ClearActiveEntities();
            world.Update();

            Assert.Equal(2, pos.RunCount - runsBeforeClear); // 本轮恢复为全量成员迭代
            Assert.Equal(new[] { e1.ID, e2.ID }, pos.Seen);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Empty_active_set_runs_nothing_and_null_argument_throws()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsPosComp>();
            var pos = EcsWorlds.System<EcsPosUpdateSystem>(world);

            world.SetActiveEntities(Array.Empty<YuoEntity>());
            world.Update();
            Assert.Equal(0, pos.RunCount); // 空集是合法收敛：什么都不跑

            Assert.Throws<ArgumentNullException>(() => world.SetActiveEntities(null!));
        }
        finally
        {
            world.ClearActiveEntities();
            world.OnDestroy();
        }
    }

    /// <summary>活动集中的实体被销毁（索引未复用）：迭代安全跳过，不产生访问错误。</summary>
    [Fact]
    public void Destroyed_member_in_active_set_is_skipped()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            e1.AddComponent<EcsPosComp>();
            e2.AddComponent<EcsPosComp>();
            var pos = EcsWorlds.System<EcsPosUpdateSystem>(world);

            world.SetActiveEntities(new[] { e1, e2 });
            e1.Dispose(); // 立即销毁，索引池回收但此刻无新实体复用

            world.Update();
            Assert.Equal(new[] { e2.ID }, pos.Seen);
            Assert.Equal(1, pos.RunCount);
        }
        finally
        {
            world.ClearActiveEntities();
            world.OnDestroy();
        }
    }

    /// <summary>
    /// BUG: 活动数组持有实体句柄但在迭代时按 EntityIndex 直查组件存储——
    /// 活动实体销毁且索引被新实体复用后，stale 句柄命中新实体的组件槽位，
    /// 系统对"不在活动集内"的组件执行了 Run（且 RunEntity 拿到的是 e3 的组件）。
    /// 期望语义：活动集中的死亡句柄应被跳过，不得把复用索引上的新组件当成旧实体的。
    /// </summary>
    [Fact]
    public void Reused_index_in_active_set_must_not_leak_new_entity_component()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            e1.AddComponent<EcsPosComp>();
            var pos = EcsWorlds.System<EcsPosUpdateSystem>(world);

            world.SetActiveEntities(new[] { e1 });
            var index = e1.EntityIndex;
            e1.Dispose();

            var e3 = new YuoEntity(world);
            Assert.Equal(index, e3.EntityIndex); // 索引被复用
            var c3 = e3.AddComponent<EcsPosComp>();
            c3.X = 77;

            world.Update();

            // BUG: 当前实现会对 stale 句柄做 store.Get(e1.EntityIndex)，命中 c3 并以 e1 名义派发。
            // 活动集是纯提示契约：e3 从未被加入活动集，其组件不应在本轮被推进。
            Assert.Empty(pos.Seen);
            Assert.Equal(0, pos.RunCount);
        }
        finally
        {
            world.ClearActiveEntities();
            world.OnDestroy();
        }
    }

    /// <summary>
    /// BUG: 缓冲销毁回放 + 索引复用 + 残留活动集 的组合同样泄漏——
    /// 死亡句柄在下一帧迭代时按复用后的索引命中新实体组件。
    /// </summary>
    [Fact]
    public void Buffered_destroy_followed_by_reuse_in_active_set()
    {
        var world = EcsWorlds.Create();
        try
        {
            var e1 = new YuoEntity(world);
            e1.AddComponent<EcsPosComp>();
            var pos = EcsWorlds.System<EcsPosUpdateSystem>(world);

            world.SetActiveEntities(new[] { e1 });
            e1.Destroy();          // 缓冲销毁
            world.Update();        // 帧首回放 e1.Dispose → 索引回收，本轮 stale 句柄安全跳过
            Assert.Equal(0, pos.RunCount);

            var e3 = new YuoEntity(world);
            Assert.Equal(e1.EntityIndex, e3.EntityIndex); // 复用同一索引
            e3.AddComponent<EcsPosComp>();

            world.Update();        // 活动集未重设：stale e1 句柄命中 e3 的组件槽位

            // BUG: e3 从未加入活动集，但其组件被当成 e1 的推进了
            Assert.Empty(pos.Seen);
            Assert.Equal(0, pos.RunCount);
        }
        finally
        {
            world.ClearActiveEntities();
            world.OnDestroy();
        }
    }

    /// <summary>活动集内出现非成员/其他来源实体：被系统侧过滤，不报错。</summary>
    [Fact]
    public void Non_member_entries_in_active_set_are_filtered()
    {
        var world = EcsWorlds.Create();
        var other = EcsWorlds.Create(setAsDefault: false);
        try
        {
            var member = new YuoEntity(world);
            member.AddComponent<EcsPosComp>();
            var outsider = new YuoEntity(world);          // 没有 PosComp
            var foreign = new YuoEntity(other);           // 其他世界的实体
            foreign.AddComponent<EcsPosComp>();
            var pos = EcsWorlds.System<EcsPosUpdateSystem>(world);

            world.SetActiveEntities(new YuoEntity?[] { null!, outsider, foreign, member }!);
            world.Update();

            Assert.Equal(new[] { member.ID }, pos.Seen);
            Assert.Equal(1, pos.RunCount);
        }
        finally
        {
            world.ClearActiveEntities();
            other.OnDestroy();
            world.OnDestroy();
        }
    }
}
