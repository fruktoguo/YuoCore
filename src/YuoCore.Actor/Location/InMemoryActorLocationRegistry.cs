using System.Collections.Concurrent;

namespace YuoCore.Actor;

/// <summary>
/// 基于内存的 Actor 位置注册表实现。
/// 使用 ConcurrentDictionary 存储 ActorId -> ActorAddress 映射，适用于单进程场景。
/// </summary>
public sealed class InMemoryActorLocationRegistry : IActorLocationRegistry
{
    private readonly ConcurrentDictionary<ActorId, ActorAddress> _locations = new();

    /// <inheritdoc />
    public ValueTask RegisterAsync(
        ActorId actorId,
        ActorAddress address,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _locations[actorId] = address;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<ActorAddress?> ResolveAsync(
        ActorId actorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            _locations.TryGetValue(actorId, out var address)
                ? address
                : (ActorAddress?)null);
    }

    /// <inheritdoc />
    public ValueTask UnregisterAsync(ActorId actorId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _locations.TryRemove(actorId, out _);
        return ValueTask.CompletedTask;
    }
}
