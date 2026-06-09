using YuoCore.Client;
using YuoCore.Protocol;

namespace YuoCore.Client.StateSync;

public sealed record YuoClientStateSyncReconnectResult(
    Welcome Welcome,
    YuoClientStateSyncRecoverResult Recovery);

/// <summary>
/// Web/Unity 共用的最小重连恢复组合器。
/// 它只把连接重建和 StateSync recover 串起来，不持有 WebSocket、不决定退避策略，也不接触服务端 ECS。
/// </summary>
public sealed class YuoClientStateSyncReconnectRecovery(
    YuoMmoClientConnection connection,
    YuoClientStateSyncReceiver receiver)
{
    private readonly YuoMmoClientConnection _connection =
        connection ?? throw new ArgumentNullException(nameof(connection));
    private readonly YuoClientStateSyncReceiver _receiver =
        receiver ?? throw new ArgumentNullException(nameof(receiver));

    public async ValueTask<YuoClientStateSyncReconnectResult> ReconnectAndRecoverAsync(
        long? knownTick = null,
        string? streamId = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedKnownTick = knownTick ?? _receiver.Mirror.Table.LastTick ?? 0;
        var resolvedStreamId = string.IsNullOrWhiteSpace(streamId) ? _receiver.LastStreamId : streamId;

        var welcome = await _connection.ReconnectAsync(cancellationToken).ConfigureAwait(false);
        var recovery = new YuoClientStateSyncRecovery(_connection.Client, _receiver);
        var recovered = await recovery.RecoverDetailedAsync(
                _connection.PlayerId,
                resolvedKnownTick,
                resolvedStreamId,
                cancellationToken)
            .ConfigureAwait(false);

        return new YuoClientStateSyncReconnectResult(welcome, recovered);
    }
}
