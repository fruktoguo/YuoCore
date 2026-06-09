using YuoCore.Abstractions;

namespace YuoCore.Protocol;

/// <summary>
/// 协议消息的类型分类。
/// </summary>
public enum YuoMessageKind
{
    /// <summary>单向消息（fire-and-forget）。</summary>
    Message = 0,
    /// <summary>需要响应的请求（RPC）。</summary>
    Request = 1,
    /// <summary>RPC 的成功响应。</summary>
    Response = 2,
    /// <summary>RPC 的错误响应。</summary>
    Error = 3
}

/// <summary>
/// 服务端内部通信的通用封包头。
/// 业务 payload 由具体 serializer 负责，这个类型只表达路由、关联请求和协议版本。
/// </summary>
public sealed record YuoProtocolEnvelope(
    ushort Version,
    int OpCode,
    YuoMessageKind Kind,
    long TargetId,
    long SenderId,
    Guid RequestId,
    ReadOnlyMemory<byte> Payload,
    YuoError? Error = null)
{
    /// <summary>创建单向消息信封。</summary>
    public static YuoProtocolEnvelope Message(int opCode, long targetId, ReadOnlyMemory<byte> payload, long senderId = 0)
        => new(1, opCode, YuoMessageKind.Message, targetId, senderId, Guid.Empty, payload);

    /// <summary>创建 RPC 请求信封。</summary>
    public static YuoProtocolEnvelope Request(int opCode, long targetId, ReadOnlyMemory<byte> payload, long senderId = 0)
        => new(1, opCode, YuoMessageKind.Request, targetId, senderId, Guid.NewGuid(), payload);

    /// <summary>基于请求信封创建成功响应。</summary>
    public static YuoProtocolEnvelope Response(YuoProtocolEnvelope request, ReadOnlyMemory<byte> payload)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return request with
        {
            Kind = YuoMessageKind.Response,
            TargetId = request.SenderId,
            SenderId = request.TargetId,
            Payload = payload,
            Error = null
        };
    }

    /// <summary>基于请求信封创建错误响应。</summary>
    public static YuoProtocolEnvelope Failure(YuoProtocolEnvelope request, YuoError error)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (error is null) throw new ArgumentNullException(nameof(error));
        return request with
        {
            Kind = YuoMessageKind.Error,
            TargetId = request.SenderId,
            SenderId = request.TargetId,
            Payload = ReadOnlyMemory<byte>.Empty,
            Error = error
        };
    }
}
