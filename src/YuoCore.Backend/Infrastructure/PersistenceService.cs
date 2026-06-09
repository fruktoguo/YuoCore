using Microsoft.Extensions.Logging;
using YuoCore.Backend.Game;
using YuoCore.Game;
using YuoCore.Persistence;

namespace YuoCore.Backend.Infrastructure;

public sealed class SnapshotPersistenceService
{
    private readonly IPlayerSnapshotRepository _repository;
    private readonly IPlayerSnapshotCache _cache;
    private readonly YuoDurablePersistenceQueue? _durableQueue;
    private readonly ILogger<SnapshotPersistenceService>? _logger;

    public SnapshotPersistenceService(
        IPlayerSnapshotRepository repository,
        IPlayerSnapshotCache cache,
        YuoDurablePersistenceQueue? durableQueue = null,
        ILogger<SnapshotPersistenceService>? logger = null)
    {
        _repository = repository;
        _cache = cache;
        _durableQueue = durableQueue;
        _logger = logger;
    }

    public async Task SaveAsync(YuoPlayerSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        if (_durableQueue is not null)
        {
            await _durableQueue.EnqueueAsync(
                new SaveSnapshotCommand(PlayerSnapshotPersistenceCodec.ToRecord(snapshot)),
                cancellationToken);
            await TryRefreshCacheAsync(snapshot, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _repository.SaveAsync(snapshot, cancellationToken);
        await TryRefreshCacheAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<YuoPlayerSnapshotDto?> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        var cached = await TryReadCacheAsync(playerId, cancellationToken).ConfigureAwait(false);
        return cached ?? await _repository.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryRefreshCacheAsync(
        YuoPlayerSnapshotDto snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            // 持久化提交已经完成；Redis 只是读取加速层，不能反向阻止 durable truth。
            await _cache.SetAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                exception,
                "Failed to refresh player snapshot cache after snapshot save for player {PlayerId}",
                snapshot.PlayerId);
        }
    }

    private async Task<YuoPlayerSnapshotDto?> TryReadCacheAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _cache.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(
                exception,
                "Failed to read player snapshot cache for player {PlayerId}; falling back to repository",
                playerId);
            return null;
        }
    }
}
