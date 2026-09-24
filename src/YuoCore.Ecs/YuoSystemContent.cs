using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    [Serializable]
    public abstract class YuoSystem<T1> : SystemBase where T1 : YuoComponent
    {
        private Type _type1;
        private ComponentStore _store1;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            influenceTypes.Add(_type1);
            // 初始化即绑定组件存储，RunEntity 不再做 Type 字典解析
            _store1 = yuoWorld.GetOrCreateComponentStore(_type1);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            var pos = _store1.GetSlot(entity.EntityIndex);
            if (pos == 0) return;
            var t1 = _store1.Dense[pos - 1];
            if (t1.IsUsable())
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
        private ComponentStore _store1;
        private ComponentStore _store2;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            _type2 = typeof(T2);
            influenceTypes.Add(_type1);
            influenceTypes.Add(_type2);
            _store1 = yuoWorld.GetOrCreateComponentStore(_type1);
            _store2 = yuoWorld.GetOrCreateComponentStore(_type2);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            var entityIndex = entity.EntityIndex;
            var pos1 = _store1.GetSlot(entityIndex);
            if (pos1 == 0) return;
            var t1 = _store1.Dense[pos1 - 1];
            if (!t1.IsUsable()) return;
            var pos2 = _store2.GetSlot(entityIndex);
            if (pos2 == 0) return;
            var t2 = _store2.Dense[pos2 - 1];
            if (!t2.IsUsable()) return;
            Run(t1 as T1, t2 as T2);
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
        private ComponentStore _store1;
        private ComponentStore _store2;
        private ComponentStore _store3;

        public override void Init(YuoWorld yuoWorld)
        {
            _type1 = typeof(T1);
            _type2 = typeof(T2);
            _type3 = typeof(T3);
            influenceTypes.Add(_type1);
            influenceTypes.Add(_type2);
            influenceTypes.Add(_type3);
            _store1 = yuoWorld.GetOrCreateComponentStore(_type1);
            _store2 = yuoWorld.GetOrCreateComponentStore(_type2);
            _store3 = yuoWorld.GetOrCreateComponentStore(_type3);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            var entityIndex = entity.EntityIndex;
            var pos1 = _store1.GetSlot(entityIndex);
            if (pos1 == 0) return;
            var t1 = _store1.Dense[pos1 - 1];
            if (!t1.IsUsable()) return;
            var pos2 = _store2.GetSlot(entityIndex);
            if (pos2 == 0) return;
            var t2 = _store2.Dense[pos2 - 1];
            if (!t2.IsUsable()) return;
            var pos3 = _store3.GetSlot(entityIndex);
            if (pos3 == 0) return;
            var t3 = _store3.Dense[pos3 - 1];
            if (!t3.IsUsable()) return;
            Run(t1 as T1, t2 as T2, t3 as T3);
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
        private ComponentStore _store1;
        private ComponentStore _store2;
        private ComponentStore _store3;
        private ComponentStore _store4;

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
            _store1 = yuoWorld.GetOrCreateComponentStore(_type1);
            _store2 = yuoWorld.GetOrCreateComponentStore(_type2);
            _store3 = yuoWorld.GetOrCreateComponentStore(_type3);
            _store4 = yuoWorld.GetOrCreateComponentStore(_type4);
            base.Init(yuoWorld);
        }

        protected override void RunEntity(YuoEntity entity)
        {
            var entityIndex = entity.EntityIndex;
            var pos1 = _store1.GetSlot(entityIndex);
            if (pos1 == 0) return;
            var t1 = _store1.Dense[pos1 - 1];
            if (!t1.IsUsable()) return;
            var pos2 = _store2.GetSlot(entityIndex);
            if (pos2 == 0) return;
            var t2 = _store2.Dense[pos2 - 1];
            if (!t2.IsUsable()) return;
            var pos3 = _store3.GetSlot(entityIndex);
            if (pos3 == 0) return;
            var t3 = _store3.Dense[pos3 - 1];
            if (!t3.IsUsable()) return;
            var pos4 = _store4.GetSlot(entityIndex);
            if (pos4 == 0) return;
            var t4 = _store4.Dense[pos4 - 1];
            if (!t4.IsUsable()) return;
            Run(t1 as T1, t2 as T2, t3 as T3, t4 as T4);
        }

        public abstract void Run(T1 component1, T2 component2, T3 component3, T4 component4);
    }
}
