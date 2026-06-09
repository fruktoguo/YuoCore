namespace YuoCore.ServiceDiscovery;

/// <summary>
/// 运行中服务的轻量描述。
/// 这里不绑定 Actor、HTTP 或具体网络类型，避免服务发现基础包反向依赖分布式 transport。
/// </summary>
public sealed record YuoServiceDescriptor(
    string Name,
    string Kind,
    string Endpoint,
    IReadOnlyDictionary<string, string> Metadata,
    long Version,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LeaseExpiresAt = null,
    TimeSpan? LeaseTtl = null)
{
    public bool IsLeaseExpired(DateTimeOffset now)
    {
        return LeaseExpiresAt is { } expiresAt && expiresAt <= now;
    }
}

public sealed record YuoServiceQuery(
    string? Kind = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public enum YuoServiceChangeKind
{
    Registered = 1,
    Updated = 2,
    Removed = 3
}

public sealed record YuoServiceChange(
    YuoServiceChangeKind Kind,
    YuoServiceDescriptor Service);
