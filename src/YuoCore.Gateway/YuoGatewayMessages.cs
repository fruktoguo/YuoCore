using YuoCore.Abstractions;

namespace YuoCore.Gateway;

/// <summary>
/// Gateway 内部使用的客户端入站消息。
/// WebSocket、TCP 或 Unity SDK 可以用不同网络库接入，但进入 Gateway dispatcher 后统一成这个形状。
/// </summary>
public sealed record YuoGatewayInboundMessage(
    uint Version,
    int OpCode,
    ulong RequestId,
    ulong ClientSeq,
    ReadOnlyMemory<byte> Payload);

/// <summary>
/// Gateway 发回客户端的统一出站消息。
/// Error 不为空时，Payload 应为空或只包含可选错误详情，便于 Web/Unity 统一处理错误。
/// </summary>
public readonly record struct YuoGatewayOutboundMessage(
    uint Version,
    int OpCode,
    ulong RequestId,
    ulong ServerSeq,
    ReadOnlyMemory<byte> Payload,
    YuoError? Error = null);

public interface IYuoClientConnection
{
    Guid ConnectionId { get; }

    ValueTask SendAsync(YuoGatewayOutboundMessage message, CancellationToken cancellationToken = default);
}

public interface IYuoGatewayDispatcher
{
    ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
        YuoGatewaySession session,
        YuoGatewayInboundMessage message,
        CancellationToken cancellationToken = default);
}
