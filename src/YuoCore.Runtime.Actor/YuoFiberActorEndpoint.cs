using YuoCore.Actor;

namespace YuoCore.Runtime;

/// <summary>
/// 把 Actor endpoint 绑定到 YuoFiber 自己的 mailbox。
/// 这是 YuoCore 后端最重要的线程安全边界：Actor 消息不会在额外线程里直接改 ECS，
/// 而是排队进入目标 Fiber，在目标 Fiber drain mailbox 时执行 handler。
/// </summary>
public sealed class YuoFiberActorEndpoint : IActorEndpoint
{
    private readonly YuoFiber _fiber;
    private readonly IActorMessageDispatcher _dispatcher;

    public YuoFiberActorEndpoint(YuoFiber fiber, IActorMessageDispatcher dispatcher)
    {
        _fiber = fiber ?? throw new ArgumentNullException(nameof(fiber));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public ValueTask SendAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return _fiber.PostAsync((_, token) => DispatchSendAsync(envelope, token), cancellationToken);
    }

    public async ValueTask<ActorEnvelope> InvokeAsync(
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return await _fiber.CallAsync(async (_, token) =>
        {
            try
            {
                var response = await _dispatcher.DispatchAsync(envelope, token).ConfigureAwait(false);
                return ActorEnvelope.Success(envelope, response);
            }
            catch (ActorException exception)
            {
                return ActorEnvelope.Failure(envelope, exception.Error);
            }
            catch (Exception exception)
            {
                return ActorEnvelope.Failure(envelope, ActorError.HandlerFailed(exception));
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask DispatchSendAsync(ActorEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var result = _dispatcher.DispatchAsync(envelope, cancellationToken);
            if (result.IsCompletedSuccessfully)
            {
                _ = result.Result;
                return ValueTask.CompletedTask;
            }

            return AwaitDispatchSendAsync(result);
        }
        catch (ActorException)
        {
            // Fire-and-forget actor messages must not tear down the owning Fiber tick.
            return ValueTask.CompletedTask;
        }
        catch (Exception)
        {
            // SendAsync 没有 response 通道；异常由宿主日志/监控在更外层接入，不能打穿 mailbox。
            return ValueTask.CompletedTask;
        }
    }

    private static async ValueTask AwaitDispatchSendAsync(ValueTask<object?> dispatchTask)
    {
        try
        {
            await dispatchTask.ConfigureAwait(false);
        }
        catch (ActorException)
        {
        }
        catch (Exception)
        {
        }
    }
}
