using System;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 指定System所属的Group
    /// 作用范围：System类
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class SystemGroupAttribute : Attribute
    {
        public Type GroupType { get; }

        /// <summary>
        /// 指定系统所属的组
        /// </summary>
        /// <param name="groupType">组类型，必须继承自SystemGroupBase</param>
        public SystemGroupAttribute(Type groupType)
        {
            GroupType = groupType;
        }
    }

    /// <summary>
    /// 指定当前Group在目标Group之前执行
    /// 作用范围：SystemGroupBase派生类（组定义）
    /// 注意：此特性用于定义组之间的执行顺序，不用于System类
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class SystemGroupOrderBeforeAttribute : Attribute
    {
        public Type TargetType { get; }

        /// <summary>
        /// 在指定类型之前执行
        /// </summary>
        /// <param name="targetType">目标System或Group类型</param>
        public SystemGroupOrderBeforeAttribute(Type targetType)
        {
            TargetType = targetType;
        }
    }

    /// <summary>
    /// 指定当前Group在目标Group之后执行
    /// 作用范围：SystemGroupBase派生类（组定义）
    /// 注意：此特性用于定义组之间的执行顺序，不用于System类
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class SystemGroupOrderAfterAttribute : Attribute
    {
        public Type TargetType { get; }

        /// <summary>
        /// 在指定类型之后执行
        /// </summary>
        /// <param name="targetType">目标System或Group类型</param>
        public SystemGroupOrderAfterAttribute(Type targetType)
        {
            TargetType = targetType;
        }
    }

    /// <summary>
    /// 标记接口，用于标识SystemGroup
    /// </summary>
    public interface ISystemGroup
    {
        string GroupName { get; }
    }

    /// <summary>
    /// 默认的系统组基类
    /// </summary>
    public abstract class SystemGroupBase : ISystemGroup
    {
        public virtual string GroupName => GetType().Name;
    }
}

