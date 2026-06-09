namespace YuoCore.Backend.Infrastructure;

public sealed class StorageOptions
{
    public bool PostgresEnabled { get; init; }
    public bool RedisEnabled { get; init; }
    public bool DurableOutboxEnabled { get; init; }
    public string PostgresConnectionString { get; init; } = "Host=localhost;Port=5432;Database=yuocore;Username=yuocore;Password=yuocore";
    public string RedisConnectionString { get; init; } = "localhost:6379";
    public string OutboxDirectory { get; init; } = "data/outbox";
    public int PersistenceQueueCapacity { get; init; } = 65_536;
}
