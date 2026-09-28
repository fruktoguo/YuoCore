using System;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 稀疏组件存储：单个组件类型在一个 YuoWorld 内的全部实例。
    /// sparse[entityIndex] -> densePos+1（0 表示不存在），dense 数组紧密存放组件本体。
    /// 语义与 entity.Components[type] 完全一致：存储只在 Components 字典真正写入的
    /// 同一点位按相同 key 类型同步更新，公开字典仍保留用于兼容外部枚举。
    /// dense 采用 swap-remove，内部迭代顺序不对外暴露。
    /// </summary>
    internal sealed class ComponentStore
    {
        internal readonly int ComponentId;
        internal readonly Type ComponentType;

        private int[] _sparse = new int[64];          // entityIndex -> densePos+1
        private int[] _denseEntities = new int[16];   // densePos -> entityIndex
        private YuoComponent[] _dense = new YuoComponent[16];
        private int _count;

        internal ComponentStore(int componentId, Type componentType)
        {
            ComponentId = componentId;
            ComponentType = componentType;
        }

        internal int Count => _count;

        /// <summary>稠密组件数组（前 Count 项有效），供系统查询计划直读。</summary>
        internal YuoComponent[] Dense => _dense;

        /// <summary>稠密实体索引数组（前 Count 项有效）。</summary>
        internal int[] DenseEntities => _denseEntities;

        /// <summary>
        /// 按实体索引取组件，不存在返回 null。等价于 Components.TryGetValue 命中语义。
        /// </summary>
        internal YuoComponent Get(int entityIndex)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.store.get");
            YuoCoreTrace.Count("yuocore.store.get.count");
            var sparse = _sparse;
            if ((uint)entityIndex >= (uint)sparse.Length)
            {
                YuoCoreTrace.Count("yuocore.store.get.miss");
                return null;
            }
            var pos = sparse[entityIndex];
            if (pos == 0)
            {
                YuoCoreTrace.Count("yuocore.store.get.miss");
                return null;
            }
            YuoCoreTrace.Count("yuocore.store.get.hit");
            return _dense[pos - 1];
        }

        /// <summary>
        /// 返回 densePos+1，0 表示不存在；供调用方一次定位后直读 Dense。
        /// </summary>
        internal int GetSlot(int entityIndex)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.store.get_slot");
            YuoCoreTrace.Count("yuocore.store.get_slot.count");
            var sparse = _sparse;
            return (uint)entityIndex < (uint)sparse.Length ? sparse[entityIndex] : 0;
        }

        internal bool Has(int entityIndex)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.store.has");
            YuoCoreTrace.Count("yuocore.store.has.count");
            var sparse = _sparse;
            return (uint)entityIndex < (uint)sparse.Length && sparse[entityIndex] != 0;
        }

        /// <summary>
        /// 写入组件；已存在同实体槽位时覆盖（等价字典索引器语义）。
        /// </summary>
        internal void Set(int entityIndex, YuoComponent component)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.store.set");
            YuoCoreTrace.Count("yuocore.store.set.count");
            // 已释放实体索引为 -1（与 Remove/Get 的负值保护口径一致）：
            // 字典侧写入照旧进行，但不向稀疏存储落位，避免越界并保证存储只承载存活实体。
            if (entityIndex < 0) return;
            EnsureSparse(entityIndex);
            var pos = _sparse[entityIndex];
            if (pos != 0)
            {
                _dense[pos - 1] = component;
                YuoCoreTrace.Count("yuocore.store.set.replaced");
                return;
            }

            if (_count == _dense.Length) GrowDense();
            pos = _count++;
            _dense[pos] = component;
            _denseEntities[pos] = entityIndex;
            _sparse[entityIndex] = pos + 1;
            YuoCoreTrace.Count("yuocore.store.set.inserted");
        }

        internal void Remove(int entityIndex)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.store.remove");
            YuoCoreTrace.Count("yuocore.store.remove.count");
            var sparse = _sparse;
            if ((uint)entityIndex >= (uint)sparse.Length) return;
            var pos = sparse[entityIndex];
            if (pos == 0) return;
            pos--;

            var last = --_count;
            if (pos != last)
            {
                var moved = _dense[last];
                var movedEntity = _denseEntities[last];
                _dense[pos] = moved;
                _denseEntities[pos] = movedEntity;
                _sparse[movedEntity] = pos + 1;
            }

            _dense[last] = null;
            _denseEntities[last] = 0;
            sparse[entityIndex] = 0;
            YuoCoreTrace.Count("yuocore.store.remove.success");
        }

        private void EnsureSparse(int entityIndex)
        {
            if (entityIndex < _sparse.Length) return;
            var newSize = _sparse.Length;
            while (newSize <= entityIndex) newSize <<= 1;
            Array.Resize(ref _sparse, newSize);
        }

        private void GrowDense()
        {
            var newSize = _dense.Length << 1;
            Array.Resize(ref _dense, newSize);
            Array.Resize(ref _denseEntities, newSize);
        }
    }
}
