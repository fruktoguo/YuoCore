using System.Net.WebSockets;
using Google.Protobuf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Client;
using YuoCore.Client.StateSync;
using YuoCore.Game;
using YuoCore.Protocol;

namespace YuoCore.Backend.Tests;

public sealed class WebSocketEndpointEndToEndTests
{
    [Fact]
    public async Task WebSocket_endpoint_accepts_yuommo_client_v1_loop_without_client_session_id()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Storage:PostgresEnabled"] = "false",
                        ["Storage:RedisEnabled"] = "false",
                        ["Storage:DurableOutboxEnabled"] = "false",
                        ["Game:GatewayRequestsUseCommandQueue"] = "true",
                        ["Gateway:WebSocketPush:PollIntervalMs"] = "60000"
                    });
                });
            });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.SubProtocols.Add("yuocore.v1");
        using var socket = await webSocketClient.ConnectAsync(new Uri("ws://localhost/ws"), cts.Token);
        using var transport = new TestServerWebSocketTransport(socket);
        var client = new YuoMmoClient(
            transport,
            new YuoClientSession(new YuoClientSessionOptions
            {
                RequestTimeout = TimeSpan.FromSeconds(5)
            }));

        var welcome = await client.LoginAsync(cancellationToken: cts.Token);
        var enter = await client.EnterMapAsync(welcome.PlayerId, 1001, cts.Token);
        var move = await client.MoveAsync(welcome.PlayerId, 11, 13, 0, 21, cts.Token);
        var disconnected = await client.DisconnectAsync(welcome.PlayerId, cancellationToken: cts.Token);

        Assert.True(Guid.TryParse(welcome.PlayerId, out _));
        Assert.Equal(1001, enter.MapId);
        Assert.Contains(enter.Players, player => player.PlayerId == welcome.PlayerId);
        Assert.Equal(1001, move.MapId);
        Assert.Contains(move.Players, player =>
            player.PlayerId == welcome.PlayerId &&
            Math.Abs(player.X - 13) < 0.001f &&
            Math.Abs(player.Z - 21) < 0.001f &&
            player.LastInputSeq == 11);
        Assert.Equal(welcome.PlayerId, disconnected.PlayerId);
        Assert.False(disconnected.Online);
        Assert.Equal(1001, disconnected.MapId);
        Assert.Equal(13, disconnected.X);
        Assert.Equal(21, disconnected.Z);
        Assert.Equal(11UL, disconnected.LastInputSeq);
    }

    [Fact]
    public async Task WebSocket_endpoint_restores_saved_player_after_backend_restart()
    {
        var store = new InMemoryPlayerSnapshotStore();
        string playerId;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using (var firstFactory = CreateFactory(pushPollIntervalMs: 60000, store))
        {
            using var socket = await ConnectSocketAsync(firstFactory, cts.Token);
            using var transport = new TestServerWebSocketTransport(socket);
            var client = new YuoMmoClient(transport);

            var welcome = await client.LoginAsync(cancellationToken: cts.Token);
            playerId = welcome.PlayerId;
            await client.EnterMapAsync(playerId, 2002, cts.Token);
            await client.MoveAsync(playerId, 77, 19, 0, 23, cts.Token);
            var disconnected = await client.DisconnectAsync(playerId, cancellationToken: cts.Token);

            Assert.False(disconnected.Online);
            Assert.Equal(2002, disconnected.MapId);
            Assert.Equal(19, disconnected.X);
            Assert.Equal(23, disconnected.Z);
            Assert.Equal(77UL, disconnected.LastInputSeq);
            Assert.True(store.TryGetSaved(Guid.Parse(playerId), out var saved));
            Assert.False(saved.Online);
        }

        await using (var secondFactory = CreateFactory(pushPollIntervalMs: 60000, store))
        {
            using var socket = await ConnectSocketAsync(secondFactory, cts.Token);
            using var transport = new TestServerWebSocketTransport(socket);
            var client = new YuoMmoClient(transport);

            var welcome = await client.LoginAsync(playerId, cts.Token);
            var restored = await client.GetSnapshotAsync(welcome.PlayerId, cts.Token);

            Assert.Equal(playerId, welcome.PlayerId);
            Assert.True(restored.Online);
            Assert.Equal(2002, restored.MapId);
            Assert.Equal(19, restored.X);
            Assert.Equal(23, restored.Z);
            Assert.Equal(77UL, restored.LastInputSeq);
        }
    }

    [Fact]
    public async Task WebSocket_endpoint_persists_offline_snapshot_when_socket_closes_without_disconnect()
    {
        var store = new InMemoryPlayerSnapshotStore();
        string playerId;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using (var firstFactory = CreateFactory(pushPollIntervalMs: 60000, store))
        {
            using var socket = await ConnectSocketAsync(firstFactory, cts.Token);
            using var transport = new TestServerWebSocketTransport(socket);
            var client = new YuoMmoClient(transport);

            var welcome = await client.LoginAsync(cancellationToken: cts.Token);
            playerId = welcome.PlayerId;
            await client.EnterMapAsync(playerId, 3003, cts.Token);
            await client.MoveAsync(playerId, 88, 27, 0, 31, cts.Token);

            socket.Abort();

            var saved = await WaitForSavedSnapshotAsync(
                store,
                Guid.Parse(playerId),
                snapshot => !snapshot.Online &&
                            snapshot.MapId == 3003 &&
                            Math.Abs(snapshot.X - 27) < 0.001f &&
                            Math.Abs(snapshot.Z - 31) < 0.001f &&
                            snapshot.LastInputSeq == 88,
                cts.Token);

            Assert.False(saved.Online);
            Assert.Equal(3003, saved.MapId);
            Assert.Equal(88UL, saved.LastInputSeq);
        }

        await using (var secondFactory = CreateFactory(pushPollIntervalMs: 60000, store))
        {
            using var socket = await ConnectSocketAsync(secondFactory, cts.Token);
            using var transport = new TestServerWebSocketTransport(socket);
            var client = new YuoMmoClient(transport);

            var welcome = await client.LoginAsync(playerId, cts.Token);
            var restored = await client.GetSnapshotAsync(welcome.PlayerId, cts.Token);

            Assert.Equal(playerId, welcome.PlayerId);
            Assert.True(restored.Online);
            Assert.Equal(3003, restored.MapId);
            Assert.Equal(27, restored.X);
            Assert.Equal(31, restored.Z);
            Assert.Equal(88UL, restored.LastInputSeq);
        }
    }

    [Fact]
    public async Task WebSocket_endpoint_pushes_state_delta_to_observer_client()
    {
        using var factory = CreateFactory(pushPollIntervalMs: 5);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var observer = await TestServerDuplexClient.ConnectAsync(factory, cts.Token);
        using var mover = await TestServerDuplexClient.ConnectAsync(factory, cts.Token);
        var observerReceiver = new YuoClientStateSyncReceiver();

        var observerWelcome = await observer.CallAsync(new HelloRequest(), Welcome.Parser, cts.Token);
        await observer.CallAsync(
            new EnterMapRequest { PlayerId = observerWelcome.PlayerId, MapId = 1001 },
            WorldSnapshot.Parser,
            cts.Token);
        var observerSnapshot = await observer.ReceiveStateSyncAsync(
            observerReceiver,
            result => result.Kind == YuoClientStateSyncMessageKind.Snapshot,
            cts.Token);

        var moverWelcome = await mover.CallAsync(new HelloRequest(), Welcome.Parser, cts.Token);
        await mover.CallAsync(
            new EnterMapRequest { PlayerId = moverWelcome.PlayerId, MapId = 1001 },
            WorldSnapshot.Parser,
            cts.Token);
        await observer.ReceiveStateSyncAsync(
            observerReceiver,
            result => result.Kind == YuoClientStateSyncMessageKind.Delta &&
                      TryFindPlayer(observerReceiver, moverWelcome.PlayerId, out _),
            cts.Token);

        await mover.CallAsync(
            new MoveRequest
            {
                PlayerId = moverWelcome.PlayerId,
                InputSeq = 12,
                X = 31,
                Y = 0,
                Z = 42
            },
            WorldSnapshot.Parser,
            cts.Token);
        var moveDelta = await observer.ReceiveStateSyncAsync(
            observerReceiver,
            result => result.Kind == YuoClientStateSyncMessageKind.Delta &&
                      TryFindPlayer(observerReceiver, moverWelcome.PlayerId, out var state) &&
                      Math.Abs(state.X - 31) < 0.001f &&
                      Math.Abs(state.Z - 42) < 0.001f &&
                      state.LastInputSeq == 12,
            cts.Token);

        Assert.True(observerSnapshot.ApplyResult?.IsApplied);
        Assert.True(moveDelta.ApplyResult?.IsApplied);
        Assert.True(TryFindPlayer(observerReceiver, moverWelcome.PlayerId, out var moved));
        Assert.Equal(31, moved.X);
        Assert.Equal(42, moved.Z);
        Assert.Equal(12UL, moved.LastInputSeq);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        int pushPollIntervalMs,
        InMemoryPlayerSnapshotStore? store = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Storage:PostgresEnabled"] = "false",
                        ["Storage:RedisEnabled"] = "false",
                        ["Storage:DurableOutboxEnabled"] = "false",
                        ["Game:GatewayRequestsUseCommandQueue"] = "true",
                        ["Gateway:WebSocketPush:PollIntervalMs"] = pushPollIntervalMs.ToString()
                    });
                });

                if (store is not null)
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<IPlayerSnapshotRepository>();
                        services.RemoveAll<IPlayerSnapshotCache>();
                        services.AddSingleton<IPlayerSnapshotRepository>(store);
                        services.AddSingleton<IPlayerSnapshotCache>(store);
                    });
                }
            });
    }

    private static async Task<WebSocket> ConnectSocketAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken)
    {
        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.SubProtocols.Add("yuocore.v1");
        return await webSocketClient.ConnectAsync(new Uri("ws://localhost/ws"), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<YuoPlayerSnapshotDto> WaitForSavedSnapshotAsync(
        InMemoryPlayerSnapshotStore store,
        Guid playerId,
        Func<YuoPlayerSnapshotDto, bool> predicate,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (store.TryGetSaved(playerId, out var snapshot) && predicate(snapshot))
            {
                return snapshot;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for saved snapshot {playerId:N}.");
    }

    private static bool TryFindPlayer(
        YuoClientStateSyncReceiver receiver,
        string playerId,
        out PlayerWorldState state)
    {
        foreach (var item in receiver.Mirror.Table.States.Values)
        {
            if (!string.Equals(item.Kind, "player", StringComparison.Ordinal))
            {
                continue;
            }

            var player = PlayerWorldState.Parser.ParseFrom(item.Payload.Span);
            if (player.PlayerId == playerId)
            {
                state = player;
                return true;
            }
        }

        state = default!;
        return false;
    }

    private sealed class TestServerWebSocketTransport(WebSocket socket) : IYuoClientTransport, IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(
                    request.ToArray(),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken).ConfigureAwait(false);
                return await ReceiveOneMessageAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            _gate.Dispose();
        }

        private async ValueTask<ReadOnlyMemory<byte>> ReceiveOneMessageAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("WebSocket closed before a response was received.");
                }

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    throw new WebSocketException($"Expected binary message, got {result.MessageType}.");
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return stream.ToArray();
                }
            }
        }
    }

    private sealed class InMemoryPlayerSnapshotStore : IPlayerSnapshotRepository, IPlayerSnapshotCache
    {
        private readonly Dictionary<Guid, YuoPlayerSnapshotDto> _snapshots = new();
        private readonly object _gate = new();

        public Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _snapshots[snapshot.PlayerId] = snapshot;
            }

            return Task.CompletedTask;
        }

        public Task SetAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            return SaveAsync(snapshot, cancellationToken);
        }

        public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_snapshots.TryGetValue(playerId, out var snapshot)
                    ? snapshot
                    : null);
            }
        }

        public bool TryGetSaved(Guid playerId, out YuoPlayerSnapshotDto snapshot)
        {
            lock (_gate)
            {
                if (_snapshots.TryGetValue(playerId, out var stored))
                {
                    snapshot = stored;
                    return true;
                }

                snapshot = default!;
                return false;
            }
        }
    }

    private sealed class TestServerDuplexClient(WebSocket socket) : IDisposable
    {
        private readonly YuoClientSession _session = new(new YuoClientSessionOptions
        {
            RequestTimeout = TimeSpan.FromSeconds(5)
        });
        private readonly Queue<YuoServerEnvelopeV1> _pendingPushes = [];
        private readonly SemaphoreSlim _gate = new(1, 1);

        public static async Task<TestServerDuplexClient> ConnectAsync(
            WebApplicationFactory<Program> factory,
            CancellationToken cancellationToken)
        {
            var webSocketClient = factory.Server.CreateWebSocketClient();
            webSocketClient.SubProtocols.Add("yuocore.v1");
            var socket = await webSocketClient.ConnectAsync(new Uri("ws://localhost/ws"), cancellationToken)
                .ConfigureAwait(false);
            return new TestServerDuplexClient(socket);
        }

        public async ValueTask<TResponse> CallAsync<TRequest, TResponse>(
            TRequest request,
            MessageParser<TResponse> responseParser,
            CancellationToken cancellationToken)
            where TRequest : IMessage<TRequest>
            where TResponse : IMessage<TResponse>
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var outbound = _session.EncodeRequest(request);
                await socket.SendAsync(
                    outbound.Bytes.ToArray(),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken).ConfigureAwait(false);

                while (true)
                {
                    var envelope = await ReceiveEnvelopeAsync(cancellationToken).ConfigureAwait(false);
                    if (envelope.RequestId == outbound.RequestId)
                    {
                        return _session.DecodeResponse(envelope.ToByteArray(), responseParser, outbound.RequestId).Payload;
                    }

                    _pendingPushes.Enqueue(envelope);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async ValueTask<YuoClientStateSyncReceiveResult> ReceiveStateSyncAsync(
            YuoClientStateSyncReceiver receiver,
            Func<YuoClientStateSyncReceiveResult, bool> predicate,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var envelope = _pendingPushes.Count > 0
                    ? _pendingPushes.Dequeue()
                    : await ReceiveEnvelopeAsync(cancellationToken).ConfigureAwait(false);
                var result = receiver.ApplyServerEnvelope(envelope);
                if (predicate(result))
                {
                    return result;
                }
            }
        }

        public void Dispose()
        {
            socket.Dispose();
            _gate.Dispose();
        }

        private async ValueTask<YuoServerEnvelopeV1> ReceiveEnvelopeAsync(CancellationToken cancellationToken)
        {
            var bytes = await ReceiveOneMessageAsync(cancellationToken).ConfigureAwait(false);
            return YuoServerEnvelopeV1.Parser.ParseFrom(bytes.Span);
        }

        private async ValueTask<ReadOnlyMemory<byte>> ReceiveOneMessageAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("WebSocket closed before a response was received.");
                }

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    throw new WebSocketException($"Expected binary message, got {result.MessageType}.");
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return stream.ToArray();
                }
            }
        }
    }
}
