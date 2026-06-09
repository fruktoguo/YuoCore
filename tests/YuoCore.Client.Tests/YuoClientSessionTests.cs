using Google.Protobuf;
using YuoCore.Client;
using YuoCore.Protocol;

namespace YuoCore.Client.Tests;

public sealed class YuoClientSessionTests
{
    [Fact]
    public void Encode_request_wraps_payload_with_opcode_request_id_and_client_sequence()
    {
        var session = new YuoClientSession();

        var outbound = session.EncodeRequest(new ClickRequest
        {
            PlayerId = "player-1",
            Amount = 7
        });
        var envelope = YuoClientEnvelopeV1.Parser.ParseFrom(outbound.Bytes.Span);
        var payload = ClickRequest.Parser.ParseFrom(envelope.Payload);

        Assert.Equal(1UL, outbound.RequestId);
        Assert.Equal(1UL, outbound.ClientSeq);
        Assert.Equal(YuoCoreProtocolOpCodes.ClickRequest, outbound.OpCode);
        Assert.Equal((uint)YuoCoreProtocolOpCodes.ClickRequest, envelope.Opcode);
        Assert.Equal("player-1", payload.PlayerId);
        Assert.Equal(7, payload.Amount);
    }

    [Fact]
    public async Task Call_async_round_trips_v1_envelope_through_abstract_transport()
    {
        var session = new YuoClientSession();
        var transport = new LoopbackClientTransport(request =>
        {
            var hello = HelloRequest.Parser.ParseFrom(request.Payload);
            return new YuoServerEnvelopeV1
            {
                Version = request.Version,
                Opcode = YuoCoreProtocolOpCodes.Welcome,
                RequestId = request.RequestId,
                ServerSeq = 99,
                Payload = ByteString.CopyFrom(new Welcome
                {
                    PlayerId = string.IsNullOrWhiteSpace(hello.PlayerId) ? "created-player" : hello.PlayerId,
                    Message = "ok"
                }.ToByteArray())
            };
        });

        var response = await session.CallAsync(
            transport,
            new HelloRequest(),
            Welcome.Parser);

        Assert.Equal(1UL, response.RequestId);
        Assert.Equal(99UL, response.ServerSeq);
        Assert.Equal(YuoCoreProtocolOpCodes.Welcome, response.OpCode);
        Assert.Equal("created-player", response.Payload.PlayerId);
        Assert.Equal("ok", response.Payload.Message);
    }

    [Fact]
    public async Task Call_async_turns_server_error_envelope_into_protocol_exception()
    {
        var session = new YuoClientSession();
        var transport = new LoopbackClientTransport(request => new YuoServerEnvelopeV1
        {
            Version = request.Version,
            Opcode = YuoCoreProtocolOpCodes.ErrorResponse,
            RequestId = request.RequestId,
            Error = new ErrorResponse { Code = "invalid_player", Message = "player_id must be a guid" }
        });

        var error = await Assert.ThrowsAsync<YuoClientProtocolException>(async () =>
            await session.CallAsync(
                transport,
                new SnapshotRequest { PlayerId = "bad" },
                PlayerSnapshot.Parser));

        Assert.Equal("invalid_player", error.Code);
        Assert.Equal("player_id must be a guid", error.Message);
    }

    [Fact]
    public async Task Call_async_rejects_unexpected_response_type_before_network_send()
    {
        var session = new YuoClientSession();
        var transport = new CountingClientTransport();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.CallAsync(
                transport,
                new HelloRequest(),
                PlayerSnapshot.Parser));

