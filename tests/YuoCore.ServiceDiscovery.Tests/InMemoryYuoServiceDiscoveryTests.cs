using YuoCore.Abstractions;
using YuoCore.ServiceDiscovery;

namespace YuoCore.ServiceDiscovery.Tests;

public sealed class InMemoryYuoServiceDiscoveryTests
{
    [Fact]
    public async Task Register_query_and_unregister_service()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://map-1",
            new Dictionary<string, string> { ["zone"] = "1" });

        await discovery.RegisterAsync(
            "gate-1",
            "Gateway",
            "ws://127.0.0.1:8001");

        var maps = await discovery.QueryAsync(new YuoServiceQuery(
            Kind: "Map",
            Metadata: new Dictionary<string, string> { ["zone"] = "1" }));

        Assert.Single(maps);
        Assert.Equal("map-1", maps[0].Name);

        Assert.True(await discovery.UnregisterAsync("map-1"));
        Assert.Empty(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
    }

    [Fact]
    public async Task Subscribe_receives_matching_changes_only()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var changes = new List<YuoServiceChange>();

        using var subscription = discovery.Subscribe(
            new YuoServiceQuery(Kind: "Map"),
            changes.Add);

        await discovery.RegisterAsync("gate-1", "Gateway", "ws://127.0.0.1:8001");
        await discovery.RegisterAsync("map-1", "Map", "local://map-1");
        await discovery.UnregisterAsync("map-1");

        Assert.Equal(2, changes.Count);
        Assert.Equal(YuoServiceChangeKind.Registered, changes[0].Kind);
        Assert.Equal(YuoServiceChangeKind.Removed, changes[1].Kind);
    }

    [Fact]
    public async Task Subscribe_isolates_callback_errors_and_notifies_remaining_subscribers()
    {
        var errors = new List<(Exception Exception, YuoServiceChange Change)>();
        var discovery = new InMemoryYuoServiceDiscovery(
            subscriptionErrorHandler: (exception, change) => errors.Add((exception, change)));
        var changes = new List<YuoServiceChange>();

        using var broken = discovery.Subscribe(
            new YuoServiceQuery(Kind: "Map"),
            _ => throw new InvalidOperationException("expected subscriber failure."));
        using var healthy = discovery.Subscribe(
            new YuoServiceQuery(Kind: "Map"),
            changes.Add);

        await discovery.RegisterAsync("map-1", "Map", "local://map-1");

        var change = Assert.Single(changes);
        Assert.Equal(YuoServiceChangeKind.Registered, change.Kind);
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Equal("map-1", error.Change.Service.Name);
    }

    [Fact]
    public async Task Query_prunes_expired_leased_services()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var discovery = new InMemoryYuoServiceDiscovery(clock);

        var registered = await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://map-1",
            leaseTtl: TimeSpan.FromSeconds(5));

        Assert.Equal(clock.UtcNow.AddSeconds(5), registered.LeaseExpiresAt);
        Assert.Equal(TimeSpan.FromSeconds(5), registered.LeaseTtl);

        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        var maps = await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map"));

        Assert.Empty(maps);
    }

    [Fact]
    public async Task Renew_extends_existing_lease_ttl()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var discovery = new InMemoryYuoServiceDiscovery(clock);

        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://map-1",
            leaseTtl: TimeSpan.FromSeconds(5));

        clock.UtcNow = clock.UtcNow.AddSeconds(3);

        Assert.True(await discovery.RenewAsync("map-1"));
        var afterRenew = Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
        Assert.Equal(clock.UtcNow.AddSeconds(5), afterRenew.LeaseExpiresAt);

        clock.UtcNow = clock.UtcNow.AddSeconds(4);
        Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
    }

    [Fact]
    public async Task PruneExpiredAsync_publishes_removed_changes()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var discovery = new InMemoryYuoServiceDiscovery(clock);
        var changes = new List<YuoServiceChange>();
        using var subscription = discovery.Subscribe(new YuoServiceQuery(Kind: "Map"), changes.Add);

        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://map-1",
            leaseTtl: TimeSpan.FromSeconds(1));

        clock.UtcNow = clock.UtcNow.AddSeconds(2);
        var pruned = await discovery.PruneExpiredAsync();

        Assert.Equal(1, pruned);
        Assert.Equal(
            new[] { YuoServiceChangeKind.Registered, YuoServiceChangeKind.Removed },
            changes.Select(change => change.Kind));
    }

    [Fact]
    public async Task Services_without_lease_do_not_expire()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var discovery = new InMemoryYuoServiceDiscovery(clock);

        await discovery.RegisterAsync("gateway-1", "Gateway", "local://gateway-1");
        clock.UtcNow = clock.UtcNow.AddDays(1);

        Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Gateway")));
        Assert.Equal(0, await discovery.PruneExpiredAsync());
    }

    [Fact]
    public async Task Register_after_expiry_publishes_removed_then_registered()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var discovery = new InMemoryYuoServiceDiscovery(clock);
        var changes = new List<YuoServiceChange>();
        using var subscription = discovery.Subscribe(new YuoServiceQuery(Kind: "Map"), changes.Add);

        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://old",
            leaseTtl: TimeSpan.FromSeconds(1));

        clock.UtcNow = clock.UtcNow.AddSeconds(2);
        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://new",
            leaseTtl: TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[]
            {
                YuoServiceChangeKind.Registered,
                YuoServiceChangeKind.Removed,
                YuoServiceChangeKind.Registered
            },
            changes.Select(change => change.Kind));
        Assert.Equal("local://new", Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map"))).Endpoint);
    }

    [Fact]
    public async Task Register_same_name_with_different_kind_notifies_old_and_new_subscribers()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var mapChanges = new List<YuoServiceChange>();
        var gatewayChanges = new List<YuoServiceChange>();
        using var mapSubscription = discovery.Subscribe(new YuoServiceQuery(Kind: "Map"), mapChanges.Add);
        using var gatewaySubscription = discovery.Subscribe(new YuoServiceQuery(Kind: "Gateway"), gatewayChanges.Add);

        await discovery.RegisterAsync("service-1", "Map", "local://map");
        await discovery.RegisterAsync("service-1", "Gateway", "local://gateway");

        Assert.Equal(
            [YuoServiceChangeKind.Registered, YuoServiceChangeKind.Removed],
            mapChanges.Select(change => change.Kind));
        Assert.Equal(
            [YuoServiceChangeKind.Registered],
            gatewayChanges.Select(change => change.Kind));
        Assert.Empty(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
        Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Gateway")));
    }

    [Fact]
    public async Task Register_same_name_with_different_metadata_notifies_old_and_new_subscribers()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var zone1Changes = new List<YuoServiceChange>();
        var zone2Changes = new List<YuoServiceChange>();
        using var zone1Subscription = discovery.Subscribe(
            new YuoServiceQuery(
                Kind: "Map",
                Metadata: new Dictionary<string, string> { ["zone"] = "1" }),
            zone1Changes.Add);
        using var zone2Subscription = discovery.Subscribe(
            new YuoServiceQuery(
                Kind: "Map",
                Metadata: new Dictionary<string, string> { ["zone"] = "2" }),
            zone2Changes.Add);

        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://map",
            new Dictionary<string, string> { ["zone"] = "1" });
        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://map",
            new Dictionary<string, string> { ["zone"] = "2" });

        Assert.Equal(
            [YuoServiceChangeKind.Registered, YuoServiceChangeKind.Removed],
            zone1Changes.Select(change => change.Kind));
        Assert.Equal(
            [YuoServiceChangeKind.Registered],
            zone2Changes.Select(change => change.Kind));
        Assert.Empty(await discovery.QueryAsync(new YuoServiceQuery(
            Kind: "Map",
            Metadata: new Dictionary<string, string> { ["zone"] = "1" })));
        Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(
            Kind: "Map",
            Metadata: new Dictionary<string, string> { ["zone"] = "2" })));
    }

    [Fact]
    public async Task Subscribe_snapshots_query_metadata()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var metadata = new Dictionary<string, string> { ["zone"] = "1" };
        var changes = new List<YuoServiceChange>();

        using var subscription = discovery.Subscribe(
            new YuoServiceQuery(Kind: "Map", Metadata: metadata),
            changes.Add);

        metadata["zone"] = "2";

        await discovery.RegisterAsync(
            "map-1",
            "Map",
            "local://map-1",
            new Dictionary<string, string> { ["zone"] = "1" });
        await discovery.RegisterAsync(
            "map-2",
            "Map",
            "local://map-2",
            new Dictionary<string, string> { ["zone"] = "2" });

        var change = Assert.Single(changes);
        Assert.Equal("map-1", change.Service.Name);
    }

    [Fact]
    public async Task Query_rejects_null_query()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await discovery.QueryAsync(null!));
    }

    [Fact]
    public void Subscribe_rejects_null_query()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        Assert.Throws<ArgumentNullException>(() =>
            discovery.Subscribe(null!, _ => { }));
    }

    [Fact]
    public async Task Register_rejects_invalid_lease_ttl()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await discovery.RegisterAsync(
                "map-1",
                "Map",
                "local://map-1",
                leaseTtl: TimeSpan.Zero));
    }

    [Fact]
    public async Task Register_rejects_empty_metadata_key()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await discovery.RegisterAsync(
                "map-1",
                "Map",
                "local://map-1",
                new Dictionary<string, string> { [""] = "1" }));
    }

    [Fact]
    public async Task Register_rejects_null_metadata_value()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await discovery.RegisterAsync(
                "map-1",
                "Map",
                "local://map-1",
                new Dictionary<string, string> { ["zone"] = null! }));
    }

    [Fact]
    public async Task Query_rejects_invalid_metadata_filter()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await discovery.QueryAsync(new YuoServiceQuery(
                Metadata: new Dictionary<string, string> { ["zone"] = null! })));
    }

    [Fact]
    public void Subscribe_rejects_invalid_metadata_filter()
    {
        var discovery = new InMemoryYuoServiceDiscovery();

        Assert.Throws<ArgumentException>(() =>
            discovery.Subscribe(
                new YuoServiceQuery(Metadata: new Dictionary<string, string> { [""] = "1" }),
                _ => { }));
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : IYuoClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
