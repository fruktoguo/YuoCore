# YuoCore ECS 内核开发规约

适用范围：`src/YuoCore.Ecs` 及其直接内核路径（Entity/Component/World/System/Fiber 调度）。
本文是 ECS 内核改动的强制约定；宿主侧（业务项目）使用规范由宿主项目自定。

## 1. 存储与索引纪律

- 组件存储唯一形态：按 `ComponentTypeRegistry` 分配的稠密 `componentId` 索引 `ComponentStore`（`sparse[entityIndex] → densePos+1`、`denseEntities`、`dense[]`、swap-remove）。禁止引入第二套组件容器或旁路字典。
- 热路径一律 `Type → int id → 数组槽位`，禁止新增 `Dictionary<Type, …>` 查找。允许字典的只有：注册期构建、冷路径工具函数。
- 系统派发表 `_systemsByComponentId` / `_systemsByBaseTypeId` / `_systemsByTagId` 是稠密真相源；新增派发面（新生命周期 tag、group filter、删除路径、StaticFunction 构建路径）必须在注册/清理的全写入点同步维护，不得漏点。
- 基类组件派发生用 `_baseComponentMask` 位掩码 + `_baseSlots` 命中，不得在热路径反射遍历继承链。
- `LifecycleIndex` 生命周期命中表：组件类型无订阅时一次数组下标短路整段派发。

## 2. 语义红线（性能优化不得触碰）

- `IAwake`/`IDestroy`/`IUpdate` 等生命周期 tag 的触发时机、顺序、对象身份不变。
- 组件替换/销毁语义不变：`Dispose`、`IsDestroy`、`Entity` 反引、`YuoCommandBuffer` 回放顺序。
- 基类组件派发、单例组件、Entity 子树、Scene 归属语义不变。
- 删除路径 swap-remove 后 `Version`/索引一致性必须保持，禁止出现 stale 句柄命中新槽位。

## 3. 活动实体契约

- `World.SetActiveEntities`/`ClearActiveEntities` 只决定本次 tick 系统遍历哪些实体，是宿主对扫描集的收敛提示；**不改变任何组件的真实状态、生命周期或可见性语义**。
- 活动集外实体仍持有真实组件；宿主对活动集外实体的过滤责任（如 `ActivePlayerId` 门控）在宿主侧，内核不兜底。
- 契约是纯提示：活动集可空、可放大，内核必须容忍，不得基于活动集做语义假设。

## 4. 观测纪律

- `YuoCoreTrace` 埋点必须满足零成本前提：无 Meter 监听器时不读时钟、不分配、不加锁；宿主以 `YUOCORE_TRACE_OFF` 编译时入口折叠为 default 返回（JIT 后零指令），内核独立构建默认保持启用。
- 百万次级热路径（`get_component`、`store.get` 等）上的埋点会放大自身开销十倍以上——该路径的埋点数据只用于归因，不得当作真实性能数字；新增热路径埋点必须评估观测污染。
- 报告耗时必须区分墙钟与并发累计（owner_wait 类等待不得与墙钟相加）。

## 5. 验证门禁

- 本仓库：`YuoCore.Game.Tests` / `YuoCore.Runtime.Tests` / `YuoCore.StateSync.Tests` 必须全绿。
- 宿主侧：daohua `ServerEcs.Tests` + `scripts/bench-ablation.sh` 消融基准终态 `exactMatch=true`。
- 性能归因：任何宣称的收益必须附消融或对照数据；禁止把多因素合并改动的收益归因给单点。

## 6. references/ 纪律

- `references/`（Arch/Friflo/DefaultEcs/ET 等参考源码）仅用于阅读，被 `.gitignore` 忽略；禁止生产代码、测试、benchmark 引用其中任何文件或类型。

## 7. 分支约定

- `main`：基线内核（pinned 对照版本）。
- `ecs-optimized`：性能演进分支，本规约的稠密索引/sparse-set 机制落地所在；向 main 回流的裁量权在仓库所有者。
