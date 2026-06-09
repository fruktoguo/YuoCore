namespace YuoCore.Actor;

/// <summary>
/// Actor 框架中的标准化错误对象。
/// 跨网络传递时保持结构一致，不依赖异常堆栈。
/// </summary>
/// <param name="Code">错误码（如 404、408、500）。</param>
/// <param name="Message">人类可读的错误描述。</param>
/// <param name="Detail">可选的详细错误信息（如堆栈或内部异常详情）。</param>
public sealed record ActorError(int Code, string Message, string? Detail = null)
{
    /// <summary>
    /// 稳定字符串错误码。
    /// 数字 Code 用于快速分组，ErrorCode 用于业务协议、日志、指标和客户端判断。
    /// </summary>
    public string ErrorCode { get; init; } = ActorErrorCodes.InternalError;

    /// <summary>创建错误请求（400），表示业务消息参数无效。</summary>
    public static ActorError BadRequest(string message = "Actor request is invalid.")
        => Create(400, ActorErrorCodes.BadRequest, message);

    /// <summary>创建超时错误（408）。</summary>
    public static ActorError Timeout(string message = "Actor request timed out.")
        => Create(408, ActorErrorCodes.Timeout, message);

    /// <summary>创建未找到错误（404），表示 Actor 或地址不存在。</summary>
    public static ActorError NotFound(string message = "Actor was not found.")
        => Create(404, ActorErrorCodes.NotFound, message);

    /// <summary>创建冲突错误（409），表示 Actor 暂时不可用或地址已过期。</summary>
    public static ActorError Conflict(string message = "Actor is temporarily unavailable.")
        => Create(409, ActorErrorCodes.Conflict, message);

    /// <summary>
    /// 创建业务规则错误（默认 400）。
    /// Handler 可以抛出 ActorException(ActorError.Business(...))，Fiber 会把它原样返回给调用方。
    /// </summary>
    public static ActorError Business(
        string errorCode,
        string message,
        string? detail = null,
        int code = 400)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Actor business error code cannot be empty.", nameof(errorCode));
        }

        if (code < 400 || code > 499)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "Business actor errors must use 4xx status code.");
        }

        return Create(code, errorCode, message, detail);
    }

    /// <summary>创建内部错误（500），用于框架内部校验失败。</summary>
    public static ActorError Internal(string message = "Actor internal error.", string? detail = null)
        => Create(500, ActorErrorCodes.InternalError, message, detail);

    /// <summary>创建内部错误（500），包装处理器抛出的异常。</summary>
    public static ActorError HandlerFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Create(500, ActorErrorCodes.HandlerFailed, exception.Message, exception.ToString());
    }

    /// <summary>创建完整 ActorError，并确保字符串错误码不会被空值污染协议契约。</summary>
    public static ActorError Create(int code, string errorCode, string message, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Actor error code cannot be empty.", nameof(errorCode));
        }

        return new ActorError(code, message, detail)
        {
            ErrorCode = errorCode
        };
    }
}
