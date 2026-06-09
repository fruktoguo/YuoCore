using YuoCore.Abstractions;

namespace YuoCore.Gateway;

/// <summary>
/// Gateway dispatcher 的通用保护层。
/// 它负责处理所有客户端类型都需要的基础规则：clientSeq 去重、serverSeq 分配和异常转错误响应。
/// 具体登录、移动、战斗等业务仍然留给内层 dispatcher，避免 Gateway 基础设施混入游戏逻辑。
/// </summary>
public sealed class YuoGatewayDispatchPipeline(IYuoGatewayDispatcher inner) : IYuoGatewayDispatcher
{
    private readonly IYuoGatewayDispatcher _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
        YuoGatewaySession session,
        YuoGatewayInboundMessage message,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);

        if (!session.TryAcceptClientSeq(message.ClientSeq, out var previousClientSeq))
        {
            return Error(message, session.NextServerSeq(), YuoError.Invalid("stale client sequence."));
        }

        try
        {
            var response = await _inner.DispatchAsync(session, message, cancellationToken).ConfigureAwait(false);
            if (response.ServerSeq == 0)
            {
                return response with { ServerSeq = session.NextServerSeq() };
            }

            session.AdvanceServerSeqAtLeast(response.ServerSeq);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            session.TryRestoreClientSeq(message.ClientSeq, previousClientSeq);
            throw;
        }
        catch (YuoException exception)
        {
            return Error(message, session.NextServerSeq(), exception.Error);
        }
        catch (Exception exception)
        {
            return Error(message, session.NextServerSeq(), YuoError.Failed("gateway dispatch failed.", exception.Message));
        }
    }

    private static YuoGatewayOutboundMessage Error(
        YuoGatewayInboundMessage request,
        ulong serverSeq,
        YuoError error)
    {
        return new YuoGatewayOutboundMessage(
            request.Version,
            request.OpCode,
            request.RequestId,
            serverSeq,
            ReadOnlyMemory<byte>.Empty,
            error);
    }
}
