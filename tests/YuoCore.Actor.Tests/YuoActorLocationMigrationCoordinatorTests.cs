using YuoCore.Abstractions;
using YuoCore.Location;

namespace YuoCore.Actor.Tests;

public sealed class YuoActorLocationMigrationCoordinatorTests
{
    [Fact]
    public async Task Migrate_invalidates_registered_sender_cache()
    {
        var key = new YuoLocationKey("Player", "move-cache");
        var oldActorId = new ActorId(9101);
        var newActorId = new ActorId(9102);
        var oldAddress = new YuoActorAddress("local", "game", 91, oldActorId.Value);
        var newAddress = new YuoActorAddress("local", "game", 92, newActorId.Value);
        var locations = new InMemoryYuoLocationRegistry();
        var transport = new InMemoryActorTransport();
        var oldCalls = 0;
        var newCalls = 0;

        await locations.RegisterAsync(key, oldAddress);

        await using var oldFiber = new ActorFiber(
            91,
            new DelegateActorMessageDispatcher((_, _) =>
            {
                oldCalls++;
                return ValueTask.FromResult<object?>(1L);
            }));
        await using var newFiber = new ActorFiber(
            92,
            new DelegateActorMessageDispatcher((_, _) =>
            {
                newCalls++;
                return ValueTask.FromResult<object?>(2L);
            }));

        transport.RegisterEndpoint(new ActorAddress("local", 91, oldActorId), oldFiber);
        transport.RegisterEndpoint(new ActorAddress("local", 92, newActorId), newFiber);
        oldFiber.Start();
        newFiber.Start();

        var sender = new YuoActorLocationSender(
            locations,
            transport,
            new YuoActorLocationSenderOptions(MaxStaleRetries: 0));
        Assert.Equal(1L, await sender.InvokeAsync<long>(key, target => new GetCounter(target)));

        var coordinator = new YuoActorLocationMigrationCoordinator(locations, [sender]);
        var migrated = await coordinator.MigrateAsync(key, "move", oldAddress, newAddress);

        Assert.True(migrated.IsSuccess);
        Assert.Equal(2L, await sender.InvokeAsync<long>(key, target => new GetCounter(target)));
        Assert.Equal(1, oldCalls);
        Assert.Equal(1, newCalls);
    }

    [Fact]
    public async Task Migrate_invalidates_sender_cache_immediately_after_lock()
    {
        var key = new YuoLocationKey("Player", "move-cache-while-locked");
        var oldActorId = new ActorId(9201);
        var newActorId = new ActorId(9202);
        var oldAddress = new YuoActorAddress("local", "game", 101, oldActorId.Value);
        var newAddress = new YuoActorAddress("local", "game", 102, newActorId.Value);
        var locations = new BlockingMigrationLocationRegistry(key, oldAddress, newAddress);
        var transport = new InMemoryActorTransport();
        var oldCalls = 0;
        var newCalls = 0;

        await using var oldFiber = new ActorFiber(
            101,
            new DelegateActorMessageDispatcher((_, _) =>
            {
                oldCalls++;
                return ValueTask.FromResult<object?>(1L);
            }));
        await using var newFiber = new ActorFiber(
            102,
            new DelegateActorMessageDispatcher((_, _) =>
            {
                newCalls++;
                return ValueTask.FromResult<object?>(2L);
            }));

        transport.RegisterEndpoint(new ActorAddress("local", 101, oldActorId), oldFiber);
        transport.RegisterEndpoint(new ActorAddress("local", 102, newActorId), newFiber);
        oldFiber.Start();
        newFiber.Start();

        var sender = new YuoActorLocationSender(
            locations,
            transport,
            new YuoActorLocationSenderOptions(
                MaxStaleRetries: 0,
                MaxLockedResolveRetries: 20,
                LockedResolveRetryDelay: TimeSpan.FromMilliseconds(10)));
        Assert.Equal(1L, await sender.InvokeAsync<long>(key, target => new GetCounter(target)));

        var coordinator = new YuoActorLocationMigrationCoordinator(locations, [sender]);
        var migration = coordinator.MigrateAsync(key, "move", oldAddress, newAddress).AsTask();
        await locations.LockAcquired.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var invokeDuringMigration = sender.InvokeAsync<long>(key, target => new GetCounter(target)).AsTask();
        await Task.Delay(50);

        Assert.False(invokeDuringMigration.IsCompleted);
        Assert.Equal(1, oldCalls);

        locations.AllowMigration.SetResult();
        var migrated = await migration;
        var result = await invokeDuringMigration;

        Assert.True(migrated.IsSuccess);
        Assert.Equal(2L, result);
        Assert.Equal(1, oldCalls);
        Assert.Equal(1, newCalls);
    }

