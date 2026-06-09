using YuoCore.Location;
using YuoCore.Network;
using YuoCore.Protocol;

namespace YuoCore.Network.Tests;

public sealed class ProcessAwareYuoTransportTests
{
    [Fact]
    public async Task RoutesLocalAddressToLocalTransport()
    {
        var local = new InMemoryYuoTransport<YuoActorAddress>();
        var remote = new InMemoryYuoTransport<YuoActorAddress>();
        var address = new YuoActorAddress("node", "game-1", 1, 100);
        var endpoint = new RecordingEndpoint();
        local.Register(address, endpoint);

        var router = new ProcessAwareYuoTransport("game-1", local, remote);
        await router.SendAsync(address, YuoProtocolEnvelope.Message(1, 100, ReadOnlyMemory<byte>.Empty));

        Assert.Equal(1, endpoint.Received);
    }

    [Fact]
    public async Task RoutesRemoteAddressToRemoteTransport()
    {
        var local = new InMemoryYuoTransport<YuoActorAddress>();
        var remoteInner = new InMemoryYuoTransport<YuoActorAddress>();
        var remote = new SerializedYuoTransport<YuoActorAddress>(remoteInner);
        var address = new YuoActorAddress("node", "game-2", 1, 100);
        var endpoint = new RecordingEndpoint();
        remoteInner.Register(address, endpoint);

        var router = new ProcessAwareYuoTransport("game-1", local, remote);
        var response = await router.InvokeAsync(address, YuoProtocolEnvelope.Request(1, 100, ReadOnlyMemory<byte>.Empty));

        Assert.Equal(1, endpoint.Invoked);
        Assert.Equal(YuoMessageKind.Response, response.Kind);
    }

