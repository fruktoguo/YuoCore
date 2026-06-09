extern alias OptimizedEcs;

using BenchmarkDotNet.Attributes;
using OptimizedYuo = OptimizedEcs::YuoCore.Ecs;

[MemoryDiagnoser]
public class EcsShapeOptimizedBenchmarks
{
    private ShapeOptimizedWorkload.ShapeRuntime _runtime = null!;
    [Params(10_000)]
    public int Entities { get; set; }
    [GlobalSetup(Target = nameof(ShapeTick1))]
    public void SetupTick1()
    {
        _runtime = new ShapeOptimizedWorkload.ShapeRuntime();
        _runtime.Start();
        _runtime.SeedEntities(Entities);
        _runtime.Tick();
    }
    [GlobalCleanup(Target = nameof(ShapeTick1))]
    public void CleanupTick1() => _runtime.Dispose();
    [Benchmark]
    public void ShapeTick1() => _runtime.Tick();
    [Benchmark]
    public void ShapeCreateAndStart()
    {
        using var runtime = new ShapeOptimizedWorkload.ShapeRuntime();
        runtime.Start();
        runtime.SeedEntities(Entities);
        runtime.Tick();
    }
}

namespace ShapeOptimizedWorkload
{
    public abstract class ShapeBaseBenchComponent : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent0 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent1 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent2 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent3 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent4 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent5 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent6 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent7 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent8 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent9 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent10 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent11 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent12 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent13 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent14 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent15 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent16 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent17 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent18 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent19 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent20 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent21 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent22 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent23 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent24 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent25 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent26 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent27 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent28 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent29 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent30 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent31 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent32 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent33 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent34 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent35 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent36 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent37 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent38 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent39 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent40 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent41 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent42 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent43 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent44 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent45 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent46 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent47 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent48 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent49 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent50 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent51 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent52 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent53 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent54 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent55 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent56 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent57 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent58 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent59 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent60 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent61 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent62 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent63 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent64 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent65 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent66 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent67 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent68 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent69 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent70 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent71 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent72 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent73 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent74 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent75 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent76 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent77 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent78 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent79 : OptimizedYuo.YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent80 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent81 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent82 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent83 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent84 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent85 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent86 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent87 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent88 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent89 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent90 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent91 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent92 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent93 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent94 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent95 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent96 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent97 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent98 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeComponent99 : ShapeBaseBenchComponent
    {
    }
    public sealed class ShapeSingleSystem0 : OptimizedYuo.YuoSystem<ShapeComponent0>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent0 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem1 : OptimizedYuo.YuoSystem<ShapeComponent1>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent1 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem2 : OptimizedYuo.YuoSystem<ShapeComponent2>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent2 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem3 : OptimizedYuo.YuoSystem<ShapeComponent3>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent3 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem4 : OptimizedYuo.YuoSystem<ShapeComponent4>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent4 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem5 : OptimizedYuo.YuoSystem<ShapeComponent5>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent5 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem6 : OptimizedYuo.YuoSystem<ShapeComponent6>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent6 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem7 : OptimizedYuo.YuoSystem<ShapeComponent7>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent7 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem8 : OptimizedYuo.YuoSystem<ShapeComponent8>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent8 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem9 : OptimizedYuo.YuoSystem<ShapeComponent9>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent9 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem10 : OptimizedYuo.YuoSystem<ShapeComponent10>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent10 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem11 : OptimizedYuo.YuoSystem<ShapeComponent11>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent11 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem12 : OptimizedYuo.YuoSystem<ShapeComponent12>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent12 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem13 : OptimizedYuo.YuoSystem<ShapeComponent13>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent13 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem14 : OptimizedYuo.YuoSystem<ShapeComponent14>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent14 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem15 : OptimizedYuo.YuoSystem<ShapeComponent15>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent15 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem16 : OptimizedYuo.YuoSystem<ShapeComponent16>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent16 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem17 : OptimizedYuo.YuoSystem<ShapeComponent17>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent17 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem18 : OptimizedYuo.YuoSystem<ShapeComponent18>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent18 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem19 : OptimizedYuo.YuoSystem<ShapeComponent19>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent19 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem20 : OptimizedYuo.YuoSystem<ShapeComponent20>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent20 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem21 : OptimizedYuo.YuoSystem<ShapeComponent21>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent21 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem22 : OptimizedYuo.YuoSystem<ShapeComponent22>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent22 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem23 : OptimizedYuo.YuoSystem<ShapeComponent23>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent23 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem24 : OptimizedYuo.YuoSystem<ShapeComponent24>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent24 c1) => c1.Value++;
    }
    public sealed class ShapeDoubleSystem25 : OptimizedYuo.YuoSystem<ShapeComponent0, ShapeComponent1>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent0 c1, ShapeComponent1 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem26 : OptimizedYuo.YuoSystem<ShapeComponent2, ShapeComponent3>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent2 c1, ShapeComponent3 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem27 : OptimizedYuo.YuoSystem<ShapeComponent4, ShapeComponent5>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent4 c1, ShapeComponent5 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem28 : OptimizedYuo.YuoSystem<ShapeComponent6, ShapeComponent7>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent6 c1, ShapeComponent7 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem29 : OptimizedYuo.YuoSystem<ShapeComponent8, ShapeComponent9>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent8 c1, ShapeComponent9 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem30 : OptimizedYuo.YuoSystem<ShapeComponent10, ShapeComponent11>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent10 c1, ShapeComponent11 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem31 : OptimizedYuo.YuoSystem<ShapeComponent12, ShapeComponent13>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent12 c1, ShapeComponent13 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem32 : OptimizedYuo.YuoSystem<ShapeComponent14, ShapeComponent15>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent14 c1, ShapeComponent15 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem33 : OptimizedYuo.YuoSystem<ShapeComponent16, ShapeComponent17>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent16 c1, ShapeComponent17 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem34 : OptimizedYuo.YuoSystem<ShapeComponent18, ShapeComponent19>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent18 c1, ShapeComponent19 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem35 : OptimizedYuo.YuoSystem<ShapeComponent20, ShapeComponent21>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent20 c1, ShapeComponent21 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem36 : OptimizedYuo.YuoSystem<ShapeComponent22, ShapeComponent23>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent22 c1, ShapeComponent23 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem37 : OptimizedYuo.YuoSystem<ShapeComponent24, ShapeComponent25>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent24 c1, ShapeComponent25 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem38 : OptimizedYuo.YuoSystem<ShapeComponent26, ShapeComponent27>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent26 c1, ShapeComponent27 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem39 : OptimizedYuo.YuoSystem<ShapeComponent28, ShapeComponent29>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent28 c1, ShapeComponent29 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem40 : OptimizedYuo.YuoSystem<ShapeComponent30, ShapeComponent31>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent30 c1, ShapeComponent31 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem41 : OptimizedYuo.YuoSystem<ShapeComponent32, ShapeComponent33>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent32 c1, ShapeComponent33 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem42 : OptimizedYuo.YuoSystem<ShapeComponent34, ShapeComponent35>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent34 c1, ShapeComponent35 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem43 : OptimizedYuo.YuoSystem<ShapeComponent36, ShapeComponent37>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent36 c1, ShapeComponent37 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem44 : OptimizedYuo.YuoSystem<ShapeComponent38, ShapeComponent39>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent38 c1, ShapeComponent39 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem45 : OptimizedYuo.YuoSystem<ShapeComponent40, ShapeComponent41>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent40 c1, ShapeComponent41 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem46 : OptimizedYuo.YuoSystem<ShapeComponent42, ShapeComponent43>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent42 c1, ShapeComponent43 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem47 : OptimizedYuo.YuoSystem<ShapeComponent44, ShapeComponent45>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent44 c1, ShapeComponent45 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem48 : OptimizedYuo.YuoSystem<ShapeComponent46, ShapeComponent47>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent46 c1, ShapeComponent47 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem49 : OptimizedYuo.YuoSystem<ShapeComponent48, ShapeComponent49>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent48 c1, ShapeComponent49 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeTripleSystem50 : OptimizedYuo.YuoSystem<ShapeComponent0, ShapeComponent1, ShapeComponent2>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent0 c1, ShapeComponent1 c2, ShapeComponent2 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem51 : OptimizedYuo.YuoSystem<ShapeComponent3, ShapeComponent4, ShapeComponent5>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent3 c1, ShapeComponent4 c2, ShapeComponent5 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem52 : OptimizedYuo.YuoSystem<ShapeComponent6, ShapeComponent7, ShapeComponent8>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent6 c1, ShapeComponent7 c2, ShapeComponent8 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem53 : OptimizedYuo.YuoSystem<ShapeComponent9, ShapeComponent10, ShapeComponent11>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent9 c1, ShapeComponent10 c2, ShapeComponent11 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem54 : OptimizedYuo.YuoSystem<ShapeComponent12, ShapeComponent13, ShapeComponent14>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent12 c1, ShapeComponent13 c2, ShapeComponent14 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem55 : OptimizedYuo.YuoSystem<ShapeComponent15, ShapeComponent16, ShapeComponent17>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent15 c1, ShapeComponent16 c2, ShapeComponent17 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem56 : OptimizedYuo.YuoSystem<ShapeComponent18, ShapeComponent19, ShapeComponent20>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent18 c1, ShapeComponent19 c2, ShapeComponent20 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem57 : OptimizedYuo.YuoSystem<ShapeComponent21, ShapeComponent22, ShapeComponent23>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent21 c1, ShapeComponent22 c2, ShapeComponent23 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem58 : OptimizedYuo.YuoSystem<ShapeComponent24, ShapeComponent25, ShapeComponent26>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent24 c1, ShapeComponent25 c2, ShapeComponent26 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem59 : OptimizedYuo.YuoSystem<ShapeComponent27, ShapeComponent28, ShapeComponent29>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent27 c1, ShapeComponent28 c2, ShapeComponent29 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem60 : OptimizedYuo.YuoSystem<ShapeComponent30, ShapeComponent31, ShapeComponent32>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent30 c1, ShapeComponent31 c2, ShapeComponent32 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem61 : OptimizedYuo.YuoSystem<ShapeComponent33, ShapeComponent34, ShapeComponent35>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent33 c1, ShapeComponent34 c2, ShapeComponent35 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem62 : OptimizedYuo.YuoSystem<ShapeComponent36, ShapeComponent37, ShapeComponent38>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent36 c1, ShapeComponent37 c2, ShapeComponent38 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem63 : OptimizedYuo.YuoSystem<ShapeComponent39, ShapeComponent40, ShapeComponent41>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent39 c1, ShapeComponent40 c2, ShapeComponent41 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem64 : OptimizedYuo.YuoSystem<ShapeComponent42, ShapeComponent43, ShapeComponent44>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent42 c1, ShapeComponent43 c2, ShapeComponent44 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem65 : OptimizedYuo.YuoSystem<ShapeComponent45, ShapeComponent46, ShapeComponent47>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent45 c1, ShapeComponent46 c2, ShapeComponent47 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem66 : OptimizedYuo.YuoSystem<ShapeComponent48, ShapeComponent49, ShapeComponent50>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent48 c1, ShapeComponent49 c2, ShapeComponent50 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem67 : OptimizedYuo.YuoSystem<ShapeComponent51, ShapeComponent52, ShapeComponent53>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent51 c1, ShapeComponent52 c2, ShapeComponent53 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem68 : OptimizedYuo.YuoSystem<ShapeComponent54, ShapeComponent55, ShapeComponent56>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent54 c1, ShapeComponent55 c2, ShapeComponent56 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem69 : OptimizedYuo.YuoSystem<ShapeComponent57, ShapeComponent58, ShapeComponent59>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent57 c1, ShapeComponent58 c2, ShapeComponent59 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeQuadSystem70 : OptimizedYuo.YuoSystem<ShapeComponent0, ShapeComponent1, ShapeComponent2, ShapeComponent3>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent0 c1, ShapeComponent1 c2, ShapeComponent2 c3, ShapeComponent3 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem71 : OptimizedYuo.YuoSystem<ShapeComponent4, ShapeComponent5, ShapeComponent6, ShapeComponent7>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent4 c1, ShapeComponent5 c2, ShapeComponent6 c3, ShapeComponent7 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem72 : OptimizedYuo.YuoSystem<ShapeComponent8, ShapeComponent9, ShapeComponent10, ShapeComponent11>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent8 c1, ShapeComponent9 c2, ShapeComponent10 c3, ShapeComponent11 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem73 : OptimizedYuo.YuoSystem<ShapeComponent12, ShapeComponent13, ShapeComponent14, ShapeComponent15>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent12 c1, ShapeComponent13 c2, ShapeComponent14 c3, ShapeComponent15 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem74 : OptimizedYuo.YuoSystem<ShapeComponent16, ShapeComponent17, ShapeComponent18, ShapeComponent19>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent16 c1, ShapeComponent17 c2, ShapeComponent18 c3, ShapeComponent19 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem75 : OptimizedYuo.YuoSystem<ShapeComponent20, ShapeComponent21, ShapeComponent22, ShapeComponent23>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent20 c1, ShapeComponent21 c2, ShapeComponent22 c3, ShapeComponent23 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem76 : OptimizedYuo.YuoSystem<ShapeComponent24, ShapeComponent25, ShapeComponent26, ShapeComponent27>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent24 c1, ShapeComponent25 c2, ShapeComponent26 c3, ShapeComponent27 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem77 : OptimizedYuo.YuoSystem<ShapeComponent28, ShapeComponent29, ShapeComponent30, ShapeComponent31>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent28 c1, ShapeComponent29 c2, ShapeComponent30 c3, ShapeComponent31 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem78 : OptimizedYuo.YuoSystem<ShapeComponent32, ShapeComponent33, ShapeComponent34, ShapeComponent35>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent32 c1, ShapeComponent33 c2, ShapeComponent34 c3, ShapeComponent35 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem79 : OptimizedYuo.YuoSystem<ShapeComponent36, ShapeComponent37, ShapeComponent38, ShapeComponent39>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent36 c1, ShapeComponent37 c2, ShapeComponent38 c3, ShapeComponent39 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem80 : OptimizedYuo.YuoSystem<ShapeComponent40, ShapeComponent41, ShapeComponent42, ShapeComponent43>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent40 c1, ShapeComponent41 c2, ShapeComponent42 c3, ShapeComponent43 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem81 : OptimizedYuo.YuoSystem<ShapeComponent44, ShapeComponent45, ShapeComponent46, ShapeComponent47>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent44 c1, ShapeComponent45 c2, ShapeComponent46 c3, ShapeComponent47 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem82 : OptimizedYuo.YuoSystem<ShapeComponent48, ShapeComponent49, ShapeComponent50, ShapeComponent51>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent48 c1, ShapeComponent49 c2, ShapeComponent50 c3, ShapeComponent51 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem83 : OptimizedYuo.YuoSystem<ShapeComponent52, ShapeComponent53, ShapeComponent54, ShapeComponent55>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent52 c1, ShapeComponent53 c2, ShapeComponent54 c3, ShapeComponent55 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem84 : OptimizedYuo.YuoSystem<ShapeComponent56, ShapeComponent57, ShapeComponent58, ShapeComponent59>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeComponent56 c1, ShapeComponent57 c2, ShapeComponent58 c3, ShapeComponent59 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeBaseSystem85 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem86 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem87 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem88 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem89 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem90 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem91 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem92 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem93 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem94 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseDoubleSystem95 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent0>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent0 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem96 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent1>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent1 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem97 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent2>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent2 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem98 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent3>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent3 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem99 : OptimizedYuo.YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent4>, OptimizedYuo.IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent4 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeRuntime : IDisposable
    {
        private readonly List<OptimizedYuo.YuoEntity> _entities = new();
        private readonly OptimizedYuo.YuoWorld _world = new();
        private bool _started;
        public void Start()
        {
            if (_started) return;
            _world.OnInit();
            _started = true;
        }
        public void SeedEntities(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var entity = new OptimizedYuo.YuoEntity(i + 20_000_000);
                AddAllComponents(entity);
                _entities.Add(entity);
            }
        }
        private static void AddAllComponents(OptimizedYuo.YuoEntity entity)
        {
            entity.AddComponent<ShapeComponent0>();
            entity.AddComponent<ShapeComponent1>();
            entity.AddComponent<ShapeComponent2>();
            entity.AddComponent<ShapeComponent3>();
            entity.AddComponent<ShapeComponent4>();
            entity.AddComponent<ShapeComponent5>();
            entity.AddComponent<ShapeComponent6>();
            entity.AddComponent<ShapeComponent7>();
            entity.AddComponent<ShapeComponent8>();
            entity.AddComponent<ShapeComponent9>();
            entity.AddComponent<ShapeComponent10>();
            entity.AddComponent<ShapeComponent11>();
            entity.AddComponent<ShapeComponent12>();
            entity.AddComponent<ShapeComponent13>();
            entity.AddComponent<ShapeComponent14>();
            entity.AddComponent<ShapeComponent15>();
            entity.AddComponent<ShapeComponent16>();
            entity.AddComponent<ShapeComponent17>();
            entity.AddComponent<ShapeComponent18>();
            entity.AddComponent<ShapeComponent19>();
            entity.AddComponent<ShapeComponent20>();
            entity.AddComponent<ShapeComponent21>();
            entity.AddComponent<ShapeComponent22>();
            entity.AddComponent<ShapeComponent23>();
            entity.AddComponent<ShapeComponent24>();
            entity.AddComponent<ShapeComponent25>();
            entity.AddComponent<ShapeComponent26>();
            entity.AddComponent<ShapeComponent27>();
            entity.AddComponent<ShapeComponent28>();
            entity.AddComponent<ShapeComponent29>();
            entity.AddComponent<ShapeComponent30>();
            entity.AddComponent<ShapeComponent31>();
            entity.AddComponent<ShapeComponent32>();
            entity.AddComponent<ShapeComponent33>();
            entity.AddComponent<ShapeComponent34>();
            entity.AddComponent<ShapeComponent35>();
            entity.AddComponent<ShapeComponent36>();
            entity.AddComponent<ShapeComponent37>();
            entity.AddComponent<ShapeComponent38>();
            entity.AddComponent<ShapeComponent39>();
            entity.AddComponent<ShapeComponent40>();
            entity.AddComponent<ShapeComponent41>();
            entity.AddComponent<ShapeComponent42>();
            entity.AddComponent<ShapeComponent43>();
            entity.AddComponent<ShapeComponent44>();
            entity.AddComponent<ShapeComponent45>();
            entity.AddComponent<ShapeComponent46>();
            entity.AddComponent<ShapeComponent47>();
            entity.AddComponent<ShapeComponent48>();
            entity.AddComponent<ShapeComponent49>();
            entity.AddComponent<ShapeComponent50>();
            entity.AddComponent<ShapeComponent51>();
            entity.AddComponent<ShapeComponent52>();
            entity.AddComponent<ShapeComponent53>();
            entity.AddComponent<ShapeComponent54>();
            entity.AddComponent<ShapeComponent55>();
            entity.AddComponent<ShapeComponent56>();
            entity.AddComponent<ShapeComponent57>();
            entity.AddComponent<ShapeComponent58>();
            entity.AddComponent<ShapeComponent59>();
            entity.AddComponent<ShapeComponent60>();
            entity.AddComponent<ShapeComponent61>();
            entity.AddComponent<ShapeComponent62>();
            entity.AddComponent<ShapeComponent63>();
            entity.AddComponent<ShapeComponent64>();
            entity.AddComponent<ShapeComponent65>();
            entity.AddComponent<ShapeComponent66>();
            entity.AddComponent<ShapeComponent67>();
            entity.AddComponent<ShapeComponent68>();
            entity.AddComponent<ShapeComponent69>();
            entity.AddComponent<ShapeComponent70>();
            entity.AddComponent<ShapeComponent71>();
            entity.AddComponent<ShapeComponent72>();
            entity.AddComponent<ShapeComponent73>();
            entity.AddComponent<ShapeComponent74>();
            entity.AddComponent<ShapeComponent75>();
            entity.AddComponent<ShapeComponent76>();
            entity.AddComponent<ShapeComponent77>();
            entity.AddComponent<ShapeComponent78>();
            entity.AddComponent<ShapeComponent79>();
            entity.AddComponent<ShapeComponent80>();
            entity.AddComponent<ShapeComponent81>();
            entity.AddComponent<ShapeComponent82>();
            entity.AddComponent<ShapeComponent83>();
            entity.AddComponent<ShapeComponent84>();
            entity.AddComponent<ShapeComponent85>();
            entity.AddComponent<ShapeComponent86>();
            entity.AddComponent<ShapeComponent87>();
            entity.AddComponent<ShapeComponent88>();
            entity.AddComponent<ShapeComponent89>();
            entity.AddComponent<ShapeComponent90>();
            entity.AddComponent<ShapeComponent91>();
            entity.AddComponent<ShapeComponent92>();
            entity.AddComponent<ShapeComponent93>();
            entity.AddComponent<ShapeComponent94>();
            entity.AddComponent<ShapeComponent95>();
            entity.AddComponent<ShapeComponent96>();
            entity.AddComponent<ShapeComponent97>();
            entity.AddComponent<ShapeComponent98>();
            entity.AddComponent<ShapeComponent99>();
        }
        public void Tick() => _world.Update();
        public void Dispose()
        {
            if (!_started) return;
            _world.OnQuit();
            _world.OnDestroy();
            _entities.Clear();
            _started = false;
        }
    }
}
