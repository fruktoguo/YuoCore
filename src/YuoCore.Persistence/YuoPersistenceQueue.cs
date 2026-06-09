using System.Threading.Channels;

namespace YuoCore.Persistence;

public abstract record YuoPersistenceCommand;

public sealed record SaveSnapshotCommand(YuoSnapshotRecord Snapshot) : YuoPersistenceCommand;

public sealed record AppendEventCommand(YuoEventRecord Event) : YuoPersistenceCommand;

/// <summary>
/// 已写入 outbox/WAL 的持久化命令。
/// 后台写回成功后，Hosting 适配层可以根据 OutboxSequence 确认进度，重启时只重放未确认命令。
/// </summary>
public sealed record YuoOutboxPersistenceCommand : YuoPersistenceCommand
{
    public YuoOutboxPersistenceCommand(long outboxSequence, YuoPersistenceCommand inner)
    {
        if (outboxSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outboxSequence), "outbox sequence must be positive.");
        }

        ArgumentNullException.ThrowIfNull(inner);

        if (inner is YuoOutboxPersistenceCommand)
        {
            throw new ArgumentException("outbox command cannot wrap another outbox command.", nameof(inner));
        }

        OutboxSequence = outboxSequence;
        Inner = inner;
    }

    public long OutboxSequence { get; }

    public YuoPersistenceCommand Inner { get; }
}

public sealed record YuoPersistenceQueueStats(
    int Capacity,
    long PendingCount,
    long TotalEnqueued,
    long TotalDequeued,
    long HighWatermark)
{
    public double BacklogRatio => Capacity <= 0 ? 0 : Math.Clamp(PendingCount / (double)Capacity, 0, 1);
    public YuoPersistenceQueueStats Sanitize()
    {
        // Channel 写入成功后 reader 可能抢先出队，统计计数会出现极短暂的负积压窗口。
        // 对外指标只表达“当前可观察积压”，不能把这个并发窗口暴露给 Prometheus 告警。
        var pending = Math.Max(0, PendingCount);
        return this with
        {
            PendingCount = pending,
            HighWatermark = Math.Max(HighWatermark, pending)
        };
    }
}

/// <summary>
/// 游戏逻辑到持久化层之间的异步队列。
/// GameShard 只负责把保存意图写入队列，不直接等待 PostgreSQL/Redis 慢 IO，避免数据库抖动拖慢 Tick。
/// </summary>
public sealed class YuoPersistenceQueue
{
    private readonly Channel<YuoPersistenceCommand> _channel;
    private long _pendingCount;
    private long _totalDequeued;
    private long _totalEnqueued;
    private long _highWatermark;

    public YuoPersistenceQueue(int capacity = 65_536)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Queue capacity must be positive.");

        Capacity = capacity;
        _channel = Channel.CreateBounded<YuoPersistenceCommand>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
    }

    public int Capacity { get; }

    /// <summary>
    /// 关闭写入端，用于停服阶段明确拒绝新的保存意图。
    /// 已经进入队列的命令仍可被 TryDequeueBatch / DequeueBatchAsync drain。
    /// </summary>
    public bool TryComplete(Exception? error = null)
    {
        return _channel.Writer.TryComplete(error);
    }

    public async ValueTask EnqueueAsync(YuoPersistenceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _channel.Writer.WriteAsync(command, cancellationToken).ConfigureAwait(false);
        MarkEnqueued();
    }

    public async ValueTask EnqueueBatchAsync(
        IReadOnlyList<YuoPersistenceCommand> commands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
        {
            return;
        }

        for (var i = 0; i < commands.Count; i++)
        {
            if (commands[i] is null)
            {
                throw new ArgumentNullException(nameof(commands), $"{nameof(commands)}[{i}] cannot be null.");
            }
        }

        // 批量入口仍逐条写入 bounded channel，保持背压和取消语义不变。
        // TryWrite 负责空闲队列的热路径，避免压测/批量保存时每条命令都创建异步状态。
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            cancellationToken.ThrowIfCancellationRequested();
            if (_channel.Writer.TryWrite(command))
            {
                MarkEnqueued();
                continue;
            }

            await _channel.Writer.WriteAsync(command, cancellationToken).ConfigureAwait(false);
            MarkEnqueued();
        }
    }

    public async ValueTask<YuoPersistenceCommand> DequeueAsync(CancellationToken cancellationToken = default)
    {
        var command = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        MarkDequeued();
        return command;
    }

    public YuoPersistenceQueueStats GetStats()
    {
        return new YuoPersistenceQueueStats(
            Capacity,
            Volatile.Read(ref _pendingCount),
            Volatile.Read(ref _totalEnqueued),
            Volatile.Read(ref _totalDequeued),
            Volatile.Read(ref _highWatermark)).Sanitize();
    }

    public async ValueTask<IReadOnlyList<YuoPersistenceCommand>> DequeueBatchAsync(
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount), "Batch size must be positive.");

        // 先等待第一条，确保后台写回服务空闲时不会忙等。
        var first = await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        var commands = new List<YuoPersistenceCommand>(Math.Min(maxCount, 256)) { first };
        MarkDequeued();

        // 第一条之后只 drain 当前已经到达的命令，不额外等待，避免批处理增加尾延迟。
        while (commands.Count < maxCount && _channel.Reader.TryRead(out var command))
        {
            commands.Add(command);
            MarkDequeued();
        }

        return commands;
    }

    public IReadOnlyList<YuoPersistenceCommand> TryDequeueBatch(int maxCount)
    {
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount), "Batch size must be positive.");

        var commands = new List<YuoPersistenceCommand>(Math.Min(maxCount, 256));
        while (commands.Count < maxCount && _channel.Reader.TryRead(out var command))
        {
            commands.Add(command);
            MarkDequeued();
        }

        return commands;
    }

    private void MarkEnqueued()
    {
        var pending = Interlocked.Increment(ref _pendingCount);
        Interlocked.Increment(ref _totalEnqueued);
        UpdateHighWatermark(pending);
    }

    private void MarkDequeued()
    {
        Interlocked.Decrement(ref _pendingCount);
        Interlocked.Increment(ref _totalDequeued);
    }

    private void UpdateHighWatermark(long pending)
    {
        while (true)
        {
            var current = Volatile.Read(ref _highWatermark);
            if (pending <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _highWatermark, pending, current) == current)
            {
                return;
            }
        }
    }
}
