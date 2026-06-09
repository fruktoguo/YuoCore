namespace YuoCore.Abstractions;

/// <summary>
/// 跨模块共享的轻量错误对象。
/// 这里不绑定 HTTP、Actor 或数据库语义，避免底层项目反向依赖上层框架。
/// </summary>
public sealed record YuoError(
    string Code,
    string Message,
    string? Detail = null)
{
    public static YuoError BadRequest(string message, string? detail = null) => new("bad_request", message, detail);

    public static YuoError NotFound(string message) => new("not_found", message);

    public static YuoError Conflict(string message) => new("conflict", message);

    public static YuoError Invalid(string message) => new("invalid", message);

    public static YuoError Failed(string message, string? detail = null) => new("failed", message, detail);
}
