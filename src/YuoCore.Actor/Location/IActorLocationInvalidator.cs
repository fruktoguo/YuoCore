namespace YuoCore.Actor;

public interface IActorLocationInvalidator
{
    ValueTask InvalidateAsync(
        ActorId actorId,
        ActorAddress? staleAddress = null,
        CancellationToken cancellationToken = default);
}
