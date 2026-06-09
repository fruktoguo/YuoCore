using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public abstract class SystemBase
    {
        public bool Enabled { get; set; } = true;

        public readonly HashSet<YuoEntity> Entities = new();

        public int EntityCount => Entities.Count;

        public virtual string SystemName => Type.Name;

        public virtual string Group => ""; // 用于可视化显示，保持不变

        /// <summary>
        /// 存储系统所属的组别类型，用于组控制执行
        /// </summary>
        public Type SystemGroupType { get; internal set; }

#if UNITY_EDITOR

        public double TimeConsuming { get; private set; }

        public double TotalTimeConsuming { get; private set; }

        public long TotalRunCount { get; private set; }

#endif

        protected SystemBase()
        {
#if UNITY_EDITOR
            _sw = new System.Diagnostics.Stopwatch();
            TimeConsuming = 0;
            TotalTimeConsuming = 0;
            TotalRunCount = 0;
#endif
        }

#if UNITY_EDITOR

        private bool isStartClock;

        internal void StartClock()
        {
            isStartClock = true;
            // Profiler.BeginSample(this.Name);
            _sw.Restart();
        }

        internal void StopClock()
        {
            if (!isStartClock) return;
            isStartClock = false;
            _sw.Stop();
            SetTimeConsuming(_sw.Elapsed.TotalMilliseconds);
            // Profiler.EndSample();
        }

        private void SetTimeConsuming(double time)
        {
            TimeConsuming = time;
            TotalTimeConsuming += time;
            TotalRunCount++;
        }

        private readonly System.Diagnostics.Stopwatch _sw;
#endif

        public virtual void Init(YuoWorld yuoWorld)
        {
            World = yuoWorld;
            foreach (var influenceType in influenceTypes)
            {
                yuoWorld.RegisterSystem(this, influenceType);
            }
        }

        public List<Type> influenceTypes = new();

        public Type Type { get; internal set; }

        public Type RunType;

        internal YuoWorld World { get; private set; }

        internal bool hasCoverSystem;

        internal bool CheckCoverSystem(YuoEntity entity)
        {
            if (!hasCoverSystem) return false;
            if (World is { } world && world.coverSystemDic.TryGetValue(RunType, out var cover))
            {
                foreach (var component in cover.componentTypes)
                {
                    if (world.baseTypeMap.TryGetValue(component, out var baseComponent))
                    {
                        if (!entity.HasBaseComponent(baseComponent))
                            return false;
                    }
                    else
                    {
                        if (!entity.HasComponent(component))
                            return false;
                    }
                }

                return true;
            }

            return false;
        }

        internal void m_Run()
        {
            if (!Enabled) return;
            if (Entities.Count == 0)
            {
                return;
            }

#if UNITY_EDITOR
            StartClock();
#endif
            // P1优化：将try-catch移到foreach外部，允许JIT编译器优化循环
            // 异常时中断当前系统的剩余实体执行，但不影响其他系统
            if (YuoWorld.CloseSystemTry)
            {
                foreach (var entity in Entities)
                {
                    if (CheckCoverSystem(entity)) continue;
                    RunEntity(entity);
                }
            }
            else
            {
                try
                {
                    foreach (var entity in Entities)
                    {
                        if (CheckCoverSystem(entity)) continue;
                        RunEntity(entity);
                    }
                }
                catch (Exception e)
                {
                    // 记录异常信息，包含系统名称便于调试
                    Console.Error.WriteLine($"[YuoECS] System '{SystemName}' execution failed: {e.Message}\n{e.StackTrace}");
                }
            }

#if UNITY_EDITOR
            StopClock();
#endif
        }

        internal virtual void Clear()
        {
            Entities.Clear();
            systemTags.Clear();
            influenceTypes.Clear();
        }

        internal virtual bool CheckValidEntity(YuoEntity entity)
        {
            foreach (var influenceType in influenceTypes)
            {
                if (!entity.HasComponent(influenceType)) return false;
            }

            Entities.Add(entity);
            return true;
        }

        internal void RemoveComponent(YuoEntity entity)
        {
            if (Entities.Contains(entity))
            {
                Entities.Remove(entity);
            }
        }

        internal bool m_Run(YuoEntity entity)
        {
            if (!Enabled) return false;
            if (!Entities.Contains(entity)) return false;
            if (CheckCoverSystem(entity)) return false;

#if UNITY_EDITOR
            StartClock();
#endif
            // P1优化：保持单实体版本的try-catch结构
            // 单实体执行时try-catch开销相对较小，保持异常隔离
            if (YuoWorld.CloseSystemTry)
            {
                RunEntity(entity);
            }
            else
            {
                try
                {
                    RunEntity(entity);
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"[YuoECS] System '{SystemName}' failed on entity {entity.EntityName}(ID:{entity.ID}): {e.Message}\n{e.StackTrace}");
                }
            }
#if UNITY_EDITOR
            StopClock();
#endif
            return true;
        }

        protected abstract void RunEntity(YuoEntity entity);

        internal List<Type> systemTags = new List<Type>();

        public bool HasTag<T>() where T : ISystemTag
        {
            return systemTags.Contains(typeof(T));
        }

        public bool HasTag(Type type)
        {
            return systemTags.Contains(type);
        }
    }

    public abstract class SystemBaseBase : SystemBase
    {
        public List<Type> baseTypes = new();
        public List<bool> typeIsBase = new();
        public List<Type> allType = new();

        public override void Init(YuoWorld yuoWorld)
        {
            base.Init(yuoWorld);
            foreach (var baseType in baseTypes)
            {
                yuoWorld.RegisterBaseSystem(this, baseType);
            }

            foreach (var type in allType)
            {
                typeIsBase.Add(yuoWorld.IsBaseType(type));
            }
        }

        internal override bool CheckValidEntity(YuoEntity entity)
        {
            foreach (var baseType in baseTypes)
            {
                if (!entity.HasBaseComponent(baseType))
                {
                    return false;
                }
            }

            return base.CheckValidEntity(entity);
        }

        protected void AddType(Type type, YuoWorld world)
        {
            if (world.IsBaseType(type))
                baseTypes.Add(type);
            else
                influenceTypes.Add(type);
            allType.Add(type);
        }

        internal override void Clear()
        {
            base.Clear();
            baseTypes.Clear();
            typeIsBase.Clear();
            allType.Clear();
        }
    }
}
