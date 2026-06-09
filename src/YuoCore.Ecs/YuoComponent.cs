using System;

namespace YuoCore.Ecs
{
    public partial class YuoComponent : IDisposable
    {
        public YuoEntity Entity { get; set; }

        public virtual string ComponentName => Type.Name;

        public Type BaseComponentType { get; internal set; } = null;

        public bool IsDisposed { get; internal set; }

        public YuoComponent()
        {
            Type = GetType();
        }

        internal void SetComponentType(Type type)
        {
            Type = type;
        }

        public Type Type { get; private set; }

        public T GetComponent<T>() where T : YuoComponent
        {
            return Entity.GetComponent<T>();
        }

        public bool TryGetComponent<T>(out T component) where T : YuoComponent
        {
            return Entity.TryGetComponent(out component);
        }

        public bool HasComponent<T>() where T : YuoComponent
        {
            return Entity.HasComponent<T>();
        }

        public bool HasBaseType => BaseComponentType != null;

        /// <summary>
        /// 添加组件到实体
        /// </summary>
        /// <typeparam name="T">组件类型</typeparam>
        /// <returns></returns>
        public T AddComponent<T>() where T : YuoComponent, new()
        {
            return Entity.AddComponent<T>();
        }

        /// <summary>
        /// 释放组件,不要在System中调用,如需释放请调用Destroy
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            // GC.SuppressFinalize(this);
        }

        ~YuoComponent()
        {
            // Debug.Log($"~{this}");
            // Dispose(false);
        }

        public bool IsDestroy { get; internal set; }

        private void Dispose(bool disposing)
        {
            if (IsDisposed) return; //如果已经被回收，就中断执行
            if (disposing)
            {
            }
            // Debug.Log($"{this} 释放组件 frame{Time.frameCount}");
            Entity.RemoveComponent(this);
            Entity = null;
            IsDisposed = true;
            IsDestroy = true;
        }

        /// <summary>
        /// 扔进回收站,这一帧结束后会被回收
        /// </summary>
        public void DestroyComponent()
        {
            // (IsDestroy && !YuoWorld.Instance._worldIsDestroy).Assert("已经销毁");
            if (IsDestroy) return;

            // Debug.Log($"{this} 扔进回收站 frame{Time.frameCount}");
            YuoWorld.DestroyComponent(this);
            IsDestroy = true;
        }

        public static bool operator true(YuoComponent component) => component.IsUsable();

        public static bool operator false(YuoComponent component) => component.IsUnusable();

        public static bool operator !(YuoComponent component) => !component.IsUnusable();

        /// <summary>
        ///  runSystem的简化调用,system必须有这个组件才能执行
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void RunSystem<T>() where T : ISystemTag
        {
            YuoWorld.RunSystem<T>(this);
        }

        public void RunSystemOfBaseComponent<T>() where T : ISystemTag
        {
            YuoWorld.RunSystemOfBase<T>(this);
        }

        public override string ToString()
        {
            return ComponentName;
        }

        // #if UNITY_EDITOR
        //         // 仅在编辑器模式下启用的操作符重载
        //         public static bool operator ==(YuoComponent left, object right)
        //         {
        //             // 如果right不是null，使用引用相等性检查
        //             if (right != null)
        //                 return ReferenceEquals(left, right);
        //
        //             // right是null，检查left是否为null或已销毁
        //             return left is null || left.IsDisposed;
        //         }
        //
        //         public static bool operator !=(YuoComponent left, object right)
        //         {
        //             return !(left == right);
        //         }
        //
        //         // 编辑器模式下的Equals和GetHashCode
        //         public override bool Equals(object obj)
        //         {
        //             if (this.IsUnusable()) return obj == null;
        //             if (obj == null) return false;
        //             return ReferenceEquals(this, obj);
        //         }
        //
        //         public override int GetHashCode()
        //         {
        //             return IsDisposed ? 0 : base.GetHashCode();
        //         }
        // #endif
    }

    public class EntityComponent : YuoComponent
    {
        public override string ComponentName => "核心组件";
        public long Id { get; set; }
    }

    public partial class YuoEntity
    {
        public long ID => EntityData?.Id ?? -1;

        string _entityName = null;

        public string EntityName
        {
            get
            {
                if (_entityName == null)
                {
                    return ID.ToString();
                }

                return _entityName;
            }
            set => _entityName = value;
        }

        public override string ToString()
        {
            return EntityName + (IsDisposed ? "(已释放)" : "");
        }
    }
}
