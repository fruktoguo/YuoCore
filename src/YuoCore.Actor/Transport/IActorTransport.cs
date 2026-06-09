namespace YuoCore.Actor;

/// <summary>
/// Actor 传输层接口。负责将 ActorEnvelope 发送到指定 ActorAddress 对应的端点。
/// 支持同进程内存传输和跨进程网络传输两种实现。
/// </summary>
public interface IActorTransport
{
    /// <summary>向指定地址发送单向消息。</summary>
    ValueTask SendAsync(
        ActorAddress address,
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default);

    /// <summary>向指定地址发送请求并等待响应。</summary>
    ValueTask<ActorEnvelope> InvokeAsync(
        ActorAddress address,
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default);
}