    [Fact]
    public async Task Migrate_unlocks_location_when_expected_address_mismatches()
    {
        var key = new YuoLocationKey("Player", "move-fail");
        var oldAddress = new YuoActorAddress("local", "game", 93, 9301);
        var wrongExpectedAddress = new YuoActorAddress("local", "game", 94, 9401);
        var newAddress = new YuoActorAddress("local", "game", 95, 9501);
        var locations = new InMemoryYuoLocationRegistry();

        await locations.RegisterAsync(key, oldAddress);

        var coordinator = new YuoActorLocationMigrationCoordinator(locations);
        var migrated = await coordinator.MigrateAsync(key, "move", wrongExpectedAddress, newAddress);
        var resolved = await locations.ResolveAsync(key);

        Assert.False(migrated.IsSuccess);
        Assert.NotNull(resolved);
        Assert.False(resolved.IsLocked);
        Assert.Equal(oldAddress, resolved.Address);
    }

    [Fact]
    public async Task Migrate_uses_lock_token_for_migration()
    {
        var key = new YuoLocationKey("Player", "move-token");
        var oldAddress = new YuoActorAddress("local", "game", 96, 9601);
        var newAddress = new YuoActorAddress("local", "game", 97, 9701);
        var locations = new TokenRequiredLocationRegistry(key, oldAddress, newAddress);

        var coordinator = new YuoActorLocationMigrationCoordinator(locations);
        var migrated = await coordinator.MigrateAsync(key, "move", oldAddress, newAddress);

        Assert.True(migrated.IsSuccess);
        Assert.Equal("token-1", locations.MigrateToken);
        Assert.Null(locations.UnlockToken);
    }

    [Fact]
    public async Task Migrate_uses_lock_token_when_unlocking_after_failure()
    {
        var key = new YuoLocationKey("Player", "move-token-fail");
        var oldAddress = new YuoActorAddress("local", "game", 98, 9801);
        var newAddress = new YuoActorAddress("local", "game", 99, 9901);
        var locations = new TokenRequiredLocationRegistry(key, oldAddress, newAddress)
        {
            ForceMigrationFailure = true
        };

        var coordinator = new YuoActorLocationMigrationCoordinator(locations);
        var migrated = await coordinator.MigrateAsync(key, "move", oldAddress, newAddress);

        Assert.False(migrated.IsSuccess);
        Assert.Equal("token-1", locations.MigrateToken);
        Assert.Equal("token-1", locations.UnlockToken);
    }

    [Fact]
    public async Task Migrate_applies_default_lock_ttl_when_none_given()
    {
        var key = new YuoLocationKey("Player", "move-default-ttl");
        var oldAddress = new YuoActorAddress("local", "game", 96, 9601);
        var newAddress = new YuoActorAddress("local", "game", 97, 9701);
        var locations = new TokenRequiredLocationRegistry(key, oldAddress, newAddress);

        var coordinator = new YuoActorLocationMigrationCoordinator(locations);
        var migrated = await coordinator.MigrateAsync(key, "move", oldAddress, newAddress);

        Assert.True(migrated.IsSuccess);
        Assert.Equal(YuoActorLocationMigrationCoordinator.DefaultLockTtl, locations.LastLockTtl);
    }

    [Fact]
    public async Task Migrate_passes_explicit_lock_ttl()
    {
        var key = new YuoLocationKey("Player", "move-explicit-ttl");
        var oldAddress = new YuoActorAddress("local", "game", 96, 9601);
        var newAddress = new YuoActorAddress("local", "game", 97, 9701);
        var locations = new TokenRequiredLocationRegistry(key, oldAddress, newAddress);

        var coordinator = new YuoActorLocationMigrationCoordinator(locations);
        var ttl = TimeSpan.FromSeconds(7);
        var migrated = await coordinator.MigrateAsync(key, "move", oldAddress, newAddress, ttl);

        Assert.True(migrated.IsSuccess);
        Assert.Equal(ttl, locations.LastLockTtl);
    }

    private sealed record GetCounter(ActorId TargetActorId) : IActorRequest<long>;

