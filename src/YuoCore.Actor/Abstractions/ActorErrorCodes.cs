namespace YuoCore.Actor;

/// <summary>
/// Actor 错误的稳定字符串码。
/// 数字 Code 适合快速判断 HTTP-like 大类，ErrorCode 适合跨进程、日志和客户端契约。
/// </summary>
public static class ActorErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string Timeout = "timeout";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string BusinessRule = "business_rule";
    public const string HandlerFailed = "handler_failed";
    public const string InternalError = "internal_error";
}
