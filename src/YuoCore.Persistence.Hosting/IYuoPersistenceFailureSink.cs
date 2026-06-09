namespace YuoCore.Persistence.Hosting;

/// <summary>
/// 持久化后台写回的可选失败出口。
/// 宿主可以在这里接入结构化日志、指标、重试队列或 dead letter；
/// Hosting 包只暴露抽象，不强制核心 Persistence 依赖具体日志/数据库实现。
/// </summary>
public interface IYuoPersistenceFailureSink
{
    ValueTask OnFailureAsync(
        YuoPersistenceCommand command,
        Exception exception,
        CancellationToken cancellationToken = default);
}
