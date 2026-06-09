namespace YuoCore.Actor;

/// <summary>
/// Actor 框架的异常包装。将 ActorError 转换为 .NET 异常，便于在调用链中传递。
/// </summary>
public sealed class ActorException : Exception
{
    /// <param name="error">结构化的 Actor 错误信息。</param>
    public ActorException(ActorError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>结构化的 Actor 错误对象。</summary>
    public ActorError Error { get; }
}
