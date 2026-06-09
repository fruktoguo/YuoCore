namespace YuoCore.StateSync;

public sealed record YuoStateSyncSubscriptionChange(
    IReadOnlyList<string> AddedStreams,
    IReadOnlyList<string> RemovedStreams)
{
    public bool HasChanges => AddedStreams.Count > 0 || RemovedStreams.Count > 0;
}

/// <summary>
/// StateSync stream 订阅表。
/// 它只维护“订阅者 -> stream”和 ACK 游标，不知道 WebSocket、Gateway session、Actor 或 ECS Entity。
/// 服务端可以用它判断某条 stream 的 replay history 是否已经被所有活跃订阅者确认。
/// </summary>
public sealed class YuoStateSyncSubscriptionTable
{
    private static readonly YuoStateSyncSubscriptionChange NoChange = new(Array.Empty<string>(), Array.Empty<string>());

    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _streamsBySubscriber = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _subscribersByStream = new(StringComparer.Ordinal);
    private readonly Dictionary<SubscriptionKey, long> _ackTickBySubscription = [];

    public bool Subscribe(string subscriberId, string streamId)
    {
        var subscriber = Normalize(subscriberId, nameof(subscriberId));
        var stream = Normalize(streamId, nameof(streamId));

        lock (_gate)
        {
            if (!_streamsBySubscriber.TryGetValue(subscriber, out var streams))
            {
                streams = new HashSet<string>(StringComparer.Ordinal);
                _streamsBySubscriber.Add(subscriber, streams);
            }

            if (!_subscribersByStream.TryGetValue(stream, out var subscribers))
            {
                subscribers = new HashSet<string>(StringComparer.Ordinal);
                _subscribersByStream.Add(stream, subscribers);
            }

            var addedToSubscriber = streams.Add(stream);
            subscribers.Add(subscriber);
            return addedToSubscriber;
        }
    }

    public YuoStateSyncSubscriptionChange SetSubscriberStreams(
        string subscriberId,
        IEnumerable<string> streamIds)
    {
        if (streamIds is null)
        {
            throw new ArgumentNullException(nameof(streamIds));
        }

        var subscriber = Normalize(subscriberId, nameof(subscriberId));
        if (streamIds is IReadOnlyList<string> streamList)
        {
            lock (_gate)
            {
                _streamsBySubscriber.TryGetValue(subscriber, out var current);
                if (HasSamePrevalidatedStreams(current, streamList))
                {
                    return NoChange;
                }
            }
        }

        var nextStreams = streamIds
            .Select(static streamId => Normalize(streamId, nameof(streamIds)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        lock (_gate)
        {
            _streamsBySubscriber.TryGetValue(subscriber, out var current);
            if (HasSameStreams(current, nextStreams))
            {
                return NoChange;
            }

            var removed = current is null
                ? Array.Empty<string>()
                : CollectRemovedStreams(current, nextStreams);
            var added = current is null
                ? nextStreams
                : CollectAddedStreams(current, nextStreams);

            foreach (var stream in removed)
            {
                UnsubscribeUnsafe(subscriber, stream);
            }

            foreach (var stream in added)
            {
                SubscribeUnsafe(subscriber, stream);
            }

            return new YuoStateSyncSubscriptionChange(added, removed);
        }
    }

    public bool Unsubscribe(string subscriberId, string streamId)
    {
        var subscriber = Normalize(subscriberId, nameof(subscriberId));
        var stream = Normalize(streamId, nameof(streamId));
        lock (_gate)
        {
            return UnsubscribeUnsafe(subscriber, stream);
        }
    }

    public int RemoveSubscriber(string subscriberId)
    {
        var subscriber = Normalize(subscriberId, nameof(subscriberId));
        string[] streams;
        lock (_gate)
        {
            if (!_streamsBySubscriber.TryGetValue(subscriber, out var currentStreams))
            {
                return 0;
            }

            streams = currentStreams.ToArray();
        }

        var removedCount = 0;
        foreach (var stream in streams)
        {
            if (Unsubscribe(subscriber, stream))
            {
                removedCount++;
            }
        }

        return removedCount;
    }

    public long Acknowledge(string subscriberId, string streamId, long ackTick)
    {
        if (ackTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ackTick), ackTick, "ACK tick must be positive.");
        }

        var subscriber = Normalize(subscriberId, nameof(subscriberId));
        var stream = Normalize(streamId, nameof(streamId));
        lock (_gate)
        {
            SubscribeUnsafe(subscriber, stream);
            var key = new SubscriptionKey(subscriber, stream);
            if (_ackTickBySubscription.TryGetValue(key, out var current))
            {
                ackTick = Math.Max(current, ackTick);
            }

            _ackTickBySubscription[key] = ackTick;
            return ackTick;
        }
    }

    public bool TryGetAckTick(string subscriberId, string streamId, out long ackTick)
    {
        lock (_gate)
        {
            return _ackTickBySubscription.TryGetValue(
                new SubscriptionKey(
                    Normalize(subscriberId, nameof(subscriberId)),
                    Normalize(streamId, nameof(streamId))),
                out ackTick);
        }
    }

