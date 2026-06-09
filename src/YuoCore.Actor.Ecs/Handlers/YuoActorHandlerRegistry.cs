using System.Reflection;

namespace YuoCore.Actor;

public sealed record YuoActorHandlerScanReport(
    string AssemblyName,
    int CandidateHandlerTypes,
    int RegisteredHandlers,
    IReadOnlyList<Type> SkippedHandlerTypes);

public sealed class YuoActorHandlerRegistry
{
    private readonly Dictionary<Type, IYuoActorHandler> _handlers = new();

    public IReadOnlyDictionary<Type, IYuoActorHandler> Handlers => _handlers;

    public YuoActorHandlerRegistry Add(IYuoActorHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateHandlerContract(handler);

        if (_handlers.TryGetValue(handler.MessageType, out var existing))
        {
            throw new InvalidOperationException(
                $"Duplicate actor handler for {handler.MessageType.FullName}: {existing.GetType().FullName}, {handler.GetType().FullName}.");
        }

        _handlers.Add(handler.MessageType, handler);
        return this;
    }

    public YuoActorHandlerRegistry AddFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        AddFromAssemblyWithReport(assembly, CreateParameterlessHandler);
        return this;
    }

    public YuoActorHandlerRegistry AddFromAssembly(
        Assembly assembly,
        Func<Type, IYuoActorHandler?> handlerFactory)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(handlerFactory);

        AddFromAssemblyWithReport(assembly, handlerFactory);
        return this;
    }

    public YuoActorHandlerRegistry AddFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return AddFromAssemblies((IEnumerable<Assembly>)assemblies);
    }

    public YuoActorHandlerRegistry AddFromAssemblies(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
        {
            AddFromAssembly(assembly);
        }

        return this;
    }

    public IReadOnlyList<YuoActorHandlerScanReport> AddFromAssembliesWithReport(
        IEnumerable<Assembly> assemblies,
        Func<Type, IYuoActorHandler?>? handlerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var reports = new List<YuoActorHandlerScanReport>();
        foreach (var assembly in assemblies)
        {
            reports.Add(AddFromAssemblyWithReport(
                assembly,
                handlerFactory ?? CreateParameterlessHandler));
        }

        return reports;
    }

    public YuoActorHandlerScanReport AddFromAssemblyWithReport(Assembly assembly)
    {
        return AddFromAssemblyWithReport(assembly, CreateParameterlessHandler);
    }

    public YuoActorHandlerScanReport AddFromAssemblyWithReport(
        Assembly assembly,
        Func<Type, IYuoActorHandler?> handlerFactory)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(handlerFactory);

        var candidates = 0;
        var registered = 0;
        var skipped = new List<Type>();

        // Handler 扫描只建议在启动阶段执行；运行中不要热修改这个表。
        // 这样 dispatcher 的读取路径可以保持无锁字典查询，避免每条消息都付同步成本。
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IYuoActorHandler).IsAssignableFrom(type))
            {
                continue;
            }

            candidates++;
            if (handlerFactory(type) is { } handler)
            {
                Add(handler);
                registered++;
            }
            else
            {
                skipped.Add(type);
            }
        }

        return new YuoActorHandlerScanReport(
            assembly.GetName().Name ?? assembly.FullName ?? string.Empty,
            candidates,
            registered,
            skipped);
    }

    private static IYuoActorHandler? CreateParameterlessHandler(Type type)
    {
        return type.GetConstructor(Type.EmptyTypes) is null
            ? null
            : Activator.CreateInstance(type) as IYuoActorHandler;
    }

    private static void ValidateHandlerContract(IYuoActorHandler handler)
    {
        var messageType = handler.MessageType;
        if (messageType is null)
        {
            throw new InvalidOperationException(
                $"Actor handler {handler.GetType().FullName} must declare a message type.");
        }

        if (!typeof(IActorMessage).IsAssignableFrom(messageType))
        {
            throw new InvalidOperationException(
                $"Actor handler {handler.GetType().FullName} message type {messageType.FullName} must implement {typeof(IActorMessage).FullName}.");
        }

        var requestResponseType = TryGetActorRequestResponseType(messageType);
        if (requestResponseType is null)
        {
            if (handler.ResponseType is not null)
            {
                throw new InvalidOperationException(
                    $"Actor handler {handler.GetType().FullName} declares response type {handler.ResponseType.FullName}, but message {messageType.FullName} is not an actor request.");
            }

            return;
        }

        if (handler.ResponseType is null)
        {
            throw new InvalidOperationException(
                $"Actor request handler {handler.GetType().FullName} for {messageType.FullName} must declare response type {requestResponseType.FullName}.");
        }

        if (handler.ResponseType != requestResponseType)
        {
            throw new InvalidOperationException(
                $"Actor handler {handler.GetType().FullName} response type mismatch for {messageType.FullName}. Expected {requestResponseType.FullName}, got {handler.ResponseType.FullName}.");
        }
    }

    private static Type? TryGetActorRequestResponseType(Type messageType)
    {
        // IActorRequest<TResponse> 是请求/响应契约的唯一来源。
        // 自定义 IYuoActorHandler 也必须和这个契约一致，避免启动成功、运行时才发生响应类型错配。
        foreach (var interfaceType in messageType.GetInterfaces())
        {
            if (interfaceType.IsGenericType &&
                interfaceType.GetGenericTypeDefinition() == typeof(IActorRequest<>))
            {
                return interfaceType.GetGenericArguments()[0];
            }
        }

        return null;
    }

    public bool TryGet(Type messageType, out IYuoActorHandler handler)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        return _handlers.TryGetValue(messageType, out handler!);
    }
}
