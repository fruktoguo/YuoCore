using System;

namespace YuoCore.Ecs
{
    [AttributeUsage(AttributeTargets.Class)]
    public class SystemOrderAttribute : Attribute
    {
        public int Order { get; } = 0;

        /// <summary>
        /// system排序,数值更小的优先执行
        /// </summary>
        /// <param name="order"></param>
        public SystemOrderAttribute(int order = 0)
        {
            Order = order;
        }
    }
}