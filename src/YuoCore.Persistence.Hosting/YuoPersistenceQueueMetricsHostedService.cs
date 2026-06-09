using Microsoft.Extensions.Hosting;

namespace YuoCore.Persistence.Hosting;

/// <summary>
/// 激活 PersistenceQueue 指标的宿主生命周期适配器。
/// 指标本身通过 .NET Meter 暴露；这个服务只负责让 DI 在宿主启动时创建指标对象。
/// </summary>
public sealed class YuoPersistenceQueueMetricsHostedService(
    YuoPersistenceQueueMetrics metrics) : IHostedService
{
    private readonly YuoPersistenceQueueMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _metrics;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
