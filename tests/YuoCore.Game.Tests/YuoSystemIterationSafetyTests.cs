using YuoCore.Ecs;

namespace YuoCore.Game.Tests;

/// <summary>
/// 回归：系统 Run 中增删组件不再炸掉/污染正在迭代的 Entities 集合。
/// HashSet 迭代器对 Remove 容忍，但 Add 会 bump version 直接中断本轮剩余实体。
/// </summary>
public sealed class YuoSystemIterationSafetyTests
{
    [Fact]
    public void Adding_matching_entity_during_run_does_not_abort_iteration()
    {
        var world = new YuoWorld();
        world.OnInit();

        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            e1.AddComponent<IterationFlagComponent>();
            e2.AddComponent<IterationFlagComponent>();

            var system = world.GetAllSystem.OfType<SpawnMatchingEntityDuringRunSystem>().Single();
            system.World = world;

            world.Update();

            // e1 生成 e3 时 Entities.Add 被延迟并入，e2 不再被漏跑。
            Assert.Equal(2, system.RunCount);
            Assert.Equal(3, system.EntityCount);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Removing_component_during_run_defers_membership_change()
    {
        var world = new YuoWorld();
        world.OnInit();

        try
        {
            var e1 = new YuoEntity(world);
            var e2 = new YuoEntity(world);
            e1.AddComponent<IterationFlagComponent>();
            e1.AddComponent<IterationTempComponent>();
            e2.AddComponent<IterationFlagComponent>();
            e2.AddComponent<IterationTempComponent>();

            var system = world.GetAllSystem.OfType<RemoveComponentDuringRunSystem>().Single();

            world.Update();

            Assert.Equal(2, system.RunCount);
            Assert.DoesNotContain(e1, system.Entities);
            Assert.DoesNotContain(e2, system.Entities);

            world.Update();
            Assert.Equal(2, system.RunCount);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    public sealed class IterationFlagComponent : YuoComponent;

    public sealed class IterationTempComponent : YuoComponent;

    /// <summary>
    /// Run 中创建同样匹配本系统的新实体，触发 CheckValidEntity→Entities.Add 迭代期写入。
    /// </summary>
    public sealed class SpawnMatchingEntityDuringRunSystem : YuoSystem<IterationFlagComponent>, IUpdate
    {
        public int RunCount;
        public YuoWorld World = null!;
        private bool _spawned;

        public override void Run(IterationFlagComponent flag)
        {
            RunCount++;
            if (_spawned) return;
            _spawned = true;

            var child = new YuoEntity(World);
            child.AddComponent<IterationFlagComponent>();
        }
    }

    public sealed class RemoveComponentDuringRunSystem : YuoSystem<IterationFlagComponent, IterationTempComponent>, IUpdate
    {
        public int RunCount;

        public override void Run(IterationFlagComponent flag, IterationTempComponent temp)
        {
            RunCount++;
            temp.Entity.RemoveComponent(temp);
        }
    }
}
