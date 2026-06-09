using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Game;

namespace YuoCore.Benchmarks;

[MemoryDiagnoser]
public class ActorCommandBenchmarks
{
    private GameRuntime _directRuntime = null!;
    private GameRuntime _actorRuntime = null!;
    private Guid[] _directPlayers = [];
    private Guid[] _actorPlayers = [];

    [Params(10_000)]
    public int Players { get; set; }

    [Params(4)]
    public int Shards { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _directRuntime = CreateRuntime();
        _actorRuntime = CreateRuntime();
        _directPlayers = Seed(_directRuntime, Players);
        _actorPlayers = Seed(_actorRuntime, Players);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _directRuntime.Dispose();
        _actorRuntime.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void DirectApplyAndTickClicks()
    {
        foreach (var playerId in _directPlayers)
        {
            _directRuntime.TryApplyClick(playerId, 1);
        }

        _directRuntime.Tick();
    }

    [Benchmark]
    public void ActorQueueAndTickClicks()
    {
        foreach (var playerId in _actorPlayers)
        {
            _actorRuntime.TryEnqueueClick(playerId, 1);
        }

        _actorRuntime.Tick();
    }

    private GameRuntime CreateRuntime()
    {
        var runtime = new GameRuntime(Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = Shards
        }));
        runtime.Start();
        return runtime;
    }

    private static Guid[] Seed(GameRuntime runtime, int count)
    {
        var players = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            players[i] = runtime.CreatePlayer($"Bench-{i}").PlayerId;
        }

        runtime.Tick();
        return players;
    }
}
