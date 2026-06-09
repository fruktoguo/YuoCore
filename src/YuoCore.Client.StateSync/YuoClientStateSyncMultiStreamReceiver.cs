using YuoCore.Protocol;
using YuoCore.StateSync;
using YuoCore.StateSync.Protocol;

namespace YuoCore.Client.StateSync;

/// <summary>
/// 客户端多 StateSync stream 接收器。
/// 它面向正式 MMO 的 AOI/地图/兴趣组多订阅场景：每条 stream 拥有独立 Mirror、LastTick 和缺口状态。
/// 简单单流客户端可以继续使用 YuoClientStateSyncReceiver。
/// </summary>
public sealed class YuoClientStateSyncMultiStreamReceiver
{
    private readonly string? _defaultStreamId;

    public YuoClientStateSyncMultiStreamReceiver(
        YuoStateSyncStreamMirrorSet? streams = null,
        string? defaultStreamId = null)
    {
        Streams = streams ?? new YuoStateSyncStreamMirrorSet();
        _defaultStreamId = string.IsNullOrWhiteSpace(defaultStreamId) ? null : defaultStreamId;
    }

    public YuoStateSyncStreamMirrorSet Streams { get; }

    public YuoClientStateSyncReceiveResult ApplyServerEnvelope(ReadOnlyMemory<byte> envelopeBytes)
    {
        var envelope = YuoServerEnvelopeV1.Parser.ParseFrom(envelopeBytes.Span);
        if (envelope.Error is { Code.Length: > 0 })
        {
            throw new YuoClientProtocolException(envelope.Error.Code, envelope.Error.Message);
        }

        return ApplyServerEnvelope(envelope);
    }

    public YuoClientStateSyncReceiveResult ApplyServerEnvelope(YuoServerEnvelopeV1 envelope)
    {
        if (envelope is null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        return envelope.Opcode switch
        {
            YuoCoreProtocolOpCodes.StateSnapshot => ApplyStateSnapshot(
                StateSnapshotMessage.Parser.ParseFrom(envelope.Payload),
                envelope.RequestId,
                envelope.ServerSeq),
            YuoCoreProtocolOpCodes.StateDelta => ApplyStateDelta(
                StateDeltaMessage.Parser.ParseFrom(envelope.Payload),
                envelope.RequestId,
                envelope.ServerSeq),
            YuoCoreProtocolOpCodes.StateSubscription => ApplyStateSubscription(
                StateSubscriptionMessage.Parser.ParseFrom(envelope.Payload),
                envelope.RequestId,
                envelope.ServerSeq),
            _ => new YuoClientStateSyncReceiveResult(
                YuoClientStateSyncMessageKind.Ignored,
                checked((int)envelope.Opcode),
                envelope.RequestId,
                envelope.ServerSeq,
                null,
                null,
                null)
        };
    }

    public YuoClientStateSyncReceiveResult ApplyStateSnapshot(
        StateSnapshotMessage message,
        ulong requestId = 0,
        ulong serverSeq = 0)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var streamId = ResolveStreamId(message.StreamId);
        var snapshot = YuoStateSyncProtocolMapper.ToStateSnapshot(message);
        var result = Streams.ApplySnapshot(streamId, snapshot);

        return new YuoClientStateSyncReceiveResult(
            YuoClientStateSyncMessageKind.Snapshot,
            YuoCoreProtocolOpCodes.StateSnapshot,
            requestId,
            serverSeq,
            snapshot.Tick,
            streamId,
            result);
    }

    public YuoClientStateSyncReceiveResult ApplyStateDelta(
        StateDeltaMessage message,
        ulong requestId = 0,
        ulong serverSeq = 0)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var streamId = ResolveStreamId(message.StreamId);
        var delta = YuoStateSyncProtocolMapper.ToStateDelta(message);
        var result = Streams.ApplyDelta(streamId, delta);

        return new YuoClientStateSyncReceiveResult(
            YuoClientStateSyncMessageKind.Delta,
            YuoCoreProtocolOpCodes.StateDelta,
            requestId,
            serverSeq,
            delta.Tick,
            streamId,
            result);
    }

    public YuoClientStateSyncReceiveResult ApplyStateSubscription(
        StateSubscriptionMessage message,
        ulong requestId = 0,
        ulong serverSeq = 0)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (message.ResetMissing)
        {
            var retained = message.StreamIds
                .Where(static streamId => !string.IsNullOrWhiteSpace(streamId))
                .Select(static streamId => streamId.Trim())
                .ToHashSet(StringComparer.Ordinal);
            foreach (var streamId in Streams.StreamIds)
            {
                if (!retained.Contains(streamId))
                {
                    Streams.Remove(streamId);
                }
            }
        }

        return new YuoClientStateSyncReceiveResult(
            YuoClientStateSyncMessageKind.Subscription,
            YuoCoreProtocolOpCodes.StateSubscription,
            requestId,
            serverSeq,
            null,
            null,
            null);
    }

    public void Clear()
    {
        Streams.Clear();
    }

    private string ResolveStreamId(string? streamId)
    {
        if (!string.IsNullOrWhiteSpace(streamId))
        {
            return streamId;
        }

        if (_defaultStreamId is not null)
        {
            return _defaultStreamId;
        }

        throw new InvalidOperationException(
            "StateSync stream id is required for multi-stream receiver. Provide stream_id in protocol message or configure a default stream id.");
    }
}
