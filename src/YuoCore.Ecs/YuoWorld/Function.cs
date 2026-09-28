using System;
using System.Collections.Generic;
using System.Reflection;
using YuoCore.Ecs.Log;

namespace YuoCore.Ecs
{
    /// <summary>
    /// YuoWorld 类是 ECS 系统的核心类，管理实体、组件和系统
    /// </summary>
    public partial class YuoWorld
    {
        /// <summary>
        /// 根据 ID 获取实体
        /// </summary>
        public YuoEntity GetEntity(long instanceId)
        {
            Entities.TryGetValue(instanceId, out YuoEntity entity);
            return entity;
        }

        /// <summary>
        /// 根据实体名称获取实体
        /// </summary>
        public YuoEntity GetEntity(string entityName)
        {
            Entities.TryGetValue(IDGenerate.GetID(entityName), out YuoEntity entity);
            return entity;
        }

        /// <summary>
        /// 设置实体的父实体
        /// </summary>
        public void SetParent(YuoEntity entity, YuoEntity parent)
        {
            entity.SetParent(parent);
        }

        /// <summary>
        /// 根据 ID 获取组件
        /// </summary>
        public T GetComponent<T>(long instanceId) where T : YuoComponent
        {
            return GetEntity(instanceId)?.GetComponent<T>();
        }

        /// <summary>
        /// 获取指定类型的所有组件
        /// </summary>
        public IReadOnlySet<YuoComponent> GetAllComponents<T>() where T : YuoComponent
        {
            return components[typeof(T)];
        }

        /// <summary>
        /// 根据类型名称获取组件类型
        /// </summary>
        public Type GetComponentType(string typeName)
        {
            return allComponentType.GetValueOrDefault(typeName);
        }

        /// <summary>
        /// 刷新世界状态
        /// </summary>
        public void Refresh()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.refresh");
            YuoCoreTrace.Count("yuocore.world.refresh.count");
            CoverSystem();
            SystemSort();
            GetLinkComponent();
            GetSingleComponent();
        }

        /// <summary>
        /// 获取系统的排序顺序
        /// </summary>
        private int GetOrder(Type type)
        {
            return type.GetCustomAttribute<SystemOrderAttribute>()?.Order ?? 0;
        }

