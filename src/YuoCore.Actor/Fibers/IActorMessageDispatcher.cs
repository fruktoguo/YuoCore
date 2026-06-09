namespace YuoCore.Actor;

/// <summary>
/// Actor 消息分发器的抽象接口。
/// 负责将 ActorEnvelope 路由到对应的处理器并返回响应。
/// </summary>
public interface IActorMessageDispatcher
{
    /// <summary>
    /// 分发一条 Actor 消息到目标处理器。
    /// </summary>
    /// <param name="envelope">待处理的消息信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处理器返回的响应对象。</returns>
    ValueTask<object?> DispatchAsync(ActorEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>
/// 基于委托的 IActorMessageDispatcher 实现，允许直接注入 lambda 作为分发逻辑。
/// </summary>
public sealed class DelegateActorMessageDispatcher(
    Func<ActorEnvelope, CancellationToken, ValueTask<object?>> dispatch) : IActorMessageDispatcher
{
    private readonly Func<ActorEnvelope, CancellationToken, ValueTask<object?>> _dispatch =
        dispatch ?? throw new ArgumentNullException(nameof(dispatch));

    /// <inheritdoc />
    public ValueTask<object?> DispatchAsync(ActorEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return _dispatch(envelope, cancellationToken);
    }
}
