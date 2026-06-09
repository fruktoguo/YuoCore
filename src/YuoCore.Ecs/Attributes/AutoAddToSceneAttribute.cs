using System;

namespace YuoCore.Ecs
{
    public class AutoAddToSceneAttribute : Attribute
    {
        public bool AutoAdd { get; }
        public AutoAddToSceneAttribute(bool autoAdd = true)
        {
            AutoAdd = autoAdd;
        }
    }
}