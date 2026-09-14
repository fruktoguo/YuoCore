using System.Net.WebSockets;
using System.Buffers;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using YuoCore.Backend.Game;
using YuoCore.Backend.Infrastructure;
using YuoCore.Gateway;
using YuoCore.Protocol;

namespace YuoCore.Backend.WebSockets;

public static class GameWebSocketEndpoint
{
    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var dispatcher = context.RequestServices.GetRequiredService<YuoGatewayDispatchPipeline>();
        var pushDispatcher = context.RequestServices.GetRequiredService<GameStateSyncPushDispatcher>();
        var aoiPushCoordinator = context.RequestServices.GetRequiredService<GameAoiStreamPushCoordinator>();
        var pushOptions = context.RequestServices.GetRequiredService<IOptions<GameWebSocketPushOptions>>().Value;
        var runtime = context.RequestServices.GetRequiredService<GameRuntime>();
        var persistence = context.RequestServices.GetRequiredService<SnapshotPersistenceService>();
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("YuoCore.Backend.WebSockets.GameWebSocketEndpoint");
        var sessions = context.RequestServices.GetRequiredService<YuoGatewaySessionManager>();
        using var socket = await context.WebSockets.AcceptWebSocketAsync("yuocore.v1");
        var session = sessions.Create();
        var connection = new GameWebSocketClientConnection(socket);
        var buffer = new byte[64 * 1024];
        using var pushCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var pushPump = new GameWebSocketPushPump(pushOptions, logger);
        Func<YuoGatewaySession, List<YuoGatewayOutboundMessage>, int> drainPushes = pushOptions.StateSyncMode switch
        {
            GameWebSocketStateSyncPushMode.AoiStream => aoiPushCoordinator.DrainPushes,
            _ => (currentSession, destination) => DrainPushesInto(pushDispatcher, currentSession, destination)
        };
        var pushTask = pushPump.RunAsync(
            connection,
            session,
            drainPushes,
            () => socket.State == WebSocketState.Open,
            (reason, cancellationToken) => CloseSlowClientAsync(socket, reason, cancellationToken),
            pushCts.Token);

