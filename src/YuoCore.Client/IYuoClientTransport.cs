namespace YuoCore.Client;

/// <summary>
/// 客户端 SDK 的字节传输抽象。
/// Unity 可以在这里接 WebSocket/TCP/KCP，Web 的 TypeScript SDK 也应保持同样的 request/response 语义。
/// </summary>
public interface IYuoClientTransport
{
    ValueTask<ReadOnlyMemory<byte>> SendAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken = default);
}
