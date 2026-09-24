using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 组件类型注册表：为每个组件 Type 分配一个进程内稳定的数值 ComponentTypeId。
    /// 热路径（组件存取、系统匹配、生命周期命中表）用 int 索引代替 Type 字典查找。
    /// ID 只增不减、进程级唯一，多个 YuoWorld 共享同一套编号，便于各世界按 id 建数组索引。
    /// </summary>
    internal static class ComponentTypeRegistry
    {
        private static readonly ConcurrentDictionary<Type, int> TypeToId = new();
        private static readonly List<Type> IdToType = new();
        private static readonly object IdLock = new();
        private static volatile int _maxId;

        /// <summary>
        /// 已分配的最大 id 边界（实际 id 范围 [0, MaxId)）。用于按 id 建数组时的容量参考。
        /// </summary>
        internal static int MaxId => _maxId;

        /// <summary>
        /// 查询已注册类型的 id；未注册返回 -1，不产生分配。
        /// </summary>
        internal static int GetId(Type type)
        {
            return type != null && TypeToId.TryGetValue(type, out var id) ? id : -1;
        }

        /// <summary>
        /// 获取或注册类型 id。注册路径只允许出现在组件写入/系统初始化路径，
        /// 纯查询路径用 GetId，避免任意查询类型污染注册表。
        /// </summary>
        internal static int GetOrAddId(Type type)
        {
            if (type == null) return -1;
            if (TypeToId.TryGetValue(type, out var id)) return id;

            lock (IdLock)
            {
                if (TypeToId.TryGetValue(type, out id)) return id;
                id = IdToType.Count;
                IdToType.Add(type);
                TypeToId[type] = id;
                _maxId = IdToType.Count;
                return id;
            }
        }

        /// <summary>
        /// id 反查 Type；越界返回 null。
        /// </summary>
        internal static Type GetType(int id)
        {
            lock (IdLock)
            {
                return (uint)id < (uint)IdToType.Count ? IdToType[id] : null;
            }
        }
    }
}
