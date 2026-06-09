using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    /// <summary>
    /// YuoWorld组控制扩展方法
    /// </summary>
    public static class YuoWorldGroupExtensions
    {
        /// <summary>
        /// 只运行指定组的系统
        /// </summary>
        public static void RunSystemOnlyGroup<T>(this YuoEntity entity, Type targetGroup) where T : ISystemTag
        {
            YuoWorld.RunSystemWithGroupControl<T>(entity, includeGroup: targetGroup);
        }

        /// <summary>
        /// 运行系统但排除指定组
        /// </summary>
        public static void RunSystemExcludeGroup<T>(this YuoEntity entity, Type excludeGroup) where T : ISystemTag
        {
            YuoWorld.RunSystemWithGroupControl<T>(entity, excludeGroup: excludeGroup);
        }

        /// <summary>
        /// 只运行指定组的系统（批量）
        /// </summary>
        public static void RunSystemOnlyGroupForEntities<T>(this List<YuoEntity> entities, Type targetGroup)
            where T : ISystemTag
        {
            YuoWorld.RunSystemForGroup<T>(entities, includeGroup: targetGroup);
        }

        /// <summary>
        /// 运行系统但排除指定组（批量）
        /// </summary>
        public static void RunSystemExcludeGroupForEntities<T>(this List<YuoEntity> entities, Type excludeGroup)
            where T : ISystemTag
        {
            YuoWorld.RunSystemForGroup<T>(entities, excludeGroup: excludeGroup);
        }
    }
}