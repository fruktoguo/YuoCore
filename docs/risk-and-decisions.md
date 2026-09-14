# YuoCore 风险与待裁定项

本文档记录不能擅自处理的架构风险、语义取舍和后续裁定项。

## 当前风险

### Web TypeScript 客户端当前仍是手写最小 codec

当前已新增：

```text
clients/web/yuocore-client-ts
YuoMmoClient
decodeClientEnvelopeV1
login/click/snapshot/enterMap/move/disconnect/stateSnapshot smoke
@yuocore/client npm package metadata
scripts/run-web-sdk-pack-smoke.sh
browser-smoke.mjs
scripts/run-web-backend-browser-smoke.sh
YuoMmoWebSocketSession
```

语义：

```text
Web SDK 只处理客户端协议和 transport 抽象，不暴露 Actor/Fiber/Location/ECS 内部对象。
MMO facade 是示例 Backend 的薄客户端封装，调用仍通过 V1 envelope 和 opcode。
SDK 已通过 npm pack + 临时 TypeScript consumer 安装验证，说明当前包结构、exports、类型声明和 ESM runtime import 可用。
SDK 已通过 Chrome/Chromium headless browser-smoke，说明 dist/index.js 可以被真实浏览器以 ESM 方式导入。
SDK 已通过真实 Backend WebSocket 浏览器 smoke，说明 Chrome/Chromium 中的 YuoMmoClient 可以连接示例 Backend /ws，并完成登录、进图、移动、状态快照、断线保存和同 playerId 手动重连恢复闭环。
Web SDK 已提供 YuoMmoWebSocketSession，用于 UI 层组合 connect / reconnect / disconnect 和当前 playerId。
```

仍需注意：

```text
业务 codec 当前是手写最小实现，只覆盖当前示例协议里的非负数值路径。
正式项目如果继续手写 TypeScript protobuf，容易和 src/YuoCore.Protocol/Contracts/yuocore.proto 字段漂移。
这不是服务端性能热点，但会影响 Web/Unity 协议契约一致性。
npm pack smoke 只证明当前包可安装和可导入，不证明协议生成策略已经完成。
browser-smoke 只验证浏览器模块导入和核心导出，不启动 Backend，也不覆盖真实 WebSocket、UI、断线重连或浏览器兼容矩阵。
Backend browser smoke 覆盖真实 /ws 请求闭环和手动重连恢复，但不等价于完整 Web UI、自动重连状态机、多浏览器矩阵或长时间 push 稳定性测试。
YuoMmoWebSocketSession 是显式调用式会话管理器，不会自动监听 close 后无限重连；退避、重试预算和 UI 状态提示仍应由更高层策略决定。
```

后续需裁定：

```text
TypeScript protobuf codegen 选型：protobuf-ts、bufbuild/protobuf-es 或 protobufjs。
Web 与 Unity 是否统一从同一 proto 生成，还是先保留手写 SDK 作为 smoke 层。
生成产物是否提交进仓库，还是由 build/package 流程生成。
```

### C# / Unity 客户端已有显式连接会话，但不是自动重连状态机

当前已新增：

```text
YuoCore.Client
YuoMmoClientConnection
ConnectAsync / ReconnectAsync / DisconnectAsync / CloseAsync
```

语义：

```text
YuoMmoClientConnection 只管理当前 IYuoClientTransport、YuoMmoClient 和 playerId。
它不引用 Unity WebSocket、TCP、KCP、Gateway、Actor、ECS 或 Backend。
Unity 或其他客户端可以把自己的 transport factory 注入进去，按同一套显式 connect/reconnect/disconnect 流程使用。
```

仍需注意：

```text
这不是自动重连状态机。
它不会监听 transport close 后自动退避重连，也不会替 UI 决定重连次数、等待时间或登录态提示。
客户端侧已提供 `YuoClientStateSyncReconnectRecovery`，用于在宿主决定重连后复用当前 playerId、streamId 和 knownTick 执行一次 StateSync recover。
这只是最小组合器，不是完整在线状态机。
自动重连、指数退避、网络错误分类和 UI 状态应作为更高层策略组合，不能塞进底层 IYuoClientTransport。
```

验证：

```text
YuoCore.Client.Tests: Passed 9
Unity adapter build: 0 warnings, 0 errors
NuGet consumer smoke 已覆盖 YuoMmoClientConnection。
```

### 长时间 StateSync/Persistence 负载存在 payload 分配压力

30 秒 `local-composite` soak smoke 显示：

```text
players=10,000 stateEntities=10,000 duration=30s
actualTicks=1,363
tick p95=37.517ms
allocated=8,678MB
gc0=592 gc1=414 gc2=84
```

初步判断：

```text
当前压测负载会为每个 entity/tick 构造 StateSync payload。
当前 persistence 压测会为每个 command 构造事件 payload。
这会放大 GC 压力，尤其在长时间 soak 下比 5 tick quick 更明显。
该热点不应直接归因到 ECS system 调度本身。
LoadTests 已增加 shared-input profile；1w quick 中分配约从 32.38MB 降到 29.32MB，只降低约 9.5%，说明输入 payload 构造不是唯一大头。
StateSync 已增加 SetPayloadOwned(byte[]) 显式所有权转移 API；1w quick 中分配约从 32.38MB 降到 30.85MB，是安全小优化但不是质变。
Persistence 已增加 EnqueueBatchAsync，并在 1w/20tick local-composite 中对比 single/batch：tick p95 约 93.855ms -> 86.270ms，persistence p95 约 23.331ms -> 21.517ms，分配 121.86MB -> 122.05MB。
该结果说明批量入队只减少一部分队列写入调度成本，不能解决 payload/command/event 构造导致的长期 GC 压力。
```

后续需裁定：

```text
StateSync payload 是否引入组件级 writer/source generator，减少每 tick 临时 byte[]。
Persistence command 是否进一步引入批量事件编码、ArrayPool/MemoryPool、业务事件复用或幂等事件 id。
压测工具是否需要同时保留“真实分配模式”和“池化模式”两套 profile，避免优化压测工具后掩盖业务成本。
```

### AOI Stream 推送主要分配压力已经定位到 session drain

本轮新增 `GameAoiStreamPushDiagnostics` 和 `aoi-streams` 阶段分配指标后，Release 诊断结果显示：

```text
players=10,000 observers=10,000 ticks=20 shards=4
allocated=9,875.92MB
move-allocated=115.52MB
drain-allocated=9,760.40MB
coordinator-drain p95=518.242ms
coordinator-remaining-sessions p95=476.866ms

frames-published=21
deltas-published=2,155
payload=24.12MB
session-drains=210,000
session-pushes=1,612,350
session-deltas=1,595,100
session-subscriptions=17,250
```

初步判断：

```text
共享 AOI frame 发布和 protobuf delta payload 不是当前 9GB 级分配的主因。
主要压力集中在每个 session 的可见 stream 过滤、订阅变化消息构造、visibleItems/activeStreamIds/HashSet/List 分配和 YuoGatewayOutboundMessage 包装。
在 1w observer 全量 drain 场景下，即使 frame 复用有效，session 侧仍会构造大量短生命周期对象。
```

已安全处理：

```text
CreateAoiStreamSessionPushes 已移除 visibleItems.ToArray() 中间数组。
过滤后的 activeStreamIds 已不再二次 Order()。
过滤模式下 push list 容量按可见 stream 数预估，避免按全 frame item 数过度预分配。
默认可见 AOI stream 数较少时，不再为每个 session 创建 HashSet；activeStreamIds.Count <= 16 时使用线性查找，大集合仍使用 HashSet。
YuoStateSyncSubscriptionTable.SetSubscriberStreams 已增加无变化快路径；订阅集合完全一致时直接返回共享 NoChange，避免 current.ToArray 和 added/removed 中间数组。
GameAoiStreamPushFrame 已增加 ActiveStreams，frame 发布时预解析 active stream cell，避免每个 session、每个 stream 反复拆 streamId 字符串。
GameAoiVisibilityContext 已改为 readonly record struct，默认可见性策略不再为每次判定创建 YuoGridAoiStreamResolver。
GameAoiStreamPushCoordinator 已复用 dirty stream resolver，避免每个 session drain 都创建 resolver。
YuoGatewayOutboundMessage 已改为 readonly record struct，减少每个 push 包装对象的独立堆分配。
Backend AOI drain 已增加调用方缓冲路径，GameWebSocketPushPump 和 aoi-streams 压测复用 List<YuoGatewayOutboundMessage>，避免每轮 drain 都新建 session push List。
YuoAoiDirtyPlayerDto 已改为 readonly record struct，减少每次移动入队 dirty 记录时的堆对象分配。
YuoStateSyncSubscriptionTable.SetSubscriberStreams 已增加 IReadOnlyList no-change 快路径；AOI 热路径传入的规范化 stream 列表如果没有变化，会跳过 Normalize + Distinct + ToArray，重复或未规范化输入仍回落到原语义。
GameAoiStreamPushCoordinator 已复用可见 stream List scratch buffer，避免每个 session drain 都新建可见 stream 临时 List；buffer 只在 coordinator 锁内使用。
DefaultGameAoiVisibilityPolicy 已实现轻量 IGameAoiVisibilityQueryPolicy，AOI session drain 默认路径只读取 GameAoiObserverState，避免为每个 session drain 分配完整 YuoPlayerSnapshotDto；自定义旧策略仍走原 IGameAoiVisibilityPolicy。
LoadTests 的 aoi-streams 已增加 --encode-pushes，可覆盖真实 WebSocket envelope V1 编码成本。
GameWebSocketEndpoint.EncodeServerMessage 的 V1 无 Error 热路径已改为手写 protobuf envelope writer，直接把共享 payload span 写入最终 byte[]，避免 ByteString/envelope 中间复制。
GameWebSocketEndpoint 已新增 RentServerMessage，真实 WebSocket 发送路径使用 ArrayPool<byte> 租用最终发送缓冲，发送完成后归还；LoadTests 增加 --pooled-encode-pushes 覆盖该路径。

同参数 Release 诊断显示：
coordinator-drain p95 约 -59.55%
coordinator-remaining-sessions p95 约 -67.55%
drain-allocated 约 -76.78%
GC0 约 -75.85%
这是第一轮 AOI drain 质变优化。

后续同参数 Release 复测显示：
allocated 约 2,391.686MB -> 825.39MB
drain-allocated 约 2,275.60MB -> 708.57MB
GC0 约 151 -> 54
这是第二轮 AOI drain 质变优化，说明可见性上下文 class 和默认策略 resolver 分配也是主要热点。

继续把 outbound message 值类型化后：
allocated 约 821.84MB -> 787.19MB
drain-allocated 约 705.07MB -> 670.31MB
这是稳定小收益，不是质变。

调用方缓冲路径后：
allocated 约 787.19MB -> 705.81MB
drain-allocated 约 670.31MB -> 588.96MB
coordinator-drain p95 约 208.184ms -> 181.296ms
这是稳定正收益，且优于“先数再填精确数组”的回退方案。

dirty DTO 值类型化后：
move-allocated 约 116.85MB -> 102.05MB
总 allocated 小幅下降，收益主要集中在 move 阶段。

订阅表 no-change 快路径后：
allocated 约 703.42MB -> 554.45MB
drain-allocated 约 601.36MB -> 452.62MB
GC0 约 46 -> 35
这是明确分配收益；coordinator-drain p95 有波动，暂不把它记为稳定延迟收益。

可见 stream List 复用后：
allocated 约 554.45MB -> 519.75MB
drain-allocated 约 452.62MB -> 417.64MB
GC0 约 35 -> 34
coordinator-drain p95 约 199.099ms -> 180.044ms
这是安全局部收益；延迟收益需要后续多轮和 soak 验证。

默认可见性策略轻量 observer state 后：
allocated 约 519.75MB -> 492.26MB
drain-allocated 约 417.64MB -> 390.64MB
GC0 约 34 -> 32
coordinator-drain p95 本轮有波动回升，不记录为稳定延迟收益。
这是安全分配收益；保留旧策略接口是为了不破坏复杂业务可见性扩展。

真实 WebSocket envelope 编码压测显示：
旧生成类型路径 allocated 约 52,793.17MB，encode-allocated 约 52,364.59MB，websocket-encode p95 约 711.258ms。
改为 ByteString.CopyFrom(ReadOnlySpan<byte>) 后，allocated 约 35,442.02MB，encode-allocated 约 34,964.15MB。
进一步改为手写无 Error envelope writer 后，allocated 约 17,861.43MB，encode-allocated 约 17,352.97MB，websocket-encode p95 约 139.354ms。
进一步改为 pooled encode send 后，allocated 约 499.02MB，encode-allocated 约 8.01MB，websocket-encode p95 约 48.870ms。
这证明真实发送路径原先存在严重重复复制；pooled send 后，编码路径对 GC 分配的影响已经接近消失。
```

