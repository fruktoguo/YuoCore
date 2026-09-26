using YuoCore.Ecs;

namespace YuoCore.Game.Tests;

public sealed class YuoCoreTraceCounterTests
{
    private static readonly AsyncLocal<bool> Recording = new();

    [Fact]
    public void Entity_lookup_and_system_iteration_report_distinct_counts()
    {
        var world = new YuoWorld();
        world.OnInit();
        var previousBegin = YuoCoreTrace.BeginHandler;
        var previousEnd = YuoCoreTrace.EndHandler;
        var previousCount = YuoCoreTrace.CountHandler;
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);

        try
        {
            var first = new YuoEntity(world);
            var second = new YuoEntity(world);
            first.AddComponent<TraceCountComponent>();
            second.AddComponent<TraceCountComponent>();
            second.AddComponent<TraceBlockComponent>();

            YuoCoreTrace.Configure(null!, null!, (name, count) =>
            {
                if (!Recording.Value) return;
                counts[name] = counts.GetValueOrDefault(name) + count;
            });
            Recording.Value = true;

            Assert.NotNull(first.GetComponent<TraceCountComponent>());
            Assert.NotNull(first.GetComponent(typeof(TraceCountComponent)));
            Assert.Equal(2, counts.GetValueOrDefault("yuocore.entity.get_component.count"));
            Assert.Equal(2, counts.GetValueOrDefault("yuocore.store.get.count"));

            counts.Clear();
            world.Update();

            var system = world.GetAllSystem.OfType<TraceCountSystem>().Single();
            Assert.Equal(1, system.RunCount);
            Assert.Equal(2, counts.GetValueOrDefault($"yuocore.system.entity_scan.{system.SystemName}"));
            Assert.Equal(1, counts.GetValueOrDefault($"yuocore.system.cover_skip.{system.SystemName}"));
            Assert.Equal(1, counts.GetValueOrDefault($"yuocore.system.entity_run.{system.SystemName}.count"));
        }
        finally
        {
            Recording.Value = false;
            YuoCoreTrace.Configure(previousBegin, previousEnd, previousCount);
            world.OnDestroy();
        }
    }

    public sealed class TraceCountComponent : YuoComponent;

    public sealed class TraceBlockComponent : YuoComponent;

    [CoverSystem(typeof(IUpdate), typeof(TraceBlockComponent))]
    public sealed class TraceCountSystem : YuoSystem<TraceCountComponent>, IUpdate
    {
        public int RunCount;

        public override void Run(TraceCountComponent component) => RunCount++;
    }
}
