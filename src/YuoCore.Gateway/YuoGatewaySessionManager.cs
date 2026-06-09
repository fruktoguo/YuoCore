using System.Collections.Concurrent;

namespace YuoCore.Gateway;

/// <summary>
/// Gateway 进程内的 session 表。
/// 这里故意只管理 session 生命周期，不做鉴权、不查数据库、不触碰 ECS，避免 Gateway 基础设施变成业务大杂烩。
/// </summary>
public sealed class YuoGatewaySessionManager
{
    private readonly ConcurrentDictionary<Guid, YuoGatewaySession> _sessions = new();

    public YuoGatewaySession Create(DateTimeOffset? now = null)
    {
        var session = new YuoGatewaySession(now: now);
        _sessions[session.SessionId] = session;
        return session;
    }

    public bool TryGet(Guid sessionId, out YuoGatewaySession session)
    {
        return _sessions.TryGetValue(sessionId, out session!);
    }

    public bool Remove(Guid sessionId)
    {
        return _sessions.TryRemove(sessionId, out _);
    }

    public IReadOnlyCollection<YuoGatewaySession> Snapshot()
    {
        return _sessions.Values.ToArray();
    }
}
