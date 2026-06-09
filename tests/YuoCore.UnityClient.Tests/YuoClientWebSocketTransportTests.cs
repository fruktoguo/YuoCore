using System.Net.WebSockets;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using YuoCore.Client;
using YuoCore.Client.Unity;
using YuoCore.Protocol;

namespace YuoCore.UnityClient.Tests;

public sealed class YuoClientWebSocketTransportTests
{
    [Fact]
    public async Task Close_async_fails_pending_request_without_server_response()
    {
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = await WebSocketTestServer.StartAsync(async socket =>
        {
            await ReceiveBinaryMessageAsync(socket, timeout.Token);
            requestReceived.TrySetResult();
            await ReceiveCloseAndReplyAsync(socket, timeout.Token);
        }, timeout.Token);
        using var transport = await YuoClientWebSocketTransport.ConnectAsync(server.WebSocketUri, cancellationToken: timeout.Token);
        var request = new YuoClientSession().EncodeRequest(new HelloRequest()).Bytes;

        var pending = transport.SendAsync(request, CancellationToken.None).AsTask();
        await requestReceived.Task.WaitAsync(timeout.Token);
        await transport.CloseAsync(cancellationToken: timeout.Token);

        var exception = await WaitForFailureAsync(pending);
        Assert.True(exception is OperationCanceledException or WebSocketException);
    }

    [Fact]
    public async Task Server_close_fails_pending_request_without_client_timeout()
    {
        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = await WebSocketTestServer.StartAsync(async socket =>
        {
            await ReceiveBinaryMessageAsync(socket, timeout.Token);
            requestReceived.TrySetResult();
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "server closing", timeout.Token);
        }, timeout.Token);
        using var transport = await YuoClientWebSocketTransport.ConnectAsync(server.WebSocketUri, cancellationToken: timeout.Token);
        var request = new YuoClientSession().EncodeRequest(new HelloRequest()).Bytes;

        var pending = transport.SendAsync(request, CancellationToken.None).AsTask();
        await requestReceived.Task.WaitAsync(timeout.Token);

        var exception = await WaitForFailureAsync(pending);
        Assert.IsType<WebSocketException>(exception);
    }

    private static async Task<Exception> WaitForFailureAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(task, completed);

        var exception = await Record.ExceptionAsync(async () => await task);
        Assert.NotNull(exception);
        return exception;
    }

    private static async Task<byte[]> ReceiveBinaryMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new WebSocketException("Unexpected close frame while waiting for a request.");
            }

            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return stream.ToArray();
            }
        }
    }

    private static async Task ReceiveCloseAndReplyAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "server acknowledged", cancellationToken);
                return;
            }
        }
    }

    private sealed class WebSocketTestServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private WebSocketTestServer(WebApplication app, Uri webSocketUri)
        {
            _app = app;
            WebSocketUri = webSocketUri;
        }

        public Uri WebSocketUri { get; }

        public static async Task<WebSocketTestServer> StartAsync(
            Func<WebSocket, Task> handler,
            CancellationToken cancellationToken)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>()
            });
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

            var app = builder.Build();
            app.UseWebSockets();
            app.Map("/ws", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                using var socket = await context.WebSockets.AcceptWebSocketAsync("yuocore.v1");
                await handler(socket);
            });

            await app.StartAsync(cancellationToken);
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()?
                .Addresses;
            var address = Assert.Single(addresses ?? Array.Empty<string>());
            return new WebSocketTestServer(app, new Uri(address.Replace("http://", "ws://", StringComparison.Ordinal) + "/ws"));
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DisposeAsync();
        }
    }
}
