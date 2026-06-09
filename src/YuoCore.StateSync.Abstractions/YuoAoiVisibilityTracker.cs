namespace YuoCore.StateSync;

/// <summary>
/// 按观察者维护上一帧可见集合，并生成 Enter / Update / Leave 差分。
/// 它不维护 AOI Cell，也不直接推送客户端；MapFiber 可以先用 IYuoAoiQuery 算出当前可见列表，
/// 再用本 tracker 把“可见性变化”转换成 StateSync delta。
/// </summary>
public sealed class YuoAoiVisibilityTracker
{
    private readonly Dictionary<long, Dictionary<long, uint>> _visibleVersionsByObserver = [];
    private readonly Dictionary<long, long> _lastTickByObserver = [];

    public YuoStateDelta BuildDelta(
        long tick,
        long observerNetworkId,
        IEnumerable<YuoAoiVisibleEntity> currentVisible,
        IReadOnlyDictionary<long, YuoEntityState> statesByNetworkId)
    {
        if (currentVisible is null)
        {
            throw new ArgumentNullException(nameof(currentVisible));
        }

        if (statesByNetworkId is null)
        {
            throw new ArgumentNullException(nameof(statesByNetworkId));
        }

        ValidateTick(tick, observerNetworkId);

        if (!_visibleVersionsByObserver.TryGetValue(observerNetworkId, out var previousVersions))
        {
            previousVersions = [];
            _visibleVersionsByObserver.Add(observerNetworkId, previousVersions);
        }

        var changes = new List<YuoStateChange>();
        var nextVersions = new Dictionary<long, uint>();

        foreach (var visible in currentVisible)
        {
            if (!statesByNetworkId.TryGetValue(visible.NetworkId, out var state))
            {
                continue;
            }

            nextVersions[visible.NetworkId] = state.Version;
            if (!previousVersions.TryGetValue(visible.NetworkId, out var previousVersion))
            {
                changes.Add(new YuoStateChange(YuoStateChangeKind.Enter, state, visible.NetworkId));
                continue;
            }

            if (previousVersion != state.Version)
            {
                changes.Add(new YuoStateChange(YuoStateChangeKind.Update, state, visible.NetworkId));
            }
        }

        foreach (var oldNetworkId in previousVersions.Keys.OrderBy(static networkId => networkId))
        {
            if (!nextVersions.ContainsKey(oldNetworkId))
            {
                changes.Add(new YuoStateChange(YuoStateChangeKind.Leave, null, oldNetworkId));
            }
        }

        _visibleVersionsByObserver[observerNetworkId] = nextVersions;
        var baseTick = _lastTickByObserver.TryGetValue(observerNetworkId, out var lastTick)
            ? lastTick
            : (long?)null;
        _lastTickByObserver[observerNetworkId] = tick;
        return new YuoStateDelta(tick, changes, baseTick);
    }

    public IReadOnlyCollection<long> GetVisibleNetworkIds(long observerNetworkId)
    {
        return _visibleVersionsByObserver.TryGetValue(observerNetworkId, out var visible)
            ? visible.Keys.ToArray()
            : [];
    }

    public bool RemoveObserver(long observerNetworkId)
    {
        var removed = _visibleVersionsByObserver.Remove(observerNetworkId);
        _lastTickByObserver.Remove(observerNetworkId);
        return removed;
    }

    public void Clear()
    {
        _visibleVersionsByObserver.Clear();
        _lastTickByObserver.Clear();
    }

    private void ValidateTick(long tick, long observerNetworkId)
    {
        if (_lastTickByObserver.TryGetValue(observerNetworkId, out var lastTick) && tick <= lastTick)
        {
            throw new InvalidOperationException(
                $"AOI visibility delta tick must increase for observer {observerNetworkId}. Last tick: {lastTick}, incoming tick: {tick}.");
        }
    }
}
