using Google.Protobuf;
using System.Text;
using YuoCore.Abstractions;
using YuoCore.Protocol;

namespace YuoCore.Protocol.Tests;

public sealed class YuoMessageRegistryTests
{
    [Fact]
    public void Registry_indexes_messages_by_opcode_and_type()
    {
        var registry = YuoCoreProtocolCatalog.CreateRegistry();

        var hello = registry.GetByOpCode(YuoCoreProtocolOpCodes.HelloRequest);
        var snapshot = registry.GetByMessageType(typeof(PlayerSnapshot));
        var stateDelta = registry.GetByMessageType(typeof(StateDeltaMessage));
        var stateSnapshotRequest = registry.GetByMessageType(typeof(StateSnapshotRequest));

        Assert.Equal(typeof(HelloRequest), hello.MessageType);
        Assert.Equal(typeof(Welcome), hello.ResponseType);
        Assert.Equal(YuoMessageDirection.ClientToGateway, hello.Direction);
        Assert.Equal(YuoCoreProtocolOpCodes.PlayerSnapshot, snapshot.OpCode);
        Assert.Equal(YuoMessageDirection.GatewayToClient, snapshot.Direction);
        Assert.Equal(YuoCoreProtocolOpCodes.StateDelta, stateDelta.OpCode);
        Assert.Equal(YuoMessageDirection.GatewayToClient, stateDelta.Direction);
        Assert.Equal(YuoCoreProtocolOpCodes.StateSnapshotRequest, stateSnapshotRequest.OpCode);
        Assert.Equal(typeof(StateSnapshotMessage), stateSnapshotRequest.ResponseType);
        Assert.Equal(YuoMessageDirection.ClientToGateway, stateSnapshotRequest.Direction);
    }

