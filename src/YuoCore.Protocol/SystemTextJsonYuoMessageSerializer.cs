using System.Text.Json;

namespace YuoCore.Protocol;

/// <summary>
/// 基于 System.Text.Json 的消息序列化实现。
/// 适用于内部传输和测试场景的生产者/消费者模式。
/// </summary>
public sealed class SystemTextJsonYuoMessageSerializer(JsonSerializerOptions? options = null) : IYuoMessageSerializer
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize(object message, Type messageType)
        => JsonSerializer.SerializeToUtf8Bytes(message, messageType, _options);

    /// <inheritdoc />
    public object? Deserialize(ReadOnlyMemory<byte> payload, Type messageType)
        => JsonSerializer.Deserialize(payload.Span, messageType, _options);
}
