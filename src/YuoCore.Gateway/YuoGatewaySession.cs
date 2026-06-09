namespace YuoCore.Gateway;

/// <summary>
/// 客户端连接在 Gateway 内的会话状态。
/// 它只描述“谁通过哪个连接接入、客户端序号处理到哪里”，不保存 WebSocket 对象，也不保存 ECS Entity。
/// 这样 GameShard 可以完全不知道客户端连接类型，Web 和 Unity 也能共用同一套会话语义。
/// </summary>
public sealed class YuoGatewaySession
{
    private ulong _lastClientSeq;
    private ulong _lastServerSeq;

    public YuoGatewaySession(Guid? sessionId = null, DateTimeOffset? now = null)
    {
        SessionId = sessionId ?? Guid.NewGuid();
        CreatedAt = now ?? DateTimeOffset.UtcNow;
        LastHeartbeatAt = CreatedAt;
    }

    public Guid SessionId { get; }

    public Guid? AccountId { get; private set; }

    public Guid? PlayerId { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset LastHeartbeatAt { get; private set; }

    public ulong LastClientSeq => _lastClientSeq;

    public ulong LastServerSeq => _lastServerSeq;

    public bool IsAuthenticated => AccountId.HasValue;

    public bool IsPlayerBound => PlayerId.HasValue;

    public void BindAccount(Guid accountId)
    {
        AccountId = accountId;
    }

    public void BindPlayer(Guid playerId)
    {
        PlayerId = playerId;
    }

    public void TouchHeartbeat(DateTimeOffset now)
    {
        LastHeartbeatAt = now;
    }

    public ulong NextServerSeq()
    {
        return Interlocked.Increment(ref _lastServerSeq);
    }

    internal void AdvanceServerSeqAtLeast(ulong serverSeq)
    {
        if (serverSeq == 0)
        {
            return;
        }

        while (true)
        {
            var current = Volatile.Read(ref _lastServerSeq);
            if (serverSeq <= current)
            {
                return;
            }

            // 某些 bridge 会提前填好 serverSeq。Gateway 仍要记住这个下限，
            // 避免下一条由 Pipeline 自动分配的响应回退到更小序号。
            if (Interlocked.CompareExchange(ref _lastServerSeq, serverSeq, current) == current)
            {
                return;
            }
        }
    }

    public bool TryAcceptClientSeq(ulong clientSeq)
    {
        return TryAcceptClientSeq(clientSeq, out _);
    }

    internal bool TryAcceptClientSeq(ulong clientSeq, out ulong previousClientSeq)
    {
        // clientSeq 为 0 表示调用方暂时不启用有序输入校验，适合登录、心跳和早期 MVP。
        if (clientSeq == 0)
        {
            previousClientSeq = 0;
            return true;
        }

        while (true)
        {
            var current = Volatile.Read(ref _lastClientSeq);
            if (clientSeq <= current)
            {
                previousClientSeq = current;
                return false;
            }

            if (Interlocked.CompareExchange(ref _lastClientSeq, clientSeq, current) == current)
            {
                previousClientSeq = current;
                return true;
            }
        }
    }

    internal bool TryRestoreClientSeq(ulong acceptedClientSeq, ulong previousClientSeq)
    {
        if (acceptedClientSeq == 0)
        {
            return true;
        }

        return Interlocked.CompareExchange(ref _lastClientSeq, previousClientSeq, acceptedClientSeq) == acceptedClientSeq;
    }
}
