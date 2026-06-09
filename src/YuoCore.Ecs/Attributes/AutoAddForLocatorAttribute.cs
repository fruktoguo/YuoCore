using System;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 标记此特性的组件会调用静态Get方法以初始化Locator缓存
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class AutoAddForLocatorAttribute : Attribute
    {
        public bool AutoAdd { get; }

        public AutoAddForLocatorAttribute(bool autoAdd = true)
        {
            AutoAdd = autoAdd;
        }
    }
}