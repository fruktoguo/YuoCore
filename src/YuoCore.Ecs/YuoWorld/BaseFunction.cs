using System;
using System.Collections.Generic;
using System.Reflection;
using YuoCore.Ecs.Log;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// 处理系统覆盖
        /// </summary>
        private void CoverSystem()
        {
            coverSystemDic.Clear();

            foreach (var system in allSystem)
            {
                if (system.Type.GetCustomAttribute<CoverSystemAttribute>() is { } coverSystemAttribute)
                {
                    coverSystemDic[coverSystemAttribute.coverSystemType] = coverSystemAttribute;
                    system.hasCoverSystem = true;
                }
                else
                    system.hasCoverSystem = false;
            }
        }

        /// <summary>
        /// 从系统列表和映射中移除指定系统
        /// </summary>
        private void RemoveSystem(SystemBase system)
        {
            allSystem.Remove(system);
            foreach (var pair in systemsOfTag)
                pair.Value.Remove(system);
            foreach (var pair in systemsOfComponent)
                pair.Value.Remove(system);
            if (system is SystemBaseBase baseSystem)
            {
                foreach (var pair in systemsOfBaseComponent)
                    pair.Value.Remove(baseSystem);
            }
        }

        /// <summary>
        /// 清理待销毁的实体和组件
        /// </summary>
        private void ExecutionCommandBuffer()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.execution_command_buffer");
            YuoCoreTrace.Count("yuocore.world.execution_command_buffer.count");
            if (componentDestroyBuffer.HasBuffer)
            {
                foreach (var yuoComponent in componentDestroyBuffer.ExecutionBuffer())
                {
                    // Debug.Log($"Component {yuoComponent} 开始释放");
                    yuoComponent.Dispose();
                }
            }

            if (entityDestroyBuffer.HasBuffer)
            {
                foreach (var entity in entityDestroyBuffer.ExecutionBuffer())
                {
                    // Debug.Log($"Entity {entity} 开始释放");
                    entity.Dispose();
                }
            }
        }

        /// <summary>
        /// 注册系统
        /// </summary>
        internal void RegisterSystem(SystemBase system, Type type)
        {
            systemsOfComponent.AddItem(type, system);
        }

        /// <summary>
        /// 注册基础系统
        /// </summary>
        internal void RegisterBaseSystem(SystemBaseBase system, Type type)
        {
            if (!baseTypeMapReverse.ContainsKey(type))
            {
                $"System {system.Type.Name} 的 {type.Name} 不是 BaseComponent".LogError();
            }
            systemsOfBaseComponent.AddItem(type, system);
        }

        /// <summary>
        /// 初始化主要数据
        /// </summary>
        private void Initialization()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                LoadAssembly(assembly);
            }
            Refresh();
        }

        #region 反射获取组件

        private static bool CheckComponent<TP>(Type find) where TP : class
        {
            var baseType = find.BaseType; //获取基类
            while (baseType != null) //获取所有基类
            {
                if (baseType == typeof(TP)) return true;
                baseType = baseType.BaseType;
            }

            return false;
        }

        private static TP CheckSystem<TP>(Type find) where TP : class
        {
            var baseType = find.BaseType; //获取基类
            while (baseType != null) //获取所有基类
            {
                if (baseType == typeof(TP))
                {
                    try
                    {
                        object obj = Activator.CreateInstance(find);
                        if (obj != null)
                        {
                            TP info = obj as TP;
                            return info;
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    break;
                }
                else
                {
                    baseType = baseType.BaseType;
                }
            }

            return null;
        }

        #endregion
    }
}
