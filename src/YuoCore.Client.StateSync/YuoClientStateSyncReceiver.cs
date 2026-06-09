using YuoCore.Protocol;
using YuoCore.StateSync;
using YuoCore.StateSync.Protocol;

namespace YuoCore.Client.StateSync;

/// <summary>
/// 客户端 StateSync 推送类型。
/// Snapshot 会重置镜像表，Delta 会按 BaseTick 连续应用；其他 opcode 会被忽略。
/// </summary>
public enum YuoClientStateSyncMessageKind
{
    Ignored = 0,
    Snapshot = 1,
    Delta = 2,
    Subscription = 3
}

/// <summary>
/// 一次服务端 StateSync 推送的处理结果。
/// ApplyResult 为 null 表示该 envelope 不是 StateSync 消息，调用方可以继续交给其他业务分发器。
/// </summary>
public sealed record YuoClientStateSyncReceiveResult(
    YuoClientStateSyncMessageKind Kind,
    int OpCode,
    ulong RequestId,
    ulong ServerSeq,
    long? Tick,
    string? StreamId,
    YuoStateSyncApplyResult? ApplyResult)
{
    public bool IsStateSyncMessage => Kind != YuoClientStateSyncMessageKind.Ignored;
}

/// <summary>
/// Unity/C# 客户端侧的 StateSync 推送接收器。
/// 它只解析服务端 envelope、转换 StateSnapshot/StateDelta，并应用到纯 Mirror；
/// 不持有 WebSocket，不创建 Unity GameObject，也不依赖 ECS 或服务端 Gateway。
/// </summary>
public sealed class YuoClientStateSyncReceiver
{
    public YuoClientStateSyncReceiver(YuoStateSyncMirror? mirror = null)
    {
        Mirror = mirror ?? new YuoStateSyncMirror();
    }

    public YuoStateSyncMirror Mirror { get; }

    /// <summary>
    /// 最近一次 StateSync snapshot/delta 携带的流标识。
    /// 重连恢复时可复用它，避免客户端忘记自己正在确认哪条 AOI/玩家观察流。
    /// </summary>
    public string? LastStreamId { get; private set; }

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

        var snapshot = YuoStateSyncProtocolMapper.ToStateSnapshot(message);
        var result = Mirror.ApplySnapshot(snapshot);
        var streamId = NormalizeStreamId(message.StreamId);
        if (streamId is not null)
        {
            LastStreamId = streamId;
        }

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

        var delta = YuoStateSyncProtocolMapper.ToStateDelta(message);
        var result = Mirror.ApplyDelta(delta);
        var streamId = NormalizeStreamId(message.StreamId);
        if (streamId is not null)
        {
            LastStreamId = streamId;
        }

        return new YuoClientStateSyncReceiveResult(
            YuoClientStateSyncMessageKind.Delta,
            YuoCoreProtocolOpCodes.StateDelta,
            requestId,
            serverSeq,
            delta.Tick,
            streamId,
            result);
    }

    public void Clear()
    {
        Mirror.Clear();
        LastStreamId = null;
    }

    private static string? NormalizeStreamId(string? streamId)
    {
        return string.IsNullOrWhiteSpace(streamId) ? null : streamId;
    }
}
