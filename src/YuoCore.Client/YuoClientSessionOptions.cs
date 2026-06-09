namespace YuoCore.Client;

/// <summary>
/// Web/Unity 客户端协议会话的轻量配置。
/// 这里不绑定具体网络库，超时只通过 CancellationToken 传给外部 transport。
/// </summary>
public sealed class YuoClientSessionOptions
{
    public static YuoClientSessionOptions Default { get; } = new();

    /// <summary>
    /// 单次请求的默认超时时间。
    /// null 或 Timeout.InfiniteTimeSpan 表示不由会话层主动超时，交给调用方 cancellationToken 控制。
    /// </summary>
    public TimeSpan? RequestTimeout { get; init; }
}
