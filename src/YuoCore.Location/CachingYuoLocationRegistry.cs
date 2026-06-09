using System.Collections.Concurrent;
using YuoCore.Abstractions;

namespace YuoCore.Location;

/// <summary>
/// Location 缓存包装器。
/// ET 的 ActorLocationSender 会缓存第一次查询到的地址，发送失败后再重新查；
/// 这里把同样语义放在 Registry 外层，便于以后把 inner 换成 Redis/PostgreSQL/远端 Location 服务。
/// </summary>
public sealed class CachingYuoLocationRegistry(IYuoLocationRegistry inner) :
    IYuoLocationRegistry,
    IYuoLocationCacheInvalidator
{
    private readonly IYuoLocationRegistry _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ConcurrentDictionary<YuoLocationKey, YuoLocationEntry> _cache = new();

    public async ValueTask<YuoResult<YuoLocationEntry>> RegisterAsync(
        YuoLocationKey key,
        YuoActorAddress address,
        long expectedVersion = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _inner.RegisterAsync(key, address, expectedVersion, cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            _cache[key] = result.Value;
        }

        return result;
    }

    public async ValueTask<YuoLocationEntry?> ResolveAsync(YuoLocationKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cache.TryGetValue(key, out var cached))
        {
            if (!cached.IsLockExpired(DateTimeOffset.UtcNow))
            {
                return cached;
            }

            _cache.TryRemove(key, out _);
        }

        var resolved = await _inner.ResolveAsync(key, cancellationToken).ConfigureAwait(false);
        if (resolved is not null)
        {
            _cache[key] = resolved;
        }

        return resolved;
    }

    public async ValueTask<YuoResult<YuoLocationEntry>> LockAsync(
        YuoLocationKey key,
        string owner,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _inner.LockAsync(key, owner, ttl, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            _cache[key] = result.Value;
        }

        return result;
    }

    public async ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
        YuoLocationKey key,
        string owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _inner.UnlockAsync(key, owner, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            _cache[key] = result.Value;
        }

        return result;
    }

    public async ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
        YuoLocationKey key,
        string owner,
        string lockToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _inner.UnlockAsync(key, owner, lockToken, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            _cache[key] = result.Value;
        }

        return result;
    }

    public async ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
        YuoLocationKey key,
        string owner,
        YuoActorAddress expectedAddress,
        YuoActorAddress newAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _inner.MigrateAsync(
                key,
                owner,
                expectedAddress,
                newAddress,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            _cache[key] = result.Value;
        }

        return result;
    }

    public async ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
        YuoLocationKey key,
        string owner,
        string lockToken,
        YuoActorAddress expectedAddress,
        YuoActorAddress newAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _inner.MigrateAsync(
                key,
                owner,
                lockToken,
                expectedAddress,
                newAddress,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsSuccess && result.Value is not null)
        {
            _cache[key] = result.Value;
        }

        return result;
    }

    public async ValueTask<bool> RemoveAsync(
        YuoLocationKey key,
        long expectedVersion = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var removed = await _inner.RemoveAsync(key, expectedVersion, cancellationToken).ConfigureAwait(false);
        if (removed)
        {
            _cache.TryRemove(key, out _);
        }

        return removed;
    }

    public void Invalidate(YuoLocationKey key, YuoActorAddress? staleAddress = null)
    {
        if (staleAddress is null)
        {
            _cache.TryRemove(key, out _);
            return;
        }

        // 只移除仍等于旧地址的缓存，避免并发刷新后被过期失败回调误删。
        if (_cache.TryGetValue(key, out var cached) && cached.Address == staleAddress.Value)
        {
            _cache.TryRemove(key, out _);
        }
    }
}