        try
        {
            while (!context.RequestAborted.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var message = await ReceiveAsync(
                    socket,
                    buffer,
                    pushOptions.MaxInboundMessageBytes,
                    pushOptions.IdleTimeoutMs,
                    context.RequestAborted);
                if (message is null) break;

                var decoded = DecodeClientMessage(message);
                connection.UseEnvelopeV1 = decoded.UsesEnvelopeV1;
                var outbound = await dispatcher.DispatchAsync(session, decoded.Inbound, context.RequestAborted);
                await connection.SendAsync(outbound, context.RequestAborted);
            }
        }
        finally
        {
            if (session.PlayerId is { } playerId)
            {
                await PersistDisconnectAsync(runtime, persistence, logger, playerId, session.SessionId.ToString("N"));
            }

            sessions.Remove(session.SessionId);
            pushDispatcher.ClearSession(session.SessionId);
            aoiPushCoordinator.ClearSession(session.SessionId);
            await pushCts.CancelAsync();
            await AwaitPushPumpStopAsync(pushTask);
        }
    }

    internal static DecodedGameWebSocketMessage DecodeClientMessage(byte[] message)
    {
        if (TryReadEnvelopeV1(message, out var envelope))
        {
            var inbound = new YuoGatewayInboundMessage(
                envelope.Version,
                checked((int)envelope.Opcode),
                envelope.RequestId,
                envelope.ClientSeq,
                envelope.Payload.ToByteArray());
            return new DecodedGameWebSocketMessage(inbound, true);
        }

        var requestId = TryReadLegacyRequestId(message, out var parsedRequestId) ? parsedRequestId : 0UL;
        return new DecodedGameWebSocketMessage(
            new YuoGatewayInboundMessage(1, 0, requestId, 0, message),
            false);
    }

    public static byte[] EncodeServerMessage(YuoGatewayOutboundMessage message, bool useEnvelopeV1)
    {
        return useEnvelopeV1
            ? EncodeServerEnvelopeV1(message)
            : ToLegacyServerEnvelope(message).ToByteArray();
    }

    public static GameWebSocketEncodedMessage RentServerMessage(YuoGatewayOutboundMessage message, bool useEnvelopeV1)
    {
        if (useEnvelopeV1 && message.Error is null)
        {
            return RentServerEnvelopeV1(message);
        }

        return GameWebSocketEncodedMessage.FromOwned(EncodeServerMessage(message, useEnvelopeV1));
    }

    private static byte[] EncodeServerEnvelopeV1(YuoGatewayOutboundMessage message)
    {
        if (message.Error is not null)
        {
            return ToServerEnvelopeV1(message).ToByteArray();
        }

        var size = ComputeServerEnvelopeV1Size(message);
        var bytes = new byte[size];
        WriteServerEnvelopeV1(message, bytes);
        return bytes;
    }

    private static GameWebSocketEncodedMessage RentServerEnvelopeV1(YuoGatewayOutboundMessage message)
    {
        var size = ComputeServerEnvelopeV1Size(message);
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        WriteServerEnvelopeV1(message, buffer.AsSpan(0, size));
        return GameWebSocketEncodedMessage.FromPooled(buffer, size);
    }

    private static int ComputeServerEnvelopeV1Size(YuoGatewayOutboundMessage message)
    {
        var opcode = checked((uint)message.OpCode);
        var payloadLength = message.Payload.Length;
        var size = 0;
        if (message.Version != 0)
        {
            size += 1 + ComputeRawVarint32Size(message.Version);
        }

        if (opcode != 0)
        {
            size += 1 + ComputeRawVarint32Size(opcode);
        }

        if (message.RequestId != 0)
        {
            size += 1 + ComputeRawVarint64Size(message.RequestId);
        }

        if (message.ServerSeq != 0)
        {
            size += 1 + ComputeRawVarint64Size(message.ServerSeq);
        }

        if (payloadLength > 0)
        {
            size += 1 + ComputeRawVarint32Size(checked((uint)payloadLength)) + payloadLength;
        }

        return size;
    }

    private static void WriteServerEnvelopeV1(YuoGatewayOutboundMessage message, Span<byte> bytes)
    {
        var opcode = checked((uint)message.OpCode);
        var payload = message.Payload.Span;
        var offset = 0;
        if (message.Version != 0)
        {
            bytes[offset++] = 0x08;
            WriteRawVarint32(bytes, ref offset, message.Version);
        }

        if (opcode != 0)
        {
            bytes[offset++] = 0x10;
            WriteRawVarint32(bytes, ref offset, opcode);
        }

        if (message.RequestId != 0)
        {
            bytes[offset++] = 0x18;
            WriteRawVarint64(bytes, ref offset, message.RequestId);
        }

        if (message.ServerSeq != 0)
        {
            bytes[offset++] = 0x20;
            WriteRawVarint64(bytes, ref offset, message.ServerSeq);
        }

        if (payload.Length > 0)
        {
            bytes[offset++] = 0x32;
            WriteRawVarint32(bytes, ref offset, checked((uint)payload.Length));
            payload.CopyTo(bytes[offset..]);
            offset += payload.Length;
        }
    }

    private static int DrainPushesInto(
        GameStateSyncPushDispatcher pushDispatcher,
        YuoGatewaySession session,
        List<YuoGatewayOutboundMessage> destination)
    {
        var startCount = destination.Count;
        foreach (var push in pushDispatcher.DrainPushes(session))
        {
            destination.Add(push);
        }

        return destination.Count - startCount;
    }

    private static YuoServerEnvelopeV1 ToServerEnvelopeV1(YuoGatewayOutboundMessage message)
    {
        var envelope = new YuoServerEnvelopeV1
        {
            Version = message.Version,
            Opcode = checked((uint)(message.Error is null ? message.OpCode : YuoCoreProtocolOpCodes.ErrorResponse)),
            RequestId = message.RequestId,
            ServerSeq = message.ServerSeq,
            Payload = ByteString.CopyFrom(message.Payload.Span)
        };

        if (message.Error is not null)
        {
            envelope.Error = new ErrorResponse { Code = message.Error.Code, Message = message.Error.Message };
        }

        return envelope;
    }

    private static int ComputeRawVarint32Size(uint value)
    {
        if ((value & (0xffffffffU << 7)) == 0) return 1;
        if ((value & (0xffffffffU << 14)) == 0) return 2;
        if ((value & (0xffffffffU << 21)) == 0) return 3;
        if ((value & (0xffffffffU << 28)) == 0) return 4;
        return 5;
    }

    private static int ComputeRawVarint64Size(ulong value)
    {
        var size = 1;
        while (value >= 0x80)
        {
            size++;
            value >>= 7;
        }

        return size;
    }

    private static void WriteRawVarint32(Span<byte> bytes, ref int offset, uint value)
    {
        while (value > 0x7f)
        {
            bytes[offset++] = (byte)((value & 0x7f) | 0x80);
            value >>= 7;
        }

        bytes[offset++] = (byte)value;
    }

    private static void WriteRawVarint64(Span<byte> bytes, ref int offset, ulong value)
    {
        while (value > 0x7f)
        {
            bytes[offset++] = (byte)((value & 0x7f) | 0x80);
            value >>= 7;
        }

        bytes[offset++] = (byte)value;
    }

    private static ServerEnvelope ToLegacyServerEnvelope(YuoGatewayOutboundMessage message)
    {
        if (message.Error is not null)
        {
            return Error(message.RequestId, message.Error.Code, message.Error.Message);
        }

        return ServerEnvelope.Parser.ParseFrom(message.Payload.Span);
    }

    private static bool TryReadEnvelopeV1(byte[] message, out YuoClientEnvelopeV1 envelope)
    {
        try
        {
            envelope = YuoClientEnvelopeV1.Parser.ParseFrom(message);
            return envelope.Version != 0 && envelope.Opcode != 0;
        }
        catch
        {
            envelope = new YuoClientEnvelopeV1();
            return false;
        }
    }

    private static bool TryReadLegacyRequestId(byte[] message, out ulong requestId)
    {
        try
        {
            requestId = ClientEnvelope.Parser.ParseFrom(message).RequestId;
            return true;
        }
        catch
        {
            requestId = 0;
            return false;
        }
    }

    private static ServerEnvelope Error(ulong requestId, string code, string message)
    {
        return new ServerEnvelope { RequestId = requestId, Error = new ErrorResponse { Code = code, Message = message } };
    }

    internal static async Task<byte[]?> ReceiveAsync(
        WebSocket socket,
        byte[] buffer,
        int maxMessageBytes,
        int idleTimeoutMs,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            if (idleTimeoutMs > 0)
            {
                // 每轮帧接收都重置空闲计时；超时未收到任何字节即按违规关闭连接。
                idleCts.CancelAfter(TimeSpan.FromMilliseconds(idleTimeoutMs));
            }

            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, idleCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation,
                        "Idle connection timed out.",
                        CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    socket.Abort();
                }

                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Binary) throw new InvalidOperationException("Only binary protobuf messages are supported.");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > maxMessageBytes)
            {
                // 分片累计无上限会被慢速客户端当成内存放大器：超限按协议违规关闭。
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        "Inbound message exceeded the configured limit.",
                        CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    socket.Abort();
                }

                return null;
            }

            if (result.EndOfMessage) return stream.ToArray();
        }
    }

    private static async Task PersistDisconnectAsync(
        GameRuntime runtime,
        SnapshotPersistenceService persistence,
        ILogger logger,
        Guid playerId,
        string sessionId)
    {
        try
        {
            if (runtime.TryDisconnect(playerId, sessionId) &&
                runtime.TryGetSnapshot(playerId, out var snapshot))
            {
                // WebSocket 关闭时 RequestAborted 通常已经触发；保存离线快照不能复用它。
                await persistence.SaveAsync(snapshot, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to persist player snapshot on websocket disconnect: {PlayerId}", playerId);
        }
    }

    private static async ValueTask CloseSlowClientAsync(
        WebSocket socket,
        string reason,
        CancellationToken cancellationToken)
    {
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, cancellationToken);
            return;
        }

        if (socket.State is not WebSocketState.Closed and not WebSocketState.Aborted)
        {
            socket.Abort();
        }
    }

    private static async Task AwaitPushPumpStopAsync(Task pushTask)
    {
        try
        {
            await pushTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private sealed class GameWebSocketClientConnection(WebSocket socket) : IYuoClientConnection
    {
        private readonly SemaphoreSlim _sendGate = new(1, 1);

        public Guid ConnectionId { get; } = Guid.NewGuid();

        public bool UseEnvelopeV1 { get; set; } = true;

        public async ValueTask SendAsync(YuoGatewayOutboundMessage message, CancellationToken cancellationToken = default)
        {
            using var encoded = RentServerMessage(message, UseEnvelopeV1);
            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                await socket.SendAsync(encoded.Memory, WebSocketMessageType.Binary, true, cancellationToken);
            }
            finally
            {
                _sendGate.Release();
            }
        }
    }
}

internal sealed record DecodedGameWebSocketMessage(
    YuoGatewayInboundMessage Inbound,
    bool UsesEnvelopeV1);

public readonly struct GameWebSocketEncodedMessage : IDisposable
{
    private readonly byte[] _buffer;
    private readonly bool _pooled;

    private GameWebSocketEncodedMessage(byte[] buffer, int length, bool pooled)
    {
        _buffer = buffer;
        Length = length;
        _pooled = pooled;
    }

    public int Length { get; }

    public ReadOnlyMemory<byte> Memory => _buffer.AsMemory(0, Length);

    public static GameWebSocketEncodedMessage FromOwned(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new GameWebSocketEncodedMessage(bytes, bytes.Length, pooled: false);
    }

    public static GameWebSocketEncodedMessage FromPooled(byte[] buffer, int length)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)length > (uint)buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Encoded length exceeds pooled buffer length.");
        }

        return new GameWebSocketEncodedMessage(buffer, length, pooled: true);
    }

    public void Dispose()
    {
        if (_pooled)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}
