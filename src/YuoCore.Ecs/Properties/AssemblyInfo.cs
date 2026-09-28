using System.Runtime.CompilerServices;

// 框架级正确性测试需要直接触碰 ComponentStore/ComponentTypeRegistry 等内部稠密索引结构，
// 与 Backend/Gateway 的 InternalsVisibleTo 约定保持一致，仅向测试程序集开放。
[assembly: InternalsVisibleTo("YuoCore.Ecs.Tests")]
