using System.Text.Json;

namespace YuoCore.Persistence;

/// <summary>
/// 基于本地 JSONL 文件的 dead-letter 存储。
/// 它只依赖 BCL，适合单机、小型服务端和测试环境；正式 MMO 可以替换为 PostgreSQL/对象存储实现。
/// </summary>
public sealed class YuoFilePersistenceDeadLetterStore : IYuoPersistenceDeadLetterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public YuoFilePersistenceDeadLetterStore(string directory, string name = "persistence-dead-letter")
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("dead-letter directory cannot be empty.", nameof(directory));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("dead-letter name cannot be empty.", nameof(name));
        }

        Directory.CreateDirectory(directory);
        _path = System.IO.Path.Combine(directory, $"{name}.jsonl");
    }

    public string Path => _path;

    public async ValueTask AppendAsync(
        YuoPersistenceDeadLetterRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        var frame = ToFrame(record);
        var line = JsonSerializer.Serialize(frame, JsonOptions) + Environment.NewLine;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_path, line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 读取当前文件中的所有有效 dead-letter 记录。
    /// 该方法复制文件内容后解析，避免后台继续追加时影响调用方枚举。
    /// </summary>
    public async ValueTask<IReadOnlyList<YuoPersistenceDeadLetterRecord>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        string[] lines;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        var records = new List<YuoPersistenceDeadLetterRecord>(lines.Length);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var frame = JsonSerializer.Deserialize<DeadLetterFrame>(line, JsonOptions)
                        ?? throw new InvalidOperationException("Invalid dead-letter frame.");
            records.Add(FromFrame(frame));
        }

        return records;
    }

    private static DeadLetterFrame ToFrame(YuoPersistenceDeadLetterRecord record)
    {
        return new DeadLetterFrame(
            ToCommandFrame(record.Command),
            record.CommandType,
            record.ExceptionType,
            record.ExceptionMessage,
            record.FailedAt);
    }

    private static YuoPersistenceDeadLetterRecord FromFrame(DeadLetterFrame frame)
    {
        return new YuoPersistenceDeadLetterRecord(
            FromCommandFrame(frame.Command),
            frame.CommandType,
            frame.ExceptionType,
            frame.ExceptionMessage,
            frame.FailedAt);
    }

    private static CommandFrame ToCommandFrame(YuoPersistenceCommand command)
    {
        return command switch
        {
            SaveSnapshotCommand save => new CommandFrame(
                Kind: "snapshot",
                SnapshotKind: save.Snapshot.Kind,
                SnapshotId: save.Snapshot.Id,
                PayloadBase64: Convert.ToBase64String(save.Snapshot.Payload.Span),
                TimestampTicks: save.Snapshot.UpdatedAt.Ticks,
                OffsetMinutes: (short)save.Snapshot.UpdatedAt.Offset.TotalMinutes),
            AppendEventCommand append => new CommandFrame(
                Kind: "event",
                Stream: append.Event.Stream,
                EventType: append.Event.EventType,
                PayloadBase64: Convert.ToBase64String(append.Event.Payload.Span),
                TimestampTicks: append.Event.CreatedAt.Ticks,
                OffsetMinutes: (short)append.Event.CreatedAt.Offset.TotalMinutes),
            YuoOutboxPersistenceCommand outbox => new CommandFrame(
                Kind: "outbox",
                OutboxSequence: outbox.OutboxSequence,
                Inner: ToCommandFrame(outbox.Inner)),
            _ => throw new InvalidOperationException($"Unsupported dead-letter command: {command.GetType().FullName}.")
        };
    }

    private static YuoPersistenceCommand FromCommandFrame(CommandFrame frame)
    {
        return frame.Kind switch
        {
            "snapshot" => new SaveSnapshotCommand(new YuoSnapshotRecord(
                Required(frame.SnapshotKind, nameof(frame.SnapshotKind)),
                Required(frame.SnapshotId, nameof(frame.SnapshotId)),
                Convert.FromBase64String(RequiredPayload(frame.PayloadBase64)),
                ToDateTimeOffset(frame))),
            "event" => new AppendEventCommand(new YuoEventRecord(
                Required(frame.Stream, nameof(frame.Stream)),
                Required(frame.EventType, nameof(frame.EventType)),
                Convert.FromBase64String(RequiredPayload(frame.PayloadBase64)),
                ToDateTimeOffset(frame))),
            "outbox" => new YuoOutboxPersistenceCommand(
                frame.OutboxSequence.GetValueOrDefault(),
                FromCommandFrame(frame.Inner ?? throw new InvalidOperationException("Outbox dead-letter frame requires inner command."))),
            _ => throw new InvalidOperationException($"Unsupported dead-letter frame kind: {frame.Kind}.")
        };
    }

    private static DateTimeOffset ToDateTimeOffset(CommandFrame frame)
    {
        return new DateTimeOffset(
            frame.TimestampTicks.GetValueOrDefault(),
            TimeSpan.FromMinutes(frame.OffsetMinutes.GetValueOrDefault()));
    }

    private static string Required(string? value, string name)
    {
        return string.IsNullOrEmpty(value)
            ? throw new InvalidOperationException($"Dead-letter frame missing {name}.")
            : value;
    }

    private static string RequiredPayload(string? value)
    {
        return value ?? throw new InvalidOperationException("Dead-letter frame missing PayloadBase64.");
    }

    private sealed record DeadLetterFrame(
        CommandFrame Command,
        string CommandType,
        string ExceptionType,
        string ExceptionMessage,
        DateTimeOffset FailedAt);

    private sealed record CommandFrame(
        string Kind,
        string? SnapshotKind = null,
        string? SnapshotId = null,
        string? Stream = null,
        string? EventType = null,
        string? PayloadBase64 = null,
        long? TimestampTicks = null,
        short? OffsetMinutes = null,
        long? OutboxSequence = null,
        CommandFrame? Inner = null);
}
