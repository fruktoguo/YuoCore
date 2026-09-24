using System;
using YuoCore.Ecs.Log;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// 将实体标记为待销毁
        /// </summary>
        public static void DestroyEntity(YuoEntity entity)
        {
            // Debug.Log($"{entity} 扔进回收站 frame{Time.frameCount}");
            entity?.World.entityDestroyBuffer.Add(entity);
        }

        /// <summary>
        /// 将组件标记为待销毁
        /// </summary>
        public static void DestroyComponent(YuoComponent component)
        {
            // Debug.Log($"{component.Entity} 的 {component} 扔进回收站 frame{Time.frameCount}");
            component?.Entity?.World.componentDestroyBuffer.Add(component);
        }

        /// <summary>
        /// 立即强制销毁实体
        /// </summary>
        public static void DestroyEntityForce(YuoEntity entity)
        {
            entity?.Dispose();
        }

        /// <summary>
        /// 立即强制销毁组件
        /// </summary>
        public static void DestroyComponentForce(YuoComponent component)
        {
            component?.Dispose();
        }

        /// <summary>
        /// 注册实体到世界
        /// </summary>
        public void RegisterEntity(YuoEntity entity)
        {
            if (!Entities.TryAdd(entity.EntityData.Id, entity))
                $"实体ID重复，请检查：ID {entity.EntityData.Id} Entity {entity}".LogError();
        }

        /// <summary>
        /// 从世界中注销实体
        /// </summary>
        public void UnRegisterEntity(YuoEntity entity)
        {
            if (Entities.Remove(entity.EntityData.Id))
            {
                // 实体真正移出世界时才回收稠密索引并清理稀疏存储残留槽位
                ReleaseEntityIndex(entity.EntityIndex);
            }
        }

        /// <summary>
        /// 从实体中移除组件
        /// </summary>
        public void RemoveComponent(YuoEntity entity, YuoComponent component)
        {
            components.RemoveItem(component.Type, component);
            // 世界销毁即游戏退出时，不调用 Destroy，只调用 ExitGame
            if (IsPlaying)
            {
                // Debug.Log($"销毁 组件：{component.Type.Name} entity {component.Entity}");
                if (baseTypeMap.TryGetValue(component.Type, out var baseType))
                {
                    RunSystemOfBase<IDestroy>(entity, baseType);
                    if (GetSystemOfBaseComponent(baseType, out var baseSystem))
                    {
                        foreach (var system in baseSystem)
                        {
                            system.RemoveComponent(entity);
                        }
                    }
                }
                RunSystem<IDestroy>(entity, component.Type);
                if (GetSystemOfComponent(component.Type, out var systems))
                {
                    foreach (var system in systems)
                    {
                        system.RemoveComponent(entity);
                    }
                }
            }

            // component.Dispose();
        }

        /// <summary>
        /// 将组件添加到实体
        /// </summary>
        public void AddComponent(YuoEntity entity, YuoComponent component)
        {
            AddComponent(entity, component, component.Type);
        }

        /// <summary>
        /// 将组件添加到实体，指定组件类型
        /// </summary>
        public void AddComponent(YuoEntity entity, YuoComponent component, Type componentType)
        {
            if (!components.TryGetValue(componentType, out var componentSet))
            {
                componentSet = new();
                components.Add(componentType, componentSet);
            }

            if (!componentSet.Contains(component))
            {
                if (baseTypeMap.TryGetValue(componentType, out var baseType))
                {
                    component.BaseComponentType = baseType;
                    entity.BaseComponents.AddItem(baseType, component);
                    // 基础组件掩码置位（残留语义与 ContainsKey 一致）并刷新首元素缓存
                    entity.SetBaseComponentMask(ComponentTypeRegistry.GetOrAddId(baseType));
                    entity.NoteBaseComponentsChanged(baseType);
                    RunSystemOfBase<IAwake>(entity, baseType);
                }

                componentSet.Add(component);
                // 运行 Awake 系统
                RunSystem<IAwake>(entity, componentType);
                startSystems.Add(component);

                if (singleComponents.Contains(componentType))
                {
                    foreach (var yuoComponent in componentSet)
                    {
                        if (yuoComponent != component && yuoComponent.Entity == entity)
                            yuoComponent.DestroyComponent();
                    }
                }
            }
        }

        /// <summary>
        /// 替换实体上的组件
        /// </summary>
        public void SetComponent(YuoComponent component1, YuoComponent component2)
        {
            if (components.TryGetValue(component1.Type, out var list))
            {
                list.Remove(component1);
                list.Add(component2);
            }
            else
            {
                // component1 的类型从未注册时不能把 component2 写进共享空集合，按新组件登记。
                components.AddItem(component1.Type, component2);
            }

            RunSystemOfTag<ISwitchComponent>(component2);
        }
    }
}
