using YuoCore.Gateway;
using YuoCore.Abstractions;

namespace YuoCore.Gateway.Tests;

public sealed class YuoGatewayDispatchPipelineTests
{
    [Fact]
    public async Task Pipeline_rejects_replayed_client_sequence()
    {
        var session = new YuoGatewaySession();
        var pipeline = new YuoGatewayDispatchPipeline(new EchoDispatcher());
        var message = new YuoGatewayInboundMessage(1, 10, 100, 1, ReadOnlyMemory<byte>.Empty);

        var first = await pipeline.DispatchAsync(session, message);
        var replay = await pipeline.DispatchAsync(session, message);

        Assert.Null(first.Error);
        Assert.NotNull(replay.Error);
        Assert.Equal("invalid", replay.Error!.Code);
        Assert.Equal(2UL, replay.ServerSeq);
    }

    [Fact]
    public async Task Pipeline_assigns_server_sequence_when_dispatcher_does_not()
    {
        var session = new YuoGatewaySession();
        var pipeline = new YuoGatewayDispatchPipeline(new EchoDispatcher());

        var response = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(1, 11, 101, 5, new byte[] { 1, 2 }));

        Assert.Equal(1UL, response.ServerSeq);
        Assert.Equal(new byte[] { 1, 2 }, response.Payload.ToArray());
    }

    [Fact]
    public async Task Pipeline_advances_session_when_dispatcher_supplies_server_sequence()
    {
        var session = new YuoGatewaySession();
        var pipeline = new YuoGatewayDispatchPipeline(new FixedServerSeqOnceDispatcher(10));

        var supplied = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(1, 11, 101, 5, ReadOnlyMemory<byte>.Empty));
        var next = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(1, 11, 102, 6, ReadOnlyMemory<byte>.Empty));

        Assert.Equal(10UL, supplied.ServerSeq);
        Assert.Equal(11UL, next.ServerSeq);
        Assert.Equal(11UL, session.LastServerSeq);
    }

    [Fact]
    public async Task Pipeline_preserves_expected_yuo_error()
    {
        var session = new YuoGatewaySession();
        var pipeline = new YuoGatewayDispatchPipeline(new BusinessErrorDispatcher());

        var response = await pipeline.DispatchAsync(
            session,
            new YuoGatewayInboundMessage(1, 12, 102, 7, ReadOnlyMemory<byte>.Empty));

        Assert.NotNull(response.Error);
        Assert.Equal("wallet.insufficient_gold", response.Error!.Code);
        Assert.Equal("not enough gold", response.Error.Message);
        Assert.Equal(1UL, response.ServerSeq);
        Assert.Equal(7UL, session.LastClientSeq);
    }

    [Fact]
    public async Task Pipeline_honors_cancellation_before_accepting_client_sequence()
    {
        var session = new YuoGatewaySession();
        var inner = new CountingDispatcher();
        var pipeline = new YuoGatewayDispatchPipeline(inner);
        var message = new YuoGatewayInboundMessage(1, 12, 102, 7, ReadOnlyMemory<byte>.Empty);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await pipeline.DispatchAsync(session, message, cts.Token));

        var retry = await pipeline.DispatchAsync(session, message);
        Assert.Null(retry.Error);
        Assert.Equal(1, inner.Count);
    }

    [Fact]
    public void Pipeline_rejects_null_dispatcher()
    {
        Assert.Throws<ArgumentNullException>(() => new YuoGatewayDispatchPipeline(null!));
    }

    [Fact]
    public async Task Pipeline_rejects_null_message_before_dispatch()
    {
        var session = new YuoGatewaySession();
        var inner = new CountingDispatcher();
        var pipeline = new YuoGatewayDispatchPipeline(inner);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await pipeline.DispatchAsync(session, null!));

        Assert.Equal(0, inner.Count);
        Assert.Equal(0UL, session.LastClientSeq);
    }

    [Fact]
    public async Task Pipeline_restores_client_sequence_when_dispatch_is_canceled_after_accept()
    {
        var session = new YuoGatewaySession();
        using var cts = new CancellationTokenSource();
        var inner = new CancelOnceDispatcher(cts);
        var pipeline = new YuoGatewayDispatchPipeline(inner);
        var message = new YuoGatewayInboundMessage(1, 13, 103, 8, ReadOnlyMemory<byte>.Empty);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await pipeline.DispatchAsync(session, message, cts.Token));

        var retry = await pipeline.DispatchAsync(session, message);
        Assert.Null(retry.Error);
        Assert.Equal(2, inner.Count);
        Assert.Equal(8UL, session.LastClientSeq);
    }

    [Fact]
    public void Session_restore_does_not_overwrite_later_accepted_client_sequence()
    {
        var session = new YuoGatewaySession();

        Assert.True(session.TryAcceptClientSeq(10, out var previous));
        Assert.Equal(0UL, previous);
        Assert.True(session.TryAcceptClientSeq(11));

        Assert.False(session.TryRestoreClientSeq(10, previous));
        Assert.Equal(11UL, session.LastClientSeq);
    }

    private sealed class EchoDispatcher : IYuoGatewayDispatcher
    {
        public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
            YuoGatewaySession session,
            YuoGatewayInboundMessage message,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new YuoGatewayOutboundMessage(
                message.Version,
                message.OpCode,
                message.RequestId,
                0,
                message.Payload));
        }
    }

    private sealed class CountingDispatcher : IYuoGatewayDispatcher
    {
        public int Count { get; private set; }

        public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
            YuoGatewaySession session,
            YuoGatewayInboundMessage message,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(new YuoGatewayOutboundMessage(
                message.Version,
                message.OpCode,
                message.RequestId,
                0,
                ReadOnlyMemory<byte>.Empty));
        }
    }

    private sealed class FixedServerSeqOnceDispatcher(ulong serverSeq) : IYuoGatewayDispatcher
    {
        private bool _used;

        public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
            YuoGatewaySession session,
            YuoGatewayInboundMessage message,
            CancellationToken cancellationToken = default)
        {
            var nextServerSeq = _used ? 0 : serverSeq;
            _used = true;
            return ValueTask.FromResult(new YuoGatewayOutboundMessage(
                message.Version,
                message.OpCode,
                message.RequestId,
                nextServerSeq,
                ReadOnlyMemory<byte>.Empty));
        }
    }

    private sealed class CancelOnceDispatcher(CancellationTokenSource cancellation) : IYuoGatewayDispatcher
    {
        public int Count { get; private set; }

        public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
            YuoGatewaySession session,
            YuoGatewayInboundMessage message,
            CancellationToken cancellationToken = default)
        {
            Count++;
            if (Count == 1)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.FromResult(new YuoGatewayOutboundMessage(
                message.Version,
                message.OpCode,
                message.RequestId,
                0,
                ReadOnlyMemory<byte>.Empty));
        }
    }

    private sealed class BusinessErrorDispatcher : IYuoGatewayDispatcher
    {
        public ValueTask<YuoGatewayOutboundMessage> DispatchAsync(
            YuoGatewaySession session,
            YuoGatewayInboundMessage message,
            CancellationToken cancellationToken = default)
        {
            throw new YuoException(new YuoError("wallet.insufficient_gold", "not enough gold"));
        }
    }
}
