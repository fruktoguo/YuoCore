using System.Reflection;
using YuoCore.Actor;
using YuoCore.Config;
using YuoCore.Ecs;
using YuoCore.Location;
using YuoCore.Runtime;

namespace YuoCore.Server;

/// <summary>
/// 通用进程宿主：按 StartConfig 创建 Fiber，并持有进程级基础设施。
/// ASP.NET Core、控制台进程、Unity 前端都可以选择是否使用它；不用它时直接 new YuoCoreRuntime 也仍然可行。
/// </summary>
public sealed class YuoServerHost : IAsyncDisposable, IDisposable
{
    private readonly YuoStartConfig _config;
    private readonly Dictionary<long, YuoFiberActorEndpoint> _actorEndpoints = new();
    private readonly List<YuoActorHandlerScanReport> _actorHandlerScanReports = new();
    private bool _started;

    public YuoServerHost(YuoStartConfig config, IYuoLocationRegistry? locationRegistry = null)
    {
        var validation = YuoStartConfigLoader.Validate(config);
        if (!validation.IsSuccess)
        {
            throw new InvalidOperationException(validation.Error!.Message);
        }

        _config = config;
        Location = locationRegistry ?? new CachingYuoLocationRegistry(new InMemoryYuoLocationRegistry());
        ActorLocations = new YuoLocationActorRegistry(Location);
        ActorRouter = new ActorRouter(ActorLocations, ActorTransport);
    }

    public YuoCoreRuntime Runtime { get; } = new();

    public IYuoLocationRegistry Location { get; }

    public YuoLocationActorRegistry ActorLocations { get; }

    public InMemoryActorTransport ActorTransport { get; } = new();

    public YuoEntityActorRegistry ActorEntities { get; } = new();

    public YuoActorHandlerRegistry ActorHandlers { get; } = new();

    public IReadOnlyList<YuoActorHandlerScanReport> ActorHandlerScanReports => _actorHandlerScanReports;

    public ActorRouter ActorRouter { get; }

    public IReadOnlyList<YuoFiber> Fibers => Runtime.Fibers.Fibers.ToArray();

    public YuoActorHandlerScanReport AddActorHandlersFromAssembly(
        Assembly assembly,
        Func<Type, IYuoActorHandler?>? handlerFactory = null)
    {
        if (_started)
        {
            throw new InvalidOperationException("Actor handlers must be registered before YuoServerHost starts.");
        }

        var report = handlerFactory is null
            ? ActorHandlers.AddFromAssemblyWithReport(assembly)
            : ActorHandlers.AddFromAssemblyWithReport(assembly, handlerFactory);
        _actorHandlerScanReports.Add(report);
        return report;
    }

    public IReadOnlyList<YuoActorHandlerScanReport> AddActorHandlersFromAssemblies(
        IEnumerable<Assembly> assemblies,
        Func<Type, IYuoActorHandler?>? handlerFactory = null)
    {
        if (_started)
        {
            throw new InvalidOperationException("Actor handlers must be registered before YuoServerHost starts.");
        }

        var reports = ActorHandlers.AddFromAssembliesWithReport(assemblies, handlerFactory);
        _actorHandlerScanReports.AddRange(reports);
        return reports;
    }

    public void Start()
    {
        if (_started) return;

        foreach (var fiber in _config.Fibers)
        {
            var yuoFiber = Runtime.Fibers.CreateFiber(new YuoFiberOptions
            {
                Id = fiber.Id,
                Name = fiber.Name,
                SetWorldAsDefault = fiber.SetWorldAsDefault,
                MaxMailboxMessagesPerTick = fiber.MaxMailboxMessagesPerTick
            });

            // 每个 Fiber 创建一个 Actor endpoint，但不等于每个 Actor 一个线程。
            // endpoint 只负责把消息排入这个 Fiber 自己的 mailbox，后续仍由 Fiber Tick 串行进入 ECS。
            _actorEndpoints[yuoFiber.Id] = new YuoFiberActorEndpoint(
                yuoFiber,
                new YuoEntityActorDispatcher(ActorEntities, ActorHandlers));
        }

        _started = true;
    }

    public async ValueTask<ActorAddress> RegisterLocalEntityActorAsync(
        ActorId actorId,
        YuoEntity entity,
        YuoLocationKey? locationKey = null,
        CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException("YuoServerHost has not started.");
        if (entity.World is null) throw new ArgumentException("Entity must belong to a YuoWorld.", nameof(entity));
        var fiber = _actorEndpoints.Keys
            .Select(id => Runtime.Fibers.TryGetFiber(id, out var current) ? current : null)
            .FirstOrDefault(current => current?.World == entity.World);
        if (fiber is null)
        {
            throw new InvalidOperationException($"No YuoFiber owns entity world for actor {actorId}.");
        }

        var endpoint = _actorEndpoints[fiber.Id];
        var address = new ActorAddress(_config.NodeId, checked((int)fiber.Id), actorId);
        ActorEntities.Register(actorId, entity);
        ActorTransport.RegisterEndpoint(address, endpoint);
        try
        {
            await ActorLocations.RegisterAsync(actorId, address, cancellationToken).ConfigureAwait(false);

            if (locationKey is not null)
            {
                var registered = await Location.RegisterAsync(
                    locationKey.Value,
                    new YuoActorAddress(_config.NodeId, _config.ProcessId, fiber.Id, actorId.Value),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!registered.IsSuccess)
                {
                    throw new InvalidOperationException(registered.Error!.Message);
                }
            }
        }
        catch
        {
            // Actor 暴露给其他模块前需要本地 endpoint、ActorLocation 和业务 Location 保持一致。
            // 任一步失败都回滚本地表和已发布的 actor location，避免留下半注册 actor。
            ActorEntities.Unregister(actorId);
            ActorTransport.UnregisterEndpoint(address);
            await ActorLocations.UnregisterAsync(actorId, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return address;
    }

    public ValueTask TickAsync(CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException("YuoServerHost has not started.");
        return Runtime.TickAsync(cancellationToken);
    }

    public void Dispose()
    {
        Runtime.Dispose();
        _actorEndpoints.Clear();
        _started = false;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
