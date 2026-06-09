using System.Collections.Concurrent;
using YuoCore.Abstractions;
using YuoCore.Protocol;

namespace YuoCore.Network;

/// <summary>
/// 进程内端点接口，供 InMemoryYuoTransport 注册和调用。
/// </summary>
public interface IYuoEndpoint
{
    /// <summary>接收一条单向消息。</summary>
    ValueTask ReceiveAsync(YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>接收请求并返回响应（RPC 风格）。</summary>
    ValueTask<YuoProtocolEnvelope> InvokeAsync(YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// 基于内存的传输层实现，适用于进程内通信和单元测试。
/// 通过注册 IYuoEndpoint 实现消息的路由和派发。
/// </summary>
public sealed class InMemoryYuoTransport<TAddress> : IYuoTransport<TAddress>
    where TAddress : notnull
{
    private readonly ConcurrentDictionary<TAddress, IYuoEndpoint> _endpoints = new();

    /// <summary>注册一个端点到指定地址。</summary>
    public void Register(TAddress address, IYuoEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoints[address] = endpoint;
    }

    /// <summary>注销指定地址的端点。</summary>
    public void Unregister(TAddress address)
    {
        _endpoints.TryRemove(address, out _);
    }

    /// <inheritdoc />
    public async ValueTask SendAsync(TAddress address, YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);
        await Resolve(address).ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<YuoProtocolEnvelope> InvokeAsync(TAddress address, YuoProtocolEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);
        return await Resolve(address).InvokeAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private IYuoEndpoint Resolve(TAddress address)
    {
        if (_endpoints.TryGetValue(address, out var endpoint))
        {
            return endpoint;
        }

        throw new YuoTransportException(YuoError.NotFound($"No endpoint registered for {address}.").Message);
    }
}