        /// <summary>
        /// 加载程序集中的类型
        /// </summary>
        public void LoadAssembly(Assembly assembly)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.load_assembly");
            YuoCoreTrace.Count("yuocore.world.load_assembly.count");
            if (assembly.IsDynamic) return;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            LoadTypes(types);
        }

        /// <summary>
        /// 创建系统实例
        /// </summary>
        void CreateSystem(SystemBase system)
        {
            if (!allSystem.Contains(system))
            {
                allSystem.Add(system);
            }
        }

        /// <summary>
        /// 创建组件类型
        /// </summary>
        void CreateComponent(Type type)
        {
            allComponentType.TryAdd(type.Name, type);
            // 扫描到的组件类型立即分配 ComponentTypeId，后续热路径以 int 索引代替 Type 解析。
            ComponentTypeRegistry.GetOrAddId(type);
            if (!components.ContainsKey(type))
            {
                components.Add(type, new());
            }

            // var baseType = type.BaseType;
            // if (baseType != null && baseType.BaseType == typeof(YuoComponent) &&
            //     !baseType.Name.Contains("YuoComponentInstance") &&
            //     !baseType.Name.Contains("YuoComponentGet"))
            // {
            //     baseComponents[type] = baseType;
            // }

            if (CheckBaseIsYuoComponent(type, out var baseType))
            {
                baseTypeMap[type] = baseType;
                baseTypeMapReverse.AddItem(baseType, type);
            }
        }

        bool CheckBaseIsYuoComponent(Type type, out Type baseType)
        {
            baseType = type.BaseType;
            while (baseType != null && baseType.BaseType != null)
            {
                if (baseType.Name.Contains("YuoComponentInstance") || baseType.Name.Contains("YuoComponentGet"))
                    return false;
                if (baseType.BaseType == typeof(YuoComponent)) return true;
                baseType = baseType.BaseType;
            }

            return false;
        }

        /// <summary>
        /// 加载类型
        /// </summary>
        public void LoadTypes(Type[] types)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.load_types");
            YuoCoreTrace.Count("yuocore.world.load_types.count");
            try
            {
                List<SystemBase> systems = new();

                foreach (var type in types)
                {
                    if (type.IsAbstract || type.ContainsGenericParameters) continue;
                    var systemBase = CheckSystem<SystemBase>(type);
                    var yuoComponent = CheckComponent<YuoComponent>(type);

                    if (systemBase != null)
                    {
                        systems.Add(systemBase);
                        systemBase.Type = type;
                    }
                    else if (yuoComponent)
                    {
                        CreateComponent(type);
                    }
                }

                foreach (var system in systems)
                {
                    CreateSystem(system);
                }
            }
            catch (Exception e)
            {
                e.LogError();
            }
        }

        /// <summary>
        /// 对系统进行排序
        /// </summary>
        public void SystemSort()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.system_sort");
            YuoCoreTrace.Count("yuocore.world.system_sort.count");
            // 使用SystemGroupSorter进行Group级别和Order级别的排序
            allSystem = SystemGroupSorter.SortSystems(allSystem, GetOrder);

            systemsOfComponent.Clear();
            systemsOfTag.Clear();
            systemsOfBaseComponent.Clear();
            systemDic.Clear();
            // 组件/标签/基类命中索引与三张映射表同步清空重建；
            // systemsOfBaseComponent 必须一并清理，否则每次 Sort 后基类系统重复派发。
            ResetComponentSystemIndex();
            ResetTagSystemIndex();
            ResetBaseComponentSystemIndex();

            foreach (var system in allSystem)
            {
                system.Clear();
                system.Init(this);
                systemDic[system.Type] = system;
                foreach (var inter in system.Type.GetInterfaces())
                {
                    foreach (var iInter in inter.GetInterfaces())
                    {
                        if (iInter == typeof(ISystemTag))
                        {
                            systemsOfTag.AddItem(inter, system);
                            IndexSystemForTag(inter);
                            system.systemTags.Add(inter);
                        }
                    }
                }
            }

            foreach (var systems in systemsOfComponent.Values)
            {
                systems.Sort((a, b) => GetOrder(a.Type) - GetOrder(b.Type));
            }

            foreach (var system in allSystem)
            {
                foreach (var entity in Entities.Values)
                {
                    system.CheckValidEntity(entity);
                }
            }

            // 缓存高频系统的直接引用，避免每帧字典查找
            CacheHighFrequencySystems();
            _groupSystemCache.Clear();
        }

        /// <summary>
        /// 缓存高频系统的直接引用
        /// 在SystemSort阶段提取IUpdate、IFixedUpdate、ILateUpdate等高频系统到专用字段
        /// 消除每帧数百次的字典查找开销
        /// </summary>
        private void CacheHighFrequencySystems()
        {
            // 缓存IUpdate系统
            _updateSystems = GetSystemsByTagId(ComponentTypeRegistry.GetId(SystemType.Update)) is { } updateList
                ? new List<SystemBase>(updateList)
                : new List<SystemBase>();

            // 缓存IFixedUpdate系统
            _fixedUpdateSystems = GetSystemsByTagId(ComponentTypeRegistry.GetId(SystemType.FixedUpdate)) is { } fixedList
                ? new List<SystemBase>(fixedList)
                : new List<SystemBase>();

            // 缓存ILateUpdate系统
            _lateUpdateSystems = GetSystemsByTagId(ComponentTypeRegistry.GetId(SystemType.LateUpdate)) is { } lateList
                ? new List<SystemBase>(lateList)
                : new List<SystemBase>();

            // 缓存IOnDrawGizmos系统
            _onDrawGizmosSystems = GetSystemsByTagId(ComponentTypeRegistry.GetId(SystemType.OnDrawGizmos)) is { } gizmosList
                ? new List<SystemBase>(gizmosList)
                : new List<SystemBase>();
        }

        /// <summary>
        /// 获取链接组件
        /// </summary>
        public void GetLinkComponent()
        {
            linkComponent.Clear();
            foreach (var type in GetAllComponentOfType().Values)
            {
                var link = type.GetCustomAttributes<LinkComponentOfAttribute>();

                foreach (var linkComponentAttribute in link)
                {
                    linkComponent.AddItem(linkComponentAttribute.LinkType, type);
                }
            }
        }

        /// <summary>
        /// 获取单例组件
        /// </summary>
        public void GetSingleComponent()
        {
            singleComponents.Clear();
            foreach (var type in GetAllComponentOfType().Values)
            {
                var single = type.GetCustomAttributes<SingleComponentAttribute>();
                if (single.Any())
                {
                    singleComponents.Add(type);
                }
            }
        }

        /// <summary>
        /// 获取所有组件类型
        /// </summary>
        public Dictionary<string, Type> GetAllComponentOfType()
        {
            return allComponentType;
        }
    }
}
