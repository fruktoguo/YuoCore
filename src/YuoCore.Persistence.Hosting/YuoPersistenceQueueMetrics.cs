using System.Diagnostics.Metrics;

namespace YuoCore.Persistence.Hosting;

/// <summary>
/// PersistenceQueue 的可选指标适配器。
/// 这里仅使用 .NET 标准 Meter，不直接引用 OpenTelemetry 或 Prometheus；
/// ASP.NET Core 宿主可以通过 AddMeter(MeterName) 决定是否导出这些指标。
/// </summary>
public sealed class YuoPersistenceQueueMetrics : IDisposable
{
    public const string MeterName = "YuoCore.Persistence";
    public const string PendingCountInstrument = "yuocore.persistence.queue.pending";
    public const string HighWatermarkInstrument = "yuocore.persistence.queue.high_watermark";
    public const string TotalEnqueuedInstrument = "yuocore.persistence.queue.enqueued";
    public const string TotalDequeuedInstrument = "yuocore.persistence.queue.dequeued";
    public const string BacklogRatioInstrument = "yuocore.persistence.queue.backlog_ratio";

    private readonly YuoPersistenceQueue _queue;
    private readonly Meter _meter;

    public YuoPersistenceQueueMetrics(YuoPersistenceQueue queue, string? meterName = null)
    {
        ArgumentNullException.ThrowIfNull(queue);

        _queue = queue;
        _meter = new Meter(meterName ?? MeterName);
        _meter.CreateObservableGauge(
            PendingCountInstrument,
            ObservePendingCount,
            "{command}",
            "当前等待写回的持久化命令数量。");
        _meter.CreateObservableGauge(
            HighWatermarkInstrument,
            ObserveHighWatermark,
            "{command}",
            "进程启动以来持久化队列积压峰值。");
        _meter.CreateObservableCounter(
            TotalEnqueuedInstrument,
            ObserveTotalEnqueued,
            "{command}",
            "进程启动以来写入持久化队列的命令总数。");
        _meter.CreateObservableCounter(
            TotalDequeuedInstrument,
            ObserveTotalDequeued,
            "{command}",
            "进程启动以来从持久化队列取出的命令总数。");
        _meter.CreateObservableGauge(
            BacklogRatioInstrument,
            ObserveBacklogRatio,
            "1",
            "当前积压数量占队列容量的比例，范围为 0 到 1。");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private long ObservePendingCount()
    {
        return _queue.GetStats().PendingCount;
    }

    private long ObserveHighWatermark()
    {
        return _queue.GetStats().HighWatermark;
    }

    private long ObserveTotalEnqueued()
    {
        return _queue.GetStats().TotalEnqueued;
    }

    private long ObserveTotalDequeued()
    {
        return _queue.GetStats().TotalDequeued;
    }

    private double ObserveBacklogRatio()
    {
        return _queue.GetStats().BacklogRatio;
    }
}
