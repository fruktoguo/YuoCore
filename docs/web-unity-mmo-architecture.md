# YuoCore Web + Unity MMO 职能分离设计

本文档定义 YuoCore 面向 Web 客户端、Unity 客户端和服务端 MMO 的长期架构。它基于当前已经落地的 `ECS + Fiber + Actor + Location` 运行时，不推翻现有 ECS，而是在 ECS 外面补齐客户端接入、状态同步、领域逻辑和持久化边界。

核心结论：

```text
YuoCore.Ecs 不是 Web 框架，也不是 Unity 框架。
YuoCore 应该成为服务端游戏运行时。

Web 和 Unity 只共享协议、错误码、状态同步格式和少量客户端安全包。
服务端内部的 ActorId、FiberId、LocationKey、数据库连接、ASP.NET Core 对客户端不可见。
```

## 一、分层原则

### 1. ECS 只负责游戏状态和组件逻辑

`YuoCore.Ecs` 继续只关心：

```text
YuoWorld
YuoEntity
YuoComponent
YuoSystem
生命周期
组件查询
```

它不应该引用：

```text
ASP.NET Core
WebSocket
PostgreSQL
Redis
OpenTelemetry
GatewaySession
Protobuf 网络连接对象
```

ECS 是游戏逻辑内核，不是网络层。

### 2. Fiber 负责所有权

服务端 MMO 里，`YuoWorld`、`YuoEntity`、`YuoComponent` 默认不是线程安全对象。

规则：

```text
当前 Fiber 拥有的 World / Entity：
  可以直接读写。

其他 Fiber / 其他进程拥有的对象：
  只能发 Actor 消息。
```

这保证不需要给每个组件加锁，也不要求所有 `YuoSystem` 都写成线程安全代码。

### 3. Gateway 只负责客户端接入

Gateway 负责：

```text
WebSocket / TCP 连接
session 管理
鉴权
协议解码
requestId / clientSeq / serverSeq
限流
心跳
客户端请求转内部 Actor 消息
服务端事件推送客户端
```

当前 Backend 示例已经落地一条最小推送链路：

```text
GameRuntime 世界事件
  -> GameStateSyncPushDispatcher 先转 StateSnapshotMessage，再转 StateDeltaMessage
  -> GameWebSocketEndpoint push loop
  -> Web / Unity 客户端 StateSync Mirror
```

这条链路仍属于示例宿主层，不能把 WebSocket、ASP.NET Core 或具体客户端连接对象反向放进 ECS / Runtime / Actor 核心。

当前 Backend 示例也已经接入最小保存恢复链路：

```text
Hello(playerId)
  -> SnapshotPersistenceService.GetAsync
  -> GameRuntime.TryRestorePlayer
  -> LoginPlayer

Disconnect / socket close
  -> TryDisconnect(sessionId)
  -> SnapshotPersistenceService.SaveAsync
```

这条链路仍是示例宿主直接快照保存，不代表最终 PersistenceQueue/outbox 策略。
当前也提供 `Storage:DurableOutboxEnabled` 可选开关，把保存请求转入 `YuoDurablePersistenceQueue` 和文件 outbox，再由 `YuoPersistenceBackgroundService` 写回仓储；该开关属于 Backend 宿主装配，不改变 GameRuntime / ECS 职责。
当前 `/ws` 端到端测试已经覆盖“第一个 Backend 保存离线快照、第二个全新 Backend 按同一 playerId 恢复”的重启级闭环；该证明仍通过外部 WebSocket 协议完成，不绕过 Gateway/Client 边界。

Gateway 不负责：

```text
移动结算
战斗结算
背包修改
地图 AOI
数据库保存
```

Gateway 收到 `Move` 后，应该投递给 `MapActor`，不能直接改 `PositionComponent`。

### 4. 外部协议不等于内部 Actor 消息

客户端协议是：

```text
C2G_Login
C2G_EnterWorld
C2G_Move
C2G_UseItem
C2G_Chat
```

内部消息是：

```text
PlayerLoginRequest
EnterMapRequest
MoveUnitRequest
UseInventoryItemRequest
PublishChatRequest
```

客户端永远不应该知道：

