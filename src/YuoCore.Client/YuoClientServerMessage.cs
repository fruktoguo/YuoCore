using YuoCore.Protocol;

namespace YuoCore.Client;

/// <summary>
/// 客户端收到的一帧服务端 V1 envelope。
/// 该类型只表达外部协议，不持有 WebSocket、Gateway session、ActorId 或服务端 Entity。
/// RequestId 为 0 时表示服务端主动推送，例如 StateSnapshot / StateDelta。
/// </summary>
public sealed record YuoClientServerMessage(
    int Version,
    int OpCode,
    ulong RequestId,
    ulong ServerSeq,
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> Bytes,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool IsPush => RequestId == 0;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorCode);

    public static YuoClientServerMessage Decode(ReadOnlyMemory<byte> bytes)
    {
        var envelope = YuoServerEnvelopeV1.Parser.ParseFrom(bytes.Span);
        var error = envelope.Error;
        return new YuoClientServerMessage(
            checked((int)envelope.Version),
            checked((int)envelope.Opcode),
            envelope.RequestId,
            envelope.ServerSeq,
            envelope.Payload.Memory,
            bytes,
            string.IsNullOrWhiteSpace(error?.Code) ? null : error.Code,
            string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message);
    }

    public static ulong ReadClientRequestId(ReadOnlyMemory<byte> requestBytes)
    {
        return YuoClientEnvelopeV1.Parser.ParseFrom(requestBytes.Span).RequestId;
    }
}
