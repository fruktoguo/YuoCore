namespace YuoCore.Runtime;

/// <summary>
/// YuoCore 运行时的顶层宿主，管理所有 Fiber 的生命周期和 Tick 调度。
/// 典型用法：创建实例，按需注册服务，然后在游戏主循环中调用 TickAsync 或 TickParallelAsync。
/// </summary>
public sealed class YuoCoreRuntime : IAsyncDisposable, IDisposable
{
    private bool _disposed;

    /// <summary>全局 Fiber 管理器，持有所有已注册的 Fiber。</summary>
    public YuoFiberManager Fibers { get; } = new();

    /// <summary>
    /// 串行驱动所有 Fiber 的 Tick（按创建顺序逐个执行）。
    /// 适用于 Fiber 数量少或需要确定性执行顺序的场景。
    /// </summary>
    public async ValueTask TickAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Fibers.TickAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 并行驱动所有 Fiber 的 Tick（Fiber 之间并行，Fiber 内部仍串行）。
    /// 利用多核能力，适用于大量独立 Fiber 的场景。
    /// </summary>
    /// <param name="maxConcurrency">最大并行度，0 表示使用 CPU 核数。</param>
    public async ValueTask TickParallelAsync(int maxConcurrency = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Fibers.TickAllParallelAsync(maxConcurrency, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Fibers.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(YuoCoreRuntime));
    }
}
