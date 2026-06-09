namespace YuoCore.Tasks;

/// <summary>
/// 统一接收 fire-and-forget YuoTask 中冒出的异常。
/// 这相当于 ETTask.Coroutine 语义里的兜底错误出口，避免 async void 异常直接打穿运行时。
/// </summary>
public static class YuoTaskExceptionHandler
{
    /// <summary>
    /// 当通过 Forget/Coroutine 启动的 YuoTask 失败时触发。
    /// 宿主可在初始化时注入自定义的异常处理逻辑（如记录日志、上报指标等）。
    /// </summary>
    public static Action<Exception>? UnhandledException { get; set; }

    /// <summary>
    /// 发布异常到已注册的全局处理器。
    /// 处理器本身的异常会被吞掉，防止破坏 fire-and-forget 续体路径。
    /// </summary>
    internal static void Publish(Exception exception)
    {
        var handler = UnhandledException;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(exception);
        }
        catch
        {
            // 异常处理器本身不能反过来破坏 fire-and-forget 续体路径。
        }
    }
}
