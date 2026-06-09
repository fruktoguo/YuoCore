using YuoCore.Gateway;

namespace YuoCore.Gateway.Tests;

public sealed class YuoGatewaySessionTests
{
    [Fact]
    public void Session_tracks_bindings_and_sequences()
    {
        var session = new YuoGatewaySession(now: DateTimeOffset.UnixEpoch);
        var accountId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        session.BindAccount(accountId);
        session.BindPlayer(playerId);

        Assert.True(session.IsAuthenticated);
        Assert.True(session.IsPlayerBound);
        Assert.Equal(accountId, session.AccountId);
        Assert.Equal(playerId, session.PlayerId);
        Assert.True(session.TryAcceptClientSeq(1));
        Assert.False(session.TryAcceptClientSeq(1));
        Assert.True(session.TryAcceptClientSeq(2));
        Assert.Equal(1UL, session.NextServerSeq());
        Assert.Equal(2UL, session.NextServerSeq());
    }

    [Fact]
    public void Session_manager_creates_and_removes_sessions()
    {
        var manager = new YuoGatewaySessionManager();
        var session = manager.Create();

        Assert.True(manager.TryGet(session.SessionId, out var found));
        Assert.Same(session, found);
        Assert.True(manager.Remove(session.SessionId));
        Assert.False(manager.TryGet(session.SessionId, out _));
    }
}
