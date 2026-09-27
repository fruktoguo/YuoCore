using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// 通过标签获取系统；tagId &lt; 0 视为无订阅
        /// </summary>
        private bool GetSystemOfTag(Type type, out List<SystemBase> systems)
        {
            var tagId = ComponentTypeRegistry.GetId(type);
            systems = tagId >= 0 ? GetSystemsByTagId(tagId) : null;
            return systems != null;
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
        ///  通过组件获取系统；componentId &lt; 0 视为无命中
        /// </summary>
        private bool GetSystemOfComponent(Type type, out List<SystemBase> systems)
        {
            var componentId = ComponentTypeRegistry.GetId(type);
            systems = componentId >= 0 ? GetSystemsByComponentId(componentId) : null;
            return systems != null;
        }

        private bool GetSystemOfBaseComponent(Type type, out List<SystemBaseBase> systems)
        {
            var baseTypeId = ComponentTypeRegistry.GetId(type);
            systems = baseTypeId >= 0 ? GetSystemsByBaseTypeId(baseTypeId) : null;
            return systems != null;
        }

        public MultiHashSetMap<Type, YuoComponent> GetAllComponents() => components;
        public MultiMap<Type, SystemBase> GetAllSystemOfComponent() => systemsOfComponent;
        public MultiMap<Type, SystemBase> GetAllSystemOfTag() => systemsOfTag;
        public List<SystemBase> GetAllSystem => allSystem;
    }
}