# YuoCore 参考 ET10 的完整架构设计

YuoCore 是基于现有 Yuo ECS 继续扩展出来的后端框架。它参考 ET10 的核心架构思想，尤其是 Fiber、Actor、Actor Location、进程配置、服务发现和多进程部署，但不会照搬 ET 的“一切皆 Entity”模型。

YuoCore 的核心规则是：

```text
YuoFiber 负责执行归属。
YuoWorld 负责游戏逻辑。
跨 Fiber 只能走 Actor 消息。
可变 ECS 状态不能跨 Fiber 共享。
```

也就是说，`YuoWorld` 仍然是你的 ECS 逻辑核心；`YuoFiber` 是后端为了多线程、多进程、消息隔离而加上的运行容器。

## 参考来源

ET10 本地参考源码放在 `references/ET10`，该目录已被 Git 忽略，只作为架构参考。

重点参考入口：

- `references/ET10/Book/5.4Actor Model.md`
- `references/ET10/Book/5.5Actor Location-ZH.md`
- `references/ET10/Packages/cn.etetet.core/Scripts/Core/Share/World/Fiber/`
- `references/ET10/Packages/cn.etetet.actorlocation/`
- `references/ET10/Packages/cn.etetet.servicediscovery/`
- `references/ET10/Packages/cn.etetet.startconfig/`

## 架构目标

YuoCore 需要支持：

- 每个逻辑运行单元内部保持单线程 ECS 编程体验。
- 通过多个 Fiber 利用多核 CPU。
- 通过 Actor 消息实现跨 Fiber、跨进程通信。
- 通过 Location 服务实现稳定业务 ID 到当前 Actor 地址的映射。
- 通过显式配置启动进程、服务、Fiber 和分片。
- 客户端实时通信使用 Raw WebSocket + Protobuf。
- PostgreSQL、Redis、日志、指标、链路、测试、压测作为后端一等基础设施。
- `YuoCore.Ecs` 可以被前端复用，不强行引入服务端专属概念。

## 核心概念映射

| ET10 概念 | YuoCore 概念 | 职责 |
| --- | --- | --- |
| `World` | `YuoCoreRuntime` / runtime host | 全局运行时，管理 Fiber、单例、时间、日志、配置 |
| `Fiber` | `YuoFiber` | 单线程执行单元，拥有 mailbox 和调度归属 |
| `Scene` | Fiber root / service domain | Fiber 内的逻辑域，例如 Gateway、GameShard、Location |
| `Entity` | `YuoEntity` / actor owner | 游戏数据对象，或者可被 Actor 消息定位的对象 |
| `Component` | `YuoComponent` | 挂在实体上的数据模块 |
| `EntitySystem` | `YuoSystem` | 处理组件逻辑的系统 |
| `MailboxComponent` | `YuoMailbox` | 把对象或服务变成 Actor 消息目标的队列 |
| `ActorId` / `InstanceId` | `YuoActorId` / `YuoActorAddress` | 可寻址的运行时身份 |
| Actor Location | `YuoLocation` | 稳定业务 ID 到当前 Actor 地址的映射 |
| `StartConfig` | `YuoStartConfig` | 进程、服务、端口、分片、Fiber 的启动契约 |
| `ServiceDiscovery` | `YuoServiceDiscovery` | 服务注册、查询和变更通知 |
| `NetInner` / `NetOuter` | internal transport / external transport | 区分服务端内网通信和客户端外网通信 |

## 目标项目结构

长期目标结构：

```text
src/
  YuoCore.Abstractions/
  YuoCore.Ecs/
  YuoCore.Runtime/
  YuoCore.Actor/
  YuoCore.Location/
  YuoCore.Network/
  YuoCore.Protocol/
  YuoCore.Config/
  YuoCore.Persistence/
  YuoCore.Observability/
  YuoCore.Server/
  YuoCore.Backend/

tests/
  YuoCore.Ecs.Tests/
  YuoCore.Runtime.Tests/
  YuoCore.Actor.Tests/
  YuoCore.Location.Tests/
  YuoCore.Backend.Tests/

benchmarks/
  YuoCore.Ecs.Benchmarks/
  YuoCore.Actor.Benchmarks/
  YuoCore.Benchmarks/

tools/
  YuoCore.LoadTests/
```

当前仓库已经拆出了 `YuoCore.Abstractions`、`YuoCore.Ecs`、`YuoCore.Runtime`、`YuoCore.Actor`、`YuoCore.Location`、`YuoCore.Network`、`YuoCore.Protocol`、`YuoCore.Config`、`YuoCore.Server` 和 `YuoCore.Backend`。其中 `Persistence` 与 `Observability` 当前仍主要落在示例后端项目内，后续需要复用时再继续独立成包。

