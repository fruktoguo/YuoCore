namespace YuoCore.Abstractions;

/// <summary>
/// 用于基础设施层返回可预期失败，减少异常被用作普通控制流。
/// 真正的程序错误仍然应该抛异常，让测试和宿主能尽早暴露问题。
/// </summary>
public readonly record struct YuoResult<T>(T? Value, YuoError? Error)
{
    public bool IsSuccess => Error is null;

    public static YuoResult<T> Ok(T value) => new(value, null);

    public static YuoResult<T> Fail(YuoError error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));

        return new(default, error);
    }
}

public readonly record struct YuoResult(YuoError? Error)
{
    public bool IsSuccess => Error is null;

    public static YuoResult Ok() => new(null);

    public static YuoResult Fail(YuoError error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));

        return new(error);
    }
}
