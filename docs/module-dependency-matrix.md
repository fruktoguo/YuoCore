# YuoCore 模块依赖矩阵

本文档记录各模块的职责、依赖方向和适用场景。后续新增模块或吸收 ET10 能力时必须同步维护。

| 模块 | 主要职责 | 当前依赖 | 单机 | Unity | Web | MMO 服务端 | 分布式 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `YuoCore.Abstractions` | Error、Result、Clock、基础契约 | 无核心依赖 | 必选 | 必选 | 可用 | 必选 | 必选 |
| `YuoCore.Analyzers` | Roslyn 架构/用法护栏，目前包含 YuoTask 消费、async void、同步 GetResult 规则 | Roslyn 包 | 可选开发期 | 可选开发期 | 可选开发期 | 可选开发期 | 可选开发期 |
| `YuoCore.Tasks` | 轻量 awaitable、async method builder、池化 completion source、取消/组合/桥接、fire-and-forget 异常出口 | 无核心依赖 | 可选 | 可选 | 可选 | 可选 | 可选 |
| `YuoCore.Timers` | 手动驱动 Delay/WaitUntil、一次性/重复 timer、取消和回调失败观测 | `Abstractions` | 可选 | 可选 | 可选 | 推荐 | 推荐 |
| `YuoCore.Ecs` | World、Entity、Component、System | 无服务端依赖 | 必选 | 必选 | 不直接用 | 必选 | 必选 |
| `YuoCore.Runtime` | Fiber、Tick、同进程调度 | `Abstractions`、`Ecs` | 可选 | 可选 | 不直接用 | 推荐 | 推荐 |
| `YuoCore.Runtime.Actor` | 把 Actor endpoint 绑定到 YuoFiber mailbox 的可选适配 | `Runtime`、`Actor` | 可选 | 可选 | 不直接用 | 推荐 | 推荐 |
| `YuoCore.Runtime.Timers` | Fiber 与 TimerScheduler 的可选 Tick 桥接 | `Runtime`、`Timers` | 可选 | 可选 | 不直接用 | 推荐 | 推荐 |
| `YuoCore.Actor` | 同进程 Actor、Router、Transport、LocationSender | `Location` | 可选 | 可选 | 不直接用 | 推荐 | Core 部分可用 |
| `YuoCore.Actor.Ecs` | ActorId 到 YuoEntity 的本机映射、Entity handler/dispatcher、启动期 handler 扫描报告 | `Actor`、`Ecs` | 可选 | 可选 | 不直接用 | 推荐 | 可选 |
| `YuoCore.Actor.Timers` | Actor 内部重试等待到 TimerScheduler 的可选适配 | `Actor`、`Timers` | 可选 | 可选 | 不直接用 | 可选 | 可选 |
| `YuoCore.Location` | 业务 ID 到 Actor 地址、缓存、锁、TTL 锁过期 | `Abstractions` | 可选 | 可选 | 不直接用 | 推荐 | 需扩展分布式实现 |
| `YuoCore.Network` | transport 抽象、内存传输、序列化边界 | `Location`、`Protocol` | 可选 | 可选 | 可选 | 可选 | 远程实现可选 |
| `YuoCore.Protocol` | Protobuf、opcode catalog、协议 envelope、serializer | `Abstractions` | 可选 | 推荐 | 推荐 | 必选 | 必选 |
| `YuoCore.Client` | Web/Unity 共用客户端协议 SDK：requestId、clientSeq、V1 envelope、opcode 查表、抽象 transport | `Abstractions`、`Protocol` | 不需要 | 推荐 | 推荐语义 | 可选测试 | 可选 |
| `YuoCore.Client.StateSync` | C# 客户端 StateSync 推送接收器：server envelope -> Snapshot/Delta -> Mirror | `Client`、`StateSync.Protocol` | 不需要 | 可选 | 可选语义 | 不需要 | 不需要 |
| `clients/unity/YuoCore.UnityClient` | Unity Editor/Standalone WebSocket transport 适配 | `YuoCore.Client` | 不需要 | 可选 | 不直接用 | 不需要 | 不需要 |
| `clients/web/yuocore-client-ts` | Web TypeScript 客户端协议 SDK：V1 envelope、WebSocket transport、StateSync Mirror、MMO 示例 facade | TypeScript DOM API | 不需要 | 不直接用 | 推荐 | 不需要 | 不需要 |
| `YuoCore.Gateway` | Session、连接抽象、dispatcher 抽象、dispatch pipeline | `Abstractions` | 不需要 | 不需要 | 服务端侧使用 | 推荐 | 推荐 |
| `YuoCore.Game` | Player/Map/Unit 领域组件、挂机示例组件/System、内部 Actor 消息和游戏域 DTO/command | `Ecs`、`Actor` | 可选 | 可选 | 不直接用 | 推荐 | 推荐 |
| `YuoCore.StateSync.Abstractions` | Snapshot、Delta、Mirror、短历史 ReplayBuffer、网格 AOI、可见性差分等纯状态同步模型 | 无核心依赖 | 可选 | 推荐 | 推荐语义 | 推荐 | 推荐 |
| `YuoCore.StateSync` | NetworkIdentity、NetworkState、ECS Snapshot/Dirty Delta 构建器、ECS AOI 投影 | `StateSync.Abstractions`、`Ecs` | 可选 | 可选 ECS 子集 | 不直接用 | 推荐 | 推荐 |
| `YuoCore.StateSync.Protocol` | StateSync 纯数据模型与 Protobuf DTO 的可选转换 | `StateSync.Abstractions`、`Protocol` | 不需要 | 可选 | 推荐协议侧 | 推荐 | 推荐 |
| `YuoCore.Persistence` | 保存抽象、事件抽象、写回队列、批量 drain、本地文件 outbox/WAL 基础 | `Abstractions` | 可选 | 不需要 | 不需要 | 推荐 | 推荐 |
| `YuoCore.Persistence.Hosting` | PersistenceQueue 到 Snapshot/Event store 的 BackgroundService 写回适配、可选失败出口 | `Persistence`、Microsoft.Extensions.Hosting.Abstractions | 不需要 | 不需要 | 不需要 | 可选 | 可选 |
| `YuoCore.ServiceDiscovery` | 服务注册、查询、订阅通知、内存 lease/heartbeat 清理 | `Abstractions` | 不需要 | 不需要 | 不需要 | 可选 | 推荐 |
| `YuoCore.Config` | StartConfig、JSON Schema 契约、metadata | `Abstractions` | 可选 | 可选 | 不需要 | 推荐 | 推荐 |
| `YuoCore.Server.ServiceDiscovery` | StartConfig services 发布到 ServiceDiscovery 的可选适配 | `Config`、`ServiceDiscovery` | 不需要 | 不需要 | 不需要 | 可选 | 推荐 |
| `YuoCore.Server` | ServerHost、Fiber/Actor/Location 装配、启动期 Actor handler 扫描入口 | `Actor`、`Actor.Ecs`、`Config`、`Ecs`、`Location`、`Runtime`、`Runtime.Actor` | 不需要 | 不需要 | 不需要 | 推荐 | 推荐 |
| `YuoCore.Backend` | ASP.NET Core 示例宿主、可选 durable outbox 装配、WebSocket StateSync push/recover 适配 | 多个服务端模块，包含 `Persistence` / `Persistence.Hosting` / `StateSync.Protocol` 可选适配 | 不需要 | 不需要 | 服务端实现 | 可选宿主 | 可选宿主 |

