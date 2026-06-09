namespace YuoCore.Actor;

/// <summary>
/// Actor 的网络位置地址，包含节点、Fiber、Actor 三级路由信息。
/// 用于在分布式环境中唯一定位一个 Actor 实例。
/// </summary>
/// <param name="NodeId">节点标识（如 "login-server-1"）。</param>
/// <param name="FiberId">所在 Fiber 的 ID。</param>
/// <param name="ActorId">Actor 实例 ID。</param>
/// <param name="Endpoint">可选的端点信息（如 "tcp://192.168.1.1:3000"），为空时使用默认传输。</param>
public readonly record struct ActorAddress(
    string NodeId,
    int FiberId,
    ActorId ActorId,
    string? Endpoint = null)
{
    /// <summary>格式：NodeId/FiberId/ActorId[@Endpoint]。</summary>
    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Endpoint)
            ? $"{NodeId}/{FiberId}/{ActorId.Value}"
            : $"{NodeId}/{FiberId}/{ActorId.Value}@{Endpoint}";
    }
}
