using System.Threading.Channels;
using YuoCore.Gateway;

namespace YuoCore.Backend.WebSockets;

internal sealed class GameWebSocketPushPump(GameWebSocketPushOptions options, ILogger logger)
{
    public async Task RunAsync(
        IYuoClientConnection connection,
        YuoGatewaySession session,
        Func<YuoGatewaySession, List<YuoGatewayOutboundMessage>, int> drainPushes,
        Func<bool> isOpen,
        Func<string, CancellationToken, ValueTask> closeAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(drainPushes);
        ArgumentNullException.ThrowIfNull(isOpen);
        ArgumentNullException.ThrowIfNull(closeAsync);

        options.Validate();

        var channel = Channel.CreateBounded<YuoGatewayOutboundMessage>(new BoundedChannelOptions(options.MaxQueuedMessages)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = ProduceAsync(channel.Writer, session, drainPushes, isOpen, closeAsync, pumpCts);
        var consumer = ConsumeAsync(channel.Reader, connection, closeAsync, pumpCts);

        try
        {
            await Task.WhenAll(producer, consumer).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (pumpCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (ChannelClosedException) when (pumpCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProduceAsync(
        ChannelWriter<YuoGatewayOutboundMessage> writer,
        YuoGatewaySession session,
        Func<YuoGatewaySession, List<YuoGatewayOutboundMessage>, int> drainPushes,
        Func<bool> isOpen,
        Func<string, CancellationToken, ValueTask> closeAsync,
        CancellationTokenSource pumpCts)
    {
        // 每个 WebSocket push producer 复用一个小缓冲，避免每轮 drain 都分配 List。
        // 消息写入 channel 后会复制结构体值，因此下一轮 Clear 不会影响 consumer。
        var pushBuffer = new List<YuoGatewayOutboundMessage>(16);
        try
        {
            while (!pumpCts.IsCancellationRequested && isOpen())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(options.PollIntervalMs), pumpCts.Token).ConfigureAwait(false);
                pushBuffer.Clear();
                _ = drainPushes(session, pushBuffer);
                foreach (var message in pushBuffer)
                {
                    if (writer.TryWrite(message))
                    {
                        continue;
                    }

                    // 慢客户端不能无限积压状态同步包；这里主动断开，让客户端重连后重新拉 snapshot。
                    logger.LogWarning(
                        "Closing WebSocket session {SessionId} because state sync push queue reached {Capacity} messages.",
                        session.SessionId,
                        options.MaxQueuedMessages);
                    await StopSlowClientAsync("state sync push queue overflow", closeAsync, pumpCts).ConfigureAwait(false);
                    writer.TryComplete();
                    return;
                }
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task ConsumeAsync(
        ChannelReader<YuoGatewayOutboundMessage> reader,
        IYuoClientConnection connection,
        Func<string, CancellationToken, ValueTask> closeAsync,
        CancellationTokenSource pumpCts)
    {
        await foreach (var message in reader.ReadAllAsync(pumpCts.Token).ConfigureAwait(false))
        {
            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(pumpCts.Token);
            sendCts.CancelAfter(TimeSpan.FromMilliseconds(options.SendTimeoutMs));

            try
            {
                await connection.SendAsync(message, sendCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!pumpCts.IsCancellationRequested && sendCts.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Closing WebSocket connection {ConnectionId} because a state sync push send exceeded {TimeoutMs} ms.",
                    connection.ConnectionId,
                    options.SendTimeoutMs);
                await StopSlowClientAsync("state sync push send timeout", closeAsync, pumpCts).ConfigureAwait(false);
                return;
            }
        }
    }

    private static async ValueTask StopSlowClientAsync(
        string reason,
        Func<string, CancellationToken, ValueTask> closeAsync,
        CancellationTokenSource pumpCts)
    {
        try
        {
            await closeAsync(reason, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await pumpCts.CancelAsync().ConfigureAwait(false);
        }
    }
}
