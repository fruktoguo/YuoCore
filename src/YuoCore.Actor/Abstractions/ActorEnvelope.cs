namespace YuoCore.Actor;

/// <summary>
/// Actor 消息信封，封装了消息体与路由元数据。
/// 支持单向消息（fire-and-forget）和请求-响应（RPC）两种模式。
/// </summary>
/// <param name="TargetActorId">目标 Actor 的 ID。</param>
/// <param name="Message">消息负载。</param>
/// <param name="SenderActorId">发送方 Actor 的 ID。</param>
/// <param name="RequestId">请求的唯一标识，用于匹配响应。</param>
/// <param name="IsResponse">此信封是否为响应消息。</param>
/// <param name="Error">可选的错误信息（响应消息时使用）。</param>
public sealed record ActorEnvelope(
    ActorId TargetActorId,
    object? Message,
    ActorId SenderActorId = default,
    Guid RequestId = default,
    bool IsResponse = false,
    ActorError? Error = null)
{
    private static long _nextRpcId;

    /// <summary>RPC 调用 ID（进程内递增），用于日志追踪和消息去重。</summary>
    public long RpcId { get; init; }

    /// <summary>是否是一个需要响应的请求（RequestId 非空且 IsResponse 为 false）。</summary>
    public bool IsRequest => RequestId != Guid.Empty && !IsResponse;

    /// <summary>
    /// 创建单向消息信封（fire-and-forget）。
    /// </summary>
    /// <param name="message">实现了 IActorMessage 接口的消息对象。</param>
    /// <param name="senderActorId">发送方的 Actor ID。</param>
    public static ActorEnvelope ForMessage(IActorMessage message, ActorId senderActorId = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new ActorEnvelope(message.TargetActorId, message, senderActorId);
    }

    /// <summary>
    /// 创建 RPC 请求信封，自动分配 RequestId 和 RpcId。
    /// </summary>
    /// <param name="request">RPC 请求消息。</param>
    /// <param name="senderActorId">发送方的 Actor ID。</param>
    /// <param name="requestId">可选的 RequestId，为空时自动生成。</param>
    /// <typeparam name="TResponse">期望的响应类型。</typeparam>
    public static ActorEnvelope ForRequest<TResponse>(
        IActorRequest<TResponse> request,
        ActorId senderActorId = default,
        Guid requestId = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ActorEnvelope(
            request.TargetActorId,
            request,
            senderActorId,
            requestId == Guid.Empty ? Guid.NewGuid() : requestId)
        {
            RpcId = Interlocked.Increment(ref _nextRpcId)
        };
    }

    /// <summary>
    /// 构建成功的响应信封。
    /// </summary>
    /// <param name="request">原始请求信封。</param>
    /// <param name="response">响应消息对象。</param>
    public static ActorEnvelope Success(ActorEnvelope request, object? response)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ActorEnvelope(
            request.SenderActorId,
            response,
            request.TargetActorId,
            request.RequestId,
            IsResponse: true)
        {
            RpcId = request.RpcId
        };
    }

    /// <summary>
    /// 构建失败的响应信封（携带 ActorError）。
    /// </summary>
    /// <param name="request">原始请求信封。</param>
    /// <param name="error">错误信息。</param>
    public static ActorEnvelope Failure(ActorEnvelope request, ActorError error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(error);
        return new ActorEnvelope(
            request.SenderActorId,
            null,
            request.TargetActorId,
            request.RequestId,
            IsResponse: true,
            Error: error)
        {
            RpcId = request.RpcId
        };
    }
}
