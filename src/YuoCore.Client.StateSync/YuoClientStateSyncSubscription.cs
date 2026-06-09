using YuoCore.Client;

namespace YuoCore.Client.StateSync;

/// <summary>
/// 把可选的客户端 push transport 接到 StateSync receiver。
/// 它不持有 WebSocket，也不处理重连；只负责把 requestId=0 的服务端推送应用到本地 Mirror。
/// </summary>
public sealed class YuoClientStateSyncSubscription : IDisposable
{
    private readonly IYuoClientPushTransport _transport;
    private readonly Action<YuoClientServerMessage> _handler;
    private bool _disposed;

    private YuoClientStateSyncSubscription(
        IYuoClientPushTransport transport,
        YuoClientStateSyncReceiver receiver,
        Action<YuoClientStateSyncReceiveResult>? onReceive)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _handler = message =>
        {
            var result = Receiver.ApplyServerEnvelope(message.Bytes);
            if (result.IsStateSyncMessage)
            {
                onReceive?.Invoke(result);
            }
        };
        _transport.PushReceived += _handler;
    }

    public YuoClientStateSyncReceiver Receiver { get; }

    public static YuoClientStateSyncSubscription Attach(
        IYuoClientPushTransport transport,
        YuoClientStateSyncReceiver? receiver = null,
        Action<YuoClientStateSyncReceiveResult>? onReceive = null)
    {
        return new YuoClientStateSyncSubscription(
            transport,
            receiver ?? new YuoClientStateSyncReceiver(),
            onReceive);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport.PushReceived -= _handler;
    }
}
