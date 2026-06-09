namespace YuoCore.ServiceDiscovery;

/// <summary>
/// 服务发现接口。支持服务的注册、续约、注销、查询和变更订阅。
/// 不绑定具体网络协议，可与内存、Redis、Consul 等实现配合使用。
/// </summary>
public interface IYuoServiceDiscovery
{
    /// <summary>注册一个服务。返回服务描述符（含版本号和更新时间）。</summary>
    ValueTask<YuoServiceDescriptor> RegisterAsync(
        string name,
        string kind,
        string endpoint,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default,
        TimeSpan? leaseTtl = null);

    /// <summary>续约已有服务的租约。返回 false 表示服务已过期或不存在。</summary>
    ValueTask<bool> RenewAsync(
        string name,
        TimeSpan? leaseTtl = null,
        CancellationToken cancellationToken = default);

    /// <summary>清除所有过期的服务租约。返回清除的服务数量。</summary>
    ValueTask<int> PruneExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>注销一个服务。返回 false 表示服务不存在。</summary>
    ValueTask<bool> UnregisterAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>根据查询条件查找匹配的服务。</summary>
    ValueTask<IReadOnlyList<YuoServiceDescriptor>> QueryAsync(
        YuoServiceQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>订阅符合查询条件的服务变更通知。返回的 IDisposable 用于取消订阅。</summary>
    IDisposable Subscribe(
        YuoServiceQuery query,
        Action<YuoServiceChange> onChange);
}
