using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Game;
using YuoCore.Protocol;

namespace YuoCore.Backend.WebSockets;

/// <summary>
/// 示例挂机游戏的客户端协议分发器。
/// WebSocket endpoint 只负责收发 bytes；具体 protobuf payload 怎么变成游戏命令，集中放在这里。
/// 后续迁移到正式 Gateway 模块时，可以保留这个形状，把实现替换成 Actor/Location 调用。
/// </summary>
public sealed class GameClientMessageDispatcher(
    GameRuntime runtime,
    GameCommandQueue queue,
    SnapshotPersistenceService? persistence = null,
    IOptions<GameOptions>? options = null)
{
    private readonly bool _useCommandQueueForRequests = options?.Value.GatewayRequestsUseCommandQueue ?? false;

    public ValueTask<ServerEnvelope> DispatchAsync(
        ClientEnvelope request,
        CancellationToken cancellationToken = default)
    {
        return DispatchAsync(request, "legacy", cancellationToken);
    }

    public async ValueTask<ServerEnvelope> DispatchAsync(
        ClientEnvelope request,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (request.PayloadCase)
        {
            case ClientEnvelope.PayloadOneofCase.Hello:
            {
                var id = ParseOrCreate(request.Hello.PlayerId);
                if (id is { } existingId)
                {
                    await RestorePlayerIfNeededAsync(existingId, cancellationToken).ConfigureAwait(false);
                }

                var snapshot = runtime.LoginPlayer(request.Hello.PlayerId, id, sessionId);
                return new ServerEnvelope
                {
                    RequestId = request.RequestId,
                    Welcome = new Welcome { PlayerId = snapshot.PlayerId.ToString(), Message = "ok" }
                };
            }
            case ClientEnvelope.PayloadOneofCase.Click:
            {
                if (!Guid.TryParse(request.Click.PlayerId, out var id))
                    return Error(request.RequestId, "invalid_player", "player_id must be a guid");

                if (_useCommandQueueForRequests)
                {
                    var result = await queue.ExecuteAsync(
                        new YuoClickCommand(id, request.Click.Amount),
                        cancellationToken).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        return Error(request.RequestId, ResultErrorCode(result), ResultErrorMessage(result, "player not found"));
                    }

                    return result.Snapshot is not null
                        ? Snapshot(request.RequestId, result.Snapshot)
                        : Snapshot(request.RequestId, id);
                }

                await queue.EnqueueAsync(new YuoClickCommand(id, request.Click.Amount), cancellationToken)
                    .ConfigureAwait(false);
                return Snapshot(request.RequestId, id);
            }
            case ClientEnvelope.PayloadOneofCase.Snapshot:
            {
                if (!Guid.TryParse(request.Snapshot.PlayerId, out var id))
                    return Error(request.RequestId, "invalid_player", "player_id must be a guid");
                return Snapshot(request.RequestId, id);
            }
            case ClientEnvelope.PayloadOneofCase.EnterMap:
            {
                if (!Guid.TryParse(request.EnterMap.PlayerId, out var id))
                    return Error(request.RequestId, "invalid_player", "player_id must be a guid");
                if (_useCommandQueueForRequests)
                {
                    var result = await queue.ExecuteAsync(
                        new YuoEnterMapCommand(id, request.EnterMap.MapId),
                        cancellationToken).ConfigureAwait(false);
                    return result.Success && result.World is not null
                        ? new ServerEnvelope { RequestId = request.RequestId, World = ToProto(result.World) }
                        : Error(request.RequestId, ResultErrorCode(result), ResultErrorMessage(result, "player not found"));
                }

                if (!runtime.TryEnterMap(id, request.EnterMap.MapId, out var world))
                    return Error(request.RequestId, "not_found", "player not found");
                return new ServerEnvelope { RequestId = request.RequestId, World = ToProto(world) };
            }
            case ClientEnvelope.PayloadOneofCase.Move:
            {
                if (!Guid.TryParse(request.Move.PlayerId, out var id))
                    return Error(request.RequestId, "invalid_player", "player_id must be a guid");
                if (_useCommandQueueForRequests)
                {
                    var result = await queue.ExecuteAsync(
                        new YuoMoveCommand(id, request.Move.X, request.Move.Y, request.Move.Z, request.Move.InputSeq),
                        cancellationToken).ConfigureAwait(false);
                    return result.Success && result.World is not null
                        ? new ServerEnvelope { RequestId = request.RequestId, World = ToProto(result.World) }
                        : Error(request.RequestId, ResultErrorCode(result), ResultErrorMessage(result, "player not found"));
                }

                if (!runtime.TryMove(id, request.Move.X, request.Move.Y, request.Move.Z, request.Move.InputSeq, out var world))
                    return Error(request.RequestId, "not_found", "player not found");
                return new ServerEnvelope { RequestId = request.RequestId, World = ToProto(world) };
            }
            case ClientEnvelope.PayloadOneofCase.Disconnect:
            {
                if (!Guid.TryParse(request.Disconnect.PlayerId, out var id))
                    return Error(request.RequestId, "invalid_player", "player_id must be a guid");
                if (_useCommandQueueForRequests)
                {
                    var result = await queue.ExecuteAsync(
                        new YuoDisconnectCommand(id, request.Disconnect.SessionId),
                        cancellationToken).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        return Error(request.RequestId, ResultErrorCode(result), ResultErrorMessage(result, "player not found or session mismatch"));
                    }

                    await PersistLiveSnapshotAsync(id, cancellationToken).ConfigureAwait(false);
                    return result.Snapshot is not null
                        ? Snapshot(request.RequestId, result.Snapshot)
                        : Snapshot(request.RequestId, id);
                }

                if (!runtime.TryDisconnect(id, request.Disconnect.SessionId))
                    return Error(request.RequestId, "not_found", "player not found or session mismatch");
                await PersistLiveSnapshotAsync(id, cancellationToken).ConfigureAwait(false);
                return Snapshot(request.RequestId, id);
            }
            case ClientEnvelope.PayloadOneofCase.Ping:
                return new ServerEnvelope
                {
                    RequestId = request.RequestId,
                    Pong = new PongResponse { UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                };
            default:
                return Error(request.RequestId, "unknown_payload", "missing payload");
        }
    }

    private ServerEnvelope Snapshot(ulong requestId, Guid id)
    {
        if (!runtime.TryGetSnapshot(id, out var snapshot))
            return Error(requestId, "not_found", "player not found");

        return Snapshot(requestId, snapshot);
    }

    private static ServerEnvelope Snapshot(ulong requestId, YuoPlayerSnapshotDto snapshot)
    {
        return new ServerEnvelope
        {
            RequestId = requestId,
            Snapshot = new PlayerSnapshot
            {
                PlayerId = snapshot.PlayerId.ToString(),
                Name = snapshot.Name,
                Gold = snapshot.Gold,
                Exp = snapshot.Exp,
                Level = snapshot.Level,
                Power = snapshot.Power,
                Clicks = snapshot.Clicks,
                UpdatedUnixMs = snapshot.UpdatedAt.ToUnixTimeMilliseconds(),
                MapId = snapshot.MapId,
                UnitId = snapshot.UnitId,
                X = snapshot.X,
                Y = snapshot.Y,
                Z = snapshot.Z,
                Online = snapshot.Online,
                LastInputSeq = snapshot.LastInputSeq
            }
        };
    }

    private async ValueTask RestorePlayerIfNeededAsync(Guid playerId, CancellationToken cancellationToken)
    {
        if (persistence is null || runtime.TryGetSnapshot(playerId, out _))
        {
            return;
        }

        var stored = await persistence.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            runtime.TryRestorePlayer(stored);
        }
    }

    private async ValueTask PersistLiveSnapshotAsync(Guid playerId, CancellationToken cancellationToken)
    {
        if (persistence is null || !runtime.TryGetSnapshot(playerId, out var snapshot))
        {
            return;
        }

        await persistence.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
    }

    private static WorldSnapshot ToProto(YuoWorldSnapshotDto snapshot)
    {
        var result = new WorldSnapshot
        {
            MapId = snapshot.MapId,
            Tick = snapshot.Tick
        };

        result.Players.AddRange(snapshot.Players.Select(ToProto));
        result.Events.AddRange(snapshot.Events.Select(ToProto));
        return result;
    }

    private static PlayerWorldState ToProto(YuoPlayerWorldStateDto state)
    {
        return new PlayerWorldState
        {
            PlayerId = state.PlayerId.ToString(),
            Name = state.Name,
            MapId = state.MapId,
            UnitId = state.UnitId,
            X = state.X,
            Y = state.Y,
            Z = state.Z,
            Online = state.Online,
            LastInputSeq = state.LastInputSeq,
            UpdatedUnixMs = state.UpdatedAt.ToUnixTimeMilliseconds()
        };
    }

    private static WorldEvent ToProto(YuoWorldEventDto item)
    {
        return new WorldEvent
        {
            Kind = item.Kind,
            Player = ToProto(item.Player),
            CreatedUnixMs = item.CreatedAt.ToUnixTimeMilliseconds()
        };
    }

    private static ServerEnvelope Error(ulong requestId, string code, string message) =>
        new() { RequestId = requestId, Error = new ErrorResponse { Code = code, Message = message } };

    private static string ResultErrorCode(YuoGameCommandResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorCode) ? "failed" : result.ErrorCode;

    private static string ResultErrorMessage(YuoGameCommandResult result, string fallback) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage) ? fallback : result.ErrorMessage;

    private static Guid? ParseOrCreate(string playerId) =>
        Guid.TryParse(playerId, out var id) ? id : null;
}
