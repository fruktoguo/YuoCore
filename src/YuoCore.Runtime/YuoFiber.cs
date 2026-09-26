using System.Collections.Concurrent;
using System.Threading.Channels;
using YuoCore.Ecs;
using YuoCore.Ecs.Log;

namespace YuoCore.Runtime;

public sealed class YuoFiber : IAsyncDisposable, IDisposable
{
    // 当前正在执行 Tick 的 Fiber，随 async 上下文流动。
    // 用于同 Fiber CallAsync / Fiber 内等本 Fiber 驱动的 Timer 等死锁守卫。
    private static readonly AsyncLocal<YuoFiber?> s_current = new();
    private readonly Channel<MailboxItem> _mailbox;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _pendingMessages;
    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 池化的邮箱节点：同步消息与异步消息共用一条队列，
    /// 消费后归还节点，fire-and-forget 投递在稳态下零分配。
    /// </summary>
    private sealed class MailboxItem
    {
        public Action<YuoFiber>? Sync;
        public Func<YuoFiber, CancellationToken, ValueTask>? Async;

        public void Reset()
        {
            Sync = null;
            Async = null;
        }
    }

    // 写入方可能来自任意线程，回收栈必须线程安全。
    private static readonly ConcurrentStack<MailboxItem> s_itemPool = new();

    private static MailboxItem RentItem()
    {
        return s_itemPool.TryPop(out var item) ? item : new MailboxItem();
    }

    private static void ReturnItem(MailboxItem item)
    {
        item.Reset();
        s_itemPool.Push(item);
    }

    internal YuoFiber(YuoFiberOptions options, YuoWorld? world = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Id = options.Id;
        Name = string.IsNullOrWhiteSpace(options.Name) ? $"Fiber-{Id}" : options.Name;
        World = world ?? new YuoWorld();
        _mailbox = options.MaxMailboxCapacity > 0
            ? Channel.CreateBounded<MailboxItem>(
                new BoundedChannelOptions(options.MaxMailboxCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait
                })
            : Channel.CreateUnbounded<MailboxItem>(
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
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.start");
        YuoCoreTrace.Count("yuocore.fiber.start.count");
        ThrowIfDisposed();
        if (_started) return;

        // Fiber 启动时才初始化 World，这样宿主可以先创建所有 Fiber，再统一注册 Actor/服务。
        // SetWorldAsDefault 只影响兼容旧单机写法的 YuoWorld.Instance，不改变这个 Fiber 对 World 的所有权。
        World.OnInit(Options.SetWorldAsDefault);
        _started = true;
    }

    public ValueTask PostAsync(Action<YuoFiber> action, CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.post.sync");
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        YuoCoreTrace.Count("yuocore.fiber.post.sync.count");
        YuoCoreTrace.Count("yuocore.fiber.enqueue.count");
        // 池化节点直接承载同步委托，不再为每条消息分配包装闭包。
        var item = RentItem();
        item.Sync = action;
        var enqueueMeasurement = YuoCoreTrace.Measure("yuocore.fiber.enqueue");
        // 快速路径：未取消且邮箱未满时同步入队，连 async 状态机都省掉。
        if (!cancellationToken.IsCancellationRequested && TryEnqueue(item))
        {
            enqueueMeasurement.Dispose();
            return ValueTask.CompletedTask;
        }

        return EnqueueAsync(item, cancellationToken, enqueueMeasurement);
    }

    public ValueTask PostAsync(Func<YuoFiber, CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.post.async");
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();
        YuoCoreTrace.Count("yuocore.fiber.post.async.count");
        YuoCoreTrace.Count("yuocore.fiber.enqueue.count");
        var item = RentItem();
        item.Async = action;
        var enqueueMeasurement = YuoCoreTrace.Measure("yuocore.fiber.enqueue");
        if (!cancellationToken.IsCancellationRequested && TryEnqueue(item))
        {
            enqueueMeasurement.Dispose();
            return ValueTask.CompletedTask;
        }

        return EnqueueAsync(item, cancellationToken, enqueueMeasurement);
    }

    /// <summary>
    /// 同步写入尝试：成功则计数已提交；失败归还节点并让调用方走等待路径。
    /// </summary>
    private bool TryEnqueue(MailboxItem item)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.enqueue.try_write");
        // 写入前计数：有界通道下阻塞中的 writer 也属于待处理压力；
        // 且避免 reader 先消费导致计数出现负值。
        Interlocked.Increment(ref _pendingMessages);
        if (_mailbox.Writer.TryWrite(item))
        {
            YuoCoreTrace.Count("yuocore.fiber.enqueue.try_write.success");
            return true;
        }
        Interlocked.Decrement(ref _pendingMessages);
        YuoCoreTrace.Count("yuocore.fiber.enqueue.try_write.fallback");
        return false;
    }

    private async ValueTask EnqueueAsync(
        MailboxItem item,
        CancellationToken cancellationToken,
        YuoCoreTrace.Measurement enqueueMeasurement)
    {
        using var measurement = enqueueMeasurement;
        // TryEnqueue 失败时计数已回退，这里重新计入阻塞中的投递。
        Interlocked.Increment(ref _pendingMessages);
        try
        {
            using (YuoCoreTrace.Measure("yuocore.fiber.enqueue.write"))
                await _mailbox.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
            YuoCoreTrace.Count("yuocore.fiber.enqueue.write.success");
        }
        catch
        {
            Interlocked.Decrement(ref _pendingMessages);
            ReturnItem(item);
            throw;
        }
    }