    [Fact]
    public void Registry_rejects_duplicate_opcode()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            YuoMessageRegistry.Create(
            [
                YuoMessageDescriptor.Create<HelloRequest>(1, YuoMessageDirection.ClientToGateway),
                YuoMessageDescriptor.Create<ClickRequest>(1, YuoMessageDirection.ClientToGateway)
            ]));

        Assert.Contains("Duplicate protocol opcode", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_rejects_duplicate_message_type()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            YuoMessageRegistry.Create(
            [
                YuoMessageDescriptor.Create<HelloRequest>(1, YuoMessageDirection.ClientToGateway),
                YuoMessageDescriptor.Create<HelloRequest>(2, YuoMessageDirection.ClientToGateway)
            ]));

        Assert.Contains("Duplicate protocol message type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_rejects_null_descriptor()
    {
        Assert.Throws<ArgumentNullException>(() =>
            YuoMessageRegistry.Create([null!]));
    }

    [Fact]
    public void Registry_rejects_null_message_type()
    {
        Assert.Throws<ArgumentNullException>(() =>
            YuoMessageRegistry.Create(
            [
                new YuoMessageDescriptor(1, null!, YuoMessageDirection.ClientToGateway, "Broken")
            ]));
    }

    [Fact]
    public void Registry_rejects_unknown_direction()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            YuoMessageRegistry.Create(
            [
                new YuoMessageDescriptor(1, typeof(HelloRequest), (YuoMessageDirection)999, "Broken")
            ]));

        Assert.Contains("direction is not supported", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ClientEnvelopeCases))]
    public void Protocol_catalog_maps_legacy_client_envelope_cases(ClientEnvelope envelope, int expectedOpCode)
    {
        var found = YuoCoreProtocolCatalog.TryGetClientEnvelopeOpCode(envelope, out var opCode);

        Assert.True(found);
        Assert.Equal(expectedOpCode, opCode);
    }

    [Fact]
    public void Protocol_catalog_returns_false_for_missing_legacy_payload()
    {
        var found = YuoCoreProtocolCatalog.TryGetClientEnvelopeOpCode(new ClientEnvelope(), out var opCode);

        Assert.False(found);
        Assert.Equal(0, opCode);
    }

    [Fact]
    public void Protocol_catalog_covers_all_generated_payload_messages()
    {
        var registry = YuoCoreProtocolCatalog.CreateRegistry();
        var registeredTypes = registry.Descriptors
            .Select(descriptor => descriptor.MessageType)
            .ToHashSet();
        var envelopeTypes = new HashSet<Type>
        {
            typeof(ClientEnvelope),
            typeof(ServerEnvelope),
            typeof(YuoClientEnvelopeV1),
            typeof(YuoServerEnvelopeV1),
            typeof(NetworkEntityState),
            typeof(StateChangeMessage),
            typeof(PlayerWorldState),
            typeof(WorldEvent)
        };

        var unregisteredPayloadTypes = typeof(ClientEnvelope).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, Namespace: "YuoCore.Protocol" })
            .Where(type => typeof(IMessage).IsAssignableFrom(type))
            .Where(type => !envelopeTypes.Contains(type))
            .Where(type => !registeredTypes.Contains(type))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // 当前 catalog 是手写过渡层，直到 Source Generator 接管前，测试必须防止新增 proto 消息漏配 opcode。
        Assert.Empty(unregisteredPayloadTypes);
    }

    [Fact]
    public void Client_envelope_v1_round_trips_opcode_sequence_and_payload()
    {
        var envelope = new YuoClientEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.ClickRequest,
            RequestId = 42,
            ClientSeq = 7,
            Payload = Google.Protobuf.ByteString.CopyFrom([1, 2, 3])
        };

        var decoded = YuoClientEnvelopeV1.Parser.ParseFrom(envelope.ToByteArray());

        Assert.Equal(1U, decoded.Version);
        Assert.Equal((uint)YuoCoreProtocolOpCodes.ClickRequest, decoded.Opcode);
        Assert.Equal(42UL, decoded.RequestId);
        Assert.Equal(7UL, decoded.ClientSeq);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Payload.ToByteArray());
    }

    [Fact]
    public void Server_envelope_v1_round_trips_error_and_payload()
    {
        var envelope = new YuoServerEnvelopeV1
        {
            Version = 1,
            Opcode = YuoCoreProtocolOpCodes.ErrorResponse,
            RequestId = 42,
            ServerSeq = 8,
            Error = new ErrorResponse { Code = "invalid", Message = "bad request" },
            Payload = Google.Protobuf.ByteString.CopyFrom([4, 5, 6])
        };

        var decoded = YuoServerEnvelopeV1.Parser.ParseFrom(envelope.ToByteArray());

        Assert.Equal(1U, decoded.Version);
        Assert.Equal((uint)YuoCoreProtocolOpCodes.ErrorResponse, decoded.Opcode);
        Assert.Equal(42UL, decoded.RequestId);
        Assert.Equal(8UL, decoded.ServerSeq);
        Assert.Equal("invalid", decoded.Error.Code);
        Assert.Equal("bad request", decoded.Error.Message);
        Assert.Equal(new byte[] { 4, 5, 6 }, decoded.Payload.ToByteArray());
    }

    [Fact]
    public void Protocol_response_factory_clears_error_from_request_frame()
    {
        var request = YuoProtocolEnvelope.Request(9, targetId: 100, ReadOnlyMemory<byte>.Empty, senderId: 200)
            with { Error = YuoError.Failed("stale decoded frame.") };

        var response = YuoProtocolEnvelope.Response(request, new byte[] { 7 });

        Assert.Equal(YuoMessageKind.Response, response.Kind);
        Assert.Null(response.Error);
        Assert.Equal(200, response.TargetId);
        Assert.Equal(100, response.SenderId);
        Assert.Equal(new byte[] { 7 }, response.Payload.ToArray());
    }

    [Fact]
    public void Protocol_failure_factory_requires_error()
    {
        var request = YuoProtocolEnvelope.Request(9, targetId: 100, ReadOnlyMemory<byte>.Empty, senderId: 200);

        Assert.Throws<ArgumentNullException>(() => YuoProtocolEnvelope.Failure(request, null!));
    }

    [Fact]
    public void Protocol_envelope_codec_round_trips_frame_fields()
    {
        var request = YuoProtocolEnvelope.Request(9, targetId: 100, new byte[] { 1, 2, 3 }, senderId: 200);

        var decoded = YuoProtocolEnvelopeCodec.Decode(YuoProtocolEnvelopeCodec.Encode(request));

        Assert.Equal(request.Version, decoded.Version);
        Assert.Equal(request.OpCode, decoded.OpCode);
        Assert.Equal(request.Kind, decoded.Kind);
        Assert.Equal(request.TargetId, decoded.TargetId);
        Assert.Equal(request.SenderId, decoded.SenderId);
        Assert.Equal(request.RequestId, decoded.RequestId);
        Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Payload.ToArray());
    }

    [Fact]
    public void Protocol_envelope_codec_rejects_unknown_message_kind()
    {
        var json = """
            {
              "version": 1,
              "opCode": 9,
              "kind": 999,
              "targetId": 100,
              "senderId": 200,
              "requestId": "00000000-0000-0000-0000-000000000000",
              "payload": ""
            }
            """;

        var error = Assert.Throws<InvalidOperationException>(() =>
            YuoProtocolEnvelopeCodec.Decode(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("Unknown YuoProtocolEnvelope message kind", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Protocol_envelope_codec_rejects_missing_version()
    {
        var json = """
            {
              "opCode": 9,
              "kind": 0,
              "targetId": 100,
              "senderId": 200,
              "requestId": "00000000-0000-0000-0000-000000000000",
              "payload": ""
            }
            """;

        var error = Assert.Throws<InvalidOperationException>(() =>
            YuoProtocolEnvelopeCodec.Decode(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("version is required", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<ClientEnvelope, int> ClientEnvelopeCases()
    {
        return new TheoryData<ClientEnvelope, int>
        {
            { new ClientEnvelope { Hello = new HelloRequest() }, YuoCoreProtocolOpCodes.HelloRequest },
            { new ClientEnvelope { Click = new ClickRequest() }, YuoCoreProtocolOpCodes.ClickRequest },
            { new ClientEnvelope { Snapshot = new SnapshotRequest() }, YuoCoreProtocolOpCodes.SnapshotRequest },
            { new ClientEnvelope { Ping = new PingRequest() }, YuoCoreProtocolOpCodes.PingRequest },
            { new ClientEnvelope { EnterMap = new EnterMapRequest() }, YuoCoreProtocolOpCodes.EnterMapRequest },
            { new ClientEnvelope { Move = new MoveRequest() }, YuoCoreProtocolOpCodes.MoveRequest },
            { new ClientEnvelope { Disconnect = new DisconnectRequest() }, YuoCoreProtocolOpCodes.DisconnectRequest }
        };
    }
}