后续需裁定：

```text
是否允许为 CreateAoiStreamSessionPushes 引入调用方复用缓冲或 ArrayPool。
是否把同一观察区域内的 session 分组广播，减少每 session 重复 List 和 YuoGatewayOutboundMessage 包装。
是否调整 Gateway push API，使“同 payload 多 session”可以少分配 YuoGatewayOutboundMessage 包装对象。
是否把订阅变化消息拆成低频路径，避免每 tick 都参与热路径分配判断。
是否把 StateSubscriptionMessage 的发送从每次 drain 判断中彻底拆到 stream set 变更事件上。
是否设计同 payload 多 session 的广播 sink，使相同 AOI delta payload 可以按 session 分组发送、复用编码策略或引入压缩。
是否设计同 payload 多 session 的广播 sink，使相同 AOI delta payload 可以按 session 分组发送、降低 CPU、网络带宽和发送调用次数。
这些优化会影响 API 形状和 Gateway 发送模型，应作为下一轮结构性优化单独设计和验证。
```

优化后 30 秒 soak 仍需注意：

```text
artifacts/load/20260608T230505Z-soak

aoi-streams:
  ticks=673
  tick p95=64.798ms
  drain p95=48.987ms
  allocated=15,557.218MB
  move-allocated=3,841.588MB
  drain-allocated=11,715.436MB
  payload=784.911MB

对比旧 soak：
  ticks +23.94%
  tick p95 -16.37%
  drain p95 -19.66%
  allocated -12.17%
```

剩余风险：

```text
AOI 重复 streamId 解析已经不是主风险。
AOI 可见性上下文 class 分配和默认策略 resolver 分配已经不是主风险。
持续负载下仍有较高分配，主要需要继续拆：
  1. StateSubscriptionMessage / protobuf ToByteArray
  2. 同 payload 多 session 的正式批量发送模型
  3. TryEnqueueMove 的 Actor message/envelope、接口装箱、mailbox item 与 handler 调度分配
  4. dirty delta payload 构建
YuoFiber.PostAsync 和 InMemoryActorTransport 的 async 包装已去掉，但短压测显示它不是当前主要分配来源。
这些需要分别设计 API 级优化和压测 profile，避免为了降低压测分配而破坏真实业务语义。

注意：
“先统计可见 delta 数量再填精确数组”的方案已测试并回退。
它只小幅降低分配，但增加一次 frame item 扫描，导致 coordinator-drain p95 明显变差。
```

### Backend WebSocket 已接入快照保存恢复，并提供可选 durable outbox 路径

当前已新增：

```text
GameClientMessageDispatcher -> SnapshotPersistenceService
Hello restore from cache/repository
DisconnectRequest save offline snapshot
GameWebSocketEndpoint socket close best-effort save
PlayerSnapshotPersistenceCodec / PlayerSnapshotStoreAdapter
Storage:DurableOutboxEnabled
```

语义：

```text
客户端用已有 playerId 登录时，Backend 会先尝试恢复持久化快照，再执行 LoginPlayer。
显式断线成功后保存 Runtime 当前快照，此时 Online=false。
socket close 时也会按 sessionId 下线并尝试保存；如果玩家已经用新 session 重连，TryDisconnect 会拒绝旧 session 下线。
启用 DurableOutboxEnabled 时，保存请求先写本地 outbox，再进入内存队列，由 YuoPersistenceBackgroundService 写回 IPlayerSnapshotRepository。
```

仍需注意：

```text
默认配置仍是示例宿主的直接快照保存路径。
durable outbox 只保证“已进入 outbox 的保存命令可 replay”，不保证业务 repository exactly-once。
如果 outbox append 之前进程崩溃，本次离线快照仍可能丢失。
快照保存不是事件溯源，也不是 exactly-once。
socket close 路径的 best-effort 保存失败只记录 warning，不阻塞连接清理。
```

后续需裁定：

```text
durable outbox 是否作为生产默认，还是仅作为部署开关。
登录恢复时如果存储里的 Online=true，是否强制按离线快照处理，还是记录异常 session 清理事件。
是否需要周期性在线快照，降低异常进程退出时的进度丢失窗口。
```

### Backend 示例已有 StateSnapshot/StateDelta 推送，但还不是正式 MMO StateSync

当前已新增：

```text
GameStateSyncPushDispatcher
GameWebSocketEndpoint push loop
GameWebSocketPushPump 有界推送队列
StateSnapshotRequest v1 请求
Gateway session -> playerId 绑定
GameRuntime.TryGetWorldSnapshot
TestServer WebSocket 双客户端状态广播测试
Backend StateDelta BaseTick
YuoClientServerMessage
IYuoClientPushTransport
YuoClientStateSyncSubscription
YuoClientStateSyncRecovery
Unity PushReceived
Web subscribePush
Web subscribeStateSyncPushes
Web recoverStateSyncSnapshot
```

语义：

```text
玩家登录后，Gateway session 记录 playerId。
session 第一次进入某个 map 后，push loop 会先推送一份 StateSnapshotMessage。
GameRuntime 为 map 内在线玩家排世界事件。
WebSocket 连接的 push loop 定期 drain 当前玩家事件，并推送 StateDeltaMessage。
请求响应和推送共用同一个 session serverSeq，避免同连接序号倒退。
推送先进入有界 Channel；队列满或单次发送超时时，Backend 示例会主动断开慢客户端。
客户端可以通过 v1 StateSnapshotRequest 主动请求当前 StateSnapshotMessage，用于 Mirror 发现缺口后的最小恢复。
当前已有真实 `/ws` 测试证明：observer 进入地图后能收到 StateSnapshot；另一个玩家移动后 observer 能收到 requestId=0 的 StateDelta，并由 YuoClientStateSyncReceiver 写入本地 Mirror。
客户端基础订阅已经补齐：Unity 可选 WebSocket transport 通过 PushReceived 暴露 requestId=0 推送；Web TS transport 通过 subscribePush 暴露同类推送。
客户端 StateSync 订阅桥也已补齐：C# 使用 YuoClientStateSyncSubscription，TS 使用 subscribeStateSyncPushes，把推送应用到本地 Mirror。
客户端 snapshot 恢复兜底已补齐：C# 使用 YuoClientStateSyncRecovery，TS 使用 recoverStateSyncSnapshot，在 Mirror 发现缺口后通过 StateSnapshotRequest 拉当前全量快照并应用。
Backend StateDelta 已带连接级 BaseTick，客户端可以对 snapshot -> delta -> delta 的本连接同步流做连续性校验。
```

仍需注意：

```text
这是 Backend 示例闭环，不是核心 Gateway 包的强制能力。
当前 snapshot/delta tick 使用连接级 serverSeq，保证客户端 Mirror 顺序，但还不是正式 MapFiber/AOI stream tick。
当前 payload 是 PlayerWorldState bytes，缺少通用组件 writer/source generator。
当前事件队列是内存队列，不提供离线期间可靠重放；断线重连会重新发当前 map snapshot，但不会补发离线期间 delta。
正式 MMO 还需要 AOI 可见性差分、客户端确认/补包策略和长期压测数据。
慢客户端策略目前是“断开后重连拿 snapshot”，不是 ack/replay。
StateSnapshotRequest 当前返回的是当前完整可见状态，不提供从 known_tick 开始的历史补包。
当前只提供基础 push 订阅和 StateSync Mirror 应用，不是可靠流。客户端收到 Delta 后是否自动重拉 Snapshot、如何做 ack/replay、如何处理订阅回调异常，仍需要正式策略。
当前恢复策略是“拉全量 snapshot”，不是历史 delta 补包；高频丢包或大地图下的全量快照成本还需要后续压测和 AOI 策略支撑。
BaseTick 当前使用连接级 serverSeq，不是正式 MapFiber/AOI stream tick；多分区 AOI 或跨 Map 迁移时仍需要更正式的 stream id/tick 设计。
```

后续需裁定：

```text
StateSync tick 是否统一使用 MapFiber tick，还是按 observer stream 独立递增。
StateDelta 是否要求 BaseTick 连续，还是允许部分无 BaseTick 的事件流。
PlayerWorldState 这类业务 payload 是否由 protobuf codegen、源生成器或手写 writer 维护。
客户端 ack、delta replay 是否作为正式 Gateway 协议的一部分。
是否新增统一高级 DuplexSession，把 request/response、server push、StateSync receiver、snapshot 重拉和重连恢复组合起来。
```

### Persistence 已有文件 outbox/WAL 基础，但不是 exactly-once

当前已新增：

```text
IYuoPersistenceOutbox
YuoFilePersistenceOutbox
YuoDurablePersistenceQueue
YuoOutboxPersistenceCommand
YuoPersistenceBackgroundService 写入成功后 ack outbox sequence
```

语义：

```text
YuoDurablePersistenceQueue.EnqueueAsync 先 append 本地 outbox，再把带 sequence 的命令写入内存队列。
进程重启后可以 ReplayPendingAsync，把未 ack 的 outbox 命令重新放入队列。
YuoPersistenceBackgroundService 在 Snapshot/Event store 写入成功后确认 outbox sequence。
ack 使用本地文件元数据，避免 StopAsync cancellation 在业务写入成功后打断 ack。
CompactAsync 会保留未 ack 命令并清理已 ack 记录。
YuoPersistenceWriterOptions.CompactOutboxOnStop 可在 StopAsync drain 后触发 outbox compact，默认关闭以避免停服额外 IO。
```

仍需注意：

```text
这是本地文件 outbox/WAL 基础，不是数据库级 exactly-once。
如果业务 store 写入成功但 outbox ack 文件写入失败，重启后仍可能 replay，业务 store 需要幂等 key、版本号或事件去重。
如果本地磁盘损坏、目录丢失或容器使用临时文件系统，outbox 不提供跨机器持久化保证。
多进程共享同一个 outbox 文件不是当前目标；每个进程/Shard 应使用自己的 outbox 目录或后续接入数据库 outbox。
```

后续需裁定：

```text
PostgreSQL outbox 表结构和事务边界。
事件流是否强制使用 Stream + EventId 去重。
快照写入是否使用 Kind + Id + Version 做幂等。
是否需要把 outbox ack/compact 指标暴露到 OpenTelemetry。
```

### Actor.Core 依赖 Protocol

已解决。

```text
YuoCore.Actor 源码未使用 YuoCore.Protocol 类型，已移除 csproj 残留引用。
```

### YuoActorLocationSender 默认只重试一次，但已可配置

ET10 `MessageLocationSender` 对 `ERR_NotFoundActor` 有多次重试和退避等待。

YuoCore 当前策略：

```text
默认：404 -> 失效缓存 -> 重查 Location -> 重试一次
可选：通过 YuoActorLocationSenderOptions 配置 MaxStaleRetries / RetryDelay
可选：通过 IActorDelayProvider 替换重试等待实现，默认仍是 Task.Delay
可选：通过 YuoCore.Actor.Timers 把 IActorDelayProvider 接到 YuoTimerScheduler
可选：通过 IActorRetryDelayPolicy 使用固定等待、指数退避或 jitter
```

保留默认一次重试原因：

```text
多次等待可能放大 tail latency。
默认行为不应因为吸收 ET 思想而突然变慢。
```

后续仍需裁定：

```text
不同业务类型是否需要不同重试预算。
Location 迁移期间是否需要统一错误码区分“正在迁移”和“真的不存在”。
```

### Location 锁已支持 TTL，但还不是完整分布式锁

当前已新增：

```text
IYuoLocationRegistry.LockAsync(key, owner, ttl)
InMemoryYuoLocationRegistry 自动清理过期锁
CachingYuoLocationRegistry 遇到过期锁缓存会回源刷新
IYuoLocationRegistry.MigrateAsync 可在持锁状态下校验 oldAddress 并原子切换到 newAddress
YuoLocationEntry.LockToken
IYuoLocationRegistry.UnlockAsync(key, owner, lockToken)
IYuoLocationRegistry.MigrateAsync(key, owner, lockToken, oldAddress, newAddress)
InMemoryYuoLocationRegistry / CachingYuoLocationRegistry 会在公开操作入口检查 CancellationToken，避免取消请求继续命中缓存或修改状态
```

