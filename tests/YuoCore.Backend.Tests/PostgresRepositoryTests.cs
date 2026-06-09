using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Game;
using YuoCore.Persistence;
using YuoCore.Persistence.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace YuoCore.Backend.Tests;

public sealed class PostgresRepositoryTests
{
    [Fact]
    public async Task Repository_round_trips_snapshot_when_testcontainers_enabled()
    {
        if (!ShouldRunTestcontainers()) return;

        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("yuocore")
            .WithUsername("yuocore")
            .WithPassword("yuocore")
            .Build();

        await postgres.StartAsync();
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await CreatePlayerSnapshotsSchemaAsync(dataSource);

        var repo = new PostgresPlayerSnapshotRepository(dataSource);
        var snapshot = new YuoPlayerSnapshotDto(
            Guid.NewGuid(),
            "db",
            10,
            20,
            2,
            3,
            4,
            DateTimeOffset.UtcNow,
            MapId: 1001,
            UnitId: 2002,
            X: 1.5f,
            Y: 2.5f,
            Z: 3.5f,
            Online: true,
            LastInputSeq: 7);
        await repo.SaveAsync(snapshot);

        var loaded = await repo.GetAsync(snapshot.PlayerId);
        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Name, loaded.Name);
        Assert.Equal(snapshot.Gold, loaded.Gold);
        Assert.Equal(snapshot.MapId, loaded.MapId);
        Assert.Equal(snapshot.UnitId, loaded.UnitId);
        Assert.Equal(snapshot.X, loaded.X);
        Assert.Equal(snapshot.Z, loaded.Z);
        Assert.True(loaded.Online);
        Assert.Equal(7UL, loaded.LastInputSeq);
    }

    [Fact]
    public async Task Snapshot_persistence_uses_redis_cache_and_falls_back_to_postgres_when_testcontainers_enabled()
    {
        if (!ShouldRunTestcontainers()) return;

        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("yuocore")
            .WithUsername("yuocore")
            .WithPassword("yuocore")
            .Build();
        await using var redis = new RedisBuilder("redis:8-alpine").Build();

        await postgres.StartAsync();
        await redis.StartAsync();

        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        await CreatePlayerSnapshotsSchemaAsync(dataSource);

        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        var repository = new PostgresPlayerSnapshotRepository(dataSource);
        var cache = new RedisPlayerSnapshotCache(multiplexer);
        var persistence = new SnapshotPersistenceService(repository, cache);

        var playerId = Guid.NewGuid();
        var saved = CreateSnapshot(playerId, "saved", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 9);
        await persistence.SaveAsync(saved);

        var cachedAfterSave = await cache.GetAsync(playerId);
        Assert.NotNull(cachedAfterSave);
        Assert.Equal("saved", cachedAfterSave.Name);

        await DeleteRedisSnapshotAsync(multiplexer, playerId);
        var savedFromRepository = await persistence.GetAsync(playerId);
        Assert.NotNull(savedFromRepository);
        Assert.Equal("saved", savedFromRepository.Name);
        Assert.Equal(1001, savedFromRepository.MapId);
        Assert.Equal(9UL, savedFromRepository.LastInputSeq);

        var databaseVersion = CreateSnapshot(playerId, "database", gold: 20, mapId: 2002, x: 3, z: 4, inputSeq: 10);
        var cacheVersion = CreateSnapshot(playerId, "cache", gold: 30, mapId: 3003, x: 5, z: 6, inputSeq: 11);
        await repository.SaveAsync(databaseVersion);
        await cache.SetAsync(cacheVersion);

        var loadedFromCache = await persistence.GetAsync(playerId);
        Assert.NotNull(loadedFromCache);
        Assert.Equal("cache", loadedFromCache.Name);
        Assert.Equal(3003, loadedFromCache.MapId);
        Assert.Equal(11UL, loadedFromCache.LastInputSeq);

        await DeleteRedisSnapshotAsync(multiplexer, playerId);
        var loadedFromPostgres = await persistence.GetAsync(playerId);
        Assert.NotNull(loadedFromPostgres);
        Assert.Equal("database", loadedFromPostgres.Name);
        Assert.Equal(2002, loadedFromPostgres.MapId);
        Assert.Equal(10UL, loadedFromPostgres.LastInputSeq);
    }

    [Fact]
    public async Task Durable_outbox_background_service_writes_to_postgres_and_replays_unacked_command_when_testcontainers_enabled()
    {
        if (!ShouldRunTestcontainers()) return;

        var outboxDirectory = Path.Combine(Path.GetTempPath(), $"yuocore-outbox-{Guid.NewGuid():N}");
        try
        {
            await using var postgres = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("yuocore")
                .WithUsername("yuocore")
                .WithPassword("yuocore")
                .Build();
            await using var redis = new RedisBuilder("redis:8-alpine").Build();

            await postgres.StartAsync();
            await redis.StartAsync();

            await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
            await CreatePlayerSnapshotsSchemaAsync(dataSource);
            await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());

            var repository = new PostgresPlayerSnapshotRepository(dataSource);
            var cache = new RedisPlayerSnapshotCache(multiplexer);
            var snapshotStore = new PlayerSnapshotStoreAdapter(repository, cache);
            var eventStore = new NoopYuoEventStore();
            var options = Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1));

            var queue = new YuoPersistenceQueue();
            var outbox = new YuoFilePersistenceOutbox(outboxDirectory, "testcontainers");
            var durable = new YuoDurablePersistenceQueue(queue, outbox);
            var first = CreateSnapshot(Guid.NewGuid(), "first", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 7);
            using (var service = new YuoPersistenceBackgroundService(
                       queue,
                       snapshotStore,
                       eventStore,
                       options,
                       failureSink: null,
                       outbox))
            {
                await service.StartAsync(CancellationToken.None);

                await durable.EnqueueAsync(new SaveSnapshotCommand(PlayerSnapshotPersistenceCodec.ToRecord(first)));

                var saved = await WaitForSnapshotAsync(repository, first.PlayerId, "first");
                Assert.Equal(1001, saved.MapId);
                Assert.Equal(7UL, saved.LastInputSeq);

                await service.StopAsync(CancellationToken.None);
            }

            Assert.Empty(await ReadReplayAsync(new YuoFilePersistenceOutbox(outboxDirectory, "testcontainers")));
            var firstCache = await cache.GetAsync(first.PlayerId);
            Assert.NotNull(firstCache);
            Assert.Equal("first", firstCache.Name);
            Assert.Equal(1001, firstCache.MapId);

            var replayedSnapshot = CreateSnapshot(
                Guid.NewGuid(),
                "replayed",
                gold: 20,
                mapId: 2002,
                x: 3,
                z: 4,
                inputSeq: 8);
            var staleCache = replayedSnapshot with
            {
                Name = "stale-cache",
                MapId = 9999,
                LastInputSeq = 1
            };
            await cache.SetAsync(staleCache);
            await new YuoFilePersistenceOutbox(outboxDirectory, "testcontainers")
                .AppendAsync(new SaveSnapshotCommand(PlayerSnapshotPersistenceCodec.ToRecord(replayedSnapshot)));

            var replayQueue = new YuoPersistenceQueue();
            var replayOutbox = new YuoFilePersistenceOutbox(outboxDirectory, "testcontainers");
            var replayDurable = new YuoDurablePersistenceQueue(replayQueue, replayOutbox);
            Assert.Equal(1, await replayDurable.ReplayPendingAsync());

            using (var replayService = new YuoPersistenceBackgroundService(
                       replayQueue,
                       snapshotStore,
                       eventStore,
                       options,
                       failureSink: null,
                       replayOutbox))
            {
                await replayService.StartAsync(CancellationToken.None);

                var saved = await WaitForSnapshotAsync(repository, replayedSnapshot.PlayerId, "replayed");
                Assert.Equal(2002, saved.MapId);
                Assert.Equal(8UL, saved.LastInputSeq);

                await replayService.StopAsync(CancellationToken.None);
            }

            var refreshedCache = await cache.GetAsync(replayedSnapshot.PlayerId);
            Assert.NotNull(refreshedCache);
            Assert.Equal("replayed", refreshedCache.Name);
            Assert.Equal(2002, refreshedCache.MapId);
            Assert.Equal(8UL, refreshedCache.LastInputSeq);
            Assert.Empty(await ReadReplayAsync(new YuoFilePersistenceOutbox(outboxDirectory, "testcontainers")));
        }
        finally
        {
            if (Directory.Exists(outboxDirectory))
            {
                Directory.Delete(outboxDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Snapshot_store_adapter_logs_cache_refresh_failure_without_failing_durable_save()
    {
        var repository = new RecordingPlayerSnapshotRepository();
        var cache = new ThrowingPlayerSnapshotCache();
        var logger = new RecordingLogger<PlayerSnapshotStoreAdapter>();
        var adapter = new PlayerSnapshotStoreAdapter(repository, cache, logger);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "cache-failure", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 3);

        await adapter.SaveAsync(PlayerSnapshotPersistenceCodec.ToRecord(snapshot));

        Assert.NotNull(repository.Saved);
        Assert.Equal(snapshot.PlayerId, repository.Saved.PlayerId);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Failed to refresh player snapshot cache", entry.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task Snapshot_persistence_service_saves_repository_before_best_effort_cache_refresh()
    {
        var repository = new RecordingPlayerSnapshotRepository();
        var cache = new ThrowingPlayerSnapshotCache();
        var logger = new RecordingLogger<SnapshotPersistenceService>();
        var persistence = new SnapshotPersistenceService(repository, cache, logger: logger);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "repository-first", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 3);

        await persistence.SaveAsync(snapshot);

        Assert.NotNull(repository.Saved);
        Assert.Equal(snapshot.PlayerId, repository.Saved.PlayerId);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Failed to refresh player snapshot cache", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_persistence_service_enqueues_durable_outbox_before_best_effort_cache_refresh()
    {
        var outboxDirectory = Path.Combine(Path.GetTempPath(), $"yuocore-durable-cache-failure-{Guid.NewGuid():N}");
        try
        {
            var repository = new RecordingPlayerSnapshotRepository();
            var queue = new YuoPersistenceQueue();
            var outbox = new YuoFilePersistenceOutbox(outboxDirectory, "cache-failure");
            var durable = new YuoDurablePersistenceQueue(queue, outbox);
            var cache = new ThrowingPlayerSnapshotCache();
            var logger = new RecordingLogger<SnapshotPersistenceService>();
            var persistence = new SnapshotPersistenceService(repository, cache, durable, logger);
            var snapshot = CreateSnapshot(Guid.NewGuid(), "durable-first", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 3);

            await persistence.SaveAsync(snapshot);

            Assert.Null(repository.Saved);
            var queued = Assert.IsType<YuoOutboxPersistenceCommand>(await queue.DequeueAsync());
            var save = Assert.IsType<SaveSnapshotCommand>(queued.Inner);
            Assert.Equal(snapshot.PlayerId.ToString("N"), save.Snapshot.Id);
            var replay = Assert.Single(await ReadReplayAsync(new YuoFilePersistenceOutbox(outboxDirectory, "cache-failure")));
            Assert.Equal(queued.OutboxSequence, replay.Sequence);
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
        }
        finally
        {
            if (Directory.Exists(outboxDirectory))
            {
                Directory.Delete(outboxDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Snapshot_persistence_service_falls_back_to_repository_when_cache_read_fails()
    {
        var snapshot = CreateSnapshot(Guid.NewGuid(), "repository-fallback", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 3);
        var repository = new RecordingPlayerSnapshotRepository { Saved = snapshot };
        var cache = new ThrowingPlayerSnapshotCache(throwOnSet: false, throwOnGet: true);
        var logger = new RecordingLogger<SnapshotPersistenceService>();
        var persistence = new SnapshotPersistenceService(repository, cache, logger: logger);

        var loaded = await persistence.GetAsync(snapshot.PlayerId);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.PlayerId, loaded.PlayerId);
        Assert.Equal("repository-fallback", loaded.Name);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("falling back to repository", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_persistence_service_treats_post_commit_cache_cancellation_as_best_effort()
    {
        var repository = new RecordingPlayerSnapshotRepository();
        var cache = new CancelingPlayerSnapshotCache();
        var logger = new RecordingLogger<SnapshotPersistenceService>();
        var persistence = new SnapshotPersistenceService(repository, cache, logger: logger);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "cache-canceled", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 3);

        await persistence.SaveAsync(snapshot);

        Assert.NotNull(repository.Saved);
        Assert.Equal(snapshot.PlayerId, repository.Saved.PlayerId);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<OperationCanceledException>(entry.Exception);
    }

    [Fact]
    public async Task Snapshot_store_adapter_treats_post_commit_cache_cancellation_as_best_effort()
    {
        var repository = new RecordingPlayerSnapshotRepository();
        var cache = new CancelingPlayerSnapshotCache();
        var logger = new RecordingLogger<PlayerSnapshotStoreAdapter>();
        var adapter = new PlayerSnapshotStoreAdapter(repository, cache, logger);
        var snapshot = CreateSnapshot(Guid.NewGuid(), "adapter-cache-canceled", gold: 10, mapId: 1001, x: 1, z: 2, inputSeq: 3);

        await adapter.SaveAsync(PlayerSnapshotPersistenceCodec.ToRecord(snapshot));

        Assert.NotNull(repository.Saved);
        Assert.Equal(snapshot.PlayerId, repository.Saved.PlayerId);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<OperationCanceledException>(entry.Exception);
    }

    private static bool ShouldRunTestcontainers()
    {
        return Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS") == "1";
    }

    private static async Task CreatePlayerSnapshotsSchemaAsync(NpgsqlDataSource dataSource)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            create table player_snapshots (
              player_id uuid primary key,
              name text not null,
              gold bigint not null,
              exp bigint not null,
              level integer not null,
              power bigint not null,
              clicks bigint not null,
              updated_at timestamptz not null,
              map_id bigint not null default 0,
              unit_id bigint not null default 0,
              x real not null default 0,
              y real not null default 0,
              z real not null default 0,
              online boolean not null default false,
              last_input_seq numeric(20,0) not null default 0
            );
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static YuoPlayerSnapshotDto CreateSnapshot(
        Guid playerId,
        string name,
        long gold,
        long mapId,
        float x,
        float z,
        ulong inputSeq)
    {
        return new YuoPlayerSnapshotDto(
            playerId,
            name,
            gold,
            Exp: gold + 1,
            Level: 2,
            Power: 3,
            Clicks: 4,
            UpdatedAt: DateTimeOffset.UtcNow,
            MapId: mapId,
            UnitId: mapId + 1,
            X: x,
            Y: 0,
            Z: z,
            Online: true,
            LastInputSeq: inputSeq);
    }

    private static async Task DeleteRedisSnapshotAsync(IConnectionMultiplexer multiplexer, Guid playerId)
    {
        await multiplexer.GetDatabase().KeyDeleteAsync($"player:snapshot:{playerId:N}");
    }

    private static async Task<YuoPlayerSnapshotDto> WaitForSnapshotAsync(
        IPlayerSnapshotRepository repository,
        Guid playerId,
        string expectedName)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var snapshot = await repository.GetAsync(playerId);
            if (snapshot?.Name == expectedName)
            {
                return snapshot;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting for player snapshot {playerId:N} with name {expectedName}.");
    }

    private static async Task<IReadOnlyList<YuoPersistenceOutboxEntry>> ReadReplayAsync(IYuoPersistenceOutbox outbox)
    {
        var entries = new List<YuoPersistenceOutboxEntry>();
        await foreach (var entry in outbox.ReplayAsync())
        {
            entries.Add(entry);
        }

        return entries;
    }

    private sealed class RecordingPlayerSnapshotRepository : IPlayerSnapshotRepository
    {
        public YuoPlayerSnapshotDto? Saved { get; set; }

        public Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            Saved = snapshot;
            return Task.CompletedTask;
        }

        public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Saved?.PlayerId == playerId ? Saved : null);
        }
    }

    private sealed class ThrowingPlayerSnapshotCache(
        bool throwOnSet = true,
        bool throwOnGet = false) : IPlayerSnapshotCache
    {
        public Task SetAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            if (throwOnSet)
            {
                throw new InvalidOperationException("expected cache refresh failure.");
            }

            return Task.CompletedTask;
        }

        public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
        {
            if (throwOnGet)
            {
                throw new InvalidOperationException("expected cache read failure.");
            }

            return Task.FromResult<YuoPlayerSnapshotDto?>(null);
        }
    }

    private sealed class CancelingPlayerSnapshotCache : IPlayerSnapshotCache
    {
        public Task SetAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<YuoPlayerSnapshotDto?>(null);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
