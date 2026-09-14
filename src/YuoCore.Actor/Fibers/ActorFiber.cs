using System.Threading.Channels;

namespace YuoCore.Actor;

public sealed class ActorFiber : IActorEndpoint, IAsyncDisposable
{
    private readonly Channel<MailboxItem> _mailbox;
    private readonly IActorMessageDispatcher _dispatcher;
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<ActorEnvelope, Exception>? _unhandledError;
    private int _pendingMessages;
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

    /// <summary>当前 mailbox 中尚未消费的消息数（含写入阻塞中的投递），供监控观测。</summary>
    public int PendingMessageCount => Volatile.Read(ref _pendingMessages);

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

        await EnqueueAsync(new MailboxItem(envelope), cancellationToken);
    }

    // 当前正在派发消息的 ActorFiber，随 async 上下文流动，用于同 Fiber Invoke 死锁守卫
    private static readonly AsyncLocal<ActorFiber?> s_current = new();

    /// <summary>当前执行上下文所属的 ActorFiber；不在任何 ActorFiber 派发内时为 null。</summary>
    public static ActorFiber? Current => s_current.Value;

    public ValueTask<ActorEnvelope> InvokeAsync(
        ActorEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        // 同 Fiber 内 Invoke 必然死锁（单 reader 被本消息占住），快速失败。
        if (ReferenceEquals(s_current.Value, this))
        {
            throw new InvalidOperationException(
                $"ActorFiber {FiberId}: InvokeAsync on the executing fiber would deadlock. Use SendAsync or restructure the call chain.");
        }

        return InvokeAsyncCore(envelope, cancellationToken);
    }

    private async ValueTask<ActorEnvelope> InvokeAsyncCore(
        ActorEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ActorEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await EnqueueAsync(new MailboxItem(envelope, completion), cancellationToken);

        using var registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));

        return await completion.Task.ConfigureAwait(false);
    }

    private ActorException CreateClosedException()
    {
        return new ActorException(ActorError.NotFound($"Actor fiber {FiberId} mailbox is closed."));
    }

    private async ValueTask EnqueueAsync(MailboxItem item, CancellationToken cancellationToken)
    {
        // 写入前计数，避免 reader 先消费导致计数出现负值
        Interlocked.Increment(ref _pendingMessages);
        try
        {
            await _mailbox.Writer.WriteAsync(item, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            Interlocked.Decrement(ref _pendingMessages);
            throw CreateClosedException();
        }
        catch
        {
            Interlocked.Decrement(ref _pendingMessages);
            throw;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in _mailbox.Reader.ReadAllAsync(cancellationToken))
            {
                Interlocked.Decrement(ref _pendingMessages);
                await DispatchMailboxItemAsync(item, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async ValueTask DispatchMailboxItemAsync(MailboxItem item, CancellationToken cancellationToken)
    {
        var previous = s_current.Value;
        s_current.Value = this;
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
        finally
        {
            s_current.Value = previous;
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