语义：

```text
ttl 为空或 <= 0：锁不会自动过期。
ttl 到期后，下一次 Register / Resolve / Lock / Unlock / Remove 会惰性清理锁。
过期清理会递增 Version，迁移方可以发现自己拿到的是旧状态。
LockAsync 会生成 lock token；新 token 重载可以防止同一个 owner 的旧迁移流程误解锁或误迁移当前锁。
旧 owner-only Unlock / Migrate 仍保留兼容，但正式迁移流程应优先使用 token 重载。
```

仍未实现：

```text
Redis/PostgreSQL 分布式锁实现
分布式实现中的 oldAddress -> newAddress 原子迁移语义
分布式实现中的 lock token 原子校验和持久化
```

已补充：

```text
YuoActorLocationSender 在 sender cache 未命中时，如果 Resolve 得到 locked entry，会按 MaxLockedResolveRetries / LockedResolveRetryDelay 等待重查。
预算耗尽后抛 ActorError 409，不再直接向锁定中的旧地址发消息。
YuoActorLocationMigrationCoordinator 提供本进程 Lock -> token Migrate -> sender cache 失效的标准流程。
迁移失败后的清理也会优先使用 token Unlock。
ActorRouter.InvokeAsync(timeout) 的 timeout 预算已覆盖 Location Resolve 和 transport Invoke 全流程。
YuoActorLocationSender.InvokeAsync(timeout) 的 timeout 预算已覆盖业务 key Resolve、locked retry 等待、stale retry 和 transport Invoke。
如果调用方 cancellationToken 先取消，LocationSender 保持 OperationCanceledException，不伪装成 ActorError 408。
```

仍需注意：

```text
只有传入 YuoActorLocationMigrationCoordinator 的本进程 sender 会被自动失效。
其他进程、其他节点或未登记 sender 的旧缓存仍不会被 Location 锁自动通知。
分布式迁移仍需要后续统一迁移广播或服务端 agent 处理。
如果某个自定义 Location 实现 Lock 成功但不返回 LockToken，Coordinator 会 best-effort owner-only unlock 后返回失败。
```

### LocationSender messageFactory 仍会暴露 ActorId 参数

当前 API：

```csharp
sender.SendAsync(key, actorId => new SomeMessage(actorId));
```

优点：

```text
兼容现有 IActorMessage.TargetActorId 模型。
不需要反射或强行克隆 record。
```

风险：

```text
业务代码仍能看到一次 ActorId，但不需要缓存地址。
```

后续可选方向：

```text
引入 LocationMessage envelope，让业务 DTO 不包含 TargetActorId。
```

这会改变 Actor 消息模型，需要用户裁定。

### Actor.Ecs 已从 Actor.Core 拆出

`YuoCore.Actor` 已不再引用 `YuoCore.Ecs`。

当前边界：

```text
YuoCore.Actor
  ActorId / Address / Envelope / Router / Transport / Endpoint / Dispatcher 抽象 / LocationSender
  InMemoryActorLocationRegistry / InMemoryActorTransport 在入口遵守 CancellationToken，取消优先于查询和投递

YuoCore.Actor.Ecs
  YuoActorHandler<T>
  YuoActorRequestHandler<TRequest, TResponse>
  YuoEntityActorRegistry
  YuoEntityActorDispatcher
  依赖 Actor + Ecs
```

风险：

```text
使用方命名空间仍是 YuoCore.Actor。
如果项目只引用 YuoCore.Actor 而没有引用 YuoCore.Actor.Ecs，将无法使用 EntityActorDispatcher 和 Entity handler。
```

护栏：

```text
tests/YuoCore.Actor.Tests/ActorDependencyTests.cs 会检查 YuoCore.Actor 不引用 YuoCore.Ecs。
tests/YuoCore.Architecture.Tests 会检查 src 项目引用符合依赖矩阵。
```

### 项目依赖矩阵测试是强约束，需要随架构变更维护

当前已新增：

```text
tests/YuoCore.Architecture.Tests
```

它会解析 `src/**/*.csproj`，检查：

```text
核心包 Abstractions / Ecs / Tasks 不允许出现项目引用和包引用。
src 项目的直接 ProjectReference 必须符合当前依赖矩阵。
src 项目的 PackageReference 必须符合当前包依赖白名单。
Persistence.Hosting / Server.ServiceDiscovery / Backend 这类可选宿主适配不允许被核心或普通运行时包反向引用。
```

风险：

```text
这个测试是强约束。
未来确实需要新增项目引用时，必须同步更新依赖矩阵、导入组合、提取日志和测试白名单。
未来确实需要新增 NuGet 包时，也必须同步更新包依赖白名单，并确认不会影响 Unity/单机/核心包体。
如果只是为了临时调用方便而加引用，测试应该阻止提交。
```

### AI 开发入口也必须覆盖每个 src 模块

当前护栏：

```text
docs/ai-development-guide.md 是 AI/自动化开发任务入口。
tests/YuoCore.Architecture.Tests 会检查每个 src 项目都在 AI 指南中出现。
tests/YuoCore.Architecture.Tests 会检查根 AGENTS.md 持续指向 AI 指南、ET10 吸收规则和模块依赖矩阵。
```

原因：

```text
ET10 的 AGENTS 入口思想是让 AI 先走任务路由，而不是直接猜文件落点。
只有依赖矩阵测试还不够；新增模块时如果 AI 指南漏写，后续自动化仍可能把代码放到错误包里。
```

边界：

```text
这是开发期护栏，不进入运行时包。
后续可以继续把高频错误固化为 Analyzer、Generator 或模板，但当前不把这些工具链变成核心运行时依赖。
```

### Gateway clientSeq 取消回滚边界

当前已修复：

```text
Gateway pipeline 在入口取消时不会接受 clientSeq。
如果取消发生在 clientSeq 已接受、inner dispatcher 正在执行时，
pipeline 会在确认 cancellationToken 已取消后尝试回滚到接受前的 previousClientSeq。
回滚只在 LastClientSeq 仍等于本次 seq 时发生，避免覆盖已经接受的更高 clientSeq。
回滚方法是 Gateway 内部机制，不作为普通业务 API 暴露。
```

仍保留的边界：

```text
普通业务异常会转成 error response，clientSeq 保持已消费。
如果客户端并发发送更高 clientSeq，较低 seq 的取消回滚不会覆盖更高进度；此时较低 seq 的重试仍可能被判 stale。
这符合“有序输入不鼓励同连接并发重试旧序号”的模型。
```

### Analyzer 诊断规则必须有 release notes 记录

当前护栏：

```text
tests/YuoCore.Analyzers.Tests 会反射 YuoDiagnosticIds 中的所有诊断 ID。
每个诊断 ID 必须出现在 AnalyzerReleases.Shipped.md 或 AnalyzerReleases.Unshipped.md。
YUO0006 已用于阻止 IYuoGatewayDispatcher 直接引用 ECS 类型，避免 Gateway 绕过 Actor/Fiber 所有权直接改组件。
YUO0007 已用于阻止 Protocol/Contracts 命名空间下的协议 DTO 引用 ECS、Actor、Runtime、Location、Gateway 内部类型，避免 Web/Unity 共享协议泄漏服务端运行时对象。
```

原因：

```text
Analyzer 是 AI/开发期护栏。
新增诊断如果没有 release notes，后续维护者和 AI 都难以理解规则来源、严重级别和迁移方向。
```

边界：

```text
这是开发期测试，不改变运行时包。
Gateway 边界 Analyzer 只检查明确的类型引用，不追踪复杂方法调用链。
如果业务通过某个 facade 间接修改 ECS，仍需要代码审查或后续更细的模板/Generator 约束。
Protocol 边界 Analyzer 只检查命名空间和类型引用；如果团队把外部协议放在非 Protocol/Contracts 命名空间，需要后续补充命名规则或模板约束。
```

### ServiceDiscovery 已支持内存 lease，但仍不是分布式目录

当前 `YuoCore.ServiceDiscovery` 已实现内存注册、查询、订阅通知、lease ttl、Renew 和显式过期清理。

已保留的边界：

```text
不创建后台线程。
不接入远程网络。
不做主从选举。
宿主通过 RenewAsync 心跳，或通过 PruneExpiredAsync / QueryAsync 显式触发过期清理。
订阅回调异常会被隔离，避免一个坏订阅者阻断注册表更新或其他订阅者通知。
可选 subscriptionErrorHandler 用于宿主记录订阅回调异常，但不强制引入日志包。
同名服务重新注册且 kind/metadata 发生变化时，会对旧查询形状发布 Removed，再对新查询形状发布 Registered。
```

未实现：

```text
master failover
fencing epoch
跨进程 agent/proxy
持久化服务目录
租约过期的分布式一致性保证
```

原因：

```text
这些能力属于分布式可选层，不能为了 MMO 服务端提前污染核心或本机运行时。
```

建议：

```text
先让 ServerHost/Gateway/Map/Persistence 能接入接口。
真正分布式实现后续放可选包。
```

### YuoTask 不应默认替代公共 Task/ValueTask API

`YuoCore.Tasks` 提供轻量 awaitable 和池化 completion source，但它不是所有异步 API 的默认返回类型。

原因：

```text
池化 YuoTaskCompletionSource 背后的任务实例只能安全消费一次。
公共 API 使用 Task/ValueTask 更符合 .NET 生态预期。
YuoTask 更适合 Actor/Fiber 内部高频路径，在压测证明收益后局部使用。
```

使用规则：

```text
外部边界：Task / ValueTask
内部高频路径：可选 YuoTask
取消语义：使用标准 CancellationToken / OperationCanceledException
组合等待：可用 YuoTask.WaitAll / WaitAny，但注意它会消费传入任务
WaitAny 内部使用标准 Task.WhenAny 管理观察生命周期，避免池化 completion source 被迟到观察者写穿；因此它不是零分配热路径工具
非热路径桥接：可用 FromTask / AsTask；这些 API 允许分配，不应用在一帧高频循环
fire-and-forget：必须通过 Forget/Coroutine，并设置 YuoTaskExceptionHandler.UnhandledException 进入日志系统，或传入局部异常处理器
YuoTaskCompletionSource 会隔离 continuation 异常并转发到 YuoTaskExceptionHandler，避免 SetResult/SetException 被回调异常打断
```

### YuoTask Analyzer 已覆盖基础异步误用，但还不是完整数据流分析

`YuoCore.Analyzers` 当前实现：

```text
YUO0001：返回 YuoTask / YuoTask<T> 的调用不能作为裸表达式丢弃。
YUO0002：禁止普通 async void，事件处理器形状除外。
YUO0003：禁止通过 GetAwaiter().GetResult() 同步消费 YuoTask，也禁止 AsTask().GetAwaiter().GetResult() 绕路同步阻塞。
YUO0004：禁止把 YuoTask 调用结果保存到字段或属性。
YUO0005：禁止同一个 YuoTask 局部变量在同一个方法 / lambda / 本地函数作用域内被 await 多次。
YUO0005 当前是语法级护栏，不证明 if/else 等分支是否互斥；如果出现实际误报，再升级为控制流分析。
YUO0002 已覆盖普通方法、本地函数、lambda 和 anonymous method。
src/Directory.Build.props 会把 YuoCore.Analyzers 作为 Analyzer 引入大部分 src 项目，不作为运行时引用。
```

已允许：

```text
await
Forget / Coroutine
return
赋值
显式作为参数传递
```

未覆盖：

```text
互斥分支中重复 await 的精确控制流判定
跨方法、集合保存、闭包捕获、ref/out 传递或别名传播导致的多次 await
YuoCore.Tasks 内部实现暂未启用该 analyzer，因为它需要少量刻意的 async void Forget 桥接
Runtime/Actor 内部是否应该批量切换到 YuoTask
更复杂委托推断或 generator 生成代码里的 async void 变体
```

这些需要后续 Analyzer 规则和 benchmark 数据继续推进。

### YuoTask 多线程友好不等于多次 await

当前 `YuoTask` 已改为完全自研 awaitable，不再包装 `ValueTask`。

边界：

```text
允许：多线程 Rent / SetResult / SetException / continuation 注册 / pool 归还
禁止：同一个 pooled YuoTask 被多次 await / 多个 continuation 竞争消费
```

设计原因：

```text
YuoTask 面向高频内部路径和对象池复用。
一次任务实例只应消费一次；如果需要多消费者或长期缓存，应使用 Task/ValueTask 或显式广播机制。
```

### YuoTask 已支持取消和 TrySet，但仍是内部原语

