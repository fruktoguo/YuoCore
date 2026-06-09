namespace YuoCore.Protocol;

public enum YuoMessageDirection
{
    ClientToGateway = 0,
    GatewayToClient = 1,
    Internal = 2
}

/// <summary>
/// 协议消息的运行时描述。
/// ET 的 Proto 工具会从 proto 文件生成 opcode 常量和 Message 特性；YuoCore 先保留更轻量的显式注册表，
/// 让 Web、Unity、Gateway 和内部传输可以共享同一套 opcode/type 关系。
/// </summary>
public sealed record YuoMessageDescriptor(
    int OpCode,
    Type MessageType,
    YuoMessageDirection Direction,
    string Name,
    Type? ResponseType = null)
{
    public static YuoMessageDescriptor Create<TMessage>(
        int opCode,
        YuoMessageDirection direction,
        string? name = null,
        Type? responseType = null)
    {
        return new YuoMessageDescriptor(opCode, typeof(TMessage), direction, name ?? typeof(TMessage).Name, responseType);
    }
}

/// <summary>
/// opcode 与消息类型的双向索引。
/// 它不负责序列化，也不依赖 Gateway/Backend；具体 codec 仍由 IYuoMessageSerializer 或 Protobuf 生成类型处理。
/// </summary>
public sealed class YuoMessageRegistry
{
    private readonly Dictionary<int, YuoMessageDescriptor> _byOpCode;
    private readonly Dictionary<Type, YuoMessageDescriptor> _byMessageType;

    private YuoMessageRegistry(
        Dictionary<int, YuoMessageDescriptor> byOpCode,
        Dictionary<Type, YuoMessageDescriptor> byMessageType)
    {
        _byOpCode = byOpCode;
        _byMessageType = byMessageType;
        Descriptors = _byOpCode.Values
            .OrderBy(descriptor => descriptor.OpCode)
            .ToArray();
    }

    public IReadOnlyList<YuoMessageDescriptor> Descriptors { get; }

    public static YuoMessageRegistry Create(IEnumerable<YuoMessageDescriptor> descriptors)
    {
        if (descriptors is null) throw new ArgumentNullException(nameof(descriptors));

        var byOpCode = new Dictionary<int, YuoMessageDescriptor>();
        var byType = new Dictionary<Type, YuoMessageDescriptor>();

        foreach (var descriptor in descriptors)
        {
            if (descriptor is null) throw new ArgumentNullException(nameof(descriptors));
            if (descriptor.MessageType is null) throw new ArgumentNullException(nameof(descriptors));

            if (descriptor.OpCode <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptors), descriptor.OpCode, "Protocol opcode must be positive.");
            }

            if (!IsKnownDirection(descriptor.Direction))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(descriptors),
                    descriptor.Direction,
                    "Protocol message direction is not supported.");
            }

            if (!byOpCode.TryAdd(descriptor.OpCode, descriptor))
            {
                var existing = byOpCode[descriptor.OpCode];
                throw new InvalidOperationException(
                    $"Duplicate protocol opcode {descriptor.OpCode}: {existing.Name}, {descriptor.Name}.");
            }

            if (!byType.TryAdd(descriptor.MessageType, descriptor))
            {
                var existing = byType[descriptor.MessageType];
                throw new InvalidOperationException(
                    $"Duplicate protocol message type {descriptor.MessageType.FullName}: {existing.OpCode}, {descriptor.OpCode}.");
            }
        }

        return new YuoMessageRegistry(byOpCode, byType);
    }

    private static bool IsKnownDirection(YuoMessageDirection direction)
    {
        return direction is YuoMessageDirection.ClientToGateway
            or YuoMessageDirection.GatewayToClient
            or YuoMessageDirection.Internal;
    }

    public bool TryGetByOpCode(int opCode, out YuoMessageDescriptor descriptor)
    {
        return _byOpCode.TryGetValue(opCode, out descriptor!);
    }

    public bool TryGetByMessageType(Type messageType, out YuoMessageDescriptor descriptor)
    {
        if (messageType is null) throw new ArgumentNullException(nameof(messageType));
        return _byMessageType.TryGetValue(messageType, out descriptor!);
    }

    public YuoMessageDescriptor GetByOpCode(int opCode)
    {
        return TryGetByOpCode(opCode, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Protocol opcode {opCode} is not registered.");
    }

    public YuoMessageDescriptor GetByMessageType(Type messageType)
    {
        return TryGetByMessageType(messageType, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException($"Protocol message type {messageType.FullName} is not registered.");
    }
}
