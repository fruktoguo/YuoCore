namespace YuoCore.Persistence;

/// <summary>
/// 快照记录，表示某个业务对象在某个时间点的完整状态。
/// </summary>
public sealed record YuoSnapshotRecord(
    string Kind,
    string Id,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 快照存储接口，用于保存和加载全量快照。
/// </summary>
public interface IYuoSnapshotStore
{
    /// <summary>保存一条快照记录。</summary>
    ValueTask SaveAsync(YuoSnapshotRecord snapshot, CancellationToken cancellationToken = default);

    /// <summary>加载指定类型和 ID 的快照。返回 null 表示不存在。</summary>
    ValueTask<YuoSnapshotRecord?> LoadAsync(string kind, string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 事件记录，表示某个流上发生的一次事件。
/// </summary>
public sealed record YuoEventRecord(
    string Stream,
    string EventType,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset CreatedAt);

/// <summary>
/// 事件存储接口，用于追加事件日志。
/// </summary>
public interface IYuoEventStore
{
    /// <summary>追加一条事件记录。</summary>
    ValueTask AppendAsync(YuoEventRecord @event, CancellationToken cancellationToken = default);
}

/// <summary>
/// 死亡信件记录，表示持久化写入最终失败的消息。
/// </summary>
public sealed record YuoPersistenceDeadLetterRecord(
    YuoPersistenceCommand Command,
    string CommandType,
    string ExceptionType,
    string ExceptionMessage,
    DateTimeOffset FailedAt);

/// <summary>
/// 死亡信件存储接口。
/// </summary>
public interface IYuoPersistenceDeadLetterStore
{
    /// <summary>追加一条死亡信件记录。</summary>
    ValueTask AppendAsync(YuoPersistenceDeadLetterRecord record, CancellationToken cancellationToken = default);
}
