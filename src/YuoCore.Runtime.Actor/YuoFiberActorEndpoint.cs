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
        // 目标 Fiber 已销毁属于典型的 stale-route：翻译为 404，让 sender 失效缓存并重查 Location。
        try
        {
            return _fiber.PostAsync((_, token) => DispatchSendAsync(envelope, token), cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            throw CreateFiberGoneException();
        }
    }

    public async ValueTask<ActorEnvelope> InvokeAsync(
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        ValueTask<ActorEnvelope> call;
        try
        {
            call = _fiber.CallAsync(async (_, token) =>
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
        }, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            throw CreateFiberGoneException();
        }

        return await call.ConfigureAwait(false);
    }

    private ActorException CreateFiberGoneException()
    {
        return new ActorException(ActorError.NotFound(
            $"Target fiber '{_fiber.Name}' (id={_fiber.Id}) is disposed."));
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

            return AwaitDispatchSendAsync(envelope, result);
        }
        catch (Exception exception)
        {
            // SendAsync 没有 response 通道；异常不得打穿 mailbox，但必须经 Fiber 错误出口可见。
            Report(envelope, exception);
            return ValueTask.CompletedTask;
        }
    }

    private async ValueTask AwaitDispatchSendAsync(ActorEnvelope envelope, ValueTask<object?> dispatchTask)
    {
        try
        {
            await dispatchTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Report(envelope, exception);
        }
    }

    private void Report(ActorEnvelope envelope, Exception exception)
    {
        _fiber.ReportUnhandledException(
            exception,
            $"actor-send target={envelope.TargetActorId} msg={envelope.Message?.GetType().Name ?? "null"}");
    }
}