```text
ActorId
FiberId
YuoActorAddress
Location cache version
Entity 引用
```

Gateway 是客户端协议和内部 Actor 消息之间的翻译层。

## 二、项目职责

目标项目结构：

```text
src/
  YuoCore.Abstractions/
    通用错误、结果、时钟、运行时 ID。
    可被服务端、Web 生成工具和 Unity 安全引用。

  YuoCore.Protocol/
    外部 Client/Gateway 协议。
    内部 envelope/serializer。
    Protobuf 契约和生成类型。
    可被服务端和 Unity C# 客户端引用。

  YuoCore.Ecs/
    ECS 内核。
    不依赖 Gateway、Backend、数据库。

  YuoCore.Runtime/
    Fiber、Tick、并行调度。
    服务端优先，Unity 可以选择不用。

  YuoCore.Actor/
    ActorId、ActorRouter、ActorHandler、ActorEnvelope。
    服务端内部通信。

  YuoCore.Location/
    playerId / mapId / unitId -> ActorAddress。
    服务端内部定位。

  YuoCore.Network/
    进程内和跨进程 transport 抽象。
    不直接绑定 ASP.NET Core。

  YuoCore.Gateway/
    客户端连接抽象、Session、Gateway dispatcher。
    不直接写游戏组件。

  YuoCore.Game/
    MMO 领域对象、组件、内部 Actor 消息。
    Player / Map / Unit / Inventory / Combat 的通用定义。

  YuoCore.StateSync/
    AOI、Snapshot、Delta、EntityEnter/Leave/Update。
    Web 和 Unity 客户端消费相同状态格式。

  YuoCore.Persistence/
    Snapshot store、Event store、写回队列、后台持久化抽象。
    不阻塞 GameShard Tick。

  YuoCore.Server/
    ServerHost、StartConfig 装配。

  YuoCore.Backend/
    ASP.NET Core 宿主。
    WebSocket 入口、Controllers、OpenAPI、Docker、监控装配。
```

## 三、Unity / Web 共享边界

Web 和 Unity 应该共享：

```text
Protobuf schema
opcode
错误码
登录流程
心跳流程
重连流程
Snapshot / Delta 格式
```

Web 和 Unity 不应该共享：

```text
渲染代码
输入代码
UI 代码
服务端 Actor 细节
数据库模型
ASP.NET Core 宿主
```

Unity 推荐引用：

```text
YuoCore.Abstractions
YuoCore.Protocol
YuoCore.Client
YuoCore.StateSync.Client 子集
可选的 YuoCore.Ecs.Client 子集
```

Unity 不应该引用：

```text
YuoCore.Backend
YuoCore.Persistence.Npgsql
YuoCore.Server
ASP.NET Core
Dapper
StackExchange.Redis
```

如果未来要正式做 Unity 包，建议把客户端可用项目多目标化，例如：

```xml
<TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>
```

服务端项目保持 `net10.0` 即可。

当前落地状态：

```text
YuoCore.Abstractions / YuoCore.Protocol / YuoCore.Client 已多目标到 net10.0;netstandard2.1。
YuoCore.Client 提供 requestId、clientSeq、opcode 查表、YuoClientEnvelopeV1/YuoServerEnvelopeV1 编解码和 IYuoClientTransport。
它不实现 WebSocket/TCP/KCP；Unity 和 Web 应在各自适配层实现传输。
clients/unity/YuoCore.UnityClient 提供可选的 ClientWebSocket transport，适合 Unity Editor / Standalone，不引用 UnityEngine，不适用于 WebGL；它已能按 requestId 匹配响应，并通过 PushReceived / IYuoClientPushTransport 暴露 requestId=0 的服务端推送。
YuoCore.StateSync.Protocol 提供 StateSync 纯数据模型与 Protobuf DTO 的可选转换，避免基础 StateSync 反向依赖 Protocol。
YuoCore.Client.StateSync 提供 YuoClientStateSyncSubscription，可把 IYuoClientPushTransport 的推送自动应用到客户端 Mirror；YuoClientStateSyncRecovery 可在 Mirror 发现缺口后通过 StateSnapshotRequest 拉全量快照恢复。
clients/web/yuocore-client-ts 已提供 Web TypeScript SDK 雏形：codec/transport 抽象、V1 envelope 编解码、opcode 常量、浏览器 WebSocket request/response transport、requestId=0 push 订阅、subscribeStateSyncPushes 和 recoverStateSyncSnapshot。
Web SDK 不暴露 ActorId、FiberId、LocationKey、GatewaySession 或服务端 Entity；业务 protobuf 消息通过 codec 注入，不绑定具体 protobuf 生成器。
C# / TS 客户端发 Disconnect 时不需要知道 Gateway 内部 sessionId；Gateway 以当前连接的 YuoGatewaySession 为权威。
当前 push/StateSync 订阅仍是基础线协议能力；snapshot 重拉已有兜底 helper，但不包含 ack/replay、历史 delta 补包或完整重连恢复。
```

