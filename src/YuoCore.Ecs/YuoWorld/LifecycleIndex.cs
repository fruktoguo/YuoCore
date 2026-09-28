using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// componentId/tagId → 生命周期命中系统列表。
        /// 表中存放的就是 systemsOfComponent / systemsOfBaseComponent / systemsOfTag
        /// 内部的同一个 List 实例，
        /// 注册、排序（SystemSort 中对 Values 排序）、RemoveSystem 的删除天然同步，
        /// 生命周期分发时免 Type 字典查找。
        /// </summary>
        private List<SystemBase>[] _systemsByComponentId = new List<SystemBase>[64];
        private List<SystemBaseBase>[] _systemsByBaseTypeId = new List<SystemBaseBase>[64];
        private List<SystemBase>[] _systemsByTagId = new List<SystemBase>[64];

        /// <summary>按 componentId 取命中系统列表；无命中返回 null。</summary>
        internal List<SystemBase> GetSystemsByComponentId(int componentId)
        {
            var table = _systemsByComponentId;
            return (uint)componentId < (uint)table.Length ? table[componentId] : null;
        }

        /// <summary>按 componentId 取命中基础组件系统列表；无命中返回 null。</summary>
        internal List<SystemBaseBase> GetSystemsByBaseTypeId(int componentId)
        {
            var table = _systemsByBaseTypeId;
            return (uint)componentId < (uint)table.Length ? table[componentId] : null;
        }

        /// <summary>按 tagId 取标签命中系统列表；无命中返回 null。</summary>
        internal List<SystemBase> GetSystemsByTagId(int tagId)
        {
            var table = _systemsByTagId;
            return (uint)tagId < (uint)table.Length ? table[tagId] : null;
        }

        /// <summary>
        /// 注册系统与组件类型的命中关系；与 systemsOfComponent.AddItem 同点位调用。
        /// </summary>
        private void IndexSystemForComponent(Type componentType)
        {
            systemsOfComponent.TryGetValue(componentType, out var list);
            var id = ComponentTypeRegistry.GetOrAddId(componentType);
            if (id >= _systemsByComponentId.Length)
            {
                var size = _systemsByComponentId.Length;
                while (size <= id) size <<= 1;
                Array.Resize(ref _systemsByComponentId, size);
            }

            _systemsByComponentId[id] = list;
        }

        /// <summary>
        /// 注册基础组件系统命中关系；与 systemsOfBaseComponent.AddItem 同点位调用。
        /// </summary>
        private void IndexBaseSystemForComponent(Type baseType)
        {
            systemsOfBaseComponent.TryGetValue(baseType, out var list);
            var id = ComponentTypeRegistry.GetOrAddId(baseType);
            if (id >= _systemsByBaseTypeId.Length)
            {
                var size = _systemsByBaseTypeId.Length;
                while (size <= id) size <<= 1;
                Array.Resize(ref _systemsByBaseTypeId, size);
            }

            _systemsByBaseTypeId[id] = list;
        }

        /// <summary>
        /// 注册标签与系统的命中关系；与 systemsOfTag.AddItem 同点位调用。
        /// </summary>
        private void IndexSystemForTag(Type tagType)
        {
            systemsOfTag.TryGetValue(tagType, out var list);
            var id = ComponentTypeRegistry.GetOrAddId(tagType);
            if (id >= _systemsByTagId.Length)
            {
                var size = _systemsByTagId.Length;
                while (size <= id) size <<= 1;
                Array.Resize(ref _systemsByTagId, size);
            }

            _systemsByTagId[id] = list;
        }

        /// <summary>
        /// 清空组件命中索引。仅在对应 MultiMap 被整体 Clear/重建时调用；
        /// systemsOfBaseComponent 若未清理则其索引列表仍是同一引用，无需同步。
        /// </summary>
        private void ResetComponentSystemIndex()
        {
            Array.Clear(_systemsByComponentId, 0, _systemsByComponentId.Length);
        }

        /// <summary>清空标签命中索引。仅在 systemsOfTag 被整体 Clear/重建时调用。</summary>
        private void ResetTagSystemIndex()
        {
            Array.Clear(_systemsByTagId, 0, _systemsByTagId.Length);
        }

        /// <summary>清空基类命中索引。仅在 systemsOfBaseComponent 被整体 Clear/重建时调用。</summary>
        private void ResetBaseComponentSystemIndex()
        {
            Array.Clear(_systemsByBaseTypeId, 0, _systemsByBaseTypeId.Length);
        }

        /// <summary>清空全部生命周期索引（世界销毁/重建时）。</summary>
        private void ResetLifecycleIndex()
        {
            Array.Clear(_systemsByComponentId, 0, _systemsByComponentId.Length);
            Array.Clear(_systemsByBaseTypeId, 0, _systemsByBaseTypeId.Length);
            Array.Clear(_systemsByTagId, 0, _systemsByTagId.Length);
        }
    }
}
