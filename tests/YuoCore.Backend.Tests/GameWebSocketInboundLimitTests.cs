using System.Net.WebSockets;
using YuoCore.Backend.WebSockets;

namespace YuoCore.Backend.Tests;

public sealed class GameWebSocketInboundLimitTests
{
    [Fact]
    public async Task Receive_rejects_fragmented_message_over_inbound_limit()
    {
        var socket = new ScriptedWebSocket();
        socket.EnqueueBinary(new byte[600], endOfMessage: false);
        socket.EnqueueBinary(new byte[600], endOfMessage: true);

        var result = await GameWebSocketEndpoint.ReceiveAsync(
            socket,
            new byte[1024],
            maxMessageBytes: 1024,
            idleTimeoutMs: 0,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.CloseStatus);
        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    [Fact]
    public async Task Receive_accepts_fragmented_message_within_limit()
    {
        var socket = new ScriptedWebSocket();
        socket.EnqueueBinary(new byte[700], endOfMessage: false);
        socket.EnqueueBinary(new byte[300], endOfMessage: true);

        var result = await GameWebSocketEndpoint.ReceiveAsync(
            socket,
            new byte[1024],
            maxMessageBytes: 1024,
            idleTimeoutMs: 0,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1000, result.Length);
        Assert.Null(socket.CloseStatus);
    }

    [Fact]
    public async Task Receive_closes_connection_after_idle_timeout()
    {
        var socket = new ScriptedWebSocket { WaitForFramesWhenEmpty = true };

        var result = await GameWebSocketEndpoint.ReceiveAsync(
            socket,
            new byte[1024],
            maxMessageBytes: 1024,
            idleTimeoutMs: 50,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    [Fact]
    public async Task Receive_resets_idle_timer_between_fragments()
    {
        var socket = new ScriptedWebSocket();
        socket.EnqueueBinary(new byte[10], endOfMessage: false);
        socket.EnqueueBinary(new byte[10], endOfMessage: true);

        var result = await GameWebSocketEndpoint.ReceiveAsync(
            socket,
            new byte[1024],
            maxMessageBytes: 1024,
            idleTimeoutMs: 60_000,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(20, result.Length);
    }

    private sealed class ScriptedWebSocket : WebSocket
    {
        private readonly Queue<(byte[] Payload, bool EndOfMessage)> _frames = new();

        public bool WaitForFramesWhenEmpty { get; set; }

        public void EnqueueBinary(byte[] payload, bool endOfMessage)
        {
            _frames.Enqueue((payload, endOfMessage));
        }

        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string? CloseStatusDescription => _closeStatusDescription;

        public override WebSocketState State => _state;

        public override string? SubProtocol => "yuocore.v1";

        public override void Abort()
        {
            _state = WebSocketState.Aborted;
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            return CloseAsync(closeStatus, statusDescription, cancellationToken);
        }

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (_frames.Count == 0 && WaitForFramesWhenEmpty)
            {
                return Task.Delay(Timeout.Infinite, cancellationToken)
                    .ContinueWith(
                        _ => new WebSocketReceiveResult(0, WebSocketMessageType.Close, true),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnRanToCompletion,
                        TaskScheduler.Default);
            }

            if (_frames.Count == 0)
            {
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            var (payload, endOfMessage) = _frames.Dequeue();
            if (payload.Length > buffer.Count)
            {
                throw new InvalidOperationException("Test frame larger than receive buffer.");
            }

            payload.AsSpan().CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(payload.Length, WebSocketMessageType.Binary, endOfMessage));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
