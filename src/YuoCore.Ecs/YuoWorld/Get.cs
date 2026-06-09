using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// 通过标签获取系统
        /// </summary>
        private bool GetSystemOfTag(Type type, out List<SystemBase> systems)
        {
            if (systemsOfTag.TryGetValue(type, out var value))
            {
                systems = value;
                return true;
            }

            systems = null;
            return false;
        }

        /// <summary>
        /// 检查实体是否有指定组的系统
        /// </summary>
        public bool HasSystemInGroup<T>(YuoEntity entity, Type targetGroup) where T : ISystemTag
        {
            if (GetSystemOfTag(typeof(T), out var systems))
            {
                foreach (var system in systems)
                {
                    if (system.SystemGroupType == targetGroup)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        ///  通过组件获取系统
        /// </summary>
        private bool GetSystemOfComponent(Type type, out List<SystemBase> systems)
        {
            if (systemsOfComponent.TryGetValue(type, out var value))
            {
                systems = value;
                return true;
            }

            systems = null;
            return false;
        }

        private bool GetSystemOfBaseComponent(Type type, out List<SystemBaseBase> systems)
        {
            if (systemsOfBaseComponent.TryGetValue(type, out var list))
            {
                systems = list;
                return true;
            }

            systems = null;
            return false;
        }

        public MultiHashSetMap<Type, YuoComponent> GetAllComponents() => components;
        public MultiMap<Type, SystemBase> GetAllSystemOfComponent() => systemsOfComponent;
        public MultiMap<Type, SystemBase> GetAllSystemOfTag() => systemsOfTag;
        public List<SystemBase> GetAllSystem => allSystem;
    }
}