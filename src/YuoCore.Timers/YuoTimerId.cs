namespace YuoCore.Timers;

/// <summary>
/// 计时器唯一标识符。
/// 由 YuoTimerScheduler 内部递增分配，用于取消和查询计时器。
/// </summary>
public readonly record struct YuoTimerId(long Value)
{
    /// <summary>空计时器 ID（值为 0 的默认实例）。</summary>
    public static YuoTimerId Empty { get; } = new(0);

    /// <summary>是否为空的计时器 ID。</summary>
    public bool IsEmpty => Value == 0;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
