using YuoCore.Client;

namespace YuoCore.Client.StateSync;

public sealed record YuoClientStateSyncRecoverResult(
    bool Replayed,
    string Reason,
    string? StreamId,
    IReadOnlyList<YuoClientStateSyncReceiveResult> Results)
{
    public bool HasAppliedResults => Results.Count > 0;
}

/// <summary>
/// 客户端 StateSync 的最小快照恢复助手。
/// 它只复用现有 YuoMmoClient.StateSnapshotRequest 拉取全量快照并写入 Mirror；
/// 不负责重连、ack/replay、退避重试或表现层对象恢复。
/// </summary>
public sealed class YuoClientStateSyncRecovery(
    YuoMmoClient client,
    YuoClientStateSyncReceiver receiver)
{
    private readonly YuoMmoClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public YuoClientStateSyncReceiver Receiver { get; } =
        receiver ?? throw new ArgumentNullException(nameof(receiver));

    public static bool ShouldRequestSnapshot(YuoClientStateSyncReceiveResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        return result.ApplyResult?.RequiresSnapshot == true;
    }

    public async ValueTask<YuoClientStateSyncReceiveResult> RequestSnapshotAsync(
        string playerId,
        long knownTick = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(playerId));
        }

        var snapshot = await _client.RequestStateSnapshotAsync(playerId, knownTick, cancellationToken)
            .ConfigureAwait(false);
        return Receiver.ApplyStateSnapshot(
            snapshot,
            requestId: 0,
            serverSeq: snapshot.Tick < 0 ? 0UL : checked((ulong)snapshot.Tick));
    }

    public async ValueTask<IReadOnlyList<YuoClientStateSyncReceiveResult>> RecoverAsync(
        string playerId,
        long knownTick = 0,
        string? streamId = null,
        CancellationToken cancellationToken = default)
    {
        return (await RecoverDetailedAsync(playerId, knownTick, streamId, cancellationToken)
                .ConfigureAwait(false))
            .Results;
    }

    public async ValueTask<YuoClientStateSyncRecoverResult> RecoverDetailedAsync(
        string playerId,
        long knownTick = 0,
        string? streamId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(playerId));
        }

        var resolvedStreamId = string.IsNullOrWhiteSpace(streamId) ? Receiver.LastStreamId : streamId;
        var response = await _client.RecoverStateSyncAsync(
                playerId,
                knownTick,
                resolvedStreamId,
                cancellationToken)
            .ConfigureAwait(false);
        var results = new List<YuoClientStateSyncReceiveResult>(
            response.Snapshot is null ? response.Deltas.Count : response.Deltas.Count + 1);
        var responseStreamId = string.IsNullOrWhiteSpace(response.StreamId) ? null : response.StreamId;

        if (response.Snapshot is not null)
        {
            if (responseStreamId is not null && string.IsNullOrWhiteSpace(response.Snapshot.StreamId))
            {
                response.Snapshot.StreamId = responseStreamId;
            }

            results.Add(Receiver.ApplyStateSnapshot(
                response.Snapshot,
                requestId: 0,
                serverSeq: response.Snapshot.Tick < 0 ? 0UL : checked((ulong)response.Snapshot.Tick)));
        }

        foreach (var delta in response.Deltas)
        {
            if (responseStreamId is not null && string.IsNullOrWhiteSpace(delta.StreamId))
            {
                delta.StreamId = responseStreamId;
            }

            results.Add(Receiver.ApplyStateDelta(
                delta,
                requestId: 0,
                serverSeq: delta.Tick < 0 ? 0UL : checked((ulong)delta.Tick)));
        }

        return new YuoClientStateSyncRecoverResult(
            response.Replayed,
            response.Reason,
            responseStreamId,
            results);
    }
}

public sealed class YuoClientStateSyncMultiStreamRecovery(
    YuoMmoClient client,
    YuoClientStateSyncMultiStreamReceiver receiver)
{
    private readonly YuoMmoClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public YuoClientStateSyncMultiStreamReceiver Receiver { get; } =
        receiver ?? throw new ArgumentNullException(nameof(receiver));

    public async ValueTask<IReadOnlyList<YuoClientStateSyncReceiveResult>> RecoverAsync(
        string playerId,
        string streamId,
        long knownTick = 0,
        CancellationToken cancellationToken = default)
    {
        return (await RecoverDetailedAsync(playerId, streamId, knownTick, cancellationToken)
                .ConfigureAwait(false))
            .Results;
    }

    public async ValueTask<YuoClientStateSyncRecoverResult> RecoverDetailedAsync(
        string playerId,
        string streamId,
        long knownTick = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(playerId));
        }

        if (string.IsNullOrWhiteSpace(streamId))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(streamId));
        }

        var response = await _client.RecoverStateSyncAsync(
                playerId,
                knownTick,
                streamId,
                cancellationToken)
            .ConfigureAwait(false);
        var results = new List<YuoClientStateSyncReceiveResult>(
            response.Snapshot is null ? response.Deltas.Count : response.Deltas.Count + 1);
        var responseStreamId = string.IsNullOrWhiteSpace(response.StreamId)
            ? streamId.Trim()
            : response.StreamId;

        if (response.Snapshot is not null)
        {
            if (string.IsNullOrWhiteSpace(response.Snapshot.StreamId))
            {
                response.Snapshot.StreamId = responseStreamId;
            }

            results.Add(Receiver.ApplyStateSnapshot(
                response.Snapshot,
                requestId: 0,
                serverSeq: response.Snapshot.Tick < 0 ? 0UL : checked((ulong)response.Snapshot.Tick)));
        }

        foreach (var delta in response.Deltas)
        {
            if (string.IsNullOrWhiteSpace(delta.StreamId))
            {
                delta.StreamId = responseStreamId;
            }

            results.Add(Receiver.ApplyStateDelta(
                delta,
                requestId: 0,
                serverSeq: delta.Tick < 0 ? 0UL : checked((ulong)delta.Tick)));
        }

        return new YuoClientStateSyncRecoverResult(
            response.Replayed,
            response.Reason,
            responseStreamId,
            results);
    }
}
