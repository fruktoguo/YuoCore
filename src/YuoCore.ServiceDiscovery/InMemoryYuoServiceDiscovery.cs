using System.Collections.Concurrent;
using YuoCore.Abstractions;

namespace YuoCore.ServiceDiscovery;

/// <summary>
/// 单进程内存服务发现。
/// 它吸收 ET ServiceDiscovery 的服务注册、索引查询、变更通知思想，但不包含主从选举、远程代理和网络转发。
/// 分布式版本后续应作为可选实现接入同一接口。
/// </summary>
public sealed class InMemoryYuoServiceDiscovery(
    IYuoClock? clock = null,
    Action<Exception, YuoServiceChange>? subscriptionErrorHandler = null) : IYuoServiceDiscovery
{
    private readonly object _gate = new();
    private readonly IYuoClock _clock = clock ?? new SystemYuoClock();
    private readonly Action<Exception, YuoServiceChange>? _subscriptionErrorHandler = subscriptionErrorHandler;
    private readonly Dictionary<string, YuoServiceDescriptor> _services = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Subscription> _subscriptions = new();
    private long _nextVersion;
    private long _nextSubscriptionId;

    public ValueTask<YuoServiceDescriptor> RegisterAsync(
        string name,
        string kind,
        string endpoint,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default,
        TimeSpan? leaseTtl = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Service name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Service kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("Service endpoint is required.", nameof(endpoint));
        ValidateLeaseTtl(leaseTtl);

        YuoServiceDescriptor descriptor;
        YuoServiceDescriptor? removedPrevious = null;
        YuoServiceChangeKind changeKind;
        List<YuoServiceDescriptor>? expired;
        lock (_gate)
        {
            var now = _clock.UtcNow;
            expired = PruneExpiredCore(now);
            var hasPrevious = _services.TryGetValue(name, out var previous);
            var metadataCopy = SnapshotServiceMetadata(metadata);

            if (hasPrevious && !HasSameDiscoveryShape(previous!, kind, metadataCopy))
            {
                removedPrevious = previous! with
                {
                    Version = Interlocked.Increment(ref _nextVersion),
                    UpdatedAt = now
                };
                changeKind = YuoServiceChangeKind.Registered;
            }
            else
            {
                changeKind = hasPrevious
                    ? YuoServiceChangeKind.Updated
                    : YuoServiceChangeKind.Registered;
            }

            descriptor = new YuoServiceDescriptor(
                name,
                kind,
                endpoint,
                metadataCopy,
                Interlocked.Increment(ref _nextVersion),
                now,
                CalculateLeaseExpiresAt(now, leaseTtl),
                leaseTtl);
            _services[name] = descriptor;
        }

        PublishRemoved(expired);
        if (removedPrevious is not null)
        {
            Publish(new YuoServiceChange(YuoServiceChangeKind.Removed, removedPrevious));
        }

        Publish(new YuoServiceChange(changeKind, descriptor));
        return ValueTask.FromResult(descriptor);
    }

    public ValueTask<bool> RenewAsync(
        string name,
        TimeSpan? leaseTtl = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateLeaseTtl(leaseTtl);

        YuoServiceDescriptor? renewed = null;
        List<YuoServiceDescriptor>? expired;
        lock (_gate)
        {
            var now = _clock.UtcNow;
            expired = PruneExpiredCore(now);
            if (_services.TryGetValue(name, out var descriptor))
            {
                var nextLeaseTtl = leaseTtl ?? descriptor.LeaseTtl;
                renewed = descriptor with
                {
                    Version = Interlocked.Increment(ref _nextVersion),
                    UpdatedAt = now,
                    LeaseExpiresAt = CalculateLeaseExpiresAt(now, nextLeaseTtl),
                    LeaseTtl = nextLeaseTtl
                };
                _services[name] = renewed;
            }
        }

        PublishRemoved(expired);
        if (renewed is null)
        {
            return ValueTask.FromResult(false);
        }

        Publish(new YuoServiceChange(YuoServiceChangeKind.Updated, renewed));
        return ValueTask.FromResult(true);
    }

    public ValueTask<int> PruneExpiredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<YuoServiceDescriptor>? expired;
        lock (_gate)
        {
            expired = PruneExpiredCore(_clock.UtcNow);
        }

        PublishRemoved(expired);
        return ValueTask.FromResult(expired?.Count ?? 0);
    }

    public ValueTask<bool> UnregisterAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        YuoServiceDescriptor? removed = null;
        lock (_gate)
        {
            if (_services.Remove(name, out var descriptor))
            {
                var now = _clock.UtcNow;
                removed = descriptor with
                {
                    Version = Interlocked.Increment(ref _nextVersion),
                    UpdatedAt = now
                };
            }
        }

        if (removed is null)
        {
            return ValueTask.FromResult(false);
        }

        Publish(new YuoServiceChange(YuoServiceChangeKind.Removed, removed));
        return ValueTask.FromResult(true);
    }

    public ValueTask<IReadOnlyList<YuoServiceDescriptor>> QueryAsync(
        YuoServiceQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        query = SnapshotQuery(query);

        List<YuoServiceDescriptor> result;
        List<YuoServiceDescriptor>? expired;
        lock (_gate)
        {
            expired = PruneExpiredCore(_clock.UtcNow);
            result = _services.Values
                .Where(service => Matches(query, service))
                .OrderBy(service => service.Name, StringComparer.Ordinal)
                .ToList();
        }

        PublishRemoved(expired);
        return ValueTask.FromResult<IReadOnlyList<YuoServiceDescriptor>>(result);
    }

    public IDisposable Subscribe(
        YuoServiceQuery query,
        Action<YuoServiceChange> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        query = SnapshotQuery(query);

        var id = Interlocked.Increment(ref _nextSubscriptionId);
        _subscriptions[id] = new Subscription(query, onChange);
        return new SubscriptionHandle(_subscriptions, id);
    }

    private void Publish(YuoServiceChange change)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (Matches(subscription.Query, change.Service))
            {
                try
                {
                    subscription.OnChange(change);
                }
                catch (Exception exception)
                {
                    // 服务发现通知是旁路观测能力；一个坏订阅者不能阻断注册表更新，也不能影响其他订阅者。
                    TryReportSubscriptionError(exception, change);
                }
            }
        }
    }

    private void TryReportSubscriptionError(Exception exception, YuoServiceChange change)
    {
        if (_subscriptionErrorHandler is null)
        {
            return;
        }

        try
        {
            _subscriptionErrorHandler(exception, change);
        }
        catch
        {
            // 错误出口本身也必须隔离，避免通知失败重新污染服务发现主流程。
        }
    }

    private List<YuoServiceDescriptor>? PruneExpiredCore(DateTimeOffset now)
    {
        List<YuoServiceDescriptor>? expired = null;
        foreach (var service in _services.Values.ToArray())
        {
            if (!service.IsLeaseExpired(now))
            {
                continue;
            }

            _services.Remove(service.Name);
            expired ??= [];
            expired.Add(service with
            {
                Version = Interlocked.Increment(ref _nextVersion),
                UpdatedAt = now
            });
        }

        return expired;
    }

    private void PublishRemoved(List<YuoServiceDescriptor>? removed)
    {
        if (removed is null)
        {
            return;
        }

        foreach (var service in removed)
        {
            Publish(new YuoServiceChange(YuoServiceChangeKind.Removed, service));
        }
    }

    private static DateTimeOffset? CalculateLeaseExpiresAt(DateTimeOffset now, TimeSpan? leaseTtl)
    {
        return leaseTtl is null ? null : now + leaseTtl.Value;
    }

    private static void ValidateLeaseTtl(TimeSpan? leaseTtl)
    {
        if (leaseTtl is { } ttl && ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseTtl), "Lease ttl must be positive.");
        }
    }

    private static bool Matches(YuoServiceQuery query, YuoServiceDescriptor service)
    {
        if (!string.IsNullOrWhiteSpace(query.Kind) &&
            !string.Equals(query.Kind, service.Kind, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.Metadata is null || query.Metadata.Count == 0)
        {
            return true;
        }

        foreach (var (key, value) in query.Metadata)
        {
            if (!service.Metadata.TryGetValue(key, out var current) ||
                !string.Equals(current, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSameDiscoveryShape(
        YuoServiceDescriptor previous,
        string nextKind,
        IReadOnlyDictionary<string, string> nextMetadata)
    {
        return string.Equals(previous.Kind, nextKind, StringComparison.Ordinal) &&
               previous.Metadata.Count == nextMetadata.Count &&
               previous.Metadata.All(pair =>
                   nextMetadata.TryGetValue(pair.Key, out var value) &&
                   string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    private static YuoServiceQuery SnapshotQuery(YuoServiceQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Metadata is null || query.Metadata.Count == 0)
        {
            return query with { Metadata = null };
        }

        return query with
        {
            Metadata = SnapshotMetadata(query.Metadata, nameof(query))
        };
    }

    private static Dictionary<string, string> SnapshotServiceMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        return SnapshotMetadata(metadata, nameof(metadata)) ?? EmptyMetadata();
    }

    private static Dictionary<string, string>? SnapshotMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        string parameterName)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var copy = new Dictionary<string, string>(metadata.Count, StringComparer.Ordinal);
        foreach (var (key, value) in metadata)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Service metadata key cannot be empty.", parameterName);
            }

            if (value is null)
            {
                throw new ArgumentException($"Service metadata value cannot be null. Key: {key}.", parameterName);
            }

            copy.Add(key, value);
        }

        return copy;
    }

    private static Dictionary<string, string> EmptyMetadata()
    {
        return new Dictionary<string, string>(0, StringComparer.Ordinal);
    }

    private sealed record Subscription(
        YuoServiceQuery Query,
        Action<YuoServiceChange> OnChange);

    private sealed class SubscriptionHandle(
        ConcurrentDictionary<long, Subscription> subscriptions,
        long id) : IDisposable
    {
        public void Dispose()
        {
            subscriptions.TryRemove(id, out _);
        }
    }
}