## 项目职责

| Project | 职责 |
| --- | --- |
| `YuoCore.Abstractions` | 极小公共契约：ID、结果类型、错误码、时钟、基础接口 |
| `YuoCore.Ecs` | 现有 ECS 核心：`YuoWorld`、`YuoEntity`、`YuoComponent`、`YuoSystem`、生命周期 |
| `YuoCore.Runtime` | FiberManager、Fiber 调度器、mailbox pump、线程归属、运行时生命周期 |
| `YuoCore.Actor` | Actor 消息模型、Send/Call、ActorRouter、Mailbox 派发、请求响应追踪 |
| `YuoCore.Location` | 稳定 ID 查找、Actor 迁移、lock/unlock、地址缓存、重试和补偿 |
| `YuoCore.Network` | 内外网传输、Session、封包、连接生命周期 |
| `YuoCore.Protocol` | Protobuf 契约、消息 envelope、opcode、协议版本 |
| `YuoCore.Config` | JSON Schema、启动配置、进程配置、分片配置、服务配置 |
| `YuoCore.Persistence` | PostgreSQL 仓储、Redis 缓存、快照、写回队列 |
| `YuoCore.Observability` | Serilog、OpenTelemetry、Prometheus、运行时指标 |
| `YuoCore.Server` | 通用服务宿主，加载配置并启动 Fiber 和服务 |
| `YuoCore.Backend` | 当前挂机游戏示例后端，基于 YuoCore 构建 |

## 当前实现边界

已经落地的代码边界：

- `YuoCore.Runtime` 提供 `TickAllParallelAsync` / `TickParallelAsync`。并行单位是 Fiber，不是单个 system。
- `YuoCore.Backend.GameRuntime` 默认创建多个 GameShard Fiber，并按玩家 ID 分片到不同 `YuoWorld`。
- `YuoCore.Location` 提供进程内 Registry，支持注册、解析、删除、版本校验、迁移锁和解锁。
- `YuoCore.Actor` 保留原始 Actor API，同时增加 `YuoLocationActorRegistry` 适配独立 Location 包。
- `YuoCore.Config` 提供 StartConfig 模型、JSON 加载和运行时校验；JSON Schema 位于 `schemas/startconfig.schema.json`。
- `YuoCore.Server` 可以按 StartConfig 启动 Fiber，单机/前端仍可以绕过 ServerHost 直接使用 `YuoWorld` 或 `YuoCoreRuntime`。

尚未抽成独立包但示例后端已有实现的能力：

- PostgreSQL/Npgsql/Dapper 仓储仍在 `YuoCore.Backend.Infrastructure`。
- Redis 缓存仍在 `YuoCore.Backend.Infrastructure`。
- Serilog、OpenTelemetry、Prometheus 仍在 `YuoCore.Backend.Program`。
- Raw WebSocket + Protobuf 的外网协议仍在 `YuoCore.Backend`，框架层只提供内部协议抽象。

## 运行拓扑

对于挂机 MMO 风格后端，一个进程内可以运行多个 Fiber：

```text
YuoCore.Server process
  GatewayFiber
    外部 WebSocket sessions
    客户端鉴权和 session 映射

  LocationFiber
    playerId -> ActorAddress
    shardId -> ActorAddress
    迁移锁

  GameShardFiber-1
    YuoWorld
      Player entities
      Idle systems
      Economy systems

  GameShardFiber-2
    YuoWorld
      Player entities
      Idle systems
      Economy systems

  PersistenceFiber
    PostgreSQL writer
    Redis snapshot cache
    批量写入队列

  SchedulerFiber
    离线收益任务
    周期维护任务
    延迟消息
```

多进程部署时，结构可以横向拆开：

```text
Gateway process
  GatewayFiber-1..N

Location process
  LocationFiber

Game process A
  GameShardFiber-1..N

Game process B
  GameShardFiber-1..N

Persistence process
  PersistenceFiber
```

## 消息流

客户端指令流：

```text
Client
  -> GatewayFiber
  -> LocationFiber 按 playerId 查询
  -> GameShardFiber PlayerActor
  -> YuoWorld 执行 ECS 逻辑
  -> 可选：PersistenceFiber 写入或缓存
  -> GameShardFiber 返回 snapshot/event
  -> GatewayFiber
  -> Client
```

服务端内部任务流：

```text
SchedulerFiber
  -> LocationFiber 查询 playerId
  -> GameShardFiber PlayerActor
  -> YuoWorld 应用离线收益
  -> PersistenceFiber 写入快照
```

## Fiber 规则

