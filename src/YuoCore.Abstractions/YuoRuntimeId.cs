using System.Threading;

namespace YuoCore.Abstractions;

/// <summary>
/// 运行时通用 ID。它只保证本进程内递增唯一，持久化业务 ID 仍应使用玩家 ID、房间 ID 等稳定键。
/// </summary>
public readonly record struct YuoRuntimeId(long Value)
{
    private static long _next;

    public static YuoRuntimeId Empty => new(0);

    public static YuoRuntimeId NewId() => new(Interlocked.Increment(ref _next));

    public bool IsEmpty => Value == 0;

    public override string ToString() => Value.ToString();
}