当前已补充：

```text
YuoTask.FromCanceled / YuoTask<T>.FromCanceled
YuoTaskCompletionSource.TrySetResult / TrySetException / TrySetCanceled
Forget/Coroutine 可传入局部异常处理器
```

边界：

```text
TrySet 只表示 completion source 是否成功完成，不改变一次性 await 语义。
TrySetResult 使用 Completing 中间态，避免重复 TrySet 覆盖已经完成的结果或异常。
局部 Forget 异常处理器自身抛异常会被吞掉，避免 fire-and-forget 打穿调用线程。
公共 API 仍不默认暴露 YuoTask。
```

### StartConfig ServiceDiscovery 接入保持可选

当前已新增：

```text
YuoCore.Server.ServiceDiscovery
```

它负责把 `YuoStartConfig.Services` 注册到 `IYuoServiceDiscovery`。
注册时可传统一 lease ttl，并可按 StartConfig 执行 Renew。
也可传入 `Func<YuoServiceConfig, TimeSpan?>`，为 Gateway / Map / Persistence 等服务选择不同 lease ttl。
批量注册会先预解析所有 service 的 endpoint、metadata 和 lease ttl，避免本地配置错误导致前几个服务已经发布、后续服务才失败。

保留的边界：

```text
YuoCore.Server 不直接依赖 YuoCore.ServiceDiscovery。
需要服务发现的宿主显式引用 YuoCore.Server.ServiceDiscovery。
StartConfig 服务发现适配在注册、续租、注销入口和批量循环中遵守 CancellationToken，取消优先于 endpoint 推导和后续服务副作用。
```

仍未实现：

```text
远程服务发现
fencing
跨进程 lease agent / watchdog
```

### Network 可选层只提供边界语义，不进入核心运行时

当前 `YuoCore.Network` 已有：

```text
InMemoryYuoTransport
SerializedYuoTransport
ProcessAwareYuoTransport
```

边界：

```text
Network 包不被 ECS / Runtime / Actor.Core 反向引用。
当前只固化 transport、序列化边界和进程选择语义，不提供真实远程 socket 实现。
所有 transport 在入口遵守 CancellationToken，取消优先于 endpoint 投递、序列化和远端选择。
本地 endpoint 缺失和远端 transport 未配置都统一暴露为 YuoTransportException，便于宿主层统一记录或转换协议错误。
```

仍未实现：

```text
真实 TCP/WebSocket/gRPC transport
跨机器认证、重连、背压和 frame 限流
网络层 OpenTelemetry 指标导出
```

原因：

```text
这些能力属于分布式服务器可选层，不能为了 MMO 后端提前污染单机、Unity 或同进程运行时。
```

### StartConfig JSON Schema 已加契约漂移测试

当前发现并修复：

```text
运行时 Validate 要求 ProcessKind 非空，但 schemas/startconfig.schema.json 没有把 processKind 标为 required。
```

当前护栏：

```text
tests/YuoCore.Config.Tests 会解析 startconfig.schema.json。
测试 required 字段覆盖 nodeId / processId / processKind / fibers。
测试会反射 YuoStartConfig / YuoServiceConfig / YuoFiberConfig，并要求 root/service/fiber schema properties 与当前 DTO 字段精确一致。
```

后续风险：

```text
当前测试只检查 schema 字段存在和 required 漂移，不执行完整 JSON Schema validation。
当前字段名转换假设使用 System.Text.Json 默认 camelCase；如果未来引入 JsonPropertyName 或自定义命名策略，需要同步扩展测试 helper。
如果后续 schema 约束变复杂，可以引入可选测试包做真实 schema validation，但不要让 Config 运行时依赖它。
```

### YuoFiberManager 已禁止同 manager 生命周期内复用 FiberId

参考 ET10 FiberId 统一设计后，当前 YuoCore 已收窄一个地址安全风险：

```text
YuoFiberManager.CreateFiber(string) 自动分配 id 时会跳过已使用 id。
YuoFiberManager.CreateFiber(YuoFiberOptions) 显式 id 如果当前存在或曾经使用过，会直接抛异常。
RemoveFiber 后不会释放 id 给同一个 manager 再次使用。
```

原因：

```text
FiberId 会进入 ActorAddress、Location 缓存、延迟消息、日志和外部保存的路由信息。
如果删除 Fiber 后复用同 id，旧消息可能误投递到新 Fiber。
```

仍未完全吸收 ET10 的点：

```text
当前还没有 zone + localSlot 编码。
StartConfig 仍允许显式 fiber id。
没有 ProcessFiberAddressSingleton。
```

这些属于更大范围的启动拓扑调整，暂时不强行一步到位。

### Fiber CallAsync 取消只跳过未开始的调用

当前语义：

```text
CallAsync 成功入队后，如果调用方在目标 Fiber 执行前取消，目标 Fiber 消费该 mailbox 项时会跳过业务 action。
这可以避免调用方已经收到取消，但目标 World 后续仍被修改。
```

边界：

```text
PostAsync 仍是 fire-and-forget，成功入队后不因为调用方之后取消而撤销。
已经开始执行的 CallAsync action 不会被强制抢占；业务如果需要长耗时可取消步骤，应在 action 内自行检查传入 token。
DrainMailboxAsync 只在出队前响应外部 tick 取消；一旦 action 已经从 Channel 取出，不会因为外部取消而静默丢弃。
```

原因：

```text
强制中断已经开始的 Fiber action 可能破坏单线程 World 内的一致性。
当前只处理“未开始执行”的取消，属于低风险语义收窄。
```

### StateSync AOI 仍不是完整订阅式 AOI

当前 `YuoGridAoiQuery` 只提供无状态网格可见性查询。

已实现：

```text
按 observer 半径过滤可见目标
按 phase 隔离可见目标
ECS Entity 到 AOI 查询参数的轻量投影
单帧 YuoGridAoiTargetIndex 可复用 target cell 索引，避免多个 observer 重复建索引
按 observer 维护上一轮可见集合并生成 Enter / Update / Leave delta
AOI 可见性 delta 会按 observer 自动携带上一 tick 作为 BaseTick
同一批 StateDelta 内按 NetworkId 合并 Enter / Update / Leave
StateDelta 压缩后会保留 BaseTick，避免破坏缺口检测
C# 镜像状态表可应用 Snapshot / Delta
C# 镜像状态表可用可选 BaseTick 校验 delta 基线并发现丢包缺口
C# 镜像状态表会先校验整批 delta change 再提交，避免无效 change 导致半应用状态
C# 镜像状态表会拒绝 Enter/Update 中 change.NetworkId 与 State.NetworkId 不一致的 delta，避免坏包把实体状态写到错误 key 下
C# 镜像协调器可在缺口后进入 SnapshotRequired 状态，并阻止后续 delta 继续污染本地镜像
C# 镜像协调器会拒绝旧 snapshot，避免网络乱序响应把客户端状态回滚到更早 tick
C# 镜像协调器在缺口恢复时要求 snapshot tick 至少达到 LastGap.ExpectedBaseTick
```

未实现：

```text
长期订阅 Cell
See / BeSee 缓存集合
字段级差分与 payload 压缩
Unity/Web 表现对象绑定与 TypeScript 侧应用器
Gateway / Web / Unity 侧真正发起 snapshot 重拉、队列丢弃和重连恢复流程
```

原因：

```text
订阅式 AOI 会影响 MapFiber tick、Gateway push、协议事件和客户端 SDK，需要在外部 envelope 稳定后分阶段实现。
```

### PersistenceQueue 已有可选 Hosting 写回服务，但缺少生产级重试处理

当前已新增 `YuoCore.Persistence.Hosting`，提供 `YuoPersistenceBackgroundService`：

```text
PersistenceQueue
  -> DequeueBatchAsync
  -> SaveSnapshotCommand 调用 IYuoSnapshotStore.SaveAsync
  -> AppendEventCommand 调用 IYuoEventStore.AppendAsync
```

已保留的边界：

```text
基础 Persistence 包不应该依赖 Microsoft.Extensions.Hosting。
ASP.NET Core/WorkerService 写回实现放在服务端可选适配层。
默认单条写回异常不会阻断整个后台循环。
失败记录通过可选 IYuoPersistenceFailureSink 暴露给宿主，不把 Serilog、OpenTelemetry 或 dead letter 存储绑进核心包。
可配置 MaxAttempts / RetryDelay 处理瞬时写回失败。
基础队列可读取 PendingCount / TotalEnqueued / TotalDequeued / HighWatermark，用于宿主层导出积压观测。
已定义 IYuoPersistenceDeadLetterStore 和 Hosting failure sink，宿主可接入可靠 dead letter 存储。
Hosting 包提供 YuoPersistenceQueueMetrics，使用 .NET Meter 暴露队列积压、峰值、入队/出队总数和积压比例。
Persistence 包提供 InMemoryYuoPersistenceDeadLetterStore，作为单机、测试和宿主默认兜底，不提供持久化保证。
```

仍未实现：

```text
PostgreSQL / Redis / 对象存储 / 消息系统等生产级 dead letter 存储实现
批量数据库写入优化
```

已处理：

```text
Backend 示例宿主已在 OpenTelemetry Metrics 中 AddMeter(YuoPersistenceQueueMetrics.MeterName)，
并在 DurableOutboxEnabled 时通过 YuoPersistenceQueueMetricsHostedService 激活 PersistenceQueue 指标。
这只影响 YuoCore.Backend 可选宿主层，不把 OpenTelemetry / Prometheus 依赖倒灌到 Persistence 或 Persistence.Hosting。
```

已处理的旧裁定项：

```text
YuoPersistenceBackgroundService 从队列取出 batch 后，如果停服取消发生在写库过程中，尚未处理的同批命令已经离开 Channel。
该风险已在 2026-06-09 Persistence 停服 In-Flight 命令取消中收口：

1. in-flight command 取消会标记 outbox ack blocked；
2. 通过 failure sink 记录失败；
3. 后续 StopAsync drain 只处理仍在 channel 中的命令；
4. 没有 outbox/failure sink 的宿主仍需接受 best-effort 风险。
```

### Gateway Pipeline 已接入 Backend WebSocket 示例，但外部 envelope 仍是旧协议

当前 `YuoGatewayDispatchPipeline` 已通过 Backend 内的 `GameGatewayProtobufDispatcher` 接入 WebSocket 示例。

已完成：

```text
WebSocket 外部仍收发原有 ClientEnvelope / ServerEnvelope protobuf oneof，避免破坏现有前端。
Backend 每个 WebSocket 连接会创建 YuoGatewaySession。
入站 bytes 先进入 YuoGatewayDispatchPipeline，再由 GameGatewayProtobufDispatcher 转回旧 GameClientMessageDispatcher。
pipeline 统一处理异常 -> YuoError，成功响应统一分配 serverSeq。
pipeline 在接受 clientSeq 前检查 CancellationToken，避免取消请求污染客户端重试序号。
Backend 旧 protobuf bridge 和 GameClientMessageDispatcher 在入口检查 CancellationToken，取消优先于 protobuf 解析和玩家创建副作用。
当前旧 oneof -> opcode 的映射已集中到 YuoCoreProtocolCatalog，Backend bridge 不再自己维护魔法数字。
YuoCore.Protocol 已定义正式过渡用的 YuoClientEnvelopeV1 / YuoServerEnvelopeV1 protobuf 契约。
Backend WebSocket 入口已支持旧 oneof 和 V1 envelope 双协议，便于 Web/Unity 渐进迁移。
Backend 示例新增 GameCommandQueue.ExecuteAsync 和 GameCommandProcessor；配置 `Game:GatewayRequestsUseCommandQueue=true` 时，Click / EnterMap / Move / Disconnect 会先进入后台 game command queue，再由 GameLoopService 处理并回包。
Click 的 request/response 会延迟到本轮 runtime.Tick 后完成，返回已应用本次点击的 PlayerSnapshot。
```

仍保留的风险：

```text
旧协议没有显式 opcode / clientSeq，旧请求仍只能通过 YuoCoreProtocolCatalog 推断内部 opcode，clientSeq 暂时为 0。
Web 客户端和 Unity SDK 还未默认迁移到 V1 envelope。
当前 GameGatewayProtobufDispatcher 仍是兼容桥：V1 payload 会先转换成旧 ClientEnvelope 再进入 GameClientMessageDispatcher。
未启用 `GatewayRequestsUseCommandQueue` 的兼容路径中，Click 仍是 fire-and-forget accepted 语义；正式宿主应使用队列模式。
全面迁移后，它应退化为业务 payload dispatcher，而不是继续保留旧 oneof 适配。
```

