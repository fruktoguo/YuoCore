using System.Collections.Generic;

namespace YuoCore.Ecs
{
    /// <summary>
    /// 不会存,只是简化访问
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class YuoComponentLocator<T> : YuoComponent where T : YuoComponent, new()
    {
        private static readonly Dictionary<YuoWorld, YuoEntity> LocatorEntities = new();

        public static T Get => GetFrom(YuoWorld.RequireInstance());

        public static T GetFrom(YuoWorld world)
        {
            if (!LocatorEntities.TryGetValue(world, out var locatorEntity) || locatorEntity.IsUnusable())
            {
                locatorEntity = world.WorldMain;
                LocatorEntities[world] = locatorEntity;
            }

            return locatorEntity.GetOrAddComponent<T>();
        }

        public static T SetInstance(YuoEntity entity)
        {
            if (LocatorEntities.TryGetValue(entity.World, out var locatorEntity) && locatorEntity.IsUsable())
                locatorEntity.DestroyComponent<T>();

            LocatorEntities[entity.World] = entity;
            return entity.AddComponent<T>();
        }
    }

    /// <summary>
    /// 会存的
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class YuoComponentCachedLocator<T> : YuoComponent where T : YuoComponent, new()
    {
        private static readonly Dictionary<YuoWorld, YuoEntity> LocatorEntities = new();

        private static readonly Dictionary<YuoWorld, T> Instances = new();

        public static T Get => GetFrom(YuoWorld.RequireInstance());

        public static T GetFrom(YuoWorld world)
        {
            if (Instances.TryGetValue(world, out var instance) && instance.IsUsable()) return instance;

            if (!LocatorEntities.TryGetValue(world, out var locatorEntity) || locatorEntity.IsUnusable())
            {
                locatorEntity = world.WorldMain;
                LocatorEntities[world] = locatorEntity;
            }

            instance = locatorEntity.GetOrAddComponent<T>();
            Instances[world] = instance;
            return instance;
        }

        public static void SetInstance(YuoEntity entity)
        {
            if (LocatorEntities.TryGetValue(entity.World, out var locatorEntity) && locatorEntity.IsUsable())
                locatorEntity.DestroyComponent<T>();
            LocatorEntities[entity.World] = entity;
            Instances[entity.World] = entity.AddComponent<T>();
        }
    }

    /// <summary>
    /// 会存且新建实体,而不是直接挂载Main实体上
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class YuoComponentEntityCachedLocator<T> : YuoComponent where T : YuoComponent, new()
    {
        private static readonly Dictionary<YuoWorld, YuoEntity> LocatorEntities = new();

        private static readonly Dictionary<YuoWorld, T> Instances = new();

        public static T Get => GetFrom(YuoWorld.RequireInstance());

        public static T GetFrom(YuoWorld world)
        {
            if (Instances.TryGetValue(world, out var instance) && instance.IsUsable()) return instance;

            if (!LocatorEntities.TryGetValue(world, out var locatorEntity) || locatorEntity.IsUnusable())
            {
                locatorEntity = world.WorldMain.AddChild($"{typeof(T).Name}");
                LocatorEntities[world] = locatorEntity;
            }

            instance = locatorEntity.GetOrAddComponent<T>();
            Instances[world] = instance;
            return instance;
        }

        public static void SetInstance(YuoEntity entity)
        {
            if (LocatorEntities.TryGetValue(entity.World, out var locatorEntity) && locatorEntity.IsUsable())
                locatorEntity.DestroyComponent<T>();
            LocatorEntities[entity.World] = entity;
            Instances[entity.World] = entity.AddComponent<T>();
        }
    }
}