## 当前注意点

- `YuoCore.Network` 当前包含本地内存 transport 和进程选择 transport。真实远程 transport 后续应放可选扩展，不反向污染 Runtime/Actor.Core。
- `YuoCore.Actor` 的错误契约同时保留数字 `Code` 和字符串 `ErrorCode`；这是 Actor.Core 内部稳定契约，不依赖 Protocol 或 Web/HTTP 错误模型。
- `YuoCore.Abstractions` 提供 `YuoException(YuoError)` 作为通用可预期业务失败出口；Gateway 可以保留该错误码，但不直接依赖 ActorException。
- `YuoCore.Location` 已支持 owner + lock token 的迁移锁重载；旧 owner-only API 保留兼容，正式迁移流程优先使用 token API。
- `YuoActorLocationMigrationCoordinator` 已默认使用 token Migrate/Unlock，并只负责本进程 sender cache 失效；跨进程迁移广播仍属于可选分布式扩展。
- `YuoCore.Client` 只负责客户端协议会话和 transport 抽象，不实现 WebSocket/TCP/KCP，不引用 Gateway、Backend、Actor、Runtime 或 ECS。
- `YuoCore.Client.StateSync` 是可选客户端桥接包，只处理服务端 StateSync 推送和本地 Mirror，不引用 ECS、Gateway、Actor、Runtime 或 Backend。
- `clients/unity/YuoCore.UnityClient` 只实现 Unity Editor/Standalone 的 ClientWebSocket 适配，不引用 UnityEngine 或服务端模块；Unity WebGL 需要单独平台适配。
- `clients/web/yuocore-client-ts` 是 Web 客户端包，不是服务端模块；当前含最小手写 MMO 示例 codec，正式项目应改为从 `yuocore.proto` 生成 TypeScript codec，避免字段漂移。
- `YuoCore.StateSync.Abstractions` 是 Web/Unity/服务端共享的纯同步模型，不依赖 ECS、Protocol 或网络；`YuoCore.StateSync` 只保留 ECS 适配。
- `YuoCore.StateSync.Abstractions` 的 `YuoStateDeltaReplayBuffer` 只保存纯 Delta 历史，用于短断线 replay；具体 WebSocket recover 协议在 Backend/Client 适配层实现。
- `YuoCore.StateSync.Protocol` 是可选桥接包，用于 Protobuf 与 StateSync 纯数据模型转换；基础 `YuoCore.StateSync` 不能反向依赖 Protocol。
- `YuoCore.Runtime.Actor` 是可选桥接包，避免基础 `YuoCore.Runtime` 因 `YuoFiberActorEndpoint` 被迫携带 Actor/Location。
- `YuoCore.Timers` 是手动驱动模块，不创建后台线程；Unity、单机和服务器都由自己的主循环或 Fiber Tick 调用 `UpdateAsync`。
- `YuoCore.Runtime.Timers` 是可选桥接包，避免基础 `YuoCore.Runtime` 被迫携带计时器 API。
- `YuoCore.Actor.Ecs` 保持与 `YuoCore.Actor` 相同命名空间，使用侧仍可 `using YuoCore.Actor;`，但项目引用必须显式选择是否导入 ECS Actor 能力。
- `YuoCore.Actor.Timers` 是可选适配包，避免基础 `YuoCore.Actor` 被迫携带 TimerScheduler。
- `YuoCore.ServiceDiscovery` 当前只有内存实现和通用接口，支持显式驱动的 lease/heartbeat，但不接入远程网络；分布式服务发现应作为可选实现扩展。
- `YuoCore.Server.ServiceDiscovery` 是可选适配包，避免 `YuoCore.Server` 默认依赖服务发现。
- `YuoCore.Persistence` 已提供可选本地文件 outbox/WAL 基础能力；需要更可靠时可用 `YuoDurablePersistenceQueue` 先写 outbox 再进入内存队列，重启后 replay 未 ack 命令。
- `YuoCore.Persistence.Hosting` 是可选 Hosting 适配包，避免基础 `YuoCore.Persistence` 强制携带 Microsoft.Extensions.Hosting；失败 sink 也只暴露抽象，日志、指标和 dead letter 由宿主自行接入。它支持 StopAsync 时 drain 已到达的内存队列命令、写入成功后 ack outbox sequence，并可通过 `CompactOutboxOnStop` 在停服后压缩已确认 outbox。
- `YuoCore.Backend` 是示例宿主，不应该被任何核心模块引用。
- `YuoCore.Game` 已承接不依赖宿主的游戏域类型：挂机成长组件/System、玩家快照、地图状态、世界事件和内部 command。它不能引用 ASP.NET Core、数据库、Redis、WebSocket 或 Backend。
- `YuoCore.Backend` 当前仍包含 MMO 示例运行时闭环第一版：登录/重连、进入地图、移动、同 map 广播、断线保存、快照恢复和 PostgreSQL/Redis 适配。Backend 负责宿主和适配，不再定义纯游戏域组件/DTO。
- `YuoCore.Backend` 可通过 `Storage:DurableOutboxEnabled=true` 启用 `YuoDurablePersistenceQueue + YuoFilePersistenceOutbox + YuoPersistenceBackgroundService`；示例配置要求同时启用 PostgreSQL，避免 outbox ack 到 Noop 仓储造成假持久化。
- `YuoCore.Backend` 示例已把 Runtime 世界事件转换为 v1 envelope 的 `StateDelta` 推送；这是宿主适配能力，不反向进入 `YuoCore.Gateway` 或核心运行时包。
