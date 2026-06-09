namespace YuoCore.StateSync;

/// <summary>
/// StateDelta 的纯数据压缩器。
/// 它只合并同一个 NetworkId 在同一批待发送 Delta 中的重复变化，不关心 AOI、协议或客户端连接。
/// </summary>
public sealed class YuoStateDeltaCompressor
{
    public YuoStateDelta Compress(YuoStateDelta delta)
    {
        if (delta is null)
        {
            throw new ArgumentNullException(nameof(delta));
        }

        if (delta.Changes is null)
        {
            throw new InvalidOperationException("State delta changes are required.");
        }

        var ordered = new List<long>();
        var compacted = new Dictionary<long, YuoStateChange>();

        foreach (var change in delta.Changes)
        {
            if (change is null)
            {
                throw new InvalidOperationException("State delta change is required.");
            }

            if (!compacted.TryGetValue(change.NetworkId, out var previous))
            {
                ordered.Add(change.NetworkId);
                compacted.Add(change.NetworkId, change);
                continue;
            }

            var merged = Merge(previous, change);
            if (merged is null)
            {
                compacted.Remove(change.NetworkId);
                ordered.Remove(change.NetworkId);
            }
            else
            {
                compacted[change.NetworkId] = merged;
            }
        }

        var changes = new List<YuoStateChange>(compacted.Count);
        foreach (var networkId in ordered)
        {
            if (compacted.TryGetValue(networkId, out var change))
            {
                changes.Add(change);
            }
        }

        return new YuoStateDelta(delta.Tick, changes, delta.BaseTick);
    }

    private static YuoStateChange? Merge(YuoStateChange previous, YuoStateChange next)
    {
        return (previous.Kind, next.Kind) switch
        {
            (YuoStateChangeKind.Enter, YuoStateChangeKind.Update) => next with { Kind = YuoStateChangeKind.Enter },
            (YuoStateChangeKind.Enter, YuoStateChangeKind.Leave) => null,
            (YuoStateChangeKind.Enter, YuoStateChangeKind.Destroy) => null,
            (YuoStateChangeKind.Update, YuoStateChangeKind.Update) => next,
            (YuoStateChangeKind.Update, YuoStateChangeKind.Leave) => next,
            (YuoStateChangeKind.Update, YuoStateChangeKind.Destroy) => next,
            (YuoStateChangeKind.Leave, YuoStateChangeKind.Enter) => next,
            (YuoStateChangeKind.Destroy, YuoStateChangeKind.Enter) => next,
            (_, YuoStateChangeKind.Destroy) => next,
            (_, YuoStateChangeKind.Leave) => next,
            _ => next
        };
    }
}
