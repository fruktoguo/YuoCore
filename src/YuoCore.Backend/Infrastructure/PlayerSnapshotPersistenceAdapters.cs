using System.Text.Json;
using Microsoft.Extensions.Logging;
using YuoCore.Game;
using YuoCore.Persistence;

namespace YuoCore.Backend.Infrastructure;

/// <summary>
/// Backend 示例的玩家快照与通用 Persistence snapshot record 的转换。
/// 这层留在 Backend 内，避免 YuoCore.Persistence 认识具体游戏 DTO。
/// </summary>
public static class PlayerSnapshotPersistenceCodec
{
    public const string PlayerSnapshotKind = "player";

    public static YuoSnapshotRecord ToRecord(YuoPlayerSnapshotDto snapshot)
    {
        return new YuoSnapshotRecord(
            PlayerSnapshotKind,
            snapshot.PlayerId.ToString("N"),
            JsonSerializer.SerializeToUtf8Bytes(snapshot),
            snapshot.UpdatedAt);
    }

    public static YuoPlayerSnapshotDto FromRecord(YuoSnapshotRecord record)
    {
        if (!string.Equals(record.Kind, PlayerSnapshotKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported snapshot kind: {record.Kind}.");
        }

        return JsonSerializer.Deserialize<YuoPlayerSnapshotDto>(record.Payload.Span)
               ?? throw new InvalidOperationException("Player snapshot payload is empty or invalid.");
    }
}

/// <summary>
/// 把通用 Persistence.Hosting 写回服务适配到 Backend 示例的玩家快照仓储。
/// </summary>
public sealed class PlayerSnapshotStoreAdapter(
    IPlayerSnapshotRepository repository,
    IPlayerSnapshotCache? cache = null,
    ILogger<PlayerSnapshotStoreAdapter>? logger = null) : IYuoSnapshotStore
{
    public async ValueTask SaveAsync(YuoSnapshotRecord snapshot, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(snapshot.Kind, PlayerSnapshotPersistenceCodec.PlayerSnapshotKind, StringComparison.Ordinal))
        {
            return;
        }

        var playerSnapshot = PlayerSnapshotPersistenceCodec.FromRecord(snapshot);
        await repository.SaveAsync(playerSnapshot, cancellationToken).ConfigureAwait(false);

        if (cache is null)
        {
            return;
        }

        try
        {
            // PostgreSQL 是 durable truth；Redis 只是重连/读取加速缓存。
            // 后台 replay 成功后尽量刷新缓存，但缓存失败不能阻止 outbox ack，否则 Redis 抖动会让已落库命令反复重放。
            await cache.SetAsync(playerSnapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Backend 示例把缓存视为 best-effort；上面的 repository 写入才是 durable commit。
            logger?.LogWarning(
                exception,
                "Failed to refresh player snapshot cache after durable persistence save for player {PlayerId}",
                playerSnapshot.PlayerId);
        }
    }

    public async ValueTask<YuoSnapshotRecord?> LoadAsync(
        string kind,
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(kind, PlayerSnapshotPersistenceCodec.PlayerSnapshotKind, StringComparison.Ordinal) ||
            !Guid.TryParse(id, out var playerId))
        {
            return null;
        }

        var snapshot = await repository.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        return snapshot is null ? null : PlayerSnapshotPersistenceCodec.ToRecord(snapshot);
    }
}

public sealed class NoopYuoEventStore : IYuoEventStore
{
    public ValueTask AppendAsync(YuoEventRecord @event, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 启动时把文件 outbox 中未 ack 的命令重放进内存队列。
/// 必须在 YuoPersistenceBackgroundService 前注册，确保后台写回启动后能 drain replay 出来的命令。
/// </summary>
public sealed class YuoBackendPersistenceReplayService(
    YuoDurablePersistenceQueue queue,
    ILogger<YuoBackendPersistenceReplayService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var replayed = await queue.ReplayPendingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (replayed > 0)
        {
            logger.LogInformation("Replayed {Count} pending persistence outbox command(s)", replayed);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
