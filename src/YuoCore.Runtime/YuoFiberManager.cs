using System.Collections.Concurrent;
using YuoCore.Ecs;

namespace YuoCore.Runtime;

public sealed class YuoFiberManager : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<long, YuoFiber> _fibers = new();
    private readonly ConcurrentDictionary<long, byte> _usedFiberIds = new();
    private long _nextAutoFiberId;
    private bool _disposed;

    public IReadOnlyCollection<YuoFiber> Fibers => _fibers.Values.ToArray();

    public YuoFiber CreateFiber(string name, YuoWorld? world = null, bool setWorldAsDefault = false)
    {
        var id = AllocateAutoFiberId();
        return CreateFiber(new YuoFiberOptions
        {
            Id = id,
            Name = name,
            SetWorldAsDefault = setWorldAsDefault
        }, world, reserveId: false);
    }

    public YuoFiber CreateFiber(YuoFiberOptions options, YuoWorld? world = null)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber_manager.create");
        YuoCoreTrace.Count("yuocore.fiber_manager.create");
        return CreateFiber(options, world, reserveId: true);
    }

    private YuoFiber CreateFiber(YuoFiberOptions options, YuoWorld? world, bool reserveId)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(options);
        if (options.Id == 0) throw new ArgumentException("Fiber id cannot be 0.", nameof(options));
        if (reserveId)
        {
            if (!TryReserveFiberId(options.Id))
            {
                throw new InvalidOperationException($"Fiber id already exists or was used before in this manager: {options.Id}");
            }
        }

        var fiber = new YuoFiber(options, world);
        if (!_fibers.TryAdd(fiber.Id, fiber))
        {
            fiber.Dispose();
            throw new InvalidOperationException($"Fiber id already exists: {fiber.Id}");
        }

        fiber.Start();
        return fiber;
    }

    public bool TryGetFiber(long id, out YuoFiber fiber)
    {
        return _fibers.TryGetValue(id, out fiber!);
    }

    public async ValueTask TickAllAsync(CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber_manager.tick_all");
        YuoCoreTrace.Count("yuocore.fiber_manager.tick_all");
        ThrowIfDisposed();

        foreach (var fiber in _fibers.Values)
        {
            await fiber.TickAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask TickAllParallelAsync(int maxConcurrency = 0, CancellationToken cancellationToken = default)
    {
        using var measurement = YuoCoreTrace.Measure("yuocore.fiber_manager.tick_all_parallel");
        YuoCoreTrace.Count("yuocore.fiber_manager.tick_all_parallel");
        ThrowIfDisposed();
        var fibers = _fibers.Values.ToArray();
        YuoCoreTrace.Count("yuocore.fiber_manager.fiber_snapshot", fibers.Length);
        if (fibers.Length == 0) return;

        var concurrency = maxConcurrency <= 0
            ? Environment.ProcessorCount
            : Math.Max(1, maxConcurrency);

        // 并行粒度限定在 Fiber 之间：每个 Fiber 内部仍是单 reader mailbox + 单 World，
        // 因此 ECS 系统不用为了多线程而加锁，跨 shard 共享状态必须走消息或外部存储。
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(concurrency, fibers.Length)
        };

        await Parallel.ForEachAsync(fibers, parallelOptions, async (fiber, token) =>
        {
            await fiber.TickAsync(token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public bool RemoveFiber(long id)
    {
        if (!_fibers.TryRemove(id, out var fiber)) return false;
        fiber.Dispose();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var fiber in _fibers.Values)
        {
            fiber.Dispose();
        }
        _fibers.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(YuoFiberManager));
    }

    private long AllocateAutoFiberId()
    {
        while (true)
        {
            var id = Interlocked.Increment(ref _nextAutoFiberId);
            if (TryReserveFiberId(id))
            {
                return id;
            }
        }
    }

    private bool TryReserveFiberId(long id)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Fiber id must be positive.", nameof(id));
        }

        // FiberId 会进入 ActorAddress、Location 缓存、延迟消息和外部日志。
        // 同一个 manager 生命周期内不回收 id，可以避免旧消息误投递到新 Fiber。
        if (_usedFiberIds.TryAdd(id, 0))
        {
            return true;
        }

        return false;
    }
}
