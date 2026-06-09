using Google.Protobuf;
using YuoCore.Client;
using YuoCore.Client.StateSync;
using YuoCore.Protocol;
using YuoCore.StateSync;
using YuoCore.StateSync.Protocol;

namespace YuoCore.Client.StateSync.Tests;

public sealed class YuoClientStateSyncReceiverTests
{
    [Fact]
    public void Snapshot_push_resets_client_mirror()
    {
        var receiver = new YuoClientStateSyncReceiver();
        var snapshot = new YuoStateSnapshot(
            10,
            [new YuoEntityState(1001, "player", 1, new byte[] { 1, 2 })]);

        var result = receiver.ApplyServerEnvelope(CreateEnvelope(
            YuoCoreProtocolOpCodes.StateSnapshot,
            YuoStateSyncProtocolMapper.ToProtocol(snapshot)));

        Assert.Equal(YuoClientStateSyncMessageKind.Snapshot, result.Kind);
        Assert.True(result.ApplyResult?.IsApplied);
        Assert.Equal(10, result.Tick);
        Assert.True(receiver.Mirror.Table.TryGet(1001, out var state));
        Assert.Equal([1, 2], state.Payload.ToArray());
    }

    [Fact]
    public void Delta_push_updates_existing_mirror_when_base_tick_matches()
    {
        var receiver = new YuoClientStateSyncReceiver();
        receiver.ApplyServerEnvelope(CreateEnvelope(
            YuoCoreProtocolOpCodes.StateSnapshot,
            YuoStateSyncProtocolMapper.ToProtocol(new YuoStateSnapshot(
                10,
                [new YuoEntityState(1001, "player", 1, new byte[] { 1 })]))));

        var result = receiver.ApplyServerEnvelope(CreateEnvelope(
            YuoCoreProtocolOpCodes.StateDelta,
            YuoStateSyncProtocolMapper.ToProtocol(new YuoStateDelta(
                11,
                [new YuoStateChange(
                    YuoStateChangeKind.Update,
                    new YuoEntityState(1001, "player", 2, new byte[] { 2 }),
                    1001)],
                BaseTick: 10))));

        Assert.Equal(YuoClientStateSyncMessageKind.Delta, result.Kind);
        Assert.True(result.ApplyResult?.IsApplied);
        Assert.Equal(11, receiver.Mirror.Table.LastTick);
        Assert.True(receiver.Mirror.Table.TryGet(1001, out var state));
        Assert.Equal(2u, state.Version);
    }

    [Fact]
    public void Delta_gap_marks_snapshot_required()
    {
        var receiver = new YuoClientStateSyncReceiver();
        receiver.ApplyServerEnvelope(CreateEnvelope(
            YuoCoreProtocolOpCodes.StateSnapshot,
            YuoStateSyncProtocolMapper.ToProtocol(new YuoStateSnapshot(
                10,
                [new YuoEntityState(1001, "player", 1, ReadOnlyMemory<byte>.Empty)]))));

        var result = receiver.ApplyServerEnvelope(CreateEnvelope(
            YuoCoreProtocolOpCodes.StateDelta,
            YuoStateSyncProtocolMapper.ToProtocol(new YuoStateDelta(
                12,
                [new YuoStateChange(
                    YuoStateChangeKind.Update,
                    new YuoEntityState(1001, "player", 2, ReadOnlyMemory<byte>.Empty),
                    1001)],
                BaseTick: 11))));

        Assert.Equal(YuoClientStateSyncMessageKind.Delta, result.Kind);
        Assert.True(result.ApplyResult?.RequiresSnapshot);
        Assert.Equal(YuoStateSyncApplyReason.GapDetected, result.ApplyResult?.Reason);
        Assert.True(receiver.Mirror.IsSnapshotRequired);
    }

    [Fact]
    public void Non_state_sync_push_is_ignored_for_other_dispatchers()
    {
        var receiver = new YuoClientStateSyncReceiver();
        var result = receiver.ApplyServerEnvelope(new YuoServerEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.PongResponse,
            RequestId = 77,
            ServerSeq = 9,
            Payload = ByteString.CopyFrom(new PongResponse { UnixMs = 123 }.ToByteArray())
        });

