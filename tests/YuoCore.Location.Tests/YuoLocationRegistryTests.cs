using YuoCore.Actor;
using YuoCore.Location;

namespace YuoCore.Location.Tests;

public sealed class YuoLocationRegistryTests
{
    [Fact]
    public void CachingRegistryRejectsNullInnerRegistry()
    {
        Assert.Throws<ArgumentNullException>(() => new CachingYuoLocationRegistry(null!));
    }

    [Fact]
    public async Task RegisterResolveAndRemoveLocation()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p1");
        var address = new YuoActorAddress("node", "game", 1, 100);

        var registered = await registry.RegisterAsync(key, address);
        var resolved = await registry.ResolveAsync(key);
        var removed = await registry.RemoveAsync(key, registered.Value!.Version);

        Assert.True(registered.IsSuccess);
        Assert.Equal(address, resolved!.Address);
        Assert.True(removed);
        Assert.Null(await registry.ResolveAsync(key));
    }

    [Fact]
    public async Task VersionCheckRejectsStaleRouteReplacement()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p2");
        var first = await registry.RegisterAsync(key, new YuoActorAddress("node", "game", 1, 100));

        var stale = await registry.RegisterAsync(
            key,
            new YuoActorAddress("node", "game", 2, 100),
            expectedVersion: first.Value!.Version + 10);

        var resolved = await registry.ResolveAsync(key);
        Assert.False(stale.IsSuccess);
        Assert.Equal(1, resolved!.Address.FiberId);
    }

    [Fact]
    public async Task LockBlocksReplacementUntilOwnerUnlocks()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p3");
        await registry.RegisterAsync(key, new YuoActorAddress("node", "game", 1, 100));

        var locked = await registry.LockAsync(key, "move");
        var blocked = await registry.RegisterAsync(key, new YuoActorAddress("node", "game", 2, 100));
        var wrongUnlock = await registry.UnlockAsync(key, "other");
        var unlocked = await registry.UnlockAsync(key, "move");
        var moved = await registry.RegisterAsync(key, new YuoActorAddress("node", "game", 2, 100), unlocked.Value!.Version);

        Assert.True(locked.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(locked.Value!.LockToken));
        Assert.False(blocked.IsSuccess);
        Assert.False(wrongUnlock.IsSuccess);
        Assert.True(moved.IsSuccess);
        Assert.Equal(2, (await registry.ResolveAsync(key))!.Address.FiberId);
    }

    [Fact]
    public async Task RemoveDoesNotDeleteLockedLocation()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-remove-locked");
        var address = new YuoActorAddress("node", "game", 1, 100);
        var registered = await registry.RegisterAsync(key, address);
        await registry.LockAsync(key, "migration", TimeSpan.FromMilliseconds(40));

        var removedWhileLocked = await registry.RemoveAsync(key, registered.Value!.Version);
        var stillResolved = await registry.ResolveAsync(key);
        await Task.Delay(90);
        var removedAfterExpiry = await registry.RemoveAsync(key);

        Assert.False(removedWhileLocked);
        Assert.NotNull(stillResolved);
        Assert.Equal(address, stillResolved!.Address);
        Assert.True(removedAfterExpiry);
        Assert.Null(await registry.ResolveAsync(key));
    }

    [Fact]
    public async Task MigrateMovesLockedLocationAtomically()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-migrate");
        var oldAddress = new YuoActorAddress("node", "game", 1, 100);
        var newAddress = new YuoActorAddress("node", "game", 2, 200);

        var registered = await registry.RegisterAsync(key, oldAddress);
        var locked = await registry.LockAsync(key, "move");
        var migrated = await registry.MigrateAsync(key, "move", oldAddress, newAddress);
        var resolved = await registry.ResolveAsync(key);

        Assert.True(registered.IsSuccess);
        Assert.True(locked.IsSuccess);
        Assert.True(migrated.IsSuccess);
        Assert.Equal(newAddress, resolved!.Address);
        Assert.False(resolved.IsLocked);
        Assert.Equal(locked.Value!.Version + 1, resolved.Version);
    }

    [Fact]
    public async Task TokenizedUnlockRejectsStaleLockToken()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-token-unlock");
        var address = new YuoActorAddress("node", "game", 1, 100);
        await registry.RegisterAsync(key, address);

        var locked = await registry.LockAsync(key, "move");
        var wrongToken = await registry.UnlockAsync(key, "move", "stale-token");
        var resolvedAfterWrongToken = await registry.ResolveAsync(key);
        var unlocked = await registry.UnlockAsync(key, "move", locked.Value!.LockToken!);
        var resolvedAfterUnlock = await registry.ResolveAsync(key);

        Assert.True(locked.IsSuccess);
        Assert.False(wrongToken.IsSuccess);
        Assert.True(resolvedAfterWrongToken!.IsLocked);
        Assert.Equal(locked.Value.LockToken, resolvedAfterWrongToken.LockToken);
        Assert.True(unlocked.IsSuccess);
        Assert.False(resolvedAfterUnlock!.IsLocked);
        Assert.Null(resolvedAfterUnlock.LockToken);
    }

    [Fact]
    public async Task TokenizedMigrateRejectsStaleLockToken()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-token-migrate");
        var oldAddress = new YuoActorAddress("node", "game", 1, 100);
        var newAddress = new YuoActorAddress("node", "game", 2, 200);
        await registry.RegisterAsync(key, oldAddress);

        var locked = await registry.LockAsync(key, "move");
        var wrongToken = await registry.MigrateAsync(key, "move", "stale-token", oldAddress, newAddress);
        var resolvedAfterWrongToken = await registry.ResolveAsync(key);
        var migrated = await registry.MigrateAsync(key, "move", locked.Value!.LockToken!, oldAddress, newAddress);
        var resolvedAfterMigrate = await registry.ResolveAsync(key);

        Assert.True(locked.IsSuccess);
        Assert.False(wrongToken.IsSuccess);
        Assert.Equal(oldAddress, resolvedAfterWrongToken!.Address);
        Assert.True(resolvedAfterWrongToken.IsLocked);
        Assert.True(migrated.IsSuccess);
        Assert.Equal(newAddress, resolvedAfterMigrate!.Address);
        Assert.False(resolvedAfterMigrate.IsLocked);
        Assert.Null(resolvedAfterMigrate.LockToken);
    }

    [Fact]
    public async Task MigrateRejectsWrongOwnerOrUnexpectedOldAddress()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-migrate-reject");
        var oldAddress = new YuoActorAddress("node", "game", 1, 100);
        var wrongAddress = new YuoActorAddress("node", "game", 9, 900);
        var newAddress = new YuoActorAddress("node", "game", 2, 200);

        await registry.RegisterAsync(key, oldAddress);
        await registry.LockAsync(key, "move");

        var wrongOwner = await registry.MigrateAsync(key, "other", oldAddress, newAddress);
        var wrongOldAddress = await registry.MigrateAsync(key, "move", wrongAddress, newAddress);
        var resolved = await registry.ResolveAsync(key);

        Assert.False(wrongOwner.IsSuccess);
        Assert.False(wrongOldAddress.IsSuccess);
        Assert.Equal(oldAddress, resolved!.Address);
        Assert.True(resolved.IsLocked);
    }

    [Fact]
    public async Task ExpiredLockIsClearedBeforeReplacement()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-expire");
        var first = new YuoActorAddress("node", "game", 1, 100);
        var second = new YuoActorAddress("node", "game", 2, 100);
        await registry.RegisterAsync(key, first);

        var locked = await registry.LockAsync(key, "move", TimeSpan.FromMilliseconds(20));
        var blocked = await registry.RegisterAsync(key, second);
        await Task.Delay(80);
        var moved = await registry.RegisterAsync(key, second);
        var resolved = await registry.ResolveAsync(key);

        Assert.True(locked.IsSuccess);
        Assert.False(blocked.IsSuccess);
        Assert.True(moved.IsSuccess);
        Assert.Equal(second, resolved!.Address);
        Assert.False(resolved.IsLocked);
    }

    [Fact]
    public async Task ResolveCleansExpiredLockAndIncrementsVersion()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-expire-resolve");
        await registry.RegisterAsync(key, new YuoActorAddress("node", "game", 1, 100));

        var locked = await registry.LockAsync(key, "move", TimeSpan.FromMilliseconds(20));
        await Task.Delay(80);
        var resolved = await registry.ResolveAsync(key);

        Assert.True(locked.IsSuccess);
        Assert.True(locked.Value!.IsLocked);
        Assert.False(resolved!.IsLocked);
        Assert.True(resolved.Version > locked.Value.Version);
    }

    [Fact]
    public async Task CachingRegistryInvalidatesStaleAddressAndReloadsInnerEntry()
    {
        var inner = new InMemoryYuoLocationRegistry();
        var registry = new CachingYuoLocationRegistry(inner);
        var key = new YuoLocationKey("Player", "p4");
        var oldAddress = new YuoActorAddress("node", "game", 1, 100);
        var newAddress = new YuoActorAddress("node", "game", 2, 100);

        await inner.RegisterAsync(key, oldAddress);
        Assert.Equal(oldAddress, (await registry.ResolveAsync(key))!.Address);

        await inner.RegisterAsync(key, newAddress);
        Assert.Equal(oldAddress, (await registry.ResolveAsync(key))!.Address);

        registry.Invalidate(key, oldAddress);

        Assert.Equal(newAddress, (await registry.ResolveAsync(key))!.Address);
    }

    [Fact]
    public async Task CachingRegistryRefreshesExpiredLockFromInnerRegistry()
    {
        var inner = new InMemoryYuoLocationRegistry();
        var registry = new CachingYuoLocationRegistry(inner);
        var key = new YuoLocationKey("Player", "p-cache-expire");
        var address = new YuoActorAddress("node", "game", 1, 100);
        await registry.RegisterAsync(key, address);

        var locked = await registry.LockAsync(key, "move", TimeSpan.FromMilliseconds(20));
        Assert.True(locked.Value!.IsLocked);

        await Task.Delay(80);
        var resolved = await registry.ResolveAsync(key);

        Assert.False(resolved!.IsLocked);
        Assert.True(resolved.Version > locked.Value.Version);
    }

    [Fact]
    public async Task CachingRegistryUpdatesCacheAfterMigration()
    {
        var inner = new InMemoryYuoLocationRegistry();
        var registry = new CachingYuoLocationRegistry(inner);
        var key = new YuoLocationKey("Player", "p-cache-migrate");
        var oldAddress = new YuoActorAddress("node", "game", 1, 100);
        var newAddress = new YuoActorAddress("node", "game", 2, 200);

        await registry.RegisterAsync(key, oldAddress);
        Assert.Equal(oldAddress, (await registry.ResolveAsync(key))!.Address);

        await registry.LockAsync(key, "move");
        var migrated = await registry.MigrateAsync(key, "move", oldAddress, newAddress);
        var resolved = await registry.ResolveAsync(key);

        Assert.True(migrated.IsSuccess);
        Assert.Equal(newAddress, resolved!.Address);
        Assert.False(resolved.IsLocked);
    }

    [Fact]
    public async Task InMemoryRegistryHonorsCancellationBeforeMutation()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-cancel-register");
        var address = new YuoActorAddress("node", "game", 1, 100);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await registry.RegisterAsync(key, address, cancellationToken: cts.Token));

        Assert.Null(await registry.ResolveAsync(key));
    }

    [Fact]
    public async Task InMemoryRegistryHonorsCancellationBeforeRemove()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var key = new YuoLocationKey("Player", "p-cancel-remove");
        var address = new YuoActorAddress("node", "game", 1, 100);
        await registry.RegisterAsync(key, address);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await registry.RemoveAsync(key, cancellationToken: cts.Token));

        Assert.Equal(address, (await registry.ResolveAsync(key))!.Address);
    }

    [Fact]
    public async Task CachingRegistryHonorsCancellationBeforeReturningCachedEntry()
    {
        var inner = new InMemoryYuoLocationRegistry();
        var registry = new CachingYuoLocationRegistry(inner);
        var key = new YuoLocationKey("Player", "p-cache-cancel");
        var address = new YuoActorAddress("node", "game", 1, 100);
        await registry.RegisterAsync(key, address);
        Assert.Equal(address, (await registry.ResolveAsync(key))!.Address);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await registry.ResolveAsync(key, cts.Token));
    }

    [Fact]
    public async Task ActorRegistryAdapterUsesLocationPackage()
    {
        var registry = new InMemoryYuoLocationRegistry();
        var adapter = new YuoLocationActorRegistry(registry);
        var actorId = new ActorId(123);
        var address = new ActorAddress("local", 7, actorId);

        await adapter.RegisterAsync(actorId, address);
        var resolved = await adapter.ResolveAsync(actorId);

        Assert.Equal(address, resolved);
    }

    [Fact]
    public void ActorRegistryAdapterRejectsNullLocationRegistry()
    {
        Assert.Throws<ArgumentNullException>(() => new YuoLocationActorRegistry(null!));
    }
}