### Timers 已有手动驱动调度器，并提供可选 Runtime/Fiber 桥接

当前新增 `YuoCore.Timers`：

```text
DelayAsync / WaitUntilAsync
ScheduleOnce / ScheduleRepeated
Remove
CallbackFailed 失败观测
回调内等待同一个 scheduler 的未来 Delay/WaitUntil 会快速失败，避免 UpdateAsync 自锁
可选 MaxDueTimersPerUpdate，把同帧到期 timer 分批执行
StopOnCallbackError=true 时，重复 timer 抛出异常前会从调度器移除，避免留下不可触发的 entries
UpdateAsync 只在破坏性出队前响应外部取消；一旦 due batch 已出队，会处理完整个批次，避免 timer 被移除但不执行
```

当前新增 `YuoCore.Runtime.Timers`：

```text
YuoFiberTimerHost
Timer UpdateAsync -> Fiber TickAsync
timer 回调可 Post 到同一 Fiber mailbox 并在同一帧消费
```

已保留的边界：

```text
不创建后台线程。
不依赖 ECS、Actor、ASP.NET Core 或网络。
由宿主显式调用 UpdateAsync 推进时间。
基础 Runtime 不强依赖 Timers，Fiber 计时器通过 Runtime.Timers 可选包接入。
```

仍未实现：

```text
大量 timer 的完整 BenchmarkDotNet 报告和堆分配趋势分析
不同 shard / 不同 timer 类型的推荐预算策略
```

### Runtime.Actor 已从基础 Runtime 拆出

问题：

```text
YuoFiberActorEndpoint 原来位于 YuoCore.Runtime。
这让基础 Runtime 必须引用 Actor，进而携带 Location。
单机和 Unity 只想要 Fiber/Tick 时会被迫导入 Actor/Location。
```

决策：

```text
新增 YuoCore.Runtime.Actor 可选项目。
YuoFiberActorEndpoint 移入该项目，继续保持 namespace YuoCore.Runtime。
YuoCore.Runtime 移除 Actor 项目引用，只依赖 Abstractions + Ecs。
```

影响：

```text
纯本地多 World / 多 Fiber 场景只引用 YuoCore.Runtime 即可。
需要 Actor 消息排入目标 Fiber mailbox 的服务端/联机场景显式引用 YuoCore.Runtime.Actor。
YuoCore.Server 和 YuoCore.Backend 已显式引用该适配包。
InvokeAsync 会把 handler 异常转换成 ActorEnvelope.Failure；SendAsync 是 fire-and-forget，会隔离 handler 异常并继续 drain 后续 mailbox。
```

仍未实现：

```text
Runtime.Actor Send 异常目前没有强制日志/指标出口。
后续应通过可选运行时监控或宿主层接入，不让基础 Runtime.Actor 直接依赖 Serilog/OpenTelemetry。
```

### Persistence outbox ack 不能越过失败 sequence

问题：

```text
YuoPersistenceBackgroundService 在 StopOnCommandError=false 时会跳过失败命令并继续处理后续命令。
如果 outbox sequence 1 写库失败，sequence 2 写库成功，旧逻辑会 ack through 2。
这会让 sequence 1 在重启 replay 时被跳过，属于数据丢失风险。
```

当前决策：

```text
记录第一个失败的 outbox sequence。
一旦 sequence N 失败，后续 sequence >= N 即使业务写入成功，也不再 ack。
重启后会从 N 开始 replay。
```

取舍：

```text
该策略会保守地产生重复写入，而不是丢失写入。
业务存储应尽量设计成幂等，尤其是事件追加和快照保存。
如果未来要减少重复，需要把 outbox 从 ack-through 模型升级为更细粒度的 per-entry ack 或可压缩区间 ack。
```

### Location 锁保护删除语义

问题：

```text
Location lock 用于迁移期间保护业务 key 的地址所有权。
如果 RemoveAsync 不检查锁，迁移窗口内的旧清理逻辑可能删除当前 key，导致后续 Resolve 得到 null。
```

当前决策：

```text
RemoveAsync 在锁未过期时返回 false。
锁过期会先清理锁并递增版本，然后 RemoveAsync 可以继续删除。
```

仍需裁定：

```text
当前 RemoveAsync 没有 owner 参数，因此锁持有者也不能显式删除。
如果后续需要“持锁删除”，应新增 RemoveLockedAsync(key, owner, expectedVersion) 之类的显式 API，
不要让无 owner 的 RemoveAsync 绕过锁。
```

### StateSync 短历史 replay 已落地，但还不是完整可靠同步流

当前已完成：

```text
YuoStateDeltaReplayBuffer 保存固定容量的纯 delta 历史。
Backend 示例按玩家观察流维护 StateSync tick，不再把 delta tick 绑定到 WebSocket serverSeq。
StateSyncRecoverRequest 可以优先 replay 连续 delta，失败时 fallback 到 full snapshot。
C# 客户端和 Web TS SDK 均可消费 recover response。
StateSyncAckRequest / StateSyncAckResponse 已落地，Backend 示例会记录每个 Gateway session 的已确认 tick。
Snapshot / Delta / Recover / Ack 已携带 streamId。
Backend 示例会在同一 stream 的所有已知活跃 session 都 ACK 后裁剪该 stream 的内存 replay buffer。
Backend 示例的 tick、replay history 和 ACK 裁剪已按 streamId 存储，不再以 playerId 作为内部历史 key。
YuoStateSyncStreamIds 已把 Player/Map/AOI/InterestGroup 的 streamId 命名规则放到 StateSync.Abstractions。
YuoGridAoiStreamResolver 已提供按 mapId + phase + AOI cell 生成 map/aoi streamId 的纯策略。
GameRuntime 已提供 BuildAoiStreamSnapshots(mapId, cellSize)，可按 AOI stream 聚合当前地图玩家 snapshot。
YuoStateStreamDeltaTracker 已提供按 streamId 生成 Enter/Update/Leave delta 的纯原语。
YuoStateSyncStreamMirrorSet 已提供多 stream 独立 Mirror/Cursor，C# 和 Web 均有多流 receiver。
YuoStateSyncSubscriptionTable 已提供 subscriber-stream 订阅关系、ACK 游标、safe prune tick 和 SetSubscriberStreams 流集合替换，Backend 示例已接入。
C# / Web 客户端均已有成功应用后 ACK helper，缺口和 rejected delta 不会被确认。
C# / Web 客户端 receiver 均会记录最近 streamId，Recover helper 默认携带该 streamId，并用 recover response.streamId 兜底子 snapshot/delta。
GameAoiStreamDeltaAggregator 已接入 YuoStateStreamDeltaTracker，可把 GameRuntime 的 AOI stream snapshot 分组连续转换为 per-stream delta。
玩家跨 AOI cell 移动时，旧 stream 会生成 Leave，新 stream 会生成 Enter；消失的旧 stream 会额外跑一次空集合 delta，避免客户端残留实体。
GameStateSyncPushDispatcher 已新增 DrainAoiStreamPushes 旁路入口，可把 AOI stream delta 转成协议 StateDelta 并写入 per-stream replay history。
StateSync ACK 已支持 session 当前订阅集合内的显式 streamId，AOI stream 可以复用 ACK response 和 replay 裁剪链路。
YuoStateSyncStreamIds 已提供 TryParseMapAoi，Backend 可在不耦合地图对象的情况下识别 map/aoi stream。
AOI stream Recover 在短历史缺失时，已能基于该 stream 记录过的 cellSize 返回当前 full snapshot fallback。
新 Gateway session 显式 recover AOI stream 时，只要 streamId 可解析且玩家当前仍在同一 mapId，就会自动订阅该 stream。
Game.StateSyncAoiCellSize 已作为 Backend 示例配置项，进程尚未观察过某 stream 时也可以重建 AOI stream snapshot。
Game.StateSyncAoiRecoverRadiusCells 已作为 Backend 示例配置项，新 session recover AOI stream 时会校验目标 cell 距离玩家当前 cell 的网格半径。
不可观察 AOI stream 的 recover 会返回 StateSyncRecoverResponse(reason=stream_not_observable)，客户端可以按业务 reason 降级，而不是只能看到 Gateway error。
C# / Web 客户端均已提供单流和多流 detailed recover API，调用方可以读取 reason、streamId 和应用结果列表。
```

最新验证：

```text
streamId/ACK 裁剪后已重新运行 pack smoke：packages=15，通过，输出 artifacts/packages/pack-smoke-20260608T173535Z。
streamId/ACK 裁剪后已重新运行 quick load：输出 artifacts/load/20260608T173535Z-quick。
quick local-composite：1w players / 1w state entities / 4 shards / 5 ticks，p95=144.108ms，allocated=30.88MB。
quick ecs-shape：1w entities / 100 components / 100 systems / 4 shards / 5 ticks，p95=167.919ms，allocated=324.25MB。
这些结果说明可靠恢复链路新增的 streamId 校验和 ACK 裁剪不是当前最重热点。
```

仍然存在的风险：

```text
Replay buffer 当前按玩家观察流保存短窗口，没有按 mapId / AOI stream id / interest group 细分。
当前 Backend 示例仍选择 player:{playerId:N} 玩家观察流，虽然命名规则已抽出，但还不是正式 AOI stream id。
服务端历史只在内存里，进程重启后一定 fallback snapshot。
如果快照很大，大量客户端同时 fallback 仍可能造成 CPU、带宽和分配尖峰。
ACK helper 仍是客户端组合层显式调用，不是完整自动重连订阅管理器。
最近 streamId 记忆只能保证恢复请求回到同一条逻辑流；它不负责重建 socket、重新订阅 push、处理多流并存或跨地图迁移时的流切换策略。
多流 MirrorSet 解决的是客户端同步状态隔离；Backend 也已按 streamId 隔离 replay/ACK 裁剪并接入订阅表，但正式 AOI stream 生成、跨地图流迁移和服务端多流下发策略仍未完成。
SetSubscriberStreams 只提供迁移原语；何时因地图切换、AOI cell 切换或兴趣组变化调用它，仍需要 Map/Room 层策略。
YuoGridAoiStreamResolver 只负责生成稳定 streamId；Backend 示例还没有把事件按 AOI stream 聚合，不能直接把多个玩家共享到同一 replay history。
BuildAoiStreamSnapshots 当前只生成当前状态快照分组，不消费 per-player event queue；per-stream delta 聚合器和旁路 push 已落地，但尚未接管默认 WebSocket push。
GameAoiStreamDeltaAggregator 当前是 Backend/Map 侧原语，DrainAoiStreamPushes 也只是可选旁路；跨地图迁移、订阅移除后的最终 ACK/裁剪策略和默认下发切换仍未完成。
YuoStateStreamDeltaTracker 已接入 Backend 聚合测试和旁路推送测试，但 Map/Room 层仍需要负责何时重建订阅集合。
当前旁路如果每 tick 对所有活跃 AOI stream 调用，会推送空 delta 来维持 tick 连续性；这是正确但可能偏重的策略，后续需要用脏 stream/事件聚合减少无变化流的发送。
Game.StateSyncAoiCellSize 目前是单一全局默认值；如果未来不同地图使用不同 AOI 网格，需要升级为 map/phase 级配置或由 MapActor 提供。
Game.StateSyncAoiRecoverRadiusCells 目前只是网格距离门槛，不等于完整 AOI 可见性规则；正式 Map/Room 层仍需要接入地形、相位、隐身、权限等业务可见性。
```

补充进展：

```text
已新增 IGameAoiVisibilityPolicy 和 DefaultGameAoiVisibilityPolicy。
GameStateSyncPushDispatcher 的 AOI recover 判定已委托给 Backend/Map 侧可见性策略。
默认策略仍保持“同地图 + 当前 cell 附近 recover 半径”的旧行为，避免改变现有示例协议表现。
自定义策略已通过测试证明可以拒绝原本在网格半径内的 recover 请求。
```

剩余风险调整：

```text
AOI recover 的硬编码风险已降低，但默认策略仍只是网格距离策略。
商业 MMO 仍需要由 Map/Room 层提供真正的业务可见性策略，并把地图相位、地形、隐身、阵营、权限和副本状态纳入 GameAoiVisibilityContext 或其上层适配。
不同地图的 cellSize 目前仍来自全局 Game.StateSyncAoiCellSize 或已观察 stream 记录；正式分地图配置尚未落地。
```

### StateSync 多流订阅集合消息已落地，但 AOI coordinator 仍未完成