## 四、服务端 MMO 所有权模型

### 1. PlayerActor

负责长期角色状态：

```text
等级
经验
背包
货币
任务
成就
当前 mapId
当前 unitId
```

典型消息：

```text
CreatePlayerRequest
GetPlayerSnapshotRequest
UseItemRequest
AcceptQuestRequest
SavePlayerRequest
```

### 2. MapActor / MapFiber

负责实时地图状态：

```text
位置
移动
战斗
技能
怪物
AOI
场景 Buff
状态同步
```

典型消息：

```text
EnterMapRequest
LeaveMapRequest
MoveUnitRequest
CastSkillRequest
```

### 3. GatewaySessionActor

负责客户端连接状态：

```text
sessionId
accountId
playerId
connectionId
lastClientSeq
lastHeartbeatAt
serverSeq
```

GameShard 不保存 WebSocket 对象。需要推送时，GameShard 发事件给 Gateway，Gateway 再推给连接。

## 五、请求链路

### 登录

```text
Client
  -> Gateway WebSocket
  -> C2G_Login
  -> Auth service
  -> PlayerActor / Create or Load
  -> session 绑定 playerId
  -> G2C_Login
```

### 进入地图

```text
Client C2G_EnterWorld
  -> Gateway
  -> Location.Resolve(playerId)
  -> PlayerActor
  -> Location.Resolve(mapId)
  -> MapActor.EnterMap
  -> 创建 Unit Entity
  -> 返回初始 Snapshot
```

### 移动

```text
Client C2G_Move
  -> Gateway 校验 session 和 clientSeq
  -> Location.Resolve(unitId or mapId)
  -> MapActor / MoveUnitRequest
  -> MapFiber 修改 PositionComponent
  -> StateSync 生成 Delta
  -> Gateway 推送 G2C_StateDelta
```

### 保存

```text
PlayerActor / MapActor
  -> 生成 Snapshot / SaveEvent
  -> PersistenceQueue
  -> PersistenceFiber / BackgroundService
  -> PostgreSQL / Redis
```

游戏 Tick 不能等待数据库慢查询。

## 六、状态同步设计

新增 `YuoCore.StateSync`，负责：

```text
NetworkIdentity
ObservedBy
Snapshot
Delta
AOI Query
Dirty Component 收集
EntityEnter / EntityLeave / EntityUpdate / EntityDestroy
```

基础接口：

```csharp
public interface IYuoStateSnapshotBuilder
{
    YuoStateSnapshot BuildFullSnapshot(YuoStateSyncContext context);
}

public interface IYuoAoiQuery
{
    IReadOnlyList<long> QueryVisibleEntities(long observerEntityId);
}
```

服务端生成状态，客户端只消费状态。

客户端本地 ECS 可以做：

```text
收到 Snapshot
  -> 创建/更新本地 Entity
收到 Delta
  -> 更新组件镜像
渲染层
  -> 插值、预测、表现
```

## 七、Protocol 设计

外部客户端 envelope 建议固定为：

```protobuf
message ClientEnvelope {
  uint32 version = 1;
  uint32 opcode = 2;
  uint64 request_id = 3;
  uint64 client_seq = 4;
  bytes payload = 5;
}

message ServerEnvelope {
  uint32 version = 1;
  uint32 opcode = 2;
  uint64 request_id = 3;
  uint64 server_seq = 4;
  ErrorResponse error = 5;
  bytes payload = 6;
}
```

