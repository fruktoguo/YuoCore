namespace YuoCore.Persistence.Hosting;

/// <summary>
/// 将后台写回的最终失败命令转存到 dead letter store。
/// 这里只做失败出口到存储契约的桥接，真正可靠写入 PostgreSQL/Redis/文件由宿主实现 IYuoPersistenceDeadLetterStore。
/// </summary>
public sealed class YuoPersistenceDeadLetterFailureSink(
    IYuoPersistenceDeadLetterStore deadLetterStore) : IYuoPersistenceFailureSink
{
    private readonly IYuoPersistenceDeadLetterStore _deadLetterStore =
        deadLetterStore ?? throw new ArgumentNullException(nameof(deadLetterStore));

    public ValueTask OnFailureAsync(
        YuoPersistenceCommand command,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(exception);

        var record = new YuoPersistenceDeadLetterRecord(
            command,
            command.GetType().FullName ?? command.GetType().Name,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            DateTimeOffset.UtcNow);

        return _deadLetterStore.AppendAsync(record, cancellationToken);
    }
}
