using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace YuoCore.Persistence;

public sealed record YuoPersistenceOutboxEntry(
    long Sequence,
    YuoPersistenceCommand Command);

public interface IYuoPersistenceOutbox
{
    ValueTask<long> AppendAsync(YuoPersistenceCommand command, CancellationToken cancellationToken = default);

    IAsyncEnumerable<YuoPersistenceOutboxEntry> ReplayAsync(CancellationToken cancellationToken = default);

    ValueTask AcknowledgeThroughAsync(long sequence, CancellationToken cancellationToken = default);

    ValueTask CompactAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 带 outbox 的持久化队列门面。
/// 写入顺序是：先落本地 outbox，再进入内存队列；进程崩溃后可 ReplayPendingAsync 重放未确认命令。
/// </summary>
public sealed class YuoDurablePersistenceQueue(
    YuoPersistenceQueue queue,
    IYuoPersistenceOutbox outbox)
{
    private readonly YuoPersistenceQueue _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    private readonly IYuoPersistenceOutbox _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));

    public YuoPersistenceQueueStats GetStats() => _queue.GetStats();

    public async ValueTask EnqueueAsync(
        YuoPersistenceCommand command,
        CancellationToken cancellationToken = default)
    {
        var sequence = await _outbox.AppendAsync(command, cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(
            new YuoOutboxPersistenceCommand(sequence, command),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReplayPendingAsync(
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "max count must be positive.");
        }

        var count = 0;
        await foreach (var entry in _outbox.ReplayAsync(cancellationToken).ConfigureAwait(false))
        {
            await _queue.EnqueueAsync(
                new YuoOutboxPersistenceCommand(entry.Sequence, entry.Command),
                cancellationToken).ConfigureAwait(false);

            count++;
            if (count >= maxCount)
            {
                break;
            }
        }

        return count;
    }
}

/// <summary>
/// 基于本地文件的最小 outbox/WAL 实现。
/// 它只依赖 BCL，适合单机、测试、小型服务端和后续数据库 outbox 的行为基准。
/// </summary>
public sealed class YuoFilePersistenceOutbox : IYuoPersistenceOutbox
{
    private const int MaxFrameBytes = 64 * 1024 * 1024;
    private const int Magic = 0x594F5042; // YOPB
    private const byte Version = 1;
    private const byte SnapshotCommand = 1;
    private const byte EventCommand = 2;

    private readonly string _logPath;
    private readonly string _ackPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _nextSequence;
    private long _acknowledgedThrough;

