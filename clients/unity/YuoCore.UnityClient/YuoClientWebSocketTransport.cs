using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using YuoCore.Client;

namespace YuoCore.Client.Unity
{
    /// <summary>
    /// Unity Editor / Standalone 可用的最小 WebSocket transport。
    /// 它只实现字节级 request/response，不暴露服务端 Actor、Fiber、Location 或 ECS 对象。
    /// </summary>
    public sealed class YuoClientWebSocketTransport : IYuoClientPushTransport, IDisposable
    {
        private readonly ClientWebSocket _socket;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<ulong, TaskCompletionSource<ReadOnlyMemory<byte>>> _pending = new ConcurrentDictionary<ulong, TaskCompletionSource<ReadOnlyMemory<byte>>>();
        private readonly CancellationTokenSource _receiveCts = new CancellationTokenSource();
        private readonly Task _receiveLoop;
        private int _closing;
        private Exception? _terminalException;
        private bool _disposed;

        private YuoClientWebSocketTransport(ClientWebSocket socket)
        {
            _socket = socket;
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
        }

        /// <summary>
        /// 收到 requestId=0 的服务端主动推送时触发。
        /// 这里暴露的是客户端协议消息，不暴露 Gateway session、Actor 或服务端 Entity。
        /// </summary>
        public event Action<YuoClientServerMessage>? PushReceived;

        public static async ValueTask<YuoClientWebSocketTransport> ConnectAsync(
            Uri uri,
            string subProtocol = "yuocore.v1",
            CancellationToken cancellationToken = default)
        {
            if (uri is null) throw new ArgumentNullException(nameof(uri));

            var socket = new ClientWebSocket();
            if (!string.IsNullOrWhiteSpace(subProtocol))
            {
                socket.Options.AddSubProtocol(subProtocol);
            }

            try
            {
                await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                return new YuoClientWebSocketTransport(socket);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        public async ValueTask<ReadOnlyMemory<byte>> SendAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_socket.State != WebSocketState.Open)
            {
                throw new WebSocketException($"WebSocket is not open. State={_socket.State}.");
            }

            var requestId = YuoClientServerMessage.ReadClientRequestId(request);
            if (requestId == 0)
            {
                throw new InvalidOperationException("Client request id 0 is reserved for server push messages.");
            }

            var completion = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_socket.State != WebSocketState.Open)
                {
                    throw new WebSocketException($"WebSocket is not open. State={_socket.State}.");
                }

                if (!_pending.TryAdd(requestId, completion))
                {
                    throw new InvalidOperationException($"Request {requestId} is already pending.");
                }

                try
                {
                    // ClientWebSocket 在旧 Unity/.NET Standard API 下主要使用 ArraySegment<byte>。
                    var requestBytes = request.ToArray();
                    await _socket.SendAsync(
                            new ArraySegment<byte>(requestBytes),
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    _pending.TryRemove(requestId, out _);
                    throw;
                }
            }
            finally
            {
                _sendLock.Release();
            }

            using (cancellationToken.Register(() =>
                   {
                       if (_pending.TryRemove(requestId, out var pending))
                       {
                           pending.TrySetCanceled(cancellationToken);
                       }
                   }))
            {
                return await completion.Task.ConfigureAwait(false);
            }
        }

        public async ValueTask CloseAsync(
            WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure,
            string statusDescription = "client closing",
            CancellationToken cancellationToken = default)
        {
            if (_disposed || Interlocked.Exchange(ref _closing, 1) != 0)
            {
                return;
            }

            var closeException = new OperationCanceledException("WebSocket transport is closing.");
            CompletePending(closeException);
            try
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _receiveCts.Cancel();
                CompletePending(closeException);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Interlocked.Exchange(ref _closing, 1);
            _receiveCts.Cancel();
            CompletePending(new ObjectDisposedException(nameof(YuoClientWebSocketTransport)));
            _sendLock.Dispose();
            _receiveCts.Dispose();
            _socket.Dispose();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            Exception? terminalException = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    var bytes = await ReceiveBinaryMessageAsync(_socket, cancellationToken).ConfigureAwait(false);
                    var message = YuoClientServerMessage.Decode(bytes);
                    if (message.IsPush)
                    {
                        DispatchPush(message);
                        continue;
                    }

                    if (_pending.TryRemove(message.RequestId, out var pending))
                    {
                        pending.TrySetResult(bytes);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposed)
            {
                terminalException = new OperationCanceledException("WebSocket receive loop was canceled.");
            }
            catch (Exception exception)
            {
                terminalException = exception;
            }
            finally
            {
                if (!_disposed)
                {
                    if (terminalException != null)
                    {
                        CompletePending(terminalException);
                    }
                    else if (_socket.State != WebSocketState.Open)
                    {
                        // 正常退出循环也要释放 pending；否则服务端主动关闭但没有异常时，请求方会一直等待。
                        CompletePending(new WebSocketException($"WebSocket receive loop stopped. State={_socket.State}."));
                    }
                }
            }
        }

        private void DispatchPush(YuoClientServerMessage message)
        {
            var handler = PushReceived;
            if (handler is null)
            {
                return;
            }

            try
            {
                handler(message);
            }
            catch
            {
                // 推送回调不应该杀掉底层接收循环；业务层需要自行记录回调异常。
            }
        }

        private void CompletePending(Exception exception)
        {
            Interlocked.CompareExchange(ref _terminalException, exception, null);
            RejectAllPending(exception);
        }

        private void RejectAllPending(Exception exception)
        {
            foreach (var item in _pending)
            {
                if (_pending.TryRemove(item.Key, out var pending))
                {
                    pending.TrySetException(exception);
                }
            }
        }

        private static async ValueTask<byte[]> ReceiveBinaryMessageAsync(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var stream = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("WebSocket closed before a response was received.");
                }

                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    throw new WebSocketException($"Expected binary message, got {result.MessageType}.");
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return stream.ToArray();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(YuoClientWebSocketTransport));
            }

            if (Volatile.Read(ref _closing) != 0)
            {
                throw new WebSocketException("WebSocket transport is closing or closed.");
            }

            if (_terminalException != null)
            {
                throw new WebSocketException($"WebSocket transport has stopped: {_terminalException.Message}");
            }

            if (_receiveLoop.IsFaulted)
            {
                throw new WebSocketException("WebSocket receive loop has stopped.");
            }
        }
    }
}
