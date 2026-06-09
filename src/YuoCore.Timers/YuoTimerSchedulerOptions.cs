namespace YuoCore.Timers;

/// <summary>
/// YuoTimerScheduler 的配置选项。
/// </summary>
/// <param name="StopOnCallbackError">当设为 true 时，回调抛出的任何异常都会导致 UpdateAsync 直接抛出，停止后续计时器处理。</param>
/// <param name="ContinueRepeatedTimerAfterError">当设为 true 且 StopOnCallbackError 为 false 时，重复计时器在回调失败后继续运行。</param>
/// <param name="MaxDueTimersPerUpdate">每次 UpdateAsync 最多处理的到期计时器数量。为 null 表示无限制。</param>
public sealed record YuoTimerSchedulerOptions(
    bool StopOnCallbackError = false,
    bool ContinueRepeatedTimerAfterError = true,
    int? MaxDueTimersPerUpdate = null);