当前已完成：

```text
协议新增 StateSubscriptionMessage。
Opcode 新增 StateSubscription=110。
Backend AOI 旁路在 session 订阅集合变化时会推送 StateSubscriptionMessage(reset_missing=true)。
C# 多流 receiver 会按订阅集合删除缺失 stream mirror。
Web TS 多流 receiver 具备同样行为，并已通过 smoke。
```

仍然存在的风险：

```text
StateSubscriptionMessage 解决的是客户端旧 stream 清理语义，不等于正式 AOI 默认推送已经完成。
当前 DrainAoiStreamPushes 仍是旁路 API。
正式接入 WebSocket 默认推送前，必须增加 Map/Room 侧或 Backend 侧 AOI push coordinator。
该 coordinator 必须保证每个 map/tick 的 stream delta 只生成一次、replay history 只写一次，然后按 session 订阅集合复用下发。
不能让每个 WebSocket session 自己调用 GameAoiStreamDeltaAggregator；聚合器和 replay buffer 都要求同一 stream tick 单调递增，重复调用会造成重复历史或 tick 冲突。
```

当前裁定：

```text
StateSubscriptionMessage 是 AOI 默认推送的必要前置能力，可以保留。
下一步应优先实现 AOI push coordinator，而不是直接在 GameWebSocketEndpoint 中把 drain 函数切到 DrainAoiStreamPushes。
```

补充进展：

```text
已新增 GameAoiStreamPushCoordinator。
已拆分 GameStateSyncPushDispatcher 的 AOI frame 发布和 session 下发职责。
同一 map/tick 的 AOI delta 现在可以由 coordinator 缓存并复用给多个 session。
GameWebSocketPushOptions 已新增 StateSyncMode，默认 PlayerStream，显式 AoiStream 才会启用 coordinator。
appsettings.json 和 appsettings.schema.json 已记录该配置。
```

剩余风险调整：

```text
AOI coordinator 已解决“多 session 同 tick 重复消费聚合器/重复写 replay history”的基础问题。
但当前 AoiStream 模式仍使用 map 内所有活跃 AOI stream 作为 session 订阅集合，还不是最终的“按观察者可见范围订阅附近 stream”。
下一步应把 IGameAoiVisibilityPolicy 从 recover 判定扩展到 live push 订阅集合计算，避免每个玩家收到整张 map 的 AOI stream。
当前 coordinator 只缓存最近一个 frame；长期商业服需要按 map/shard 的 tick 生命周期管理、脏 stream 聚合和背压策略。
```

补充进展：

```text
GameAoiStreamPushCoordinator 已在 session 下发阶段启用 IGameAoiVisibilityPolicy 过滤。
玩家不再默认订阅整张 map 的所有 AOI stream。
不可观察 stream 不会进入该 session 的 StateSubscriptionMessage，也不会下发对应 StateDelta。
```

剩余风险调整：

```text
live push 已复用默认网格可见性策略，但该策略仍是“同地图 + cell 半径”。
正式 MMO 仍需要把地形、相位、隐身、阵营、权限、副本层等业务规则接入 IGameAoiVisibilityPolicy。
AOI frame 当前仍按 map 全量生成，再按 session 过滤；这能保证语义正确，但大地图/高人数下仍可能浪费 CPU 和分配。
下一步性能优化应改为脏 stream/分区级 frame 生成，而不是每 tick 枚举整张地图所有 stream。
```

补充进展：

```text
YuoStateStreamDeltaTracker 已新增 TryBuildDirtyDelta。
GameAoiStreamDeltaAggregator 已新增 BuildDirtyDeltas。
GameAoiStreamPushCoordinator 已改为使用脏 stream 模式。
AOI 状态无变化时不会再发送空 StateDelta，也不会推进该 stream 的 lastTick。
下一次有变化的 delta 仍能把 BaseTick 指向客户端实际收到过的最后 tick。
```

剩余风险调整：

```text
空 delta 推送已减少，但当前仍需要每次 map tick 后构建 AOI stream snapshots 来判断是否有变化。
也就是说网络发送和 replay history 压力下降了，但 CPU 枚举/分组整张地图的成本还没有完全消除。
下一步性能优化应让 Map/Room 在移动、进入、离开时标记脏 cell，只对脏 stream 生成 snapshot/delta。
```

补充进展：

```text
GameRuntime 已开始记录 YuoAoiDirtyPlayerDto。
EnterMap / Move / Disconnect 会产生 AOI dirty 线索。
GameAoiStreamPushCoordinator 会消费这些 dirty 线索。
无 dirty 且已有 cached frame 时，coordinator 不再重建 AOI snapshots。
同一 session 已发送过同一 frame tick 时，不会重复下发 cached frame。
```

剩余风险调整：

```text
当前 dirty 线索已经能跳过无 dirty tick 的 AOI snapshot 重建。
但只要某 map 有 dirty，当前仍会重建该 map 的全量 AOI snapshots。
也就是说“无变化 tick”成本已降低，“有少量变化的大地图”仍会有全图枚举成本。
下一步需要把 dirty player DTO 进一步转成 dirty stream 集合，并提供按 streamId 构建 snapshot 的 Runtime/API 或 Map 索引。
```

补充进展：

```text
GameRuntime 已提供按指定 streamIds 构建 AOI snapshots 的 API。
GameAoiStreamDeltaAggregator 已提供 BuildDirtyDeltasForStreams。
GameAoiStreamPushCoordinator 已改为在 cached frame 存在时，只对 dirty streams 发布局部 delta。
非 dirty stream 不会再因为同一 map 内其他玩家移动而参与 delta 计算或 replay history 写入。
```

剩余风险调整：

```text
发布和聚合范围已经缩小到 dirty stream。
但 GameRuntime.BuildAoiStreamSnapshots(mapId, cellSize, streamIds) 当前内部仍枚举 map 内玩家来筛选目标 stream。
这已经避免了非 dirty stream 的 delta/replay/history 成本，但还没有完全消除大地图下的玩家枚举成本。
下一步如果压测显示 AOI snapshot 枚举是热点，应在 Map/Room 层维护 stream -> player/unit 索引。
```

补充进展：

```text
新增 aoi-streams 压测模式后，1w 玩家同 tick 全移动首次暴露出旧 WorldEvent 广播 O(N^2) 热点。
MoveActorHandler 每次移动都会 EnqueueMapEvent，而 EnqueueMapEvent 会遍历同地图所有在线玩家。
这条路径属于旧 HTTP / PlayerStream 兼容事件流，不属于 AOI Stream 状态同步必须路径。

GameOptions 已新增 WorldEventsEnabled，默认 true。
aoi-streams 压测显式关闭该开关。
关闭旧事件广播后，1w 玩家 / 4 shard / 256 observer / 5 tick 的 aoi-streams 压测结果：

tick p95: 220.428 ms
max: 220.428 ms
move p95: 39.841 ms
coordinator-drain p95: 188.401 ms
allocated: 185.08 MB

同一压测在关闭前：

tick p95: 48,837.992 ms
move p95: 36,535.242 ms
coordinator-drain p95: 12,302.746 ms
allocated: 10,194.97 MB
```

补充进展：

```text
GameStateSyncPushDispatcher.CreateAoiStreamSessionPushes 已优化 live push 可见性过滤。
同一个 session 现在只读取一次玩家 snapshot，再判断所有 active stream。
这降低了 observer * activeStream 数量放大后的 GameRuntime 锁竞争。

追加压测结果：

tick p95: 212.014 ms
coordinator-drain p95: 161.284 ms
allocated: 168.36 MB

最终 scripts/run-local-load-suite.sh --quick 中的 aoi-streams 结果：

tick p95: 207.859 ms
coordinator-drain p95: 155.695 ms
allocated: 168.38 MB
```

剩余风险调整：

```text
旧 WorldEvent 广播已经被证明不能作为 MMO 高频移动同步路径。
该能力只能作为低频兼容事件、调试事件或 HTTP 示例路径使用。
正式 MMO 状态同步应使用 StateSync AOI Stream。

当前 quick 1w 压测已经通过 250ms/500ms 阈值，但 coordinator-drain p95 仍接近 155ms 到 161ms。
这不是最终商业级结论，只能说明当前示例路径可继续推进。
下一阶段应继续拆分 drain 内部成本：

1. session 可见性过滤；
2. StateDelta protobuf payload 构建；
3. dirty stream snapshot 枚举；
4. replay history 写入；
5. outbound message 分配。

如果目标是更高 tick rate 或更多 observer，应优先实现 Map/Room stream 索引和 payload 缓存/复用，而不是继续微调锁。
```

补充进展：

```text
已在 GameRuntime 的 Backend 示例层加入 AOI stream 索引：

mapId -> streamId -> playerId set
playerId -> current stream

BuildAoiStreamSnapshots 在 cellSize 等于 GameOptions.StateSyncAoiCellSize 时走索引。
不同 cellSize 仍回退旧扫描，避免同一索引被错误复用到不同 AOI 网格配置。
```

中间风险记录：

```text
第一版索引在玩家每次移动时都 remove/add，哪怕没有跨 cell。
这导致 1w 全移动压测变差：

tick p95: 344.910 ms
move p95: 111.175 ms
coordinator-drain p95: 233.733 ms

该结果证明：AOI 索引不是无脑加缓存，移动路径写放大会直接吃掉 snapshot 优化收益。
```

已处理：

```text
UpdateAoiIndex 已增加同 stream 早退。
YuoGridAoiStreamResolver 已在 GameRuntime 内缓存。
新增测试覆盖跨 stream 移动后的 indexed snapshot。

修正后 aoi-streams 单项压测：

tick p95: 165.643 ms
move p95: 43.216 ms
coordinator-drain p95: 132.426 ms
coordinator-first-session p95: 97.218 ms
allocated: 168.17 MB

最终 scripts/run-local-load-suite.sh --quick 复验：

tick p95: 186.148 ms
move p95: 49.231 ms
coordinator-drain p95: 136.765 ms
coordinator-first-session p95: 108.329 ms
allocated: 168.17 MB
```

补充进展：

```text
YuoStateStreamDeltaTracker 已新增 BuildDeltaOrdered / TryBuildDirtyDeltaOrdered。
普通入口仍由 tracker 自己排序，Ordered 入口只给已经按 NetworkId 排好序的 AOI 聚合热路径使用。
GameAoiStreamDeltaAggregator 已切到 Ordered 入口，避免 AOI stream snapshot 和 tracker 重复排序。

单项压测：

tick p95: 184.439 ms
coordinator-drain p95: 132.418 ms
coordinator-first-session p95: 99.114 ms
allocated: 166.40 MB

最终 scripts/run-local-load-suite.sh --quick 复验：

tick p95: 181.024 ms
coordinator-drain p95: 136.774 ms
coordinator-first-session p95: 97.143 ms
allocated: 166.40 MB
```

当前裁定：

```text
这是 MMO 状态同步的第一层可靠恢复能力，可以支撑示例闭环和短断线恢复。
商业级状态同步还需要继续实现正式 AOI stream 分区、跨进程/重启后的历史策略和重连订阅恢复。
这些能力应继续放在 StateSync/Gateway/Backend 适配层，不能反向污染 Ecs 或 Runtime 核心。
```

## 2026-06-09 Persistence 文件 Dead-Letter Store

补充进展：

```text
已新增 YuoFilePersistenceDeadLetterStore。
它只依赖 BCL 和 YuoCore.Persistence，使用 JSONL 追加写入失败命令记录。
可保存并读取 SaveSnapshotCommand、AppendEventCommand 和 YuoOutboxPersistenceCommand。
Outbox wrapper 会保留 OutboxSequence，避免排查失败时丢失 WAL 顺序信息。
```

剩余风险：

```text
文件 dead-letter 是单机/小服可选可靠失败记录，不是分布式 dead-letter 系统。
它没有实现：

1. 多进程并发写入仲裁；
2. 自动重放或补偿调度；
3. 告警；
4. 文件切分和长期归档；
5. 数据库事务内 dead-letter。

商业级部署如果需要强审计和后台补偿，应把 IYuoPersistenceDeadLetterStore 替换为 PostgreSQL/对象存储/消息系统实现。
```

当前裁定：

```text
该能力可以作为 YuoCore 单机、Unity 本地服务、小型 Web 后端和测试环境的默认可靠失败记录。
它满足可裁剪原则：Persistence 核心不依赖 Hosting、ASP.NET、数据库或 Redis。
```

## 2026-06-09 WebSocket 重启恢复验证

