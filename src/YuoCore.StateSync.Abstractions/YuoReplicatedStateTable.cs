namespace YuoCore.StateSync;

/// <summary>
/// 客户端/镜像侧的最小状态表。
/// 它只维护 NetworkId -> YuoEntityState，不创建 GameObject，不依赖 Unity，也不依赖服务端 ECS。
/// Unity 或 Web 客户端可以在这一层之上再做表现对象绑定。
/// </summary>
public sealed class YuoReplicatedStateTable
{
    private readonly Dictionary<long, YuoEntityState> _states = [];

    public IReadOnlyDictionary<long, YuoEntityState> States => _states;

    public long? LastTick { get; private set; }

    public void ApplySnapshot(YuoStateSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        ValidateSnapshot(snapshot);

        _states.Clear();
        foreach (var state in snapshot.Entities)
        {
            _states[state.NetworkId] = state;
        }

        LastTick = snapshot.Tick;
    }

    public void ApplyDelta(YuoStateDelta delta)
    {
        if (delta is null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        ValidateDeltaTick(delta);
        ValidateDeltaChanges(delta);

        foreach (var change in delta.Changes)
        {
            switch (change.Kind)
            {
                case YuoStateChangeKind.Enter:
                case YuoStateChangeKind.Update:
                    _states[change.NetworkId] = change.State!;
                    break;

                case YuoStateChangeKind.Leave:
                case YuoStateChangeKind.Destroy:
                    _states.Remove(change.NetworkId);
                    break;

                default:
                    throw new InvalidOperationException($"Unknown state change kind: {change.Kind}.");
            }
        }

        LastTick = delta.Tick;
    }

    public bool TryGet(long networkId, out YuoEntityState state)
    {
        return _states.TryGetValue(networkId, out state!);
    }

    public void Clear()
    {
        _states.Clear();
        LastTick = null;
    }

    private void ValidateDeltaTick(YuoStateDelta delta)
    {
        if (LastTick is { } lastTick && delta.Tick <= lastTick)
        {
            throw new InvalidOperationException($"State delta tick must increase. Local last tick: {lastTick}, delta tick: {delta.Tick}.");
        }

        if (delta.BaseTick is not { } baseTick)
        {
            return;
        }

        if (LastTick != baseTick)
        {
            throw new YuoStateSyncGapException(LastTick, baseTick, delta.Tick);
        }
    }

    private static void ValidateDeltaChanges(YuoStateDelta delta)
    {
        if (delta.Changes is null)
        {
            throw new InvalidOperationException("State delta changes are required.");
        }

        foreach (var change in delta.Changes)
        {
            if (change is null)
            {
                throw new InvalidOperationException("State delta change is required.");
            }

            switch (change.Kind)
            {
                case YuoStateChangeKind.Enter:
                case YuoStateChangeKind.Update:
                    if (change.State is null)
                    {
                        throw new InvalidOperationException($"{change.Kind} change must carry entity state: {change.NetworkId}.");
                    }

                    if (change.State.NetworkId != change.NetworkId)
                    {
                        throw new InvalidOperationException(
                            $"{change.Kind} change network id mismatch. Change id: {change.NetworkId}, state id: {change.State.NetworkId}.");
                    }

                    break;

                case YuoStateChangeKind.Leave:
                case YuoStateChangeKind.Destroy:
                    break;

                default:
                    throw new InvalidOperationException($"Unknown state change kind: {change.Kind}.");
            }
        }
    }

    private static void ValidateSnapshot(YuoStateSnapshot snapshot)
    {
        if (snapshot.Entities is null)
        {
            throw new InvalidOperationException("State snapshot entities are required.");
        }

        var networkIds = new HashSet<long>();
        foreach (var state in snapshot.Entities)
        {
            if (state is null)
            {
                throw new InvalidOperationException("State snapshot entity state is required.");
            }

            if (!networkIds.Add(state.NetworkId))
            {
                throw new InvalidOperationException($"State snapshot contains duplicate network id: {state.NetworkId}.");
            }
        }
    }
}
