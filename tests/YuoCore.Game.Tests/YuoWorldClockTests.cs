using YuoCore.Ecs;
using YuoCore.Ecs.Helpers;
using YuoCore.Game;

namespace YuoCore.Game.Tests;

/// <summary>
/// 回归：世界时钟可注入并由 Update 驱动，挂机系统按世界时钟结算而非墙钟。
/// </summary>
public sealed class YuoWorldClockTests
{
    [Fact]
    public void Injected_deterministic_clock_drives_frame_time_and_idle_accrual()
    {
        var world = new YuoWorld();
        var clock = new FixedStepTimeInfo { StepMs = 2000 };
        world.Time = clock;
        world.OnInit();

        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<YuoIdlePlayerComponent>().PlayerId = Guid.NewGuid();
            var wallet = entity.AddComponent<YuoIdleWalletComponent>();
            var progress = entity.AddComponent<YuoIdleProgressComponent>();
            progress.LastTickAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.FrameTime);

            var start = clock.FrameTime;
            world.Update();

            Assert.Equal(start + 2000, clock.FrameTime);
            // 固定步长 2s：Gold = 10 * 1 * 2 = 20，与墙钟无关
            Assert.Equal(20, wallet.Gold);

            world.Update();
            Assert.Equal(start + 4000, clock.FrameTime);
            Assert.Equal(40, wallet.Gold);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    private sealed class FixedStepTimeInfo : TimeInfo
    {
        public long StepMs;

        public override void Update()
        {
            FrameTime += StepMs;
        }
    }
}
