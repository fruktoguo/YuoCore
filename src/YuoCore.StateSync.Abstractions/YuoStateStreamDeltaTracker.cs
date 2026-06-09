namespace YuoCore.StateSync;

/// <summary>
/// 按 StateSync stream 维护上一帧实体版本，并从当前实体集合生成 Delta。
/// 它适合 Map/Room 先把实体按 AOI stream 分组，再对每个 stream 独立生成 Enter/Update/Leave。
/// </summary>
public sealed class YuoStateStreamDeltaTracker
{
    private readonly Dictionary<string, Dictionary<long, uint>> _visibleVersionsByStream = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastTickByStream = new(StringComparer.Ordinal);

    public YuoStateDelta BuildDelta(
        string streamId,
        long tick,
        IEnumerable<YuoEntityState> currentStates)
    {
        return BuildDeltaCore(streamId, tick, currentStates, currentStatesAreSorted: false);
    }

    /// <summary>
    /// 从已经按 NetworkId 升序排列的状态集合构建 Delta。
    /// 普通调用方继续使用 BuildDelta；AOI 聚合热路径可以用该入口避免重复排序。
    /// </summary>
    public YuoStateDelta BuildDeltaOrdered(
        string streamId,
        long tick,
        IEnumerable<YuoEntityState> currentStates)
    {
        return BuildDeltaCore(streamId, tick, currentStates, currentStatesAreSorted: true);
    }

    /// <summary>
    /// 只在 stream 真的发生 Enter/Update/Leave 时生成并提交 Delta。
    /// 如果当前实体集合与上一条已发送 Delta 完全一致，返回 false，并且不推进该 stream 的 lastTick。
    /// 这保证跳过空 tick 后，下一条非空 Delta 的 BaseTick 仍指向客户端实际收到过的 tick。
    /// </summary>
    public bool TryBuildDirtyDelta(
        string streamId,
        long tick,
        IEnumerable<YuoEntityState> currentStates,
        out YuoStateDelta delta)
    {
        return TryBuildDirtyDeltaCore(streamId, tick, currentStates, currentStatesAreSorted: false, out delta);
    }

    /// <summary>
    /// 只在 stream 真的发生变化时生成并提交 Delta，且要求 currentStates 已经按 NetworkId 升序排列。
    /// 该入口避免 AOI 聚合层和 tracker 各排序一次。
    /// </summary>
    public bool TryBuildDirtyDeltaOrdered(
        string streamId,
        long tick,
        IEnumerable<YuoEntityState> currentStates,
        out YuoStateDelta delta)
    {
        return TryBuildDirtyDeltaCore(streamId, tick, currentStates, currentStatesAreSorted: true, out delta);
    }

    public bool RemoveStream(string streamId)
    {
        var stream = NormalizeStreamId(streamId);
        var removed = _visibleVersionsByStream.Remove(stream);
        _lastTickByStream.Remove(stream);
        return removed;
    }

    public void Clear()
    {
        _visibleVersionsByStream.Clear();
        _lastTickByStream.Clear();
    }

    private YuoStateDelta BuildDeltaCore(
        string streamId,
        long tick,
        IEnumerable<YuoEntityState> currentStates,
        bool currentStatesAreSorted)
    {
        var stream = NormalizeStreamId(streamId);
        if (currentStates is null)
        {
            throw new ArgumentNullException(nameof(currentStates));
        }

        ValidateTick(stream, tick);

        if (!_visibleVersionsByStream.TryGetValue(stream, out var previousVersions))
        {
            previousVersions = [];
            _visibleVersionsByStream.Add(stream, previousVersions);
        }

        var nextVersions = new Dictionary<long, uint>();
        var changes = new List<YuoStateChange>();
        foreach (var state in EnumerateStates(currentStates, currentStatesAreSorted))
        {
            AddStateChange(state, previousVersions, nextVersions, changes);
        }

        AddLeaveChanges(previousVersions, nextVersions, changes);

        var baseTick = _lastTickByStream.TryGetValue(stream, out var lastTick)
            ? lastTick
            : (long?)null;
        _visibleVersionsByStream[stream] = nextVersions;
        _lastTickByStream[stream] = tick;
        return new YuoStateDelta(tick, changes, baseTick);
    }

