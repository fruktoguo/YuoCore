using Google.Protobuf;

namespace YuoCore.Client;

public sealed record YuoClientOutboundMessage(
    ulong RequestId,
    ulong ClientSeq,
    int OpCode,
    ReadOnlyMemory<byte> Bytes);

public sealed record YuoClientInboundMessage<TResponse>(
    ulong RequestId,
    ulong ServerSeq,
    int OpCode,
    TResponse Payload)
    where TResponse : IMessage<TResponse>;
