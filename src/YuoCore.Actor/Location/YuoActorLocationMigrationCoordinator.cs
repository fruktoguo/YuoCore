using YuoCore.Abstractions;
using YuoCore.Location;

namespace YuoCore.Actor;

/// <summary>
/// 进程内 Actor Location 迁移协调器。
/// 它把常见迁移流程固定为 Lock -> Migrate -> 失效本进程 sender cache，避免业务代码漏掉缓存清理。
/// 分布式广播、跨进程 fencing 和可靠迁移日志仍应放在可选分布式实现里。
/// </summary>
public sealed class YuoActorLocationMigrationCoordinator
{
    /// <summary>
    /// 迁移锁默认 TTL。调用方不传 lockTtl 时使用，避免迁移方崩溃后锁永不过期。
    /// </summary>
    public static readonly TimeSpan DefaultLockTtl = TimeSpan.FromSeconds(30);

    private readonly IYuoLocationRegistry _locations;
    private readonly IReadOnlyList<YuoActorLocationSender> _senders;

    public YuoActorLocationMigrationCoordinator(
        IYuoLocationRegistry locations,
        IEnumerable<YuoActorLocationSender>? senders = null)
    {
        _locations = locations;
        _senders = senders?.ToArray() ?? [];
    }

    public async ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
        YuoLocationKey key,
        string owner,
        YuoActorAddress expectedAddress,
        YuoActorAddress newAddress,
        TimeSpan? lockTtl = null,
        CancellationToken cancellationToken = default)
    {
        var locked = await _locations.LockAsync(
            key,
            owner,
            lockTtl ?? DefaultLockTtl,
            cancellationToken).ConfigureAwait(false);
        if (!locked.IsSuccess)
        {
            return locked;
        }

        // 拿到迁移锁后立即清理本进程 sender 的旧地址缓存。
        // 否则已经缓存旧地址的发送方会绕过 locked location，继续把消息打到旧 Actor。
        InvalidateLocalSenderCaches(key, expectedAddress);

        var lockToken = locked.Value?.LockToken;
        if (string.IsNullOrWhiteSpace(lockToken))
        {
            await TryUnlockWithoutTokenAfterMissingTokenAsync(key, owner, cancellationToken).ConfigureAwait(false);
            return YuoResult<YuoLocationEntry>.Fail(YuoError.Failed(
                $"Location {key} lock did not return a lock token."));
        }

        var migrated = await _locations.MigrateAsync(
            key,
            owner,
            lockToken,
            expectedAddress,
            newAddress,
            cancellationToken).ConfigureAwait(false);

        if (migrated.IsSuccess)
        {
            return migrated;
        }

        // 迁移失败通常是 expectedAddress 不匹配或 owner 不匹配。
        // 如果本轮已经拿到锁，应尽量释放，避免业务错误把 location 长时间锁死。
        await TryUnlockAfterFailedMigrationAsync(key, owner, lockToken, cancellationToken).ConfigureAwait(false);
        return migrated;
    }

    private void InvalidateLocalSenderCaches(YuoLocationKey key, YuoActorAddress staleAddress)
    {
        foreach (var sender in _senders)
        {
            sender.Invalidate(key, staleAddress);
        }
    }

    private async ValueTask TryUnlockAfterFailedMigrationAsync(
        YuoLocationKey key,
        string owner,
        string lockToken,
        CancellationToken cancellationToken)
    {
        try
        {
            await _locations.UnlockAsync(key, owner, lockToken, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 解锁失败通常说明锁已过期或被其他迁移流程处理。
            // 原始迁移错误更重要，这里不覆盖它；需要强审计的项目应在分布式迁移层记录。
        }
    }

    private async ValueTask TryUnlockWithoutTokenAfterMissingTokenAsync(
        YuoLocationKey key,
        string owner,
        CancellationToken cancellationToken)
    {
        try
        {
            await _locations.UnlockAsync(key, owner, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 这里是兼容旧实现的兜底清理；调用方仍会收到缺少 lock token 的错误。
        }
    }
}
