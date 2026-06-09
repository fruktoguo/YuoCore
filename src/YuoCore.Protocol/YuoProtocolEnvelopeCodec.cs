using System.Text.Json;
using YuoCore.Abstractions;

namespace YuoCore.Protocol;

/// <summary>
/// YuoProtocolEnvelope 的二进制编解码工具。
/// 这里先使用稳定、易排查的 JSON frame 承载 envelope 元信息，payload 本身仍然是业务协议 bytes。
/// 后续如果内部传输切到 Protobuf/自定义二进制格式，只需要替换这一层，不影响 Actor、Location 和 Fiber。
/// </summary>
public static class YuoProtocolEnvelopeCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static byte[] Encode(YuoProtocolEnvelope envelope)
    {
        if (envelope is null) throw new ArgumentNullException(nameof(envelope));

        var frame = new EnvelopeFrame(
            envelope.Version,
            envelope.OpCode,
            envelope.Kind,
            envelope.TargetId,
            envelope.SenderId,
            envelope.RequestId,
            envelope.Payload.ToArray(),
            envelope.Error);

        return JsonSerializer.SerializeToUtf8Bytes(frame, Options);
    }

    public static YuoProtocolEnvelope Decode(ReadOnlySpan<byte> bytes)
    {
        var frame = JsonSerializer.Deserialize<EnvelopeFrame>(bytes, Options)
            ?? throw new InvalidOperationException("Failed to decode YuoProtocolEnvelope.");

        ValidateFrame(frame);

        return new YuoProtocolEnvelope(
            frame.Version,
            frame.OpCode,
            frame.Kind,
            frame.TargetId,
            frame.SenderId,
            frame.RequestId,
            frame.Payload ?? Array.Empty<byte>(),
            frame.Error);
    }

    private static void ValidateFrame(EnvelopeFrame frame)
    {
        if (frame.Version == 0)
        {
            throw new InvalidOperationException("YuoProtocolEnvelope version is required.");
        }

        if (!IsKnownKind(frame.Kind))
        {
            throw new InvalidOperationException($"Unknown YuoProtocolEnvelope message kind: {(int)frame.Kind}.");
        }
    }

    private static bool IsKnownKind(YuoMessageKind kind)
    {
        return kind is YuoMessageKind.Message
            or YuoMessageKind.Request
            or YuoMessageKind.Response
            or YuoMessageKind.Error;
    }

    private sealed record EnvelopeFrame(
        ushort Version,
        int OpCode,
        YuoMessageKind Kind,
        long TargetId,
        long SenderId,
        Guid RequestId,
        byte[]? Payload,
        YuoError? Error);
}
