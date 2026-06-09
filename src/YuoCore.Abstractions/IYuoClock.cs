namespace YuoCore.Abstractions;

/// <summary>
/// 可注入的时间抽象接口，用于解耦对 DateTimeOffset.UtcNow 的直接依赖。
/// 在测试中可替换为假时钟以控制时间流逝。
/// </summary>
public interface IYuoClock
{
    /// <summary>获取当前的 UTC 时间。</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// 默认的系统时钟实现，直接委托给 DateTimeOffset.UtcNow。
/// </summary>
public sealed class SystemYuoClock : IYuoClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
