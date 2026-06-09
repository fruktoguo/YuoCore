using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Backend.WebSockets;
using YuoCore.Ecs;
using YuoCore.Game;
using YuoCore.Gateway;
using YuoCore.Persistence;
using YuoCore.Protocol;
using YuoCore.StateSync;
using ParallelShapeWorkload;

var config = LoadConfig.Parse(args);
if (string.Equals(config.Mode, "local-composite", StringComparison.OrdinalIgnoreCase))
{
    var result = LocalCompositeRunner.Run(config);
    if (!result.Passed)
    {
        Environment.ExitCode = 2;
    }

    return;
}

if (string.Equals(config.Mode, "ecs-shape", StringComparison.OrdinalIgnoreCase))
{
    var result = EcsShapeRunner.Run(config);
    if (!result.Passed)
    {
        Environment.ExitCode = 2;
    }

    return;
}

if (string.Equals(config.Mode, "aoi-streams", StringComparison.OrdinalIgnoreCase))
{
    var result = AoiStreamsRunner.Run(config);
    if (!result.Passed)
    {
        Environment.ExitCode = 2;
    }

    return;
}

await WebSocketLoadRunner.RunAsync(config);

public static class EcsShapeRunner
{
    private const int ComponentTypes = 100;
    private const int SystemTypes = 100;

    public static EcsShapeResult Run(LoadConfig config)
    {
        Console.WriteLine(
            $"ecs-shape: entities={config.StateEntities:n0} ticks={config.Ticks:n0} duration-mode={config.DurationMode} duration={config.DurationSeconds:n0}s shards={config.Shards:n0} components={ComponentTypes} systems={SystemTypes}");

        using var runtime = new ParallelShapeRuntime(config.Shards);
        runtime.Start();

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var runtimeBefore = RuntimeCounters.Capture();
        var setupSw = Stopwatch.StartNew();
        runtime.SeedEntities(config.StateEntities);
        runtime.Tick();
        setupSw.Stop();

        var samples = new List<double>(config.DurationMode ? Math.Min(config.Ticks, 4096) : config.Ticks);
        var runSw = Stopwatch.StartNew();
        for (var tick = 1; ShouldRunTick(config, tick, runSw.Elapsed); tick++)
        {
            var sw = Stopwatch.StartNew();
            runtime.Tick();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        runSw.Stop();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var runtimeAfter = RuntimeCounters.Capture();
        var tickStats = BuildStats(samples);
        var passed = IsWithinThresholds(config, tickStats);
        var runtimeMetrics = RuntimeMetrics.From(runtimeBefore, runtimeAfter);
        var result = new EcsShapeResult(
            Passed: passed,
            Entities: config.StateEntities,
            Ticks: config.Ticks,
            ActualTicks: samples.Count,
            DurationMode: config.DurationMode,
            ElapsedSeconds: runSw.Elapsed.TotalSeconds,
            TicksPerSecond: samples.Count / Math.Max(runSw.Elapsed.TotalSeconds, 0.001),
            Shards: config.Shards,
            ComponentTypes: ComponentTypes,
            SystemTypes: SystemTypes,
            SetupMs: setupSw.Elapsed.TotalMilliseconds,
            TickMs: tickStats,
            AllocatedMb: (allocatedAfter - allocatedBefore) / 1024d / 1024d,
            Runtime: runtimeMetrics,
            FailP95Ms: config.FailP95Ms,
            FailMaxMs: config.FailMaxMs);

        Console.WriteLine($"setup-ms={result.SetupMs:n3}");
        Console.WriteLine($"tick-ms: avg={tickStats.Avg:n3} p50={tickStats.P50:n3} p95={tickStats.P95:n3} max={tickStats.Max:n3}");
        Console.WriteLine($"elapsed={result.ElapsedSeconds:n3}s ticks-per-second={result.TicksPerSecond:n2}");
        Console.WriteLine($"allocated-mb={result.AllocatedMb:n2} heap-after-mb={runtimeMetrics.HeapAfterMb:n2} gc0={runtimeMetrics.Gen0Collections:n0} gc1={runtimeMetrics.Gen1Collections:n0} gc2={runtimeMetrics.Gen2Collections:n0}");
        if (!passed)
        {
            Console.WriteLine($"threshold-failed: p95={tickStats.P95:n3} max={tickStats.Max:n3}");
        }

        WriteJsonResultIfRequested(config, result);
        return result;
    }

    private static bool ShouldRunTick(LoadConfig config, int nextTick, TimeSpan elapsed)
    {
        return config.DurationMode
            ? elapsed < TimeSpan.FromSeconds(config.DurationSeconds)
            : nextTick <= config.Ticks;
    }

    private static MetricStats BuildStats(IReadOnlyList<double> samples)
    {
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        var totalMs = sorted.Sum();
        return new MetricStats(
            Total: totalMs,
            Avg: totalMs / sorted.Length,
            P50: Percentile(sorted, 0.50),
            P95: Percentile(sorted, 0.95),
            Max: sorted[^1]);
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sortedSamples.Length) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }

    private static bool IsWithinThresholds(LoadConfig config, MetricStats tickStats)
    {
        if (config.FailP95Ms is { } p95 && tickStats.P95 > p95)
        {
            return false;
        }

        if (config.FailMaxMs is { } max && tickStats.Max > max)
        {
            return false;
        }

        return true;
    }

