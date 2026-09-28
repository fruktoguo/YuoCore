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
            using var measurement = YuoCoreTrace.Measure(TraceCoverStage);
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
            YuoCoreTrace.Count("yuocore.system.run.count");
            using var runMeasurement = YuoCoreTrace.Measure(TraceRunStage);
            if (Entities.Count == 0)
            {
                YuoCoreTrace.Count("yuocore.system.empty_run");
                return;
            }

#if UNITY_EDITOR
            StartClock();
#endif
            // 迭代期间 Entities 的成员变更先进入待决缓冲，本轮结束后统一并入，
            // 避免 Run 内增删组件直接炸掉迭代器。
            // 异常按实体隔离：单个实体失败只跳过该实体并保留完整证据，
            // 不再中断本轮剩余实体（与单实体 m_Run 的隔离语义一致）。
            // 迭代前压实稠密数组：内部删除留下的墓碑做保序平移；
            // 若外部代码直接改写了公开 Entities 导致数量偏差，则以 HashSet 为准整体重建。
            if (_denseHoles > 0 || _denseLive != Entities.Count)
            {
                CompactDenseEntities();
            }

            _isIteratingEntities = true;
            long scanned = 0;
            long nullSkipped = 0;
            long coverChecked = 0;
            long coverSkipped = 0;
            long entityRuns = 0;
            try
            {
                using var iterationMeasurement = YuoCoreTrace.Measure("yuocore.system.entity_iteration");
                var dense = _activeSelectionEnabled ? _activeEntities : _denseEntities;
                var count = _activeSelectionEnabled ? _activeCount : _denseCount;
                if (count == 0)
                {
                    YuoCoreTrace.Count("yuocore.system.empty_run");
                }
                if (YuoWorld.CloseSystemTry)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var entity = dense[i];
                        scanned++;
                        // 墓碑槽位与迭代期间被销毁的实体一并跳过，
                        // 已释放实体不得再触发任何系统回调（稠密索引可能已被新实体复用）。
                        if (entity == null || entity.IsDisposed)
                        {
                            nullSkipped++;
                            continue;
                        }
                        if (hasCoverSystem) coverChecked++;
                        if (CheckCoverSystem(entity))
                        {
                            coverSkipped++;
                            continue;
                        }
                        entityRuns++;
                        using var entityMeasurement = YuoCoreTrace.Measure(TraceEntityStage);
                        RunEntity(entity);
                    }
                }
                else
                {
                    for (var i = 0; i < count; i++)
                    {
                        var entity = dense[i];
                        scanned++;
                        if (entity == null || entity.IsDisposed)
                        {
                            nullSkipped++;
                            continue;
                        }
                        if (hasCoverSystem) coverChecked++;
                        if (CheckCoverSystem(entity))
                        {
                            coverSkipped++;
                            continue;
                        }
                        try
                        {
                            entityRuns++;
                            using var entityMeasurement = YuoCoreTrace.Measure(TraceEntityStage);
                            RunEntity(entity);
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine($"[YuoECS] System '{SystemName}' failed on entity {entity.EntityName}(ID:{entity.ID}): {e.Message}\n{e.StackTrace}");
                        }
                    }
                }
            }
            finally
            {
                _isIteratingEntities = false;
                // 循环只做本地累计，避免每个实体都进入宿主 Meter 监听器。
                YuoCoreTrace.Count("yuocore.system.entity_scan", scanned);
                YuoCoreTrace.Count(TraceScanCount, scanned);
                if (nullSkipped > 0)
                {
                    YuoCoreTrace.Count("yuocore.system.null_skip", nullSkipped);
                    YuoCoreTrace.Count(TraceNullSkipCount, nullSkipped);
                }
                if (coverChecked > 0) YuoCoreTrace.Count("yuocore.system.cover_check.count", coverChecked);
                YuoCoreTrace.Count("yuocore.system.cover_skip", coverSkipped);
                YuoCoreTrace.Count(TraceCoverSkipCount, coverSkipped);
                YuoCoreTrace.Count("yuocore.system.entity_run.count", entityRuns);
                YuoCoreTrace.Count(TraceEntityRunCount, entityRuns);
                using (YuoCoreTrace.Measure(TracePendingStage))
                    ApplyPendingEntityChanges();
            }

