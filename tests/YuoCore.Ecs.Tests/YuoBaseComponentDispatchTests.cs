using YuoCore.Ecs;

namespace YuoCore.Ecs.Tests;

/// <summary>
/// 多态/基类路径：_systemsByBaseTypeId + _baseComponentMask + _baseSlots 的派发与存储口径。
/// baseTypeMap 锚点 = 继承链上"直接挂在 YuoComponent 下"的那一层。
/// </summary>
public sealed class YuoBaseComponentDispatchTests
{
    [Fact]
    public void Base_system_hits_derived_component_lifecycle()
    {
        var world = EcsWorlds.Create();
        try
        {
            LifecycleTrace.Reset();
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsDerivedComp>();

            var baseAwake = EcsWorlds.System<EcsBaseAwakeSystem>(world);
            var baseUpdate = EcsWorlds.System<EcsBaseUpdateSystem>(world);
            var baseDestroy = EcsWorlds.System<EcsBaseDestroySystem>(world);

            Assert.Equal(1, baseAwake.Count);
            Assert.Equal(new[] { entity.ID }, baseAwake.Seen);
            Assert.Contains(entity, baseUpdate.Entities);

            world.Update();
            Assert.Equal(1, baseUpdate.RunCount);
            Assert.Equal(new[] { entity.ID }, baseUpdate.Seen);

            entity.RemoveComponent<EcsDerivedComp>();
            Assert.Equal(1, baseDestroy.Count);
            Assert.DoesNotContain(entity, baseUpdate.Entities);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>二级派生同样锚定到最上层基础组件：Derived2 → baseTypeMap 指向 EcsBaseComp。</summary>
    [Fact]
    public void Second_level_derived_anchors_to_top_base()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var derived2 = entity.AddComponent<EcsDerived2Comp>();

            Assert.Equal(typeof(EcsBaseComp), derived2.BaseComponentType);
            Assert.True(entity.HasBaseComponent(typeof(EcsBaseComp)));
            Assert.Same(derived2, entity.GetBaseComponent<EcsBaseComp>());

            // 中间层 EcsDerivedComp 不是该组件的锚点
            Assert.False(entity.HasBaseComponent(typeof(EcsDerivedComp)));

            world.Update();
            var baseUpdate = EcsWorlds.System<EcsBaseUpdateSystem>(world);
            Assert.Equal(new[] { entity.ID }, baseUpdate.Seen);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>组件不是自身的基类：直接挂锚点类型不走基础组件通道。</summary>
    [Fact]
    public void Anchor_component_is_not_its_own_base()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsBaseComp>();

            Assert.False(entity.HasBaseComponent(typeof(EcsBaseComp)));
            Assert.Null(entity.GetBaseComponent<EcsBaseComp>());
            Assert.Equal(0, EcsWorlds.System<EcsBaseAwakeSystem>(world).Count);   // 基础系统不命中锚点本身
            Assert.Equal(1, EcsWorlds.System<EcsBaseDirectAwakeSystem>(world).Count); // 普通组件系统命中
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>不相关基类层级的组件互不触发。</summary>
    [Fact]
    public void Unrelated_base_hierarchies_do_not_cross()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsOtherDerivedComp>();

            Assert.Equal(0, EcsWorlds.System<EcsBaseAwakeSystem>(world).Count);
            Assert.True(entity.HasBaseComponent(typeof(EcsOtherBaseComp)));
            Assert.False(entity.HasBaseComponent(typeof(EcsBaseComp)));

            world.Update();
            Assert.Equal(0, EcsWorlds.System<EcsBaseUpdateSystem>(world).RunCount);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// 常规组件键按"声明类型"匹配，不做多态：实体只挂 Derived 时 HasComponent(Base)=false。
    /// 多态可见性走 HasBaseComponent/GetBaseComponent 独立通道。
    /// </summary>
    [Fact]
    public void Has_component_is_exact_type_only_base_goes_through_base_channel()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var derived = entity.AddComponent<EcsDerivedComp>();

            Assert.True(entity.HasComponent<EcsDerivedComp>());
            Assert.False(entity.HasComponent<EcsBaseComp>());      // Components 字典按精确类型键控
            Assert.Null(entity.GetComponent<EcsBaseComp>());

            Assert.True(entity.HasBaseComponent<EcsBaseComp>());
            Assert.Same(derived, entity.GetBaseComponent<EcsBaseComp>());
            Assert.Same(derived, entity.GetBaseComponent(typeof(EcsBaseComp)));
            Assert.True(entity.TryGetBaseComponent<EcsBaseComp>(out var viaTry));
            Assert.Same(derived, viaTry);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// SetComponent(声明类型=Base, 实例=Derived) 的固定行为：
    ///   - entity.Components / ComponentStore 以声明类型 Base 为键 → GetComponent(Base) 命中；
    ///   - 世界级 components 集合与生命周期派发以 component.Type(=Derived) 为键；
    ///   - 但 membership 掩码只按声明键置位 → 具体类型系统(EcsDerivedComp)派发失败，
    ///     基类键系统(YuoSystem&lt;BaseComp&gt;)不在派发列表；
    ///   - 唯一命中通道是 baseTypeMap 基类派发（YuoSystemOfBase）。
    /// </summary>
    [Fact]
    public void Declared_type_storage_with_runtime_type_dispatch()
    {
        var world = EcsWorlds.Create();
        try
        {
            LifecycleTrace.Reset();
            var entity = new YuoEntity(world);
            var derivedInstance = new EcsDerivedComp();

            entity.SetComponent(typeof(EcsBaseComp), derivedInstance);

            // 存储口径：声明类型为键
            Assert.Same(derivedInstance, entity.GetComponent<EcsBaseComp>());
            Assert.Same(derivedInstance, entity.GetComponent(typeof(EcsBaseComp)));
            Assert.True(entity.HasComponent<EcsBaseComp>());
            Assert.False(entity.HasComponent<EcsDerivedComp>());
            Assert.Null(entity.GetComponent<EcsDerivedComp>());

            // 世界集合口径：runtime 类型为键
            Assert.DoesNotContain(derivedInstance, world.GetAllComponents<EcsBaseComp>());
            Assert.Contains(derivedInstance, world.GetAllComponents<EcsDerivedComp>());

            // 派发口径：只有基础组件系统经由 baseTypeMap[Derived]=Base 命中；
            // 具体类型系统因掩码按声明键置位而检查失败，基类键普通系统不在 Derived 派发列表。
            Assert.Equal(1, EcsWorlds.System<EcsBaseAwakeSystem>(world).Count);
            Assert.Equal(0, EcsWorlds.System<EcsDerivedAwakeSystem>(world).Count);
            Assert.Equal(0, EcsWorlds.System<EcsBaseDirectAwakeSystem>(world).Count);
            Assert.Equal(new[] { "awake:base" }, LifecycleTrace.Entries);
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>
    /// _baseComponentMask 只增不清，镜像 BaseComponents 的 ContainsKey 残留语义：
    /// 最后一个派生组件移除后 HasBaseComponent 仍为 true，但 GetBaseComponent 正确返回 null。
    /// </summary>
    [Fact]
    public void Base_mask_stays_set_after_last_derived_removed()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            entity.AddComponent<EcsDerivedComp>();
            entity.RemoveComponent<EcsDerivedComp>();

            // 残留语义：BaseComponents 字典的 key 在 RemoveItem 后保留空集合
            Assert.True(entity.BaseComponents.ContainsKey(typeof(EcsBaseComp)));
            Assert.True(entity.HasBaseComponent<EcsBaseComp>());   // 与 ContainsKey 口径一致
            Assert.Empty(entity.BaseComponents[typeof(EcsBaseComp)]);
            Assert.Null(entity.GetBaseComponent<EcsBaseComp>());   // 首元素缓存已刷新为空
        }
        finally
        {
            world.OnDestroy();
        }
    }

    /// <summary>同一基础类型下挂多个不同派生组件：GetBaseComponent 返回集合的首枚举元素。</summary>
    [Fact]
    public void Base_slot_returns_first_of_multiple_derived_components()
    {
        var world = EcsWorlds.Create();
        try
        {
            var entity = new YuoEntity(world);
            var derived = entity.AddComponent<EcsDerivedComp>();
            var derived2 = entity.AddComponent<EcsDerived2Comp>();

            var first = entity.GetBaseComponent<EcsBaseComp>();
            Assert.NotNull(first);
            Assert.True(ReferenceEquals(first, derived) || ReferenceEquals(first, derived2));

            // 摘掉当前首项后，首元素缓存必须刷新到剩余项而不是留空
            entity.RemoveComponent(first!);
            var next = entity.GetBaseComponent<EcsBaseComp>();
            Assert.NotNull(next);
            Assert.NotSame(first, next);
        }
        finally
        {
            world.OnDestroy();
        }
    }
}