    public IReadOnlyCollection<string> GetStreams(string subscriberId)
    {
        var subscriber = Normalize(subscriberId, nameof(subscriberId));
        lock (_gate)
        {
            return _streamsBySubscriber.TryGetValue(subscriber, out var streams)
                ? streams.ToArray()
                : [];
        }
    }

    public IReadOnlyCollection<string> GetSubscribers(string streamId)
    {
        var stream = Normalize(streamId, nameof(streamId));
        lock (_gate)
        {
            return _subscribersByStream.TryGetValue(stream, out var subscribers)
                ? subscribers.ToArray()
                : [];
        }
    }

    public bool TryGetSafePruneTick(string streamId, out long pruneTick)
    {
        var stream = Normalize(streamId, nameof(streamId));
        lock (_gate)
        {
            pruneTick = 0;
            if (!_subscribersByStream.TryGetValue(stream, out var subscribers) || subscribers.Count == 0)
            {
                return false;
            }

            var minAckTick = long.MaxValue;
            foreach (var subscriber in subscribers)
            {
                if (!_ackTickBySubscription.TryGetValue(new SubscriptionKey(subscriber, stream), out var ackTick))
                {
                    return false;
                }

                minAckTick = Math.Min(minAckTick, ackTick);
            }

            if (minAckTick == long.MaxValue)
            {
                return false;
            }

            pruneTick = minAckTick;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _streamsBySubscriber.Clear();
            _subscribersByStream.Clear();
            _ackTickBySubscription.Clear();
        }
    }

    private void SubscribeUnsafe(string subscriber, string stream)
    {
        if (!_streamsBySubscriber.TryGetValue(subscriber, out var streams))
        {
            streams = new HashSet<string>(StringComparer.Ordinal);
            _streamsBySubscriber.Add(subscriber, streams);
        }

        if (!_subscribersByStream.TryGetValue(stream, out var subscribers))
        {
            subscribers = new HashSet<string>(StringComparer.Ordinal);
            _subscribersByStream.Add(stream, subscribers);
        }

        streams.Add(stream);
        subscribers.Add(subscriber);
    }

    private bool UnsubscribeUnsafe(string subscriber, string stream)
    {
        var removed = false;
        if (_streamsBySubscriber.TryGetValue(subscriber, out var streams))
        {
            removed = streams.Remove(stream);
            if (streams.Count == 0)
            {
                _streamsBySubscriber.Remove(subscriber);
            }
        }

        if (_subscribersByStream.TryGetValue(stream, out var subscribers))
        {
            subscribers.Remove(subscriber);
            if (subscribers.Count == 0)
            {
                _subscribersByStream.Remove(stream);
            }
        }

        _ackTickBySubscription.Remove(new SubscriptionKey(subscriber, stream));
        return removed;
    }

    private static bool HasSameStreams(HashSet<string>? current, IReadOnlyList<string> nextStreams)
    {
        if (current is null)
        {
            return nextStreams.Count == 0;
        }

        if (current.Count != nextStreams.Count)
        {
            return false;
        }

        for (var i = 0; i < nextStreams.Count; i++)
        {
            if (!current.Contains(nextStreams[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSamePrevalidatedStreams(HashSet<string>? current, IReadOnlyList<string> nextStreams)
    {
        if (current is null)
        {
            return nextStreams.Count == 0;
        }

        if (current.Count != nextStreams.Count)
        {
            return false;
        }

        // AOI 热路径会反复传入已经由框架生成、去重、trim 过的 streamId 列表。
        // 这里先做无分配 no-change 判断；如果发现重复或未规范化输入，就回落到通用 LINQ/Distinct 路径。
        for (var i = 0; i < nextStreams.Count; i++)
        {
            var stream = nextStreams[i];
            if (!IsPrevalidatedStreamId(stream) || !current.Contains(stream))
            {
                return false;
            }

            for (var j = 0; j < i; j++)
            {
                if (string.Equals(nextStreams[j], stream, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string[] CollectRemovedStreams(HashSet<string> current, IReadOnlyList<string> nextStreams)
    {
        List<string>? removed = null;
        foreach (var stream in current)
        {
            if (ContainsStream(nextStreams, stream))
            {
                continue;
            }

            removed ??= [];
            removed.Add(stream);
        }

        return removed?.ToArray() ?? Array.Empty<string>();
    }

    private static string[] CollectAddedStreams(HashSet<string> current, IReadOnlyList<string> nextStreams)
    {
        List<string>? added = null;
        for (var i = 0; i < nextStreams.Count; i++)
        {
            var stream = nextStreams[i];
            if (current.Contains(stream))
            {
                continue;
            }

            added ??= [];
            added.Add(stream);
        }

        return added?.ToArray() ?? Array.Empty<string>();
    }

    private static bool ContainsStream(IReadOnlyList<string> streams, string stream)
    {
        for (var i = 0; i < streams.Count; i++)
        {
            if (string.Equals(streams[i], stream, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("StateSync subscription id cannot be empty.", parameterName);
        }

        return value.Trim();
    }

    private static bool IsPrevalidatedStreamId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return !char.IsWhiteSpace(value[0]) && !char.IsWhiteSpace(value[value.Length - 1]);
    }

    private readonly record struct SubscriptionKey(string SubscriberId, string StreamId);
}
