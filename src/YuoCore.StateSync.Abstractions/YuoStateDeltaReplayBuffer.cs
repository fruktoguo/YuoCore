namespace YuoCore.StateSync;

/// <summary>
/// 固定容量的 StateSync 增量历史。
/// 它用于断线重连或客户端检测到 BaseTick 缺口后的“先尝试 replay，再退回 snapshot”流程。
/// 注意：这里保存的是纯 Delta 数据，不保存 WebSocket 连接、玩家对象或 ECS Entity 引用。
/// </summary>
public sealed class YuoStateDeltaReplayBuffer
{
    private readonly int _capacity;
    private readonly Queue<long> _ticks = [];
    private readonly Dictionary<long, YuoStateDelta> _deltas = [];
    private readonly object _gate = new();

    public YuoStateDeltaReplayBuffer(int capacity = 256)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Replay buffer capacity must be positive.");
        }

        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _deltas.Count;
            }
        }
    }

    public long? OldestTick
    {
        get
        {
            lock (_gate)
            {
                return _ticks.Count == 0 ? null : _ticks.Peek();
            }
        }
    }

    public long? NewestTick { get; private set; }

    public void Add(YuoStateDelta delta)
    {
        if (delta is null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        if (delta.Tick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), delta.Tick, "Delta tick must be positive.");
        }

        lock (_gate)
        {
            if (NewestTick is { } newest && delta.Tick <= newest)
            {
                throw new InvalidOperationException(
                    $"Delta tick must increase inside a replay stream. Newest: {newest}, incoming: {delta.Tick}.");
            }

            _ticks.Enqueue(delta.Tick);
            _deltas[delta.Tick] = delta;
            NewestTick = delta.Tick;

            while (_ticks.Count > _capacity)
            {
                var removed = _ticks.Dequeue();
                _deltas.Remove(removed);
            }
        }
    }

    public YuoStateDeltaReplayResult ReplayAfter(long knownTick)
    {
        lock (_gate)
        {
            if (knownTick < 0)
            {
                return new YuoStateDeltaReplayResult(
                    YuoStateDeltaReplayStatus.InvalidKnownTick,
                    [],
                    knownTick,
                    OldestTickUnsafe(),
                    NewestTick,
                    "known tick cannot be negative.");
            }

            if (NewestTick is null)
            {
                return knownTick == 0
                    ? new YuoStateDeltaReplayResult(
                        YuoStateDeltaReplayStatus.UpToDate,
                        [],
                        knownTick,
                        OldestTickUnsafe(),
                        NewestTick)
                    : new YuoStateDeltaReplayResult(
                        YuoStateDeltaReplayStatus.MissingHistory,
                        [],
                        knownTick,
                        OldestTickUnsafe(),
                        NewestTick,
                        "no delta history is available for the requested stream.");
            }

            if (knownTick == NewestTick)
            {
                return new YuoStateDeltaReplayResult(
                    YuoStateDeltaReplayStatus.UpToDate,
                    [],
                    knownTick,
                    OldestTickUnsafe(),
                    NewestTick);
            }

            if (knownTick > NewestTick)
            {
                return new YuoStateDeltaReplayResult(
                    YuoStateDeltaReplayStatus.MissingHistory,
                    [],
                    knownTick,
                    OldestTickUnsafe(),
                    NewestTick,
                    "known tick is newer than the server stream.");
            }

            var deltas = _ticks
                .Where(tick => tick > knownTick)
                .Select(tick => _deltas[tick])
                .ToArray();

            if (deltas.Length == 0)
            {
                return new YuoStateDeltaReplayResult(
                    YuoStateDeltaReplayStatus.MissingHistory,
                    [],
                    knownTick,
                    OldestTickUnsafe(),
                    NewestTick,
                    "no delta is available after known tick.");
            }

            var first = deltas[0];
            if (first.BaseTick is { } baseTick && baseTick != knownTick)
            {
                return new YuoStateDeltaReplayResult(
                    YuoStateDeltaReplayStatus.MissingHistory,
                    [],
                    knownTick,
                    OldestTickUnsafe(),
                    NewestTick,
                    $"first available delta depends on tick {baseTick}, not known tick {knownTick}.");
            }

            for (var i = 1; i < deltas.Length; i++)
            {
                var previousTick = deltas[i - 1].Tick;
                if (deltas[i].BaseTick is { } nextBaseTick && nextBaseTick != previousTick)
                {
                    return new YuoStateDeltaReplayResult(
                        YuoStateDeltaReplayStatus.MissingHistory,
                        [],
                        knownTick,
                        OldestTickUnsafe(),
                        NewestTick,
                        $"delta {deltas[i].Tick} depends on tick {nextBaseTick}, not previous tick {previousTick}.");
                }
            }

            return new YuoStateDeltaReplayResult(
                YuoStateDeltaReplayStatus.Replayed,
                deltas,
                knownTick,
                OldestTickUnsafe(),
                NewestTick);
        }
    }

    public int PruneThrough(long tick)
    {
        if (tick <= 0)
        {
            return 0;
        }

        lock (_gate)
        {
            var removedCount = 0;
            while (_ticks.Count > 0 && _ticks.Peek() <= tick)
            {
                var removed = _ticks.Dequeue();
                if (_deltas.Remove(removed))
                {
                    removedCount++;
                }
            }

            NewestTick = _ticks.Count == 0 ? null : _ticks.Last();
            return removedCount;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _ticks.Clear();
            _deltas.Clear();
            NewestTick = null;
        }
    }

    private long? OldestTickUnsafe()
    {
        return _ticks.Count == 0 ? null : _ticks.Peek();
    }
}
