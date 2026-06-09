using YuoCore.Actor;

namespace YuoCore.Actor.Tests;

public sealed class ActorDependencyTests
{
    [Fact]
    public void Actor_core_does_not_reference_ecs()
    {
        var referencedAssemblies = typeof(ActorRouter).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Actor.Core 只负责消息、路由、mailbox 和 location sender。
        // ECS Entity 入口放在 YuoCore.Actor.Ecs，避免 Unity/单机只用 Actor 时被迫引入 ECS。
        Assert.DoesNotContain("YuoCore.Ecs", referencedAssemblies);
    }
}
