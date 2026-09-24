using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// 按 ComponentTypeId 索引的稀疏组件存储表。
        /// 存储内容与 entity.Components 字典严格同步（同一点位、相同 key 类型写入）。
        /// </summary>
        private ComponentStore[] _componentStores = new ComponentStore[64];

        /// <summary>世界内实体稠密索引高水位。</summary>
        private int _entityIndexTop;

        /// <summary>回收待复用的实体索引。</summary>
        private readonly Stack<int> _freeEntityIndices = new();

        /// <summary>
        /// 分配世界内实体稠密索引。复用回收索引前会先清掉该索引上的残留槽位，
        /// 保证旧实体遗留数据不会泄漏给新实体。
        /// </summary>
        internal int AllocateEntityIndex()
        {
            if (_freeEntityIndices.Count > 0)
            {
                var index = _freeEntityIndices.Pop();
                SweepEntityStores(index);
                return index;
            }

            return _entityIndexTop++;
        }

        /// <summary>
        /// 释放实体索引：清理该实体在所有稀疏存储中的槽位并回收索引。
        /// </summary>
        internal void ReleaseEntityIndex(int entityIndex)
        {
            if (entityIndex < 0) return;
            SweepEntityStores(entityIndex);
            _freeEntityIndices.Push(entityIndex);
        }

        private void SweepEntityStores(int entityIndex)
        {
            var stores = _componentStores;
            for (var i = 0; i < stores.Length; i++)
            {
                stores[i]?.Remove(entityIndex);
            }
        }

        internal ComponentStore GetComponentStore(int componentId)
        {
            var stores = _componentStores;
            return (uint)componentId < (uint)stores.Length ? stores[componentId] : null;
        }

        internal ComponentStore GetOrCreateComponentStore(int componentId)
        {
            var stores = _componentStores;
            if ((uint)componentId >= (uint)stores.Length)
            {
                var newSize = stores.Length;
                while (newSize <= componentId) newSize <<= 1;
                Array.Resize(ref _componentStores, newSize);
                stores = _componentStores;
            }

            return stores[componentId] ??= new ComponentStore(componentId, ComponentTypeRegistry.GetType(componentId));
        }

        internal ComponentStore GetOrCreateComponentStore(Type componentType)
        {
            return GetOrCreateComponentStore(ComponentTypeRegistry.GetOrAddId(componentType));
        }

        /// <summary>
        /// 与 entity.Components[keyType]=component 同点位的存储写入。
        /// </summary>
        internal void StoreSetComponent(YuoEntity entity, Type keyType, YuoComponent component)
        {
            GetOrCreateComponentStore(ComponentTypeRegistry.GetOrAddId(keyType)).Set(entity.EntityIndex, component);
        }

        /// <summary>
        /// 与 entity.Components[keyType]=component 同点位的存储写入（已解析 id）。
        /// </summary>
        internal void StoreSetComponent(YuoEntity entity, int componentId, YuoComponent component)
        {
            GetOrCreateComponentStore(componentId).Set(entity.EntityIndex, component);
        }

        /// <summary>
        /// 与 entity.Components.Remove(keyType) 同点位的存储移除。
        /// </summary>
        internal void StoreRemoveComponent(YuoEntity entity, Type keyType)
        {
            var id = ComponentTypeRegistry.GetId(keyType);
            if (id < 0) return;
            GetComponentStore(id)?.Remove(entity.EntityIndex);
        }

        /// <summary>
        /// 与 entity.Components.Remove(keyType) 同点位的存储移除（已解析 id）。
        /// </summary>
        internal void StoreRemoveComponent(YuoEntity entity, int componentId)
        {
            GetComponentStore(componentId)?.Remove(entity.EntityIndex);
        }

        /// <summary>
        /// 初始化/销毁时重置存储与实体索引池。
        /// </summary>
        private void ResetComponentStores()
        {
            _componentStores = new ComponentStore[Math.Max(64, ComponentTypeRegistry.MaxId)];
            _entityIndexTop = 0;
            _freeEntityIndices.Clear();
        }
    }
}
