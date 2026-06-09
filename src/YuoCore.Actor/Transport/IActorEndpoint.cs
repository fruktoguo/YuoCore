namespace YuoCore.Actor;

/// <summary>
/// Actor 端点接口。表示一个能够接收和响应 Actor 消息的通信端点。
/// 每个 ActorFiber 可以作为 IActorEndpoint 被注册到传输层。
/// </summary>
public interface IActorEndpoint
{
    /// <summary>向此端点发送一条单向消息（fire-and-forget）。</summary>
    ValueTask SendAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>向此端点发送请求并等待响应（RPC 调用）。</summary>
    ValueTask<ActorEnvelope> InvokeAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default);
}
