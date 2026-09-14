using System.Threading.Channels;
using YuoCore.Ecs;
using YuoCore.Ecs.Log;

namespace YuoCore.Runtime;

public sealed class YuoFiber : IAsyncDisposable, IDisposable
{
    // 当前正在执行 Tick 的 Fiber，随 async 上下文流动。
    // 用于同 Fiber CallAsync / Fiber 内等本 Fiber 驱动的 Timer 等死锁守卫。
    private static readonly AsyncLocal<YuoFiber?> s_current = new();
    private readonly Channel<Func<YuoFiber, CancellationToken, ValueTask>> _mailbox;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _pendingMessages;
    private bool _started;
    private bool _disposed;

    internal YuoFiber(YuoFiberOptions options, YuoWorld? world = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Id = options.Id;
        Name = string.IsNullOrWhiteSpace(options.Name) ? $"Fiber-{Id}" : options.Name;
        World = world ?? new YuoWorld();
        _mailbox = options.MaxMailboxCapacity > 0
            ? Channel.CreateBounded<Func<YuoFiber, CancellationToken, ValueTask>>(
                new BoundedChannelOptions(options.MaxMailboxCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait
                })
            : Channel.CreateUnbounded<Func<YuoFiber, CancellationToken, ValueTask>>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
    }

    public long Id { get; }

    public string Name { get; }

    public YuoWorld World { get; }

    public YuoFiberOptions Options { get; }

    public bool IsStarted => _started;

    public bool IsDisposed => _disposed;

    /// <summary>
    /// 当前 mailbox 中尚未消费的消息数（含写入阻塞中的投递），供监控/背压观测。
    /// </summary>
    public int PendingMessageCount => Volatile.Read(ref _pendingMessages);

    /// <summary>当前执行上下文所属的 Fiber；不在任何 Fiber Tick 内时为 null。</summary>
    public static YuoFiber? Current => s_current.Value;

    public void Start()
    {
        ThrowIfDisposed();
        if (_started) return;

        // Fiber 启动时才初始化 World，这样宿主可以先创建所有 Fiber，再统一注册 Actor/服务。
        // SetWorldAsDefault 只影响兼容旧单机写法的 YuoWorld.Instance，不改变这个 Fiber 对 World 的所有权。
        World.OnInit(Options.SetWorldAsDefault);
        _started = true;
    }

    public ValueTask PostAsync(Action<YuoFiber> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return PostAsync((fiber, _) =>
        {
            action(fiber);
            return ValueTask.CompletedTask;
        }, cancellationToken);
    }

    public ValueTask PostAsync(Func<YuoFiber, CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        return EnqueueAsync(action, cancellationToken);
    }

    private async ValueTask EnqueueAsync(
        Func<YuoFiber, CancellationToken, ValueTask> action,
        CancellationToken cancellationToken)
    {
        // 写入前计数：有界通道下阻塞中的 writer 也属于待处理压力；
        // 且避免 reader 先消费导致计数出现负值。
        Interlocked.Increment(ref _pendingMessages);
        try
        {
            await _mailbox.Writer.WriteAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _pendingMessages);
            throw;
        }
    }

    public ValueTask<T> CallAsync<T>(Func<YuoFiber, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        // 同 Fiber 内等待本 Fiber mailbox 必然死锁（drain 被本动作占住），快速失败。
        if (ReferenceEquals(s_current.Value, this))
        {
            throw new InvalidOperationException(
                $"YuoFiber '{Name}': CallAsync on the executing fiber would deadlock. Use PostAsync or restructure the call chain.");
        }

        return CallAsyncCore(action, cancellationToken);
    }

    private async ValueTask<T> CallAsyncCore<T>(Func<YuoFiber, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
    {
        // CallAsync 是跨 Fiber 同步请求的基础：调用方等待 TaskCompletionSource，
        // 真正的业务代码仍然排进目标 Fiber mailbox，由目标 Fiber 串行执行。
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await EnqueueAsync(async (fiber, token) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.TrySetResult(await action(fiber, token).ConfigureAwait(false));
            }
            catch (OperationCanceledException e)
            {
                completion.TrySetCanceled(e.CancellationToken);
            }
            catch (Exception e)
            {
                completion.TrySetException(e);
            }
        }, cancellationToken).ConfigureAwait(false);

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask TickAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureStarted();

        var previous = s_current.Value;
        s_current.Value = this;
        try
        {
            // mailbox 先于 World.Update 执行，保证本帧收到的命令可以在同一帧进入 ECS 逻辑。
            // MaxMailboxMessagesPerTick 用来限制突发消息把帧更新完全饿死。
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            await DrainMailboxAsync(linkedCts.Token).ConfigureAwait(false);
            World.Update();
        }
        finally
        {
            s_current.Value = previous;
        }
    }

    public async ValueTask FixedTickAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureStarted();

        var previous = s_current.Value;
        s_current.Value = this;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            await DrainMailboxAsync(linkedCts.Token).ConfigureAwait(false);
            World.FixedUpdate();
        }
        finally
        {
            s_current.Value = previous;
        }
    }

    public void LateTick()
    {
        ThrowIfDisposed();
        EnsureStarted();

        var previous = s_current.Value;
        s_current.Value = this;
        try
        {
            World.LateUpdate();
        }
        finally
        {
            s_current.Value = previous;
        }
    }

    public async ValueTask DrainMailboxAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var maxMessages = Math.Max(1, Options.MaxMailboxMessagesPerTick);
        var processed = 0;
        // Channel 配置为单读多写：多个线程可以投递，只有拥有者 Fiber 在 Tick 中消费。
        // 这是当前多线程模型的核心约束，不允许外部线程直接改这个 Fiber 的 ECS 数据。
        while (processed++ < maxMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_mailbox.Reader.TryRead(out var action))
            {
                return;
            }

            Interlocked.Decrement(ref _pendingMessages);
            try
            {
                await action(this, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 单条 fire-and-forget 消息失败不得中断 tick，也不允许静默丢失
                ReportUnhandledException(exception);
            }
        }
    }

    /// <summary>
    /// fire-and-forget 路径的异常出口：优先走宿主注入的 UnhandledException，
    /// 未配置时记录错误日志，保证异常可见。
    /// </summary>
    public void ReportUnhandledException(Exception exception, string? context = null)
    {
        if (Options.UnhandledException is { } handler)
        {
            handler(this, exception);
            return;
        }

        var scope = context is null ? "mailbox action" : context;
        $"YuoFiber '{Name}' {scope} failed: {exception}".LogError();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mailbox.Writer.TryComplete();
        _disposeCts.Cancel();
        if (_started)
        {
            World.OnQuit();
            World.OnDestroy();
        }
        _disposeCts.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void EnsureStarted()
    {
        if (!_started) throw new InvalidOperationException($"YuoFiber '{Name}' has not been started.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(YuoFiber), $"YuoFiber '{Name}' is disposed.");
    }
}
