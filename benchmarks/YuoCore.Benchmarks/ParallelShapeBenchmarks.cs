using BenchmarkDotNet.Attributes;
using YuoCore.Ecs;
using YuoCore.Runtime;

namespace YuoCore.Benchmarks
{
    [MemoryDiagnoser]
    public class ParallelShapeBenchmarks
    {
        private ParallelShapeWorkload.ParallelShapeRuntime _runtime = null!;

        [Params(10_000)]
        public int Entities { get; set; }

        [Params(4)]
        public int Shards { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _runtime = new ParallelShapeWorkload.ParallelShapeRuntime(Shards);
            _runtime.Start();
            _runtime.SeedEntities(Entities);
            _runtime.Tick();
        }

        [GlobalCleanup]
        public void Cleanup() => _runtime.Dispose();

        [Benchmark]
        public void ParallelShapeTick1() => _runtime.Tick();

        [Benchmark]
        public void ParallelShapeTickBatch100()
        {
            for (var i = 0; i < 100; i++)
            {
                _runtime.Tick();
            }
        }
    }
}

namespace ParallelShapeWorkload
{
    // 继承型组件用于覆盖 YuoSystemOfBase，验证你的 ECS BaseComponent 设计在分片并行下仍然工作。
    public abstract class ShapeBaseBenchComponent : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent0 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent1 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent2 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent3 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent4 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent5 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent6 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent7 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent8 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent9 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent10 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent11 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent12 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent13 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent14 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent15 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent16 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent17 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent18 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent19 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent20 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent21 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent22 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent23 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent24 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent25 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent26 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent27 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent28 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent29 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent30 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent31 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent32 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent33 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent34 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent35 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent36 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent37 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent38 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent39 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent40 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent41 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent42 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent43 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent44 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent45 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent46 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent47 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent48 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent49 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent50 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent51 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent52 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent53 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent54 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent55 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent56 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent57 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent58 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent59 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent60 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent61 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent62 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent63 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent64 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent65 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent66 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent67 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent68 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent69 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent70 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent71 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent72 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent73 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent74 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent75 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent76 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent77 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent78 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent79 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent80 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent81 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent82 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent83 : YuoComponent
    {
        public long Value;
    }
    public sealed class ShapeComponent84 : YuoComponent
    {
        public long Value;
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
    public sealed class ShapeSingleSystem0 : YuoSystem<ShapeComponent0>, IUpdate
    {
        public override void Run(ShapeComponent0 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem1 : YuoSystem<ShapeComponent1>, IUpdate
    {
        public override void Run(ShapeComponent1 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem2 : YuoSystem<ShapeComponent2>, IUpdate
    {
        public override void Run(ShapeComponent2 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem3 : YuoSystem<ShapeComponent3>, IUpdate
    {
        public override void Run(ShapeComponent3 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem4 : YuoSystem<ShapeComponent4>, IUpdate
    {
        public override void Run(ShapeComponent4 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem5 : YuoSystem<ShapeComponent5>, IUpdate
    {
        public override void Run(ShapeComponent5 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem6 : YuoSystem<ShapeComponent6>, IUpdate
    {
        public override void Run(ShapeComponent6 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem7 : YuoSystem<ShapeComponent7>, IUpdate
    {
        public override void Run(ShapeComponent7 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem8 : YuoSystem<ShapeComponent8>, IUpdate
    {
        public override void Run(ShapeComponent8 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem9 : YuoSystem<ShapeComponent9>, IUpdate
    {
        public override void Run(ShapeComponent9 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem10 : YuoSystem<ShapeComponent10>, IUpdate
    {
        public override void Run(ShapeComponent10 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem11 : YuoSystem<ShapeComponent11>, IUpdate
    {
        public override void Run(ShapeComponent11 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem12 : YuoSystem<ShapeComponent12>, IUpdate
    {
        public override void Run(ShapeComponent12 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem13 : YuoSystem<ShapeComponent13>, IUpdate
    {
        public override void Run(ShapeComponent13 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem14 : YuoSystem<ShapeComponent14>, IUpdate
    {
        public override void Run(ShapeComponent14 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem15 : YuoSystem<ShapeComponent15>, IUpdate
    {
        public override void Run(ShapeComponent15 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem16 : YuoSystem<ShapeComponent16>, IUpdate
    {
        public override void Run(ShapeComponent16 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem17 : YuoSystem<ShapeComponent17>, IUpdate
    {
        public override void Run(ShapeComponent17 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem18 : YuoSystem<ShapeComponent18>, IUpdate
    {
        public override void Run(ShapeComponent18 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem19 : YuoSystem<ShapeComponent19>, IUpdate
    {
        public override void Run(ShapeComponent19 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem20 : YuoSystem<ShapeComponent20>, IUpdate
    {
        public override void Run(ShapeComponent20 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem21 : YuoSystem<ShapeComponent21>, IUpdate
    {
        public override void Run(ShapeComponent21 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem22 : YuoSystem<ShapeComponent22>, IUpdate
    {
        public override void Run(ShapeComponent22 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem23 : YuoSystem<ShapeComponent23>, IUpdate
    {
        public override void Run(ShapeComponent23 c1) => c1.Value++;
    }
    public sealed class ShapeSingleSystem24 : YuoSystem<ShapeComponent24>, IUpdate
    {
        public override void Run(ShapeComponent24 c1) => c1.Value++;
    }
    public sealed class ShapeDoubleSystem25 : YuoSystem<ShapeComponent0, ShapeComponent1>, IUpdate
    {
        public override void Run(ShapeComponent0 c1, ShapeComponent1 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem26 : YuoSystem<ShapeComponent2, ShapeComponent3>, IUpdate
    {
        public override void Run(ShapeComponent2 c1, ShapeComponent3 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem27 : YuoSystem<ShapeComponent4, ShapeComponent5>, IUpdate
    {
        public override void Run(ShapeComponent4 c1, ShapeComponent5 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem28 : YuoSystem<ShapeComponent6, ShapeComponent7>, IUpdate
    {
        public override void Run(ShapeComponent6 c1, ShapeComponent7 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem29 : YuoSystem<ShapeComponent8, ShapeComponent9>, IUpdate
    {
        public override void Run(ShapeComponent8 c1, ShapeComponent9 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem30 : YuoSystem<ShapeComponent10, ShapeComponent11>, IUpdate
    {
        public override void Run(ShapeComponent10 c1, ShapeComponent11 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem31 : YuoSystem<ShapeComponent12, ShapeComponent13>, IUpdate
    {
        public override void Run(ShapeComponent12 c1, ShapeComponent13 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem32 : YuoSystem<ShapeComponent14, ShapeComponent15>, IUpdate
    {
        public override void Run(ShapeComponent14 c1, ShapeComponent15 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem33 : YuoSystem<ShapeComponent16, ShapeComponent17>, IUpdate
    {
        public override void Run(ShapeComponent16 c1, ShapeComponent17 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem34 : YuoSystem<ShapeComponent18, ShapeComponent19>, IUpdate
    {
        public override void Run(ShapeComponent18 c1, ShapeComponent19 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem35 : YuoSystem<ShapeComponent20, ShapeComponent21>, IUpdate
    {
        public override void Run(ShapeComponent20 c1, ShapeComponent21 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem36 : YuoSystem<ShapeComponent22, ShapeComponent23>, IUpdate
    {
        public override void Run(ShapeComponent22 c1, ShapeComponent23 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem37 : YuoSystem<ShapeComponent24, ShapeComponent25>, IUpdate
    {
        public override void Run(ShapeComponent24 c1, ShapeComponent25 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem38 : YuoSystem<ShapeComponent26, ShapeComponent27>, IUpdate
    {
        public override void Run(ShapeComponent26 c1, ShapeComponent27 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem39 : YuoSystem<ShapeComponent28, ShapeComponent29>, IUpdate
    {
        public override void Run(ShapeComponent28 c1, ShapeComponent29 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem40 : YuoSystem<ShapeComponent30, ShapeComponent31>, IUpdate
    {
        public override void Run(ShapeComponent30 c1, ShapeComponent31 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem41 : YuoSystem<ShapeComponent32, ShapeComponent33>, IUpdate
    {
        public override void Run(ShapeComponent32 c1, ShapeComponent33 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem42 : YuoSystem<ShapeComponent34, ShapeComponent35>, IUpdate
    {
        public override void Run(ShapeComponent34 c1, ShapeComponent35 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem43 : YuoSystem<ShapeComponent36, ShapeComponent37>, IUpdate
    {
        public override void Run(ShapeComponent36 c1, ShapeComponent37 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem44 : YuoSystem<ShapeComponent38, ShapeComponent39>, IUpdate
    {
        public override void Run(ShapeComponent38 c1, ShapeComponent39 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem45 : YuoSystem<ShapeComponent40, ShapeComponent41>, IUpdate
    {
        public override void Run(ShapeComponent40 c1, ShapeComponent41 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem46 : YuoSystem<ShapeComponent42, ShapeComponent43>, IUpdate
    {
        public override void Run(ShapeComponent42 c1, ShapeComponent43 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem47 : YuoSystem<ShapeComponent44, ShapeComponent45>, IUpdate
    {
        public override void Run(ShapeComponent44 c1, ShapeComponent45 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem48 : YuoSystem<ShapeComponent46, ShapeComponent47>, IUpdate
    {
        public override void Run(ShapeComponent46 c1, ShapeComponent47 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeDoubleSystem49 : YuoSystem<ShapeComponent48, ShapeComponent49>, IUpdate
    {
        public override void Run(ShapeComponent48 c1, ShapeComponent49 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeTripleSystem50 : YuoSystem<ShapeComponent0, ShapeComponent1, ShapeComponent2>, IUpdate
    {
        public override void Run(ShapeComponent0 c1, ShapeComponent1 c2, ShapeComponent2 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem51 : YuoSystem<ShapeComponent3, ShapeComponent4, ShapeComponent5>, IUpdate
    {
        public override void Run(ShapeComponent3 c1, ShapeComponent4 c2, ShapeComponent5 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem52 : YuoSystem<ShapeComponent6, ShapeComponent7, ShapeComponent8>, IUpdate
    {
        public override void Run(ShapeComponent6 c1, ShapeComponent7 c2, ShapeComponent8 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem53 : YuoSystem<ShapeComponent9, ShapeComponent10, ShapeComponent11>, IUpdate
    {
        public override void Run(ShapeComponent9 c1, ShapeComponent10 c2, ShapeComponent11 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem54 : YuoSystem<ShapeComponent12, ShapeComponent13, ShapeComponent14>, IUpdate
    {
        public override void Run(ShapeComponent12 c1, ShapeComponent13 c2, ShapeComponent14 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem55 : YuoSystem<ShapeComponent15, ShapeComponent16, ShapeComponent17>, IUpdate
    {
        public override void Run(ShapeComponent15 c1, ShapeComponent16 c2, ShapeComponent17 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem56 : YuoSystem<ShapeComponent18, ShapeComponent19, ShapeComponent20>, IUpdate
    {
        public override void Run(ShapeComponent18 c1, ShapeComponent19 c2, ShapeComponent20 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem57 : YuoSystem<ShapeComponent21, ShapeComponent22, ShapeComponent23>, IUpdate
    {
        public override void Run(ShapeComponent21 c1, ShapeComponent22 c2, ShapeComponent23 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem58 : YuoSystem<ShapeComponent24, ShapeComponent25, ShapeComponent26>, IUpdate
    {
        public override void Run(ShapeComponent24 c1, ShapeComponent25 c2, ShapeComponent26 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem59 : YuoSystem<ShapeComponent27, ShapeComponent28, ShapeComponent29>, IUpdate
    {
        public override void Run(ShapeComponent27 c1, ShapeComponent28 c2, ShapeComponent29 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem60 : YuoSystem<ShapeComponent30, ShapeComponent31, ShapeComponent32>, IUpdate
    {
        public override void Run(ShapeComponent30 c1, ShapeComponent31 c2, ShapeComponent32 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem61 : YuoSystem<ShapeComponent33, ShapeComponent34, ShapeComponent35>, IUpdate
    {
        public override void Run(ShapeComponent33 c1, ShapeComponent34 c2, ShapeComponent35 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem62 : YuoSystem<ShapeComponent36, ShapeComponent37, ShapeComponent38>, IUpdate
    {
        public override void Run(ShapeComponent36 c1, ShapeComponent37 c2, ShapeComponent38 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem63 : YuoSystem<ShapeComponent39, ShapeComponent40, ShapeComponent41>, IUpdate
    {
        public override void Run(ShapeComponent39 c1, ShapeComponent40 c2, ShapeComponent41 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem64 : YuoSystem<ShapeComponent42, ShapeComponent43, ShapeComponent44>, IUpdate
    {
        public override void Run(ShapeComponent42 c1, ShapeComponent43 c2, ShapeComponent44 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem65 : YuoSystem<ShapeComponent45, ShapeComponent46, ShapeComponent47>, IUpdate
    {
        public override void Run(ShapeComponent45 c1, ShapeComponent46 c2, ShapeComponent47 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem66 : YuoSystem<ShapeComponent48, ShapeComponent49, ShapeComponent50>, IUpdate
    {
        public override void Run(ShapeComponent48 c1, ShapeComponent49 c2, ShapeComponent50 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem67 : YuoSystem<ShapeComponent51, ShapeComponent52, ShapeComponent53>, IUpdate
    {
        public override void Run(ShapeComponent51 c1, ShapeComponent52 c2, ShapeComponent53 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem68 : YuoSystem<ShapeComponent54, ShapeComponent55, ShapeComponent56>, IUpdate
    {
        public override void Run(ShapeComponent54 c1, ShapeComponent55 c2, ShapeComponent56 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeTripleSystem69 : YuoSystem<ShapeComponent57, ShapeComponent58, ShapeComponent59>, IUpdate
    {
        public override void Run(ShapeComponent57 c1, ShapeComponent58 c2, ShapeComponent59 c3) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; }
    }
    public sealed class ShapeQuadSystem70 : YuoSystem<ShapeComponent0, ShapeComponent1, ShapeComponent2, ShapeComponent3>, IUpdate
    {
        public override void Run(ShapeComponent0 c1, ShapeComponent1 c2, ShapeComponent2 c3, ShapeComponent3 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem71 : YuoSystem<ShapeComponent4, ShapeComponent5, ShapeComponent6, ShapeComponent7>, IUpdate
    {
        public override void Run(ShapeComponent4 c1, ShapeComponent5 c2, ShapeComponent6 c3, ShapeComponent7 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem72 : YuoSystem<ShapeComponent8, ShapeComponent9, ShapeComponent10, ShapeComponent11>, IUpdate
    {
        public override void Run(ShapeComponent8 c1, ShapeComponent9 c2, ShapeComponent10 c3, ShapeComponent11 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem73 : YuoSystem<ShapeComponent12, ShapeComponent13, ShapeComponent14, ShapeComponent15>, IUpdate
    {
        public override void Run(ShapeComponent12 c1, ShapeComponent13 c2, ShapeComponent14 c3, ShapeComponent15 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem74 : YuoSystem<ShapeComponent16, ShapeComponent17, ShapeComponent18, ShapeComponent19>, IUpdate
    {
        public override void Run(ShapeComponent16 c1, ShapeComponent17 c2, ShapeComponent18 c3, ShapeComponent19 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem75 : YuoSystem<ShapeComponent20, ShapeComponent21, ShapeComponent22, ShapeComponent23>, IUpdate
    {
        public override void Run(ShapeComponent20 c1, ShapeComponent21 c2, ShapeComponent22 c3, ShapeComponent23 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem76 : YuoSystem<ShapeComponent24, ShapeComponent25, ShapeComponent26, ShapeComponent27>, IUpdate
    {
        public override void Run(ShapeComponent24 c1, ShapeComponent25 c2, ShapeComponent26 c3, ShapeComponent27 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem77 : YuoSystem<ShapeComponent28, ShapeComponent29, ShapeComponent30, ShapeComponent31>, IUpdate
    {
        public override void Run(ShapeComponent28 c1, ShapeComponent29 c2, ShapeComponent30 c3, ShapeComponent31 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem78 : YuoSystem<ShapeComponent32, ShapeComponent33, ShapeComponent34, ShapeComponent35>, IUpdate
    {
        public override void Run(ShapeComponent32 c1, ShapeComponent33 c2, ShapeComponent34 c3, ShapeComponent35 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem79 : YuoSystem<ShapeComponent36, ShapeComponent37, ShapeComponent38, ShapeComponent39>, IUpdate
    {
        public override void Run(ShapeComponent36 c1, ShapeComponent37 c2, ShapeComponent38 c3, ShapeComponent39 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem80 : YuoSystem<ShapeComponent40, ShapeComponent41, ShapeComponent42, ShapeComponent43>, IUpdate
    {
        public override void Run(ShapeComponent40 c1, ShapeComponent41 c2, ShapeComponent42 c3, ShapeComponent43 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem81 : YuoSystem<ShapeComponent44, ShapeComponent45, ShapeComponent46, ShapeComponent47>, IUpdate
    {
        public override void Run(ShapeComponent44 c1, ShapeComponent45 c2, ShapeComponent46 c3, ShapeComponent47 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem82 : YuoSystem<ShapeComponent48, ShapeComponent49, ShapeComponent50, ShapeComponent51>, IUpdate
    {
        public override void Run(ShapeComponent48 c1, ShapeComponent49 c2, ShapeComponent50 c3, ShapeComponent51 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem83 : YuoSystem<ShapeComponent52, ShapeComponent53, ShapeComponent54, ShapeComponent55>, IUpdate
    {
        public override void Run(ShapeComponent52 c1, ShapeComponent53 c2, ShapeComponent54 c3, ShapeComponent55 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeQuadSystem84 : YuoSystem<ShapeComponent56, ShapeComponent57, ShapeComponent58, ShapeComponent59>, IUpdate
    {
        public override void Run(ShapeComponent56 c1, ShapeComponent57 c2, ShapeComponent58 c3, ShapeComponent59 c4) { c1.Value++; c2.Value += c1.Value & 1; c3.Value += c2.Value & 1; c4.Value += c3.Value & 1; }
    }
    public sealed class ShapeBaseSystem85 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem86 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem87 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem88 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem89 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem90 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem91 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem92 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem93 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseSystem94 : YuoSystemOfBase<ShapeBaseBenchComponent>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1) => c1.Value++;
    }
    public sealed class ShapeBaseDoubleSystem95 : YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent0>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent0 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem96 : YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent1>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent1 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem97 : YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent2>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent2 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem98 : YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent3>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent3 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ShapeBaseDoubleSystem99 : YuoSystemOfBase<ShapeBaseBenchComponent, ShapeComponent4>, IUpdate
    {
        public override void Run(ShapeBaseBenchComponent c1, ShapeComponent4 c2) { c1.Value++; c2.Value += c1.Value & 1; }
    }
    public sealed class ParallelShapeRuntime : IDisposable
    {
        private readonly int _shardCount;
        private readonly YuoCoreRuntime _runtime = new();
        private readonly List<YuoFiber> _shards = new();
        private bool _started;

        public ParallelShapeRuntime(int shardCount)
        {
            _shardCount = Math.Max(1, shardCount);
        }

        public void Start()
        {
            if (_started) return;

            for (var i = 0; i < _shardCount; i++)
            {
                _shards.Add(_runtime.Fibers.CreateFiber(new YuoFiberOptions
                {
                    Id = i + 1,
                    Name = $"ShapeShard-{i}",
                    SetWorldAsDefault = i == 0,
                    MaxMailboxMessagesPerTick = 100_000
                }));
            }

            _started = true;
        }

        public void SeedEntities(int count)
        {
            if (!_started) throw new InvalidOperationException("Runtime has not started.");

            for (var i = 0; i < count; i++)
            {
                var shard = _shards[i % _shards.Count];
                var entity = new YuoEntity(shard.World, i + 30_000_000);
                AddAllComponents(entity);
            }
        }

        public void Tick()
        {
            _runtime.TickParallelAsync(_shards.Count).AsTask().GetAwaiter().GetResult();
        }

        private static void AddAllComponents(YuoEntity entity)
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

        public void Dispose()
        {
            _runtime.Dispose();
            _shards.Clear();
        }
    }
}
