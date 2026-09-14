using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public partial class YuoEntity : IDisposable
    {
        public YuoWorld World { get; private set; }
        public YuoEntity Parent { get; private set; }
        public EntityComponent EntityData { get; private set; }
        public Dictionary<Type, YuoComponent> Components { get; } = new();
        public HashSet<YuoEntity> Children { get; } = new();
        public MultiHashSetMap<Type, YuoComponent> ChildComponents { get; private set; } = new();
        public MultiHashSetMap<Type, YuoComponent> BaseComponents { get; private set; } = new();
        
        public YuoEntity() : this(YuoWorld.RequireInstance())
        {
        }

        public YuoEntity(YuoWorld world) : this(world, (world ?? throw new ArgumentNullException(nameof(world))).IdGenerate.GenerateInstanceId())
        {
        }

        public YuoEntity(long id) : this(YuoWorld.RequireInstance(), id)
        {
        }

        public YuoEntity(YuoWorld world, long id)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            //EntityComponent为基础组件,无法移除,不会显示在组件列表中,但当销毁时会自动移除
            //可以通过获取EntityComponent是否为null来判断Entity是否释放
            EntityData = new EntityComponent();
            EntityData.Entity = this;

            EntityData.Id = id;

            World.AddComponent(this, EntityData);
            World.RegisterEntity(this);
            // $"Add Entity{EntityData.Id}".Log();
        }

        public YuoEntity(string name) : this(name.GetHashCode())
        {
            EntityName = name;
        }

        public YuoEntity(YuoWorld world, string name) : this(world, name.GetHashCode())
        {
            EntityName = name;
        }
    }
}
