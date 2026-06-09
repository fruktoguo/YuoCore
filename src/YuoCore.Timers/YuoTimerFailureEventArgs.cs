namespace YuoCore.Timers;

/// <summary>
/// 计时器回调失败时的事件参数。
/// 通过 YuoTimerScheduler.CallbackFailed 事件对外发布。
/// </summary>
public sealed class YuoTimerFailureEventArgs(YuoTimerId timerId, Exception exception) : EventArgs
{
    /// <summary>发生异常的计时器 ID。</summary>
    public YuoTimerId TimerId { get; } = timerId;

    /// <summary>回调中抛出的异常。</summary>
    public Exception Exception { get; } = exception;
}
