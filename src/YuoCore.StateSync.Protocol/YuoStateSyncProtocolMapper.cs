using Google.Protobuf;
using YuoCore.Protocol;

namespace YuoCore.StateSync.Protocol;

/// <summary>
/// StateSync 纯数据模型与外部 Protobuf 协议之间的可选转换层。
/// 该层放在独立包中，避免 YuoCore.StateSync 反向依赖 Protocol。
/// </summary>
public static class YuoStateSyncProtocolMapper
{
    public static StateSnapshotMessage ToProtocol(YuoStateSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.Entities is null)
        {
            throw new InvalidOperationException("State snapshot entities are required.");
        }

        var message = new StateSnapshotMessage
        {
            Tick = snapshot.Tick
        };
        foreach (var state in snapshot.Entities)
        {
            message.Entities.Add(ToProtocol(state));
        }

        return message;
    }

    public static YuoStateSnapshot ToStateSnapshot(StateSnapshotMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return new YuoStateSnapshot(
            message.Tick,
            message.Entities.Select(ToEntityState).ToArray());
    }

    public static StateDeltaMessage ToProtocol(YuoStateDelta delta)
    {
        if (delta is null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        if (delta.Changes is null)
        {
            throw new InvalidOperationException("State delta changes are required.");
        }

        var message = new StateDeltaMessage
        {
            Tick = delta.Tick
        };
        if (delta.BaseTick is { } baseTick)
        {
            message.BaseTick = baseTick;
        }

        foreach (var change in delta.Changes)
        {
            message.Changes.Add(ToProtocol(change));
        }

        return message;
    }

    public static YuoStateDelta ToStateDelta(StateDeltaMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return new YuoStateDelta(
            message.Tick,
            message.Changes.Select(ToStateChange).ToArray(),
            message.HasBaseTick ? message.BaseTick : null);
    }

    private static NetworkEntityState ToProtocol(YuoEntityState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        return new NetworkEntityState
        {
            NetworkId = state.NetworkId,
            Kind = state.Kind,
            Version = state.Version,
            Payload = ByteString.CopyFrom(state.Payload.ToArray())
        };
    }

    private static YuoEntityState ToEntityState(NetworkEntityState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        return new YuoEntityState(
            state.NetworkId,
            state.Kind,
            state.Version,
            state.Payload.Memory);
    }

    private static StateChangeMessage ToProtocol(YuoStateChange change)
    {
        if (change is null)
        {
            throw new ArgumentNullException(nameof(change));
        }

        return new StateChangeMessage
        {
            Kind = ToProtocol(change.Kind),
            NetworkId = change.NetworkId,
            State = change.State is null ? null : ToProtocol(change.State)
        };
    }

    private static YuoStateChange ToStateChange(StateChangeMessage message)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return new YuoStateChange(
            ToStateChangeKind(message.Kind),
            message.State is null ? null : ToEntityState(message.State),
            message.NetworkId);
    }

    private static StateChangeKind ToProtocol(YuoStateChangeKind kind)
    {
        return kind switch
        {
            YuoStateChangeKind.Enter => StateChangeKind.Enter,
            YuoStateChangeKind.Update => StateChangeKind.Update,
            YuoStateChangeKind.Leave => StateChangeKind.Leave,
            YuoStateChangeKind.Destroy => StateChangeKind.Destroy,
            _ => StateChangeKind.Unknown
        };
    }

    private static YuoStateChangeKind ToStateChangeKind(StateChangeKind kind)
    {
        return kind switch
        {
            StateChangeKind.Enter => YuoStateChangeKind.Enter,
            StateChangeKind.Update => YuoStateChangeKind.Update,
            StateChangeKind.Leave => YuoStateChangeKind.Leave,
            StateChangeKind.Destroy => YuoStateChangeKind.Destroy,
            _ => throw new InvalidOperationException($"Unknown protocol state change kind: {kind}.")
        };
    }
}
