using YuoCore.Abstractions;

namespace YuoCore.Location;

/// <summary>
/// 稳定业务键到 Actor 地址的位置注册表。
/// 支持乐观锁（expectedVersion）、分布式锁（Lock/Unlock）和原子迁移（Migrate）。
/// 是 Actor 路由和玩家迁移的核心基础设施。
/// </summary>
public interface IYuoLocationRegistry
{
    /// <summary>注册或更新业务键对应的 Actor 地址。expectedVersion=0 时忽略版本检查。</summary>
    ValueTask<YuoResult<YuoLocationEntry>> RegisterAsync(
        YuoLocationKey key,
        YuoActorAddress address,
        long expectedVersion = 0,
        CancellationToken cancellationToken = default);

    /// <summary>解析业务键对应的位置信息。返回 null 表示未注册。</summary>
    ValueTask<YuoLocationEntry?> ResolveAsync(YuoLocationKey key, CancellationToken cancellationToken = default);

    /// <summary>锁定指定业务键，防止并发迁移。ttl 为 null 或零值时不过期。</summary>
    ValueTask<YuoResult<YuoLocationEntry>> LockAsync(
        YuoLocationKey key,
        string owner,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>释放指定业务键的锁。只有锁拥有者可以解锁。</summary>
    ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
        YuoLocationKey key,
        string owner,
        CancellationToken cancellationToken = default);

    /// <summary>释放指定业务键的锁。owner 和 lockToken 都匹配时才会解锁。</summary>
    ValueTask<YuoResult<YuoLocationEntry>> UnlockAsync(
        YuoLocationKey key,
        string owner,
        string lockToken,
        CancellationToken cancellationToken = default);

    /// <summary>原子迁移：在锁持有状态下将地址从 expectedAddress 更新为 newAddress。</summary>
    ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
        YuoLocationKey key,
        string owner,
        YuoActorAddress expectedAddress,
        YuoActorAddress newAddress,
        CancellationToken cancellationToken = default);

    /// <summary>原子迁移：在 owner 和 lockToken 都匹配时迁移地址。</summary>
    ValueTask<YuoResult<YuoLocationEntry>> MigrateAsync(
        YuoLocationKey key,
        string owner,
        string lockToken,
        YuoActorAddress expectedAddress,
        YuoActorAddress newAddress,
        CancellationToken cancellationToken = default);

    /// <summary>删除指定业务键的位置信息。expectedVersion=0 时忽略版本检查。</summary>
    ValueTask<bool> RemoveAsync(YuoLocationKey key, long expectedVersion = 0, CancellationToken cancellationToken = default);
}
