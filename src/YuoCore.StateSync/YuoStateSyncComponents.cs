using YuoCore.Ecs;

namespace YuoCore.StateSync;

/// <summary>
/// 网络同步身份。NetworkId 是客户端状态表里的稳定 key，不一定等于 ECS EntityId。
/// 这样服务端可以自由重建 Entity，只要 NetworkId 稳定，客户端表现层就能继续复用对象。
/// </summary>
public sealed class YuoNetworkIdentityComponent : YuoComponent
{
    public long NetworkId;
    public string Kind = string.Empty;
}

/// <summary>
/// 已序列化的网络状态缓存。
/// 这里先用 bytes 保存状态，避免 StateSync 项目依赖具体战斗、移动或背包组件。
/// 后续可以用源生成器或组件 writer 把具体 Component 转成 payload。
/// </summary>
public sealed class YuoNetworkStateComponent : YuoComponent
{
    public byte[] Payload = [];
    public uint Version;
    public bool IsDirty = true;

    public void SetPayload(ReadOnlySpan<byte> payload)
    {
        Payload = payload.ToArray();
        Version++;
        IsDirty = true;
    }

    /// <summary>
    /// 直接接管调用方生成好的 payload 数组。
    /// 调用后外部不能再修改该数组；需要保留复制隔离时继续使用 SetPayload。
    /// </summary>
    public void SetPayloadOwned(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Payload = payload;
        Version++;
        IsDirty = true;
    }

    public void ClearDirty()
    {
        IsDirty = false;
    }
}

public sealed class YuoObservedByComponent : YuoComponent
{
    public HashSet<long> ObserverNetworkIds { get; } = new();
}
