using System.Text.Json;
using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Game;
using StackExchange.Redis;

namespace YuoCore.Backend.Infrastructure;

public interface IPlayerSnapshotCache
{
    Task SetAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default);
    Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default);
}

public sealed class NoopPlayerSnapshotCache : IPlayerSnapshotCache
{
    public Task SetAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default) => Task.FromResult<YuoPlayerSnapshotDto?>(null);
}

public sealed class RedisPlayerSnapshotCache : IPlayerSnapshotCache
{
    private readonly IConnectionMultiplexer _redis;

    public RedisPlayerSnapshotCache(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task SetAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(Key(snapshot.PlayerId), JsonSerializer.Serialize(snapshot), TimeSpan.FromMinutes(10));
    }

    public async Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync(Key(playerId));
        return json.HasValue ? JsonSerializer.Deserialize<YuoPlayerSnapshotDto>(json.ToString()) : null;
    }

    private static string Key(Guid id) => $"player:snapshot:{id:N}";
}