补充进展：

```text
已新增 `/ws` 端到端测试，覆盖第一进程登录/进图/移动/显式断线保存，
然后用第二个全新 TestServer 和同一测试快照仓储按 playerId 重新登录恢复。

后续又新增非显式断线测试：客户端不发送 DisconnectRequest，直接关闭 WebSocket。
服务端 endpoint 的 finally 路径会调用 TryDisconnect + SaveAsync(CancellationToken.None)，保存 Online=false 快照；
第二个 TestServer 再按同一 playerId 登录，可以恢复 mapId、坐标和 lastInputSeq。

该测试证明当前 Backend 示例闭环已经覆盖：

1. Gateway sessionId 不暴露给客户端；
2. DisconnectRequest 以当前连接 session 为准；
3. 显式断线会保存 Online=false 快照；
4. Backend 进程重启后可从持久化层恢复 mapId、坐标和 lastInputSeq；
5. 新登录会把恢复出的玩家状态重新置为 Online=true。
```

剩余风险：

```text
该测试使用内存仓储替换 IPlayerSnapshotRepository / IPlayerSnapshotCache，证明的是 Backend 边界和恢复流程。
PostgreSQL、Redis、durable outbox 的真实组合仍需要独立集成测试和 Testcontainers 长测。

socket 非显式断开时保存已有端到端验证，但仍属于 best-effort；极端进程崩溃仍依赖周期保存、outbox 或数据库事务策略。
这不应该通过把数据库写入 ECS/Actor 热路径来解决，应继续在 Persistence/Hosting 适配层完善。
```

补充进展：

```text
已新增 Postgres + Redis + SnapshotPersistenceService 的 Testcontainers 组合测试。
该测试证明：

1. SaveAsync 会写入 Redis cache 和 PostgreSQL repository；
2. GetAsync 优先返回 Redis cache；
3. Redis miss 后能回落 PostgreSQL；
4. mapId / position / online / lastInputSeq 能穿透真实 DB/cache 序列化边界。
```

实际修复：

```text
真实 PostgreSQL/Npgsql 下，Dapper 对 timestamptz 的读取类型是 DateTime。
PostgresPlayerSnapshotRepository 已把内部 row 的 UpdatedAt 改为 DateTime，并在 ToDto 时转成 UTC DateTimeOffset。
SELECT alias 也已加双引号，避免 PostgreSQL 小写化 alias 让 Dapper 构造映射变脆。
```

剩余风险调整：

```text
Postgres + Redis 的基础保存/读取组合已验证。
durable outbox + YuoPersistenceBackgroundService + PostgreSQL repository 的真实容器组合也已验证：

1. SaveAsync 先写文件 outbox；
2. 后台服务写回 PostgreSQL；
3. 成功后 ack outbox；
4. 重启后 replay 未 ack 命令；
5. replay 后再次写回 PostgreSQL 并 ack。
```

剩余风险：

```text
PlayerSnapshotStoreAdapter 已在 PostgreSQL 写入成功后 best-effort 刷新 Redis cache。
durable outbox 容器测试已覆盖 replay 前 Redis 存在旧快照，replay 后 Redis 被刷新成新快照。

cache 刷新失败后的可观测性已有第一层收口：

1. SnapshotPersistenceService 已改为先提交 repository / durable outbox，再 best-effort 刷新 Redis；
2. Redis SetAsync 失败仍不会阻止 repository 保存、outbox 入队或 outbox ack；
3. Redis GetAsync 失败会记录 warning，并回落 PostgreSQL repository；
4. PlayerSnapshotStoreAdapter 和 SnapshotPersistenceService 都会记录 warning 日志，包含玩家 id 和原始异常；
5. 写后 cache refresh 即使收到 OperationCanceledException，也只作为 best-effort failure 记录，避免 durable commit 后反向制造失败；
6. 读取侧如果调用方取消 token，仍会抛出 OperationCanceledException，不继续 fallback；
7. 当前仍没有单独 dead-letter cache refresh 任务；
8. 当前依赖 Redis TTL 或下一次保存恢复 cache。

裁定：
这是有意为之。PostgreSQL 是 durable truth，Redis 是 best-effort cache。
不能让 Redis 故障阻止已经写入 PostgreSQL 的 outbox ack，否则缓存故障会演变成持久化 replay 积压。

后续如果需要更强一致性，应在 Backend/Cache 适配层增加告警、指标或异步补偿队列，而不是修改 YuoCore.Persistence 核心。
```

## 2026-06-09 Actor Handler 契约提前校验

补充进展：

```text
YuoActorHandlerRegistry.Add 已在注册期校验自定义 IYuoActorHandler：

1. MessageType 必须是 IActorMessage；
2. fire-and-forget 消息不能声明 ResponseType；
3. IActorRequest<TResponse> 必须声明 ResponseType；
4. ResponseType 必须等于请求接口里的 TResponse。
```

裁定：

```text
这是启动期安全检查，不属于 Actor 消息热路径优化。
正确位置是 handler registry，而不是 ActorFiber.DispatchMailboxItemAsync。

原因：
ActorFiber 应继续只负责 mailbox 串行、异常隔离和 request/response envelope。
handler 契约错误应该在启动扫描时暴露，避免运行到某条业务消息时才出现响应类型错配。
```

剩余风险：

```text
当前扫描仍基于反射和工厂返回值。
如果未来接入 DI 容器或 source generator，应继续复用同一契约检查，不能让生成/装配路径绕过 registry。
```

## 2026-06-09 Location 迁移锁期间旧缓存绕锁风险

已处理：

```text
YuoActorLocationMigrationCoordinator 现在会在 LockAsync 成功后立即失效注册进来的 YuoActorLocationSender 本地缓存。
YuoActorLocationSender 命中本地 cached address 时，默认也会重新 Resolve Location 元数据。

这样已经缓存 oldAddress 的 sender 在迁移锁持有期间不会直接打到旧 Actor：

1. coordinator 管理的 sender cache 会被主动清理；
2. 非 coordinator 主动清理的 sender，也会在 cached address 命中时检查 Location 是否 locked；
3. Resolve 看到 locked location 后按 locked retry 策略等待或返回 409；
4. 迁移完成后重新解析到 newAddress。
```

裁定：

```text
迁移锁判断必须放在 LocationSender / MigrationCoordinator，而不是让 ActorFiber 或业务 handler 自己判断。
ActorFiber 只知道 ActorId，不知道 LocationKey 的迁移锁。
业务 handler 也不应该感知“自己是不是旧地址”这种运行时路由细节。

默认开启 YuoActorLocationSenderOptions.ValidateCachedAddressOnUse。
这是一个正确性优先的取舍：多一次 Location 元数据读取，换取迁移期间不会因为本地 sender cache 绕过锁。
单机高频、无迁移锁项目可以显式关闭，但不能作为 MMO 默认行为。
```

剩余风险：

```text
当前要求 sender 重新 Resolve 时能读到最新 locked entry。
跨进程 / 多机器部署下，如果本进程使用了额外的 stale Location cache，仍需要以下可选能力之一：

1. 分布式缓存失效广播；
2. Location resolve 短 TTL；
3. stale route 失败后强制 re-resolve；
4. 迁移期间旧 Actor endpoint 自身进入拒绝写入状态。

这些能力不应该进入 YuoCore.Ecs 或 Runtime 核心，应放在可选分布式 Location/Network/ServiceDiscovery 适配层。
```

## 2026-06-09 Persistence 停服 In-Flight 命令取消

已处理：

```text
YuoPersistenceBackgroundService 在 ExecuteAsync 中处理命令时，如果 Host 停服取消 stoppingToken，
已经从队列出队但尚未写完的命令现在会：

1. 标记 outbox ack blocked；
2. 通过 failure sink 记录失败；
3. 结束后台循环，交给 StopAsync drain 队列中尚未出队的命令。
```

裁定：

```text
这是可靠停服 drain 的必要边界修复。
队列 drain 只能处理还在 channel 中的命令，不能看到已经被后台 reader 取出的 in-flight 命令。
因此 in-flight stop cancellation 必须在 ExecuteAsync 内部处理。
```

剩余风险：

```text
如果宿主没有启用 outbox，也没有配置 failure sink/dead-letter，则停服取消时的 in-flight 命令仍无法恢复。
YuoCore 不应在基础 Persistence 包里强制引入数据库、Redis 或分布式队列。

商业级配置建议：

1. 开启 durable outbox；
2. 配置 dead-letter failure sink；
3. 停服时给足够的 Host shutdown timeout；
4. 对关键经济流水优先使用 AppendEventCommand + outbox，而不是只依赖内存队列。
```

## 2026-06-09 NuGet 可选包发布覆盖

已处理：

```text
scripts/run-pack-smoke.sh 现在默认打包全部应发布 src 库，共 27 个包。
YuoCore.Backend 仍然保持 IsPackable=false，只作为 ASP.NET Core 示例宿主和集成测试目标。
```

裁定：

```text
YuoCore 的可裁剪能力必须通过包边界证明，而不是只靠源码项目引用看起来分层。
因此 pack smoke 需要覆盖 Gateway、Network、Game、Runtime.Actor、Actor.Ecs、Timers、ServiceDiscovery、Analyzer 等所有可选包。
```

剩余风险：

```text
当前 pack smoke 会扫描 nuspec 中的宿主级依赖泄漏，并证明包能成功生成。
consumer smoke 已经覆盖三个全新临时项目：

1. net10.0 核心包；
2. netstandard2.1 Unity 风格客户端包；
3. net10.0 服务端可选组合包。

这证明基础 NuGet 安装和编译路径可用，但还不是完整发布验收。

后续发布前应继续补：
1. 更细的逐包 consumer profile，而不是只测三个组合画像；
2. Unity 编辑器实际导入测试；
3. Web/TS SDK 的真实 registry dry-run、完整 UI/重连联调和 protobuf codegen 策略；
4. symbols/source-link 元数据完整性检查；
5. NuGet 包签名和许可证元数据策略。
```

补充进展：

```text
Web/TS SDK 的本地 npm tarball consumer smoke 已完成：

1. npm pack 生成 @yuocore/client tarball；
2. 全新临时 TypeScript consumer 通过 file: tarball 安装；
3. TypeScript typecheck 通过；
4. Node ESM runtime import 通过。
5. Chrome/Chromium headless browser-smoke 已验证 dist/index.js 可被浏览器 ESM 导入。
6. 真实 Backend WebSocket browser-smoke 已验证 login -> enter map -> move -> state snapshot -> disconnect -> reconnect -> get snapshot。

这移除了“包结构完全未验证”的风险。
剩余风险转为 registry 发布元数据、完整 UI/自动重连状态机和 protobuf codegen 一致性。

YuoActorLocationSenderOptions 迁移锁缓存校验选项加入后，已复跑 NuGet consumer smoke：
1. 27 个可发布包打包成功；
2. CoreNet10 consumer restore/build 通过；
3. UnityClientNetStandard consumer restore/build 通过；
4. ServerNet10 consumer restore/build 通过；
5. 输出 artifacts/packages/consumer-smoke-20260609T012008Z。

随后 CoreNet10 consumer probe 已增强为直接编译：
new YuoActorLocationSenderOptions(ValidateCachedAddressOnUse: true)
并读取 ValidateCachedAddressOnUse 属性。
复跑 consumer 输出 artifacts/packages/consumer-smoke-20260609T012834Z。
```

## 2026-06-09 Load Suite 结果汇总

已处理：

```text
scripts/run-local-load-suite.sh 现在会在 quick/soak 三项压测结束后生成 summary.md。
默认情况下也会尝试与同 mode 上一次结果生成 comparison.md；可通过 YUO_LOAD_BASELINE_DIR 指定 baseline，或用 YUO_LOAD_COMPARE_PREVIOUS=false 关闭。
summary 会列出每个场景的 Passed、ActualTicks、tick p95/max、AllocatedMb 和主要热点分段。
默认 suite 现在额外运行 aoi-streams-pooled-encode，覆盖真实 WebSocket V1 envelope pooled 编码成本。
summary 表增加编码 p95、编码分配和编码字节，避免编码成本被 drain 热点遮住。
summary 顶部会写入机器和 Runtime 元数据，包括 OS、CPU、逻辑核数、内存、dotnet 版本、git head 和 dirty/clean 状态。
scripts/compare-load-results.sh 已提供两个 load 结果目录之间的人工 baseline 对比，覆盖 tick p95、allocated MB 和 encode p95 delta。
对比使用 baseline/current 场景并集；场景新增或消失都会显示 missing，避免压测覆盖面退化被静默忽略。
对比表会显示 config match，只有 players/entities/ticks/observers 等核心配置一致时，delta 才能直接用于判断退化。
```

