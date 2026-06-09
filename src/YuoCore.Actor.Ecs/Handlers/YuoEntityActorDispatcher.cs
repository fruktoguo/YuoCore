namespace YuoCore.Actor;

public sealed class YuoEntityActorDispatcher(
    YuoEntityActorRegistry entities,
    YuoActorHandlerRegistry handlers) : IActorMessageDispatcher
{
    private readonly YuoEntityActorRegistry _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly YuoActorHandlerRegistry _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));

    public async ValueTask<object?> DispatchAsync(ActorEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        // Dispatcher 是消息层进入 ECS 的最后一道边界：
        // 这里解析 ActorId -> YuoEntity，确认目标存在后才允许 handler 直接操作组件。
        if (envelope.Message is null)
        {
            return null;
        }

        if (!_entities.TryResolve(envelope.TargetActorId, out var entity))
        {
            throw new ActorException(ActorError.NotFound($"Actor entity {envelope.TargetActorId} was not found."));
        }

        var messageType = envelope.Message.GetType();
        if (!_handlers.TryGet(messageType, out var handler))
        {
            throw new ActorException(ActorError.NotFound($"Actor handler was not found for {messageType.FullName}."));
        }

        return await handler.HandleAsync(entity, envelope.Message, cancellationToken).ConfigureAwait(false);
    }
}
