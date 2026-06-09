// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

/// <summary>
/// netstandard2.1 没有内置 IsExternalInit；该 shim 只用于让 record/init 语法在 Unity 兼容目标下编译。
/// </summary>
internal static class IsExternalInit
{
}