    private sealed class BlockingMigrationLocationRegistry(
        YuoLocationKey expectedKey,
        YuoActorAddress oldAddress,
        YuoActorAddress newAddress) : IYuoLocationRegistry
    {
        private const string LockToken = "blocking-token";
        private readonly object _gate = new();
        private bool _locked;
        private bool _migrated;

        public TaskCompletionSource LockAcquired { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowMigration { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<YuoResult<YuoLocationEntry>> RegisterAsync(
            YuoLocationKey key,
            YuoActorAddress address,
            long expectedVersion = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoLocationEntry?> ResolveAsync(
            YuoLocationKey key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(expectedKey, key);

            lock (_gate)
            {
                return ValueTask.FromResult<YuoLocationEntry?>(_migrated
                    ? Entry(newAddress, isLocked: false, version: 2)
                    : Entry(oldAddress, _locked, version: 1));
            }
        }

        public ValueTask<YuoResult<YuoLocationEntry>> LockAsync(
            YuoLocationKey key,
            string owner,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(expectedKey, key);
            Assert.Equal("move", owner);

            lock (_gate)
            {
                _locked = true;
            }

            LockAcquired.TrySetResult();
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(
                Entry(oldAddress, isLocked: true, version: 1)));
        }

        public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
            YuoLocationKey key,
            string owner,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Coordinator must use tokenized unlock.");
        }

        public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
            YuoLocationKey key,
            string owner,
            string lockToken,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
            YuoLocationKey key,
            string owner,
            YuoActorAddress expectedAddress,
            YuoActorAddress migratedAddress,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Coordinator must use tokenized migration.");
        }

        public async ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
            YuoLocationKey key,
            string owner,
            string lockToken,
            YuoActorAddress expectedAddress,
            YuoActorAddress migratedAddress,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedKey, key);
            Assert.Equal("move", owner);
            Assert.Equal(LockToken, lockToken);
            Assert.Equal(oldAddress, expectedAddress);
            Assert.Equal(newAddress, migratedAddress);

            await AllowMigration.Task.WaitAsync(cancellationToken);
            lock (_gate)
            {
                _locked = false;
                _migrated = true;
            }

            return YuoResult<YuoLocationEntry>.Ok(Entry(newAddress, isLocked: false, version: 2));
        }

        public ValueTask<bool> RemoveAsync(
            YuoLocationKey key,
            long expectedVersion = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private YuoLocationEntry Entry(
            YuoActorAddress address,
            bool isLocked,
            long version)
        {
            return new YuoLocationEntry(
                expectedKey,
                address,
                version,
                isLocked ? "move" : null,
                isLocked ? DateTimeOffset.UtcNow.AddMinutes(1) : null,
                DateTimeOffset.UtcNow)
            {
                LockToken = isLocked ? LockToken : null
            };
        }
    }

    private sealed class TokenRequiredLocationRegistry(
        YuoLocationKey expectedKey,
        YuoActorAddress oldAddress,
        YuoActorAddress newAddress) : IYuoLocationRegistry
    {
        private const string LockToken = "token-1";

        public string? MigrateToken { get; private set; }

        public string? UnlockToken { get; private set; }

        public TimeSpan? LastLockTtl { get; private set; }

        public bool ForceMigrationFailure { get; init; }

        public ValueTask<YuoResult<YuoLocationEntry>> RegisterAsync(
            YuoLocationKey key,
            YuoActorAddress address,
            long expectedVersion = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoLocationEntry?> ResolveAsync(
            YuoLocationKey key,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoResult<YuoLocationEntry>> LockAsync(
            YuoLocationKey key,
            string owner,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedKey, key);
            Assert.Equal("move", owner);
            LastLockTtl = ttl;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(new YuoLocationEntry(
                key,
                oldAddress,
                1,
                owner,
                DateTimeOffset.UtcNow.AddMinutes(1),
                DateTimeOffset.UtcNow)
            {
                LockToken = LockToken
            }));
        }

        public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
            YuoLocationKey key,
            string owner,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Coordinator must use tokenized unlock.");
        }

        public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
            YuoLocationKey key,
            string owner,
            string lockToken,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedKey, key);
            UnlockToken = lockToken;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(new YuoLocationEntry(
                key,
                oldAddress,
                2,
                null,
                null,
                DateTimeOffset.UtcNow)));
        }

        public ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
            YuoLocationKey key,
            string owner,
            YuoActorAddress expectedAddress,
            YuoActorAddress migratedAddress,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Coordinator must use tokenized migration.");
        }

        public ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
            YuoLocationKey key,
            string owner,
            string lockToken,
            YuoActorAddress expectedAddress,
            YuoActorAddress migratedAddress,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedKey, key);
            Assert.Equal("move", owner);
            Assert.Equal(oldAddress, expectedAddress);
            Assert.Equal(newAddress, migratedAddress);
            MigrateToken = lockToken;
            if (ForceMigrationFailure)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.Conflict("forced migration failure.")));
            }

            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(new YuoLocationEntry(
                key,
                newAddress,
                2,
                null,
                null,
                DateTimeOffset.UtcNow)));
        }

        public ValueTask<bool> RemoveAsync(
            YuoLocationKey key,
            long expectedVersion = 0,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