#if UNITY_EDITOR
            StopClock();
#endif
        }

        private bool _isIteratingEntities;
        private HashSet<YuoEntity> _pendingJoinEntities;
        private HashSet<YuoEntity> _pendingLeaveEntities;
        private string _traceRunStage;
        private string _traceEntityStage;
        private string _traceCoverStage;
        private string _tracePendingStage;
        private string _traceScanCount;
        private string _traceNullSkipCount;
        private string _traceCoverSkipCount;
        private string _traceEntityRunCount;

        private string TraceRunStage => _traceRunStage ??= $"yuocore.system.run.{SystemName}";
        private string TraceEntityStage => _traceEntityStage ??= $"yuocore.system.entity_run.{SystemName}";
        private string TraceCoverStage => _traceCoverStage ??= $"yuocore.system.cover_check.{SystemName}";
        private string TracePendingStage => _tracePendingStage ??= $"yuocore.system.pending_apply.{SystemName}";
        private string TraceScanCount => _traceScanCount ??= $"yuocore.system.entity_scan.{SystemName}";
        private string TraceNullSkipCount => _traceNullSkipCount ??= $"yuocore.system.null_skip.{SystemName}";
        private string TraceCoverSkipCount => _traceCoverSkipCount ??= $"yuocore.system.cover_skip.{SystemName}";
        private string TraceEntityRunCount => _traceEntityRunCount ??= $"yuocore.system.entity_run.{SystemName}.count";

        // 稠密实体集合：公开的 Entities(HashSet) 仅作成员视图供 Contains/枚举兼容，
        // 实际迭代走 _denseEntities 连续数组，缓存友好且无迭代器节点开销。
        // 删除置墓碑(null)保序不移位；加入追加尾部维持加入顺序稳定。
        // 反查表按世界内 EntityIndex 建 int 稀疏数组（存槽位+1，0 为空），
        // 比 Dictionary 更省内存且无哈希开销。
        private YuoEntity[] _denseEntities = Array.Empty<YuoEntity>();
        private int[] _entitySlots = Array.Empty<int>();
        private int _denseCount;   // 已用槽位数（含墓碑）
        private int _denseHoles;   // 墓碑数
        private int _denseLive;    // 存活实体数

        // World 在一次批量推进前可设置活动实体集合。该集合按本 System 的加入顺序
        // 生成独立稠密数组，避免每个 Tick 重新扫描所有匹配实体。
        private YuoEntity[] _activeEntities = Array.Empty<YuoEntity>();
        private int[] _activeEntityMarks = Array.Empty<int>();
        private int _activeGeneration;
        private int _activeCount;
        private bool _activeSelectionEnabled;

        // 迭代期间收到活动集切换请求时的挂起状态，本轮迭代结束后生效
        private IReadOnlyList<YuoEntity> _pendingActiveEntities;
        private bool _pendingActiveClear;

        internal void SetActiveEntities(IReadOnlyList<YuoEntity> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));
            // 迭代期间切换活动集会压实/换底层数组，先挂起到本轮结束统一生效
            if (_isIteratingEntities)
            {
                _pendingActiveEntities = entities;
                _pendingActiveClear = false;
                return;
            }

            if (_denseHoles > 0 || _denseLive != Entities.Count)
            {
                CompactDenseEntities();
            }

            using var measurement = YuoCoreTrace.Measure("yuocore.system.active_query");
            YuoCoreTrace.Count("yuocore.system.active_query.count");
            _activeSelectionEnabled = true;
            _activeGeneration++;
            if (_activeGeneration == int.MaxValue)
            {
                Array.Clear(_activeEntityMarks, 0, _activeEntityMarks.Length);
                _activeGeneration = 1;
            }

            if (_activeEntities.Length < entities.Count)
            {
                Array.Resize(ref _activeEntities, entities.Count);
            }

            var generation = _activeGeneration;
            var activeCount = 0;
            for (var i = 0; i < entities.Count; i++)
            {
                var entity = entities[i];
                if (entity == null || entity.IsDisposed) continue;
                var entityIndex = entity.EntityIndex;
                if ((uint)entityIndex >= (uint)_entitySlots.Length) continue;
                var slot = _entitySlots[entityIndex];
                if (slot == 0 || !ReferenceEquals(_denseEntities[slot - 1], entity)) continue;

                if (_activeEntityMarks.Length <= entityIndex)
                {
                    var size = _activeEntityMarks.Length;
                    while (size <= entityIndex) size = Math.Max(4, size * 2);
                    Array.Resize(ref _activeEntityMarks, size);
                }

                // 同一实体在输入中重复出现时仍然只执行一次。
                if (_activeEntityMarks[entityIndex] == generation) continue;
                _activeEntityMarks[entityIndex] = generation;
                _activeEntities[activeCount++] = entity;
            }

            // 公开 Entities 的迭代顺序是加入顺序；按稠密槽位做稳定插入排序，
            // 使活动查询不会因调用方传入顺序变化而改变业务结果。
            for (var i = 1; i < activeCount; i++)
            {
                var value = _activeEntities[i];
                var valueSlot = _entitySlots[value.EntityIndex];
                var j = i - 1;
                while (j >= 0 && _entitySlots[_activeEntities[j].EntityIndex] > valueSlot)
                {
                    _activeEntities[j + 1] = _activeEntities[j];
                    j--;
                }

                _activeEntities[j + 1] = value;
            }

            if (_activeCount > activeCount)
            {
                Array.Clear(_activeEntities, activeCount, _activeCount - activeCount);
            }
            _activeCount = activeCount;
            YuoCoreTrace.Count("yuocore.system.active_entity.count", activeCount);
        }

        internal void ClearActiveEntities()
        {
            if (_isIteratingEntities)
            {
                _pendingActiveEntities = null;
                _pendingActiveClear = true;
                return;
            }

            if (!_activeSelectionEnabled) return;
            Array.Clear(_activeEntities, 0, _activeCount);
            _activeCount = 0;
            _activeSelectionEnabled = false;
        }

        private void AddEntity(YuoEntity entity)
        {
            var entityIndex = entity.EntityIndex;
            // 已释放实体的索引为 -1，不得再进入成员集
            if (entityIndex < 0) return;
            if (!Entities.Add(entity)) return;

            if ((uint)entityIndex < (uint)_entitySlots.Length)
            {
                var staleSlot = _entitySlots[entityIndex];
                // 槽位被索引复用前的旧实体残留占用：该残留必然失效，清掉再写入
                if (staleSlot != 0 && !ReferenceEquals(_denseEntities[staleSlot - 1], entity))
                    _entitySlots[entityIndex] = 0;
            }
            else
            {
                var size = _entitySlots.Length;
                while (size <= entityIndex) size = Math.Max(4, size * 2);
                Array.Resize(ref _entitySlots, size);
            }

            var dense = _denseEntities;
            if (_denseCount == dense.Length)
            {
                Array.Resize(ref _denseEntities, Math.Max(4, dense.Length * 2));
                dense = _denseEntities;
            }

            _entitySlots[entityIndex] = _denseCount + 1;
            dense[_denseCount++] = entity;
            _denseLive++;
        }

        private void RemoveEntity(YuoEntity entity)
        {
            if (!Entities.Remove(entity)) return;

            var entityIndex = entity.EntityIndex;
            if ((uint)entityIndex < (uint)_entitySlots.Length)
            {
                var slot = _entitySlots[entityIndex];
                // 槽位必须仍指向本实体才允许墓碑化；索引已被复用时不得误伤新实体
                if (slot != 0 && ReferenceEquals(_denseEntities[slot - 1], entity))
                {
                    _denseEntities[slot - 1] = null;
                    _entitySlots[entityIndex] = 0;
                    _denseHoles++;
                    _denseLive--;
                }
            }

            // 实体不在稠密集（外部直写、索引复用、越界等）：_denseLive 未含该成员，
            // 不做扣减，等下次压实重建统一校正。
        }

        private void CompactDenseEntities()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.system.compact_entities");
            YuoCoreTrace.Count("yuocore.system.compact_entities.count");
            if (_denseLive != Entities.Count)
            {
                // 与公开集合出现偏差（外部直接改写 Entities）：以 HashSet 为准重建。
                // 重建同时清掉成员集中的已释放实体（其 EntityIndex 已失效，不得写入槽位表）。
                var entities = Entities;
                if (_denseEntities.Length < entities.Count)
                {
                    Array.Resize(ref _denseEntities, entities.Count);
                }

                Array.Clear(_entitySlots, 0, _entitySlots.Length);
                _denseCount = 0;
                List<YuoEntity> dead = null;
                foreach (var entity in entities)
                {
                    if (entity == null || entity.IsDisposed)
                    {
                        (dead ??= new List<YuoEntity>()).Add(entity);
                        continue;
                    }

                    var entityIndex = entity.EntityIndex;
                    if (entityIndex >= _entitySlots.Length)
                    {
                        var size = _entitySlots.Length;
                        while (size <= entityIndex) size = Math.Max(4, size * 2);
                        Array.Resize(ref _entitySlots, size);
                    }

                    _denseEntities[_denseCount] = entity;
                    _entitySlots[entityIndex] = _denseCount + 1;
                    _denseCount++;
                }

                if (dead != null)
                {
                    foreach (var entity in dead)
                    {
                        entities.Remove(entity);
                    }
                }

                _denseLive = _denseCount;
                _denseHoles = 0;
                return;
            }

            // 保序压实：非空槽位前移，其余槽位置空，同步槽位表。
            // 迭代期间被销毁的实体同样移出（其索引已失效，槽位表不可再触碰）。
            var denseArr = _denseEntities;
            var write = 0;
            for (var i = 0; i < _denseCount; i++)
            {
                var entity = denseArr[i];
                if (entity == null) continue;
                if (entity.IsDisposed)
                {
                    denseArr[i] = null;
                    Entities.Remove(entity);
                    continue;
                }

                if (write != i)
                {
                    denseArr[write] = entity;
                    denseArr[i] = null;
                    _entitySlots[entity.EntityIndex] = write + 1;
                }

                write++;
            }

            _denseCount = write;
            _denseHoles = 0;
            _denseLive = write;
        }

        private void ApplyPendingEntityChanges()
        {
            if (_pendingJoinEntities is { Count: > 0 })
            {
                foreach (var entity in _pendingJoinEntities)
                {
                    AddEntity(entity);
                }

                _pendingJoinEntities.Clear();
            }

            if (_pendingLeaveEntities is { Count: > 0 })
            {
                foreach (var entity in _pendingLeaveEntities)
                {
                    RemoveEntity(entity);
                }

                _pendingLeaveEntities.Clear();
            }

            // 迭代期间请求的活动集切换在成员变更之后统一生效
            if (_pendingActiveClear)
            {
                _pendingActiveClear = false;
                ClearActiveEntities();
            }
            else if (_pendingActiveEntities != null)
            {
                var pending = _pendingActiveEntities;
                _pendingActiveEntities = null;
                SetActiveEntities(pending);
            }
        }

        internal virtual void Clear()
        {
            Entities.Clear();
            Array.Clear(_denseEntities, 0, _denseCount);
            Array.Clear(_entitySlots, 0, _entitySlots.Length);
            _denseCount = 0;
            _denseHoles = 0;
            _denseLive = 0;
            ClearActiveEntities();
            Array.Clear(_activeEntityMarks, 0, _activeEntityMarks.Length);
            _activeGeneration = 0;
            systemTags.Clear();
            influenceTypes.Clear();
            _requiredComponentIds = null;
            _requiredComponentIdsSourceCount = -1;
            _pendingJoinEntities?.Clear();
            _pendingLeaveEntities?.Clear();
            _pendingActiveEntities = null;
            _pendingActiveClear = false;
        }

        // influenceTypes 对应的组件 id 缓存：构建后掩码按位判定代替逐类型 HasComponent。
        // 记录构建时的列表长度，长度变化时重建以兼容运行期改写 influenceTypes 的用法。
        private int[] _requiredComponentIds;
        private int _requiredComponentIdsSourceCount = -1;

        private int[] RequiredComponentIds
        {
            get
            {
                var ids = _requiredComponentIds;
                if (ids == null || _requiredComponentIdsSourceCount != influenceTypes.Count)
                {
                    ids = new int[influenceTypes.Count];
                    for (var i = 0; i < ids.Length; i++)
                    {
                        ids[i] = ComponentTypeRegistry.GetOrAddId(influenceTypes[i]);
                    }

                    _requiredComponentIds = ids;
                    _requiredComponentIdsSourceCount = influenceTypes.Count;
                }

                return ids;
            }
        }

        internal virtual bool MatchesRequiredComponents(YuoEntity entity)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.system.match_components");
            YuoCoreTrace.Count("yuocore.system.match_components.count");
            var required = RequiredComponentIds;
            for (var i = 0; i < required.Length; i++)
            {
                // 掩码未命中再走字典兜底，兼容绕过 SetComponent 直写 Components 的边角
                if (!entity.TestComponentMask(required[i]) && !entity.Components.ContainsKey(influenceTypes[i]))
                    return false;
            }

            return true;
        }

        internal virtual bool CheckValidEntity(YuoEntity entity)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.system.check_valid_entity");
            YuoCoreTrace.Count("yuocore.system.check_valid_entity.count");
            // 已释放实体不得加入成员集（索引已回收，成员集会因残留产生幽灵回调）
            if (entity == null || entity.IsDisposed) return false;
            if (!MatchesRequiredComponents(entity)) return false;

            if (_isIteratingEntities)
            {
                (_pendingJoinEntities ??= new()).Add(entity);
                _pendingLeaveEntities?.Remove(entity);
            }
            else
            {
                AddEntity(entity);
            }

            return true;
        }

        internal void RemoveComponent(YuoEntity entity)
        {
            if (_isIteratingEntities)
            {
                if (Entities.Contains(entity) || (_pendingJoinEntities?.Contains(entity) ?? false))
                {
                    (_pendingLeaveEntities ??= new()).Add(entity);
                    _pendingJoinEntities?.Remove(entity);
                }

                return;
            }

            RemoveEntity(entity);
        }

        internal bool m_Run(YuoEntity entity)
        {
            if (!Enabled) return false;
            // 已释放实体一律跳过：稠密索引可能已被新实体复用，继续执行会串读到他人组件
            if (entity == null || entity.IsDisposed) return false;
            if (!Entities.Contains(entity))
            {
                YuoCoreTrace.Count("yuocore.system.single_skip.not_member");
                return false;
            }
            if (hasCoverSystem) YuoCoreTrace.Count("yuocore.system.cover_check.count");
            if (CheckCoverSystem(entity))
            {
                YuoCoreTrace.Count("yuocore.system.cover_skip");
                return false;
            }
            YuoCoreTrace.Count("yuocore.system.single_run.count");
            using var runMeasurement = YuoCoreTrace.Measure(TraceEntityStage);

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

        /// <summary>
        /// RunEntity 执行前的统一判定：组件必须归属该实体且处于可运行状态。
        /// 归属校验阻止稠密索引复用后串读到新实体的组件；
        /// IDestroy 派发放行已标记待销毁但尚未释放的组件（缓冲回放路径），其余标签仍要求可用。
        /// </summary>
        protected bool CanRunComponent(YuoComponent component, YuoEntity entity)
        {
            return component != null
                && ReferenceEquals(component.Entity, entity)
                && (component.IsUsable() || (RunType == typeof(IDestroy) && !component.IsDisposed));
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

        // baseTypes 对应的组件 id 缓存，语义同 RequiredComponentIds。
        private int[] _requiredBaseTypeIds;
        private int _requiredBaseTypeIdsSourceCount = -1;

        private int[] RequiredBaseTypeIds
        {
            get
            {
                var ids = _requiredBaseTypeIds;
                if (ids == null || _requiredBaseTypeIdsSourceCount != baseTypes.Count)
                {
                    ids = new int[baseTypes.Count];
                    for (var i = 0; i < ids.Length; i++)
                    {
                        ids[i] = ComponentTypeRegistry.GetOrAddId(baseTypes[i]);
                    }

                    _requiredBaseTypeIds = ids;
                    _requiredBaseTypeIdsSourceCount = baseTypes.Count;
                }

                return ids;
            }
        }

        internal override bool CheckValidEntity(YuoEntity entity)
        {
            var requiredBase = RequiredBaseTypeIds;
            for (var i = 0; i < requiredBase.Length; i++)
            {
                // 掩码未命中再走 BaseComponents 兜底，与 HasBaseComponent 口径一致
                if (!entity.TestBaseComponentMask(requiredBase[i]) && !entity.BaseComponents.ContainsKey(baseTypes[i]))
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
            _requiredBaseTypeIds = null;
            _requiredBaseTypeIdsSourceCount = -1;
        }
    }
}
