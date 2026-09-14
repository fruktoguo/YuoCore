using System;
using System.Collections.Generic;

namespace YuoCore.Ecs
{
    public partial class YuoWorld
    {
        /// <summary>
        /// 是否关闭系统的 Try-Catch
        /// </summary>
        public static bool CloseSystemTry = false;

        /// <summary>
        /// YuoWorld 的单例实例
        /// </summary>
        public static YuoWorld Instance { get; private set; }

        public static YuoWorld RequireInstance()
        {
            return Instance ?? throw new InvalidOperationException("YuoWorld.Instance is not initialized. Create a YuoWorld and call OnInit(), or pass a YuoWorld explicitly.");
        }

        /// <summary>
        /// 主实体，通常用于挂载核心组件
        /// </summary>
        public static YuoEntity Main => RequireInstance().WorldMain;

        public YuoEntity WorldMain { get; private set; }

        #region Systems

        /// <summary>
        /// 所有系统的列表
        /// </summary>
        private List<SystemBase> allSystem = new();

        /// <summary>
        /// 组件类型到系统的映射
        /// </summary>
        private MultiMap<Type, SystemBase> systemsOfComponent = new();

        /// <summary>
        /// 标签类型到系统的映射
        /// </summary>
        private MultiMap<Type, SystemBase> systemsOfTag = new();

        /// <summary>
        /// 基础组件类型到系统的映射
        /// </summary>
        private MultiMap<Type, SystemBaseBase> systemsOfBaseComponent = new();

        /// <summary>
        /// 系统类型到系统实例的字典
        /// </summary>
        private Dictionary<Type, SystemBase> systemDic = new();

        internal Dictionary<Type, CoverSystemAttribute> coverSystemDic = new();

        #endregion

        /// <summary>
        /// 所有实体的字典，以实体ID为键
        /// </summary>
        public Dictionary<long, YuoEntity> Entities { get; private set; } = new();

        /// <summary>
        /// 世界时钟，默认共享 TimeInfo.Instance；OnInit 前可注入确定性时钟，
        /// 每次 Update 推进一次 FrameTime
        /// </summary>
        public YuoCore.Ecs.Helpers.TimeInfo Time { get; set; } = YuoCore.Ecs.Helpers.TimeInfo.Instance;

        private IDGenerate _idGenerate;

        /// <summary>
        /// 实体 ID 生成器，绑定本世界时钟；OnInit 前可注入自定义生成器
        /// </summary>
        public IDGenerate IdGenerate
        {
            get => _idGenerate ??= new IDGenerate(Time);
            set => _idGenerate = value;
        }

        /// <summary>
        /// 组件类型到组件实例的映射
        /// </summary>
        private MultiHashSetMap<Type, YuoComponent> components = new();

        /// <summary>
        /// 默认场景实体
        /// </summary>
        public static YuoEntity Scene => RequireInstance().DefaultScene;

        public YuoEntity DefaultScene => AllScenes[0];

        /// <summary>
        /// 所有场景实体的列表
        /// </summary>
        public readonly List<YuoEntity> AllScenes = new();

        /// <summary>
        /// 待销毁的实体列表
        /// </summary>
        private readonly YuoCommandBuffer<YuoEntity> entityDestroyBuffer = new();

        /// <summary>
        /// 待销毁的组件列表
        /// </summary>
        private readonly YuoCommandBuffer<YuoComponent> componentDestroyBuffer = new();

        /// <summary>
        /// 链接组件的映射
        /// </summary>
        private MultiHashSetMap<Type, Type> linkComponent = new();

        /// <summary>
        /// 单例组件类型集合
        /// </summary>
        private HashSet<Type> singleComponents = new();

        /// <summary>
        /// 基础组件类型映射,key是componentType,value是baseType
        /// </summary>
        internal Dictionary<Type, Type> baseTypeMap = new();

        /// <summary>
        /// 基础组件类型映射,但是反向的,key是baseType,value是componentType的集合
        /// </summary>
        internal MultiHashSetMap<Type, Type> baseTypeMapReverse = new();

        public bool HasBaseType(Type type)
        {
            return baseTypeMap.ContainsKey(type);
        }

        [System.Obsolete("Use HasBaseType instead")]
        public bool HaseBaseType(Type type)
        {
            return HasBaseType(type);
        }

        public bool IsBaseType(Type type)
        {
            return baseTypeMapReverse.ContainsKey(type);
        }

        /// <summary>
        /// IStart 需要在所有组件添加完成后，在每一帧的末尾才会调用
        /// </summary>
        private readonly List<YuoComponent> startSystems = new();

        /// <summary>
        /// 所有组件类型的字典
        /// </summary>
        readonly Dictionary<string, Type> allComponentType = new();

        /// <summary>
        /// 组控制系统缓存，避免重复计算
        /// </summary>
        private readonly Dictionary<(Type tag, Type inc, Type exc), List<SystemBase>> _groupSystemCache = new();

        /// <summary>
        /// 缓存访问锁，确保线程安全
        /// </summary>
        private readonly object _cacheLock = new object();

        #region 高频系统直接引用缓存

        /// <summary>
        /// IUpdate系统的直接引用缓存，避免每帧字典查找
        /// </summary>
        private List<SystemBase> _updateSystems;

        /// <summary>
        /// IFixedUpdate系统的直接引用缓存，避免每帧字典查找
        /// </summary>
        private List<SystemBase> _fixedUpdateSystems;

        /// <summary>
        /// ILateUpdate系统的直接引用缓存，避免每帧字典查找
        /// </summary>
        private List<SystemBase> _lateUpdateSystems;

        /// <summary>
        /// IOnDrawGizmos系统的直接引用缓存
        /// </summary>
        private List<SystemBase> _onDrawGizmosSystems;

        #endregion
    }
}
