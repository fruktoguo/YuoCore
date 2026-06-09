using System.Text.Json;

namespace YuoCore.Actor;

public sealed class SystemTextJsonMessageSerializer(JsonSerializerOptions? options = null) : IMessageSerializer
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public ReadOnlyMemory<byte> Serialize(object message, Type messageType)
    {
        return JsonSerializer.SerializeToUtf8Bytes(message, messageType, _options);
    }

    public object? Deserialize(ReadOnlyMemory<byte> payload, Type messageType)
    {
        return JsonSerializer.Deserialize(payload.Span, messageType, _options);
    }
}
