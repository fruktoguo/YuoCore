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
            using var measurement = YuoCoreTrace.Measure("yuocore.world.on_init");
            YuoCoreTrace.Count("yuocore.world.on_init.count");
            IsPlaying = true;
            // 重复初始化前先完整销毁存量实体：实体索引池随后归零重建，
            // 存活旧实体若不清理，其旧索引会与新实体槽位串位。
            if (Entities is { Count: > 0 })
            {
                foreach (var entity in Entities.Values.ToList())
                {
                    entity.Dispose();
                }
            }

            allSystem = new();
            Entities = new();
            components = new();
            systemsOfTag = new();
            systemsOfComponent = new();
            systemsOfBaseComponent = new();
            ResetComponentStores();
            ResetLifecycleIndex();

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
            using var measurement = YuoCoreTrace.Measure("yuocore.world.on_destroy");
            YuoCoreTrace.Count("yuocore.world.on_destroy.count");
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
            ResetComponentStores();
            systemsOfTag.Clear();
            systemsOfComponent.Clear();
            systemsOfBaseComponent.Clear();
            ResetLifecycleIndex();
            systemDic.Clear();
            allSystem.Clear();

            // 清除所有场景
            AllScenes.Clear();
            WorldMain = null;
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
            "Game Exit".Log();
        }

        /// <summary>
        /// 更新函数，每帧调用
        /// </summary>
        public void Update()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.update");
            YuoCoreTrace.Count("yuocore.world.update.count");
            using (YuoCoreTrace.Measure("yuocore.world.time_update"))
                Time.Update();
            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
                ExecutionCommandBuffer();
            var processedStartCount = 0;
            try
            {
                using var startMeasurement = YuoCoreTrace.Measure("yuocore.world.start_systems");
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

            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
                ExecutionCommandBuffer();

            using (YuoCoreTrace.Measure("yuocore.world.update_systems"))
                RunSystemForAllEntity(SystemType.Update);
        }

        /// <summary>
        /// 固定更新函数，固定时间间隔调用
        /// </summary>
        public void FixedUpdate()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.fixed_update");
            YuoCoreTrace.Count("yuocore.world.fixed_update.count");
            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
                ExecutionCommandBuffer();
            using (YuoCoreTrace.Measure("yuocore.world.fixed_update_systems"))
                RunSystemForAllEntity(SystemType.FixedUpdate);
            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
                ExecutionCommandBuffer();
        }
        
        public void OnDrawGizmos()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.gizmos");
            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
                ExecutionCommandBuffer();
            RunSystemForAllEntity(SystemType.OnDrawGizmos);
            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
                ExecutionCommandBuffer();
        }

        /// <summary>
        /// 延迟更新函数，在 Update 之后调用
        /// </summary>
        public void LateUpdate()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.world.late_update");
            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
                ExecutionCommandBuffer();
            using (YuoCoreTrace.Measure("yuocore.world.late_update_systems"))
                RunSystemForAllEntity(SystemType.LateUpdate);
            using (YuoCoreTrace.Measure("yuocore.world.command_buffer"))
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
