using YuoCore.Ecs;

namespace YuoCore.Actor;

public interface IYuoActorHandler
{
    Type MessageType { get; }

    Type? ResponseType { get; }

    ValueTask<object?> HandleAsync(YuoEntity entity, object message, CancellationToken cancellationToken);
}

/// <summary>
/// Fire-and-forget Actor 消息入口。
/// Dispatcher 已经保证 entity 是目标 Actor 对应的 ECS 实体，handler 内可以直接操作组件。
/// </summary>
public abstract class YuoActorHandler<TMessage> : IYuoActorHandler
    where TMessage : IActorMessage
{
    public Type MessageType => typeof(TMessage);

    public Type? ResponseType => null;

    public async ValueTask<object?> HandleAsync(YuoEntity entity, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(message);

        if (message is not TMessage typed)
        {
            throw new ActorException(ActorError.Internal(
                $"Actor message type mismatch. Expected {typeof(TMessage).FullName}, got {message.GetType().FullName}."));
        }

        await RunAsync(entity, typed, cancellationToken).ConfigureAwait(false);
        return null;
    }

    protected abstract ValueTask RunAsync(YuoEntity entity, TMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Actor 请求入口，对应 ET 的 MessageLocationHandler<TEntity, Request, Response>。
/// 它不是 ECS System，而是把一次跨 Fiber/跨进程调用转成目标 Entity 内部逻辑。
/// </summary>
public abstract class YuoActorRequestHandler<TRequest, TResponse> : IYuoActorHandler
    where TRequest : IActorRequest<TResponse>
{
    public Type MessageType => typeof(TRequest);

    public Type ResponseType => typeof(TResponse);

    public async ValueTask<object?> HandleAsync(YuoEntity entity, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(message);

        if (message is not TRequest typed)
        {
            throw new ActorException(ActorError.Internal(
                $"Actor request type mismatch. Expected {typeof(TRequest).FullName}, got {message.GetType().FullName}."));
        }

        return await RunAsync(entity, typed, cancellationToken).ConfigureAwait(false);
    }

    protected abstract ValueTask<TResponse> RunAsync(YuoEntity entity, TRequest request, CancellationToken cancellationToken);
}
