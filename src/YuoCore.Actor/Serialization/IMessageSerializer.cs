namespace YuoCore.Actor;

public interface IMessageSerializer
{
    ReadOnlyMemory<byte> Serialize(object message, Type messageType);

    object? Deserialize(ReadOnlyMemory<byte> payload, Type messageType);
}