    private static void WriteJsonResultIfRequested(LoadConfig config, EcsShapeResult result)
    {
        if (string.IsNullOrWhiteSpace(config.JsonOutputPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(config.JsonOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            config.JsonOutputPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json-output={config.JsonOutputPath}");
    }
}

public static class LocalCompositeRunner
{
    public static LocalCompositeResult Run(LoadConfig config)
    {
        Console.WriteLine(
            $"local-composite: players={config.Players:n0} ticks={config.Ticks:n0} duration-mode={config.DurationMode} duration={config.DurationSeconds:n0}s shards={config.Shards:n0} state={config.StateEntities:n0} observers={config.AoiObservers:n0} aoi-mode={config.AoiResultMode} payload-profile={config.PayloadProfile} persistence-enqueue-mode={config.PersistenceEnqueueMode}");

        using var runtime = new GameRuntime(Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = config.Shards,
            MaxTickConcurrency = config.Shards
        }));
        runtime.Start();

        var playerIds = SeedPlayers(runtime, config.Players);
        var stateWorld = CreateStateWorld(config.StateEntities, out var stateEntities);
        try
        {
            var snapshotBuilder = new YuoFullStateSnapshotBuilder();
            var deltaBuilder = new YuoDirtyStateDeltaBuilder();
            var persistenceQueue = new YuoPersistenceQueue(Math.Max(1024, config.PersistenceCommandsPerTick * 4));
            var persistenceBatchBuffer = string.Equals(config.PersistenceEnqueueMode, "batch", StringComparison.OrdinalIgnoreCase)
                ? new YuoPersistenceCommand[config.PersistenceCommandsPerTick]
                : null;

            runtime.Tick();
            _ = snapshotBuilder.BuildFullSnapshot(0, stateEntities);

            var capacity = config.DurationMode ? Math.Min(config.Ticks, 4096) : config.Ticks;
            var collectTickSamples = !string.IsNullOrWhiteSpace(config.CsvOutputPath);
            var samples = new List<double>(capacity);
            var actorEnqueueSamples = new List<double>(capacity);
            var runtimeTickSamples = new List<double>(capacity);
            var stateSyncSamples = new List<double>(capacity);
            var aoiSamples = new List<double>(capacity);
            var aoiProjectionSamples = new List<double>(capacity);
            var aoiIndexSamples = new List<double>(capacity);
            var aoiQuerySamples = new List<double>(capacity);
            var persistenceSamples = new List<double>(capacity);
            var tickSamples = collectTickSamples ? new List<LocalCompositeTickSample>(capacity) : null;
            var totalActorCommands = 0L;
            var totalStateChanges = 0L;
            var totalVisible = 0L;
            var totalPersistenceCommands = 0L;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var runtimeBefore = RuntimeCounters.Capture();
            var runSw = Stopwatch.StartNew();
            var sw = new Stopwatch();
            var segmentSw = new Stopwatch();

            for (var tick = 1; ShouldRunTick(config, tick, runSw.Elapsed); tick++)
            {
                sw.Restart();

                segmentSw.Restart();
                foreach (var playerId in playerIds)
                {
                    runtime.TryEnqueueClick(playerId, 1);
                }
                segmentSw.Stop();
                var actorEnqueueMs = segmentSw.Elapsed.TotalMilliseconds;
                actorEnqueueSamples.Add(actorEnqueueMs);

                segmentSw.Restart();
                runtime.Tick();
                segmentSw.Stop();
                var runtimeTickMs = segmentSw.Elapsed.TotalMilliseconds;
                runtimeTickSamples.Add(runtimeTickMs);

                totalActorCommands += playerIds.Length;

                segmentSw.Restart();
                var stateChanges = UpdateStateAndBuildDelta(deltaBuilder, stateEntities, tick, config.PayloadProfile).Changes.Count;
                totalStateChanges += stateChanges;
                segmentSw.Stop();
                var stateSyncMs = segmentSw.Elapsed.TotalMilliseconds;
                stateSyncSamples.Add(stateSyncMs);

                segmentSw.Restart();
                var aoiResult = QueryAoi(stateEntities, Math.Min(config.AoiObservers, stateEntities.Count), config.AoiRadius, config.AoiResultMode);
                totalVisible += aoiResult.VisibleCount;
                segmentSw.Stop();
                var aoiMs = segmentSw.Elapsed.TotalMilliseconds;
                aoiSamples.Add(aoiMs);
                aoiProjectionSamples.Add(aoiResult.ProjectionMs);
                aoiIndexSamples.Add(aoiResult.IndexMs);
                aoiQuerySamples.Add(aoiResult.QueryMs);

                segmentSw.Restart();
                var persistenceDrained = EnqueueAndDrainPersistence(
                    persistenceQueue,
                    config.PersistenceCommandsPerTick,
                    tick,
                    config.PayloadProfile,
                    config.PersistenceEnqueueMode,
                    persistenceBatchBuffer);
                totalPersistenceCommands += persistenceDrained;
                segmentSw.Stop();
                var persistenceMs = segmentSw.Elapsed.TotalMilliseconds;
                persistenceSamples.Add(persistenceMs);

                sw.Stop();
                var tickMs = sw.Elapsed.TotalMilliseconds;
                samples.Add(tickMs);
                if (tickSamples is not null)
                {
                    tickSamples.Add(new LocalCompositeTickSample(
                        Tick: tick,
                        TickMs: tickMs,
                        ActorEnqueueMs: actorEnqueueMs,
                        RuntimeTickMs: runtimeTickMs,
                        StateSyncDeltaMs: stateSyncMs,
                        AoiQueryMs: aoiMs,
                        AoiProjectMs: aoiResult.ProjectionMs,
                        AoiIndexMs: aoiResult.IndexMs,
                        AoiObserversMs: aoiResult.QueryMs,
                        PersistenceMs: persistenceMs,
                        VisibleResults: aoiResult.VisibleCount,
                        StateChanges: stateChanges,
                        PersistenceDrained: persistenceDrained,
                        AllocatedMbTotal: (GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore) / 1024d / 1024d));
                }
            }

            runSw.Stop();
            var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
            var runtimeAfter = RuntimeCounters.Capture();
            var runtimeMetrics = RuntimeMetrics.From(runtimeBefore, runtimeAfter);
            var stats = runtime.Stats();
            var tickStats = BuildStats(samples);
            var segmentStats = new Dictionary<string, MetricStats>(StringComparer.Ordinal)
            {
                ["actor-enqueue"] = BuildStats(actorEnqueueSamples),
                ["runtime-tick"] = BuildStats(runtimeTickSamples),
                ["statesync-delta"] = BuildStats(stateSyncSamples),
                ["aoi-query"] = BuildStats(aoiSamples),
                ["aoi-project"] = BuildStats(aoiProjectionSamples),
                ["aoi-index"] = BuildStats(aoiIndexSamples),
                ["aoi-observers"] = BuildStats(aoiQuerySamples),
                ["persistence"] = BuildStats(persistenceSamples)
            };
            var allocatedMb = (allocatedAfter - allocatedBefore) / 1024d / 1024d;
            var passed = IsWithinThresholds(config, tickStats);

            Console.WriteLine($"tick-ms: avg={tickStats.Avg:n3} p50={tickStats.P50:n3} p95={tickStats.P95:n3} max={tickStats.Max:n3}");
            foreach (var segment in segmentStats)
            {
                PrintSegmentStats(segment.Key, segment.Value);
            }

            Console.WriteLine($"actor-commands={totalActorCommands:n0} processed={stats.ProcessedCommands:n0} players={stats.Players:n0}");
            Console.WriteLine($"state-changes={totalStateChanges:n0} visible-results={totalVisible:n0}");
            Console.WriteLine($"persistence-drained={totalPersistenceCommands:n0} queue-pending={persistenceQueue.GetStats().PendingCount:n0}");
            Console.WriteLine($"elapsed={runSw.Elapsed.TotalSeconds:n3}s ticks-per-second={samples.Count / Math.Max(runSw.Elapsed.TotalSeconds, 0.001):n2}");
            Console.WriteLine($"allocated-mb={allocatedMb:n2} heap-after-mb={runtimeMetrics.HeapAfterMb:n2} gc0={runtimeMetrics.Gen0Collections:n0} gc1={runtimeMetrics.Gen1Collections:n0} gc2={runtimeMetrics.Gen2Collections:n0}");
            Console.WriteLine($"threadpool threads={runtimeMetrics.ThreadPoolThreadCount:n0} pending={runtimeMetrics.ThreadPoolPendingWorkItems:n0} completed-delta={runtimeMetrics.ThreadPoolCompletedWorkItemsDelta:n0} available-workers={runtimeMetrics.ThreadPoolAvailableWorkerThreads:n0}/{runtimeMetrics.ThreadPoolMaxWorkerThreads:n0}");

            if (!passed)
            {
                Console.WriteLine($"threshold-failed: p95={tickStats.P95:n3} max={tickStats.Max:n3}");
            }

            var result = new LocalCompositeResult(
                Passed: passed,
                Players: config.Players,
                Ticks: config.Ticks,
                ActualTicks: samples.Count,
                DurationMode: config.DurationMode,
                ElapsedSeconds: runSw.Elapsed.TotalSeconds,
                TicksPerSecond: samples.Count / Math.Max(runSw.Elapsed.TotalSeconds, 0.001),
                Shards: config.Shards,
                StateEntities: config.StateEntities,
                AoiObservers: config.AoiObservers,
                AoiRadius: config.AoiRadius,
                AoiResultMode: config.AoiResultMode,
                PayloadProfile: config.PayloadProfile,
                PersistenceEnqueueMode: config.PersistenceEnqueueMode,
                PersistenceCommandsPerTick: config.PersistenceCommandsPerTick,
                TickMs: tickStats,
                SegmentsMs: segmentStats,
                ActorCommands: totalActorCommands,
                ProcessedActorCommands: stats.ProcessedCommands,
                RuntimePlayers: stats.Players,
                StateChanges: totalStateChanges,
                VisibleResults: totalVisible,
                PersistenceDrained: totalPersistenceCommands,
                PersistencePending: persistenceQueue.GetStats().PendingCount,
                AllocatedMb: allocatedMb,
                Runtime: runtimeMetrics,
                FailP95Ms: config.FailP95Ms,
                FailMaxMs: config.FailMaxMs);

            WriteJsonResultIfRequested(config, result);
            WriteCsvResultIfRequested(
                config,
                tickSamples is null ? Array.Empty<LocalCompositeTickSample>() : tickSamples);
            return result;
        }
        finally
        {
            stateWorld.OnDestroy();
        }
    }

    private static bool ShouldRunTick(LoadConfig config, int nextTick, TimeSpan elapsed)
    {
        return config.DurationMode
            ? elapsed < TimeSpan.FromSeconds(config.DurationSeconds)
            : nextTick <= config.Ticks;
    }

    private static Guid[] SeedPlayers(GameRuntime runtime, int players)
    {
        var playerIds = new Guid[players];
        for (var i = 0; i < players; i++)
        {
            playerIds[i] = runtime.CreatePlayer($"Load-{i}").PlayerId;
        }

        return playerIds;
    }

    private static YuoWorld CreateStateWorld(int count, out List<YuoEntity> entities)
    {
        var world = new YuoWorld();
        world.OnInit(setAsDefault: false);
        entities = new List<YuoEntity>(count);

        for (var i = 0; i < count; i++)
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<YuoNetworkIdentityComponent>().NetworkId = i + 1;
            entity.GetComponent<YuoNetworkIdentityComponent>().Kind = "unit";
            entity.AddComponent<YuoNetworkStateComponent>().SetPayloadOwned(BitConverter.GetBytes(i));
            var aoi = entity.AddComponent<YuoAoiComponent>();
            aoi.SetPosition(i % 256, 0, i / 256);
            aoi.ViewRadius = 24;
            entities.Add(entity);
        }

        return world;
    }

