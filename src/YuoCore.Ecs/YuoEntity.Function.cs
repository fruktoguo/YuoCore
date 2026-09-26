using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using YuoCore.Ecs.Log;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 零分配的组件迭代器结构体，用于遍历指定类型的组件集合
    /// </summary>
    /// <typeparam name="T">组件类型</typeparam>
    public struct ComponentEnumerator<T> : IEnumerable<T>, IEnumerator<T> where T : YuoComponent
    {
        private HashSet<YuoComponent>.Enumerator _enumerator;
        private readonly HashSet<YuoComponent> _source;
        private readonly bool _hasSource;
        private T _current;

        public ComponentEnumerator(HashSet<YuoComponent> source)
        {
            _source = source;
            _hasSource = source != null && source.Count > 0;
            _enumerator = _hasSource ? source.GetEnumerator() : default;
            _current = null;
        }

        public T Current => _current;

        object IEnumerator.Current => _current;

        public bool MoveNext()
        {
            if (!_hasSource) return false;

            while (_enumerator.MoveNext())
            {
                if (_enumerator.Current is T typed)
                {
                    _current = typed;
                    return true;
                }
            }
            return false;
        }

        public void Reset()
        {
            if (_hasSource)
            {
                _enumerator = _source.GetEnumerator();
            }
            _current = null;
        }

        public void Dispose()
        {
            _enumerator.Dispose();
        }

        public ComponentEnumerator<T> GetEnumerator() => this;

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => this;

        /// <summary>
        /// 获取组件数量（零分配）
        /// </summary>
        public int Count => _source?.Count ?? 0;

        /// <summary>
        /// 检查是否有组件
        /// </summary>
        public bool Any => _hasSource;
    }

    public partial class YuoEntity
    {
        #region 组件管理

        public T GetComponent<T>() where T : YuoComponent
        {
            return GetComponent(typeof(T)) as T;
        }

        public T GetComponentAnyType<T>()
        {
            foreach (var component in Components.Values)
            {
                if (component is T data)
                {
                    return data;
                }
            }

            return default;
        }

        public bool TryGetComponentAnyType<T>(out T component)
        {
            foreach (var item in Components.Values)
            {
                if (item is T data)
                {
                    component = data;
                    return true;
                }
            }

            component = default;
            return false;
        }

        public List<T> GetComponentsAnyType<T>()
        {
            var list = new List<T>();
            foreach (var component in Components.Values)
            {
                if (component is T data)
                {
                    list.Add(data);
                }
            }

            return list;
        }

        public bool TryGetComponentsAnyType<T>(out List<T> components)
        {
            components = GetComponentsAnyType<T>();
            return components.Count > 0;
        }

        public bool TryGetComponent<T>(out T component) where T : YuoComponent
        {
            component = GetComponent<T>();
            return component != null;
        }

        public bool TryGetComponent(Type type, out YuoComponent component)
        {
            component = GetComponent(type);
            return component != null;
        }

        public bool TryGetBaseComponent<T>(out T component) where T : YuoComponent
        {
            component = GetBaseComponent<T>();
            return component != null;
        }

        public YuoComponent GetComponent(Type type)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.get_component");
            YuoCoreTrace.Count("yuocore.entity.get_component.count");
            if (IsDisposed) return null;
            if (Components.TryGetValue(type, out var component))
                return component;
#if UNITY_EDITOR
            //为空的时候尝试获取BaseComponent,仅在编辑器下生效,防止脑子瓦特的时候用错了
            if (GetBaseComponent(type) is { } c)
            {
                "应该使用[GetBaseComponent]".LogError();
                return c;
            }
#endif
            return null;
        }

        public YuoComponent GetBaseComponent(Type type)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.get_base_component");
            YuoCoreTrace.Count("yuocore.entity.get_base_component.count");
            if (IsDisposed) return null;
            return BaseComponents.TryGetValue(type, out var component)
                ? component.Count > 0 ? component.FirstOrDefault() : null
                : null;
        }

        public HashSet<YuoComponent> GetBaseComponents(Type type)
        {
            if (IsDisposed) return null;
            return BaseComponents.GetValueOrDefault(type);
        }

        /// <summary>
        ///  获取指定类型的子组件集合,可能会有gc
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        [Obsolete("此方法会产生GC分配，建议使用 GetBaseComponentsEnumerator<T>() 进行零分配遍历")]
        public List<T> GetBaseComponents<T>() where T : YuoComponent
        {
            if (IsDisposed) return null;
            return BaseComponents.TryGetValue(typeof(T), out var componentList)
                ? componentList.Cast<T>().ToList()
                : null;
        }

        /// <summary>
        /// 获取指定类型的子组件迭代器（零分配）
        /// 使用方式: foreach (var component in entity.GetBaseComponentsEnumerator&lt;T&gt;()) { ... }
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <returns>零分配的组件迭代器</returns>
        public ComponentEnumerator<T> GetBaseComponentsEnumerator<T>() where T : YuoComponent
        {
            if (IsDisposed) return new ComponentEnumerator<T>(null);
            BaseComponents.TryGetValue(typeof(T), out var componentList);
            return new ComponentEnumerator<T>(componentList);
        }

        /// <summary>
        /// 尝试获取指定类型的子组件迭代器（零分配）
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <param name="enumerator">输出的迭代器</param>
        /// <returns>是否存在该类型的组件</returns>
        public bool TryGetBaseComponentsEnumerator<T>(out ComponentEnumerator<T> enumerator) where T : YuoComponent
        {
            if (IsDisposed)
            {
                enumerator = new ComponentEnumerator<T>(null);
                return false;
            }

            if (BaseComponents.TryGetValue(typeof(T), out var componentList) && componentList.Count > 0)
            {
                enumerator = new ComponentEnumerator<T>(componentList);
                return true;
            }

            enumerator = new ComponentEnumerator<T>(null);
            return false;
        }

        /// <summary>
        /// 尝试获取指定类型的子组件集合（会产生GC分配）
        /// 建议使用 TryGetBaseComponentsEnumerator&lt;T&gt;() 进行零分配遍历
        /// </summary>
        [Obsolete("此方法会产生GC分配，建议使用 TryGetBaseComponentsEnumerator<T>() 进行零分配遍历")]
        public bool TryGetBaseComponents<T>(out List<T> components) where T : YuoComponent
        {
            if (IsDisposed)
            {
                components = null;
                return false;
            }

            if (BaseComponents.TryGetValue(typeof(T), out var componentList))
            {
                components = componentList.Cast<T>().ToList();
                return true;
            }

            components = null;
            return false;
        }

        /// <summary>
        /// 获取指定类型的基础组件,获取第一个
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetBaseComponent<T>() where T : YuoComponent
        {
            return GetBaseComponent(typeof(T)) as T;
        }

        /// <summary>
        /// 如果不存在该组件,则添加该组件
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T GetOrAddComponent<T>() where T : YuoComponent, new()
        {
            return TryGetComponent<T>(out var component) ? component : AddComponent<T>();
        }

        public bool HasComponent<T>() where T : YuoComponent
        {
            return Components.ContainsKey(typeof(T));
        }

        public bool HasBaseComponent<T>() where T : YuoComponent
        {
            return BaseComponents.ContainsKey(typeof(T));
        }

        public bool HasComponent(Type type)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.has_component");
            YuoCoreTrace.Count("yuocore.entity.has_component.count");
            return Components.ContainsKey(type);
        }

        public bool HasBaseComponent(Type type)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.has_base_component");
            YuoCoreTrace.Count("yuocore.entity.has_base_component.count");
            return BaseComponents.ContainsKey(type);
        }

        /// <summary>
        /// 替换Component
        /// </summary>
        /// <param name="component"></param>
        /// <param name="componentType"></param>
        /// <typeparam name="T"></typeparam>
        public void ReplaceComponent<T>(T component, Type componentType = null) where T : YuoComponent
        {
            if (componentType != null) component.SetComponentType(componentType);
            var componentTemp = GetComponent(component.Type);
            if (componentTemp == null)
            {
                component.Entity = this;
                Components.Add(component.Type, component);
                Parent?.ChildComponents.AddItem(component.Type, component);
                World.AddComponent(this, component);
            }
            else
            {
                Components[component.Type] = component;
                component.Entity = this;
                World.SetComponent(componentTemp, component);
            }
        }

        public YuoComponent AddComponent(Type type)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.add_component");
            YuoCoreTrace.Count("yuocore.entity.add_component.count");
            if (Components.ContainsKey(type)) return GetComponent(type);
            if (Activator.CreateInstance(type) is not YuoComponent component) return null;
            SetComponent(type, component);
            return component;
        }

        public T AddComponent<T>() where T : YuoComponent, new()
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.add_component");
            YuoCoreTrace.Count("yuocore.entity.add_component.count");
            if (GetComponent<T>() != null) return GetComponent<T>();
            T component = new T();
            SetComponent(typeof(T), component);
            return component;
        }

        public void SetComponent(Type type, YuoComponent component)
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.set_component");
            YuoCoreTrace.Count("yuocore.entity.set_component.count");
            // $"{this} AddComponent:{component.Name}".Log();
            component.Entity = this;
            Components.TryAdd(type, component);
            World.AddComponent(this, component);
            Parent?.AddChildComponent(type, component);
        }

        public void RemoveComponent<T>() where T : YuoComponent
        {
            RemoveComponent(GetComponent<T>());
        }

        public void RemoveComponent(Type type)
        {
            RemoveComponent(GetComponent(type));
        }

        public void DestroyComponent<T>() where T : YuoComponent
        {
            GetComponent<T>().DestroyComponent();
        }

        public void RemoveComponent<T>(T component) where T : YuoComponent
        {
            using var measurement = YuoCoreTrace.Measure("yuocore.entity.remove_component");
            YuoCoreTrace.Count("yuocore.entity.remove_component.count");
            if (component.IsNullOrDisposed()) return;
            if (!Components.ContainsKey(component.Type)) return;
            World.RemoveComponent(this, component);
            Components.Remove(component.Type);
            if (component.HasBaseType)
            {
                BaseComponents.RemoveItem(component.BaseComponentType, component);
            }

            Parent?.RemoveChildComponent(component);
        }

        #endregion

        #region 子实体管理

        private void AddChildComponent(Type type, YuoComponent component)
        {
            if (component?.Entity == null) return;
            ChildComponents.AddItem(type, component);
            Children.Add(component.Entity);
        }

        public List<T> GetChildren<T>() where T : YuoComponent
        {
            var children = new List<T>();
            if (ChildComponents.TryGetValue(typeof(T), out var cs))
            {
                foreach (var item in cs)
                {
                    children.Add(item as T);
                }
            }

            return children;
        }

        public T GetChild<T>() where T : YuoComponent
        {
            var cs = ChildComponents[typeof(T)];
            return cs is { Count: > 0 } ? cs.First() as T : null;
        }

        public T AddChild<T>(long entityID = long.MinValue) where T : YuoComponent, new()
        {
            return AddChild(typeof(T), entityID) as T;
        }

        public T AddChild<T>(string name) where T : YuoComponent, new()
        {
            var com = AddChild(typeof(T), IDGenerate.GetID(name)) as T;
            if (com != null) com.Entity.EntityName = name;
            return com;
        }

        public YuoEntity AddChild(long entityID = long.MinValue)
        {
            var child = entityID != long.MinValue ? new YuoEntity(World, entityID) : new YuoEntity(World);
            child.Parent = this;
            var type = typeof(EntityComponent);
            AddChildComponent(type, child.EntityData);
            return child;
        }

        public YuoComponent AddChild(Type type, long entityID = long.MinValue)
        {
            var child = AddChild(entityID);
            var component = child.AddComponent(type);
            AddChildComponent(type, component);
            return component;
        }

        public YuoEntity AddChild(string entityName)
        {
            var child = AddChild(IDGenerate.GetID(entityName));
            child.EntityName = entityName;
            return child;
        }

        public void RemoveChild(YuoEntity entity)
        {
            if (!IsDisposed)
            {
                if (Children.Contains(entity))
                    Children.Remove(entity);
                foreach (var item in entity.Components)
                {
                    RemoveChildComponent(item.Value);
                    ChildComponents.RemoveItem(item.Key, item.Value);
                }
            }
        }

        private void RemoveChildComponent(YuoComponent component)
        {
            if (component == null) return;
            ChildComponents.RemoveItem(component.Type, component);
        }

        internal void SetParent(YuoEntity parent)
        {
            if (Parent != null)
            {
                Parent.RemoveChild(this);
            }

            Parent = parent;
        }

        #endregion

        #region 生命周期

        public bool IsDisposed { get; private set; }
        public bool IsDestroy { get; internal set; }

        /// <summary>
        /// 释放实体,不要在System中调用,如需释放请调用Destroy
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;

            using var measurement = YuoCoreTrace.Measure("yuocore.entity.dispose");
            YuoCoreTrace.Count("yuocore.entity.dispose.count");

            // Debug.Log($"{this} 释放实体 frame{Time.frameCount}");
            foreach (var item in Components.Values.ToList())
            {
                // World.RemoveComponent(this, item);
                item.Dispose();
            }

            if (Parent != null) Parent.RemoveChild(this);
            World.UnRegisterEntity(this);

            foreach (var yuoEntity in Children.ToList())
            {
                // Debug.Log($"{this} 释放子实体:{yuoEntity}");
                yuoEntity.Dispose();
            }

            Components.Clear();
            ChildComponents.Clear();
            ChildComponents = null;

            World.RemoveComponent(this, EntityData);
            EntityData.Dispose();
            EntityData = null;
            IsDisposed = true;
            IsDestroy = true;
        }

        /// <summary>
        /// 扔进回收站,这一帧结束后会被回收
        /// </summary>
        public void Destroy()
        {
            if (IsDestroy) return;

            // Debug.Log($"{this} 扔进回收站 frame{Time.frameCount}");
            YuoWorld.DestroyEntity(this);
            IsDestroy = true;
        }

        #endregion

        #region AddWithData

        public YuoComponent AddComponent<T2>(Type type, T2 componentInitData)
        {
            if (Components.ContainsKey(type)) return GetComponent(type);
            if (Activator.CreateInstance(type) is not YuoComponent component) return null;
            component.Entity = this;
            if (component is IComponentInit<T2> init) init.ComponentInit(componentInitData);
            SetComponent(type, component);
            return component;
        }

        public T AddComponent<T, T2>(T2 componentInitData) where T : YuoComponent, IComponentInit<T2>, new()
        {
            if (GetComponent<T>() != null) return GetComponent<T>();
            T component = new T();
            component.Entity = this;
            component.ComponentInit(componentInitData);
            SetComponent(typeof(T), component);
            return component;
        }

        public T AddComponent<T, T2, T3>(T2 componentInitData2, T3 componentInitData3)
            where T : YuoComponent, IComponentInit<T2, T3>, new()
        {
            if (GetComponent<T>() != null) return GetComponent<T>();
            T component = new T();
            component.Entity = this;
            component.ComponentInit(componentInitData2, componentInitData3);
            SetComponent(typeof(T), component);
            return component;
        }

        public YuoComponent AddChild<T2>(Type type, T2 componentInitData, long entityID = long.MinValue)
        {
            var child = entityID != long.MinValue ? new YuoEntity(World, entityID) : new YuoEntity(World);
            child.Parent = this;
            var component = child.AddComponent(type, componentInitData);
            AddChildComponent(type, component);
            return component;
        }

        public T AddChild<T, T2>(T2 componentInitData) where T : YuoComponent, IComponentInit<T2>, new()
        {
            var child = new YuoEntity(World);
            child.Parent = this;
            var component = child.AddComponent<T, T2>(componentInitData);
            AddChildComponent(typeof(T), component);
            return component;
        }

        public T AddChild<T, T2, T3>(T2 componentInitData2, T3 componentInitData3)
            where T : YuoComponent, IComponentInit<T2, T3>, new()
        {
            var child = new YuoEntity(World);
            child.Parent = this;
            var component = child.AddComponent<T, T2, T3>(componentInitData2, componentInitData3);
            AddChildComponent(typeof(T), component);
            return component;
        }

        public T GetOrAddComponent<T, T2>(T2 componentInitData) where T : YuoComponent, IComponentInit<T2>, new()
        {
            if (TryGetComponent<T>(out var component))
            {
                component.ComponentInit(componentInitData);
                return component;
            }

            component = AddComponent<T, T2>(componentInitData);
            return component;
        }

        public T GetOrAddComponent<T, T2, T3>(T2 componentInitData2, T3 componentInitData3)
            where T : YuoComponent, IComponentInit<T2, T3>, new()
        {
            if (TryGetComponent<T>(out var component))
            {
                component.ComponentInit(componentInitData2, componentInitData3);
                return component;
            }

            component = AddComponent<T, T2, T3>(componentInitData2, componentInitData3);
            return component;
        }

        #endregion

        #region 运算符

        public static bool operator true(YuoEntity entity) => entity is { IsDisposed: false };
        public static bool operator false(YuoEntity entity) => entity is not { IsDisposed: false };
        public static bool operator !(YuoEntity entity) => entity is not { IsDisposed: false };

        #endregion

        #region 系统管理

        /// <summary>
        ///  runSystem的简化调用
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void RunSystem<T>() where T : ISystemTag
        {
            YuoWorld.RunSystem<T>(this);
        }

        #endregion

        #region 其他

        List<YuoEntity> tempList;

        /// <summary>
        ///  获取所有子Entity,包括子Entity的子Entity
        /// </summary>
        /// <returns></returns>
        public List<YuoEntity> GetAllChildren()
        {
            tempList ??= new();
            tempList.Clear();
            foreach (var child in Children)
            {
                tempList.Add(child);
                tempList.AddRange(child.GetAllChildren());
            }

            return tempList;
        }

        #endregion
    }
}
