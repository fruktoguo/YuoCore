namespace YuoCore.Actor;

/// <summary>
/// Actor 消息的基础接口。所有在 Actor 之间传递的消息必须实现此接口。
/// </summary>
public interface IActorMessage
{
    /// <summary>目标 Actor 的 ID。</summary>
    ActorId TargetActorId { get; }
}

/// <summary>
/// 需要响应（RPC 风格）的 Actor 请求消息。
/// 泛型参数 TResponse 标识期望的响应类型。
/// </summary>
/// <typeparam name="TResponse">响应消息的类型。</typeparam>
public interface IActorRequest<TResponse> : IActorMessage
{
}

/// <summary>
/// Actor 响应消息的基接口。在 Invoke 调用链中返回给请求方。
/// </summary>
public interface IActorResponse
{
    /// <summary>对应请求的 RequestId，用于匹配请求-响应对。</summary>
    Guid RequestId { get; }

    /// <summary>可选的错误信息，为空表示成功。</summary>
    ActorError? Error { get; }
}
