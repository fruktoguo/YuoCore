using System.Threading;

namespace YuoCore.Actor;

/// <summary>
/// Actor 实例唯一标识符。进程内递增分配，用于在运行时定位具体的 Actor 实例。
/// </summary>
public readonly record struct ActorId(long Value)
{
    private static long _nextValue;

    /// <summary>空的 Actor ID（值为 0 的默认实例）。</summary>
    public static ActorId Empty { get; } = new(0);

    /// <summary>生成一个新的全局唯一 ActorId（进程内递增）。</summary>
    public static ActorId NewId() => new(Interlocked.Increment(ref _nextValue));

    /// <summary>是否为空 ID。</summary>
    public bool IsEmpty => Value == 0;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