    [Fact]
    public async Task ThrowsWhenRemoteTransportIsMissing()
    {
        var local = new InMemoryYuoTransport<YuoActorAddress>();
        var router = new ProcessAwareYuoTransport("game-1", local);
        var address = new YuoActorAddress("node", "game-2", 1, 100);

        await Assert.ThrowsAsync<YuoTransportException>(async () =>
            await router.SendAsync(address, YuoProtocolEnvelope.Message(1, 100, ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public async Task InMemoryTransport_throws_transport_exception_when_endpoint_is_missing()
    {
        var transport = new InMemoryYuoTransport<YuoActorAddress>();
        var address = new YuoActorAddress("node", "game-1", 1, 100);

        var exception = await Assert.ThrowsAsync<YuoTransportException>(async () =>
            await transport.SendAsync(address, YuoProtocolEnvelope.Message(1, 100, ReadOnlyMemory<byte>.Empty)));

        Assert.Contains("No endpoint registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAwareTransport_preserves_local_missing_endpoint_as_transport_exception()
    {
        var local = new InMemoryYuoTransport<YuoActorAddress>();
        var router = new ProcessAwareYuoTransport("game-1", local);
        var address = new YuoActorAddress("node", "game-1", 1, 100);

        var exception = await Assert.ThrowsAsync<YuoTransportException>(async () =>
            await router.InvokeAsync(address, YuoProtocolEnvelope.Request(1, 100, ReadOnlyMemory<byte>.Empty)));

        Assert.Contains("No endpoint registered", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SerializedRemoteTransportBreaksPayloadReferenceSharing()
    {
        var remoteInner = new InMemoryYuoTransport<YuoActorAddress>();
        var remote = new SerializedYuoTransport<YuoActorAddress>(remoteInner);
        var address = new YuoActorAddress("node", "game-2", 1, 100);
        remoteInner.Register(address, new EchoPayloadEndpoint());

        var payload = new byte[] { 1, 2, 3 };
        var response = await remote.InvokeAsync(address, YuoProtocolEnvelope.Request(7, 100, payload));
        payload[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, response.Payload.ToArray());
    }

    [Fact]
    public async Task InMemoryTransportHonorsCancellationBeforeEndpointDispatch()
    {
        var transport = new InMemoryYuoTransport<YuoActorAddress>();
        var address = new YuoActorAddress("node", "game-1", 1, 100);
        var endpoint = new RecordingEndpoint();
        transport.Register(address, endpoint);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await transport.SendAsync(address, YuoProtocolEnvelope.Message(1, 100, ReadOnlyMemory<byte>.Empty), cts.Token));

        Assert.Equal(0, endpoint.Received);
    }

    [Fact]
    public void InMemoryTransport_rejects_null_endpoint()
    {
        var transport = new InMemoryYuoTransport<YuoActorAddress>();
        var address = new YuoActorAddress("node", "game-1", 1, 100);

        Assert.Throws<ArgumentNullException>(() => transport.Register(address, null!));
    }

    [Fact]
    public async Task InMemoryTransport_rejects_null_envelope_before_endpoint_lookup()
    {
        var transport = new InMemoryYuoTransport<YuoActorAddress>();
        var address = new YuoActorAddress("node", "game-1", 1, 100);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await transport.SendAsync(address, null!));
    }

    [Fact]
    public async Task SerializedTransportHonorsCancellationBeforeInnerTransport()
    {
        var inner = new RecordingTransport<YuoActorAddress>();
        var transport = new SerializedYuoTransport<YuoActorAddress>(inner);
        var address = new YuoActorAddress("node", "game-2", 1, 100);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await transport.InvokeAsync(address, YuoProtocolEnvelope.Request(1, 100, ReadOnlyMemory<byte>.Empty), cts.Token));

        Assert.Equal(0, inner.Invoked);
    }

    [Fact]
    public void SerializedTransport_rejects_null_inner_transport()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SerializedYuoTransport<YuoActorAddress>(null!));
    }

    [Fact]
    public async Task SerializedTransport_rejects_null_envelope_before_inner_transport()
    {
        var inner = new RecordingTransport<YuoActorAddress>();
        var transport = new SerializedYuoTransport<YuoActorAddress>(inner);
        var address = new YuoActorAddress("node", "game-2", 1, 100);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await transport.InvokeAsync(address, null!));

        Assert.Equal(0, inner.Invoked);
    }

    [Fact]
    public async Task ProcessAwareTransportHonorsCancellationBeforeRemoteSelection()
    {
        var local = new InMemoryYuoTransport<YuoActorAddress>();
        var router = new ProcessAwareYuoTransport("game-1", local);
        var address = new YuoActorAddress("node", "game-2", 1, 100);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await router.SendAsync(address, YuoProtocolEnvelope.Message(1, 100, ReadOnlyMemory<byte>.Empty), cts.Token));
    }

    [Fact]
    public void ProcessAwareTransport_rejects_invalid_constructor_arguments()
    {
        var local = new InMemoryYuoTransport<YuoActorAddress>();

        Assert.Throws<ArgumentException>(() => new ProcessAwareYuoTransport("", local));
        Assert.Throws<ArgumentNullException>(() => new ProcessAwareYuoTransport("game-1", null!));
    }

    [Fact]
    public async Task ProcessAwareTransport_rejects_null_envelope_before_route_selection()
    {
        var local = new InMemoryYuoTransport<YuoActorAddress>();
        var router = new ProcessAwareYuoTransport("game-1", local);
        var remoteAddress = new YuoActorAddress("node", "game-2", 1, 100);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await router.SendAsync(remoteAddress, null!));
    }

    private sealed class RecordingEndpoint : IYuoEndpoint
    {
        public int Received { get; private set; }

        public int Invoked { get; private set; }

        public ValueTask ReceiveAsync(YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Received++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<YuoProtocolEnvelope> InvokeAsync(YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Invoked++;
            return ValueTask.FromResult(YuoProtocolEnvelope.Response(envelope, ReadOnlyMemory<byte>.Empty));
        }
    }

    private sealed class EchoPayloadEndpoint : IYuoEndpoint
    {
        public ValueTask ReceiveAsync(YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<YuoProtocolEnvelope> InvokeAsync(YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(YuoProtocolEnvelope.Response(envelope, envelope.Payload));
        }
    }

    private sealed class RecordingTransport<TAddress> : IYuoTransport<TAddress>
        where TAddress : notnull
    {
        public int Invoked { get; private set; }

        public ValueTask SendAsync(
            TAddress address,
            YuoProtocolEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public ValueTask<YuoProtocolEnvelope> InvokeAsync(
            TAddress address,
            YuoProtocolEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Invoked++;
            return ValueTask.FromResult(YuoProtocolEnvelope.Response(envelope, ReadOnlyMemory<byte>.Empty));
        }
    }
}