YuoFiber 的目的，是让系统可以安全多线程，但不要求 ECS 系统本身线程安全。

规则：

- 一个 `YuoWorld` 只属于一个 `YuoFiber`。
- 一个 `YuoEntity` 只能被所属 `YuoFiber` 修改。
- 跨 Fiber 调用必须走消息，不能直接拿对象引用改数据。
- Actor handler 在接收方 Fiber 上执行。
- GameShard Fiber 上不要跑长时间阻塞 IO。
- 持久化和外部 IO 应该用专门 Fiber 或异步服务。
- 只读配置可以不可变共享。
- 可变共享状态必须改成“所有权 + 消息”模型。

当前 Runtime 的并行 Tick 是 shard 级并行：

```text
TickParallelAsync
  -> ShapeShard-0.World.Update()
  -> ShapeShard-1.World.Update()
  -> ShapeShard-2.World.Update()
  -> ShapeShard-3.World.Update()
```

每个 `World.Update()` 内部仍然按原 ECS 顺序执行 system。这样能利用多核，同时不破坏你现在系统内“直接读写组件”的编程模型。

## Actor 模型

Actor 是一个拥有 mailbox 的消息目标。消息进入 mailbox 后，按目标 Fiber 的规则串行处理。

常见 Actor 类型：

- Service Actor：Gateway、Location、Persistence、Scheduler。
- Entity Actor：Player、Unit、Room、Guild、Market。
- Shard Actor：一个 Fiber 级目标，内部拥有一个 `YuoWorld`。

Actor API 形态：

```text
Send(actorId, message)
Call<TResponse>(actorId, request)
Publish(topic, event)
```

Actor 派发路径：

```text
ActorRouter
  -> 如果目标在本进程，投递到本地 Fiber mailbox
  -> 如果目标在远端，走 internal transport
  -> 如果路由失效，触发 Location 重查或失败
```

性能关键点：

- 同进程本地投递不需要序列化。
- 跨进程才需要 Protobuf 或二进制序列化。
- Actor handler 默认串行，避免数据竞争。
- 长逻辑可以拆成异步任务，但必须明确不会破坏 mailbox 顺序语义。

## Actor Location

Actor Location 解决的问题是：

```text
业务稳定 ID 是固定的。
运行时 Actor 地址可能变化。
```

例如玩家从一个 GameShard 迁移到另一个 GameShard 时：

```text
playerId 不变
ActorAddress 变化
```

Location 状态：

```text
locationType + stableId -> ActorAddress
```

例子：

```text
PlayerLocation + playerId -> GameShardFiber-3 / PlayerActor
GuildLocation + guildId -> GuildFiber-1 / GuildActor
MarketLocation + marketId -> MarketFiber-1 / MarketActor
```

必需操作：

```text
Add(locationType, key, actorAddress)
Get(locationType, key)
Remove(locationType, key)
Lock(locationType, key, owner, ttl)
Unlock(locationType, key, oldAddress, newAddress, token)
```

迁移流程：

```text
1. 源 shard 在 Location 中锁住 playerId。
2. 源 shard 序列化玩家状态。
3. 目标 shard 创建 player actor 并恢复状态。
4. Location unlock，把 old address 替换成 new address。
5. 后续消息路由到目标 shard。
```

发送方可以缓存 Location 查询结果。遇到 stale route 错误时，清缓存，重新查询 Location，并按有限次数重试。

## 网络层

外网传输：

- Raw WebSocket + Protobuf。
- 只处理客户端协议。
- 终止于 Gateway Fiber。
- 不允许客户端消息直接修改 GameShard 内部状态。

内网传输：

- 进程间 Actor 消息。
- 可以使用 TCP、WebSocket 或其他二进制传输。
- 必须支持同进程本地短路。
- 使用 `YuoActorAddress` 和消息 envelope 元数据。

协议 envelope 推荐字段：

```text
MessageId
RequestId
SourceActor
TargetActor
PayloadType
PayloadBytes
TraceId
TimeoutMs
```

## 配置系统

YuoCore 应使用 JSON Schema 作为公开配置契约。

目标配置概念：

```text
StartProcessConfig
  processId
  processType
  machine
  innerEndpoint
  outerEndpoint

StartFiberConfig
  fiberId
  fiberType
  sceneName
  zone
  shard
  processId

ServiceConfig
  serviceName
  serviceType
  metadata
```

配置需要回答：

- 当前要启动哪个进程？
- 这个进程内要创建哪些 Fiber？
- 哪些端口是内网，哪些端口是外网？
- 哪个 shard 负责哪段玩家？
- 哪些服务需要注册到服务发现？

## 持久化策略

