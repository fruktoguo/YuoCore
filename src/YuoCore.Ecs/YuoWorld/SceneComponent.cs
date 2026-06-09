using System.Collections.Generic;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 场景组件类
    /// </summary>
    public class SceneComponent : YuoComponent
    {
        public HashSet<YuoEntity> Children  => Entity.Children;
    }
}