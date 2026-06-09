using YuoCore.Ecs;
using YuoCore.Game;

namespace YuoCore.Game.Tests;

public sealed class YuoIdleGameSystemTests
{
    [Fact]
    public void Idle_game_system_runs_in_plain_ecs_world_without_backend_host()
    {
        var world = new YuoWorld();
        world.OnInit();

        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<YuoIdlePlayerComponent>().PlayerId = Guid.NewGuid();
            var wallet = entity.AddComponent<YuoIdleWalletComponent>();
            var progress = entity.AddComponent<YuoIdleProgressComponent>();
            progress.LastTickAt = DateTimeOffset.UtcNow.AddSeconds(-2);

            world.Update();

            Assert.True(wallet.Gold > 0);
            Assert.True(wallet.Exp > 0);
            Assert.True(progress.UpdatedAt > progress.LastTickAt.AddSeconds(-3));
        }
        finally
        {
            world.OnDestroy();
        }
    }

    [Fact]
    public void Runtime_stats_system_runs_in_plain_ecs_world()
    {
        var world = new YuoWorld();
        world.OnInit();

        try
        {
            var stats = world.WorldMain.AddComponent<YuoRuntimeStatsComponent>();

            world.Update();
            world.Update();

            Assert.Equal(2, stats.TickCount);
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
