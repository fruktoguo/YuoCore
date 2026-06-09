using YuoCore.Location;
using YuoCore.Protocol;

namespace YuoCore.Network;

/// <summary>
/// 按 ProcessId 选择传输路径。
/// 业务层只提交 ActorAddress；这里判断目标是否属于当前进程，
/// 本进程走内存队列/endpoint，远端进程交给 remoteTransport。
/// </summary>
public sealed class ProcessAwareYuoTransport(
    string localProcessId,
    IYuoTransport<YuoActorAddress> localTransport,
    IYuoTransport<YuoActorAddress>? remoteTransport = null) : IYuoTransport<YuoActorAddress>
{
    private readonly string _localProcessId = string.IsNullOrWhiteSpace(localProcessId)
        ? throw new ArgumentException("Local process id is required.", nameof(localProcessId))
        : localProcessId;
    private readonly IYuoTransport<YuoActorAddress> _localTransport =
        localTransport ?? throw new ArgumentNullException(nameof(localTransport));
    private readonly IYuoTransport<YuoActorAddress>? _remoteTransport = remoteTransport;

    public ValueTask SendAsync(
        YuoActorAddress address,
        YuoProtocolEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);

        return Select(address).SendAsync(address, envelope, cancellationToken);
    }

    public ValueTask<YuoProtocolEnvelope> InvokeAsync(
        YuoActorAddress address,
        YuoProtocolEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);

        return Select(address).InvokeAsync(address, envelope, cancellationToken);
    }

    private IYuoTransport<YuoActorAddress> Select(YuoActorAddress address)
    {
        if (string.Equals(address.ProcessId, _localProcessId, StringComparison.Ordinal))
        {
            return _localTransport;
        }

        return _remoteTransport ?? throw new YuoTransportException(
            $"Remote transport is not configured. localProcess={_localProcessId}, target={address}.");
    }
}
