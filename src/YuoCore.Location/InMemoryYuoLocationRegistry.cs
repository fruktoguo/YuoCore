using System.Collections.Concurrent;
using YuoCore.Abstractions;

namespace YuoCore.Location;

/// <summary>
/// 进程内 Location 实现，适合单机、测试和后续 Redis/PostgreSQL 实现的行为基准。
/// Register 使用 expectedVersion 防止旧地址覆盖新地址；lock 用于迁移期间阻止并发写入。
/// </summary>
public sealed class InMemoryYuoLocationRegistry : IYuoLocationRegistry
{
    private readonly ConcurrentDictionary<YuoLocationKey, YuoLocationEntry> _entries = new();
    private readonly object _gate = new();

    public ValueTask<YuoResult<YuoLocationEntry>> RegisterAsync(
        YuoLocationKey key,
        YuoActorAddress address,
        long expectedVersion = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _entries.TryGetValue(key, out var current);
            if (current is not null)
            {
                current = ClearExpiredLockIfNeeded(current);
            }

            if (current is { IsLocked: true })
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is locked by {current.LockOwner}.")));
            }

            if (expectedVersion > 0 && current is not null && current.Version != expectedVersion)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} version mismatch. expected={expectedVersion}, actual={current.Version}.")));
            }

            var next = new YuoLocationEntry(key, address, (current?.Version ?? 0) + 1, null, null, DateTimeOffset.UtcNow);
            _entries[key] = next;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(next));
        }
    }

    public ValueTask<YuoLocationEntry?> ResolveAsync(YuoLocationKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return ValueTask.FromResult<YuoLocationEntry?>(null);
            }

            entry = ClearExpiredLockIfNeeded(entry);
            return ValueTask.FromResult<YuoLocationEntry?>(entry);
        }
    }

    public ValueTask<YuoResult<YuoLocationEntry>> LockAsync(
        YuoLocationKey key,
        string owner,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(owner))
        {
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.Invalid("Lock owner cannot be empty.")));
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.NotFound($"Location {key} does not exist.")));
            }

            current = ClearExpiredLockIfNeeded(current);
            if (current.IsLocked && current.LockOwner != owner)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is locked by {current.LockOwner}.")));
            }

            var now = DateTimeOffset.UtcNow;
            var expiresAt = ttl is { } value && value > TimeSpan.Zero ? now.Add(value) : (DateTimeOffset?)null;
            var locked = current with
            {
                LockOwner = owner,
                LockToken = Guid.NewGuid().ToString("N"),
                LockExpiresAt = expiresAt,
                UpdatedAt = now
            };
            _entries[key] = locked;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(locked));
        }
    }

    public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
        YuoLocationKey key,
        string owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.NotFound($"Location {key} does not exist.")));
            }

            current = ClearExpiredLockIfNeeded(current);
            if (!current.IsLocked)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(current));
            }

            if (current.LockOwner != owner)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is locked by {current.LockOwner}, not {owner}.")));
            }

            var unlocked = current with
            {
                LockOwner = null,
                LockToken = null,
                LockExpiresAt = null,
                Version = current.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _entries[key] = unlocked;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(unlocked));
        }
    }

    public ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
        YuoLocationKey key,
        string owner,
        string lockToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(lockToken))
        {
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.BadRequest("Lock token cannot be empty.")));
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.NotFound($"Location {key} does not exist.")));
            }

            current = ClearExpiredLockIfNeeded(current);
            if (!current.IsLocked)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(current));
            }

            if (current.LockOwner != owner)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is locked by {current.LockOwner}, not {owner}.")));
            }

            if (!string.Equals(current.LockToken, lockToken, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} lock token mismatch.")));
            }

            var unlocked = current with
            {
                LockOwner = null,
                LockToken = null,
                LockExpiresAt = null,
                Version = current.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _entries[key] = unlocked;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(unlocked));
        }
    }

    public ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
        YuoLocationKey key,
        string owner,
        YuoActorAddress expectedAddress,
        YuoActorAddress newAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(owner))
        {
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.Invalid("Migration owner cannot be empty.")));
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.NotFound($"Location {key} does not exist.")));
            }

            current = ClearExpiredLockIfNeeded(current);
            if (!current.IsLocked)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is not locked for migration.")));
            }

            if (current.LockOwner != owner)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is locked by {current.LockOwner}, not {owner}.")));
            }

            if (current.Address != expectedAddress)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} address mismatch during migration.")));
            }

            var migrated = current with
            {
                Address = newAddress,
                LockOwner = null,
                LockToken = null,
                LockExpiresAt = null,
                Version = current.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _entries[key] = migrated;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(migrated));
        }
    }

    public ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
        YuoLocationKey key,
        string owner,
        string lockToken,
        YuoActorAddress expectedAddress,
        YuoActorAddress newAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(owner))
        {
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.Invalid("Migration owner cannot be empty.")));
        }

        if (string.IsNullOrWhiteSpace(lockToken))
        {
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.BadRequest("Lock token cannot be empty.")));
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(YuoError.NotFound($"Location {key} does not exist.")));
            }

            current = ClearExpiredLockIfNeeded(current);
            if (!current.IsLocked)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is not locked for migration.")));
            }

            if (current.LockOwner != owner)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} is locked by {current.LockOwner}, not {owner}.")));
            }

            if (!string.Equals(current.LockToken, lockToken, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} lock token mismatch.")));
            }

            if (current.Address != expectedAddress)
            {
                return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Fail(
                    YuoError.Conflict($"Location {key} address mismatch during migration.")));
            }

            var migrated = current with
            {
                Address = newAddress,
                LockOwner = null,
                LockToken = null,
                LockExpiresAt = null,
                Version = current.Version + 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _entries[key] = migrated;
            return ValueTask.FromResult(YuoResult<YuoLocationEntry>.Ok(migrated));
        }
    }

    public ValueTask<bool> RemoveAsync(YuoLocationKey key, long expectedVersion = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var current))
            {
                return ValueTask.FromResult(false);
            }

            current = ClearExpiredLockIfNeeded(current);
            if (current.IsLocked)
            {
                return ValueTask.FromResult(false);
            }

            if (expectedVersion > 0 && current.Version != expectedVersion)
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(_entries.TryRemove(key, out _));
        }
    }

    private YuoLocationEntry ClearExpiredLockIfNeeded(YuoLocationEntry entry)
    {
        var now = DateTimeOffset.UtcNow;
        if (!entry.IsLockExpired(now))
        {
            return entry;
        }

        // 锁过期相当于一次 location 元数据变更，递增版本便于迁移方发现自己持有的是旧状态。
        var unlocked = entry with
        {
            LockOwner = null,
            LockToken = null,
            LockExpiresAt = null,
            Version = entry.Version + 1,
            UpdatedAt = now
        };
        _entries[entry.Key] = unlocked;
        return unlocked;
    }
}
