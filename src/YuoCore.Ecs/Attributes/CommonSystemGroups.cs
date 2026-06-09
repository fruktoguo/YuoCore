using System;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 基础系统组定义
    /// </summary>
    /// <summary>
    /// 前置执行组 - 在默认组之前执行的系统
    /// </summary>
    [SystemGroupOrderBefore(typeof(SystemGroupExtend))]
    public class SystemGroupExtendBefore : SystemGroupBase
    {
    }

    /// <summary>
    /// 默认系统组 - 未指定组的系统归入此组
    /// </summary>
    [SystemGroupOrderAfter(typeof(SystemGroupExtendBefore))]
    public class SystemGroupExtend : SystemGroupBase
    {
    }

    /// <summary>
    /// 后置执行组 - 在默认组之后执行的系统
    /// </summary>
    [SystemGroupOrderAfter(typeof(SystemGroupExtend))]
    public class SystemGroupExtendAfter : SystemGroupBase
    {
    }

    /// <summary>
    /// 最终执行组 - 最后执行的系统（如清理、UI更新等）
    /// </summary>
    [SystemGroupOrderAfter(typeof(SystemGroupExtendAfter))]
    public class SystemGroupExtendFinal : SystemGroupBase
    {
    }

    public static class SystemGroupType
    {
        public static readonly Type ExtendBefore = typeof(SystemGroupExtendBefore);
        public static readonly Type Extend = typeof(SystemGroupExtend);
        public static readonly Type ExtendAfter = typeof(SystemGroupExtendAfter);
        public static readonly Type ExtendFinal = typeof(SystemGroupExtendFinal);
    }
}