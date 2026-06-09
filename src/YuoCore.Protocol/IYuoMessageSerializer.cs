namespace YuoCore.Protocol;

/// <summary>
/// 内部消息序列化契约。Raw WebSocket 的 Protobuf DTO 可以继续放在具体 Backend，
/// 框架层只需要知道"对象如何变成 bytes、bytes 如何回到对象"。
/// </summary>
public interface IYuoMessageSerializer
{
    /// <summary>将消息对象序列化为字节数组。</summary>
    ReadOnlyMemory<byte> Serialize(object message, Type messageType);

    /// <summary>将字节数组反序列化为消息对象。</summary>
    object? Deserialize(ReadOnlyMemory<byte> payload, Type messageType);
}
