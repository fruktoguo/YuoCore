namespace YuoCore.Analyzers;

/// <summary>
/// YuoCore 源代码分析器的诊断 ID 常量定义。
/// YUO0001-YUO0005: YuoTask 使用规则
/// YUO0006-YUO0007: 架构边界规则
/// </summary>
public static class YuoDiagnosticIds
{
    /// <summary>YuoTask 调用结果必须通过 await、Forget、Coroutine、return 等被消费。</summary>
    public const string YuoTaskMustBeConsumed = "YUO0001";
    /// <summary>禁止使用 async void（事件处理器除外）。</summary>
    public const string AsyncVoidForbidden = "YUO0002";
    /// <summary>禁止通过 GetResult() 同步消费 YuoTask。</summary>
    public const string YuoTaskGetResultForbidden = "YUO0003";
    /// <summary>禁止将 YuoTask 保存到字段或属性中。</summary>
    public const string YuoTaskMustNotBeStored = "YUO0004";
    /// <summary>禁止多次 await 同一个 YuoTask 实例。</summary>
    public const string YuoTaskMustNotBeAwaitedMultipleTimes = "YUO0005";
    /// <summary>Gateway dispatcher 不允许直接引用 ECS 类型。</summary>
    public const string GatewayDispatcherMustNotUseEcs = "YUO0006";
    /// <summary>协议契约不允许引用运行时内部类型。</summary>
    public const string ProtocolContractMustNotUseRuntimeTypes = "YUO0007";
}