当前示例协议仍可以保留 oneof 形式用于 MVP，但长期推荐 opcode + bytes，便于 Web/Unity 自动生成 SDK，也便于 Gateway 做统一限流、日志和转发。

当前已落地的中间能力：

```text
YuoCore.Protocol 提供 YuoMessageRegistry。
YuoCoreProtocolCatalog 集中维护当前 yuocore.proto 的 opcode -> message type 映射。
yuocore.proto 已定义 YuoClientEnvelopeV1 / YuoServerEnvelopeV1，字段包含 version、opcode、requestId、seq 和 payload bytes。
Backend 旧 oneof bridge 只从 catalog 推断 opcode，不再在 Backend 内散落魔法数字。
Backend WebSocket endpoint 已可同时接受旧 oneof ClientEnvelope 和新的 YuoClientEnvelopeV1；响应也会按请求格式返回旧 ServerEnvelope 或 YuoServerEnvelopeV1。
Backend Gateway bridge 会把 DisconnectRequest 的 sessionId 归一化为当前连接的 YuoGatewaySession.SessionId，避免把内部连接标识暴露给 Web/Unity。
```

后续全面替换为正式外部 envelope 时，应保留 catalog 语义，并把 Web/Unity 客户端默认迁移到 YuoClientEnvelopeV1 / YuoServerEnvelopeV1。

## 八、实施阶段

### 阶段 1：协议与 Gateway 分离

目标：

```text
Backend WebSocket 只做 socket 收发。
业务 dispatch 放到 Gateway / Backend dispatcher。
Protobuf 契约移动到 YuoCore.Protocol。
```

### 阶段 2：新增 MMO 分层项目

目标：

```text
YuoCore.Gateway
YuoCore.Game
YuoCore.StateSync
YuoCore.Persistence
```

先提供稳定接口和最小实现，不急着做完整技能/地图。

### 阶段 3：PlayerActor / MapActor

目标：

```text
playerId -> Location -> PlayerActor
mapId -> Location -> MapActor
unitId -> MapFiber 内 Entity
```

### 阶段 4：状态同步 MVP

目标：

```text
Web 和 Unity 都能看到同一个地图里的玩家移动。
```

### 阶段 5：持久化异步化

目标：

```text
GameShard 不直接 await 数据库。
所有保存进入 PersistenceQueue。
```

### 阶段 6：多进程

目标：

```text
Gateway / Game / Location / Persistence 可以拆成不同进程。
ProcessAwareTransport 接真实网络 transport。
```

## 九、可复用 goal 指令

```text
基于 docs/web-unity-mmo-architecture.md 和当前 YuoCore 已有 ECS/Fiber/Actor/Location 架构，完整落地 Web + Unity MMO 职能分离方案：

1. 保持 YuoCore.Ecs 纯净，不引入 ASP.NET Core、WebSocket、数据库、Redis、OpenTelemetry 依赖。
2. 将客户端 Protobuf 契约和共享协议类型移动到 YuoCore.Protocol，Backend 只引用协议生成结果。
3. 新增并实现 YuoCore.Gateway：Session、连接抽象、Gateway dispatcher、requestId/clientSeq/serverSeq、心跳和错误返回基础设施。
4. 新增并实现 YuoCore.Game：Player/Map/Unit 的领域组件和内部 Actor 消息，明确 PlayerActor 管长期状态、MapActor/MapFiber 管实时状态。
5. 新增并实现 YuoCore.StateSync：NetworkIdentity、ObservedBy、Snapshot、Delta、AOI 查询接口和最小可测试的 snapshot builder。
6. 新增并实现 YuoCore.Persistence：SnapshotStore/EventStore 抽象、Channel 写回队列和后台写回边界，避免 GameShard 直接阻塞数据库。
7. 调整 YuoCore.Backend：WebSocket endpoint 只负责收发 bytes，业务 dispatch 放入独立 dispatcher；HTTP API 通过 Game/Gateway 边界调用，不直接污染 ECS。
8. 补充 xUnit 测试覆盖 Gateway session、Protocol 分离、StateSync snapshot、Persistence queue、Backend dispatcher。
9. 运行 dotnet build YuoCore.sln --disable-build-servers 和 dotnet test YuoCore.sln --no-build。
10. 不提交不推送；如果遇到会改变 Actor 语义、异常隔离、客户端协议兼容性或 ECS 动态能力的高风险优化，记录为待裁定项。
```

