using Google.Protobuf;
using YuoCore.Abstractions;
using YuoCore.Gateway;
using YuoCore.Protocol;

namespace YuoCore.Backend.WebSockets;

/// <summary>
/// Backend 示例的 Gateway 协议桥。
/// 外部线协议暂时仍是旧的 ClientEnvelope/ServerEnvelope oneof protobuf；
/// 进入服务端后先适配成 Gateway pipeline 需要的 bytes payload，后续替换为 opcode + bytes envelope 时只需要改这一层。
/// </summary>
public sealed class GameGatewayProtobufDispatcher(
    GameClientMessageDispatcher inner,
    GameStateSyncPushDispatcher? stateSyncPush = null) : IYuoGatewayDispatcher
{
    public async ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
        YuoGatewaySession session,
        YuoGatewayInboundMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (message.OpCode == YuoCoreProtocolOpCodes.StateSnapshotRequest)
        {
            return DispatchStateSnapshotRequest(session, message);
        }

        if (message.OpCode == YuoCoreProtocolOpCodes.StateSyncRecoverRequest)
        {
            return DispatchStateSyncRecoverRequest(session, message);
        }

        if (message.OpCode == YuoCoreProtocolOpCodes.StateSyncAckRequest)
        {
            return DispatchStateSyncAckRequest(session, message);
        }

        var request = DecodeRequest(message);
        NormalizeGatewaySession(request, session);
        var response = await inner.DispatchAsync(request, session.SessionId.ToString("N"), cancellationToken).ConfigureAwait(false);
        BindSessionPlayerIfPossible(session, response);
        var encodeAsEnvelopeV1Payload = message.OpCode != 0;
        var (opCode, payload) = encodeAsEnvelopeV1Payload
            ? EncodeResponsePayload(response)
            : (ResolveLegacyRequestOpCode(request), response.ToByteArray());

        return new YuoGatewayOutboundMessage(
            message.Version,
            opCode,
            response.RequestId,
            0,
            payload);
    }

    private static ClientEnvelope DecodeRequest(YuoGatewayInboundMessage message)
    {
        if (message.OpCode == 0)
        {
            return ClientEnvelope.Parser.ParseFrom(message.Payload.Span);
        }

        return message.OpCode switch
        {
            YuoCoreProtocolOpCodes.HelloRequest => new ClientEnvelope
            {
                RequestId = message.RequestId,
                Hello = HelloRequest.Parser.ParseFrom(message.Payload.Span)
            },
            YuoCoreProtocolOpCodes.ClickRequest => new ClientEnvelope
            {
                RequestId = message.RequestId,
                Click = ClickRequest.Parser.ParseFrom(message.Payload.Span)
            },
            YuoCoreProtocolOpCodes.SnapshotRequest => new ClientEnvelope
            {
                RequestId = message.RequestId,
                Snapshot = SnapshotRequest.Parser.ParseFrom(message.Payload.Span)
            },
            YuoCoreProtocolOpCodes.PingRequest => new ClientEnvelope
            {
                RequestId = message.RequestId,
                Ping = PingRequest.Parser.ParseFrom(message.Payload.Span)
            },
            YuoCoreProtocolOpCodes.EnterMapRequest => new ClientEnvelope
            {
                RequestId = message.RequestId,
                EnterMap = EnterMapRequest.Parser.ParseFrom(message.Payload.Span)
            },
            YuoCoreProtocolOpCodes.MoveRequest => new ClientEnvelope
            {
                RequestId = message.RequestId,
                Move = MoveRequest.Parser.ParseFrom(message.Payload.Span)
            },
            YuoCoreProtocolOpCodes.DisconnectRequest => new ClientEnvelope
            {
                RequestId = message.RequestId,
                Disconnect = DisconnectRequest.Parser.ParseFrom(message.Payload.Span)
            },
            _ => throw new InvalidOperationException($"Unsupported gateway opcode {message.OpCode}.")
        };
    }

    private static void NormalizeGatewaySession(ClientEnvelope request, YuoGatewaySession session)
    {
        if (request.PayloadCase != ClientEnvelope.PayloadOneofCase.Disconnect)
        {
            return;
        }

        // 外部客户端不应该知道 Gateway 内部 sessionId；真实连接场景以当前连接绑定为准。
        // 旧测试或内部调用即使填了 session_id，也会被连接 session 归一化，避免伪造其他连接。
        request.Disconnect.SessionId = session.SessionId.ToString("N");
    }

    private YuoGatewayOutboundMessage DispatchStateSnapshotRequest(
        YuoGatewaySession session,
        YuoGatewayInboundMessage message)
    {
        if (stateSyncPush is null)
        {
            return Error(message, YuoError.Invalid("state sync push dispatcher is not configured."));
        }

        var request = StateSnapshotRequest.Parser.ParseFrom(message.Payload.Span);
        if (!ValidateStateSnapshotPlayer(session, request.PlayerId, out var error))
        {
            return Error(message, error);
        }

        if (!stateSyncPush.TryCreateSnapshotPush(
                session,
                out var snapshot,
                force: true,
                requestId: message.RequestId))
        {
            return Error(message, YuoError.NotFound("player state snapshot is not available."));
        }

        return snapshot;
    }

    private YuoGatewayOutboundMessage DispatchStateSyncRecoverRequest(
        YuoGatewaySession session,
        YuoGatewayInboundMessage message)
    {
        if (stateSyncPush is null)
        {
            return Error(message, YuoError.Invalid("state sync push dispatcher is not configured."));
        }

        var request = StateSyncRecoverRequest.Parser.ParseFrom(message.Payload.Span);
        if (!ValidateStateSnapshotPlayer(session, request.PlayerId, out var error))
        {
            return Error(message, error);
        }

        if (!stateSyncPush.TryCreateRecoverResponse(
                session,
                request.KnownTick,
                request.StreamId,
                message.RequestId,
                out var response))
        {
            return Error(message, YuoError.Invalid("player state sync recovery is not available or stream_id does not match session."));
        }

        return response;
    }

    private YuoGatewayOutboundMessage DispatchStateSyncAckRequest(
        YuoGatewaySession session,
        YuoGatewayInboundMessage message)
    {
        if (stateSyncPush is null)
        {
            return Error(message, YuoError.Invalid("state sync push dispatcher is not configured."));
        }

        var request = StateSyncAckRequest.Parser.ParseFrom(message.Payload.Span);
        if (!ValidateStateSnapshotPlayer(session, request.PlayerId, out var error))
        {
            return Error(message, error);
        }

        if (!stateSyncPush.TryCreateAckResponse(
                session,
                request.AckTick,
                request.StreamId,
                message.RequestId,
                out var response))
        {
            return Error(message, YuoError.NotFound("player state sync ack is not available."));
        }

        return response;
    }

    private static bool ValidateStateSnapshotPlayer(
        YuoGatewaySession session,
        string requestedPlayerId,
        out YuoError error)
    {
        error = default!;
        if (session.PlayerId is not { } boundPlayerId)
        {
            error = YuoError.Invalid("session is not bound to a player.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requestedPlayerId) &&
            (!Guid.TryParse(requestedPlayerId, out var requested) || requested != boundPlayerId))
        {
            error = YuoError.Invalid("state snapshot player_id does not match session.");
            return false;
        }

        return true;
    }

    private static (int OpCode, byte[] Payload) EncodeResponsePayload(ServerEnvelope response)
    {
        return response.PayloadCase switch
        {
            ServerEnvelope.PayloadOneofCase.Welcome => (YuoCoreProtocolOpCodes.Welcome, response.Welcome.ToByteArray()),
            ServerEnvelope.PayloadOneofCase.Snapshot => (YuoCoreProtocolOpCodes.PlayerSnapshot, response.Snapshot.ToByteArray()),
            ServerEnvelope.PayloadOneofCase.Error => (YuoCoreProtocolOpCodes.ErrorResponse, response.Error.ToByteArray()),
            ServerEnvelope.PayloadOneofCase.Pong => (YuoCoreProtocolOpCodes.PongResponse, response.Pong.ToByteArray()),
            ServerEnvelope.PayloadOneofCase.World => (YuoCoreProtocolOpCodes.WorldSnapshot, response.World.ToByteArray()),
            _ => (YuoCoreProtocolOpCodes.ErrorResponse, new ErrorResponse
            {
                Code = "missing_payload",
                Message = "response payload is missing"
            }.ToByteArray())
        };
    }

    private static void BindSessionPlayerIfPossible(YuoGatewaySession session, ServerEnvelope response)
    {
        var playerId = response.PayloadCase switch
        {
            ServerEnvelope.PayloadOneofCase.Welcome => response.Welcome.PlayerId,
            ServerEnvelope.PayloadOneofCase.Snapshot => response.Snapshot.PlayerId,
            _ => string.Empty
        };

        if (Guid.TryParse(playerId, out var id))
        {
            // Gateway session 只保存连接和玩家绑定关系，后续广播推送靠这个 id 去 Runtime 拉取出站事件。
            session.BindPlayer(id);
        }
    }

    private static int ResolveLegacyRequestOpCode(ClientEnvelope request)
    {
        // 当前旧协议没有显式 opcode，只能从 oneof case 推断。
        // 映射集中放在 Protocol catalog，避免 Backend/Gateway 散落魔法数字。
        return YuoCoreProtocolCatalog.TryGetClientEnvelopeOpCode(request, out var opCode) ? opCode : 0;
    }

    private static YuoGatewayOutboundMessage Error(YuoGatewayInboundMessage request, YuoError error)
    {
        return new YuoGatewayOutboundMessage(
            request.Version,
            request.OpCode,
            request.RequestId,
            0,
            ReadOnlyMemory<byte>.Empty,
            error);
    }
}