    public ValueTask<T> CallAsync<T>(Func<YuoFiber, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.call");
        YuoCoreTrace.Count("yuocore.fiber.call.count");
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
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.call_core");
        YuoCoreTrace.Count("yuocore.fiber.enqueue.count");
        // CallAsync 是跨 Fiber 同步请求的基础：调用方等待 TaskCompletionSource，
        // 真正的业务代码仍然排进目标 Fiber mailbox，由目标 Fiber 串行执行。
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = RentItem();
        item.Async = async (fiber, token) =>
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
        };

        var enqueueMeasurement = YuoCoreTrace.Measure("yuocore.fiber.enqueue");
        if (cancellationToken.IsCancellationRequested || !TryEnqueue(item))
        {
            await EnqueueAsync(item, cancellationToken, enqueueMeasurement).ConfigureAwait(false);
        }
        else
        {
            enqueueMeasurement.Dispose();
        }

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask TickAsync(CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.tick");
        YuoCoreTrace.Count("yuocore.fiber.tick.count");
        ThrowIfDisposed();
        EnsureStarted();

        var previous = s_current.Value;
        s_current.Value = this;
        // 常见路径下调用方不传递可取消令牌：直接用 dispose 令牌，省掉每帧 linked CTS 分配。
        var linkedCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token)
            : null;
        try
        {
            // mailbox 先于 World.Update 执行，保证本帧收到的命令可以在同一帧进入 ECS 逻辑。
            // MaxMailboxMessagesPerTick 用来限制突发消息把帧更新完全饿死。
            using (YuoCoreTrace.Measure("yuocore.fiber.drain_mailbox"))
                await DrainMailboxAsync(linkedCts?.Token ?? _disposeCts.Token).ConfigureAwait(false);
            using (YuoCoreTrace.Measure("yuocore.fiber.world_update"))
                World.Update();
        }
        finally
        {
            linkedCts?.Dispose();
            s_current.Value = previous;
        }
    }

    public async ValueTask FixedTickAsync(CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.fixed_tick");
        YuoCoreTrace.Count("yuocore.fiber.fixed_tick.count");
        ThrowIfDisposed();
        EnsureStarted();

        var previous = s_current.Value;
        s_current.Value = this;
        var linkedCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token)
            : null;
        try
        {
            using (YuoCoreTrace.Measure("yuocore.fiber.drain_mailbox"))
                await DrainMailboxAsync(linkedCts?.Token ?? _disposeCts.Token).ConfigureAwait(false);
            using (YuoCoreTrace.Measure("yuocore.fiber.world_fixed_update"))
                World.FixedUpdate();
        }
        finally
        {
            linkedCts?.Dispose();
            s_current.Value = previous;
        }
    }

    public void LateTick()
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.late_tick");
        YuoCoreTrace.Count("yuocore.fiber.late_tick.count");
        ThrowIfDisposed();
        EnsureStarted();

        var previous = s_current.Value;
        s_current.Value = this;
        try
        {
            using (YuoCoreTrace.Measure("yuocore.fiber.world_late_update"))
                World.LateUpdate();
        }
        finally
        {
            s_current.Value = previous;
        }
    }

    public async ValueTask DrainMailboxAsync(CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber.mailbox_drain");
        YuoCoreTrace.Count("yuocore.fiber.mailbox_drain.count");
        ThrowIfDisposed();

        var maxMessages = Math.Max(1, Options.MaxMailboxMessagesPerTick);
        var processed = 0;
        // Channel 配置为单读多写：多个线程可以投递，只有拥有者 Fiber 在 Tick 中消费。
        // 这是当前多线程模型的核心约束，不允许外部线程直接改这个 Fiber 的 ECS 数据。
        while (processed++ < maxMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_mailbox.Reader.TryRead(out var item))
            {
                return;
            }

            Interlocked.Decrement(ref _pendingMessages);
            YuoCoreTrace.Count("yuocore.fiber.mailbox_message");
            try
            {
                using var itemMeasurement = YuoCoreTrace.Measure("yuocore.fiber.mailbox_action");
                if (item.Sync is { } sync)
                {
                    YuoCoreTrace.Count("yuocore.fiber.mailbox_action.sync.count");
                    sync(this);
                }
                else
                {
                    YuoCoreTrace.Count("yuocore.fiber.mailbox_action.async.count");
                    // 同步完成的 ValueTask 不产生额外挂起；真正异步的消息才进入状态机。
                    var task = item.Async!(this, cancellationToken);
                    if (!task.IsCompletedSuccessfully)
                    {
                        await task.ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ReturnItem(item);
                throw;
            }
            catch (Exception exception)
            {
                // 单条 fire-and-forget 消息失败不得中断 tick，也不允许静默丢失
                ReturnItem(item);
                ReportUnhandledException(exception);
                continue;
            }

            ReturnItem(item);
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
