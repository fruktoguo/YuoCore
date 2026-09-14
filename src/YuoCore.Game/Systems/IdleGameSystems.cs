using YuoCore.Ecs;

namespace YuoCore.Game;

/// <summary>
/// 通用挂机成长系统。
/// 它只依赖 ECS 组件，可以在单机、Unity 模拟环境或服务端 GameShard 中复用。
/// </summary>
public sealed class YuoIdleTickSystem : YuoSystem<YuoIdlePlayerComponent, YuoIdleWalletComponent, YuoIdleProgressComponent>, IUpdate
{
    public override void Run(YuoIdlePlayerComponent player, YuoIdleWalletComponent wallet, YuoIdleProgressComponent progress)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(progress.Entity.World.Time.FrameTime);
        var elapsed = Math.Clamp((now - progress.LastTickAt).TotalSeconds, 0, 5);
        if (elapsed <= 0) return;

        var levelScale = Math.Max(1, progress.Level);
        wallet.Gold += (long)Math.Floor(10 * levelScale * elapsed);
        wallet.Exp += (long)Math.Floor(5 * levelScale * elapsed);

        while (wallet.Exp >= ExpToNextLevel(progress.Level))
        {
            wallet.Exp -= ExpToNextLevel(progress.Level);
            progress.Level++;
            progress.Power += 2 + progress.Level;
        }

        progress.LastTickAt = now;
        progress.UpdatedAt = now;
    }

    private static long ExpToNextLevel(int level) => 100 + level * 25L;
}

public sealed class YuoRuntimeStatsSystem : YuoSystem<YuoRuntimeStatsComponent>, IUpdate
{
    public override void Run(YuoRuntimeStatsComponent component)
    {
        component.TickCount++;
    }
}
