using System.Threading.Channels;

namespace YuoCore.Persistence.Tests;

public sealed class YuoPersistenceQueueTests
{
    [Fact]
    public async Task Queue_roundtrips_snapshot_command()
    {
        var queue = new YuoPersistenceQueue();
        var snapshot = new YuoSnapshotRecord("player", "p1", new byte[] { 7, 8 }, DateTimeOffset.UnixEpoch);
        var command = new SaveSnapshotCommand(snapshot);

        await queue.EnqueueAsync(command);
        var dequeued = await queue.DequeueAsync();

        var saved = Assert.IsType<SaveSnapshotCommand>(dequeued);
        Assert.Equal("player", saved.Snapshot.Kind);
        Assert.Equal("p1", saved.Snapshot.Id);
        Assert.Equal(new byte[] { 7, 8 }, saved.Snapshot.Payload.ToArray());
    }

    [Fact]
    public async Task Queue_drains_available_batch_without_waiting_for_full_batch()
    {
        var queue = new YuoPersistenceQueue();
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord("player", "p1", new byte[] { 1 }, DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord("player", "p2", new byte[] { 2 }, DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord("player", "p3", new byte[] { 3 }, DateTimeOffset.UnixEpoch)));

        var batch = await queue.DequeueBatchAsync(maxCount: 2);
        var remaining = await queue.DequeueBatchAsync(maxCount: 10);

        Assert.Equal(2, batch.Count);
        Assert.Single(remaining);
    }

