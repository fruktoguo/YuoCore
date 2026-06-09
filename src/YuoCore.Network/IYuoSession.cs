using YuoCore.Protocol;

namespace YuoCore.Network;

/// <summary>
/// 客户端会话抽象接口，代表一个与客户端的逻辑连接。
/// Gateway 使用此接口向客户端发送消息，底层可以是 WebSocket、TCP 或其他传输。
/// </summary>
public interface IYuoSession
{
    /// <summary>会话的唯一标识符。</summary>
    string SessionId { get; }

    /// <summary>向此会话发送协议信封消息。</summary>
    ValueTask SendAsync(YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default);
}
