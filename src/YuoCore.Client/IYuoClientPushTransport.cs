namespace YuoCore.Client;

/// <summary>
/// 可选的客户端双工传输能力。
/// IYuoClientTransport 只要求 request/response；实现了该接口的传输还可以把服务端主动推送暴露给上层。
/// </summary>
public interface IYuoClientPushTransport : IYuoClientTransport
{
    /// <summary>
    /// 收到 requestId=0 的服务端推送时触发。
    /// 事件参数只包含外部协议 envelope，不包含 Gateway session、Actor、Fiber 或服务端 Entity。
    /// </summary>
    event Action<YuoClientServerMessage>? PushReceived;
}
