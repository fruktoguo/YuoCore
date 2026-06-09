using System.Collections.Concurrent;

namespace YuoCore.Actor;

/// <summary>
/// 基于内存的 Actor 传输层实现。进程内 Actor 直接通过 IActorEndpoint 进行方法调用。
/// 适用于单进程测试或开发模式，不涉及序列化和网络开销。
/// </summary>
public sealed class InMemoryActorTransport : IActorTransport
{
    private readonly ConcurrentDictionary<ActorAddress, IActorEndpoint> _endpoints = new();

    /// <summary>注册一个端点到指定地址。</summary>
    public void RegisterEndpoint(ActorAddress address, IActorEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpoints[address] = endpoint;
    }

    /// <summary>注销指定地址的端点。</summary>
    public void UnregisterEndpoint(ActorAddress address)
    {
        _endpoints.TryRemove(address, out _);
    }

    /// <inheritdoc />
    public ValueTask SendAsync(
        ActorAddress address,
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);
        var endpoint = ResolveEndpoint(address);
        return endpoint.SendAsync(envelope, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<ActorEnvelope> InvokeAsync(
        ActorAddress address,
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);
        var endpoint = ResolveEndpoint(address);
        return endpoint.InvokeAsync(envelope, cancellationToken);
    }

    private IActorEndpoint ResolveEndpoint(ActorAddress address)
    {
        if (_endpoints.TryGetValue(address, out var endpoint))
        {
            return endpoint;
        }

        throw new ActorException(ActorError.NotFound($"No in-memory endpoint registered for {address}."));
    }
}
