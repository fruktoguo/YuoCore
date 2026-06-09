using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Benchmarks;
using YuoCore.Game;

if (args.Contains("--perf-smoke", StringComparer.Ordinal))
{
    PerfSmokeRunner.Run(args);
    return;
}

if (args.Contains("--timer-smoke", StringComparer.Ordinal))
{
    TimerPerfSmokeRunner.Run(args);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(EcsIdleBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
public class EcsIdleBenchmarks
{
    private GameRuntime _runtime = null!;

    [Params(1_000, 10_000)]
    public int Players { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _runtime = new GameRuntime(Options.Create(new GameOptions { TickRate = 10 }));
        _runtime.Start();
        _runtime.SeedPlayers(Players);
        _runtime.Tick();
    }

    [GlobalCleanup]
    public void Cleanup() => _runtime.Dispose();

    [Benchmark]
    public void Tick() => _runtime.Tick();

    [Benchmark]
    public void TickBatch100()
    {
        for (var i = 0; i < 100; i++)
        {
            _runtime.Tick();
        }
    }

    [Benchmark]
    public YuoGameStatsDto SnapshotStats() => _runtime.Stats();
}

public static class PerfSmokeRunner
{
    public static void Run(string[] args)
    {
        var options = PerfSmokeOptions.Parse(args);
        Console.WriteLine($"perf-smoke: entities={options.Entities:n0} ticks={options.Ticks:n0} shards={options.Shards:n0}");

        using var runtime = new ParallelShapeWorkload.ParallelShapeRuntime(options.Shards);
        runtime.Start();
        runtime.SeedEntities(options.Entities);

        // 预热一次，避免把 system 首次初始化和 JIT 成本算进 tick 结果。
        runtime.Tick();

        var samples = new double[options.Ticks];
        var total = 0.0;
        var sw = new System.Diagnostics.Stopwatch();
        for (var i = 0; i < options.Ticks; i++)
        {
            sw.Restart();
            runtime.Tick();
            sw.Stop();
            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            samples[i] = elapsedMs;
            total += elapsedMs;
        }

        Array.Sort(samples);
        var average = total / options.Ticks;
        var p50 = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var max = samples[^1];

        Console.WriteLine($"tick-ms: avg={average:n3} p50={p50:n3} p95={p95:n3} max={max:n3}");
        Console.WriteLine($"entity-tick/s: {(options.Entities * options.Ticks) / (total / 1000.0):n0}");
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedSamples.Length) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }
}

public sealed record PerfSmokeOptions(int Entities, int Ticks, int Shards)
{
    public static PerfSmokeOptions Parse(string[] args)
    {
        var entities = 10_000;
        var ticks = 20;
        var shards = Math.Max(1, Environment.ProcessorCount / 2);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--entities":
                    entities = int.Parse(args[++i]);
                    break;
                case "--ticks":
                    ticks = int.Parse(args[++i]);
                    break;
                case "--shards":
                    shards = int.Parse(args[++i]);
                    break;
            }
        }

        if (entities <= 0) throw new ArgumentOutOfRangeException(nameof(entities), "entities must be greater than zero.");
        if (ticks <= 0) throw new ArgumentOutOfRangeException(nameof(ticks), "ticks must be greater than zero.");
        if (shards <= 0) throw new ArgumentOutOfRangeException(nameof(shards), "shards must be greater than zero.");

        return new PerfSmokeOptions(entities, ticks, shards);
    }
}
