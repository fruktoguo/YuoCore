namespace YuoCore.Location;

/// <summary>
/// Location 注册表中的单条记录，包含地址、版本号、锁状态和更新时间。
/// Version 用于乐观并发控制，LockOwner/LockExpiresAt 用于迁移期间的排他访问。
/// </summary>
public sealed record YuoLocationEntry(
    YuoLocationKey Key,
    YuoActorAddress Address,
    long Version,
    string? LockOwner,
    DateTimeOffset? LockExpiresAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// 可选锁令牌。
    /// 同一个 owner 可能有多次迁移尝试；token 用于区分“当前持有的锁”和旧流程遗留的锁。
    /// </summary>
    public string? LockToken { get; init; }

    /// <summary>是否被锁定（有有效的锁所有者）。</summary>
    public bool IsLocked => !string.IsNullOrWhiteSpace(LockOwner);

    /// <summary>锁是否已过期。</summary>
    public bool IsLockExpired(DateTimeOffset now)
        => IsLocked && LockExpiresAt is not null && LockExpiresAt <= now;
}