## 十、本轮落地状态

当前已完成第一批职能分离落地：

```text
YuoCore.Protocol
  已接管 yuocore.proto 的生成职责。
  Backend 和 LoadTests 改为引用协议项目，不再各自生成协议类型。

YuoCore.Gateway
  已新增 GatewaySession、SessionManager、入站/出站消息、连接抽象和 dispatcher 抽象。
  Session 覆盖 accountId/playerId 绑定、clientSeq 去重、serverSeq 递增。

YuoCore.Game
  已新增 Player/Map/Unit/Transform 等 MMO 领域组件。
  已新增 EnterMap/MoveUnit 内部 Actor request/response。

YuoCore.StateSync
  已新增 NetworkIdentity、NetworkState、ObservedBy 组件。
  已新增全量 Snapshot builder、Dirty Delta builder 和网格 AOI 查询基础。
  当前 AOI 只负责可见性查询，不负责 enter/leave 事件广播。
  StateSync 不依赖具体业务组件。

YuoCore.Persistence
  已新增 SnapshotStore/EventStore 抽象。
  已新增 Channel 写回队列，GameShard 后续可以只投递保存意图，不直接等待数据库 IO。

YuoCore.Backend
  WebSocket endpoint 已收窄为 socket bytes 收发。
  示例游戏业务分发已拆到 GameClientMessageDispatcher。
```

验证结果：

```text
dotnet build YuoCore.sln --disable-build-servers
Build succeeded.
0 Warning(s)
0 Error(s)

dotnet test YuoCore.sln --no-build
Passed: 38
Failed: 0
Skipped: 0
```

当前仍需继续推进的部分：

```text
正式 opcode + bytes 外部协议替换当前 oneof MVP 协议。
Gateway dispatcher 接入 ActorLocationSender，而不是直接调用示例 GameRuntime。
PlayerActor / MapActor 的完整注册、迁移和 Location 生命周期。
StateSync 的 Dirty/Delta/AOI 增量同步。
PersistenceQueue 接入真实 BackgroundService、PostgreSQL 和 Redis 写回实现。
Unity / Web 客户端 SDK 生成与示例。
```

## 十一、StateSync 恢复流当前设计

当前 Web/Unity 客户端状态同步恢复采用两段式策略：

```text
1. 客户端 Mirror 应用 StateDelta 时，如果 BaseTick 与本地 LastTick 不匹配，进入 SnapshotRequired。
2. 客户端发送 StateSyncRecoverRequest(playerId, knownTick)。
3. 服务端优先从短历史 ReplayBuffer 找 knownTick 之后的连续 delta。
4. 如果能 replay，返回 StateSyncRecoverResponse(replayed=true, deltas=[...])。
5. 如果历史缺失、knownTick 为空或链路不连续，返回 StateSyncRecoverResponse(replayed=false, snapshot=fullSnapshot)。
6. 客户端先应用 snapshot，再按顺序应用 deltas。
7. 客户端成功应用 snapshot/delta 后，可以发送 StateSyncAckRequest(playerId, ackTick)。
8. 服务端记录该 Gateway session 的最高 ack tick。
9. 同一 stream 的所有已知活跃 session 都 ack 后，服务端可裁剪小于等于最小 ack tick 的短历史。
```

职责边界：

