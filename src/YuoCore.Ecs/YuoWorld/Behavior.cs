using System;
using System.Collections.Generic;
using System.Linq;
using YuoCore.Ecs.Log;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// 初始化 YuoWorld
        /// </summary>
        public void OnInit(bool setAsDefault = true)
        {
            IsPlaying = true;
            allSystem = new();
            Entities = new();
            components = new();
            systemsOfTag = new();
            systemsOfComponent = new();
            systemsOfBaseComponent = new();

            if (setAsDefault)
            {
                Instance = this;
            }

            // 系统初始化必须放在所有初始化之前
            Initialization();

            // 创建主实体并添加核心组件
            WorldMain = new YuoEntity(this, 0);
            WorldMain.EntityName = "核心组件";
            WorldMain.AddComponent<YuoWorldMainComponent>();

            // 添加一个默认场景实体
            var scene = new YuoEntity(this, IDGenerate.GetID(IDGenerate.IDType.Scene, 0));
            scene.EntityName = "默认场景";
            AllScenes.Add(scene);
            scene.AddComponent<SceneComponent>();
        }

        /// <summary>
        /// 销毁 YuoWorld
        /// </summary>
        public void OnDestroy()
        {
            try
            {
                // 退出前执行 ExitGame 系统
                RunSystemForAllEntity<IExitGame>();

                // 销毁所有实体
                foreach (var entity in Entities.Values.ToList())
                {
                    entity?.Dispose();
                }
            }
            catch (Exception e)
            {
                $"Game Exit Error {e}".LogError();
            }

            // 清理所有集合
            Entities.Clear();
            components.Clear();
            systemsOfTag.Clear();
            systemsOfComponent.Clear();
            systemsOfBaseComponent.Clear();
            allSystem.Clear();

            // 清除所有场景
            AllScenes.Clear();
            WorldMain = null;
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
            // 手动触发垃圾回收
            GC.Collect();
            "Game Exit".Log();
        }

        /// <summary>
        /// 更新函数，每帧调用
        /// </summary>
        public void Update()
        {
            ExecutionCommandBuffer();
            var processedStartCount = 0;
            try
            {
                for (; processedStartCount < startSystems.Count;)
                {
                    var component = startSystems[processedStartCount++];
                    if (component.IsUnusable()) continue;

                    if (linkComponent.TryGetValue(component.Type, out var linkTypes))
                    {
                        foreach (var linkType in linkTypes)
                        {
                            component.Entity.AddComponent(linkType);
                        }
                    }

                    var startType = typeof(IStart);
                    RunSystem(startType, component);
                }
            }
            finally
            {
                if (processedStartCount > 0)
                {
                    startSystems.RemoveRange(0, Math.Min(processedStartCount, startSystems.Count));
                }
            }

            ExecutionCommandBuffer();

            RunSystemForAllEntity(SystemType.Update);
        }

        /// <summary>
        /// 固定更新函数，固定时间间隔调用
        /// </summary>
        public void FixedUpdate()
        {
            ExecutionCommandBuffer();
            RunSystemForAllEntity(SystemType.FixedUpdate);
            ExecutionCommandBuffer();
        }
        
        public void OnDrawGizmos()
        {
            ExecutionCommandBuffer();
            RunSystemForAllEntity(SystemType.OnDrawGizmos);
            ExecutionCommandBuffer();
        }

        /// <summary>
        /// 延迟更新函数，在 Update 之后调用
        /// </summary>
        public void LateUpdate()
        {
            ExecutionCommandBuffer();
            RunSystemForAllEntity(SystemType.LateUpdate);
            ExecutionCommandBuffer();
        }

        public bool IsPlaying { get; private set; } = true;

        public static bool isPlaying => Instance?.IsPlaying ?? false;

        public void OnQuit()
        {
            IsPlaying = false;
        }
    }
}