        Assert.Equal(0, transport.SentCount);
    }

    [Fact]
    public async Task Call_async_applies_configured_request_timeout_to_transport()
    {
        var session = new YuoClientSession(new YuoClientSessionOptions
        {
            RequestTimeout = TimeSpan.FromMilliseconds(20)
        });
        var transport = new HangingClientTransport();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await session.CallAsync(
                transport,
                new HelloRequest(),
                Welcome.Parser));

        Assert.True(transport.ObservedCancellation);
    }

    [Fact]
    public async Task Mmo_client_facade_calls_common_game_requests_with_v1_envelopes()
    {
        var opCodes = new List<uint>();
        var transport = new LoopbackClientTransport(request =>
        {
            opCodes.Add(request.Opcode);
            return request.Opcode switch
            {
                YuoCoreProtocolOpCodes.HelloRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.Welcome,
                    new Welcome { PlayerId = "player-1", Message = "ok" }.ToByteArray()),
                YuoCoreProtocolOpCodes.ClickRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.PlayerSnapshot,
                    new PlayerSnapshot { PlayerId = "player-1", Clicks = 2, Gold = 20 }.ToByteArray()),
                YuoCoreProtocolOpCodes.EnterMapRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.WorldSnapshot,
                    new WorldSnapshot { MapId = 1001 }.ToByteArray()),
                YuoCoreProtocolOpCodes.MoveRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.WorldSnapshot,
                    new WorldSnapshot
                    {
                        MapId = 1001,
                        Players =
                        {
                            new PlayerWorldState
                            {
                                PlayerId = "player-1",
                                X = 3,
                                Z = 4,
                                LastInputSeq = 9
                            }
                        }
                    }.ToByteArray()),
                YuoCoreProtocolOpCodes.DisconnectRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.PlayerSnapshot,
                    new PlayerSnapshot { PlayerId = "player-1", Online = false }.ToByteArray()),
                YuoCoreProtocolOpCodes.StateSnapshotRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.StateSnapshot,
                    new StateSnapshotMessage { Tick = 99 }.ToByteArray()),
                YuoCoreProtocolOpCodes.StateSyncRecoverRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.StateSyncRecover,
                    new StateSyncRecoverResponse
                    {
                        Replayed = true,
                        Reason = "Replayed",
                        Deltas =
                        {
                            new StateDeltaMessage { Tick = 100, BaseTick = 99 }
                        }
                    }.ToByteArray()),
                YuoCoreProtocolOpCodes.StateSyncAckRequest => Reply(
                    request,
                    YuoCoreProtocolOpCodes.StateSyncAck,
                    new StateSyncAckResponse
                    {
                        Accepted = true,
                        AckTick = 100,
                        LatestTick = 100,
                        Reason = "accepted"
                    }.ToByteArray()),
                _ => throw new InvalidOperationException($"unexpected opcode {request.Opcode}")
            };
        });
        var client = new YuoMmoClient(transport);

        var login = await client.LoginAsync();
        var click = await client.ClickAsync(login.PlayerId, 2);
        var enter = await client.EnterMapAsync(login.PlayerId, 1001);
        var move = await client.MoveAsync(login.PlayerId, 9, 3, 0, 4);
        var disconnect = await client.DisconnectAsync(login.PlayerId, "session-a");
        var stateSnapshot = await client.RequestStateSnapshotAsync(login.PlayerId);
        var stateRecover = await client.RecoverStateSyncAsync(login.PlayerId, stateSnapshot.Tick);
        var stateAck = await client.AckStateSyncAsync(login.PlayerId, Assert.Single(stateRecover.Deltas).Tick);

        Assert.Equal("player-1", login.PlayerId);
        Assert.Equal(2, click.Clicks);
        Assert.Equal(20, click.Gold);
        Assert.Equal(1001, enter.MapId);
        Assert.Single(move.Players);
        Assert.Equal(9UL, move.Players[0].LastInputSeq);
        Assert.False(disconnect.Online);
        Assert.Equal(99, stateSnapshot.Tick);
        Assert.True(stateRecover.Replayed);
        Assert.Equal(100, Assert.Single(stateRecover.Deltas).Tick);
        Assert.True(stateAck.Accepted);
        Assert.Equal(100, stateAck.AckTick);
        Assert.Equal([
            (uint)YuoCoreProtocolOpCodes.HelloRequest,
            (uint)YuoCoreProtocolOpCodes.ClickRequest,
            (uint)YuoCoreProtocolOpCodes.EnterMapRequest,
            (uint)YuoCoreProtocolOpCodes.MoveRequest,
            (uint)YuoCoreProtocolOpCodes.DisconnectRequest,
            (uint)YuoCoreProtocolOpCodes.StateSnapshotRequest,
            (uint)YuoCoreProtocolOpCodes.StateSyncRecoverRequest,
            (uint)YuoCoreProtocolOpCodes.StateSyncAckRequest
        ], opCodes);
    }

    [Fact]
    public async Task Mmo_client_connection_reconnects_with_existing_player_id_and_closes_transports()
    {
        var transports = new List<IYuoClientTransport>();
        var closed = new List<IYuoClientTransport>();
        var connection = new YuoMmoClientConnection(
            _ =>
            {
                var transport = new LoopbackClientTransport(request =>
                {
                    return request.Opcode switch
                    {
                        YuoCoreProtocolOpCodes.HelloRequest => Reply(
                            request,
                            YuoCoreProtocolOpCodes.Welcome,
                            new Welcome
                            {
                                PlayerId = string.IsNullOrWhiteSpace(HelloRequest.Parser.ParseFrom(request.Payload).PlayerId)
                                    ? "player-1"
                                    : HelloRequest.Parser.ParseFrom(request.Payload).PlayerId,
                                Message = "ok"
                            }.ToByteArray()),
                        YuoCoreProtocolOpCodes.SnapshotRequest => Reply(
                            request,
                            YuoCoreProtocolOpCodes.PlayerSnapshot,
                            new PlayerSnapshot
                            {
                                PlayerId = SnapshotRequest.Parser.ParseFrom(request.Payload).PlayerId,
                                Gold = 25,
                                Online = true
                            }.ToByteArray()),
                        YuoCoreProtocolOpCodes.DisconnectRequest => Reply(
                            request,
                            YuoCoreProtocolOpCodes.PlayerSnapshot,
                            new PlayerSnapshot
                            {
                                PlayerId = DisconnectRequest.Parser.ParseFrom(request.Payload).PlayerId,
                                Online = false
                            }.ToByteArray()),
                        _ => throw new InvalidOperationException($"unexpected opcode {request.Opcode}")
                    };
                });
                transports.Add(transport);
                return ValueTask.FromResult<IYuoClientTransport>(transport);
            },
            (transport, _) =>
            {
                closed.Add(transport);
                return ValueTask.CompletedTask;
            });

        var welcome = await connection.ConnectAsync();
        var snapshot = await connection.Client.GetSnapshotAsync(welcome.PlayerId);
        var reconnected = await connection.ReconnectAsync();
        var disconnected = await connection.DisconnectAsync();

        Assert.Equal("player-1", welcome.PlayerId);
        Assert.Equal("player-1", connection.PlayerId);
        Assert.Equal(25, snapshot.Gold);
        Assert.Equal("player-1", reconnected.PlayerId);
        Assert.False(disconnected.Online);
        Assert.Equal(2, transports.Count);
        Assert.Equal([transports[0], transports[1]], closed);
        Assert.Throws<InvalidOperationException>(() => connection.Client);
    }

    [Fact]
    public void Server_message_decode_identifies_request_response_and_push_frames()
    {
        var responseBytes = new YuoServerEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.Welcome,
            RequestId = 7,
            ServerSeq = 8,
            Payload = ByteString.CopyFrom(new Welcome { PlayerId = "player-1" }.ToByteArray())
        }.ToByteArray();
        var pushBytes = new YuoServerEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.StateDelta,
            RequestId = 0,
            ServerSeq = 9,
            Payload = ByteString.CopyFrom(new StateDeltaMessage { Tick = 9 }.ToByteArray())
        }.ToByteArray();

        var response = YuoClientServerMessage.Decode(responseBytes);
        var push = YuoClientServerMessage.Decode(pushBytes);

        Assert.False(response.IsPush);
        Assert.Equal(7UL, response.RequestId);
        Assert.Equal(YuoCoreProtocolOpCodes.Welcome, response.OpCode);
        Assert.True(push.IsPush);
        Assert.Equal(0UL, push.RequestId);
        Assert.Equal(YuoCoreProtocolOpCodes.StateDelta, push.OpCode);
        Assert.Equal(9UL, push.ServerSeq);
    }

    [Fact]
    public void Server_message_reads_client_request_id_from_v1_request()
    {
        var request = new YuoClientEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.HelloRequest,
            RequestId = 123,
            ClientSeq = 456,
            Payload = ByteString.CopyFrom(new HelloRequest().ToByteArray())
        }.ToByteArray();

        Assert.Equal(123UL, YuoClientServerMessage.ReadClientRequestId(request));
    }

    private static YuoServerEnvelopeV1 Reply(
        YuoClientEnvelopeV1 request,
        int opCode,
        byte[] payload)
    {
        return new YuoServerEnvelopeV1
        {
            Version = request.Version,
            Opcode = checked((uint)opCode),
            RequestId = request.RequestId,
            ServerSeq = request.ClientSeq,
            Payload = ByteString.CopyFrom(payload)
        };
    }

    private sealed class LoopbackClientTransport(
        Func<YuoClientEnvelopeV1, YuoServerEnvelopeV1> handler) : IYuoClientTransport
    {
        public ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inbound = YuoClientEnvelopeV1.Parser.ParseFrom(request.Span);
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(handler(inbound).ToByteArray());
        }
    }

    private sealed class CountingClientTransport : IYuoClientTransport
    {
        public int SentCount { get; private set; }

        public ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            SentCount++;
            return ValueTask.FromResult(ReadOnlyMemory<byte>.Empty);
        }
    }

    private sealed class HangingClientTransport : IYuoClientTransport
    {
        public bool ObservedCancellation { get; private set; }

        public async ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            return ReadOnlyMemory<byte>.Empty;
        }
    }
}