    [Fact]
    public async Task Queue_try_dequeue_batch_drains_only_available_commands()
    {
        var queue = new YuoPersistenceQueue();
        await queue.EnqueueAsync(CreateSnapshotCommand("p1"));
        await queue.EnqueueAsync(CreateSnapshotCommand("p2"));

        var batch = queue.TryDequeueBatch(maxCount: 8);
        var empty = queue.TryDequeueBatch(maxCount: 8);

        Assert.Equal(2, batch.Count);
        Assert.Empty(empty);
        Assert.Equal(0, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Queue_enqueue_batch_tracks_pending_totals_and_high_watermark()
    {
        var queue = new YuoPersistenceQueue(capacity: 8);

        await queue.EnqueueBatchAsync([
            CreateSnapshotCommand("batch-1"),
            CreateSnapshotCommand("batch-2"),
            CreateSnapshotCommand("batch-3")
        ]);

        var stats = queue.GetStats();
        Assert.Equal(3, stats.PendingCount);
        Assert.Equal(3, stats.TotalEnqueued);
        Assert.Equal(0, stats.TotalDequeued);
        Assert.Equal(3, stats.HighWatermark);

        var batch = queue.TryDequeueBatch(maxCount: 8);
        Assert.Equal(3, batch.Count);
        Assert.Equal(0, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Queue_enqueue_batch_accepts_empty_batch()
    {
        var queue = new YuoPersistenceQueue(capacity: 2);

        await queue.EnqueueBatchAsync(Array.Empty<YuoPersistenceCommand>());

        var stats = queue.GetStats();
        Assert.Equal(0, stats.PendingCount);
        Assert.Equal(0, stats.TotalEnqueued);
        Assert.Equal(0, stats.HighWatermark);
    }

    [Fact]
    public async Task Queue_enqueue_batch_rejects_null_inputs_before_writing()
    {
        var queue = new YuoPersistenceQueue(capacity: 4);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await queue.EnqueueBatchAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await queue.EnqueueBatchAsync([
                CreateSnapshotCommand("valid"),
                null!
            ]));

        var stats = queue.GetStats();
        Assert.Equal(0, stats.PendingCount);
        Assert.Equal(0, stats.TotalEnqueued);
    }

    [Fact]
    public async Task Queue_enqueue_batch_does_not_count_canceled_waiting_write()
    {
        var queue = new YuoPersistenceQueue(capacity: 1);
        await queue.EnqueueAsync(CreateSnapshotCommand("existing"));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await queue.EnqueueBatchAsync([
                CreateSnapshotCommand("blocked")
            ], cts.Token));

        var stats = queue.GetStats();
        Assert.Equal(1, stats.PendingCount);
        Assert.Equal(1, stats.TotalEnqueued);
        Assert.Equal(0, stats.TotalDequeued);
        Assert.Equal(1, stats.HighWatermark);
    }

    [Fact]
    public async Task Queue_complete_rejects_future_writes_but_keeps_pending_commands_drainable()
    {
        var queue = new YuoPersistenceQueue(capacity: 4);
        await queue.EnqueueAsync(CreateSnapshotCommand("before-complete"));

        Assert.True(queue.TryComplete());
        Assert.False(queue.TryComplete());

        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await queue.EnqueueAsync(CreateSnapshotCommand("after-complete")));
        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await queue.EnqueueBatchAsync([CreateSnapshotCommand("batch-after-complete")]));

        var drained = queue.TryDequeueBatch(maxCount: 8);
        var command = Assert.Single(drained);
        var save = Assert.IsType<SaveSnapshotCommand>(command);

        Assert.Equal("before-complete", save.Snapshot.Id);
        Assert.Equal(0, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Queue_stats_track_pending_totals_and_high_watermark()
    {
        var queue = new YuoPersistenceQueue(capacity: 4);

        await queue.EnqueueAsync(CreateSnapshotCommand("p1"));
        await queue.EnqueueAsync(CreateSnapshotCommand("p2"));
        await queue.EnqueueAsync(CreateSnapshotCommand("p3"));

        var afterEnqueue = queue.GetStats();
        Assert.Equal(4, afterEnqueue.Capacity);
        Assert.Equal(3, afterEnqueue.PendingCount);
        Assert.Equal(3, afterEnqueue.TotalEnqueued);
        Assert.Equal(0, afterEnqueue.TotalDequeued);
        Assert.Equal(3, afterEnqueue.HighWatermark);
        Assert.Equal(0.75, afterEnqueue.BacklogRatio);

        var batch = await queue.DequeueBatchAsync(maxCount: 2);

        var afterDequeue = queue.GetStats();
        Assert.Equal(2, batch.Count);
        Assert.Equal(1, afterDequeue.PendingCount);
        Assert.Equal(3, afterDequeue.TotalEnqueued);
        Assert.Equal(2, afterDequeue.TotalDequeued);
        Assert.Equal(3, afterDequeue.HighWatermark);
    }

    [Fact]
    public async Task Queue_stats_do_not_count_canceled_waiting_enqueue()
    {
        var queue = new YuoPersistenceQueue(capacity: 1);
        await queue.EnqueueAsync(CreateSnapshotCommand("p1"));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await queue.EnqueueAsync(CreateSnapshotCommand("p2"), cts.Token));

        var stats = queue.GetStats();
        Assert.Equal(1, stats.PendingCount);
        Assert.Equal(1, stats.TotalEnqueued);
        Assert.Equal(0, stats.TotalDequeued);
        Assert.Equal(1, stats.HighWatermark);
    }

    [Fact]
    public void Queue_stats_sanitize_transient_negative_pending_snapshot()
    {
        var stats = new YuoPersistenceQueueStats(
                Capacity: 4,
                PendingCount: -1,
                TotalEnqueued: 10,
                TotalDequeued: 11,
                HighWatermark: 3)
            .Sanitize();

        Assert.Equal(0, stats.PendingCount);
        Assert.Equal(0, stats.BacklogRatio);
        Assert.Equal(3, stats.HighWatermark);
        Assert.Equal(10, stats.TotalEnqueued);
        Assert.Equal(11, stats.TotalDequeued);
    }

    [Fact]
    public void Queue_rejects_invalid_capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new YuoPersistenceQueue(capacity: 0));
    }

    [Fact]
    public async Task InMemory_dead_letter_store_keeps_stable_snapshot()
    {
        var store = new InMemoryYuoPersistenceDeadLetterStore();
        var command = CreateSnapshotCommand("dead");
        var record = new YuoPersistenceDeadLetterRecord(
            command,
            command.GetType().FullName!,
            typeof(InvalidOperationException).FullName!,
            "expected",
            DateTimeOffset.UnixEpoch);

        await store.AppendAsync(record);

        var snapshot = store.GetRecords();
        Assert.Equal(1, store.Count);

        store.Clear();

        Assert.Single(snapshot);
        Assert.Same(command, snapshot[0].Command);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task InMemory_dead_letter_store_honors_cancellation()
    {
        var store = new InMemoryYuoPersistenceDeadLetterStore();
        var command = CreateSnapshotCommand("cancel");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.AppendAsync(new YuoPersistenceDeadLetterRecord(
                command,
                command.GetType().FullName!,
                typeof(InvalidOperationException).FullName!,
                "expected",
                DateTimeOffset.UnixEpoch), cts.Token));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task InMemory_dead_letter_store_rejects_null_record()
    {
        var store = new InMemoryYuoPersistenceDeadLetterStore();

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await store.AppendAsync(null!));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task File_dead_letter_store_persists_records_across_instances()
    {
        var directory = CreateTempDirectory();
        try
        {
            var command = CreateSnapshotCommand("file-dead");
            var store = new YuoFilePersistenceDeadLetterStore(directory);
            await store.AppendAsync(new YuoPersistenceDeadLetterRecord(
                command,
                command.GetType().FullName!,
                typeof(InvalidOperationException).FullName!,
                "expected file dead letter",
                DateTimeOffset.UnixEpoch.AddSeconds(3)));

            var reopened = new YuoFilePersistenceDeadLetterStore(directory);
            var records = await reopened.ReadAllAsync();
            var record = Assert.Single(records);
            var saved = Assert.IsType<SaveSnapshotCommand>(record.Command);

            Assert.Equal("file-dead", saved.Snapshot.Id);
            Assert.EndsWith(nameof(SaveSnapshotCommand), record.CommandType, StringComparison.Ordinal);
            Assert.EndsWith(nameof(InvalidOperationException), record.ExceptionType, StringComparison.Ordinal);
            Assert.Equal("expected file dead letter", record.ExceptionMessage);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(3), record.FailedAt);
            Assert.True(File.Exists(reopened.Path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task File_dead_letter_store_preserves_outbox_sequence_and_inner_command()
    {
        var directory = CreateTempDirectory();
        try
        {
            var inner = new AppendEventCommand(new YuoEventRecord(
                "stream-1",
                "Broken",
                new byte[] { 1, 2, 3 },
                DateTimeOffset.UnixEpoch.AddMinutes(1)));
            var command = new YuoOutboxPersistenceCommand(42, inner);
            var store = new YuoFilePersistenceDeadLetterStore(directory);

            await store.AppendAsync(new YuoPersistenceDeadLetterRecord(
                command,
                command.GetType().FullName!,
                typeof(TimeoutException).FullName!,
                "outbox failed",
                DateTimeOffset.UnixEpoch.AddSeconds(5)));

            var record = Assert.Single(await new YuoFilePersistenceDeadLetterStore(directory).ReadAllAsync());
            var outbox = Assert.IsType<YuoOutboxPersistenceCommand>(record.Command);
            var replayedInner = Assert.IsType<AppendEventCommand>(outbox.Inner);

            Assert.Equal(42, outbox.OutboxSequence);
            Assert.Equal("stream-1", replayedInner.Event.Stream);
            Assert.Equal("Broken", replayedInner.Event.EventType);
            Assert.Equal(new byte[] { 1, 2, 3 }, replayedInner.Event.Payload.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task File_outbox_replays_only_unacknowledged_commands()
    {
        var directory = CreateTempDirectory();
        try
        {
            var outbox = new YuoFilePersistenceOutbox(directory);
            var snapshotSequence = await outbox.AppendAsync(CreateSnapshotCommand("wal-1"));
            var eventSequence = await outbox.AppendAsync(new AppendEventCommand(new YuoEventRecord(
                "player-wal-1",
                "Created",
                new byte[] { 3, 4 },
                DateTimeOffset.UnixEpoch.AddSeconds(1))));

            var all = await ReadReplayAsync(outbox);
            Assert.Equal([snapshotSequence, eventSequence], all.Select(entry => entry.Sequence));
            Assert.IsType<SaveSnapshotCommand>(all[0].Command);
            Assert.IsType<AppendEventCommand>(all[1].Command);

            await outbox.AcknowledgeThroughAsync(snapshotSequence);

            var pending = await ReadReplayAsync(new YuoFilePersistenceOutbox(directory));
            var pendingEntry = Assert.Single(pending);
            Assert.Equal(eventSequence, pendingEntry.Sequence);

            await outbox.CompactAsync();

            var compacted = await ReadReplayAsync(new YuoFilePersistenceOutbox(directory));
            Assert.Single(compacted);
            Assert.Equal(eventSequence, compacted[0].Sequence);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Durable_queue_writes_outbox_before_memory_queue_and_can_replay_pending_commands()
    {
        var directory = CreateTempDirectory();
        try
        {
            var queue = new YuoPersistenceQueue();
            var outbox = new YuoFilePersistenceOutbox(directory);
            var durable = new YuoDurablePersistenceQueue(queue, outbox);

            await durable.EnqueueAsync(CreateSnapshotCommand("durable-1"));

            var queued = Assert.IsType<YuoOutboxPersistenceCommand>(await queue.DequeueAsync());
            Assert.Equal(1, queued.OutboxSequence);
            Assert.IsType<SaveSnapshotCommand>(queued.Inner);

            var replayQueue = new YuoPersistenceQueue();
            var replayDurable = new YuoDurablePersistenceQueue(replayQueue, new YuoFilePersistenceOutbox(directory));
            var replayed = await replayDurable.ReplayPendingAsync();
            var replayedCommand = Assert.IsType<YuoOutboxPersistenceCommand>(await replayQueue.DequeueAsync());

            Assert.Equal(1, replayed);
            Assert.Equal(queued.OutboxSequence, replayedCommand.OutboxSequence);
            Assert.IsType<SaveSnapshotCommand>(replayedCommand.Inner);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Outbox_command_rejects_invalid_wrapping()
    {
        var inner = CreateSnapshotCommand("invalid");

        Assert.Throws<ArgumentOutOfRangeException>(() => new YuoOutboxPersistenceCommand(0, inner));
        Assert.Throws<ArgumentNullException>(() => new YuoOutboxPersistenceCommand(1, null!));
        Assert.Throws<ArgumentException>(() => new YuoOutboxPersistenceCommand(
            2,
            new YuoOutboxPersistenceCommand(1, inner)));
    }

    private static SaveSnapshotCommand CreateSnapshotCommand(string id)
    {
        return new SaveSnapshotCommand(new YuoSnapshotRecord("player", id, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch));
    }

    private static async Task<IReadOnlyList<YuoPersistenceOutboxEntry>> ReadReplayAsync(
        IYuoPersistenceOutbox outbox)
    {
        var entries = new List<YuoPersistenceOutboxEntry>();
        await foreach (var entry in outbox.ReplayAsync())
        {
            entries.Add(entry);
        }

        return entries;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"yuocore-persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
