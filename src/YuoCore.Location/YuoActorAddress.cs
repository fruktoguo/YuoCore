namespace YuoCore.Location;

/// <summary>
/// 可跨 Fiber、跨进程传递的 Actor 地址。
/// ActorId 是运行时对象身份，Node/Process/Fiber 用于把消息路由到当前所有者。
/// </summary>
public readonly record struct YuoActorAddress(
    string NodeId,
    string ProcessId,
    long FiberId,
    long ActorId,
    string? Endpoint = null)
{
    public override string ToString()
    {
        var route = $"{NodeId}/{ProcessId}/{FiberId}/{ActorId}";
        return string.IsNullOrWhiteSpace(Endpoint) ? route : $"{route}@{Endpoint}";
    }
}
