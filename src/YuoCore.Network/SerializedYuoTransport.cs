using YuoCore.Protocol;

namespace YuoCore.Network;

/// <summary>
/// 给任意传输层增加一次真实的 envelope 序列化/反序列化边界。
/// 它主要用于测试和进程内模拟远端：即使底层还是 InMemory transport，上层也不会因为对象引用共享而误判跨进程语义。
/// 真实网络实现可以在 socket 收发处使用相同的 codec，或者替换为更紧凑的 Protobuf frame。
/// </summary>
public sealed class SerializedYuoTransport<TAddress>(
    IYuoTransport<TAddress> innerTransport) : IYuoTransport<TAddress>
    where TAddress : notnull
{
    private readonly IYuoTransport<TAddress> _innerTransport =
        innerTransport ?? throw new ArgumentNullException(nameof(innerTransport));

    public async ValueTask SendAsync(
        TAddress address,
        YuoProtocolEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);

        var decodedEnvelope = RoundTrip(envelope);
        await _innerTransport.SendAsync(address, decodedEnvelope, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<YuoProtocolEnvelope> InvokeAsync(
        TAddress address,
        YuoProtocolEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);

        var decodedRequest = RoundTrip(envelope);
        var response = await _innerTransport.InvokeAsync(address, decodedRequest, cancellationToken).ConfigureAwait(false);

        // 响应也走一遍编解码，避免调用方拿到远端 endpoint 内部复用的 payload 内存。
        return RoundTrip(response);
    }

    private static YuoProtocolEnvelope RoundTrip(YuoProtocolEnvelope envelope)
    {
        var bytes = YuoProtocolEnvelopeCodec.Encode(envelope);
        return YuoProtocolEnvelopeCodec.Decode(bytes);
    }
}
