using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace YuoCore.Persistence.Hosting;

/// <summary>
/// PersistenceQueue 的通用后台写回服务。
/// 它放在 Hosting 可选包中，避免基础 Persistence 抽象强制依赖 Microsoft.Extensions.Hosting。
/// </summary>
public sealed class YuoPersistenceBackgroundService(
    YuoPersistenceQueue queue,
    IYuoSnapshotStore snapshotStore,
    IYuoEventStore eventStore,
    IOptions<YuoPersistenceWriterOptions>? options = null,
    IYuoPersistenceFailureSink? failureSink = null,
    IYuoPersistenceOutbox? outbox = null) : BackgroundService
{
    private readonly YuoPersistenceQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly IYuoSnapshotStore _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    private readonly IYuoEventStore _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    private readonly YuoPersistenceWriterOptions _options = ValidateOptions(
        options?.Value ?? new YuoPersistenceWriterOptions());
    private long _outboxAckBlockedAt;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_options.CompleteQueueOnStop)
        {
            // 停服时先关闭写端，避免生产者在 base.StopAsync 之后、DrainPendingCommandsAsync 之前继续写入新命令。
            _queue.TryComplete();
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        if (_options.DrainOnStop)
        {
            await DrainPendingCommandsAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_options.CompactOutboxOnStop && outbox is not null)
        {
            await outbox.CompactAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<YuoPersistenceCommand> batch;
            try
            {
                batch = await _queue.DequeueBatchAsync(_options.BatchSize, stoppingToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            foreach (var command in batch)
            {
                try
                {
                    await ExecuteAndAcknowledgeCommandAsync(command, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (stoppingToken.IsCancellationRequested)
                {
                    MarkOutboxAckBlocked(command);
                    // 该命令已经从内存队列出队，停服取消会让它不再出现在 StopAsync 的 drain 阶段。
                    // 使用非取消 token 记录失败，避免没有 outbox 的宿主静默丢失 in-flight 命令。
                    await ReportCommandFailureAsync(command, exception, CancellationToken.None).ConfigureAwait(false);
                    return;
                }
                catch (Exception exception) when (!_options.StopOnCommandError)
                {
                    MarkOutboxAckBlocked(command);
                    await ReportCommandFailureAsync(command, exception, stoppingToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask DrainPendingCommandsAsync(CancellationToken cancellationToken)
    {
        for (var batchIndex = 0; batchIndex < _options.MaxStopDrainBatches; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = _queue.TryDequeueBatch(_options.BatchSize);
            if (batch.Count == 0)
            {
                return;
            }

            foreach (var command in batch)
            {
                try
                {
                    await ExecuteAndAcknowledgeCommandAsync(command, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (!_options.StopOnCommandError)
                {
                    MarkOutboxAckBlocked(command);
                    await ReportCommandFailureAsync(command, exception, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private ValueTask ExecuteCommandAsync(YuoPersistenceCommand command, CancellationToken cancellationToken)
    {
        var actual = command is YuoOutboxPersistenceCommand outboxCommand
            ? outboxCommand.Inner
            : command;

        return actual switch
        {
            SaveSnapshotCommand save => _snapshotStore.SaveAsync(save.Snapshot, cancellationToken),
            AppendEventCommand append => _eventStore.AppendAsync(append.Event, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown persistence command: {actual.GetType().FullName}.")
        };
    }

    private async ValueTask ExecuteAndAcknowledgeCommandAsync(
        YuoPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        await ExecuteCommandWithRetryAsync(command, cancellationToken).ConfigureAwait(false);

        if (command is YuoOutboxPersistenceCommand outboxCommand && outbox is not null)
        {
            if (IsOutboxAckBlocked(outboxCommand.OutboxSequence))
            {
                return;
            }

            // 业务存储已经成功后，ack 是防止重启重复 replay 的本地元数据提交。
            // 这里不再使用 stoppingToken，避免 StopAsync 刚好打断 ack，造成下一次启动重复写入。
            await outbox.AcknowledgeThroughAsync(
                outboxCommand.OutboxSequence,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void MarkOutboxAckBlocked(YuoPersistenceCommand command)
    {
        if (command is not YuoOutboxPersistenceCommand outboxCommand)
        {
            return;
        }

        while (true)
        {
            var current = Volatile.Read(ref _outboxAckBlockedAt);
            if (current != 0 && current <= outboxCommand.OutboxSequence)
            {
                return;
            }

            if (Interlocked.CompareExchange(
                    ref _outboxAckBlockedAt,
                    outboxCommand.OutboxSequence,
                    current) == current)
            {
                return;
            }
        }
    }

    private bool IsOutboxAckBlocked(long outboxSequence)
    {
        var blockedAt = Volatile.Read(ref _outboxAckBlockedAt);
        return blockedAt != 0 && outboxSequence >= blockedAt;
    }

    private async ValueTask ExecuteCommandWithRetryAsync(
        YuoPersistenceCommand command,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteCommandAsync(command, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (attempt < _options.MaxAttempts)
            {
                if (_options.RetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_options.RetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask ReportCommandFailureAsync(
        YuoPersistenceCommand command,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (failureSink is null)
        {
            return;
        }

        try
        {
            await failureSink.OnFailureAsync(command, exception, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 停服取消时不再强行记录失败，避免 shutdown 被失败出口拖住。
        }
        catch
        {
            // 失败出口本身不能成为后台写回循环的新单点故障。
            // 需要强一致 dead letter 的项目，应在宿主层实现可靠 sink 并自行报警。
        }
    }

    private static YuoPersistenceWriterOptions ValidateOptions(YuoPersistenceWriterOptions options)
    {
        if (options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.BatchSize), "batch size must be positive.");
        }

        if (options.MaxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxAttempts), "max attempts must be positive.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RetryDelay), "retry delay cannot be negative.");
        }

        if (options.MaxStopDrainBatches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.MaxStopDrainBatches), "max stop drain batches must be positive.");
        }

        return options;
    }
}

public sealed record YuoPersistenceWriterOptions(
    int BatchSize = 256,
    bool StopOnCommandError = false,
    int MaxAttempts = 1,
    TimeSpan RetryDelay = default,
    bool DrainOnStop = true,
    int MaxStopDrainBatches = 1024,
    bool CompactOutboxOnStop = false,
    bool CompleteQueueOnStop = true);
