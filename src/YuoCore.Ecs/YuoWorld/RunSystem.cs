using System;
using System.Collections.Generic;
using System.Linq;
using YuoCore.Ecs.Log;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        #region RunSystemForEntity

        /// <summary>
        /// 为指定实体运行标签类型的系统
        /// </summary>
        private void RunSystemOfTag(Type tagType, YuoEntity entity)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.dispatch.single_entity");
            YuoCoreTrace.Count("yuocore.world.dispatch.single_entity.count");
            if (entity.IsDisposed) return;
            if (GetSystemOfTag(tagType, out var systems))
            {
                foreach (var system in systems)
                {
                    YuoCoreTrace.Count("yuocore.world.dispatch.single_entity.system_call.count");
                    system.RunType = tagType;
                    system.m_Run(entity);
                }
            }
        }

        /// <summary>
        /// 为多个实体运行指定类型的系统
        /// </summary>
        private void RunSystemOfTag<T>(List<YuoEntity> entities) where T : ISystemTag
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.dispatch.batch_entity");
            Type type = typeof(T);
            if (GetSystemOfTag(type, out var systems))
            {
                foreach (var system in systems)
                {
                    system.RunType = type;
                    foreach (var entity in entities)
                    {
                        YuoCoreTrace.Count("yuocore.world.dispatch.entity_attempt.count");
                        if (entity.IsDisposed) continue;
                        YuoCoreTrace.Count("yuocore.world.dispatch.entity_run.count");
                        system.m_Run(entity);
                    }
                }
            }
        }

        /// <summary>
        /// 静态方法：为指定实体运行系统
        /// </summary>
        public static void RunSystem<T>(YuoEntity entity) where T : ISystemTag
        {
            entity.World.RunSystemOfTag<T>(entity);
        }

        /// <summary>
        /// 静态方法：为多个实体运行系统
        /// </summary>
        public static void RunSystem<T>(List<YuoEntity> entities) where T : ISystemTag
        {
            if (entities == null || entities.Count == 0) return;
            entities[0].World.RunSystemOfTag<T>(entities);
        }

        /// <summary>
        /// 静态方法：根据标签类型为指定实体运行系统
        /// </summary>
        public static void RunSystem(Type tagType, YuoEntity entity)
        {
            entity.World.RunSystemOfTag(tagType, entity);
        }

        /// <summary>
        /// 为指定实体运行标签类型的系统
        /// </summary>
        private void RunSystemOfTag<T>(YuoEntity entity) where T : ISystemTag
        {
            RunSystemOfTag(typeof(T), entity);
        }

        #endregion

        #region RunSystemForComponent

        /// <summary>
        /// 为指定组件运行标签类型的系统
        /// </summary>
        private void RunSystemOfTag(Type tagType, YuoComponent component)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.dispatch.component");
            YuoCoreTrace.Count("yuocore.world.dispatch.component.count");
            if (component.IsUnusable()) return;
            var componentId = ComponentTypeRegistry.GetId(component.Type);
            var systems = componentId >= 0 ? GetSystemsByComponentId(componentId) : null;
            if (systems != null)
            {
                foreach (var system in systems)
                {
                    if (system.HasTag(tagType))
                    {
                        system.RunType = tagType;
                        system.m_Run(component.Entity);
                    }
                }
            }
        }

        /// <summary>
        /// 为指定组件运行标签类型的系统
        /// </summary>
        private void RunSystemOfTag<T>(YuoComponent component) where T : ISystemTag
        {
            RunSystemOfTag(typeof(T), component);
        }

        /// <summary>
        /// 静态方法：为指定组件运行系统
        /// </summary>
        public static void RunSystem<T>(YuoComponent component) where T : ISystemTag
        {
            component.Entity.World.RunSystemOfTag<T>(component);
        }

        /// <summary>
        /// 静态方法：根据标签类型为指定组件运行系统
        /// </summary>
        public static void RunSystem(Type tagType, YuoComponent component)
        {
            component.Entity.World.RunSystemOfTag(tagType, component);
        }

        #endregion

        #region RunSystemForBaseComponent

        /// <summary>
        /// 为基础组件运行标签类型的系统
        /// </summary>
        private void RunSystemOfBaseComponent(Type tagType, YuoComponent component)
        {
            if (component.IsUnusable()) return;
            if (component.BaseComponentType != null)
            {
                var baseTypeId = ComponentTypeRegistry.GetId(component.BaseComponentType);
                var systems = baseTypeId >= 0 ? GetSystemsByBaseTypeId(baseTypeId) : null;
                if (systems != null)
                {
                    foreach (var system in systems)
                    {
                        if (system.HasTag(tagType))
                        {
                            system.RunType = tagType;
                            system.m_Run(component.Entity);
                        }
                    }
                }
            }
            else $"{component} 不存在父类组件".LogError();
        }

        /// <summary>
        /// 静态方法：为基础组件运行标签类型的系统
        /// </summary>
        public static void RunSystemOfBase(Type tagType, YuoComponent component)
        {
            component.Entity.World.RunSystemOfBaseComponent(tagType, component);
        }

        /// <summary>
        /// 静态方法：为基础组件运行指定类型的系统
        /// </summary>
        public static void RunSystemOfBase<T>(YuoComponent component) where T : ISystemTag
        {
            component.Entity.World.RunSystemOfBaseComponent(typeof(T), component);
        }

        #endregion

        /// <summary>
        /// 运行指定类型的系统
        /// </summary>
        private void RunSystem<T>(YuoEntity entity, Type componentType) where T : ISystemTag
        {
            var runType = typeof(T);

            // 生命周期命中表：componentId 直查，免 Type 字典查找
            var componentId = ComponentTypeRegistry.GetId(componentType);
            var systems = componentId >= 0 ? GetSystemsByComponentId(componentId) : null;
            if (systems == null) return;

            foreach (var system in systems)
            {
                if (system.CheckValidEntity(entity))
                {
                    if (system is T)
                    {
                        system.RunType = runType;
                        try
                        {
                            if (system.Enabled)
                                system.m_Run(entity);
                        }
                        catch (Exception e)
                        {
                            e.LogError();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 运行基础组件的指定类型系统
        /// </summary>
        private void RunSystemOfBase<T>(YuoEntity entity, Type baseComponentType) where T : ISystemTag
        {
            var baseTypeId = ComponentTypeRegistry.GetId(baseComponentType);
            var systems = baseTypeId >= 0 ? GetSystemsByBaseTypeId(baseTypeId) : null;
            if (systems != null)
            {
                var runType = typeof(T);
                foreach (var system in systems)
                {
                    if (system.CheckValidEntity(entity))
                    {
                        if (system is T)
                        {
                            system.RunType = runType;
                            try
                            {
                                if (system.Enabled)
                                    system.m_Run(entity);
                            }
                            catch (Exception e)
                            {
                                e.LogError();
                            }
                        }
                    }
                }
            }
        }

        #region RunSystemForAllEntity

        /// <summary>
        /// 根据标签执行对应的系统（所有实体）
        /// </summary>
        private void RunSystemForAllEntity<T>() where T : ISystemTag
        {
            RunSystemForAllEntity(typeof(T));
        }

        /// <summary>
        /// 根据标签类型执行对应的系统（所有实体）
        /// 对高频系统（IUpdate、IFixedUpdate、ILateUpdate）使用预缓存的直接引用，避免字典查找
        /// </summary>
        private void RunSystemForAllEntity(Type tagType)
        {
            var stage = tagType == SystemType.Update
                ? "yuocore.world.dispatch.update"
                : tagType == SystemType.FixedUpdate
                    ? "yuocore.world.dispatch.fixed_update"
                    : tagType == SystemType.LateUpdate
                        ? "yuocore.world.dispatch.late_update"
                        : "yuocore.world.dispatch.other";
            using var measurement = YuoCoreTrace.Measure(stage);
            YuoCoreTrace.Count("yuocore.world.dispatch.count");
            List<SystemBase> systems = null;

            // 高频系统快速路径：使用预缓存的直接引用，避免字典查找
            if (tagType == SystemType.Update)
            {
                systems = _updateSystems;
            }
            else if (tagType == SystemType.FixedUpdate)
            {
                systems = _fixedUpdateSystems;
            }
            else if (tagType == SystemType.LateUpdate)
            {
                systems = _lateUpdateSystems;
            }
            else if (tagType == SystemType.OnDrawGizmos)
            {
                systems = _onDrawGizmosSystems;
            }
            else
            {
                // 其他标签走稠密索引直查，免 Type 字典查找；tagId < 0 视为无订阅
                var tagId = ComponentTypeRegistry.GetId(tagType);
                systems = tagId >= 0 ? GetSystemsByTagId(tagId) : null;
            }

            // 执行系统
            if (systems != null)
            {
                YuoCoreTrace.Count("yuocore.world.dispatch.system_list", systems.Count);
                foreach (var system in systems)
                {
                    system.RunType = tagType;
                    system.m_Run();
                }
            }
        }

        /// <summary>
        /// 静态方法：为所有实体运行指定类型的系统
        /// </summary>
        public static void RunSystemForAll<T>() where T : ISystemTag
        {
            Instance.RunSystemForAllEntity<T>();
        }

        /// <summary>
        /// 静态方法：为所有实体运行指定标签类型的系统
        /// </summary>
        public static void RunSystemForAll(Type tagType)
        {
            Instance.RunSystemForAllEntity(tagType);
        }

        #endregion

        #region RunSystemWithGroupControl

        /// <summary>
        /// 高性能组控制系统执行
        /// </summary>
        public static void RunSystemWithGroupControl<T>(YuoEntity entity, Type includeGroup = null, Type excludeGroup = null)
            where T : ISystemTag
        {
            entity.World.RunSystemOfTagWithGroupFilter(entity, typeof(T), includeGroup, excludeGroup);
        }

        /// <summary>
        /// 高性能组过滤执行（内部方法）
        /// </summary>
        private void RunSystemOfTagWithGroupFilter(YuoEntity entity, Type tagType, Type includeGroup, Type excludeGroup)
        {
            if (entity.IsDisposed) return;

            // 快速路径：无组过滤时使用原有逻辑
            if (includeGroup == null && excludeGroup == null)
            {
                RunSystemOfTag(tagType, entity);
                return;
            }

            // 优化路径：使用缓存的系统列表
            if (GetSystemOfTagWithGroupFilter(tagType, includeGroup, excludeGroup, out var systems))
            {
                foreach (var system in systems)
                {
                    system.RunType = tagType;
                    system.m_Run(entity);
                }
            }
        }

        /// <summary>
        /// 获取过滤后的系统列表（带缓存，零GC）
        /// 直接返回缓存的列表引用，调用方不应修改返回的列表
        /// </summary>
        private bool GetSystemOfTagWithGroupFilter(Type tagType, Type includeGroup, Type excludeGroup,
            out List<SystemBase> systems)
        {
            systems = null;

            // 标签稠密索引直查；tagId < 0 视为无订阅
            var tagId = ComponentTypeRegistry.GetId(tagType);
            var allSystems = tagId >= 0 ? GetSystemsByTagId(tagId) : null;
            if (allSystems == null)
                return false;

            // 构建ValueTuple缓存键（零分配）
            var cacheKey = (tagType, includeGroup, excludeGroup);

            // 先尝试无锁读取（大多数情况下缓存已存在）
            if (_groupSystemCache.TryGetValue(cacheKey, out systems))
            {
                return systems.Count > 0;
            }

            // 缓存未命中，需要加锁构建
            lock (_cacheLock)
            {
                // 双重检查，避免重复构建
                if (_groupSystemCache.TryGetValue(cacheKey, out systems))
                {
                    return systems.Count > 0;
                }

                // 过滤逻辑
                var filteredSystems = new List<SystemBase>();
                foreach (var system in allSystems)
                {
                    // 简化的过滤逻辑
                    if (includeGroup != null && system.SystemGroupType != includeGroup)
                        continue;

                    if (excludeGroup != null && system.SystemGroupType == excludeGroup)
                        continue;

                    filteredSystems.Add(system);
                }

                // 缓存结果并返回
                _groupSystemCache[cacheKey] = filteredSystems;
                systems = filteredSystems;
                return systems.Count > 0;
            }
        }

        /// <summary>
        /// 批量执行多个实体的组控制系统（优化版本，避免重复获取系统列表）
        /// </summary>
        public static void RunSystemForGroup<T>(List<YuoEntity> entities, Type includeGroup = null,
            Type excludeGroup = null) where T : ISystemTag
        {
            if (entities == null || entities.Count == 0)
                return;

            var tagType = typeof(T);

            // 快速路径：无组过滤时使用原有逻辑
            if (includeGroup == null && excludeGroup == null)
            {
                entities[0].World.RunSystemOfTag<T>(entities);
                return;
            }

            // 一次性获取过滤后的系统列表，避免每个实体都重复查询
            var world = entities[0].World;
            if (!world.GetSystemOfTagWithGroupFilter(tagType, includeGroup, excludeGroup, out var systems))
                return;

            // 批量执行
            foreach (var system in systems)
            {
                system.RunType = tagType;
                foreach (var entity in entities)
                {
                    if (!entity.IsDisposed)
                        system.m_Run(entity);
                }
            }
        }

        #endregion
    }
}
