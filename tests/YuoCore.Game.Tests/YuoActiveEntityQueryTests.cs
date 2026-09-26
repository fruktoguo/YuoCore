using YuoCore.Ecs;

namespace YuoCore.Game.Tests;

/// <summary>验证活动实体查询只推进指定实体，并保持 System 的加入顺序。</summary>
public sealed class YuoActiveEntityQueryTests
{
    [Fact]
    public void Active_query_reduces_candidates_and_preserves_system_order()
    {
        var world = new YuoWorld();
        world.OnInit();

        try
        {
            var first = new YuoEntity(world);
            var second = new YuoEntity(world);
            var third = new YuoEntity(world);
            first.AddComponent<ActiveQueryComponent>();
            second.AddComponent<ActiveQueryComponent>();
            third.AddComponent<ActiveQueryComponent>();

            var system = world.GetAllSystem.OfType<ActiveQuerySystem>().Single();
            world.SetActiveEntities(new[] { third, first, third });
            world.Update();

            Assert.Equal(new[] { first.ID, third.ID }, system.SeenIds);
            Assert.Equal(2, system.RunCount);
            Assert.Equal(3, system.EntityCount);
        }
        finally
        {
            world.ClearActiveEntities();
            world.OnDestroy();
        }
    }

    public sealed class ActiveQueryComponent : YuoComponent;

    public sealed class ActiveQuerySystem : YuoSystem<ActiveQueryComponent>, IUpdate
    {
        public int RunCount;
        public List<long> SeenIds { get; } = new();

        public override void Run(ActiveQueryComponent component)
        {
            RunCount++;
            SeenIds.Add(component.Entity.ID);
        }
    }
}
