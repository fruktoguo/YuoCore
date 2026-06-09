using YuoCore.Protocol;

namespace YuoCore.Client;

/// <summary>
/// Web/Unity 共用的最小 MMO 协议客户端门面。
/// 它只组合 V1 envelope、Protobuf DTO 和 IYuoClientTransport，不引用服务端 Actor、ECS、Gateway 或 Backend。
/// </summary>
public sealed class YuoMmoClient(
    IYuoClientTransport transport,
    YuoClientSession? session = null)
{
    private readonly IYuoClientTransport _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    private readonly YuoClientSession _session = session ?? new YuoClientSession();

    public async ValueTask<Welcome> LoginAsync(
        string? playerId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.CallAsync(
            _transport,
            new HelloRequest { PlayerId = playerId ?? string.Empty },
            Welcome.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<PlayerSnapshot> ClickAsync(
        string playerId,
        long amount = 1,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new ClickRequest
            {
                PlayerId = playerId,
                Amount = amount
            },
            PlayerSnapshot.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<PlayerSnapshot> GetSnapshotAsync(
        string playerId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new SnapshotRequest { PlayerId = playerId },
            PlayerSnapshot.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<WorldSnapshot> EnterMapAsync(
        string playerId,
        long mapId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new EnterMapRequest
            {
                PlayerId = playerId,
                MapId = mapId
            },
            WorldSnapshot.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<WorldSnapshot> MoveAsync(
        string playerId,
        ulong inputSeq,
        float x,
        float y,
        float z,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new MoveRequest
            {
                PlayerId = playerId,
                InputSeq = inputSeq,
                X = x,
                Y = y,
                Z = z
            },
            WorldSnapshot.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<PlayerSnapshot> DisconnectAsync(
        string playerId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new DisconnectRequest
            {
                PlayerId = playerId,
                SessionId = sessionId ?? string.Empty
            },
            PlayerSnapshot.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<StateSnapshotMessage> RequestStateSnapshotAsync(
        string playerId,
        long knownTick = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new StateSnapshotRequest
            {
                PlayerId = playerId,
                KnownTick = knownTick
            },
            StateSnapshotMessage.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<StateSyncRecoverResponse> RecoverStateSyncAsync(
        string playerId,
        long knownTick,
        string? streamId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new StateSyncRecoverRequest
            {
                PlayerId = playerId,
                KnownTick = knownTick,
                StreamId = streamId ?? string.Empty
            },
            StateSyncRecoverResponse.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    public async ValueTask<StateSyncAckResponse> AckStateSyncAsync(
        string playerId,
        long ackTick,
        string? streamId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfNullOrWhiteSpace(playerId, nameof(playerId));
        var response = await _session.CallAsync(
            _transport,
            new StateSyncAckRequest
            {
                PlayerId = playerId,
                AckTick = ackTick,
                StreamId = streamId ?? string.Empty
            },
            StateSyncAckResponse.Parser,
            cancellationToken).ConfigureAwait(false);
        return response.Payload;
    }

    private static void ThrowIfNullOrWhiteSpace(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", parameterName);
        }
    }
}