    private static YuoStateDelta UpdateStateAndBuildDelta(
        YuoDirtyStateDeltaBuilder deltaBuilder,
        IReadOnlyList<YuoEntity> entities,
        int tick,
        string payloadProfile)
    {
        if (string.Equals(payloadProfile, "shared-input", StringComparison.OrdinalIgnoreCase))
        {
            var payload = BitConverter.GetBytes(tick);
            foreach (var entity in entities)
            {
                entity.GetComponent<YuoNetworkStateComponent>().SetPayload(payload);
            }
        }
        else
        {
            foreach (var entity in entities)
            {
                entity.GetComponent<YuoNetworkStateComponent>().SetPayloadOwned(BitConverter.GetBytes(tick));
            }
        }

        return deltaBuilder.BuildDirtyDelta(tick, entities);
    }

    private static AoiQueryResult QueryAoi(IReadOnlyList<YuoEntity> entities, int observers, float radius, string resultMode)
    {
        var sw = Stopwatch.StartNew();
        var targets = new YuoAoiTarget[entities.Count];
        for (var i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            var identity = entity.GetComponent<YuoNetworkIdentityComponent>();
            var aoi = entity.GetComponent<YuoAoiComponent>();
            targets[i] = new YuoAoiTarget(identity.NetworkId, aoi.Position);
        }
        sw.Stop();
        var projectionMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var index = YuoGridAoiTargetIndex.Create(cellSize: 16, targets);
        sw.Stop();
        var indexMs = sw.Elapsed.TotalMilliseconds;

        var total = 0L;

        sw.Restart();
        for (var i = 0; i < observers; i++)
        {
            var identity = entities[i].GetComponent<YuoNetworkIdentityComponent>();
            var aoi = entities[i].GetComponent<YuoAoiComponent>();
            var observer = new YuoAoiObserver(identity.NetworkId, aoi.Position, radius);
            total += string.Equals(resultMode, "count", StringComparison.OrdinalIgnoreCase)
                ? index.CountVisible(observer)
                : index.QueryVisible(observer).Count;
        }
        sw.Stop();

        return new AoiQueryResult(total, projectionMs, indexMs, sw.Elapsed.TotalMilliseconds);
    }