        Assert.False(result.IsStateSyncMessage);
        Assert.Equal(YuoClientStateSyncMessageKind.Ignored, result.Kind);
        Assert.Null(result.ApplyResult);
        Assert.Null(receiver.Mirror.Table.LastTick);
    }

    [Fact]
    public void Receiver_exposes_stream_id_for_ack_and_recover_helpers()
    {
        var receiver = new YuoClientStateSyncReceiver();
        var snapshot = new StateSnapshotMessage
        {
            Tick = 10,
            StreamId = "map:1001:aoi:cell_1"
        };
        snapshot.Entities.Add(new NetworkEntityState
        {
            NetworkId = 1001,
            Kind = "player",
            Version = 1,
            Payload = ByteString.CopyFrom([1])
        });

        var result = receiver.ApplyStateSnapshot(snapshot);

        Assert.Equal("map:1001:aoi:cell_1", result.StreamId);
        Assert.True(YuoClientStateSyncAcks.ShouldAck(result));
    }

    [Fact]
    public void Multi_stream_receiver_keeps_independent_mirrors_per_stream()
    {
        var receiver = new YuoClientStateSyncMultiStreamReceiver();
        var firstStream = YuoStateSyncStreamIds.ForMapAoi(1001, "cell_1");
        var secondStream = YuoStateSyncStreamIds.ForMapAoi(1001, "cell_2");

        var firstSnapshot = receiver.ApplyStateSnapshot(MakeSnapshotMessage(firstStream, 10, 1001, version: 1));
        var secondSnapshot = receiver.ApplyStateSnapshot(MakeSnapshotMessage(secondStream, 3, 2001, version: 1));
        var secondDelta = receiver.ApplyStateDelta(MakeDeltaMessage(secondStream, 4, baseTick: 3, 2001, version: 2));

        Assert.True(firstSnapshot.ApplyResult?.IsApplied);
        Assert.True(secondSnapshot.ApplyResult?.IsApplied);
        Assert.True(secondDelta.ApplyResult?.IsApplied);
        Assert.Equal(10, receiver.Streams.GetCursor(firstStream).LastTick);
        Assert.Equal(4, receiver.Streams.GetCursor(secondStream).LastTick);

        var firstGap = receiver.ApplyStateDelta(MakeDeltaMessage(firstStream, 12, baseTick: 11, 1001, version: 2));

        Assert.True(firstGap.ApplyResult?.RequiresSnapshot);
        Assert.True(receiver.Streams.GetCursor(firstStream).IsSnapshotRequired);
        Assert.False(receiver.Streams.GetCursor(secondStream).IsSnapshotRequired);
    }

    [Fact]
    public void Multi_stream_receiver_subscription_message_removes_missing_streams()
    {
        var receiver = new YuoClientStateSyncMultiStreamReceiver();
        var firstStream = YuoStateSyncStreamIds.ForMapAoi(1001, "cell_1");
        var secondStream = YuoStateSyncStreamIds.ForMapAoi(1001, "cell_2");
        receiver.ApplyStateSnapshot(MakeSnapshotMessage(firstStream, 10, 1001, version: 1));
        receiver.ApplyStateSnapshot(MakeSnapshotMessage(secondStream, 3, 2001, version: 1));

        var result = receiver.ApplyStateSubscription(new StateSubscriptionMessage
        {
            StreamIds = { secondStream },
            Reason = "aoi_moved",
            ResetMissing = true
        });

        Assert.Equal(YuoClientStateSyncMessageKind.Subscription, result.Kind);
        Assert.False(receiver.Streams.TryGet(firstStream, out _));
        Assert.True(receiver.Streams.TryGet(secondStream, out _));
    }

    [Fact]
    public void Multi_stream_receiver_requires_stream_id_without_default()
    {
        var receiver = new YuoClientStateSyncMultiStreamReceiver();

        Assert.Throws<InvalidOperationException>(() =>
            receiver.ApplyStateSnapshot(MakeSnapshotMessage(streamId: "", 1, 1001, version: 1)));
    }

    [Fact]
    public void Error_envelope_throws_client_protocol_exception()
    {
        var receiver = new YuoClientStateSyncReceiver();
        var envelope = new YuoServerEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.StateSnapshot,
            Error = new ErrorResponse
            {
                Code = "snapshot_failed",
                Message = "snapshot failed"
            }
        };

        var exception = Assert.Throws<YuoClientProtocolException>(
            () => receiver.ApplyServerEnvelope(envelope.ToByteArray()));

        Assert.Equal("snapshot_failed", exception.Code);
    }

    [Fact]
    public void Subscription_applies_push_transport_messages_and_can_unsubscribe()
    {
        var transport = new FakePushTransport();
        var received = new List<YuoClientStateSyncReceiveResult>();
        using var subscription = YuoClientStateSyncSubscription.Attach(
            transport,
            onReceive: received.Add);

        transport.Emit(CreateEnvelope(
            YuoCoreProtocolOpCodes.StateSnapshot,
            YuoStateSyncProtocolMapper.ToProtocol(new YuoStateSnapshot(
                10,
                [new YuoEntityState(1001, "player", 1, new byte[] { 1 })]))));

        Assert.Single(received);
        Assert.Equal(YuoClientStateSyncMessageKind.Snapshot, received[0].Kind);
        Assert.True(subscription.Receiver.Mirror.Table.TryGet(1001, out var state));
        Assert.Equal(1u, state.Version);

        subscription.Dispose();
        transport.Emit(CreateEnvelope(
            YuoCoreProtocolOpCodes.StateDelta,
            YuoStateSyncProtocolMapper.ToProtocol(new YuoStateDelta(
                11,
                [new YuoStateChange(
                    YuoStateChangeKind.Update,
                    new YuoEntityState(1001, "player", 2, new byte[] { 2 }),
                    1001)]))));

        Assert.Single(received);
        Assert.True(subscription.Receiver.Mirror.Table.TryGet(1001, out state));
        Assert.Equal(1u, state.Version);
    }

    [Fact]
    public async Task Recovery_requests_snapshot_and_applies_it_after_delta_gap()
    {
        var receiver = new YuoClientStateSyncReceiver();
        receiver.ApplyStateSnapshot(YuoStateSyncProtocolMapper.ToProtocol(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1001, "player", 1, new byte[] { 1 })])));
        var gap = receiver.ApplyStateDelta(YuoStateSyncProtocolMapper.ToProtocol(new YuoStateDelta(
            12,
            [new YuoStateChange(
                YuoStateChangeKind.Update,
                new YuoEntityState(1001, "player", 2, new byte[] { 2 }),
                1001)],
            BaseTick: 11)));
        var transport = new LoopbackClientTransport(request =>
        {
            Assert.Equal((uint)YuoCoreProtocolOpCodes.StateSnapshotRequest, request.Opcode);
            var snapshotRequest = StateSnapshotRequest.Parser.ParseFrom(request.Payload);
            Assert.Equal("player-1", snapshotRequest.PlayerId);
            Assert.Equal(10L, snapshotRequest.KnownTick);
            return new YuoServerEnvelopeV1
            {
                Version = request.Version,
                Opcode = YuoCoreProtocolOpCodes.StateSnapshot,
                RequestId = request.RequestId,
                ServerSeq = 99,
                Payload = ByteString.CopyFrom(YuoStateSyncProtocolMapper.ToProtocol(new YuoStateSnapshot(
                    13,
                    [new YuoEntityState(1001, "player", 3, new byte[] { 3 })])).ToByteArray())
            };
        });
        var recovery = new YuoClientStateSyncRecovery(new YuoMmoClient(transport), receiver);

        Assert.True(YuoClientStateSyncRecovery.ShouldRequestSnapshot(gap));
        var recovered = await recovery.RequestSnapshotAsync("player-1", knownTick: 10);

        Assert.Equal(YuoClientStateSyncMessageKind.Snapshot, recovered.Kind);
        Assert.True(recovered.ApplyResult?.IsApplied);
        Assert.False(receiver.Mirror.IsSnapshotRequired);
        Assert.True(receiver.Mirror.Table.TryGet(1001, out var state));
        Assert.Equal(3u, state.Version);
    }

    [Fact]
    public async Task Ack_helper_sends_ack_only_after_successful_apply()
    {
        var receiver = new YuoClientStateSyncReceiver();
        var acked = false;
        var transport = new LoopbackClientTransport(request =>
        {
            Assert.Equal((uint)YuoCoreProtocolOpCodes.StateSyncAckRequest, request.Opcode);
            var ackRequest = StateSyncAckRequest.Parser.ParseFrom(request.Payload);
            Assert.Equal("player-1", ackRequest.PlayerId);
            Assert.Equal(10L, ackRequest.AckTick);
            Assert.Equal("player:stream", ackRequest.StreamId);
            acked = true;

            return new YuoServerEnvelopeV1
            {
                Version = request.Version,
                Opcode = YuoCoreProtocolOpCodes.StateSyncAck,
                RequestId = request.RequestId,
                ServerSeq = 100,
                Payload = ByteString.CopyFrom(new StateSyncAckResponse
                {
                    Accepted = true,
                    AckTick = ackRequest.AckTick,
                    LatestTick = ackRequest.AckTick,
                    Reason = "accepted",
                    StreamId = ackRequest.StreamId
                }.ToByteArray())
            };
        });
        var snapshot = new StateSnapshotMessage
        {
            Tick = 10,
            StreamId = "player:stream"
        };
        snapshot.Entities.Add(new NetworkEntityState
        {
            NetworkId = 1001,
            Kind = "player",
            Version = 1
        });
        var applied = receiver.ApplyStateSnapshot(snapshot);

        var response = await YuoClientStateSyncAcks.AckIfAppliedAsync(
            new YuoMmoClient(transport),
            "player-1",
            applied);

        Assert.True(acked);
        Assert.NotNull(response);
        Assert.True(response.Accepted);
        Assert.Equal("player:stream", response.StreamId);

        var gap = receiver.ApplyStateDelta(YuoStateSyncProtocolMapper.ToProtocol(new YuoStateDelta(
            12,
            [new YuoStateChange(
                YuoStateChangeKind.Update,
                new YuoEntityState(1001, "player", 2, ReadOnlyMemory<byte>.Empty),
                1001)],
            BaseTick: 11)));
        Assert.True(gap.ApplyResult?.RequiresSnapshot);

        var skipped = await YuoClientStateSyncAcks.AckIfAppliedAsync(
            new YuoMmoClient(new ThrowingClientTransport()),
            "player-1",
            gap);

        Assert.Null(skipped);
    }

    [Fact]
    public async Task Recovery_can_apply_replayed_deltas_before_falling_back_to_snapshot()
    {
        var receiver = new YuoClientStateSyncReceiver();
        var snapshot = YuoStateSyncProtocolMapper.ToProtocol(new YuoStateSnapshot(
            10,
            [new YuoEntityState(1001, "player", 1, new byte[] { 1 })]));
        snapshot.StreamId = "player:stream";
        receiver.ApplyStateSnapshot(snapshot);
        var transport = new LoopbackClientTransport(request =>
        {
            Assert.Equal((uint)YuoCoreProtocolOpCodes.StateSyncRecoverRequest, request.Opcode);
            var recoverRequest = StateSyncRecoverRequest.Parser.ParseFrom(request.Payload);
            Assert.Equal("player-1", recoverRequest.PlayerId);
            Assert.Equal(10L, recoverRequest.KnownTick);
            Assert.Equal("player:stream", recoverRequest.StreamId);
            return new YuoServerEnvelopeV1
            {
                Version = request.Version,
                Opcode = YuoCoreProtocolOpCodes.StateSyncRecover,
                RequestId = request.RequestId,
                ServerSeq = 99,
                Payload = ByteString.CopyFrom(new StateSyncRecoverResponse
                {
                    Replayed = true,
                    Reason = "Replayed",
                    StreamId = "player:stream",
                    Deltas =
                    {
                        YuoStateSyncProtocolMapper.ToProtocol(new YuoStateDelta(
                            11,
                            [new YuoStateChange(
                                YuoStateChangeKind.Update,
                                new YuoEntityState(1001, "player", 2, new byte[] { 2 }),
                                1001)],
                            BaseTick: 10))
                    }
                }.ToByteArray())
            };
        });
        var recovery = new YuoClientStateSyncRecovery(new YuoMmoClient(transport), receiver);

        var results = await recovery.RecoverAsync("player-1", knownTick: 10);

        var result = Assert.Single(results);
        Assert.Equal(YuoClientStateSyncMessageKind.Delta, result.Kind);
        Assert.True(result.ApplyResult?.IsApplied);
        Assert.Equal("player:stream", result.StreamId);
        Assert.Equal("player:stream", receiver.LastStreamId);
        Assert.True(receiver.Mirror.Table.TryGet(1001, out var state));
        Assert.Equal(2u, state.Version);
    }

    [Fact]
    public async Task Reconnect_recovery_reuses_connection_player_stream_and_known_tick()
    {
        var receiver = new YuoClientStateSyncReceiver();
        receiver.ApplyStateSnapshot(MakeSnapshotMessage("player:stream", 10, 1001, version: 1));
        var transports = new List<IYuoClientTransport>();
        var closed = new List<IYuoClientTransport>();
        var recoverRequested = false;
        var connection = new YuoMmoClientConnection(
            _ =>
            {
                var transport = new LoopbackClientTransport(request =>
                {
                    return request.Opcode switch
                    {
                        YuoCoreProtocolOpCodes.HelloRequest => new YuoServerEnvelopeV1
                        {
                            Version = request.Version,
                            Opcode = YuoCoreProtocolOpCodes.Welcome,
                            RequestId = request.RequestId,
                            ServerSeq = request.ClientSeq,
                            Payload = ByteString.CopyFrom(new Welcome
                            {
                                PlayerId = string.IsNullOrWhiteSpace(HelloRequest.Parser.ParseFrom(request.Payload).PlayerId)
                                    ? "player-1"
                                    : HelloRequest.Parser.ParseFrom(request.Payload).PlayerId,
                                Message = "ok"
                            }.ToByteArray())
                        },
                        YuoCoreProtocolOpCodes.StateSyncRecoverRequest => ReplyRecover(request),
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

        await connection.ConnectAsync("player-1");
        var recovery = new YuoClientStateSyncReconnectRecovery(connection, receiver);

        var result = await recovery.ReconnectAndRecoverAsync();

        Assert.True(recoverRequested);
        Assert.Equal("player-1", result.Welcome.PlayerId);
        Assert.True(result.Recovery.Replayed);
        Assert.Equal("player:stream", result.Recovery.StreamId);
        Assert.Equal(2, transports.Count);
        Assert.Equal([transports[0]], closed);
        Assert.True(receiver.Mirror.Table.TryGet(1001, out var state));
        Assert.Equal(2u, state.Version);
        Assert.Equal(11, receiver.Mirror.Table.LastTick);

        YuoServerEnvelopeV1 ReplyRecover(YuoClientEnvelopeV1 request)
        {
            var recoverRequest = StateSyncRecoverRequest.Parser.ParseFrom(request.Payload);
            Assert.Equal("player-1", recoverRequest.PlayerId);
            Assert.Equal(10L, recoverRequest.KnownTick);
            Assert.Equal("player:stream", recoverRequest.StreamId);
            recoverRequested = true;

            return new YuoServerEnvelopeV1
            {
                Version = request.Version,
                Opcode = YuoCoreProtocolOpCodes.StateSyncRecover,
                RequestId = request.RequestId,
                ServerSeq = request.ClientSeq,
                Payload = ByteString.CopyFrom(new StateSyncRecoverResponse
                {
                    Replayed = true,
                    Reason = "Replayed",
                    StreamId = "player:stream",
                    Deltas =
                    {
                        MakeDeltaMessage("player:stream", 11, baseTick: 10, 1001, version: 2)
                    }
                }.ToByteArray())
            };
        }
    }

    [Fact]
    public async Task Recovery_detailed_exposes_recover_reason_without_apply_results()
    {
        var receiver = new YuoClientStateSyncReceiver();
        var transport = new LoopbackClientTransport(request =>
        {
            Assert.Equal((uint)YuoCoreProtocolOpCodes.StateSyncRecoverRequest, request.Opcode);
            var recoverRequest = StateSyncRecoverRequest.Parser.ParseFrom(request.Payload);
            Assert.Equal("player-1", recoverRequest.PlayerId);
            Assert.Equal("map:1001:aoi:default.cell_9_0", recoverRequest.StreamId);
            return new YuoServerEnvelopeV1
            {
                Version = request.Version,
                Opcode = YuoCoreProtocolOpCodes.StateSyncRecover,
                RequestId = request.RequestId,
                ServerSeq = 99,
                Payload = ByteString.CopyFrom(new StateSyncRecoverResponse
                {
                    Replayed = false,
                    Reason = "stream_not_observable",
                    StreamId = "map:1001:aoi:default.cell_9_0"
                }.ToByteArray())
            };
        });
        var recovery = new YuoClientStateSyncRecovery(new YuoMmoClient(transport), receiver);

        var result = await recovery.RecoverDetailedAsync(
            "player-1",
            knownTick: 1,
            streamId: "map:1001:aoi:default.cell_9_0");

        Assert.False(result.Replayed);
        Assert.False(result.HasAppliedResults);
        Assert.Empty(result.Results);
        Assert.Equal("stream_not_observable", result.Reason);
        Assert.Equal("map:1001:aoi:default.cell_9_0", result.StreamId);
        Assert.Null(receiver.LastStreamId);
    }

    [Fact]
    public async Task Multi_stream_recovery_detailed_exposes_recover_reason_without_apply_results()
    {
        var receiver = new YuoClientStateSyncMultiStreamReceiver();
        var transport = new LoopbackClientTransport(request =>
        {
            Assert.Equal((uint)YuoCoreProtocolOpCodes.StateSyncRecoverRequest, request.Opcode);
            var recoverRequest = StateSyncRecoverRequest.Parser.ParseFrom(request.Payload);
            Assert.Equal("player-1", recoverRequest.PlayerId);
            Assert.Equal("map:1001:aoi:default.cell_9_0", recoverRequest.StreamId);
            return new YuoServerEnvelopeV1
            {
                Version = request.Version,
                Opcode = YuoCoreProtocolOpCodes.StateSyncRecover,
                RequestId = request.RequestId,
                ServerSeq = 99,
                Payload = ByteString.CopyFrom(new StateSyncRecoverResponse
                {
                    Replayed = false,
                    Reason = "stream_not_observable",
                    StreamId = "map:1001:aoi:default.cell_9_0"
                }.ToByteArray())
            };
        });
        var recovery = new YuoClientStateSyncMultiStreamRecovery(new YuoMmoClient(transport), receiver);

        var result = await recovery.RecoverDetailedAsync(
            "player-1",
            "map:1001:aoi:default.cell_9_0",
            knownTick: 1);

        Assert.False(result.Replayed);
        Assert.False(result.HasAppliedResults);
        Assert.Empty(result.Results);
        Assert.Equal("stream_not_observable", result.Reason);
        Assert.Equal("map:1001:aoi:default.cell_9_0", result.StreamId);
        Assert.Null(receiver.Streams.GetCursor("map:1001:aoi:default.cell_9_0").LastTick);
    }

    private static YuoServerEnvelopeV1 CreateEnvelope<TMessage>(int opcode, TMessage message)
        where TMessage : IMessage<TMessage>
    {
        return new YuoServerEnvelopeV1
        {
            Version = 1,
            Opcode = checked((uint)opcode),
            ServerSeq = 1,
            Payload = ByteString.CopyFrom(message.ToByteArray())
        };
    }

    private static StateSnapshotMessage MakeSnapshotMessage(
        string streamId,
        long tick,
        long networkId,
        uint version)
    {
        var snapshot = new StateSnapshotMessage
        {
            Tick = tick,
            StreamId = streamId
        };
        snapshot.Entities.Add(new NetworkEntityState
        {
            NetworkId = networkId,
            Kind = "player",
            Version = version,
            Payload = ByteString.CopyFrom([checked((byte)version)])
        });
        return snapshot;
    }

    private static StateDeltaMessage MakeDeltaMessage(
        string streamId,
        long tick,
        long baseTick,
        long networkId,
        uint version)
    {
        var delta = new StateDeltaMessage
        {
            Tick = tick,
            BaseTick = baseTick,
            StreamId = streamId
        };
        delta.Changes.Add(new StateChangeMessage
        {
            Kind = StateChangeKind.Update,
            NetworkId = networkId,
            State = new NetworkEntityState
            {
                NetworkId = networkId,
                Kind = "player",
                Version = version,
                Payload = ByteString.CopyFrom([checked((byte)version)])
            }
        });
        return delta;
    }

    private sealed class FakePushTransport : IYuoClientPushTransport
    {
        public event Action<YuoClientServerMessage>? PushReceived;

        public ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Emit(YuoServerEnvelopeV1 envelope)
        {
            PushReceived?.Invoke(YuoClientServerMessage.Decode(envelope.ToByteArray()));
        }
    }

    private sealed class ThrowingClientTransport : IYuoClientTransport
    {
        public ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("ACK should not be sent for unapplied StateSync results.");
        }
    }

    private sealed class LoopbackClientTransport(
        Func<YuoClientEnvelopeV1, YuoServerEnvelopeV1> handler) : IYuoClientTransport
    {
        public ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = YuoClientEnvelopeV1.Parser.ParseFrom(request.Span);
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(handler(envelope).ToByteArray());
        }
    }
}
