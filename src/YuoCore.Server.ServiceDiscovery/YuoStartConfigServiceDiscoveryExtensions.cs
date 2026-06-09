using YuoCore.Config;
using YuoCore.ServiceDiscovery;

namespace YuoCore.Server.ServiceDiscovery;

/// <summary>
/// StartConfig 到 ServiceDiscovery 的可选适配层。
/// 它单独成包，避免 YuoCore.Server 默认依赖服务发现；只有分布式或多进程宿主需要时才显式引用。
/// </summary>
public static class YuoStartConfigServiceDiscoveryExtensions
{
    public static async ValueTask<IReadOnlyList<YuoServiceDescriptor>> RegisterStartConfigServicesAsync(
        this IYuoServiceDiscovery discovery,
        YuoStartConfig config,
        CancellationToken cancellationToken = default)
    {
        return await RegisterStartConfigServicesCoreAsync(
            discovery,
            config,
            leaseTtlSelector: _ => null,
            cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<IReadOnlyList<YuoServiceDescriptor>> RegisterStartConfigServicesAsync(
        this IYuoServiceDiscovery discovery,
        YuoStartConfig config,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken = default)
    {
        return await RegisterStartConfigServicesCoreAsync(
            discovery,
            config,
            leaseTtlSelector: _ => leaseTtl,
            cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<IReadOnlyList<YuoServiceDescriptor>> RegisterStartConfigServicesAsync(
        this IYuoServiceDiscovery discovery,
        YuoStartConfig config,
        Func<YuoServiceConfig, TimeSpan?> leaseTtlSelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(leaseTtlSelector);

        return await RegisterStartConfigServicesCoreAsync(
            discovery,
            config,
            leaseTtlSelector,
            cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<int> RenewStartConfigServicesAsync(
        this IYuoServiceDiscovery discovery,
        YuoStartConfig config,
        TimeSpan? leaseTtl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(config);

        var count = 0;
        foreach (var service in config.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await discovery.RenewAsync(service.Name, leaseTtl, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    public static async ValueTask<int> RenewStartConfigServicesAsync(
        this IYuoServiceDiscovery discovery,
        YuoStartConfig config,
        Func<YuoServiceConfig, TimeSpan?> leaseTtlSelector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(leaseTtlSelector);

        var count = 0;
        foreach (var service in config.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 不同服务可以有不同租约预算：Gateway 通常需要更短 TTL，Map/Persistence 可以更长。
            if (await discovery.RenewAsync(service.Name, leaseTtlSelector(service), cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    private static async ValueTask<IReadOnlyList<YuoServiceDescriptor>> RegisterStartConfigServicesCoreAsync(
        IYuoServiceDiscovery discovery,
        YuoStartConfig config,
        Func<YuoServiceConfig, TimeSpan?> leaseTtlSelector,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(leaseTtlSelector);

        var validation = YuoStartConfigLoader.Validate(config);
        if (!validation.IsSuccess)
        {
            throw new InvalidOperationException(validation.Error!.Message);
        }

        var registrations = new List<PendingServiceRegistration>(config.Services.Count);
        foreach (var service in config.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            registrations.Add(new PendingServiceRegistration(
                service,
                ResolveEndpoint(config, service),
                BuildMetadata(config, service),
                ValidateLeaseTtl(leaseTtlSelector(service))));
        }

        var registered = new List<YuoServiceDescriptor>(registrations.Count);
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = await discovery.RegisterAsync(
                registration.Service.Name,
                registration.Service.Kind,
                registration.Endpoint,
                registration.Metadata,
                cancellationToken,
                registration.LeaseTtl).ConfigureAwait(false);
            registered.Add(descriptor);
        }

        return registered;
    }

    public static async ValueTask<int> UnregisterStartConfigServicesAsync(
        this IYuoServiceDiscovery discovery,
        YuoStartConfig config,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(config);

        var count = 0;
        foreach (var service in config.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await discovery.UnregisterAsync(service.Name, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }
        }

        return count;
    }

    private static string ResolveEndpoint(YuoStartConfig config, YuoServiceConfig service)
    {
        if (!string.IsNullOrWhiteSpace(service.Endpoint))
        {
            return service.Endpoint;
        }

        if (string.Equals(service.Kind, "Gateway", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(config.OuterEndpoint))
        {
            return config.OuterEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(config.InnerEndpoint))
        {
            return config.InnerEndpoint;
        }

        throw new InvalidOperationException($"Service '{service.Name}' has no endpoint and process endpoints are empty.");
    }

    private static Dictionary<string, string> BuildMetadata(YuoStartConfig config, YuoServiceConfig service)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nodeId"] = config.NodeId,
            ["processId"] = config.ProcessId,
            ["processKind"] = config.ProcessKind
        };

        if (!string.IsNullOrWhiteSpace(config.InnerEndpoint))
        {
            metadata["innerEndpoint"] = config.InnerEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(config.OuterEndpoint))
        {
            metadata["outerEndpoint"] = config.OuterEndpoint;
        }

        foreach (var (key, value) in config.Metadata)
        {
            metadata[key] = value;
        }

        foreach (var (key, value) in service.Metadata)
        {
            metadata[key] = value;
        }

        return metadata;
    }

    private static TimeSpan? ValidateLeaseTtl(TimeSpan? leaseTtl)
    {
        if (leaseTtl is { } ttl && ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTtl), "Service discovery lease ttl must be positive.");
        }

        return leaseTtl;
    }

    private sealed record PendingServiceRegistration(
        YuoServiceConfig Service,
        string Endpoint,
        IReadOnlyDictionary<string, string> Metadata,
        TimeSpan? LeaseTtl);
}
