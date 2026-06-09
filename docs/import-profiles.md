# YuoCore 导入组合

本文档记录不同使用场景应该导入哪些包，防止 MMO 后端能力污染单机和 Unity 最小路径。

## 单机最小导入

```text
YuoCore.Abstractions
YuoCore.Ecs
可选：YuoCore.Tasks
可选：YuoCore.Timers
```

适合：

- 纯 C# 单机
- 不需要多线程 Fiber
- 不需要联网

## Unity 本地 ECS

```text
YuoCore.Abstractions
YuoCore.Ecs
可选：YuoCore.Runtime
可选：YuoCore.Tasks
可选：YuoCore.Timers
可选：YuoCore.Runtime.Timers
```

说明：

- 只做本地玩法逻辑时不需要 Gateway、Backend、Persistence。
- 如果要在 Unity 内跑多个 World 或模拟服务端 shard，可以额外引入 Runtime。

## Unity 联机客户端

```text
YuoCore.Abstractions
YuoCore.Client
YuoCore.Protocol
YuoCore.StateSync.Abstractions
可选：YuoCore.Client.StateSync
可选：YuoCore.StateSync.Protocol
可选：clients/unity/YuoCore.UnityClient
可选：YuoCore.Ecs
可选：YuoCore.Tasks
可选：YuoCore.Timers
```

说明：

- Unity 不应引用 `YuoCore.Backend`。
- Unity 不应被迫引用数据库、Redis、ASP.NET Core。
- `YuoCore.Client` 只提供 request/response、opcode 和 envelope 编解码；Unity 自己选择 WebSocket/TCP/KCP 实现 `IYuoClientTransport`。
- `YuoCore.StateSync.Abstractions` 提供客户端镜像表、Snapshot/Delta 缺口处理和纯 AOI 类型，不会引入 ECS 或服务端依赖。
- `YuoCore.Client.StateSync` 用于 C# 客户端直接处理服务端 StateSnapshot/StateDelta 推送；如果客户端自己写分发器，可以只用 `YuoCore.StateSync.Protocol`。
- `YuoCore.StateSync.Protocol` 用于把 Protobuf StateSnapshot/StateDelta 转换成客户端镜像表可应用的 StateSync 模型。
- `clients/unity/YuoCore.UnityClient` 是 Unity Editor / Standalone 可选 WebSocket 适配，不引用 UnityEngine，也不适用于 WebGL。
- 客户端本地 ECS 只作为预测/表现镜像，不拥有服务端权威状态。

## Web 客户端

```text
clients/web/yuocore-client-ts
Protobuf TypeScript 生成代码
协议文档
StateSync 消息格式
可选：YuoCore.StateSync.Protocol 的 DTO 语义
```

说明：

- Web 不直接引用服务端 Actor/Fiber/Location。
- Web 通过 Gateway 暴露的外部协议通信。
- `clients/web/yuocore-client-ts` 保持与 `YuoCore.Client` 相同的 envelope/opcode/requestId/clientSeq 语义。
- TypeScript SDK 提供 codec/transport 抽象、浏览器 WebSocket request/response transport，以及通用 StateSnapshot/StateDelta Mirror；业务 protobuf 类型和 payload 解析仍由生成工具或业务层接入。

## 单进程 MMO 服务端

```text
YuoCore.Abstractions
YuoCore.Tasks
YuoCore.Timers
YuoCore.Ecs
YuoCore.Runtime
YuoCore.Runtime.Actor
YuoCore.Runtime.Timers
YuoCore.Actor
YuoCore.Actor.Ecs
YuoCore.Location
YuoCore.Protocol
YuoCore.Gateway
YuoCore.Game
YuoCore.StateSync.Abstractions
YuoCore.StateSync
YuoCore.Persistence
YuoCore.Config
YuoCore.Server
YuoCore.Backend
可选：YuoCore.Persistence.Hosting
可选：YuoCore.Actor.Timers
可选：YuoCore.ServiceDiscovery
可选：YuoCore.Server.ServiceDiscovery
```

说明：

- 只需要纯 Actor 消息、Router、Transport、LocationSender 时引用 `YuoCore.Actor`。
- 需要把 ActorId 解析到 `YuoEntity` 并在 handler 内直接操作组件时，再引用 `YuoCore.Actor.Ecs`。
- 需要让 Actor 消息进入目标 `YuoFiber` mailbox 执行时，再引用 `YuoCore.Runtime.Actor`。
- 使用 `YuoCore.Server` 时，可以在 `Start()` 前调用 `AddActorHandlersFromAssembly/Assemblies` 扫描 ECS Actor handler；启动后不允许再修改 handler 表。
- 需要让 `YuoActorLocationSender` 的重试等待走手动 timer 时，再引用 `YuoCore.Actor.Timers`。
- `YuoCore.Tasks` 是高频内部异步路径优化工具，对外 HTTP/WebSocket/API 边界仍推荐保留 `Task`/`ValueTask`。
- `YuoCore.Timers` 是手动驱动计时器，适合接到主循环或 Fiber Tick，不会创建后台线程。
- 需要把计时器固定接入 Fiber Tick 时再引用 `YuoCore.Runtime.Timers`。
- `YuoCore.Analyzers` 是开发期包，只应作为 analyzer 引入项目，不应成为运行时依赖。
- 需要用 `BackgroundService` 消费 `PersistenceQueue` 时，再引用 `YuoCore.Persistence.Hosting`；基础 `YuoCore.Persistence` 只保留抽象和队列。
- 需要把 StartConfig 中的 services 发布到服务发现时，再引用 `YuoCore.Server.ServiceDiscovery`。

## 分布式 MMO 服务端

在单进程 MMO 服务端基础上可选加入：

```text
远程 Actor transport
分布式 Location 实现
ServiceDiscovery
远程 StartConfig / 进程注册
```

要求：

- 分布式能力必须可选。
- 不能让单机和 Unity 最小导入被迫携带远程网络实现。
