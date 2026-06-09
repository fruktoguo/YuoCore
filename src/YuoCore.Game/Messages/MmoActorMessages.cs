using YuoCore.Actor;

namespace YuoCore.Game;

public sealed record YuoEnterMapActorRequest(
    ActorId TargetActorId,
    Guid PlayerId,
    long MapId) : IActorRequest<YuoEnterMapActorResponse>;

public sealed record YuoEnterMapActorResponse(
    long UnitId,
    long MapId);

public sealed record YuoMoveUnitActorRequest(
    ActorId TargetActorId,
    long UnitId,
    float X,
    float Y,
    float Z,
    ulong InputSeq) : IActorRequest<YuoMoveUnitActorResponse>;

public sealed record YuoMoveUnitActorResponse(
    long UnitId,
    float X,
    float Y,
    float Z,
    ulong AcceptedInputSeq);
