using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// 跨系统调用顺序记录器。测试程序集已禁用并行化，静态列表在测试间重置使用是安全的。
/// </summary>
public static class LifecycleTrace
{
    public static readonly List<string> Entries = new();

    public static void Reset() => Entries.Clear();

    public static void Record(string entry) => Entries.Add(entry);
}

/// <summary>统一的世界构造入口：每个测试一个独立世界。</summary>
public static class EcsWorlds
{
    public static YuoWorld Create(bool setAsDefault = true)
    {
        var world = new YuoWorld();
        world.OnInit(setAsDefault);
        return world;
    }

    /// <summary>测试程序集内的系统都会在 OnInit 时被自动扫描注册。</summary>
    public static T System<T>(YuoWorld world) where T : SystemBase =>
        world.GetAllSystem.OfType<T>().Single();
}

// ---------- 组件 ----------

public class EcsPosComp : YuoComponent
{
    public int X;
}

public class EcsVelComp : YuoComponent
{
    public int V;
}

public class EcsTagComp : YuoComponent;

public class EcsExtraComp : YuoComponent;

public class EcsFlagComp : YuoComponent;

public class EcsTempComp : YuoComponent;

public class EcsSpawnComp : YuoComponent;

/// <summary>YuoComponent 的直接子类是"基础组件锚点"（baseTypeMap 的 value 口径）。</summary>
public class EcsBaseComp : YuoComponent
{
    public int B;
}

public class EcsDerivedComp : EcsBaseComp;

/// <summary>二级派生：按 CheckBaseIsYuoComponent 规则仍锚定到 EcsBaseComp。</summary>
public class EcsDerived2Comp : EcsDerivedComp;

public class EcsOtherBaseComp : YuoComponent;

public class EcsOtherDerivedComp : EcsOtherBaseComp;

[SingleComponent]
public class EcsSingleComp : YuoComponent
{
    public int V;
}

// ---------- 自定义标签 ----------

/// <summary>有系统实现的探测标签。</summary>
public interface IEcsProbeTag : ISystemTag;

/// <summary>没有任何系统实现的标签，验证"未注册 tag 不派发"。</summary>
public interface IEcsNoSystemTag : ISystemTag;

// ---------- 系统 ----------

public sealed class EcsPosAwakeSystem : YuoSystem<EcsPosComp>, IAwake
{
    public int Count;
    public List<long> Seen { get; } = new();

    public override void Run(EcsPosComp component)
    {
        Count++;
        Seen.Add(component.Entity.ID);
        LifecycleTrace.Record("awake:pos");
    }
}

/// <summary>同组件类型的第二个 IAwake 系统，验证稠密表一槽多系统。</summary>
public sealed class EcsPosAwakeSecondSystem : YuoSystem<EcsPosComp>, IAwake
{
    public int Count;

    public override void Run(EcsPosComp component)
    {
        Count++;
        LifecycleTrace.Record("awake:pos2");
    }
}

public sealed class EcsPosDestroySystem : YuoSystem<EcsPosComp>, IDestroy
{
    public int Count;
    public List<long> Seen { get; } = new();

    /// <summary>销毁派发时刻组件是否仍能通过实体取回（存储点位语义的探针）。</summary>
    public bool ComponentReadableDuringDestroy { get; private set; }

    /// <summary>销毁派发时刻世界组件集是否已不含该组件（世界映射先删语义的探针）。</summary>
    public bool RemovedFromWorldSetBeforeDispatch { get; private set; }

    public override void Run(EcsPosComp component)
    {
        Count++;
        Seen.Add(component.Entity.ID);
        ComponentReadableDuringDestroy = component.Entity.GetComponent<EcsPosComp>() == component;
        RemovedFromWorldSetBeforeDispatch = !component.Entity.World.GetAllComponents<EcsPosComp>().Contains(component);
        LifecycleTrace.Record("destroy:pos");
    }
}

public sealed class EcsVelDestroySystem : YuoSystem<EcsVelComp>, IDestroy
{
    public int Count;

    public override void Run(EcsVelComp component)
    {
        Count++;
        LifecycleTrace.Record("destroy:vel");
    }
}

public sealed class EcsPosStartSystem : YuoSystem<EcsPosComp>, IStart
{
    public int Count;
    public List<long> Seen { get; } = new();

    public override void Run(EcsPosComp component)
    {
        Count++;
        Seen.Add(component.Entity.ID);
    }
}

public sealed class EcsPosUpdateSystem : YuoSystem<EcsPosComp>, IUpdate
{
    public int RunCount;
    public List<long> Seen { get; } = new();

    public override void Run(EcsPosComp component)
    {
        RunCount++;
        Seen.Add(component.Entity.ID);
    }
}

public sealed class EcsVelUpdateSystem : YuoSystem<EcsVelComp>, IUpdate
{
    public int RunCount;
    public List<long> Seen { get; } = new();

    public override void Run(EcsVelComp component)
    {
        RunCount++;
        Seen.Add(component.Entity.ID);
    }
}

public sealed class EcsFlagUpdateSystem : YuoSystem<EcsFlagComp>, IUpdate
{
    public int RunCount;
    public List<long> Seen { get; } = new();

    public override void Run(EcsFlagComp component)
    {
        RunCount++;
        Seen.Add(component.Entity.ID);
    }
}

/// <summary>多组件监听：实体集齐 Pos+Vel 后才应命中。</summary>
public sealed class EcsPosVelAwakeSystem : YuoSystem<EcsPosComp, EcsVelComp>, IAwake
{
    public int Count;
    public List<long> Seen { get; } = new();

    public override void Run(EcsPosComp component1, EcsVelComp component2)
    {
        Count++;
        Seen.Add(component1.Entity.ID);
    }
}