    private bool TryBuildDirtyDeltaCore(
        string streamId,
        long tick,
        IEnumerable<YuoEntityState> currentStates,
        bool currentStatesAreSorted,
        out YuoStateDelta delta)
    {
        var stream = NormalizeStreamId(streamId);
        if (currentStates is null)
        {
            throw new ArgumentNullException(nameof(currentStates));
        }

        ValidateTick(stream, tick);

        var hasPrevious = _visibleVersionsByStream.TryGetValue(stream, out var previousVersions);
        previousVersions ??= [];

        var nextVersions = new Dictionary<long, uint>();
        var changes = new List<YuoStateChange>();
        foreach (var state in EnumerateStates(currentStates, currentStatesAreSorted))
        {
            AddStateChange(state, previousVersions, nextVersions, changes);
        }

        AddLeaveChanges(previousVersions, nextVersions, changes);

        if (changes.Count == 0)
        {
            delta = default!;
            return false;
        }

        var baseTick = _lastTickByStream.TryGetValue(stream, out var lastTick)
            ? lastTick
            : (long?)null;
        if (!hasPrevious)
        {
            _visibleVersionsByStream.Add(stream, nextVersions);
        }
        else
        {
            _visibleVersionsByStream[stream] = nextVersions;
        }

        _lastTickByStream[stream] = tick;
        delta = new YuoStateDelta(tick, changes, baseTick);
        return true;
    }

    private static void AddStateChange(
        YuoEntityState state,
        IReadOnlyDictionary<long, uint> previousVersions,
        Dictionary<long, uint> nextVersions,
        List<YuoStateChange> changes)
    {
        if (state is null)
        {
            throw new InvalidOperationException("State stream entity state is required.");
        }

        if (!nextVersions.TryAdd(state.NetworkId, state.Version))
        {
            throw new InvalidOperationException($"State stream contains duplicate network id: {state.NetworkId}.");
        }

        if (!previousVersions.TryGetValue(state.NetworkId, out var previousVersion))
        {
            changes.Add(new YuoStateChange(YuoStateChangeKind.Enter, state, state.NetworkId));
            return;
        }

        if (previousVersion != state.Version)
        {
            changes.Add(new YuoStateChange(YuoStateChangeKind.Update, state, state.NetworkId));
        }
    }

    private static void AddLeaveChanges(
        Dictionary<long, uint> previousVersions,
        IReadOnlyDictionary<long, uint> nextVersions,
        List<YuoStateChange> changes)
    {
        foreach (var oldNetworkId in previousVersions.Keys.OrderBy(static id => id))
        {
            if (!nextVersions.ContainsKey(oldNetworkId))
            {
                changes.Add(new YuoStateChange(YuoStateChangeKind.Leave, null, oldNetworkId));
            }
        }
    }

    private void ValidateTick(string stream, long tick)
    {
        if (tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "State stream delta tick must be positive.");
        }

        if (_lastTickByStream.TryGetValue(stream, out var lastTick) && tick <= lastTick)
        {
            throw new InvalidOperationException(
                $"State stream delta tick must increase for stream {stream}. Last tick: {lastTick}, incoming tick: {tick}.");
        }
    }

    private static IEnumerable<YuoEntityState> EnumerateStates(
        IEnumerable<YuoEntityState> currentStates,
        bool currentStatesAreSorted)
    {
        return currentStatesAreSorted
            ? currentStates
            : currentStates.OrderBy(static item => item.NetworkId);
    }

    private static string NormalizeStreamId(string streamId)
    {
        if (string.IsNullOrWhiteSpace(streamId))
        {
            throw new ArgumentException("StateSync stream id cannot be empty.", nameof(streamId));
        }

        return streamId.Trim();
    }
}