```text
YuoStateDeltaReplayBuffer 位于 StateSync.Abstractions，只保存纯 YuoStateDelta。
YuoStateSyncStreamIds 位于 StateSync.Abstractions，统一 Player/Map/AOI/InterestGroup 的 streamId 命名规则。
YuoGridAoiStreamResolver 位于 StateSync.Abstractions，可按 mapId、phase 和 AOI cell 生成 map/aoi streamId。
YuoStateSyncStreamMirrorSet 位于 StateSync.Abstractions，让多条 stream 拥有独立 Mirror、LastTick 和缺口状态。
YuoStateStreamDeltaTracker 位于 StateSync.Abstractions，可按 streamId 对当前实体集合生成 Enter/Update/Leave delta。
YuoStateSyncSubscriptionTable 位于 StateSync.Abstractions，维护 subscriber-stream 关系、ACK 游标、safe prune tick 和 SetSubscriberStreams 流集合替换。
Backend 负责把 GameRuntime 世界事件转成 StateDelta，并维护示例玩家观察流；其 tick、replay history 和 ACK 裁剪已按 streamId 存储，并通过订阅表判断裁剪点。
GameRuntime 已提供 BuildAoiStreamSnapshots(mapId, cellSize)，可以把当前地图玩家按 AOI stream 分组生成只读 snapshot 聚合。
YuoCore.Client.StateSync 负责 C# 客户端恢复助手，并记录最近 LastStreamId；RecoverAsync 默认携带该 streamId。
YuoClientStateSyncMultiStreamReceiver / YuoStateSyncMultiStreamReceiver 用于正式 AOI 多流订阅客户端；简单客户端仍可使用单流 receiver。
Web recoverStateSync 同样默认携带 receiver.lastStreamId，并兼容旧的 options 参数位置。
YuoClientStateSyncAcks / ackStateSyncIfApplied 只在本地 Mirror 成功应用后发送 ACK；缺口、旧包和坏包不会被确认。
Web TS SDK 保持同一语义，但当前 codec 仍是手写示例，正式项目应由 proto 生成。
ACK 属于协议/Backend/Client 适配层能力，不进入 Ecs、Runtime、Actor 或 Gateway 核心。
当前 Backend 示例 streamId 仍选择 player:{playerId:N} 玩家观察流；正式 MMO 应把 ResolveStreamId / 订阅表升级为 map/aoi/interest group 维度。
```

后续商业化需要补：

```text
按 map / AOI stream 正式分区
跨进程 / 重启后的历史策略
断线重连后自动重新订阅观察流
大 snapshot 的压缩、限速和分片策略
```

## 十二、Unity WebSocket transport 的关闭语义

`clients/unity/YuoCore.UnityClient` 是可选客户端适配包，只面向 Unity Editor / Standalone 的 `ClientWebSocket` 场景。
它不引用 `UnityEngine`，也不引用服务端 `Actor`、`Ecs`、`Gateway`、`Runtime` 或 `Backend`。

当前关闭语义：

```text
1. request / response 仍按客户端外部协议 envelope 的 requestId 匹配。
2. requestId=0 保留给服务端 push，客户端普通 request 不允许使用 0。
3. 客户端主动 CloseAsync 时，所有未完成 request 会立即失败。
4. 服务端主动关闭 socket，receive loop 会失败所有未完成 request。
5. receive loop 正常退出但 socket 已非 Open，也视为连接终止并释放 pending。
6. Dispose 会取消 receive loop 并释放 pending。
```

这个规则的目标是避免 Unity 客户端在网络断开、切后台、场景切换或本地关闭连接时出现永久等待的 Task。

注意：

```text
Unity WebGL 不能直接使用 ClientWebSocket，仍需要单独平台适配。
WebGL / 浏览器 SDK 应继续走 Web TypeScript 客户端或未来的 WebGL 专用 bridge。
```

## 十三、Gateway 到 ECS 的写入边界

当前示例后端的客户端请求路径应保持为：

```text
WebSocket / HTTP
  -> Gateway / Controller
  -> YuoGameCommand 或 GameRuntime facade
  -> ActorRouter
  -> 目标 GameShard Fiber mailbox
  -> ActorHandler
  -> 修改 ECS Component
```

已收窄的写路径：

```text
Click      -> ApplyClickActorMessage
EnterMap   -> EnterMapActorMessage
Move       -> MoveActorMessage
Disconnect -> DisconnectActorMessage
```

注意：

```text
GameRuntime 仍保留 TryGetSnapshot、BuildSnapshot、DrainEvents、AOI 查询等读取/投影入口。
这些入口可以被 Gateway push、HTTP stats、测试和 persistence 使用，但不应该成为外部协议层直接写组件的绕路。
后续新增 UseItem、CastSkill、AcceptQuest 等业务命令时，应优先新增 Actor message / handler，而不是在 Gateway dispatcher 里直接改组件。
```