public sealed class EcsPosVelUpdateSystem : YuoSystem<EcsPosComp, EcsVelComp>, IUpdate
{
    public int RunCount;
    public List<long> Seen { get; } = new();

    public override void Run(EcsPosComp component1, EcsVelComp component2)
    {
        RunCount++;
        Seen.Add(component1.Entity.ID);
    }
}

public sealed class EcsPosSwitchSystem : YuoSystem<EcsPosComp>, ISwitchComponent
{
    public int Count;
    public List<long> Seen { get; } = new();

    public override void Run(EcsPosComp component)
    {
        Count++;
        Seen.Add(component.Entity.ID);
    }
}

// ---------- 基础组件系统（走 _systemsByBaseTypeId + _baseComponentMask 路径） ----------

public sealed class EcsBaseAwakeSystem : YuoSystemOfBase<EcsBaseComp>, IAwake
{
    public int Count;
    public List<long> Seen { get; } = new();

    public override void Run(EcsBaseComp baseComponent)
    {
        Count++;
        Seen.Add(baseComponent.Entity.ID);
        LifecycleTrace.Record("awake:base");
    }
}

public sealed class EcsBaseUpdateSystem : YuoSystemOfBase<EcsBaseComp>, IUpdate
{
    public int RunCount;
    public List<long> Seen { get; } = new();

    public override void Run(EcsBaseComp baseComponent)
    {
        RunCount++;
        Seen.Add(baseComponent.Entity.ID);
    }
}

public sealed class EcsBaseDestroySystem : YuoSystemOfBase<EcsBaseComp>, IDestroy
{
    public int Count;

    public override void Run(EcsBaseComp baseComponent)
    {
        Count++;
        LifecycleTrace.Record("destroy:base");
    }
}

/// <summary>挂在具体派生类型上的普通系统（_systemsByComponentId 路径）。</summary>
public sealed class EcsDerivedAwakeSystem : YuoSystem<EcsDerivedComp>, IAwake
{
    public int Count;

    public override void Run(EcsDerivedComp component)
    {
        Count++;
        LifecycleTrace.Record("awake:derived");
    }
}

/// <summary>挂在锚点基类上的"普通组件系统"：派生组件按 runtime Type 派发时不应命中它。</summary>
public sealed class EcsBaseDirectAwakeSystem : YuoSystem<EcsBaseComp>, IAwake
{
    public int Count;

    public override void Run(EcsBaseComp component)
    {
        Count++;
        LifecycleTrace.Record("awake:base-direct");
    }
}

// ---------- 标签键系统 ----------

public sealed class EcsProbeSystem : YuoSystem<EcsTagComp>, IEcsProbeTag
{
    public int Count;
    public List<long> Seen { get; } = new();

    public override void Run(EcsTagComp component)
    {
        Count++;
        Seen.Add(component.Entity.ID);
    }
}

/// <summary>tag 键校验：该系统只有 IUpdate 标签，LateUpdate 派发不得触及它。</summary>
public sealed class EcsTagUpdateOnlySystem : YuoSystem<EcsTagComp>, IUpdate
{
    public int RunCount;

    public override void Run(EcsTagComp component)
    {
        RunCount++;
    }
}

// ---------- Run 中改组件的 deferred 路径 ----------

/// <summary>Armed 时在 Run 内把自身组件标记销毁，用于验证命令缓冲回放时机。</summary>
public sealed class EcsSelfDestructSystem : YuoSystem<EcsTagComp>, IUpdate
{
    public bool Armed;
    public int RunCount;

    public override void Run(EcsTagComp component)
    {
        RunCount++;
        if (Armed && !component.IsDestroy)
        {
            component.DestroyComponent();
        }
    }
}

public sealed class EcsTagDestroySystem : YuoSystem<EcsTagComp>, IDestroy
{
    public int Count;
    public List<long> Seen { get; } = new();

    public override void Run(EcsTagComp component)
    {
        Count++;
        Seen.Add(component.Entity.ID);
    }
}

/// <summary>Run 内给已有实体摘组件，验证 membership 延迟并入。</summary>
public sealed class EcsFlagTempUpdateSystem : YuoSystem<EcsFlagComp, EcsTempComp>, IUpdate
{
    public int RunCount;
    public List<long> Seen { get; } = new();

    public override void Run(EcsFlagComp component1, EcsTempComp component2)
    {
        RunCount++;
        Seen.Add(component1.Entity.ID);
        component2.Entity.RemoveComponent(component2);
    }
}

/// <summary>Run 内创建同样匹配本系统的新实体：pendingJoin 语义与迭代期 IAwake 的探针。</summary>
public sealed class EcsSpawnSystem : YuoSystem<EcsSpawnComp>, IAwake, IUpdate
{
    public YuoWorld? Host;
    public bool Spawned;
    public int AwakeCount;
    public int UpdateRunCount;
    public List<long> UpdateSeen { get; } = new();

    public override void Run(EcsSpawnComp component)
    {
        if (RunType == typeof(IAwake))
        {
            AwakeCount++;
            return;
        }

        UpdateRunCount++;
        UpdateSeen.Add(component.Entity.ID);
        if (Spawned || Host == null) return;
        Spawned = true;
        var child = new YuoEntity(Host);
        child.AddComponent<EcsSpawnComp>();
    }
}

/// <summary>IDestroy 回调内重入释放宿主实体的探针组件。</summary>
public class EcsReentrantDisposeComp : YuoComponent
{
}

/// <summary>IDestroy 回调内对宿主实体再次调用 Dispose：验证处置重入守卫。</summary>
public sealed class EcsReentrantDisposeSystem : YuoSystem<EcsReentrantDisposeComp>, IDestroy
{
    public override void Run(EcsReentrantDisposeComp component)
    {
        component.Entity?.Dispose();
    }
}
