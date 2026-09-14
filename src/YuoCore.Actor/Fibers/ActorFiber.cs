using System.Threading.Channels;

namespace YuoCore.Actor;

public sealed class ActorFiber : IActorEndpoint, IAsyncDisposable
{
    private readonly Channel<MailboxItem> _mailbox;
    private readonly IActorMessageDispatcher _dispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<ActorEnvelope, Exception>? _unhandledError;
    private Task? _runTask;

    public ActorFiber(
        int fiberId,
        IActorMessageDispatcher dispatcher,
        string nodeId = "local",
        Action<ActorEnvelope, Exception>? onUnhandledError = null)
    {
        FiberId = fiberId;
        NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _unhandledError = onUnhandledError;
        _mailbox = Channel.CreateUnbounded<MailboxItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public int FiberId { get; }

    public string NodeId { get; }

    public ChannelReader<MailboxItem> Reader => _mailbox.Reader;

    public void Start(CancellationToken cancellationToken = default)
    {
        if (_runTask is not null)
        {
            return;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        _runTask = Task.Run(() => RunAsync(linked.Token), CancellationToken.None);
    }

    public async ValueTask SendAsync(ActorEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        try
        {
            await _mailbox.Writer.WriteAsync(new MailboxItem(envelope), cancellationToken);
        }
        catch (ChannelClosedException)
        {
            throw CreateClosedException();
        }
    }

    public async ValueTask<ActorEnvelope> InvokeAsync(
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var completion = new TaskCompletionSource<ActorEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await _mailbox.Writer.WriteAsync(new MailboxItem(envelope, completion), cancellationToken);
        }
        catch (ChannelClosedException)
        {
            throw CreateClosedException();
        }

        using var registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));

        return await completion.Task.ConfigureAwait(false);
    }

    private ActorException CreateClosedException()
    {
        return new ActorException(ActorError.NotFound($"Actor fiber {FiberId} mailbox is closed."));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _mailbox.Reader.ReadAllAsync(cancellationToken))
            {
                await DispatchMailboxItemAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask DispatchMailboxItemAsync(MailboxItem item, CancellationToken cancellationToken)
    {
        try
        {
            // ActorFiber 当前采用单 reader mailbox，同一个 Fiber 内消息串行派发。
            // 后续如果要做 per-actor 并行，也必须保证同一 Actor 的可变状态仍然单线程进入。
            var response = await _dispatcher.DispatchAsync(item.Envelope, cancellationToken)
                .ConfigureAwait(false);

            item.Reply?.TrySetResult(ActorEnvelope.Success(item.Envelope, response));
        }
        catch (ActorException exception)
        {
            if (item.Reply is not null)
            {
                item.Reply.TrySetResult(ActorEnvelope.Failure(item.Envelope, exception.Error));
            }
            else
            {
                // Fire-and-forget actor messages must not tear down the fiber loop,
                // 但异常必须经过出口可见，不允许静默吞掉。
                _unhandledError?.Invoke(item.Envelope, exception);
            }
        }
        catch (Exception exception)
        {
            if (item.Reply is not null)
            {
                item.Reply.TrySetResult(ActorEnvelope.Failure(
                    item.Envelope,
                    ActorError.HandlerFailed(exception)));
            }
            else
            {
                _unhandledError?.Invoke(item.Envelope, exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _mailbox.Writer.TryComplete();

        if (_runTask is not null)
        {
            await _runTask.ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    public sealed record MailboxItem(
        ActorEnvelope Envelope,
        TaskCompletionSource<ActorEnvelope>? Reply = null);
}