    private static long EnqueueAndDrainPersistence(
        YuoPersistenceQueue queue,
        int count,
        int tick,
        string payloadProfile,
        string enqueueMode,
        YuoPersistenceCommand[]? batchBuffer)
    {
        if (string.Equals(enqueueMode, "batch", StringComparison.OrdinalIgnoreCase))
        {
            // 压测只评估队列批量写入本身，批量缓冲复用由调用方持有，避免每 tick 新数组掩盖真实队列成本。
            var commands = batchBuffer ?? throw new ArgumentNullException(nameof(batchBuffer));
            if (string.Equals(payloadProfile, "shared-input", StringComparison.OrdinalIgnoreCase))
            {
                var payload = BitConverter.GetBytes(tick);
                for (var i = 0; i < count; i++)
                {
                    commands[i] = new AppendEventCommand(new YuoEventRecord(
                        $"load-{i % 1024}",
                        "Tick",
                        payload,
                        DateTimeOffset.UtcNow));
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    commands[i] = new AppendEventCommand(new YuoEventRecord(
                        $"load-{i % 1024}",
                        "Tick",
                        BitConverter.GetBytes(tick),
                        DateTimeOffset.UtcNow));
                }
            }

            queue.EnqueueBatchAsync(commands)
                .GetAwaiter()
                .GetResult();

            return DrainPersistence(queue);
        }

        if (string.Equals(payloadProfile, "shared-input", StringComparison.OrdinalIgnoreCase))
        {
            var payload = BitConverter.GetBytes(tick);
            for (var i = 0; i < count; i++)
            {
                queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
                        $"load-{i % 1024}",
                        "Tick",
                        payload,
                        DateTimeOffset.UtcNow)))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
                        $"load-{i % 1024}",
                        "Tick",
                        BitConverter.GetBytes(tick),
                        DateTimeOffset.UtcNow)))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }

        return DrainPersistence(queue);
    }

    private static long DrainPersistence(YuoPersistenceQueue queue)
    {
        var drained = 0;
        while (true)
        {
            var batch = queue.TryDequeueBatch(1024);
            if (batch.Count == 0) return drained;
            drained += batch.Count;
        }
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sortedSamples.Length) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }

    private static MetricStats BuildStats(IReadOnlyList<double> samples)
    {
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        var totalMs = sorted.Sum();
        return new MetricStats(
            Total: totalMs,
            Avg: totalMs / sorted.Length,
            P50: Percentile(sorted, 0.50),
            P95: Percentile(sorted, 0.95),
            Max: sorted[^1]);
    }

    private static void PrintSegmentStats(string name, MetricStats stats)
    {
        Console.WriteLine($"segment-ms {name}: total={stats.Total:n3} avg={stats.Avg:n3} p50={stats.P50:n3} p95={stats.P95:n3} max={stats.Max:n3}");
    }

    private static bool IsWithinThresholds(LoadConfig config, MetricStats tickStats)
    {
        if (config.FailP95Ms is { } p95 && tickStats.P95 > p95)
        {
            return false;
        }

        if (config.FailMaxMs is { } max && tickStats.Max > max)
        {
            return false;
        }

        return true;
    }

    private static void WriteJsonResultIfRequested(LoadConfig config, LocalCompositeResult result)
    {
        if (string.IsNullOrWhiteSpace(config.JsonOutputPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(config.JsonOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(config.JsonOutputPath, json);
        Console.WriteLine($"json-output={config.JsonOutputPath}");
    }

    private static void WriteCsvResultIfRequested(LoadConfig config, IReadOnlyList<LocalCompositeTickSample> samples)
    {
        if (string.IsNullOrWhiteSpace(config.CsvOutputPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(config.CsvOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new StreamWriter(config.CsvOutputPath);
        writer.WriteLine("tick,tick_ms,actor_enqueue_ms,runtime_tick_ms,statesync_delta_ms,aoi_query_ms,aoi_project_ms,aoi_index_ms,aoi_observers_ms,persistence_ms,visible_results,state_changes,persistence_drained,allocated_mb_total");
        foreach (var sample in samples)
        {
            writer.WriteLine(string.Join(',', new[]
            {
                sample.Tick.ToString(CultureInfo.InvariantCulture),
                sample.TickMs.ToString("R", CultureInfo.InvariantCulture),
                sample.ActorEnqueueMs.ToString("R", CultureInfo.InvariantCulture),
                sample.RuntimeTickMs.ToString("R", CultureInfo.InvariantCulture),
                sample.StateSyncDeltaMs.ToString("R", CultureInfo.InvariantCulture),
                sample.AoiQueryMs.ToString("R", CultureInfo.InvariantCulture),
                sample.AoiProjectMs.ToString("R", CultureInfo.InvariantCulture),
                sample.AoiIndexMs.ToString("R", CultureInfo.InvariantCulture),
                sample.AoiObserversMs.ToString("R", CultureInfo.InvariantCulture),
                sample.PersistenceMs.ToString("R", CultureInfo.InvariantCulture),
                sample.VisibleResults.ToString(CultureInfo.InvariantCulture),
                sample.StateChanges.ToString(CultureInfo.InvariantCulture),
                sample.PersistenceDrained.ToString(CultureInfo.InvariantCulture),
                sample.AllocatedMbTotal.ToString("R", CultureInfo.InvariantCulture)
            }));
        }

        Console.WriteLine($"csv-output={config.CsvOutputPath}");
    }

    private readonly record struct AoiQueryResult(long VisibleCount, double ProjectionMs, double IndexMs, double QueryMs);
}

public static class AoiStreamsRunner
{
    public static AoiStreamsResult Run(LoadConfig config)
    {
        Console.WriteLine(
            $"aoi-streams: players={config.Players:n0} ticks={config.Ticks:n0} duration-mode={config.DurationMode} duration={config.DurationSeconds:n0}s shards={config.Shards:n0} observers={config.AoiObservers:n0} encode-pushes={config.EncodePushes} pooled-encode-pushes={config.PooledEncodePushes}");

        var gameOptions = Options.Create(new GameOptions
        {
            TickRate = 10,
            ShardCount = config.Shards,
            MaxTickConcurrency = config.Shards,
            WorldEventsEnabled = false,
            StateSyncAoiCellSize = 10,
            StateSyncAoiRecoverRadiusCells = 1
        });
        using var runtime = new GameRuntime(gameOptions);
        runtime.Start();
        var setupSw = Stopwatch.StartNew();
        var players = SeedPlayers(runtime, config.Players);
        runtime.Tick();
        var dispatcher = new GameStateSyncPushDispatcher(runtime, gameOptions);
        var coordinator = new GameAoiStreamPushCoordinator(runtime, dispatcher, gameOptions);
        var sessions = players
            .Take(Math.Min(config.AoiObservers, players.Length))
            .Select(playerId =>
            {
                var session = new YuoGatewaySession();
                session.BindPlayer(playerId);
                return session;
            })
            .ToArray();
        var warmupBuffer = new List<YuoGatewayOutboundMessage>(16);
        foreach (var session in sessions)
        {
            warmupBuffer.Clear();
            _ = coordinator.DrainPushes(session, warmupBuffer);
        }
        setupSw.Stop();

        var samples = new List<double>(config.DurationMode ? Math.Min(config.Ticks, 4096) : config.Ticks);
        var moveSamples = new List<double>(samples.Capacity);
        var drainSamples = new List<double>(samples.Capacity);
        var publishDrainSamples = new List<double>(samples.Capacity);
        var sessionDrainSamples = new List<double>(samples.Capacity);
        var encodeSamples = new List<double>(samples.Capacity);
        var totalPushes = 0L;
        var totalDeltas = 0L;
        var totalSubscriptions = 0L;
        var totalEncodedBytes = 0L;
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var moveAllocatedBytes = 0L;
        var drainAllocatedBytes = 0L;
        var encodeAllocatedBytes = 0L;
        var runtimeBefore = RuntimeCounters.Capture();
        var runSw = Stopwatch.StartNew();
        for (var tick = 1; ShouldRunTick(config, tick, runSw.Elapsed); tick++)
        {
            var sw = Stopwatch.StartNew();
            var segmentSw = Stopwatch.StartNew();
            var segmentAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            MovePlayers(runtime, players, tick);
            moveAllocatedBytes += GC.GetTotalAllocatedBytes(precise: false) - segmentAllocatedBefore;
            segmentSw.Stop();
            moveSamples.Add(segmentSw.Elapsed.TotalMilliseconds);

            segmentSw.Restart();
            segmentAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            var firstDrainMs = 0d;
            var remainingDrainMs = 0d;
            var encodeMs = 0d;
            var pushBuffer = new List<YuoGatewayOutboundMessage>(16);
            for (var i = 0; i < sessions.Length; i++)
            {
                pushBuffer.Clear();
                var sessionSw = Stopwatch.StartNew();
                _ = coordinator.DrainPushes(sessions[i], pushBuffer);
                sessionSw.Stop();
                if (i == 0)
                {
                    firstDrainMs = sessionSw.Elapsed.TotalMilliseconds;
                }
                else
                {
                    remainingDrainMs += sessionSw.Elapsed.TotalMilliseconds;
                }

                totalPushes += pushBuffer.Count;
                totalDeltas += pushBuffer.Count(static push => push.OpCode == YuoCoreProtocolOpCodes.StateDelta);
                totalSubscriptions += pushBuffer.Count(static push => push.OpCode == YuoCoreProtocolOpCodes.StateSubscription);
                if ((config.EncodePushes || config.PooledEncodePushes) && pushBuffer.Count > 0)
                {
                    var encodeAllocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                    var encodeSw = Stopwatch.StartNew();
                    foreach (var push in pushBuffer)
                    {
                        if (config.PooledEncodePushes)
                        {
                            using var encoded = GameWebSocketEndpoint.RentServerMessage(push, useEnvelopeV1: true);
                            totalEncodedBytes += encoded.Length;
                        }
                        else
                        {
                            totalEncodedBytes += GameWebSocketEndpoint.EncodeServerMessage(push, useEnvelopeV1: true).Length;
                        }
                    }

                    encodeSw.Stop();
                    encodeMs += encodeSw.Elapsed.TotalMilliseconds;
                    encodeAllocatedBytes += GC.GetTotalAllocatedBytes(precise: false) - encodeAllocatedBefore;
                }
            }
            drainAllocatedBytes += GC.GetTotalAllocatedBytes(precise: false) - segmentAllocatedBefore;
            segmentSw.Stop();
            drainSamples.Add(segmentSw.Elapsed.TotalMilliseconds);
            publishDrainSamples.Add(firstDrainMs);
            sessionDrainSamples.Add(remainingDrainMs);
            encodeSamples.Add(encodeMs);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        runSw.Stop();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        var runtimeAfter = RuntimeCounters.Capture();
        var tickStats = BuildStats(samples);
        var moveStats = BuildStats(moveSamples);
        var drainStats = BuildStats(drainSamples);
        var publishDrainStats = BuildStats(publishDrainSamples);
        var sessionDrainStats = BuildStats(sessionDrainSamples);
        var encodeStats = BuildStats(encodeSamples);
        var passed = IsWithinThresholds(config, tickStats);
        var runtimeMetrics = RuntimeMetrics.From(runtimeBefore, runtimeAfter);
        var diagnostics = coordinator.GetDiagnostics();
        var result = new AoiStreamsResult(
            Passed: passed,
            Players: config.Players,
            Observers: sessions.Length,
            Ticks: config.Ticks,
            ActualTicks: samples.Count,
            DurationMode: config.DurationMode,
            ElapsedSeconds: runSw.Elapsed.TotalSeconds,
            TicksPerSecond: samples.Count / Math.Max(runSw.Elapsed.TotalSeconds, 0.001),
            Shards: config.Shards,
            SetupMs: setupSw.Elapsed.TotalMilliseconds,
            TickMs: tickStats,
            MoveMs: moveStats,
            DrainMs: drainStats,
            PublishDrainMs: publishDrainStats,
            SessionDrainMs: sessionDrainStats,
            EncodeMs: encodeStats,
            Pushes: totalPushes,
            Deltas: totalDeltas,
            Subscriptions: totalSubscriptions,
            EncodePushes: config.EncodePushes,
            PooledEncodePushes: config.PooledEncodePushes,
            EncodedBytes: totalEncodedBytes,
            AllocatedMb: (allocatedAfter - allocatedBefore) / 1024d / 1024d,
            MoveAllocatedMb: moveAllocatedBytes / 1024d / 1024d,
            DrainAllocatedMb: drainAllocatedBytes / 1024d / 1024d,
            EncodeAllocatedMb: encodeAllocatedBytes / 1024d / 1024d,
            Diagnostics: diagnostics,
            Runtime: runtimeMetrics,
            FailP95Ms: config.FailP95Ms,
            FailMaxMs: config.FailMaxMs);

        Console.WriteLine($"setup-ms={result.SetupMs:n3}");
        Console.WriteLine($"tick-ms: avg={tickStats.Avg:n3} p50={tickStats.P50:n3} p95={tickStats.P95:n3} max={tickStats.Max:n3}");
        Console.WriteLine($"segment-ms move: avg={moveStats.Avg:n3} p95={moveStats.P95:n3} max={moveStats.Max:n3}");
        Console.WriteLine($"segment-ms coordinator-drain: avg={drainStats.Avg:n3} p95={drainStats.P95:n3} max={drainStats.Max:n3}");
        Console.WriteLine($"segment-ms coordinator-first-session: avg={publishDrainStats.Avg:n3} p95={publishDrainStats.P95:n3} max={publishDrainStats.Max:n3}");
        Console.WriteLine($"segment-ms coordinator-remaining-sessions: avg={sessionDrainStats.Avg:n3} p95={sessionDrainStats.P95:n3} max={sessionDrainStats.Max:n3}");
        if (config.EncodePushes || config.PooledEncodePushes)
        {
            Console.WriteLine($"segment-ms websocket-encode: avg={encodeStats.Avg:n3} p95={encodeStats.P95:n3} max={encodeStats.Max:n3}");
        }
        Console.WriteLine($"pushes={totalPushes:n0} deltas={totalDeltas:n0} subscriptions={totalSubscriptions:n0}");
        Console.WriteLine($"elapsed={result.ElapsedSeconds:n3}s ticks-per-second={result.TicksPerSecond:n2}");
        Console.WriteLine($"allocated-mb={result.AllocatedMb:n2} move-allocated-mb={result.MoveAllocatedMb:n2} drain-allocated-mb={result.DrainAllocatedMb:n2} heap-after-mb={runtimeMetrics.HeapAfterMb:n2} gc0={runtimeMetrics.Gen0Collections:n0} gc1={runtimeMetrics.Gen1Collections:n0} gc2={runtimeMetrics.Gen2Collections:n0}");
        if (config.EncodePushes || config.PooledEncodePushes)
        {
            Console.WriteLine($"websocket-encoded-bytes={totalEncodedBytes:n0} encode-allocated-mb={result.EncodeAllocatedMb:n2}");
        }
        Console.WriteLine($"aoi-diagnostics dirty-entries={diagnostics.DirtyEntriesConsumed:n0} full-snapshot-passes={diagnostics.FullSnapshotPasses:n0} dirty-snapshot-passes={diagnostics.DirtySnapshotPasses:n0} snapshots-built={diagnostics.SnapshotsBuilt:n0} frames-published={diagnostics.FramesPublished:n0} deltas-published={diagnostics.DeltasPublished:n0} payload-mb={diagnostics.PayloadBytesPublished / 1024d / 1024d:n2}");
        Console.WriteLine($"aoi-diagnostics session-drains={diagnostics.SessionDrainCalls:n0} same-frame-skips={diagnostics.SameFrameSkips:n0} session-pushes={diagnostics.SessionPushesCreated:n0} session-deltas={diagnostics.SessionDeltaPushesCreated:n0} session-subscriptions={diagnostics.SessionSubscriptionPushesCreated:n0} cached-frames={diagnostics.CachedFrameCount:n0} dirty-streams={diagnostics.DirtyStreamCount:n0} session-frame-tracks={diagnostics.SessionFrameTrackCount:n0}");
        if (!passed)
        {
            Console.WriteLine($"threshold-failed: p95={tickStats.P95:n3} max={tickStats.Max:n3}");
        }

        WriteJsonResultIfRequested(config, result);
        return result;
    }

    private static Guid[] SeedPlayers(GameRuntime runtime, int players)
    {
        var playerIds = new Guid[players];
        for (var i = 0; i < players; i++)
        {
            var playerId = Guid.NewGuid();
            playerIds[i] = playerId;
            runtime.TryRestorePlayer(new YuoPlayerSnapshotDto(
                playerId,
                $"Aoi-{i}",
                Gold: 0,
                Exp: 0,
                Level: 1,
                Power: 1,
                Clicks: 0,
                UpdatedAt: DateTimeOffset.UtcNow,
                MapId: 1001,
                UnitId: 0,
                X: i % 200,
                Y: 0,
                Z: i / 200,
                Online: true,
                LastInputSeq: 1));
        }

        return playerIds;
    }

    private static void MovePlayers(GameRuntime runtime, IReadOnlyList<Guid> players, int tick)
    {
        for (var i = 0; i < players.Count; i++)
        {
            runtime.TryEnqueueMove(
                players[i],
                (i % 200) + (tick % 8) * 0.25f,
                0,
                i / 200,
                checked((ulong)(tick + 1)));
        }

        runtime.Tick();
    }

    private static bool ShouldRunTick(LoadConfig config, int nextTick, TimeSpan elapsed)
    {
        return config.DurationMode
            ? elapsed < TimeSpan.FromSeconds(config.DurationSeconds)
            : nextTick <= config.Ticks;
    }

    private static MetricStats BuildStats(IReadOnlyList<double> samples)
    {
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        var totalMs = sorted.Sum();
        return new MetricStats(
            Total: totalMs,
            Avg: totalMs / sorted.Length,
            P50: Percentile(sorted, 0.50),
            P95: Percentile(sorted, 0.95),
            Max: sorted[^1]);
    }

    private static double Percentile(double[] sortedSamples, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sortedSamples.Length) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }

    private static bool IsWithinThresholds(LoadConfig config, MetricStats tickStats)
    {
        if (config.FailP95Ms is { } p95 && tickStats.P95 > p95)
        {
            return false;
        }

        if (config.FailMaxMs is { } max && tickStats.Max > max)
        {
            return false;
        }

        return true;
    }

    private static void WriteJsonResultIfRequested(LoadConfig config, AoiStreamsResult result)
    {
        if (string.IsNullOrWhiteSpace(config.JsonOutputPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(config.JsonOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            config.JsonOutputPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"json-output={config.JsonOutputPath}");
    }
}

public sealed record RuntimeMetrics(
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double HeapAfterMb,
    long ThreadPoolThreadCount,
    long ThreadPoolPendingWorkItems,
    long ThreadPoolCompletedWorkItemsDelta,
    int ThreadPoolAvailableWorkerThreads,
    int ThreadPoolMaxWorkerThreads)
{
    public static RuntimeMetrics From(RuntimeCounters before, RuntimeCounters after)
    {
        return new RuntimeMetrics(
            after.Gen0Collections - before.Gen0Collections,
            after.Gen1Collections - before.Gen1Collections,
            after.Gen2Collections - before.Gen2Collections,
            after.TotalMemoryBytes / 1024d / 1024d,
            after.ThreadPoolThreadCount,
            after.ThreadPoolPendingWorkItems,
            after.ThreadPoolCompletedWorkItems - before.ThreadPoolCompletedWorkItems,
            after.ThreadPoolAvailableWorkerThreads,
            after.ThreadPoolMaxWorkerThreads);
    }
}

public sealed record RuntimeCounters(
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long TotalMemoryBytes,
    long ThreadPoolThreadCount,
    long ThreadPoolPendingWorkItems,
    long ThreadPoolCompletedWorkItems,
    int ThreadPoolAvailableWorkerThreads,
    int ThreadPoolMaxWorkerThreads)
{
    public static RuntimeCounters Capture()
    {
        ThreadPool.GetAvailableThreads(out var availableWorkers, out _);
        ThreadPool.GetMaxThreads(out var maxWorkers, out _);
        return new RuntimeCounters(
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetTotalMemory(forceFullCollection: false),
            ThreadPool.ThreadCount,
            ThreadPool.PendingWorkItemCount,
            ThreadPool.CompletedWorkItemCount,
            availableWorkers,
            maxWorkers);
    }
}

public sealed record LocalCompositeTickSample(
    int Tick,
    double TickMs,
    double ActorEnqueueMs,
    double RuntimeTickMs,
    double StateSyncDeltaMs,
    double AoiQueryMs,
    double AoiProjectMs,
    double AoiIndexMs,
    double AoiObserversMs,
    double PersistenceMs,
    long VisibleResults,
    long StateChanges,
    long PersistenceDrained,
    double AllocatedMbTotal);

public sealed record MetricStats(
    double Total,
    double Avg,
    double P50,
    double P95,
    double Max);

public sealed record LocalCompositeResult(
    bool Passed,
    int Players,
    int Ticks,
    int ActualTicks,
    bool DurationMode,
    double ElapsedSeconds,
    double TicksPerSecond,
    int Shards,
    int StateEntities,
    int AoiObservers,
    float AoiRadius,
    string AoiResultMode,
    string PayloadProfile,
    string PersistenceEnqueueMode,
    int PersistenceCommandsPerTick,
    MetricStats TickMs,
    IReadOnlyDictionary<string, MetricStats> SegmentsMs,
    long ActorCommands,
    long ProcessedActorCommands,
    long RuntimePlayers,
    long StateChanges,
    long VisibleResults,
    long PersistenceDrained,
    long PersistencePending,
    double AllocatedMb,
    RuntimeMetrics Runtime,
    double? FailP95Ms,
    double? FailMaxMs);

public sealed record EcsShapeResult(
    bool Passed,
    int Entities,
    int Ticks,
    int ActualTicks,
    bool DurationMode,
    double ElapsedSeconds,
    double TicksPerSecond,
    int Shards,
    int ComponentTypes,
    int SystemTypes,
    double SetupMs,
    MetricStats TickMs,
    double AllocatedMb,
    RuntimeMetrics Runtime,
    double? FailP95Ms,
    double? FailMaxMs);

public sealed record AoiStreamsResult(
    bool Passed,
    int Players,
    int Observers,
    int Ticks,
    int ActualTicks,
    bool DurationMode,
    double ElapsedSeconds,
    double TicksPerSecond,
    int Shards,
    double SetupMs,
    MetricStats TickMs,
    MetricStats MoveMs,
    MetricStats DrainMs,
    MetricStats PublishDrainMs,
    MetricStats SessionDrainMs,
    MetricStats EncodeMs,
    long Pushes,
    long Deltas,
    long Subscriptions,
    bool EncodePushes,
    bool PooledEncodePushes,
    long EncodedBytes,
    double AllocatedMb,
    double MoveAllocatedMb,
    double DrainAllocatedMb,
    double EncodeAllocatedMb,
    GameAoiStreamPushDiagnostics Diagnostics,
    RuntimeMetrics Runtime,
    double? FailP95Ms,
    double? FailMaxMs);

public static class WebSocketLoadRunner
{
    public static async Task RunAsync(LoadConfig config)
    {
        Console.WriteLine($"load: url={config.Url} players={config.Players} duration={config.DurationSeconds}s interval={config.ClickIntervalMs}ms");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.DurationSeconds));
        var sent = 0L;
        var received = 0L;
        var errors = 0L;
        var sw = Stopwatch.StartNew();
        var ramp = new SemaphoreSlim(config.ConnectConcurrency);

        var tasks = Enumerable.Range(0, config.Players).Select(async _ =>
        {
            await ramp.WaitAsync(cts.Token);
            try
            {
                await RunClientAsync(config, () => Interlocked.Increment(ref sent), () => Interlocked.Increment(ref received), () => Interlocked.Increment(ref errors), cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
            finally
            {
                ramp.Release();
            }
        }).ToArray();

        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                Console.WriteLine($"t={sw.Elapsed.TotalSeconds:n0}s sent={sent:n0} recv={received:n0} errors={errors:n0}");
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"done sent={sent:n0} recv={received:n0} errors={errors:n0} elapsed={sw.Elapsed}");
    }

    private static async Task RunClientAsync(LoadConfig config, Action sent, Action received, Action error, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("yuocore.v1");
        await socket.ConnectAsync(new Uri(config.Url), cancellationToken);

        await SendAsync(socket, new ClientEnvelope { RequestId = 1, Hello = new HelloRequest() }, sent, cancellationToken);
        var welcome = await ReceiveAsync(socket, received, cancellationToken);
        var playerId = welcome.Welcome?.PlayerId;
        if (string.IsNullOrWhiteSpace(playerId)) throw new InvalidOperationException("welcome did not include player id");

        ulong requestId = 2;
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await SendAsync(socket, new ClientEnvelope
            {
                RequestId = requestId++,
                Click = new ClickRequest { PlayerId = playerId, Amount = 1 }
            }, sent, cancellationToken);

            var response = await ReceiveAsync(socket, received, cancellationToken);
            if (response.PayloadCase == ServerEnvelope.PayloadOneofCase.Error) error();
            await Task.Delay(config.ClickIntervalMs, cancellationToken);
        }
    }

    private static async Task SendAsync(ClientWebSocket socket, ClientEnvelope envelope, Action sent, CancellationToken cancellationToken)
    {
        var bytes = envelope.ToByteArray();
        await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, cancellationToken);
        sent();
    }

    private static async Task<ServerEnvelope> ReceiveAsync(ClientWebSocket socket, Action received, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("closed");
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                received();
                return ServerEnvelope.Parser.ParseFrom(stream.ToArray());
            }
        }
    }
}

public sealed class LoadConfig
{
    public string Mode { get; set; } = "ws";
    public string Url { get; set; } = "ws://localhost:8080/ws";
    public int Players { get; set; } = 10_000;
    public int DurationSeconds { get; set; } = 60;
    public int ClickIntervalMs { get; set; } = 1_000;
    public int ConnectConcurrency { get; set; } = 200;
    public int Ticks { get; set; } = 20;
    public int Shards { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int StateEntities { get; set; } = 10_000;
    public int AoiObservers { get; set; } = 256;
    public float AoiRadius { get; set; } = 24;
    public string AoiResultMode { get; set; } = "sorted";
    public string PayloadProfile { get; set; } = "allocated";
    public string PersistenceEnqueueMode { get; set; } = "single";
    public int PersistenceCommandsPerTick { get; set; } = 10_000;
    public string? JsonOutputPath { get; set; }
    public string? CsvOutputPath { get; set; }
    public double? FailP95Ms { get; set; }
    public double? FailMaxMs { get; set; }
    public bool DurationMode { get; set; }
    public bool EncodePushes { get; set; }
    public bool PooledEncodePushes { get; set; }

    public static LoadConfig Parse(string[] args)
    {
        var config = new LoadConfig();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode": config.Mode = args[++i]; break;
                case "--url": config.Url = args[++i]; break;
                case "--players": config.Players = int.Parse(args[++i]); break;
                case "--duration": config.DurationSeconds = int.Parse(args[++i]); break;
                case "--interval": config.ClickIntervalMs = int.Parse(args[++i]); break;
                case "--connect-concurrency": config.ConnectConcurrency = int.Parse(args[++i]); break;
                case "--ticks": config.Ticks = int.Parse(args[++i]); break;
                case "--shards": config.Shards = int.Parse(args[++i]); break;
                case "--state-entities": config.StateEntities = int.Parse(args[++i]); break;
                case "--aoi-observers": config.AoiObservers = int.Parse(args[++i]); break;
                case "--aoi-radius": config.AoiRadius = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--aoi-result-mode": config.AoiResultMode = args[++i]; break;
                case "--payload-profile": config.PayloadProfile = args[++i]; break;
                case "--persistence-enqueue-mode": config.PersistenceEnqueueMode = args[++i]; break;
                case "--persistence-per-tick": config.PersistenceCommandsPerTick = int.Parse(args[++i]); break;
                case "--json-output": config.JsonOutputPath = args[++i]; break;
                case "--csv-output": config.CsvOutputPath = args[++i]; break;
                case "--fail-p95-ms": config.FailP95Ms = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--fail-max-ms": config.FailMaxMs = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--duration-mode": config.DurationMode = true; break;
                case "--encode-pushes": config.EncodePushes = true; break;
                case "--pooled-encode-pushes": config.PooledEncodePushes = true; break;
                default: throw new ArgumentException($"unknown argument: {args[i]}");
            }
        }

        Validate(config);
        return config;
    }

    private static void Validate(LoadConfig config)
    {
        if (config.Players <= 0) throw new ArgumentOutOfRangeException(nameof(Players), "players must be greater than zero.");
        if (config.DurationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(DurationSeconds), "duration must be greater than zero.");
        if (config.ClickIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(ClickIntervalMs), "interval must be greater than zero.");
        if (config.ConnectConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(ConnectConcurrency), "connect concurrency must be greater than zero.");
        if (config.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(Ticks), "ticks must be greater than zero.");
        if (config.Shards <= 0) throw new ArgumentOutOfRangeException(nameof(Shards), "shards must be greater than zero.");
        if (config.StateEntities <= 0) throw new ArgumentOutOfRangeException(nameof(StateEntities), "state entities must be greater than zero.");
        if (config.AoiObservers <= 0) throw new ArgumentOutOfRangeException(nameof(AoiObservers), "aoi observers must be greater than zero.");
        if (!float.IsFinite(config.AoiRadius) || config.AoiRadius <= 0) throw new ArgumentOutOfRangeException(nameof(AoiRadius), "aoi radius must be finite and positive.");
        if (!string.Equals(config.AoiResultMode, "sorted", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(config.AoiResultMode, "count", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(AoiResultMode), "aoi result mode must be sorted or count.");
        }
        if (!string.Equals(config.PayloadProfile, "allocated", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(config.PayloadProfile, "shared-input", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(PayloadProfile), "payload profile must be allocated or shared-input.");
        }
        if (!string.Equals(config.PersistenceEnqueueMode, "single", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(config.PersistenceEnqueueMode, "batch", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(PersistenceEnqueueMode), "persistence enqueue mode must be single or batch.");
        }
        if (config.PersistenceCommandsPerTick <= 0) throw new ArgumentOutOfRangeException(nameof(PersistenceCommandsPerTick), "persistence commands per tick must be greater than zero.");
        if (config.FailP95Ms is <= 0) throw new ArgumentOutOfRangeException(nameof(FailP95Ms), "fail p95 threshold must be positive.");
        if (config.FailMaxMs is <= 0) throw new ArgumentOutOfRangeException(nameof(FailMaxMs), "fail max threshold must be positive.");
    }
}
