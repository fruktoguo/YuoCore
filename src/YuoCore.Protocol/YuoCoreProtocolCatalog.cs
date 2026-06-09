namespace YuoCore.Protocol;

public static class YuoCoreProtocolOpCodes
{
    public const int HelloRequest = 1;
    public const int ClickRequest = 2;
    public const int SnapshotRequest = 3;
    public const int PingRequest = 4;
    public const int EnterMapRequest = 5;
    public const int MoveRequest = 6;
    public const int DisconnectRequest = 7;
    public const int StateSnapshotRequest = 8;
    public const int StateSyncRecoverRequest = 9;
    public const int StateSyncAckRequest = 10;

    public const int Welcome = 101;
    public const int PlayerSnapshot = 102;
    public const int ErrorResponse = 103;
    public const int PongResponse = 104;
    public const int WorldSnapshot = 105;
    public const int StateSnapshot = 106;
    public const int StateDelta = 107;
    public const int StateSyncRecover = 108;
    public const int StateSyncAck = 109;
    public const int StateSubscription = 110;
}

/// <summary>
/// 当前 yuocore.proto 的消息目录。
/// 后续引入 Source Generator 后，这个文件应由 proto/codegen 自动生成；当前先手写集中表，避免 Gateway/Backend 散落 opcode 魔法数字。
/// </summary>
public static class YuoCoreProtocolCatalog
{
    public static YuoMessageRegistry CreateRegistry()
    {
        return YuoMessageRegistry.Create(
        [
            YuoMessageDescriptor.Create<HelloRequest>(
                YuoCoreProtocolOpCodes.HelloRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(Welcome)),
            YuoMessageDescriptor.Create<ClickRequest>(
                YuoCoreProtocolOpCodes.ClickRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(PlayerSnapshot)),
            YuoMessageDescriptor.Create<SnapshotRequest>(
                YuoCoreProtocolOpCodes.SnapshotRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(PlayerSnapshot)),
            YuoMessageDescriptor.Create<PingRequest>(
                YuoCoreProtocolOpCodes.PingRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(PongResponse)),
            YuoMessageDescriptor.Create<EnterMapRequest>(
                YuoCoreProtocolOpCodes.EnterMapRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(WorldSnapshot)),
            YuoMessageDescriptor.Create<MoveRequest>(
                YuoCoreProtocolOpCodes.MoveRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(WorldSnapshot)),
            YuoMessageDescriptor.Create<DisconnectRequest>(
                YuoCoreProtocolOpCodes.DisconnectRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(PlayerSnapshot)),
            YuoMessageDescriptor.Create<StateSnapshotRequest>(
                YuoCoreProtocolOpCodes.StateSnapshotRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(StateSnapshotMessage)),
            YuoMessageDescriptor.Create<StateSyncRecoverRequest>(
                YuoCoreProtocolOpCodes.StateSyncRecoverRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(StateSyncRecoverResponse)),
            YuoMessageDescriptor.Create<StateSyncAckRequest>(
                YuoCoreProtocolOpCodes.StateSyncAckRequest,
                YuoMessageDirection.ClientToGateway,
                responseType: typeof(StateSyncAckResponse)),
            YuoMessageDescriptor.Create<Welcome>(
                YuoCoreProtocolOpCodes.Welcome,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<PlayerSnapshot>(
                YuoCoreProtocolOpCodes.PlayerSnapshot,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<ErrorResponse>(
                YuoCoreProtocolOpCodes.ErrorResponse,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<PongResponse>(
                YuoCoreProtocolOpCodes.PongResponse,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<WorldSnapshot>(
                YuoCoreProtocolOpCodes.WorldSnapshot,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<StateSnapshotMessage>(
                YuoCoreProtocolOpCodes.StateSnapshot,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<StateDeltaMessage>(
                YuoCoreProtocolOpCodes.StateDelta,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<StateSyncRecoverResponse>(
                YuoCoreProtocolOpCodes.StateSyncRecover,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<StateSyncAckResponse>(
                YuoCoreProtocolOpCodes.StateSyncAck,
                YuoMessageDirection.GatewayToClient),
            YuoMessageDescriptor.Create<StateSubscriptionMessage>(
                YuoCoreProtocolOpCodes.StateSubscription,
                YuoMessageDirection.GatewayToClient)
        ]);
    }

    public static bool TryGetClientEnvelopeOpCode(ClientEnvelope envelope, out int opCode)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));

        opCode = envelope.PayloadCase switch
        {
            ClientEnvelope.PayloadOneofCase.Hello => YuoCoreProtocolOpCodes.HelloRequest,
            ClientEnvelope.PayloadOneofCase.Click => YuoCoreProtocolOpCodes.ClickRequest,
            ClientEnvelope.PayloadOneofCase.Snapshot => YuoCoreProtocolOpCodes.SnapshotRequest,
            ClientEnvelope.PayloadOneofCase.Ping => YuoCoreProtocolOpCodes.PingRequest,
            ClientEnvelope.PayloadOneofCase.EnterMap => YuoCoreProtocolOpCodes.EnterMapRequest,
            ClientEnvelope.PayloadOneofCase.Move => YuoCoreProtocolOpCodes.MoveRequest,
            ClientEnvelope.PayloadOneofCase.Disconnect => YuoCoreProtocolOpCodes.DisconnectRequest,
            _ => 0
        };

        return opCode != 0;
    }
}
