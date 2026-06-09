using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    [Serializable]
    public abstract class YuoSystem<T1> : SystemBase where T1 : YuoComponent
    {
        private Type _type1;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            influenceTypes.Add(_type1);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            if (entity.TryGetComponent(_type1, out var t1) && t1.IsUsable())
            {
                Run(t1 as T1);
            }
        }

        public abstract void Run(T1 component);
    }

    [Serializable]
    public abstract class YuoSystem<T1, T2> : SystemBase
        where T1 : YuoComponent
        where T2 : YuoComponent
    {
        private Type _type1;
        private Type _type2;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            _type2 = typeof(T2);
            influenceTypes.Add(_type1);
            influenceTypes.Add(_type2);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            if (entity.TryGetComponent(_type1, out var t1) && t1.IsUsable() &&
                entity.TryGetComponent(_type2, out var t2) && t2.IsUsable())
            {
                Run(t1 as T1, t2 as T2);
            }
        }

        public abstract void Run(T1 component1, T2 component2);
    }

    [Serializable]
    public abstract class YuoSystem<T1, T2, T3> : SystemBase
        where T1 : YuoComponent
        where T2 : YuoComponent
        where T3 : YuoComponent
    {
        private Type _type1;
        private Type _type2;
        private Type _type3;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            _type2 = typeof(T2);
            _type3 = typeof(T3);
            influenceTypes.Add(_type1);
            influenceTypes.Add(_type2);
            influenceTypes.Add(_type3);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            if (entity.TryGetComponent(_type1, out var t1) && t1.IsUsable() &&
                entity.TryGetComponent(_type2, out var t2) && t2.IsUsable() &&
                entity.TryGetComponent(_type3, out var t3) && t3.IsUsable())
            {
                Run(t1 as T1, t2 as T2, t3 as T3);
            }
        }

        public abstract void Run(T1 component1, T2 component2, T3 component3);
    }

    [Serializable]
    public abstract class YuoSystem<T1, T2, T3, T4> : SystemBase
        where T1 : YuoComponent
        where T2 : YuoComponent
        where T3 : YuoComponent
        where T4 : YuoComponent
    {
        private Type _type1;
        private Type _type2;
        private Type _type3;
        private Type _type4;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            _type2 = typeof(T2);
            _type3 = typeof(T3);
            _type4 = typeof(T4);
            influenceTypes.Add(_type1);
            influenceTypes.Add(_type2);
            influenceTypes.Add(_type3);
            influenceTypes.Add(_type4);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            if (entity.TryGetComponent(_type1, out var t1) && t1.IsUsable() &&
                entity.TryGetComponent(_type2, out var t2) && t2.IsUsable() &&
                entity.TryGetComponent(_type3, out var t3) && t3.IsUsable() &&
                entity.TryGetComponent(_type4, out var t4) && t4.IsUsable())
            {
                Run(t1 as T1, t2 as T2, t3 as T3, t4 as T4);
            }
        }

        public abstract void Run(T1 component1, T2 component2, T3 component3, T4 component4);
    }
}
