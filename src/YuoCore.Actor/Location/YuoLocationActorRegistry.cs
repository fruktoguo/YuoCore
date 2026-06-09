using YuoCore.Location;

namespace YuoCore.Actor;

/// <summary>
/// Actor 到独立 Location 包的兼容适配器。
/// 旧代码仍然按 ActorId 注册/查询；底层 Location 使用稳定的 Type+Key 格式，后续可以替换成分布式实现。
/// </summary>
public sealed class YuoLocationActorRegistry(IYuoLocationRegistry locations) :
    IActorLocationRegistry,
    IActorLocationInvalidator
{
    private readonly IYuoLocationRegistry _locations = locations ?? throw new ArgumentNullException(nameof(locations));

    public async ValueTask RegisterAsync(ActorId actorId, ActorAddress address, CancellationToken cancellationToken = default)
    {
        var result = await _locations.RegisterAsync(
            ToKey(actorId),
            ToYuoAddress(address),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            throw new ActorException(ActorError.Create(
                409,
                ActorErrorCodes.Conflict,
                result.Error!.Message,
                result.Error.Detail));
        }
    }

    public async ValueTask<ActorAddress?> ResolveAsync(ActorId actorId, CancellationToken cancellationToken = default)
    {
        var entry = await _locations.ResolveAsync(ToKey(actorId), cancellationToken).ConfigureAwait(false);
        return entry is null ? null : ToActorAddress(entry.Address);
    }

    public async ValueTask UnregisterAsync(ActorId actorId, CancellationToken cancellationToken = default)
    {
        await _locations.RemoveAsync(ToKey(actorId), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public ValueTask InvalidateAsync(
        ActorId actorId,
        ActorAddress? staleAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (_locations is IYuoLocationCacheInvalidator invalidator)
        {
            invalidator.Invalidate(
                ToKey(actorId),
                staleAddress is null ? null : ToYuoAddress(staleAddress.Value));
        }

        return ValueTask.CompletedTask;
    }

    private static YuoLocationKey ToKey(ActorId actorId)
    {
        return new YuoLocationKey("actor", actorId.Value.ToString());
    }

    private static YuoActorAddress ToYuoAddress(ActorAddress address)
    {
        return new YuoActorAddress(address.NodeId, "default", address.FiberId, address.ActorId.Value, address.Endpoint);
    }

    private static ActorAddress ToActorAddress(YuoActorAddress address)
    {
        return new ActorAddress(address.NodeId, checked((int)address.FiberId), new ActorId(address.ActorId), address.Endpoint);
    }
}
