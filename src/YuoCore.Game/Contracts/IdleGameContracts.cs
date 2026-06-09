namespace YuoCore.Game;

public sealed record YuoCreatePlayerRequest(string? Name);

public sealed record YuoPlayerSnapshotDto(
    Guid PlayerId,
    string Name,
    long Gold,
    long Exp,
    int Level,
    long Power,
    long Clicks,
    DateTimeOffset UpdatedAt,
    long MapId = 0,
    long UnitId = 0,
    float X = 0,
    float Y = 0,
    float Z = 0,
    bool Online = false,
    ulong LastInputSeq = 0);

public sealed record YuoPlayerWorldStateDto(
    Guid PlayerId,
    string Name,
    long MapId,
    long UnitId,
    float X,
    float Y,
    float Z,
    bool Online,
    ulong LastInputSeq,
    DateTimeOffset UpdatedAt);

public sealed record YuoWorldEventDto(
    string Kind,
    YuoPlayerWorldStateDto Player,
    DateTimeOffset CreatedAt);

public sealed record YuoWorldSnapshotDto(
    long MapId,
    long Tick,
    IReadOnlyList<YuoPlayerWorldStateDto> Players,
    IReadOnlyList<YuoWorldEventDto> Events);

public sealed record YuoAoiStreamSnapshotDto(
    string StreamId,
    long MapId,
    long Tick,
    IReadOnlyList<YuoPlayerWorldStateDto> Players);

public readonly record struct YuoAoiDirtyPlayerDto(
    Guid PlayerId,
    long PreviousMapId,
    long CurrentMapId,
    float PreviousX,
    float PreviousY,
    float PreviousZ,
    float CurrentX,
    float CurrentY,
    float CurrentZ,
    DateTimeOffset CreatedAt);

public sealed record YuoGameStatsDto(
    int Players,
    long TickCount,
    long ProcessedCommands,
    double TickRate);

public abstract record YuoGameCommand;

public sealed record YuoClickCommand(Guid PlayerId, long Amount) : YuoGameCommand;

public sealed record YuoPersistPlayerCommand(Guid PlayerId) : YuoGameCommand;

public sealed record YuoEnterMapCommand(Guid PlayerId, long MapId) : YuoGameCommand;

public sealed record YuoMoveCommand(Guid PlayerId, float X, float Y, float Z, ulong InputSeq) : YuoGameCommand;

public sealed record YuoDisconnectCommand(Guid PlayerId, string SessionId) : YuoGameCommand;
