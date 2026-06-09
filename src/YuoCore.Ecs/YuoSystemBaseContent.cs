using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    /// <summary>
    ///  用于执行BaseComponent的系统
    /// </summary>
    public abstract class YuoSystemOfBase<T1> : SystemBaseBase where T1 : YuoComponent
    {
        Type _type1;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            AddType(_type1, yuoWorld);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            var t1 = typeIsBase[0] ? entity.GetBaseComponent(_type1) : entity.GetComponent(_type1);

            if (t1 is T1 typed1 && typed1.IsUsable()) Run(typed1);
        }

        public abstract void Run(T1 baseComponent);
    }

    /// <summary>
    /// 用于执行BaseComponent和一个附加组件的系统（双组件）
    /// </summary>
    public abstract class YuoSystemOfBase<T1, T2> : SystemBaseBase
        where T1 : YuoComponent
        where T2 : YuoComponent
    {
        private Type _type1;
        private Type _type2;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            _type2 = typeof(T2);

            AddType(_type1, yuoWorld);
            AddType(_type2, yuoWorld);

            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            var t1 = typeIsBase[0] ? entity.GetBaseComponent(_type1) : entity.GetComponent(_type1);
            var t2 = typeIsBase[1] ? entity.GetBaseComponent(_type2) : entity.GetComponent(_type2);

            if (t1 is T1 typed1 && typed1.IsUsable() &&
                t2 is T2 typed2 && typed2.IsUsable()) Run(typed1, typed2);
        }

        public abstract void Run(T1 baseComponent, T2 component2);
    }
}
