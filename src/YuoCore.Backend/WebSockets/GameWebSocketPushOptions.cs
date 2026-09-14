namespace YuoCore.Backend.WebSockets;

public enum GameWebSocketStateSyncPushMode
{
    PlayerStream = 0,
    AoiStream = 1
}

public sealed class GameWebSocketPushOptions
{
    public GameWebSocketStateSyncPushMode StateSyncMode { get; set; } = GameWebSocketStateSyncPushMode.PlayerStream;

    public int PollIntervalMs { get; set; } = 50;

    public int MaxQueuedMessages { get; set; } = 256;

    public int SendTimeoutMs { get; set; } = 5_000;

    /// <summary>单条客户端消息（含分片累计）的最大字节数，超限即按 MessageTooBig 关闭连接。</summary>
    public int MaxInboundMessageBytes { get; set; } = 256 * 1024;

    /// <summary>入站空闲超时（毫秒）；超过该时间无任何客户端消息即关闭连接。0 表示禁用。</summary>
    public int IdleTimeoutMs { get; set; }

    public void Validate()
    {
        if (PollIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PollIntervalMs), "poll interval must be greater than zero.");
        }

        if (MaxQueuedMessages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxQueuedMessages), "max queued messages must be greater than zero.");
        }

        if (SendTimeoutMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SendTimeoutMs), "send timeout must be greater than zero.");
        }

        if (MaxInboundMessageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxInboundMessageBytes), "max inbound message bytes must be greater than zero.");
        }

        if (IdleTimeoutMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(IdleTimeoutMs), "idle timeout must not be negative.");
        }

        if (!Enum.IsDefined(StateSyncMode))
        {
            throw new ArgumentOutOfRangeException(nameof(StateSyncMode), "state sync push mode is not supported.");
        }
    }
}
