namespace YuoCore.Actor;

/// <summary>
/// Actor 位置注册表接口，负责 ActorId 到 ActorAddress 的注册、解析和注销。
/// 在分布式场景中，Location Registry 是路由的核心依赖。
/// </summary>
public interface IActorLocationRegistry
{
    /// <summary>注册 Actor 到指定地址。</summary>
    ValueTask RegisterAsync(ActorId actorId, ActorAddress address, CancellationToken cancellationToken = default);

    /// <summary>解析 Actor 的当前地址。返回 null 表示未注册。</summary>
    ValueTask<ActorAddress?> ResolveAsync(ActorId actorId, CancellationToken cancellationToken = default);

    /// <summary>注销 Actor 的地址绑定。</summary>
    ValueTask UnregisterAsync(ActorId actorId, CancellationToken cancellationToken = default);
}
