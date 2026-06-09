using YuoCore.Protocol;

namespace YuoCore.Network;

/// <summary>
/// 内部传输层抽象。进程内测试可以用 InMemory，跨进程时替换成 TCP/KCP/WebSocket 等实现。
/// </summary>
public interface IYuoTransport<TAddress>
    where TAddress : notnull
{
    ValueTask SendAsync(TAddress address, YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default);

    ValueTask<YuoProtocolEnvelope> InvokeAsync(TAddress address, YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default);
}