    public YuoFilePersistenceOutbox(string directory, string name = "persistence")
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("outbox directory cannot be empty.", nameof(directory));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("outbox name cannot be empty.", nameof(name));
        }

        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, $"{name}.outbox");
        _ackPath = Path.Combine(directory, $"{name}.ack");
        _acknowledgedThrough = ReadAcknowledgedThrough(_ackPath);
        _nextSequence = Math.Max(_acknowledgedThrough, ReadMaxSequence(_logPath));
    }

    public async ValueTask<long> AppendAsync(
        YuoPersistenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command is YuoOutboxPersistenceCommand)
        {
            throw new ArgumentException("append the inner command, not the outbox wrapper.", nameof(command));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sequence = checked(++_nextSequence);
            var frame = EncodeFrame(sequence, command);
            await using var stream = new FileStream(
                _logPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);

            await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return sequence;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async IAsyncEnumerable<YuoPersistenceOutboxEntry> ReplayAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<YuoPersistenceOutboxEntry> entries;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            entries = ReadAllValidEntries(_logPath)
                .Where(entry => entry.Sequence > _acknowledgedThrough)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    public async ValueTask AcknowledgeThroughAsync(
        long sequence,
        CancellationToken cancellationToken = default)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "ack sequence must be positive.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (sequence <= _acknowledgedThrough)
            {
                return;
            }

            var tempPath = $"{_ackPath}.tmp";
            await File.WriteAllTextAsync(tempPath, sequence.ToString(), cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _ackPath, overwrite: true);
            _acknowledgedThrough = sequence;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask CompactAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var pending = ReadAllValidEntries(_logPath)
                .Where(entry => entry.Sequence > _acknowledgedThrough)
                .ToArray();
            var tempPath = $"{_logPath}.tmp";

            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                foreach (var entry in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var frame = EncodeFrame(entry.Sequence, entry.Command);
                    await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _logPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static byte[] EncodeFrame(long sequence, YuoPersistenceCommand command)
    {
        using var bodyStream = new MemoryStream();
        using (var writer = new BinaryWriter(bodyStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(sequence);

            switch (command)
            {
                case SaveSnapshotCommand save:
                    writer.Write(SnapshotCommand);
                    writer.Write(save.Snapshot.Kind);
                    writer.Write(save.Snapshot.Id);
                    writer.Write(save.Snapshot.UpdatedAt.Ticks);
                    writer.Write((short)save.Snapshot.UpdatedAt.Offset.TotalMinutes);
                    WritePayload(writer, save.Snapshot.Payload);
                    break;
                case AppendEventCommand append:
                    writer.Write(EventCommand);
                    writer.Write(append.Event.Stream);
                    writer.Write(append.Event.EventType);
                    writer.Write(append.Event.CreatedAt.Ticks);
                    writer.Write((short)append.Event.CreatedAt.Offset.TotalMinutes);
                    WritePayload(writer, append.Event.Payload);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported outbox command: {command.GetType().FullName}.");
            }
        }

        var body = bodyStream.ToArray();
        var frame = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, sizeof(int)), body.Length);
        body.CopyTo(frame.AsMemory(sizeof(int)));
        return frame;
    }

    private static IReadOnlyList<YuoPersistenceOutboxEntry> ReadAllValidEntries(string logPath)
    {
        if (!File.Exists(logPath))
        {
            return [];
        }

        var entries = new List<YuoPersistenceOutboxEntry>();
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];

        while (stream.Read(lengthBuffer) == sizeof(int))
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (length <= 0 || length > MaxFrameBytes)
            {
                break;
            }

            var body = new byte[length];
            var read = stream.Read(body, 0, body.Length);
            if (read != body.Length)
            {
                break;
            }

            if (TryDecodeBody(body, out var entry))
            {
                entries.Add(entry);
            }
            else
            {
                break;
            }
        }

        return entries;
    }

    private static bool TryDecodeBody(byte[] body, out YuoPersistenceOutboxEntry entry)
    {
        entry = default!;

        try
        {
            using var bodyStream = new MemoryStream(body);
            using var reader = new BinaryReader(bodyStream, System.Text.Encoding.UTF8, leaveOpen: false);
            if (reader.ReadInt32() != Magic)
            {
                return false;
            }

            if (reader.ReadByte() != Version)
            {
                return false;
            }

            var sequence = reader.ReadInt64();
            var commandType = reader.ReadByte();
            YuoPersistenceCommand? command = commandType switch
            {
                SnapshotCommand => DecodeSnapshot(reader),
                EventCommand => DecodeEvent(reader),
                _ => null
            };

            if (sequence <= 0 || command is null || bodyStream.Position != bodyStream.Length)
            {
                return false;
            }

            entry = new YuoPersistenceOutboxEntry(sequence, command);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static SaveSnapshotCommand DecodeSnapshot(BinaryReader reader)
    {
        var kind = reader.ReadString();
        var id = reader.ReadString();
        var updatedAt = ReadDateTimeOffset(reader);
        var payload = ReadPayload(reader);
        return new SaveSnapshotCommand(new YuoSnapshotRecord(kind, id, payload, updatedAt));
    }

    private static AppendEventCommand DecodeEvent(BinaryReader reader)
    {
        var stream = reader.ReadString();
        var eventType = reader.ReadString();
        var createdAt = ReadDateTimeOffset(reader);
        var payload = ReadPayload(reader);
        return new AppendEventCommand(new YuoEventRecord(stream, eventType, payload, createdAt));
    }

    private static void WritePayload(BinaryWriter writer, ReadOnlyMemory<byte> payload)
    {
        writer.Write(payload.Length);
        writer.Write(payload.Span);
    }

    private static ReadOnlyMemory<byte> ReadPayload(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0 || length > MaxFrameBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "payload length is invalid.");
        }

        return reader.ReadBytes(length);
    }

    private static DateTimeOffset ReadDateTimeOffset(BinaryReader reader)
    {
        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt16();
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    private static long ReadAcknowledgedThrough(string ackPath)
    {
        if (!File.Exists(ackPath))
        {
            return 0;
        }

        return long.TryParse(File.ReadAllText(ackPath), out var sequence) && sequence > 0
            ? sequence
            : 0;
    }

    private static long ReadMaxSequence(string logPath)
    {
        return ReadAllValidEntries(logPath)
            .Select(entry => entry.Sequence)
            .DefaultIfEmpty(0)
            .Max();
    }
}
