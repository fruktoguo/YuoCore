namespace YuoCore.Abstractions;

/// <summary>
/// 跨模块可预期业务失败的异常包装。
/// 基础设施层可以捕获它并把 YuoError 原样转成协议错误；普通程序错误仍应使用普通异常暴露。
/// </summary>
public sealed class YuoException : Exception
{
    public YuoException(YuoError error)
        : base(error?.Message ?? throw new ArgumentNullException(nameof(error)))
    {
        Error = error;
    }

    public YuoException(YuoError error, Exception innerException)
        : base(error?.Message ?? throw new ArgumentNullException(nameof(error)), innerException)
    {
        Error = error;
    }

    public YuoError Error { get; }
}
