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

        if (!Enum.IsDefined(StateSyncMode))
        {
            throw new ArgumentOutOfRangeException(nameof(StateSyncMode), "state sync push mode is not supported.");
        }
    }
}