裁定：

```text
长期压测不能只留下分散 JSON。
YuoCore 需要一个默认人类可读摘要，方便第二天直接判断是否有严重退化，以及下一步该看哪个分段。
AOI drain 和 WebSocket envelope 编码属于不同层级的成本，默认套件必须同时保留两者。
baseline 对比先保持人工报告，不默认失败；阈值需要基于同硬件多轮样本裁定。
```

剩余风险：

```text
summary.md 只做单轮结果汇总，不做跨 run 趋势对比。
它仍不知道当前机器温度、后台负载和 .NET GC/ThreadPool 细分配置是否变化。
aoi-streams-pooled-encode 只模拟发送前编码和 pooled buffer 生命周期，不等价于真实 socket send、TLS、内核缓冲、Nginx 或公网链路成本。

后续长期压测应继续补：
1. baseline 对比的可配置失败阈值和多轮趋势归档；
2. 更完整的机器/Runtime 信息，比如 GC mode、容器限制、CPU governor、温度和后台负载；
3. p99、分配速率、GC pause 和 ThreadPool 细分；
4. soak CSV 的自动异常尖峰检测；
5. 压测结果归档策略。
```

本轮 quick 观察：

```text
artifacts/load/20260608T212221Z-quick/summary.md

aoi-streams 的主要热点仍是 drain，p95 约 126.960ms。
ecs-shape 的主要压力仍是 100 组件 / 100 system 的 tick 本体，p95 约 130.695ms，分配约 328.24MB。
local-composite 当前热点是 actor-enqueue，p95 约 50.513ms。
```

裁定：

```text
本轮 quick 没有触发阈值失败，暂不做高风险热路径改写。
如果后续 soak 或 baseline 对比显示 aoi drain / ecs-shape / actor-enqueue 继续恶化，再进入专项优化。
```

## 2026-06-09 Unity 客户端 pending request 悬挂风险

已处理：

```text
YuoClientWebSocketTransport 在 CloseAsync / Dispose / receive loop 终止时都会释放 pending request。
客户端主动关闭和服务端主动关闭均有真实 Kestrel WebSocket 集成测试覆盖。
scripts/run-client-sdk-smoke.sh 已加入 YuoCore.UnityClient.Tests。
```

裁定：

```text
这是客户端 SDK 稳定性问题，不是 ECS、Actor 或 Gateway 职责。
改动只落在 Unity 可选适配包和测试项目，保持核心库可裁剪。
```

剩余风险：

```text
Unity WebGL 仍未覆盖，因为它不使用 ClientWebSocket。
移动平台后台挂起、弱网半开连接、代理丢包和 TLS 断链仍需要真机/设备侧 smoke。
```

## 2026-06-09 Disconnect 直接改 ECS 风险

已处理：

```text
GameRuntime.TryDisconnect 不再直接修改 YuoPlayerPresenceComponent。
它改为投递 DisconnectActorMessage，并在目标 GameShard Fiber 内由 DisconnectActorHandler 校验 sessionId、更新在线状态、写 AOI dirty 和 leave 事件。
错误 session 会返回 false，不会产生离线状态和 leave 事件。
```

裁定：

```text
外部接入层可以调用 GameRuntime facade，但写 ECS 的实际动作必须进入目标 Fiber。
这和 Click / EnterMap / Move 的 Actor 化方向保持一致。
```

剩余风险：

```text
GameRuntime 内部仍有读取投影和测试辅助入口会直接读 Entity/Component。
这些读路径目前由 GameRuntime._gate 保护，并用于示例后端的 snapshot / AOI / persistence。
如果未来要把 Map/Room 真正拆成独立并行 Fiber，需要进一步把跨 Fiber 读取改成 snapshot projection 或 actor request。
```

## 2026-06-09 Load baseline 阈值裁定

已处理：

```text
compare-load-results.sh 支持可选 tick p95 / allocated MB 相对退化阈值。
run-local-load-suite.sh 可通过环境变量把这些阈值传给 comparison。
阈值只在 scenario config match=yes 时生效。
```

裁定：

```text
默认不启用失败阈值，避免短样本和不同配置误伤。
CI 或夜间任务可以显式设置：
YUO_LOAD_FAIL_TICK_P95_DELTA_PCT
YUO_LOAD_FAIL_ALLOC_DELTA_PCT
```

本轮 1w quick：

```text
artifacts/load/20260609T022906Z-quick

aoi-streams-pooled-encode p95=193.146ms，drain p95=150.142ms
aoi-streams p95=180.418ms，drain p95=133.947ms
ecs-shape p95=160.418ms
local-composite-count p95=123.905ms，actor-enqueue p95=46.299ms
```

与同配置 baseline `20260609T011443Z-quick` 对比：

```text
最大 tick p95 增幅是 ecs-shape +14.059%。
分配没有同步上升。
未触发本轮严重退化阈值。
```

剩余风险：

```text
quick 只有 5 tick，不能作为商业级长稳承诺。
soak 阈值、p99、GC pause、CPU 频率、WSL 后台负载和多轮趋势仍需要长期任务补齐。
```

## 2026-06-09 Persistence 停服后继续入队风险

已处理：

```text
YuoPersistenceQueue 新增 TryComplete。
YuoPersistenceBackgroundService.StopAsync 默认先关闭 queue 写端，再停后台循环并 drain 已入队命令。
关闭后继续 EnqueueAsync / EnqueueBatchAsync 会抛 ChannelClosedException。
```

裁定：

```text
正式宿主默认 CompleteQueueOnStop=true。
这样停服边界明确：StopAsync 开始后不再接受新的保存意图。
已经进入队列的命令仍由 StopAsync drain，或由 outbox 在重启后 replay。
```

可选例外：

```text
自定义宿主如果要在 StopAsync 后接管同一个 queue，可以设置 CompleteQueueOnStop=false。
这种模式下调用方必须自己保证不会把新命令写成无人消费的悬挂项。
```

剩余风险：

```text
CompleteQueueOnStop 只保护内存队列写端。
如果调用方使用 durable queue，并且 StopAsync 后仍直接写 outbox，再写内存队列失败，那么该命令会在下次启动 replay。
这是 durable outbox 的预期安全退路，但会带来重启后延迟写入；业务层仍要保持幂等。
```

补充验证：

```text
scripts/run-pack-consumer-smoke.sh
结果：通过。
输出：artifacts/packages/consumer-smoke-20260609T023551Z
```

本次 ServerNet10 consumer 已直接编译 `YuoPersistenceWriterOptions(CompleteQueueOnStop: false)` 和 `YuoPersistenceQueue.TryComplete()`。

裁定：

```text
停服关闭写端语义已进入可发布包的 public API 面。
后续修改该构造参数、默认值或 TryComplete 行为时，必须同步跑 NuGet consumer smoke，避免源码内测试通过但外部项目无法升级。
```

## 2026-06-09 GameRuntime 读路径 Actor 化边界

已处理：

```text
TryGetSnapshot 改为 ReadPlayerSnapshotActorMessage。
TryGetAoiObserverState 改为 ReadAoiObserverStateActorMessage。
两个 DTO 都在目标 GameShard Fiber 内生成。
```

裁定：

```text
单玩家读路径应该和写路径一样进入目标 Fiber。
读取快照会 drain 该玩家 Fiber mailbox，返回处理到读取请求为止的强一致 DTO。
这更适合 Gateway、保存、重连和 StateSync push，不再保留“外部直接观察未处理 mailbox 状态”的语义。
```

剩余风险：

```text
TryGetWorldSnapshot / BuildAoiStreamSnapshots 仍是跨玩家聚合读路径。
当前它们已经改为读取 GameRuntime 内的 YuoPlayerWorldStateDto 投影表，不再直接扫玩家 Entity 组件。
但这张表仍由 GameRuntime facade 管理，适合作为单进程示例后端和 Map/Room projection 过渡层，不是最终 Map/Room 多 Fiber 读模型。
如果未来 Map/Room 真正并行化，跨玩家读必须走 Map/Room snapshot projection，或者由 Map/Room Actor 统一生成世界快照。
不要把跨玩家聚合硬塞进单个 PlayerActor，否则会重新引入跨所有权读写。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
结果：Passed: 65

dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
结果：Passed: 16
```

## 2026-06-09 Backend 世界状态投影表

已处理：

```text
GameRuntime 新增 _worldStates。
创建、登录、恢复、点击、进图、移动、断线都会刷新 YuoPlayerWorldStateDto 投影。
BuildMapSnapshot / BuildAoiStreamSnapshots / AOI index playerId 解析 / EnqueueMapEvent 在线同图筛选都改读投影。
_worldStates 由独立 worldStateGate 保护；读侧先复制快照数组，再做跨玩家聚合。
```

裁定：

```text
跨玩家聚合读应该读稳定 DTO 投影，而不是在 Gateway/StateSync 热路径里扫 Entity 组件。
这是后续 Map/Room projection 的低风险前置步骤。
```

剩余风险：

```text
当前投影更新与 GameRuntime._gate、目标 Fiber tick 的调用约束绑定。
如果后续允许多个 GameShard 在没有 GameRuntime._gate 的情况下并行执行 actor handler，
_worldStates 必须迁移到 Map/Room 所有者，或演进为线程安全分片投影。
当前 worldStateGate 只是保护 Dictionary 并发安全，不等于最终多线程架构。
不要简单把 _worldStates 换成 ConcurrentDictionary 就宣称完成多线程一致性；
真正的一致性边界应该来自 Map/Room Fiber 所有权。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
结果：Passed: 65

dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
结果：Passed: 16

小规模并行 load suite：
输出：artifacts/load/20260609T025438Z-quick
结果：local-composite-count / aoi-streams / aoi-streams-pooled-encode / ecs-shape 全部通过。
```

## 2026-06-09 AOI session drain 不应触发 Actor 强一致读

问题：

```text
TryGetSnapshot / TryGetAoiObserverState 改为 Actor request 后，单次读取会 drain 目标 Fiber mailbox。
这对 HTTP / 保存 / 显式查询是正确语义，但不适合 AOI push 的每 session drain 热路径。
默认 1w quick 中 256 个 observer 每帧都会 drain，导致同一帧重复 flush GameShard。
```

失败证据：

```text
artifacts/load/20260609T025715Z-quick
aoi-streams p95=534.177ms
coordinator-drain p95=495.815ms
结果：失败。
```

已处理：

```text
新增 GameRuntime.TryGetProjectedAoiObserverState。
GameAoiStreamPushCoordinator.DrainPushes 改用投影 observer state 获取 mapId。
GameStateSyncPushDispatcher 默认 AOI query policy 改用投影 observer state。
ApplyClickActorHandler / TryApplyClick 不再刷新 world projection，避免点击热路径争用 worldStateGate。
```

裁定：

```text
强一致 Actor 读用于边界请求和保存。
高频状态推送应读 Map/Room projection。
当前 Backend 示例先读 GameRuntime projection；未来 Map/Room 拆分后，投影应由 Map/Room Fiber 拥有。
```

复测：

```text
artifacts/load/20260609T030633Z-quick
aoi-streams p95=210.737ms，allocated=101.246MB
aoi-streams-pooled-encode p95=226.814ms，allocated=101.316MB
local-composite-count p95=144.780ms，allocated=30.822MB
ecs-shape p95=156.187ms，allocated=327.427MB
结果：通过。
```

剩余风险：

```text
相对 artifacts/load/20260609T022906Z-quick，aoi-streams / pooled-encode / local-composite 的 tick p95 仍高约 16%~17%。
这不是阻断级退化，但也不能当作性能无影响。
后续应跑多轮 quick 或 soak，再决定是否继续优化 first-session frame publish、AOI delta 构建和 actor enqueue。
```

### MultiMap 共享空集合可被索引器写入污染（已修复）

```text
问题：MultiMap/MultiHashSetMap/MultiDicMap 的索引器在键缺失时返回共享可变 _empty，
SetComponent、RemoveComponent 等路径会把组件写进/误删共享空集合，污染后续所有缺键查询；
RemoveItem 在键缺失时还会白建空集合造成泄漏。
修复：索引器返回类型收窄为 IReadOnlyList/IReadOnlySet/IReadOnlyDictionary，
写入统一走 AddItem/RemoveItem/TryGetValue；RemoveItem 缺键直接返回 false 不再建空壳。
```
