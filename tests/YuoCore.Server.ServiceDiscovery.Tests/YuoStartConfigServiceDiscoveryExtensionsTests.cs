using YuoCore.Abstractions;
using YuoCore.Config;
using YuoCore.ServiceDiscovery;
using YuoCore.Server.ServiceDiscovery;

namespace YuoCore.Server.ServiceDiscovery.Tests;

public sealed class YuoStartConfigServiceDiscoveryExtensionsTests
{
    [Fact]
    public async Task RegisterStartConfigServicesAsync_publishes_services_with_process_metadata()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var config = CreateConfig();

        var registered = await discovery.RegisterStartConfigServicesAsync(config);

        Assert.Equal(2, registered.Count);
        var maps = await discovery.QueryAsync(new YuoServiceQuery(
            Kind: "Map",
            Metadata: new Dictionary<string, string> { ["zone"] = "1" }));

        var map = Assert.Single(maps);
        Assert.Equal("map-1", map.Name);
        Assert.Equal("inner://127.0.0.1:7001", map.Endpoint);
        Assert.Equal("node-a", map.Metadata["nodeId"]);
        Assert.Equal("game-a", map.Metadata["processId"]);
        Assert.Equal("Game", map.Metadata["processKind"]);
        Assert.Equal("1", map.Metadata["zone"]);
    }

    [Fact]
    public async Task RegisterStartConfigServicesAsync_uses_outer_endpoint_for_gateway_without_service_endpoint()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var config = CreateConfig();

        await discovery.RegisterStartConfigServicesAsync(config);
        var gateways = await discovery.QueryAsync(new YuoServiceQuery(Kind: "Gateway"));

        var gateway = Assert.Single(gateways);
        Assert.Equal("ws://127.0.0.1:8001/ws", gateway.Endpoint);
    }

    [Fact]
    public async Task UnregisterStartConfigServicesAsync_removes_registered_services()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var config = CreateConfig();

        await discovery.RegisterStartConfigServicesAsync(config);
        var removed = await discovery.UnregisterStartConfigServicesAsync(config);

        Assert.Equal(2, removed);
        Assert.Empty(await discovery.QueryAsync(new YuoServiceQuery()));
    }

    [Fact]
    public async Task RegisterStartConfigServicesAsync_can_publish_leased_services_and_renew_them()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var discovery = new InMemoryYuoServiceDiscovery(clock);
        var config = CreateConfig();

        await discovery.RegisterStartConfigServicesAsync(config, TimeSpan.FromSeconds(5));
        var map = Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
        Assert.Equal(TimeSpan.FromSeconds(5), map.LeaseTtl);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(5), map.LeaseExpiresAt);

        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        Assert.Equal(2, await discovery.RenewStartConfigServicesAsync(config));

        map = Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
        Assert.Equal(clock.UtcNow.AddSeconds(5), map.LeaseExpiresAt);

        clock.UtcNow = clock.UtcNow.AddSeconds(4);
        Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
    }

    [Fact]
    public async Task RegisterStartConfigServicesAsync_can_use_service_specific_lease_ttl()
    {
        var clock = new ManualClock(DateTimeOffset.UnixEpoch);
        var discovery = new InMemoryYuoServiceDiscovery(clock);
        var config = CreateConfig();

        await discovery.RegisterStartConfigServicesAsync(config, SelectLeaseTtl);

        var map = Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
        var gateway = Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Gateway")));
        Assert.Equal(TimeSpan.FromSeconds(30), map.LeaseTtl);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(30), map.LeaseExpiresAt);
        Assert.Equal(TimeSpan.FromSeconds(5), gateway.LeaseTtl);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(5), gateway.LeaseExpiresAt);

        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        Assert.Equal(2, await discovery.RenewStartConfigServicesAsync(config, SelectLeaseTtl));

        map = Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Map")));
        gateway = Assert.Single(await discovery.QueryAsync(new YuoServiceQuery(Kind: "Gateway")));
        Assert.Equal(clock.UtcNow.AddSeconds(30), map.LeaseExpiresAt);
        Assert.Equal(clock.UtcNow.AddSeconds(5), gateway.LeaseExpiresAt);

        static TimeSpan? SelectLeaseTtl(YuoServiceConfig service)
        {
            return service.Kind == "Gateway"
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromSeconds(30);
        }
    }

    [Fact]
    public async Task RegisterStartConfigServicesAsync_resolves_all_endpoints_before_registering_any_service()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var config = CreateConfig() with
        {
            InnerEndpoint = "",
            OuterEndpoint = "",
            Services =
            [
                new YuoServiceConfig
                {
                    Name = "map-1",
                    Kind = "Map",
                    Endpoint = "local://map-1"
                },
                new YuoServiceConfig
                {
                    Name = "map-2",
                    Kind = "Map"
                }
            ]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await discovery.RegisterStartConfigServicesAsync(config));

        Assert.Contains("has no endpoint", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await discovery.QueryAsync(new YuoServiceQuery()));
    }

    [Fact]
    public async Task RegisterStartConfigServicesAsync_validates_all_lease_ttls_before_registering_any_service()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var config = CreateConfig();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await discovery.RegisterStartConfigServicesAsync(
                config,
                service => service.Kind == "Gateway"
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds(30)));

        Assert.Contains("lease ttl", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await discovery.QueryAsync(new YuoServiceQuery()));
    }

    [Fact]
    public async Task RegisterStartConfigServicesAsync_honors_cancellation_before_registration()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var config = CreateConfig();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await discovery.RegisterStartConfigServicesAsync(config, cts.Token));

        Assert.Empty(await discovery.QueryAsync(new YuoServiceQuery()));
    }

    [Fact]
    public async Task UnregisterStartConfigServicesAsync_honors_cancellation_before_removal()
    {
        var discovery = new InMemoryYuoServiceDiscovery();
        var config = CreateConfig();
        await discovery.RegisterStartConfigServicesAsync(config);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await discovery.UnregisterStartConfigServicesAsync(config, cts.Token));

        Assert.Equal(2, (await discovery.QueryAsync(new YuoServiceQuery())).Count);
    }

    private static YuoStartConfig CreateConfig()
    {
        return new YuoStartConfig
        {
            NodeId = "node-a",
            ProcessId = "game-a",
            ProcessKind = "Game",
            InnerEndpoint = "inner://127.0.0.1:7001",
            OuterEndpoint = "ws://127.0.0.1:8001/ws",
            Metadata = new Dictionary<string, string> { ["region"] = "dev" },
            Fibers =
            [
                new YuoFiberConfig { Id = 1, Name = "Map-1", Kind = "Map" }
            ],
            Services =
            [
                new YuoServiceConfig
                {
                    Name = "map-1",
                    Kind = "Map",
                    Metadata = new Dictionary<string, string> { ["zone"] = "1" }
                },
                new YuoServiceConfig
                {
                    Name = "gateway-1",
                    Kind = "Gateway"
                }
            ]
        };
    }

    private sealed class ManualClock(DateTimeOffset utcNow) : IYuoClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