ECS 运行时不应该阻塞等待数据库写入。

推荐模式：

```text
GameShardFiber
  -> 产生 snapshot/event，发送给 PersistenceFiber

PersistenceFiber
  -> 批量写入
  -> 更新 Redis cache
  -> 写 PostgreSQL
```

对挂机游戏来说：

- 热玩家状态放在 GameShard 内存。
- Redis 保存最新快速快照和重连缓存。
- PostgreSQL 保存可靠玩家快照和经济流水。
- 离线收益根据持久化时间戳和当前配置计算。

## 可观测性

指标应该按进程、Fiber、Actor 类型输出。

关键指标：

- Fiber mailbox 长度。
- Fiber tick 耗时。
- Actor 消息发送/接收数量。
- Actor Call 延迟。
- Location 查询延迟。
- Location 重试次数和 stale route 次数。
- Game tick 耗时。
- 每个 shard 玩家数量。
- Persistence queue 长度。
- 数据库写入延迟。
- WebSocket 连接数量。

链路追踪：

```text
Client request
  -> Gateway span
  -> Actor route span
  -> GameShard handler span
  -> Persistence span
```

## 测试策略

测试层级：

```text
YuoCore.Ecs.Tests
  组件和系统正确性
  生命周期行为
  查询性能边界

YuoCore.Runtime.Tests
  Fiber mailbox 顺序
  线程归属
  调度器关闭

YuoCore.Actor.Tests
  Send/Call
  Timeout
  本地投递
  伪远端 transport

YuoCore.Location.Tests
  Add/Get/Remove
  Lock/Unlock
  stale route retry
  迁移补偿

YuoCore.Backend.Tests
  HTTP/WebSocket API
  PostgreSQL/Redis Testcontainers
```

基准和压测层级：

```text
YuoCore.Ecs.Benchmarks
  100 种组件
  100 个 system
  单组件、双组件、三组件、多组件 system
  组件继承形态

YuoCore.Actor.Benchmarks
  mailbox 吞吐
  本地 actor call
  跨 Fiber call
  序列化成本

YuoCore.Benchmarks
  完整挂机游戏模拟
  1w 真实玩家
  重连、快照、保存、离线收益压力
```

## 落地路线

第一阶段：保持 ECS 稳定。

- 保留 `YuoCore.Ecs` API。
- 不把网络、数据库、ASP.NET 概念塞进 ECS。
- 保持现有 benchmark 可运行。

第二阶段：拆分 Runtime 和 Actor。当前已完成基础拆分。

- 新建 `YuoCore.Runtime`。
- 把 Fiber scheduler 和 mailbox pump 放进去。
- `YuoCore.Actor` 只负责消息模型和路由。

第三阶段：引入 Location。

- 新建 `YuoCore.Location`。
- 实现稳定 ID 到 actor address 的查询。
- 加入 lock/unlock 和 retry 语义。
- 补充 stale route 和迁移测试。

第四阶段：规范服务启动。

- 新建 `YuoCore.Config`。
- 定义 process/fiber/service 的 JSON Schema。
- 新建 `YuoCore.Server`，按配置启动 Fibers。

第五阶段：强化网络和协议。

- 新建 `YuoCore.Network`。
- 新建 `YuoCore.Protocol`。
- 区分外部客户端消息和内部 actor 消息。
- 加入本地投递短路。

第六阶段：规模化验证。

- 增加 actor benchmark。
- 增加 Location benchmark。
- 扩展 1w 玩家压测，覆盖重连、快照、保存、离线收益。

## 非目标

- 不直接复制 ET10 到活动源码。
- 不强行把 ET 的完整 Entity 树模型套到现有 Yuo ECS 上。
- 不通过到处加锁来让 `YuoWorld` 线程安全。
- 不允许客户端 WebSocket 消息直接修改 shard 状态。
- 不把 PostgreSQL、Redis 依赖放进 `YuoCore.Ecs`。

## 最终形态

YuoCore 最终应该是：

```text
YuoCore.Ecs
  纯逻辑核心。

YuoCore.Runtime
  安全的单线程执行容器。

YuoCore.Actor + YuoCore.Location
  分布式消息和对象寻址。

YuoCore.Network + YuoCore.Protocol
  传输和协议契约。

YuoCore.Server
  多进程、多 Fiber 启动和编排。

YuoCore.Backend
  基于框架写出来的挂机游戏后端。
```

这套架构的方向就是：

```text
ET 的 Fiber / Actor / Location 架构思想
  +
你的 Yuo ECS 逻辑核心
  =
YuoCore
```

这样既能获得 ET 风格的服务端架构能力，又不会破坏你现有 ECS 的设计。
