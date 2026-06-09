using System.Threading.Channels;
using YuoCore.Ecs;

namespace YuoCore.Runtime;

public sealed class YuoFiber : IAsyncDisposable, IDisposable
{
    private readonly Channel<Func<YuoFiber, CancellationToken, ValueTask>> _mailbox;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _started;
    private bool _disposed;

    internal YuoFiber(YuoFiberOptions options, YuoWorld? world = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Id = options.Id;
        Name = string.IsNullOrWhiteSpace(options.Name) ? $"Fiber-{Id}" : options.Name;
        World = world ?? new YuoWorld();
        _mailbox = Channel.CreateUnbounded<Func<YuoFiber, CancellationToken, ValueTask>>(
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
        return _mailbox.Writer.WriteAsync(action, cancellationToken);
    }

    public async ValueTask<T> CallAsync<T>(Func<YuoFiber, CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();

        // CallAsync 是跨 Fiber 同步请求的基础：调用方等待 TaskCompletionSource，
        // 真正的业务代码仍然排进目标 Fiber mailbox，由目标 Fiber 串行执行。
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _mailbox.Writer.WriteAsync(async (fiber, token) =>
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

        // mailbox 先于 World.Update 执行，保证本帧收到的命令可以在同一帧进入 ECS 逻辑。
        // MaxMailboxMessagesPerTick 用来限制突发消息把帧更新完全饿死。
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        await DrainMailboxAsync(linkedCts.Token).ConfigureAwait(false);
        World.Update();
    }

    public async ValueTask FixedTickAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureStarted();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        await DrainMailboxAsync(linkedCts.Token).ConfigureAwait(false);
        World.FixedUpdate();
    }

    public void LateTick()
    {
        ThrowIfDisposed();
        EnsureStarted();
        World.LateUpdate();
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

            await action(this, cancellationToken).ConfigureAwait(false);
        }
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
