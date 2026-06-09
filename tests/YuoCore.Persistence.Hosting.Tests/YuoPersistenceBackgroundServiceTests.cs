using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using YuoCore.Persistence.Hosting;

namespace YuoCore.Persistence.Hosting.Tests;

public sealed class YuoPersistenceBackgroundServiceTests
{
    [Fact]
    public void Background_service_rejects_null_dependencies()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 0);
        var events = new RecordingEventStore(expectedWrites: 0);

        Assert.Throws<ArgumentNullException>(() => new YuoPersistenceBackgroundService(null!, snapshots, events));
        Assert.Throws<ArgumentNullException>(() => new YuoPersistenceBackgroundService(queue, null!, events));
        Assert.Throws<ArgumentNullException>(() => new YuoPersistenceBackgroundService(queue, snapshots, null!));
    }

    [Fact]
    public async Task Background_service_writes_snapshot_and_event_commands()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
        var events = new RecordingEventStore(expectedWrites: 1);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 8)));

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "p1",
            new byte[] { 1, 2 },
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
            "player-p1",
            "LevelUp",
            new byte[] { 3 },
            DateTimeOffset.UnixEpoch)));

        await snapshots.WaitAsync();
        await events.WaitAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Single(snapshots.Saved);
        Assert.Single(events.Appended);
        Assert.Equal("p1", snapshots.Saved[0].Id);
        Assert.Equal("LevelUp", events.Appended[0].EventType);
    }

    [Fact]
    public async Task Background_service_continues_after_command_error_by_default()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
        var events = new RecordingEventStore(expectedWrites: 1, failFirst: true);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1)));

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
            "broken",
            "Fail",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "p2",
            new byte[] { 9 },
            DateTimeOffset.UnixEpoch)));

        await snapshots.WaitAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Single(snapshots.Saved);
        Assert.Equal(1, events.Failures);
    }

    [Fact]
    public async Task Background_service_reports_command_error_to_failure_sink_and_continues()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
        var events = new RecordingEventStore(expectedWrites: 1, failFirst: true);
        var failures = new RecordingFailureSink(expectedFailures: 1);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1)),
            failures);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
            "broken",
            "Fail",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "p3",
            new byte[] { 7 },
            DateTimeOffset.UnixEpoch)));

        await failures.WaitAsync();
        await snapshots.WaitAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Single(failures.Failures);
        Assert.IsType<AppendEventCommand>(failures.Failures[0].Command);
        Assert.IsType<InvalidOperationException>(failures.Failures[0].Exception);
        Assert.Single(snapshots.Saved);
        Assert.Equal("p3", snapshots.Saved[0].Id);
    }

    [Fact]
    public async Task Background_service_ignores_failure_sink_error_when_continue_mode_is_enabled()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
        var events = new RecordingEventStore(expectedWrites: 1, failFirst: true);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1)),
            new ThrowingFailureSink());

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
            "broken",
            "Fail",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "p4",
            new byte[] { 8 },
            DateTimeOffset.UnixEpoch)));

        await snapshots.WaitAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Single(snapshots.Saved);
        Assert.Equal("p4", snapshots.Saved[0].Id);
    }

    [Fact]
    public async Task Background_service_retries_command_before_reporting_failure()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
        var events = new RecordingEventStore(expectedWrites: 1, failFirst: true);
        var failures = new RecordingFailureSink(expectedFailures: 1);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1, MaxAttempts: 2)),
            failures);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
            "retry",
            "Transient",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "p5",
            new byte[] { 9 },
            DateTimeOffset.UnixEpoch)));

        await events.WaitAsync();
        await snapshots.WaitAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, events.Failures);
        Assert.Single(events.Appended);
        Assert.Empty(failures.Failures);
        Assert.Single(snapshots.Saved);
    }

    [Fact]
    public async Task Background_service_stop_drains_pending_commands_without_waiting_for_new_work()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 2);
        var events = new RecordingEventStore(expectedWrites: 0);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1, DrainOnStop: true)));

        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "stop-1",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "stop-2",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, snapshots.Saved.Count);
        Assert.Equal(0, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Background_service_stop_completes_queue_before_drain_by_default()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
        var events = new RecordingEventStore(expectedWrites: 0);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1, DrainOnStop: true)));

        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "complete-on-stop",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));

        await service.StopAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ChannelClosedException>(async () =>
            await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
                "player",
                "after-stop",
                ReadOnlyMemory<byte>.Empty,
                DateTimeOffset.UnixEpoch))));
        Assert.Single(snapshots.Saved);
        Assert.Equal("complete-on-stop", snapshots.Saved[0].Id);
        Assert.Equal(0, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Background_service_can_keep_queue_open_on_stop_for_custom_hosts()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 0);
        var events = new RecordingEventStore(expectedWrites: 0);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(
                BatchSize: 1,
                DrainOnStop: false,
                CompleteQueueOnStop: false)));

        await service.StopAsync(CancellationToken.None);
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "custom-host-after-stop",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));

        Assert.Equal(1, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Background_service_stop_drain_is_bounded_by_batch_limit()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 2);
        var events = new RecordingEventStore(expectedWrites: 0);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(
                BatchSize: 1,
                DrainOnStop: true,
                MaxStopDrainBatches: 2)));

        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "bounded-1",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "bounded-2",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "bounded-3",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, snapshots.Saved.Count);
        Assert.Equal(1, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Background_service_reports_inflight_command_canceled_by_stop()
    {
        var queue = new YuoPersistenceQueue();
        var snapshots = new RecordingSnapshotStore(expectedWrites: 0);
        var events = new CancelOnStopEventStore();
        var failures = new RecordingFailureSink(expectedFailures: 1);
        using var service = new YuoPersistenceBackgroundService(
            queue,
            snapshots,
            events,
            Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1)),
            failures);

        await service.StartAsync(CancellationToken.None);
        await queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
            "inflight-stop",
            "Canceled",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));

        await events.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        var failure = Assert.Single(failures.Failures);
        Assert.IsType<AppendEventCommand>(failure.Command);
        Assert.IsAssignableFrom<OperationCanceledException>(failure.Exception);
        Assert.Equal(0, queue.GetStats().PendingCount);
    }

    [Fact]
    public async Task Background_service_acknowledges_outbox_command_after_successful_write()
    {
        var directory = CreateTempDirectory();
        try
        {
            var queue = new YuoPersistenceQueue();
            var outbox = new YuoFilePersistenceOutbox(directory);
            var durable = new YuoDurablePersistenceQueue(queue, outbox);
            var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
            var events = new RecordingEventStore(expectedWrites: 0);
            using var service = new YuoPersistenceBackgroundService(
                queue,
                snapshots,
                events,
                Options.Create(new YuoPersistenceWriterOptions(BatchSize: 1)),
                failureSink: null,
                outbox);

            await service.StartAsync(CancellationToken.None);
            await durable.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
                "player",
                "outbox-ack",
                new byte[] { 1 },
                DateTimeOffset.UnixEpoch)));

            await snapshots.WaitAsync();
            await service.StopAsync(CancellationToken.None);

            var replayed = new List<YuoPersistenceOutboxEntry>();
            await foreach (var entry in new YuoFilePersistenceOutbox(directory).ReplayAsync())
            {
                replayed.Add(entry);
            }

            Assert.Single(snapshots.Saved);
            Assert.Empty(replayed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Background_service_compacts_outbox_on_stop_when_enabled()
    {
        var directory = CreateTempDirectory();
        try
        {
            var queue = new YuoPersistenceQueue();
            var outbox = new YuoFilePersistenceOutbox(directory);
            var durable = new YuoDurablePersistenceQueue(queue, outbox);
            var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
            var events = new RecordingEventStore(expectedWrites: 0);
            using var service = new YuoPersistenceBackgroundService(
                queue,
                snapshots,
                events,
                Options.Create(new YuoPersistenceWriterOptions(
                    BatchSize: 1,
                    CompactOutboxOnStop: true)),
                failureSink: null,
                outbox);

            await service.StartAsync(CancellationToken.None);
            await durable.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
                "player",
                "outbox-compact",
                new byte[] { 1 },
                DateTimeOffset.UnixEpoch)));

            await snapshots.WaitAsync();
            await service.StopAsync(CancellationToken.None);

            var outboxPath = Path.Combine(directory, "persistence.outbox");
            var replayed = new List<YuoPersistenceOutboxEntry>();
            await foreach (var entry in new YuoFilePersistenceOutbox(directory).ReplayAsync())
            {
                replayed.Add(entry);
            }

            Assert.Empty(replayed);
            Assert.True(File.Exists(outboxPath));
            Assert.Equal(0, new FileInfo(outboxPath).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Background_service_does_not_ack_past_failed_outbox_command()
    {
        var directory = CreateTempDirectory();
        try
        {
            var queue = new YuoPersistenceQueue();
            var outbox = new YuoFilePersistenceOutbox(directory);
            var durable = new YuoDurablePersistenceQueue(queue, outbox);
            var snapshots = new RecordingSnapshotStore(expectedWrites: 1);
            var events = new RecordingEventStore(expectedWrites: 0, failFirst: true);
            var failures = new RecordingFailureSink(expectedFailures: 1);
            using var service = new YuoPersistenceBackgroundService(
                queue,
                snapshots,
                events,
                Options.Create(new YuoPersistenceWriterOptions(BatchSize: 8, StopOnCommandError: false)),
                failures,
                outbox);

            await service.StartAsync(CancellationToken.None);
            await durable.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
                "player-outbox",
                "FailFirst",
                ReadOnlyMemory<byte>.Empty,
                DateTimeOffset.UnixEpoch)));
            await durable.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
                "player",
                "outbox-after-failure",
                new byte[] { 2 },
                DateTimeOffset.UnixEpoch)));

            await failures.WaitAsync();
            await snapshots.WaitAsync();
            await service.StopAsync(CancellationToken.None);

            var replayed = new List<YuoPersistenceOutboxEntry>();
            await foreach (var entry in new YuoFilePersistenceOutbox(directory).ReplayAsync())
            {
                replayed.Add(entry);
            }

            Assert.Equal([1L, 2L], replayed.Select(entry => entry.Sequence));
            Assert.IsType<AppendEventCommand>(replayed[0].Command);
            Assert.IsType<SaveSnapshotCommand>(replayed[1].Command);
            Assert.Single(snapshots.Saved);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DeadLetter_failure_sink_persists_failed_command_metadata()
    {
        var store = new InMemoryYuoPersistenceDeadLetterStore();
        var sink = new YuoPersistenceDeadLetterFailureSink(store);
        var command = new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "p6",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch));

        var before = DateTimeOffset.UtcNow;
        await sink.OnFailureAsync(command, new InvalidOperationException("expected dead letter."));
        var after = DateTimeOffset.UtcNow;

        var record = Assert.Single(store.GetRecords());
        Assert.Same(command, record.Command);
        Assert.EndsWith(nameof(SaveSnapshotCommand), record.CommandType, StringComparison.Ordinal);
        Assert.EndsWith(nameof(InvalidOperationException), record.ExceptionType, StringComparison.Ordinal);
        Assert.Equal("expected dead letter.", record.ExceptionMessage);
        Assert.InRange(record.FailedAt, before, after);
    }

    [Fact]
    public async Task Queue_metrics_observe_current_queue_stats()
    {
        var queue = new YuoPersistenceQueue(capacity: 4);
        await queue.EnqueueAsync(new SaveSnapshotCommand(new YuoSnapshotRecord(
            "player",
            "m1",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        await queue.EnqueueAsync(new AppendEventCommand(new YuoEventRecord(
            "player-m1",
            "Created",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UnixEpoch)));
        _ = await queue.DequeueAsync();

        var meterName = $"YuoCore.Persistence.Tests.{Guid.NewGuid():N}";
        using var metrics = new YuoPersistenceQueueMetrics(queue, meterName);
        using var listener = new MeterListener();
        var longs = new Dictionary<string, long>(StringComparer.Ordinal);
        var doubles = new Dictionary<string, double>(StringComparer.Ordinal);

        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            longs[instrument.Name] = measurement;
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            doubles[instrument.Name] = measurement;
        });

        listener.Start();
        listener.RecordObservableInstruments();

        Assert.Equal(1, longs[YuoPersistenceQueueMetrics.PendingCountInstrument]);
        Assert.Equal(2, longs[YuoPersistenceQueueMetrics.HighWatermarkInstrument]);
        Assert.Equal(2, longs[YuoPersistenceQueueMetrics.TotalEnqueuedInstrument]);
        Assert.Equal(1, longs[YuoPersistenceQueueMetrics.TotalDequeuedInstrument]);
        Assert.Equal(0.25, doubles[YuoPersistenceQueueMetrics.BacklogRatioInstrument], precision: 6);
    }

    private sealed class RecordingSnapshotStore(int expectedWrites) : IYuoSnapshotStore
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<YuoSnapshotRecord> Saved { get; } = [];

        public ValueTask SaveAsync(YuoSnapshotRecord snapshot, CancellationToken cancellationToken = default)
        {
            Saved.Add(snapshot);
            if (Saved.Count >= expectedWrites)
            {
                _completed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<YuoSnapshotRecord?> LoadAsync(
            string kind,
            string id,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<YuoSnapshotRecord?>(Saved.FirstOrDefault(snapshot =>
                snapshot.Kind == kind && snapshot.Id == id));
        }

        public Task WaitAsync()
        {
            return _completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class RecordingEventStore(int expectedWrites, bool failFirst = false) : IYuoEventStore
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _failed;

        public List<YuoEventRecord> Appended { get; } = [];

        public int Failures { get; private set; }

        public ValueTask AppendAsync(YuoEventRecord @event, CancellationToken cancellationToken = default)
        {
            if (failFirst && !_failed)
            {
                _failed = true;
                Failures++;
                throw new InvalidOperationException("expected persistence failure.");
            }

            Appended.Add(@event);
            if (Appended.Count >= expectedWrites)
            {
                _completed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public Task WaitAsync()
        {
            return _completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class CancelOnStopEventStore : IYuoEventStore
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask AppendAsync(YuoEventRecord @event, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class RecordingFailureSink(int expectedFailures) : IYuoPersistenceFailureSink
    {
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(YuoPersistenceCommand Command, Exception Exception)> Failures { get; } = [];

        public ValueTask OnFailureAsync(
            YuoPersistenceCommand command,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            Failures.Add((command, exception));
            if (Failures.Count >= expectedFailures)
            {
                _completed.TrySetResult();
            }

            return ValueTask.CompletedTask;
        }

        public Task WaitAsync()
        {
            return _completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ThrowingFailureSink : IYuoPersistenceFailureSink
    {
        public ValueTask OnFailureAsync(
            YuoPersistenceCommand command,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("expected sink failure.");
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"yuocore-persistence-hosting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
