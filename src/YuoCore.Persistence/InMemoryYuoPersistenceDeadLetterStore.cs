namespace YuoCore.Persistence;

/// <summary>
/// 进程内 dead-letter 存储，适合单机、测试和宿主默认兜底。
/// 它只保存当前进程内存，不提供持久化保证；生产环境应替换为 PostgreSQL、Redis 或文件等可选实现。
/// </summary>
public sealed class InMemoryYuoPersistenceDeadLetterStore : IYuoPersistenceDeadLetterStore
{
    private readonly object _gate = new();
    private readonly List<YuoPersistenceDeadLetterRecord> _records = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _records.Count;
            }
        }
    }

    public ValueTask AppendAsync(
        YuoPersistenceDeadLetterRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            _records.Add(record);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 返回当前 dead-letter 的稳定快照，调用方可以安全枚举。
    /// 这里复制列表是有意的，避免外部枚举期间和后台写入竞争。
    /// </summary>
    public IReadOnlyList<YuoPersistenceDeadLetterRecord> GetRecords()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
        }
    }
}
