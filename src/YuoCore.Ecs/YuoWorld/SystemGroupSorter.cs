using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 系统组排序器 - 负责处理Group级别的拓扑排序
    /// </summary>
    public static class SystemGroupSorter
    {
        /// <summary>
        /// 默认系统组类型，可通过配置修改
        /// </summary>
        public static Type DefaultGroupType { get; set; } = typeof(SystemGroupExtend);

        /// <summary>
        /// 系统组信息
        /// </summary>
        private class GroupInfo
        {
            public Type GroupType;
            public List<SystemBase> Systems = new();
            public HashSet<Type> GroupOrderBefore = new();
            public HashSet<Type> GroupOrderAfter = new();
            public int Order = 0;
        }

        // 复用的临时列表，避免每次调用都分配新列表
        [ThreadStatic] private static List<SystemBase> _tempInvalidSystems;
        [ThreadStatic] private static HashSet<Type> _tempOriginalTypes;
        [ThreadStatic] private static HashSet<Type> _tempSortedTypes;
        [ThreadStatic] private static List<Type> _tempMissingTypes;
        [ThreadStatic] private static List<Type> _tempCycleGroups;

        /// <summary>
        /// 对所有系统按照Group和Order进行排序
        /// </summary>
        /// <param name="allSystems">需要排序的所有系统</param>
        /// <param name="getOrder">获取系统优先级的函数，数值越小优先级越高</param>
        /// <param name="defaultGroupType">默认组类型，为null时使用DefaultGroupType</param>
        /// <returns>按正确顺序排列的系统列表</returns>
        /// <exception cref="ArgumentNullException">参数为null时抛出</exception>
        /// <exception cref="ArgumentException">发现无效系统时抛出</exception>
        /// <exception cref="InvalidOperationException">检测到循环依赖时抛出</exception>
        /// <remarks>
        /// 排序规则：
        /// 1. 首先按SystemGroup进行分组
        /// 2. 然后根据SystemOrderBefore/After特性对组进行拓扑排序
        /// 3. 最后在组内按SystemOrder数值排序
        /// </remarks>
        public static List<SystemBase> SortSystems(List<SystemBase> allSystems, Func<Type, int> getOrder, Type defaultGroupType = null)
        {
            defaultGroupType ??= DefaultGroupType;
            // 参数验证
            if (allSystems == null)
                throw new ArgumentNullException(nameof(allSystems), "系统列表不能为空");
            if (getOrder == null)
                throw new ArgumentNullException(nameof(getOrder), "排序函数不能为空");

            // 系统有效性验证（零分配优化）
            _tempInvalidSystems ??= new List<SystemBase>();
            _tempInvalidSystems.Clear();
            
            foreach (var system in allSystems)
            {
                if (system?.Type == null)
                {
                    _tempInvalidSystems.Add(system);
                }
            }
            
            if (_tempInvalidSystems.Count > 0)
            {
                throw new ArgumentException($"发现 {_tempInvalidSystems.Count} 个无效系统对象，请检查系统实例是否正确初始化");
            }

            try
            {
                // 1. 将系统分组
                var groups = GroupSystems(allSystems, defaultGroupType);

                // 2. 构建组之间的依赖关系
                BuildGroupDependencies(groups);

                // 3. 对组进行拓扑排序
                var sortedGroups = TopologicalSortGroups(groups);

                // 4. 组内按SystemOrder排序
                var result = SortSystemsWithinGroups(sortedGroups, getOrder);

                // 5. 验证排序结果
                ValidateSortResult(result, allSystems);

                // 新增：为每个系统设置SystemGroupType
                foreach (var system in result)
                {
                    var groupAttr = system.Type.GetCustomAttribute<SystemGroupAttribute>();
                    system.SystemGroupType = groupAttr?.GroupType ?? defaultGroupType;
                }
                
                return result;
            }
            catch (Exception ex) when (!(ex is ArgumentNullException || ex is ArgumentException || ex is InvalidOperationException))
            {
                throw new InvalidOperationException($"系统排序过程中发生未知错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 对组内系统进行排序
        /// </summary>
        private static List<SystemBase> SortSystemsWithinGroups(List<GroupInfo> sortedGroups, Func<Type, int> getOrder)
        {
            var result = new List<SystemBase>();
            
            foreach (var group in sortedGroups)
            {
                // 组内系统只按SystemOrder排序，移除错误的系统级依赖检查
                try
                {
                    group.Systems.Sort((a, b) =>
                    {
                        try
                        {
                            return getOrder(a.Type) - getOrder(b.Type);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"获取系统 {a.Type.Name} 的排序值时发生错误: {ex.Message}", ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"对组 {group.GroupType.Name} 内的系统进行排序时发生错误: {ex.Message}", ex);
                }

                result.AddRange(group.Systems);
            }

            return result;
        }

        /// <summary>
        /// 验证排序结果的完整性（零分配优化）
        /// </summary>
        private static void ValidateSortResult(List<SystemBase> sortedSystems, List<SystemBase> originalSystems)
        {
            if (sortedSystems.Count != originalSystems.Count)
            {
                // 使用复用的临时集合
                _tempOriginalTypes ??= new HashSet<Type>();
                _tempSortedTypes ??= new HashSet<Type>();
                _tempMissingTypes ??= new List<Type>();
                
                _tempOriginalTypes.Clear();
                _tempSortedTypes.Clear();
                _tempMissingTypes.Clear();
                
                // 手写循环替代 Select().ToHashSet()
                foreach (var system in originalSystems)
                {
                    _tempOriginalTypes.Add(system.Type);
                }
                
                foreach (var system in sortedSystems)
                {
                    _tempSortedTypes.Add(system.Type);
                }
                
                // 手写循环替代 Except().ToList()
                foreach (var type in _tempOriginalTypes)
                {
                    if (!_tempSortedTypes.Contains(type))
                    {
                        _tempMissingTypes.Add(type);
                    }
                }
                
                // 构建错误信息
                var missingNames = new StringBuilder();
                for (int i = 0; i < _tempMissingTypes.Count; i++)
                {
                    if (i > 0) missingNames.Append(", ");
                    missingNames.Append(_tempMissingTypes[i].Name);
                }
                
                throw new InvalidOperationException($"排序结果不完整，缺少 {_tempMissingTypes.Count} 个系统: {missingNames}");
            }
        }

        /// <summary>
        /// 将系统按照SystemGroupAttribute分组
        /// </summary>
        private static Dictionary<Type, GroupInfo> GroupSystems(List<SystemBase> allSystems, Type defaultGroupType)
        {
            var groups = new Dictionary<Type, GroupInfo>();

            // 确保默认组存在
            groups[defaultGroupType] = new GroupInfo { GroupType = defaultGroupType };

            foreach (var system in allSystems)
            {
                Type groupType;
                try
                {
                    var groupAttr = system.Type.GetCustomAttribute<SystemGroupAttribute>();
                    groupType = groupAttr?.GroupType ?? defaultGroupType;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"获取系统 {system.Type.Name} 的SystemGroupAttribute时发生错误: {ex.Message}", ex);
                }

                if (!groups.ContainsKey(groupType))
                {
                    groups[groupType] = new GroupInfo { GroupType = groupType };
                }

                groups[groupType].Systems.Add(system);
            }

            return groups;
        }

        /// <summary>
        /// 构建组之间的依赖关系
        /// </summary>
        private static void BuildGroupDependencies(Dictionary<Type, GroupInfo> groups)
        {
            foreach (var group in groups.Values)
            {
                var groupType = group.GroupType;

                try
                {
                    // 获取组类型上的SystemOrderBefore和SystemOrderAfter特性
                    var beforeAttrs = groupType.GetCustomAttributes<SystemGroupOrderBeforeAttribute>();
                    var afterAttrs = groupType.GetCustomAttributes<SystemGroupOrderAfterAttribute>();

                    foreach (var attr in beforeAttrs)
                    {
                        group.GroupOrderBefore.Add(attr.TargetType);
                    }

                    foreach (var attr in afterAttrs)
                    {
                        group.GroupOrderAfter.Add(attr.TargetType);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"获取组 {groupType.Name} 的依赖特性时发生错误: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 对组进行拓扑排序（Kahn算法）
        /// </summary>
        private static List<GroupInfo> TopologicalSortGroups(Dictionary<Type, GroupInfo> groups)
        {
            var result = new List<GroupInfo>();
            var inDegree = new Dictionary<Type, int>();
            var graph = new Dictionary<Type, List<Type>>();

            // 初始化图和入度
            foreach (var groupType in groups.Keys)
            {
                inDegree[groupType] = 0;
                graph[groupType] = new List<Type>();
            }

            // 构建依赖图
            foreach (var group in groups.Values)
            {
                // GroupOrderBefore: 当前组 -> 目标组
                foreach (var target in group.GroupOrderBefore)
                {
                    if (groups.ContainsKey(target))
                    {
                        graph[group.GroupType].Add(target);
                        inDegree[target]++;
                    }
                }

                // GroupOrderAfter: 目标组 -> 当前组
                foreach (var target in group.GroupOrderAfter)
                {
                    if (groups.ContainsKey(target))
                    {
                        graph[target].Add(group.GroupType);
                        inDegree[group.GroupType]++;
                    }
                }
            }

            // Kahn算法拓扑排序
            var queue = new Queue<Type>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value == 0)
                {
                    queue.Enqueue(kvp.Key);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(groups[current]);

                foreach (var neighbor in graph[current])
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // 检测循环依赖（零分配优化）
            if (result.Count != groups.Count)
            {
                _tempCycleGroups ??= new List<Type>();
                _tempCycleGroups.Clear();
                
                // 手写循环替代 Where().ToList()
                foreach (var groupType in groups.Keys)
                {
                    bool found = false;
                    foreach (var r in result)
                    {
                        if (r.GroupType == groupType)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        _tempCycleGroups.Add(groupType);
                    }
                }
                
                var errorMessage = BuildCycleErrorMessage(_tempCycleGroups, groups);
                throw new InvalidOperationException(errorMessage);
            }

            return result;
        }

        /// <summary>
        /// 构建循环依赖的详细错误信息（零分配优化）
        /// </summary>
        private static string BuildCycleErrorMessage(List<Type> cycleGroups, Dictionary<Type, GroupInfo> groups)
        {
            var sb = new StringBuilder();
            sb.AppendLine("检测到SystemGroup循环依赖:");
            
            foreach (var group in cycleGroups)
            {
                sb.AppendLine($"- {group.Name}");
                if (groups.TryGetValue(group, out var groupInfo))
                {
                    if (groupInfo.GroupOrderBefore.Count > 0)
                    {
                        sb.Append("  → 需要在这些组之前执行: ");
                        bool first = true;
                        foreach (var t in groupInfo.GroupOrderBefore)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(t.Name);
                            first = false;
                        }
                        sb.AppendLine();
                    }
                    if (groupInfo.GroupOrderAfter.Count > 0)
                    {
                        sb.Append("  → 需要在这些组之后执行: ");
                        bool first = true;
                        foreach (var t in groupInfo.GroupOrderAfter)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(t.Name);
                            first = false;
                        }
                        sb.AppendLine();
                    }
                }
            }
            
            sb.AppendLine();
            sb.AppendLine("解决方案:");
            sb.AppendLine("1. 检查[SystemGroupOrderBefore]和[SystemGroupOrderAfter]特性配置");
            sb.AppendLine("2. 确保组之间的依赖关系不会形成闭环");
            sb.AppendLine("3. 重新设计组的执行顺序，避免相互依赖");
            
            return sb.ToString();
        }
    }
}
