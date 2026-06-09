using Google.Protobuf;
using YuoCore.Protocol;

namespace YuoCore.Client;

/// <summary>
/// Web/Unity 共用的 V1 客户端协议会话。
/// 它只负责 requestId、clientSeq、opcode 查表和 Protobuf envelope 编解码，不拥有网络连接。
/// </summary>
public sealed class YuoClientSession
{
    private readonly YuoMessageRegistry _registry;
    private readonly YuoClientSessionOptions _options;
    private long _nextRequestId;
    private long _nextClientSeq;

    public YuoClientSession(YuoClientSessionOptions options)
        : this(null, options)
    {
    }

    public YuoClientSession(
        YuoMessageRegistry? registry = null,
        YuoClientSessionOptions? options = null)
    {
        _registry = registry ?? YuoCoreProtocolCatalog.CreateRegistry();
        _options = options ?? YuoClientSessionOptions.Default;
    }

    public YuoClientOutboundMessage EncodeRequest<TRequest>(TRequest request)
        where TRequest : IMessage<TRequest>
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var descriptor = _registry.GetByMessageType(typeof(TRequest));
        if (descriptor.Direction != YuoMessageDirection.ClientToGateway)
        {
            throw new InvalidOperationException(
                $"Message {descriptor.Name} is not a client-to-gateway request.");
        }

        var requestId = checked((ulong)Interlocked.Increment(ref _nextRequestId));
        var clientSeq = checked((ulong)Interlocked.Increment(ref _nextClientSeq));
        var envelope = new YuoClientEnvelopeV1
        {
            Version = 1,
            Opcode = checked((uint)descriptor.OpCode),
            RequestId = requestId,
            ClientSeq = clientSeq,
            Payload = ByteString.CopyFrom(request.ToByteArray())
        };

        return new YuoClientOutboundMessage(
            requestId,
            clientSeq,
            descriptor.OpCode,
            envelope.ToByteArray());
    }

    public YuoClientInboundMessage<TResponse> DecodeResponse<TResponse>(
        ReadOnlyMemory<byte> responseBytes,
        MessageParser<TResponse> responseParser,
        ulong expectedRequestId = 0)
        where TResponse : IMessage<TResponse>
    {
        if (responseParser is null) throw new ArgumentNullException(nameof(responseParser));

        var envelope = YuoServerEnvelopeV1.Parser.ParseFrom(responseBytes.Span);
        if (expectedRequestId != 0 && envelope.RequestId != expectedRequestId)
        {
            throw new YuoClientProtocolException(
                "request_mismatch",
                $"Expected response for request {expectedRequestId}, got {envelope.RequestId}.");
        }

        if (envelope.Error is { Code.Length: > 0 })
        {
            throw new YuoClientProtocolException(envelope.Error.Code, envelope.Error.Message);
        }

        var descriptor = _registry.GetByMessageType(typeof(TResponse));
        if (envelope.Opcode != descriptor.OpCode)
        {
            throw new YuoClientProtocolException(
                "opcode_mismatch",
                $"Expected opcode {descriptor.OpCode} for {descriptor.Name}, got {envelope.Opcode}.");
        }

        return new YuoClientInboundMessage<TResponse>(
            envelope.RequestId,
            envelope.ServerSeq,
            checked((int)envelope.Opcode),
            responseParser.ParseFrom(envelope.Payload));
    }

    public async ValueTask<YuoClientInboundMessage<TResponse>> CallAsync<TRequest, TResponse>(
        IYuoClientTransport transport,
        TRequest request,
        MessageParser<TResponse> responseParser,
        CancellationToken cancellationToken = default)
        where TRequest : IMessage<TRequest>
        where TResponse : IMessage<TResponse>
    {
        if (transport is null) throw new ArgumentNullException(nameof(transport));

        var requestDescriptor = _registry.GetByMessageType(typeof(TRequest));
        if (requestDescriptor.ResponseType is not null &&
            requestDescriptor.ResponseType != typeof(TResponse))
        {
            throw new InvalidOperationException(
                $"Request {requestDescriptor.Name} expects response {requestDescriptor.ResponseType.FullName}, not {typeof(TResponse).FullName}.");
        }

        var outbound = EncodeRequest(request);
        using var timeoutSource = CreateTimeoutSource(cancellationToken);
        var sendToken = timeoutSource?.Token ?? cancellationToken;
        var response = await transport.SendAsync(outbound.Bytes, sendToken).ConfigureAwait(false);
        return DecodeResponse(response, responseParser, outbound.RequestId);
    }

    private CancellationTokenSource? CreateTimeoutSource(CancellationToken cancellationToken)
    {
        var timeout = _options.RequestTimeout;
        if (timeout is null || timeout == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("RequestTimeout must be positive, null, or Timeout.InfiniteTimeSpan.");
        }

        var source = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        source.CancelAfter(timeout.Value);
        return source;
    }
}
