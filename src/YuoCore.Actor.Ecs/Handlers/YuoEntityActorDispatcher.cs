using YuoCore.Ecs;

namespace YuoCore.Actor;

public sealed class YuoEntityActorDispatcher(
    YuoEntityActorRegistry entities,
    YuoActorHandlerRegistry handlers,
    YuoWorld? ownedWorld = null) : IActorMessageDispatcher
{
    private readonly YuoEntityActorRegistry _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly YuoActorHandlerRegistry _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));

    // 非 null 时强制目标实体归属本 Fiber 的 World。
    // Registry 是宿主级共享表，没有这道边界会让别的 Fiber 的 handler 直接改本 World 的 ECS 状态。
    private readonly YuoWorld? _ownedWorld = ownedWorld;

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

        if (_ownedWorld is not null && !ReferenceEquals(entity.World, _ownedWorld))
        {
            throw new ActorException(ActorError.NotFound(
                $"Actor entity {envelope.TargetActorId} is not owned by this fiber."));
        }

        var messageType = envelope.Message.GetType();
        if (!_handlers.TryGet(messageType, out var handler))
        {
            throw new ActorException(ActorError.NotFound($"Actor handler was not found for {messageType.FullName}."));
        }

        return await handler.HandleAsync(entity, envelope.Message, cancellationToken).ConfigureAwait(false);
    }
}
