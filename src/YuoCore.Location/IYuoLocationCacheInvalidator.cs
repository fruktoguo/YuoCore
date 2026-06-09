namespace YuoCore.Location;

/// <summary>
/// Location 缓存失效接口。当 Sender 检测到地址过时时主动通知缓存层清除过期条目。
/// CachingYuoLocationRegistry 实现了此接口以支持精确失效。
/// </summary>
public interface IYuoLocationCacheInvalidator
{
    /// <summary>
    /// 失效指定业务键的缓存。
    /// 当提供 staleAddress 时，仅当缓存中的地址与该地址匹配时才清除，避免并发刷新后误删新地址。
    /// </summary>
    void Invalidate(YuoLocationKey key, YuoActorAddress? staleAddress = null);
}
