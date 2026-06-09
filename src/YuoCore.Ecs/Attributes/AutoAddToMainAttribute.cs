using System;

namespace YuoCore.Ecs
{
    public class AutoAddToMainAttribute : Attribute
    {
        public bool AutoAdd { get; }
        public AutoAddToMainAttribute(bool autoAdd = true)
        {
            AutoAdd = autoAdd;
        }
    }
}