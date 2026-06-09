# YuoCore 架构落地与性能热点记录

本文记录本轮按 `docs/yuocore-et-essence-final-design.md` 推进后的实现结果、验证结果、性能热点和需要裁定的架构项。

## 2026-06-09：Web TypeScript SDK npm pack consumer smoke

本轮把 Web TypeScript SDK 从“源码内 smoke”推进到“npm tarball 可被外部项目消费”的验证：

```text
clients/web/yuocore-client-ts/package.json
  -> 包名 @yuocore/client
  -> ESM package
  -> exports 指向 dist/index.js 和 dist/index.d.ts
  -> files 只包含 dist 与 README.md

scripts/run-web-sdk-pack-smoke.sh
  -> 构建 SDK
  -> npm pack 生成 tarball
  -> 创建全新临时 TypeScript consumer
  -> 通过 file: tarball 安装 @yuocore/client
  -> TypeScript typecheck
  -> Node ESM runtime import 检查

clients/web/yuocore-client-ts/scripts/browser-smoke.mjs
  -> Node 标准库启动临时 HTTP server
  -> 启动 Chrome/Chromium headless
  -> 通过 CDP 读取页面状态
  -> 页面以浏览器 ESM 方式导入 dist/index.js 并执行最小断言
```

修复点：

```text
npm pack 必须在 SDK 包目录执行。
之前使用 npm --prefix <sdk> pack 时，npm 会尝试读取仓库根目录 package.json，导致 pack smoke 失败。
脚本已改为 cd 到 SDK_DIR 后执行 npm pack --pack-destination。
```

验证：

```bash
npm --prefix clients/web/yuocore-client-ts run smoke
npm --prefix clients/web/yuocore-client-ts run browser-smoke
scripts/run-web-sdk-pack-smoke.sh
```

结果：

```text
Web SDK smoke: yuocore-client-ts smoke passed
Web SDK browser smoke: yuocore-client-ts browser smoke passed
Web SDK pack smoke: Passed
输出：artifacts/packages/web-sdk-pack-smoke-20260608T214233Z
tarball：yuocore-client-0.1.0.tgz
consumer：artifacts/packages/web-sdk-pack-smoke-20260608T214233Z/consumer
```

边界：

```text
这证明当前 Web SDK 的 npm 包结构、exports、类型声明和 ESM 导入可用。
浏览器 smoke 额外证明 dist/index.js 可以被真实 Chrome/Chromium 作为浏览器 ESM 模块导入。
它不解决 TypeScript protobuf codegen 选型，也不保证未来协议字段不会漂移。
正式协议生成策略仍按风险文档等待裁定。
```

## 2026-06-09：客户端 SDK 聚合 smoke

本轮新增 `scripts/run-client-sdk-smoke.sh`，把客户端侧常用验证收成一个入口：

```text
YuoCore.Client.Tests
YuoCore.Client.StateSync.Tests
clients/unity/YuoCore.UnityClient build
clients/web/yuocore-client-ts smoke
clients/web/yuocore-client-ts browser-smoke
scripts/run-web-sdk-pack-smoke.sh
```

语义：

```text
该脚本不启动 Backend。
它验证 Web/Unity 客户端 SDK 和适配层能在自己的边界内构建、运行 smoke，并证明 Web npm tarball 可被全新 TypeScript consumer 安装。
Unity 侧当前验证到 netstandard2.1 adapter build，不等价于 Unity Editor 导入测试。
```

验证：

```bash
scripts/run-client-sdk-smoke.sh
```

结果：

```text
YuoCore.Client.Tests: Passed 8
YuoCore.Client.StateSync.Tests: Passed 15
Unity adapter build: succeeded, 0 warnings, 0 errors
Web SDK smoke: yuocore-client-ts smoke passed
Web SDK browser-smoke: yuocore-client-ts browser smoke passed
Web SDK pack smoke: Passed
输出：artifacts/packages/web-sdk-pack-smoke-20260608T215313Z
client-sdk-smoke=passed
```

补充最新聚合验证：

```bash
scripts/run-client-sdk-smoke.sh
```

结果：

```text
YuoCore.Client.Tests: Passed 9
YuoCore.Client.StateSync.Tests: Passed 15
Unity adapter build: succeeded, 0 warnings, 0 errors
Web SDK smoke: yuocore-client-ts smoke passed
Web SDK browser smoke: yuocore-client-ts browser smoke passed
Web SDK npm tarball consumer smoke: passed
输出：artifacts/packages/web-sdk-pack-smoke-20260608T232634Z
tarball：yuocore-client-0.1.0.tgz
client-sdk-smoke=passed
```

## 2026-06-09：Web SDK 真实 Backend WebSocket 浏览器 smoke

本轮新增真实 Backend 联调门禁：

```text
clients/web/yuocore-client-ts
  -> YuoWebSocketTransport.close()
  -> YuoMmoWebSocketSession
  -> connect / reconnect / disconnect

scripts/run-web-backend-browser-smoke.sh
  -> 构建 Web TypeScript SDK
  -> 构建 YuoCore.Backend
  -> 使用随机本地端口启动真实 ASP.NET Core Backend
  -> 禁用 PostgreSQL / Redis / durable outbox
  -> 启动临时浏览器测试页面
  -> Chrome/Chromium headless 导入 dist/index.js
  -> 通过 YuoWebSocketTransport.connect 连接真实 /ws
```

浏览器内执行的最小闭环：

```text
YuoMmoClient.login
YuoMmoClient.enterMap
YuoMmoClient.move
YuoMmoClient.requestStateSnapshot
YuoMmoClient.disconnect
YuoWebSocketTransport.close
YuoMmoClient.login(existing playerId)
YuoMmoClient.getSnapshot
```

验证：

```bash
scripts/run-web-backend-browser-smoke.sh
```

结果：

```text
Web SDK smoke: Passed
Web SDK pack smoke: Passed，tarball consumer 已导入 YuoMmoWebSocketSession 类型
Backend build: succeeded, 0 warnings, 0 errors
Browser WebSocket loop: yuocore web backend browser smoke passed
web-backend-browser-smoke=passed
```

边界：

```text
这证明 Web SDK 在真实浏览器环境中可以连接真实 Backend /ws，并完成最小 MMO 请求闭环。
它同时覆盖手动重连恢复：断线保存后，新的 WebSocket 用同一个 playerId 登录，并验证 mapId、坐标和 lastInputSeq 恢复。
它不是完整前端应用测试，不覆盖 UI 渲染、自动重连状态机、浏览器兼容矩阵或长时间 push 稳定性。
Backend 启动时仍使用示例配置，不启用 PostgreSQL、Redis 或 durable outbox。
```

## 2026-06-09：C# / Unity 侧 MMO 客户端连接会话

本轮补齐 C# 客户端侧与 Web `YuoMmoWebSocketSession` 对称的显式会话管理器：

```text
YuoCore.Client
  -> 新增 YuoMmoClientConnection
  -> ConnectAsync
  -> ReconnectAsync
  -> DisconnectAsync
  -> CloseAsync
```

职责：

```text
管理当前 IYuoClientTransport、YuoMmoClient 和 playerId。
不引用 Unity WebSocket、TCP、KCP、Gateway、Actor、ECS 或 Backend。
transport 由外部工厂创建，关闭时可使用可选 close 回调；没有回调时退回 IAsyncDisposable / IDisposable。
```

验证：

```bash
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
scripts/run-pack-consumer-smoke.sh
```

结果：

```text
YuoCore.Client.Tests: Passed 9
Unity adapter build: succeeded, 0 warnings, 0 errors
NuGet consumer smoke: passed
输出：artifacts/packages/consumer-smoke-20260608T221843Z
```

本轮还修正了 `scripts/run-pack-consumer-smoke.sh`：

```text
每次 consumer smoke 默认使用 <output>/.nuget-packages 作为隔离 NuGet global packages folder。
否则相同 0.1.0 版本的旧本地包可能从全局 cache 命中，导致 consumer smoke 没有验证刚生成的新 nupkg。
```

## 2026-06-09：当前阶段全局 build / test 门禁

本轮在 Web/C# 客户端连接会话、Web Backend browser smoke、NuGet consumer smoke 调整后，重新运行完整 solution 门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-web-backend-browser-smoke.sh
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: all listed test projects passed
Backend.Tests: Passed 53
StateSync.Tests: Passed 108
Actor.Tests: Passed 50
Client.Tests: Passed 9
Client.StateSync.Tests: Passed 15
Analyzers.Tests: Passed 31
Architecture.Tests: Passed 15
```

边界：

```text
本次 solution test 不启用 RUN_TESTCONTAINERS=1。
PostgreSQL / Redis / durable outbox 的真实容器组合仍由 Backend.Tests 的 Testcontainers 专项命令覆盖。
```

补充 Testcontainers 验证：

```bash
RUN_TESTCONTAINERS=1 dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~PostgresRepositoryTests --disable-build-servers
```

结果：

```text
Backend Testcontainers focused tests: Passed 3
覆盖：PostgreSQL repository、Redis cache、SnapshotPersistenceService、durable outbox replay。
```

补充 1w quick 压测：

```bash
scripts/run-local-load-suite.sh --quick
```

产物：

```text
artifacts/load/20260608T223144Z-quick/summary.md
```

结果：

```text
aoi-streams: passed, tick p95=180.754ms, allocated=166.397MB, hotspot=drain p95=131.744ms
ecs-shape: passed, tick p95=152.381ms, allocated=328.227MB, hotspot=tick p95=152.381ms
local-composite-count: passed, tick p95=131.511ms, allocated=30.854MB, hotspot=actor-enqueue p95=51.009ms
```

判断：

```text
默认 quick 三项均通过当前阈值。
主要热点仍是既有的 AOI drain、100组件/100system ECS tick 和 actor enqueue。
本轮没有出现需要立即冒险改写热路径的证据；长期商业级判断仍需看 soak、p99、GC pause 和目标硬件基线。
```

补充 30 秒 soak 压测：

```bash
YUO_LOAD_DURATION_SECONDS=30 scripts/run-local-load-suite.sh --soak
```

产物：

```text
artifacts/load/20260608T223434Z-soak/summary.md
```

结果：

```text
aoi-streams: passed, ticks=543, tick p95=77.480ms, max=200.627ms, allocated=17,712.746MB, hotspot=drain p95=60.977ms
ecs-shape: passed, ticks=321, tick p95=105.655ms, max=157.399ms, allocated=330.809MB, hotspot=tick p95=105.655ms
local-composite-count: passed, ticks=1,428, tick p95=34.254ms, max=125.715ms, allocated=8,655.754MB, hotspot=actor-enqueue p95=17.622ms
```

判断：

```text
30 秒 soak 三项均通过当前阈值。
aoi-streams 的持续分配量最高，30 秒约 17.7GB，heap-after 约 880MB。
local-composite 的持续分配约 8.65GB，GC0/GC1/GC2 仍偏高。
当前没有 p95 阈值失败，但商业级长稳仍需要继续拆 payload 构建、push frame 复用、AOI delta 编码和 replay history 分配。
```

## 2026-06-09：AOI Stream 推送链路诊断指标

本轮没有直接改 AOI/StateSync 的业务语义，而是先给 `GameAoiStreamPushCoordinator` 增加只读诊断快照：

```text
GameAoiStreamPushDiagnostics
  -> DirtyEntriesConsumed
  -> FullSnapshotPasses / DirtySnapshotPasses / SnapshotsBuilt
  -> FramesPublished / DeltasPublished / PayloadBytesPublished
  -> SessionDrainCalls / SameFrameSkips
  -> SessionPushesCreated / SessionDeltaPushesCreated / SessionSubscriptionPushesCreated
  -> CachedFrameCount / DirtyStreamCount / SessionFrameTrackCount
```

职责边界：

```text
诊断只记录计数，不参与推送判定。
Coordinator 仍然不持有 WebSocket，不直接访问 ECS Entity。
LoadTests 只读取诊断快照并输出 JSON/控制台指标。
```

同时 `aoi-streams` 压测新增阶段分配观测：

```text
MoveAllocatedMb
DrainAllocatedMb
Diagnostics
```

验证：

```bash
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release.json
```

结果：

```text
LoadTests build: succeeded, 0 warnings, 0 errors
Backend AOI focused tests: Passed 13

aoi-streams release diagnostics:
players=10,000 observers=10,000 ticks=20 shards=4
tick p95=549.410ms
move p95=31.167ms
coordinator-drain p95=518.242ms
coordinator-first-session p95=69.031ms
coordinator-remaining-sessions p95=476.866ms

allocated=9,875.92MB
move-allocated=115.52MB
drain-allocated=9,760.40MB

dirty-entries=200,000
full-snapshot-passes=1
dirty-snapshot-passes=20
snapshots-built=2,155
frames-published=21
deltas-published=2,155
payload=24.12MB
session-drains=210,000
session-pushes=1,612,350
session-deltas=1,595,100
session-subscriptions=17,250
```

判断：

```text
这次诊断明确把 AOI Stream 的主要分配压力定位到 session drain 阶段。
共享 frame 发布只产生 2,155 个 delta、约 24.12MB payload，不是 9GB 级分配的主因。
1w observer 下每 tick 要为大量 session 做可见 stream 过滤、订阅变化判断和 YuoGatewayOutboundMessage 列表构造，这部分占据绝大多数分配。
下一步如果继续优化，应优先分析 CreateAoiStreamSessionPushes 内的 activeStreamIds/HashSet/visibleItems/List/StateSubscriptionMessage 分配，而不是先改 ECS tick 或 AOI delta 聚合器。
```

随后做了一个低风险热路径优化：

```text
GameStateSyncPushDispatcher.CreateAoiStreamSessionPushes
  -> 过滤模式下不再额外生成 visibleItems.ToArray()
  -> activeStreamIds 已继承 frame.ActiveStreamIds 的排序，不再二次 Order()
  -> push list 容量按可见 stream 数预估，而不是按全 frame item 数过度预分配
```

语义边界：

```text
不改变 AOI 可见性策略。
不改变 StateSubscriptionMessage 触发条件。
不改变 replay history。
不改变 YuoGatewayOutboundMessage payload。
```

复测：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-list-capacity.json
```

对比 `artifacts/load/manual-aoi-diagnostics-release.json`：

```text
tick p95: 549.410ms -> 528.171ms，约 -3.87%
coordinator-drain p95: 518.242ms -> 493.390ms，约 -4.80%
coordinator-remaining-sessions p95: 476.866ms -> 444.318ms，约 -6.83%
allocated: 9,875.916MB -> 9,765.135MB，约 -1.12%
drain-allocated: 9,760.401MB -> 9,649.717MB，约 -1.13%
GC0: 621 -> 614，约 -1.13%
```

判断：

```text
该优化是低风险正收益，但不是质变。
它证明 session push 热路径确实存在可消除的中间分配，但剩余 9.6GB drain 分配仍然来自更核心的每 session 包装对象、HashSet、订阅消息和列表构造。
下一轮如果继续优化，应优先做更细的 allocator 归因，或设计“同可见区域 session 分组 / push payload 共享包装 / 订阅变化低频化”的 API 级优化。
```

继续做了第二个低风险优化：

```text
CreateAoiStreamSessionPushes
  -> 默认可见 AOI stream 数较少时，不再为每个 session 创建 HashSet
  -> activeStreamIds.Count <= 16 时使用线性查找
  -> 大集合仍保留 HashSet 路径，避免恢复半径扩大后退化成大规模线性扫描
```

复测：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-linear-lookup.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-linear-lookup-2.json
```

第二轮 `linear-lookup` 对比最初诊断基线：

```text
tick p95: 549.410ms -> 524.736ms，约 -4.49%
coordinator-drain p95: 518.242ms -> 496.830ms，约 -4.13%
coordinator-remaining-sessions p95: 476.866ms -> 453.948ms，约 -4.81%
allocated: 9,875.916MB -> 9,709.410MB，约 -1.69%
drain-allocated: 9,760.401MB -> 9,593.910MB，约 -1.71%
GC0: 621 -> 610，约 -1.77%
```

判断：

```text
小集合线性查找进一步降低了 session drain 分配。
单次 p95 有波动，但两次 Release 复测均没有退回基线，且分配稳定下降。
这仍不是质变；剩余分配量说明后续需要从 API/批量广播/复用缓冲角度处理，而不是继续抠单个 LINQ 中间集合。
```

第三个低风险优化落在 `YuoStateSyncSubscriptionTable.SetSubscriberStreams`：

```text
原实现：
  -> 每次 SetSubscriberStreams 都 current.ToArray()
  -> next/current 通过多段 LINQ Contains 计算 added/removed
  -> 即使订阅集合完全不变，也会分配 currentStreams、removed、added 等数组

新实现：
  -> 保留原有 public API 和 ACK 语义
  -> 先检查 current 与 next 是否完全一致
  -> 无变化时直接返回共享 NoChange
  -> 有变化时只为实际 added/removed 分配结果数组
```

语义边界：

```text
不改变 Subscribe / Unsubscribe / Acknowledge / GetStreams 的行为。
不改变 removed stream 会清 ACK 的语义。
不引入 Backend、Gateway、WebSocket 或 ECS 依赖，优化仍停留在 StateSync.Abstractions 内部。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-subscription-fastpath.json
```

结果：

```text
StateSync focused tests: Passed 109
Backend AOI focused tests: Passed 13
Architecture tests: Passed 15
LoadTests Release build: succeeded
```

对比最初诊断基线：

```text
tick p95: 549.410ms -> 515.589ms，约 -6.16%
coordinator-drain p95: 518.242ms -> 487.964ms，约 -5.84%
coordinator-remaining-sessions p95: 476.866ms -> 454.096ms，约 -4.77%
allocated: 9,875.916MB -> 9,636.492MB，约 -2.42%
drain-allocated: 9,760.401MB -> 9,521.013MB，约 -2.45%
GC0: 621 -> 606，约 -2.42%
```

判断：

```text
订阅表无变化快路径是安全正收益，说明 session drain 中大量 SetSubscriberStreams 调用确实是稳定重复订阅。
但优化后 20 tick 仍有约 9.5GB drain 分配，剩余热点已经更偏向每 session 输出消息与协议对象构造。
继续优化需要进入 Gateway push API 和批量广播模型，而不是只优化订阅表。
```

第四个优化是本轮最关键的 AOI drain 优化：

```text
GameAoiStreamPushFrame
  -> 新增 ActiveStreams
  -> frame 发布时把 ActiveStreamIds 预解析成 GameAoiStreamPushActiveStream(StreamId, Cell)

GameStateSyncPushDispatcher
  -> CreateAoiStreamSessionPushes 不再对每个 session、每个 stream 反复 TryParseStreamCell
  -> 可见性策略仍然收到 GameAoiVisibilityContext
  -> 自定义 IGameAoiVisibilityPolicy 仍然可用
```

原热点：

```text
1w observer 场景下，frame.ActiveStreamIds 会被每个 session 扫描。
旧实现每次 CanObserveAoiStream 都会拆 streamId 字符串、Split、Substring、int.Parse。
这些操作发生在 session drain 热路径，导致大量短生命周期对象和 CPU 成本。
```

新边界：

```text
streamId 字符串结构只在 frame 发布时解析一次。
session drain 只复用 frame.ActiveStreams 中的 YuoAoiStreamCell。
不改变 StateDelta payload。
不改变 streamId。
不改变 subscription / ack / recover 语义。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-frame-active-streams.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-frame-active-streams-2.json
```

第二轮 `frame-active-streams` 对比最初诊断基线：

```text
tick p95: 549.410ms -> 234.178ms，约 -57.38%
coordinator-drain p95: 518.242ms -> 209.637ms，约 -59.55%
coordinator-remaining-sessions p95: 476.866ms -> 154.754ms，约 -67.55%
allocated: 9,875.916MB -> 2,382.550MB，约 -75.88%
drain-allocated: 9,760.401MB -> 2,266.740MB，约 -76.78%
GC0: 621 -> 150，约 -75.85%
```

判断：

```text
这是当前 AOI drain 的质变优化。
主要原因不是减少了推送消息数量，而是消除了 session x active stream 的重复字符串解析。
优化后 20 tick 仍有约 2.27GB drain 分配，剩余大头更可能是每个 push 的 YuoGatewayOutboundMessage、List、StateSubscriptionMessage 和 protobuf ToByteArray。
下一步继续优化应转向 Gateway push 包装复用、同 payload 多 session 批量发送、订阅变化事件化。
```

补充优化后 30 秒 soak：

```bash
YUO_LOAD_DURATION_SECONDS=30 scripts/run-local-load-suite.sh --soak
```

产物：

```text
artifacts/load/20260608T230505Z-soak/summary.md
```

结果：

```text
aoi-streams:
  passed
  ticks=673
  tick p95=64.798ms
  max=172.819ms
  allocated=15,557.218MB
  hotspot=drain p95=48.987ms
  move-allocated=3,841.588MB
  drain-allocated=11,715.436MB
  payload=784.911MB

local-composite-count:
  passed
  ticks=1,545
  tick p95=32.347ms
  max=126.541ms
  allocated=9,364.147MB
  hotspot=actor-enqueue p95=16.076ms

ecs-shape:
  passed
  ticks=366
  tick p95=92.309ms
  max=157.151ms
  allocated=331.688MB
  hotspot=tick p95=92.309ms
```

对比旧 30 秒 soak `artifacts/load/20260608T223434Z-soak`：

```text
aoi-streams:
  ticks: 543 -> 673，约 +23.94%
  tick p95: 77.480ms -> 64.798ms，约 -16.37%
  drain p95: 60.977ms -> 48.987ms，约 -19.66%
  allocated: 17,712.746MB -> 15,557.218MB，约 -12.17%
  GC0: 1121 -> 986，约 -12.04%

local-composite-count:
  ticks: 1,428 -> 1,545，约 +8.19%
  tick p95: 34.254ms -> 32.347ms，约 -5.57%
  allocated: 8,655.754MB -> 9,364.147MB，约 +8.18%

ecs-shape:
  ticks: 321 -> 366，约 +14.02%
  tick p95: 105.655ms -> 92.309ms，约 -12.63%
  allocated 基本持平。
```

判断：

```text
30 秒 soak 证明 AOI frame active stream 预解析优化在持续负载中也有效。
aoi-streams 的 p95 和吞吐都有明显改善，但 duration 模式分配仍高。
当前 aoi-streams 每 30 秒仍分配约 15.6GB，其中 move 阶段约 3.84GB，drain 阶段约 11.72GB，delta payload 本身约 784.91MB。
下一轮应继续拆 drain 内每个 YuoGatewayOutboundMessage、StateSubscriptionMessage、List 和 protobuf 序列化分配，同时单独分析 MovePlayers / TryEnqueueMove 的命令分配。
```

补充 Actor/Fiber 进程内投递快路径验证：

```text
YuoFiber.PostAsync
  -> 去掉 async 包装，直接返回 ChannelWriter.WriteAsync

InMemoryActorTransport.SendAsync / InvokeAsync
  -> 去掉 async 包装，直接返回 endpoint 的 ValueTask
```

语义：

```text
不改变 mailbox 串行执行。
不改变 ActorRouter 路由、Location、stale route 重试。
不改变 YuoFiberActorEndpoint 的投递与执行边界。
```

验证：

```bash
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-async-fastpath.json
```

对比 `frame-active-streams-2`：

```text
move p95: 32.285ms -> 29.409ms，约 -8.91%
move-allocated: 115.807MB -> 116.085MB，基本持平
allocated: 2,382.546MB -> 2,391.686MB，基本持平
drain p95: 209.637ms -> 246.716ms，本轮波动更差
```

判断：

```text
这个快路径不是当前 AOI 分配主因。
它保留为基础设施层的低风险同步完成快路径，但不能把它计为主要性能优化。
TryEnqueueMove 的分配大头仍来自每条 Actor message/envelope 和移动事件/dirty event 构造。
```

补充 AOI 可见性判定热路径优化：

```text
GameAoiVisibilityContext
  -> sealed record class 改为 readonly record struct
  -> 避免 session x active stream 判定时为上下文创建堆对象

DefaultGameAoiVisibilityPolicy
  -> 不再为每次 CanRecoverStream 创建 YuoGridAoiStreamResolver
  -> 直接按 CellSize 计算 observer 当前 cell

GameAoiStreamPushCoordinator
  -> 复用 coordinator 级 YuoGridAoiStreamResolver
  -> 避免每个 session drain 都创建 dirty stream resolver
```

语义边界：

```text
不改变 AOI streamId。
不改变可见性半径、地图判断、订阅变化、replay history 或 StateDelta payload。
优化只发生在 Backend 示例 AOI/Gateway 推送热路径，不把 WebSocket、ASP.NET 或数据库依赖带入核心模块。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-visibility-context-struct.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-visibility-context-struct-2.json
```

结果：

```text
Backend AOI focused tests: Passed 13
Architecture tests: Passed 15
LoadTests Release build: succeeded

第二轮 aoi-streams:
  tick p95=216.607ms
  coordinator-drain p95=190.889ms
  coordinator-remaining-sessions p95=120.358ms
  allocated=825.39MB
  move-allocated=116.82MB
  drain-allocated=708.57MB
  GC0=54
```

对比 `artifacts/load/manual-aoi-diagnostics-release-after-async-fastpath.json`：

```text
allocated: 2,391.686MB -> 825.39MB，约 -65.49%
drain-allocated: 约 2,275.60MB -> 708.57MB，约 -68.86%
GC0: 约 151 -> 54，约 -64.24%
move-allocated 基本持平，说明收益集中在 AOI drain。
```

判断：

```text
这是继 ActiveStreams 预解析之后的第二个 AOI drain 质变优化。
主要原因是移除了每次可见性判定的 context class 和 resolver 对象分配。
短压测下 drain 分配已从最初约 9.76GB 降到约 0.71GB；剩余主要风险更偏向每个 push 的 YuoGatewayOutboundMessage、List、protobuf ToByteArray 和状态 payload 本身。
```

补充可见 stream 列表小优化：

```text
ResolveObservableAoiStreamIds
  -> 返回 IReadOnlyList<string>
  -> 不再对每个 session 把 List<string> 复制成 string[]
  -> 没有可见 stream 时不创建 List，直接返回空集合
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~Aoi --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-observable-list-no-array.json
```

结果：

```text
Backend AOI focused tests: Passed 13
LoadTests Release build: succeeded

aoi-streams:
  tick p95=233.645ms
  coordinator-drain p95=206.398ms
  allocated=821.84MB
  move-allocated=116.77MB
  drain-allocated=705.07MB
  GC0=53
```

判断：

```text
该优化是小收益，不是质变。
它确认可见 stream 列表的 ToArray 属于可清理分配，但剩余主要压力仍在每个 push 的包装对象、订阅消息和 payload 序列化。
```

补充 Gateway outbound message 包装优化：

```text
YuoGatewayOutboundMessage
  -> sealed record class 改为 readonly record struct

原因：
AOI Stream push 会为每个 session / delta 构造 outbound message 包装。
Payload byte[] 和 protobuf message 仍然是引用对象，但包装本身不需要引用身份。
改为值类型后，List<YuoGatewayOutboundMessage> 和 Channel<YuoGatewayOutboundMessage> 存储的是消息头值，减少每个 push 的独立对象分配。
```

语义边界：

```text
不改变 Version / OpCode / RequestId / ServerSeq / Payload / Error 字段。
不改变 WebSocket envelope 编码。
不改变 Gateway dispatcher 的 request/response 语义。
这是 0.x 阶段的公共 API 形状变化；如果未来进入 1.0，应避免再随意改变 Gateway 消息类型的值/引用语义。
```

验证：

```bash
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Aoi|FullyQualifiedName~WebSocket|FullyQualifiedName~Gateway" --disable-build-servers
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-outbound-struct.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-outbound-struct-2.json
```

结果：

```text
Gateway tests: Passed 11
Backend focused tests: Passed 36
Client tests: Passed 9
LoadTests Release build: succeeded

第二轮 aoi-streams:
  tick p95=231.952ms
  coordinator-drain p95=208.184ms
  allocated=787.19MB
  move-allocated=116.88MB
  drain-allocated=670.31MB
  GC0=51
```

对比 `after-observable-list-no-array`：

```text
allocated: 821.84MB -> 787.19MB，约 -4.22%
drain-allocated: 705.07MB -> 670.31MB，约 -4.93%
GC0: 53 -> 51，约 -3.77%
```

判断：

```text
这是稳定正收益，但不是质变。
它清掉了一部分每 push 包装对象分配；剩余主要分配仍来自 StateSubscriptionMessage、protobuf ToByteArray、session push List 和状态 payload。
```

补充已回退的 session push 精确数组尝试：

```text
尝试内容：
  CreateAoiStreamSessionPushes 先统计可见 delta 数量，再创建精确长度数组返回。

期望：
  去掉 List<YuoGatewayOutboundMessage> 对象和容量余量。

结果：
  allocated=783.97MB
  drain-allocated=668.53MB
  coordinator-drain p95=237.738ms

对比 outbound struct 第二轮：
  allocated=787.19MB
  drain-allocated=670.31MB
  coordinator-drain p95=208.184ms

裁定：
  分配只小幅下降，但多扫一遍 frame items 让 drain p95 明显变差。
  该方案已回退，不保留。
```

后续判断：

```text
session push List 不是不能优化，但不能用“先数再填”的方式处理。
如果要继续减少这部分分配，应改为 Gateway push API 支持调用方缓冲、批量发送或同 payload 多 session 共享，而不是在当前 IReadOnlyList 返回模型里增加额外扫描。
```

补充调用方缓冲 drain 优化：

```text
GameStateSyncPushDispatcher
  -> 新增 CreateAoiStreamSessionPushes(..., List<YuoGatewayOutboundMessage> destination, ...)

GameAoiStreamPushCoordinator
  -> 新增 DrainPushes(YuoGatewaySession, List<YuoGatewayOutboundMessage> destination)

GameWebSocketPushPump
  -> producer 内部复用一个 List<YuoGatewayOutboundMessage>
  -> 每轮 drain 前 Clear，drain 后写入 channel

tools/YuoCore.LoadTests
  -> aoi-streams 使用同一个 pushBuffer 复用计数
```

语义边界：

```text
旧 IReadOnlyList 返回 API 仍保留，现有测试和外部调用方不需要立刻迁移。
新路径只在 Backend 示例和压测里复用调用方缓冲。
YuoGatewayOutboundMessage 已经是 readonly record struct，写入 Channel 后是值复制，下一轮 Clear 不影响 consumer。
不改变 StateDelta / StateSubscription payload，不改变 WebSocket 编码，不改变订阅语义。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Aoi|FullyQualifiedName~WebSocket|FullyQualifiedName~Gateway" --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-drain-buffer.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-drain-buffer-2.json
```

结果：

```text
Backend focused tests: Passed 36
LoadTests Release build: succeeded

第二轮 aoi-streams:
  tick p95=218.023ms
  coordinator-drain p95=181.296ms
  coordinator-remaining-sessions p95=106.514ms
  allocated=705.81MB
  move-allocated=116.85MB
  drain-allocated=588.96MB
  GC0=46
```

对比 outbound struct 第二轮：

```text
allocated: 787.19MB -> 705.81MB，约 -10.34%
drain-allocated: 670.31MB -> 588.96MB，约 -12.14%
GC0: 51 -> 46，约 -9.80%
coordinator-drain p95: 208.184ms -> 181.296ms，约 -12.92%
```

判断：

```text
这是稳定正收益。
和“先数再填精确数组”不同，调用方缓冲没有额外扫描 frame items，直接减少每次 drain 的 List 分配。
该方向可以继续扩展到更正式的 Gateway push sink / batch API，但当前只在 Backend 示例层落地，避免污染核心 Gateway 包。
```

补充 AOI dirty DTO 值类型化：

```text
YuoAoiDirtyPlayerDto
  -> sealed record class 改为 readonly record struct

原因：
MoveActorHandler 每次接受移动都会 EnqueueAoiDirty。
1w 玩家 / 20 tick 会产生 200,000 条 dirty 记录。
这些 dirty 记录只作为数据载体进入 ConcurrentQueue，然后被 AOI coordinator drain，不需要引用身份。
```

语义边界：

```text
不改变字段内容。
不改变 dirty drain 顺序。
不改变 AOI stream resolver、snapshot、delta 或 WebSocket payload。
这是 YuoCore.Game 0.x 阶段的公共 DTO 形状变化；进入 1.0 前需要稳定。
```

验证：

```bash
dotnet test tests/YuoCore.Game.Tests/YuoCore.Game.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Aoi|FullyQualifiedName~Move|FullyQualifiedName~WebSocket" --disable-build-servers
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-dirty-struct.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-dirty-struct-2.json
```

结果：

```text
Game tests: Passed 2
Backend focused tests: Passed 24
StateSync tests: Passed 109
LoadTests Release build: succeeded

第二轮 aoi-streams:
  tick p95=218.662ms
  move p95=27.139ms
  coordinator-drain p95=191.566ms
  allocated=703.42MB
  move-allocated=102.05MB
  drain-allocated=601.36MB
  GC0=46
```

对比 drain-buffer 第二轮：

```text
move-allocated: 116.85MB -> 102.05MB，约 -12.67%
allocated: 705.81MB -> 703.42MB，约 -0.34%
drain-allocated 本轮略高，属于 drain 阶段波动；收益主要集中在 move 阶段。
```

判断：

```text
该优化对总分配是小收益，但明确降低了 move 阶段 dirty event 对象分配。
后续 move 阶段剩余主要分配仍来自 ActorEnvelope、Actor message 接口装箱/对象、mailbox item 和 handler 调度路径。
这些属于 Actor API 层优化，不能直接用绕过 Actor 的方式处理，否则会破坏线程所有权设计。
```

## 2026-06-09：StateSync 订阅表 no-change 快路径

AOI session drain 每次都会把当前可见 stream 集合写入 `YuoStateSyncSubscriptionTable.SetSubscriberStreams`。
旧实现为了保持通用 API 语义，每次都会执行：

```text
IEnumerable<string>
  -> Select(Normalize)
  -> Distinct
  -> ToArray
  -> 再判断是否和现有订阅集合一致
```

在 1w observer / 20 tick AOI 压测里，`SessionDrainCalls=210,000`，其中绝大多数 session 的可见 stream 集合并没有变化。
因此旧路径会为大量 no-change 调用创建临时数组和 LINQ 枚举对象。

本轮改动：

```text
YuoStateSyncSubscriptionTable.SetSubscriberStreams
  -> 如果 streamIds 是 IReadOnlyList<string>
  -> 先在锁内做无分配 no-change 判断
  -> 只有发现集合变化、重复项、空白项或首尾未 trim 时，才回落到原来的 Normalize + Distinct + ToArray 通用路径
```

语义边界：

```text
不改变 public API。
不跳过 Subscribe/Unsubscribe/ACK 清理语义。
重复输入仍然保持原来的 Distinct 语义。
未规范化输入仍然由原路径 Normalize。
该优化只让“已经规范化且没有变化”的 IReadOnlyList 输入少分配。
```

新增测试：

```text
Set_subscriber_streams_duplicate_list_falls_back_to_distinct_semantics
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Aoi|FullyQualifiedName~Move|FullyQualifiedName~WebSocket" --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-subscription-nochange-fastpath.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-subscription-nochange-fastpath-2.json
```

结果：

```text
StateSync tests: Passed 110
Backend focused tests: Passed 24
LoadTests Release build: succeeded

第二轮 aoi-streams:
  tick p95=225.995ms
  move p95=36.088ms
  coordinator-drain p95=199.099ms
  allocated=554.45MB
  move-allocated=101.82MB
  drain-allocated=452.62MB
  GC0=35
```

对比 dirty DTO 值类型化后的第二轮：

```text
allocated: 703.42MB -> 554.45MB，约 -21.18%
drain-allocated: 601.36MB -> 452.62MB，约 -24.73%
GC0: 46 -> 35
coordinator-drain p95: 191.566ms -> 199.099ms，本轮延迟略有波动，未显示稳定延迟收益。
```

判断：

```text
这是明确的分配收益，且没有改变 Gateway、Actor、ECS 或 StateSync 对外语义。
本次优化说明 AOI drain 剩余分配里有相当一部分来自订阅表的通用规范化路径。
延迟没有同等幅度下降，说明后续主延迟仍在每 session 可见性判断、payload push 构造和批量发送模型。
```

## 2026-06-09：AOI 可见 stream 临时 List 复用

订阅表 no-change 快路径后，`CreateAoiStreamSessionPushes` 外层仍然会在每个 session drain 时为可见 AOI stream 创建一个 `List<string>`。
在 1w observer / 20 tick 场景下，`SessionDrainCalls=210,000`，这会形成稳定的短生命周期 List 和数组分配。

本轮改动：

```text
GameStateSyncPushDispatcher.CreateAoiStreamSessionPushes
  -> 增加可选 observableStreamBuffer 参数
  -> 过滤 AOI 可见 stream 时可以写入调用方提供的 List<string>

GameAoiStreamPushCoordinator
  -> 在 coordinator 锁内持有一个 _observableStreamBuffer
  -> 每次 session drain 复用该 buffer
```

语义边界：

```text
不改变可见性策略。
不改变订阅表的 set 语义。
不改变 Gateway outbound message 形状。
buffer 只在 GameAoiStreamPushCoordinator 的 _gate 锁内复用，不跨线程暴露。
这是 Backend 示例宿主热路径优化，不把额外依赖带进 Ecs/Runtime/Actor/StateSync.Abstractions。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Aoi|FullyQualifiedName~Move|FullyQualifiedName~WebSocket" --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-observable-buffer.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-observable-buffer-2.json
```

结果：

```text
StateSync tests: Passed 110
Backend focused tests: Passed 24
LoadTests Release build: succeeded

第二轮 aoi-streams:
  tick p95=214.050ms
  move p95=40.853ms
  coordinator-drain p95=180.044ms
  allocated=519.75MB
  move-allocated=102.10MB
  drain-allocated=417.64MB
  GC0=34
```

对比订阅表 no-change 快路径第二轮：

```text
allocated: 554.45MB -> 519.75MB，约 -6.26%
drain-allocated: 452.62MB -> 417.64MB，约 -7.73%
GC0: 35 -> 34
coordinator-drain p95: 199.099ms -> 180.044ms，本轮有延迟收益，但仍需后续复测确认稳定性。
```

判断：

```text
这是安全的局部优化，收益符合预期。
当前 AOI drain 剩余大头继续集中在每 session push 包装、serverSeq 分配、可见性判断和后续 WebSocket 发送模型。
下一轮如果继续优化，应优先评估“同 payload 多 session”的批量发送 API，而不是继续压榨局部 List 分配。
```

## 2026-06-09：AOI 默认可见性策略轻量 observer state 快路径

继续分析 AOI drain 后发现，默认可见性策略只需要玩家所在地图和坐标，却因为旧接口 `IGameAoiVisibilityPolicy` 接收完整 `YuoPlayerSnapshotDto`，导致每次 session drain 都要调用 `GameRuntime.TryGetSnapshot` 并分配一个完整玩家快照 DTO。

在 1w observer / 20 tick 场景下：

```text
SessionDrainCalls=210,000
```

这意味着仅可见性判定入口就会产生约 20 万个短生命周期 `YuoPlayerSnapshotDto`。

本轮改动：

```text
GameAoiObserverState
  -> 只包含 PlayerId / MapId / X / Y / Z

GameAoiVisibilityQueryContext
  -> 使用轻量 observer state 的可见性上下文

IGameAoiVisibilityQueryPolicy
  -> 继承 IGameAoiVisibilityPolicy
  -> 供 AOI drain 热路径选择无快照 DTO 的查询接口

DefaultGameAoiVisibilityPolicy
  -> 同时实现旧接口和轻量接口

GameRuntime.TryGetAoiObserverState
  -> 在 runtime 锁内直接读取 presence + transform
  -> 不构造完整 YuoPlayerSnapshotDto

GameStateSyncPushDispatcher
  -> 如果策略实现 IGameAoiVisibilityQueryPolicy，则走 observer state 快路径
  -> 自定义旧策略仍回落到 TryGetSnapshot + IGameAoiVisibilityPolicy
```

语义边界：

```text
不删除旧 IGameAoiVisibilityPolicy。
不改变自定义旧策略行为。
不改变默认 AOI 判断结果。
不把该快路径下沉到 StateSync.Abstractions；它属于 Backend 示例业务策略层。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Aoi|FullyQualifiedName~Move|FullyQualifiedName~WebSocket" --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-observer-state.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --json-output artifacts/load/manual-aoi-diagnostics-release-after-observer-state-2.json
```

结果：

```text
StateSync tests: Passed 110
Backend focused tests: Passed 24
LoadTests Release build: succeeded

第二轮 aoi-streams:
  tick p95=225.469ms
  move p95=37.869ms
  coordinator-drain p95=198.107ms
  allocated=492.26MB
  move-allocated=101.62MB
  drain-allocated=390.64MB
  GC0=32
```

对比可见 stream List 复用第二轮：

```text
allocated: 519.75MB -> 492.26MB，约 -5.29%
drain-allocated: 417.64MB -> 390.64MB，约 -6.46%
GC0: 34 -> 32
coordinator-drain p95: 180.044ms -> 198.107ms，本轮延迟波动回升，不能记为稳定延迟收益。
```

判断：

```text
这是安全分配优化，符合“默认策略无需完整快照”的职责事实。
自定义复杂策略仍可继续使用完整 YuoPlayerSnapshotDto。
后续如果要继续降低 AOI drain，需要从 push/envelope 批处理或真实 WebSocket 编码路径入手。
```

补充 observer state 快路径后的完整门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 504
Failed: 0
Skipped: 0

Web Backend browser smoke: passed
覆盖真实 ASP.NET Core Backend /ws、Chrome/Chromium WebSocket 客户端、login -> enter map -> move -> snapshot -> disconnect -> reconnect -> get snapshot。
该 smoke 已经走 GameWebSocketClientConnection.SendAsync 的 pooled encode/send 路径。
```

## 2026-06-09：AOI WebSocket envelope 编码压测与热路径优化

之前的 `aoi-streams` 压测只覆盖：

```text
GameAoiStreamPushCoordinator
  -> CreateAoiStreamSessionPushes
  -> YuoGatewayOutboundMessage
```

它没有覆盖真实 WebSocket 发送前的 envelope 编码：

```text
YuoGatewayOutboundMessage
  -> YuoServerEnvelopeV1
  -> ToByteArray()
```

本轮给 `tools/YuoCore.LoadTests` 增加 `--encode-pushes` 开关。
开启后，`aoi-streams` 会对每条 push 调用 `GameWebSocketEndpoint.EncodeServerMessage(push, useEnvelopeV1: true)`，并单独记录：

```text
EncodeMs
EncodePushes
EncodedBytes
EncodeAllocatedMb
```

新增入口：

```bash
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --encode-pushes --json-output artifacts/load/manual-aoi-diagnostics-release-with-websocket-encode.json
```

第一轮真实编码基线：

```text
allocated=52,793.17MB
drain-allocated=52,692.34MB
encode-allocated=52,364.59MB
websocket-encode p95=711.258ms
encoded-bytes=18,164,621,440
GC0=3,314
```

判断：

```text
真实发送路径的分配远大于纯 AOI drain。
原因是旧编码路径存在多次 payload 复制：
  1. message.Payload.ToArray()
  2. ByteString.CopyFrom(...)
  3. YuoServerEnvelopeV1.ToByteArray()
```

第一步优化：

```text
Payload = ByteString.CopyFrom(message.Payload.Span)
```

结果第二轮：

```text
allocated=35,442.02MB
drain-allocated=35,341.33MB
encode-allocated=34,964.15MB
websocket-encode p95=257.386ms
GC0=2,223
```

对比旧真实编码基线：

```text
allocated: 52,793.17MB -> 35,442.02MB，约 -32.87%
encode-allocated: 52,364.59MB -> 34,964.15MB，约 -33.23%
websocket-encode p95: 711.258ms -> 257.386ms
```

第二步优化：

```text
无 Error 的 YuoServerEnvelopeV1 热路径改为手写 protobuf envelope writer。
直接把 payload ReadOnlySpan<byte> 拷贝到最终 byte[]。
Error 路径仍使用生成类型，降低兼容风险。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Envelope|FullyQualifiedName~WebSocket|FullyQualifiedName~Aoi|FullyQualifiedName~Move" --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --encode-pushes --json-output artifacts/load/manual-aoi-diagnostics-release-with-websocket-encode-manual.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --encode-pushes --json-output artifacts/load/manual-aoi-diagnostics-release-with-websocket-encode-manual-2.json
```

结果第二轮：

```text
Backend focused tests: Passed 24
LoadTests Release build: succeeded

allocated=17,861.43MB
drain-allocated=17,760.82MB
encode-allocated=17,352.97MB
websocket-encode p95=139.354ms
encoded-bytes=18,164,522,440
GC0=1,121
```

对比 Span Copy 版本第二轮：

```text
allocated: 35,442.02MB -> 17,861.43MB，约 -49.60%
encode-allocated: 34,964.15MB -> 17,352.97MB，约 -50.37%
websocket-encode p95: 257.386ms -> 139.354ms
GC0: 2,223 -> 1,121
```

对比最初真实编码基线第二轮：

```text
allocated: 52,793.17MB -> 17,861.43MB，约 -66.17%
encode-allocated: 52,364.59MB -> 17,352.97MB，约 -66.86%
websocket-encode p95: 711.258ms -> 139.354ms
GC0: 3,314 -> 1,121
```

剩余边界：

```text
当前 encoded-bytes 约 18.16GB。
手写 writer 后的 encode-allocated 约 17.35GB，已经接近“每个 session 最终要发一份完整 binary frame”的理论下限。
继续降低不能靠减少中间复制，需要设计同 payload 多 session 的批量发送模型、网关广播 sink、压缩或更粗粒度 AOI 分组。
```

第三步优化：

```text
新增 GameWebSocketEndpoint.RentServerMessage。
V1 无 Error 热路径使用 ArrayPool<byte> 租用最终发送缓冲。
GameWebSocketClientConnection.SendAsync 改为租用 encoded buffer，await WebSocket.SendAsync 后归还。
EncodeServerMessage 仍保留 byte[] API，供测试和简单调用使用。
LoadTests 增加 --pooled-encode-pushes，用于模拟生产发送路径的 pooled encode。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter "FullyQualifiedName~Envelope|FullyQualifiedName~WebSocket|FullyQualifiedName~Aoi|FullyQualifiedName~Move" --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --pooled-encode-pushes --json-output artifacts/load/manual-aoi-diagnostics-release-with-websocket-encode-pooled.json
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode aoi-streams --players 10000 --ticks 20 --shards 4 --aoi-observers 10000 --pooled-encode-pushes --json-output artifacts/load/manual-aoi-diagnostics-release-with-websocket-encode-pooled-2.json
```

结果第二轮：

```text
Backend focused tests: Passed 24
LoadTests Release build: succeeded

allocated=499.02MB
drain-allocated=397.91MB
encode-allocated=8.01MB
websocket-encode p95=48.870ms
encoded-bytes=18,164,448,610
GC0=33
```

对比手写 writer 但分配 byte[] 的第二轮：

```text
allocated: 17,861.43MB -> 499.02MB，约 -97.21%
encode-allocated: 17,352.97MB -> 8.01MB，约 -99.95%
websocket-encode p95: 139.354ms -> 48.870ms
GC0: 1,121 -> 33
```

对比不编码的 observer-state 第二轮：

```text
allocated: 492.26MB -> 499.02MB
drain-allocated: 390.64MB -> 397.91MB
GC0: 32 -> 33
```

判断：

```text
pooled encode 后，真实 WebSocket envelope 编码对 GC 分配的影响已经接近消失。
剩余成本主要是 CPU 写入和实际网络字节量，不再是托管堆分配。
后续“同 payload 多 session”的价值会更多体现在降低 CPU/网络带宽/发送调用次数，而不是 GC 分配。
```

补充 pooled encode 后的完整门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 504
Failed: 0
Skipped: 0
```

补充手写 writer 后的完整门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 504
Failed: 0
Skipped: 0
```

补充可见 stream List 复用后的完整门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 504
Failed: 0
Skipped: 0
```

补充订阅表快路径的包消费门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-pack-consumer-smoke.sh
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 504
Failed: 0
Skipped: 0

Consumer smoke:
  packages=27
  passed
  output=artifacts/packages/consumer-smoke-20260609T001841Z
  CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build。
  已覆盖 YuoCore.StateSync.Abstractions 在 netstandard2.1 和 net10.0 包中的消费编译。
```

补充阶段门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 503
Failed: 0
Skipped: 0
```

补充完整门禁和包消费验证：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-pack-smoke.sh
scripts/run-pack-consumer-smoke.sh
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 503
Failed: 0
Skipped: 0

Pack smoke:
  packages=27
  passed
  output=artifacts/packages/pack-smoke-20260608T234210Z

Consumer smoke:
  packages=27
  passed
  output=artifacts/packages/consumer-smoke-20260608T234450Z
  CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build。

补充 Consumer smoke:
  packages=27
  passed
  output=artifacts/packages/consumer-smoke-20260609T000619Z
  CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build。
  本次覆盖 YuoCore.Game 的 readonly record struct dirty DTO 包形状，证明该 0.x 公共 DTO 变更没有破坏包消费编译。
```

验证备注：

```text
本轮曾并行运行 Backend Release build 和 LoadTests Release build。
Backend build 因两个 dotnet build 同时写同一依赖项目 bin/Release deps.json 出现 IO 占用失败。
LoadTests Release build 已成功，后续压测使用该成功构建产物。
该并行构建失败不是代码编译错误；后续完整阶段门禁仍应单独运行 dotnet build YuoCore.sln。
```

补充阶段门禁：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Solution build: succeeded
Warnings: 0
Errors: 0

Solution tests: Passed 503
Failed: 0
Skipped: 0
```

补充全局测试门禁：

```bash
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
All tests passed
Test assemblies: 26
Passed tests: 503
Failed: 0
Skipped: 0
```

补充 NuGet 打包/消费门禁：

```bash
scripts/run-pack-smoke.sh
scripts/run-pack-consumer-smoke.sh
```

结果：

```text
Pack smoke:
  packages=27
  passed
  output=artifacts/packages/pack-smoke-20260608T231615Z
  禁止依赖扫描通过，未发现 Backend / ASP.NET Core / Redis / Npgsql / Dapper / Serilog / OpenTelemetry 泄漏到可发布库。

Consumer smoke:
  packages=27
  passed
  output=artifacts/packages/consumer-smoke-20260608T231923Z
  CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build。
  本次 smoke 使用隔离 NuGet cache，避免命中旧 0.1.0 包。
```

## 2026-06-09：StateSync per-stream Delta Tracker

本轮新增纯 per-stream delta 聚合器：

```text
YuoCore.StateSync.Abstractions
  -> 新增 YuoStateStreamDeltaTracker
  -> BuildDelta(streamId, tick, currentStates)
  -> RemoveStream(streamId)
  -> Clear()
```

语义：

```text
每条 stream 独立维护上一帧 NetworkId -> Version。
当前状态中新出现的实体生成 Enter。
Version 变化的实体生成 Update。
上一帧存在、本帧消失的实体生成 Leave。
BaseTick 按 stream 独立生成，不同 AOI stream 不会互相污染。
```

边界：

```text
Tracker 不知道 AOI 查询、Map、Gateway、WebSocket、Actor 或 ECS。
Map/Room 层需要先把实体投影成 YuoEntityState，并按 streamId 分组，再调用该 tracker。
这一步提供 per-stream delta 生成原语；Backend 示例还没有把它接入 WebSocket 多流下发。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
```

结果：

```text
StateSync focused tests: Passed 95
```

## 2026-06-09：Backend 当前地图玩家按 AOI stream 聚合快照

本轮在 Backend 示例 Runtime 中新增只读 AOI stream snapshot 聚合：

```text
YuoCore.Game
  -> 新增 YuoAoiStreamSnapshotDto

YuoCore.Backend.Game
  -> GameRuntime.BuildAoiStreamSnapshots(mapId, cellSize)
  -> 使用 YuoGridAoiStreamResolver 计算 map/aoi streamId
  -> 按 streamId 分组当前地图玩家
  -> 每组输出 StreamId、MapId、Tick、Players
```

语义：

```text
这是 Map/Room 侧多流下发前的只读聚合能力。
它证明当前 Runtime 能把地图内玩家按 AOI stream 分组，后续可以基于这些组生成 per-stream snapshot。
它不读取或消费现有 per-player event queue，也不替换 GameStateSyncPushDispatcher 当前 player stream 推送。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 39
新增覆盖：Runtime_can_group_current_map_players_by_aoi_stream_snapshot
```

## 2026-06-09：AOI Stream 生成策略

本轮新增纯 AOI stream 解析器：

```text
YuoCore.StateSync.Abstractions
  -> 新增 YuoAoiStreamCell
  -> 新增 YuoGridAoiStreamResolver
  -> ResolveCell(mapId, position, phase)
  -> ResolveStreamId(mapId, position, phase)
  -> ResolveStreamId(mapId, observer)
```

生成规则：

```text
mapId=1001, phase=normal, cell=(2,3)
  -> map:1001:aoi:normal.cell_2_3

phase 为空时使用 default。
负坐标按 MathF.Floor 映射，例如 x=-1, z=-11, cellSize=10 -> cell=(-1,-2)。
```

边界：

```text
Resolver 位于 StateSync.Abstractions，不依赖 ECS、Backend、Gateway、WebSocket 或 Actor。
当前 Backend 示例仍使用 player:{playerId:N} 玩家观察流。
不能在服务端尚未按 stream 聚合事件前直接把多个玩家切到同一个共享 AOI stream；否则可能把 per-player event queue 写进同一条 replay history。
正式切换需要 Map/Room 层先按 AOI stream 聚合 snapshot/delta，再通过订阅表下发。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
```

结果：

```text
StateSync focused tests: Passed 90
```

## 2026-06-09：StateSync 订阅表抽象与 Backend 接入

本轮把 stream 订阅和 ACK 游标抽成纯 StateSync Abstractions：

```text
YuoCore.StateSync.Abstractions
  -> 新增 YuoStateSyncSubscriptionTable
  -> Subscribe(subscriberId, streamId)
  -> SetSubscriberStreams(subscriberId, streamIds)
  -> Unsubscribe / RemoveSubscriber
  -> Acknowledge(subscriberId, streamId, ackTick)
  -> TryGetSafePruneTick(streamId)
  -> GetStreams / GetSubscribers
```

Backend 接入：

```text
GameStateSyncPushDispatcher
  -> 不再维护 _ackedStateTickBySession
  -> 不再维护 _sessionStreamBySession
  -> BindSessionStream 使用 SetSubscriberStreams，当前示例绑定为单 stream
  -> ClearSession 调用 subscriptionTable.RemoveSubscriber
  -> ACK 调用 subscriptionTable.Acknowledge
  -> replay history 裁剪调用 subscriptionTable.TryGetSafePruneTick
```

语义：

```text
订阅表不知道 GatewaySession、WebSocket、Actor、ECS 或 Backend。
subscriberId 当前由 Backend 用 sessionId:N 生成，未来可以替换为连接、Actor、客户端订阅 id 或内部服务订阅 id。
ACK tick 按 subscriber + stream 单调递增；所有活跃订阅者都有 ACK 后，才给出 safe prune tick。
SetSubscriberStreams 用于跨地图或 AOI cell 迁移；被移除的 stream 会同步清理旧 ACK。
表内部加锁，避免 push loop 和 request handler 并发读写普通 Dictionary。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
StateSync focused tests: Passed 80
Backend focused tests: Passed 38
```

后续边界：

```text
当前 Backend 仍只绑定一个 player stream。
正式 AOI 下发还需要让 Map/Room 侧根据 AOI cell 生成多个 stream，并在移动跨 cell 时调用 SetSubscriberStreams 切换订阅集合。
```

## 2026-06-09：Backend StateSync 历史与 ACK 裁剪改为 streamId 维度

本轮把 Backend 示例中的 StateSync replay history / tick / ACK 裁剪从 `playerId` 维度推进到 `streamId` 维度：

```text
GameStateSyncPushDispatcher
  -> _nextStateTickByStream
  -> _deltaHistoryByStream
  -> _sessionStreamBySession
  -> AllocateStateTick(streamId)
  -> GetReplayBuffer(streamId)
  -> PruneReplayHistoryIfAllActiveSessionsAcked(streamId)
```

保留兼容入口：

```text
TryGetReplayHistoryStats(Guid playerId, ...)
  -> 内部转换为当前示例的 player streamId

TryGetReplayHistoryStats(string streamId, ...)
  -> 直接按 streamId 查询
```

语义：

```text
当前 Backend 示例仍选择 player:{playerId:N} 作为观察流。
但 replay history、tick 分配和 ACK 裁剪已经不再依赖 playerId 作为内部 key。
后续切换到 map/aoi/interest group stream 时，可以替换 ResolveStreamId 策略，而不是重写 replay/ACK 裁剪逻辑。
不同 stream 的 ACK 裁剪互不影响；某个 stream 全部活跃 session ACK 后，只裁剪该 stream 的 replay buffer。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 38
新增覆盖：Gateway_bridge_v1_state_ack_prunes_only_matching_stream_history
```

## 2026-06-09：StateSync 多流 MirrorSet 与客户端多流 Receiver

本轮补上正式 AOI / 多兴趣流订阅所需的客户端侧基础模型：

```text
YuoCore.StateSync.Abstractions
  -> 新增 YuoStateSyncStreamCursor
  -> 新增 YuoStateSyncStreamMirrorSet
  -> 每条 stream 独立维护 YuoStateSyncMirror、LastTick 和 SnapshotRequired

YuoCore.Client.StateSync
  -> 新增 YuoClientStateSyncMultiStreamReceiver
  -> 根据 StateSnapshot/StateDelta 的 streamId 分发到对应 Mirror
  -> 没有 streamId 且未配置 defaultStreamId 时拒绝处理，避免多流模式误混流

clients/web/yuocore-client-ts
  -> 新增 YuoStateSyncStreamMirrorSet
  -> 新增 YuoStateSyncMultiStreamReceiver
  -> smoke 覆盖两个 AOI stream 的独立 tick/gap 状态
```

语义：

```text
单流客户端仍可继续使用 YuoClientStateSyncReceiver / YuoStateSyncReceiver。
多流客户端使用 MultiStreamReceiver 后，map:1001:aoi:cell_1 和 map:1001:aoi:cell_2 的 BaseTick 不会互相污染。
这是客户端/同步层的多流镜像能力，不是服务端 AOI 订阅表、跨进程路由或 WebSocket 重连状态机。
Backend 示例目前仍只选择 player:{playerId:N} 玩家观察流。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

结果：

```text
StateSync focused tests: Passed 73
Client.StateSync focused tests: Passed 12
TypeScript compile: Passed
Web SDK smoke: yuocore-client-ts smoke passed
```

## 2026-06-09：客户端 Recover 默认沿用最近 StateSync streamId

本轮补齐重连恢复的一个关键细节：客户端不再只知道 tick，还会记住最近的 StateSync streamId。

```text
YuoCore.Client.StateSync
  -> YuoClientStateSyncReceiver 增加 LastStreamId
  -> ApplyStateSnapshot / ApplyStateDelta 看到 streamId 时更新 LastStreamId
  -> Clear 同时清理 LastStreamId
  -> YuoClientStateSyncRecovery.RecoverAsync 默认使用显式 streamId，未传则使用 Receiver.LastStreamId
  -> Recover response 自身有 streamId 但子 snapshot/delta 没填时，会用 response.streamId 兜底

clients/web/yuocore-client-ts
  -> YuoStateSyncReceiver 增加 lastStreamId
  -> recoverStateSync 默认携带 receiver.lastStreamId
  -> 兼容旧签名，第五参数仍可直接传 options；如果第五参数是字符串才当作显式 streamId
  -> recover response.streamId 同样会兜底填入子 snapshot/delta
```

语义：

```text
这让断线重连后的 RecoverRequest 能默认回到同一条玩家/AOI/兴趣流，而不是退回空 streamId。
它仍不负责自动重连、重新订阅 WebSocket、指数退避或连接状态机。
正式自动重连管理器应在更高层组合 transport reconnect、subscribe push、recover、ack 和业务回调。
```

验证：

```bash
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

结果：

```text
Client.StateSync focused tests: Passed 10
TypeScript compile: Passed
Web SDK smoke: yuocore-client-ts smoke passed
```

## 2026-06-09：客户端 StateSync 成功应用后 ACK helper

本轮补齐客户端侧“什么时候可以 ACK”的最小组合能力：

```text
YuoCore.Client.StateSync
  -> YuoClientStateSyncReceiveResult 增加 StreamId
  -> YuoClientStateSyncAcks.ShouldAck(result)
  -> YuoClientStateSyncAcks.AckIfAppliedAsync(client, playerId, result)

clients/web/yuocore-client-ts
  -> YuoStateSyncPushResult 增加 streamId
  -> shouldAckStateSync(result)
  -> ackStateSyncIfApplied(client, playerId, result)
```

语义：

```text
只有 Snapshot/Delta 成功应用到本地 Mirror 后才会 ACK。
GapDetected、SnapshotRequired、Rejected、Ignored 都不会 ACK。
ACK 会优先携带 result.streamId，避免客户端确认错同步流。
PushReceived 仍是同步事件；helper 不把异步 ACK 强塞进传输层事件，而是由订阅回调或业务组合层显式调用。
```

验证：

```bash
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

结果：

```text
Client.StateSync focused tests: Passed 10
TypeScript compile: Passed
Web SDK smoke: yuocore-client-ts smoke passed
```

## 2026-06-09：StateSync streamId 命名规则抽象

本轮把 Backend 示例中的 `player:{playerId}` 字符串拼接下沉到纯 StateSync Abstractions：

```text
YuoCore.StateSync.Abstractions
  -> 新增 YuoStateSyncStreamIds
  -> ForPlayer(Guid)
  -> ForMap(long)
  -> ForMapAoi(long, string)
  -> ForInterestGroup(string, string)
  -> MatchesOrEmpty(expected, requested)

YuoCore.Backend
  -> GameStateSyncPushDispatcher 继续选择玩家观察流
  -> streamId 格式改为调用 YuoStateSyncStreamIds.ForPlayer(playerId)
  -> recover/ack stream 校验改为调用 YuoStateSyncStreamIds.MatchesOrEmpty
```

设计边界：

```text
这是命名规则集中化，不是正式 AOI 订阅系统。
StateSync.Abstractions 仍不依赖 Protocol、Gateway、Backend、WebSocket、Actor 或 ECS。
Backend 示例仍使用玩家观察流，后续正式 MMO 可以按 map/aoi/interest group 维度替换流选择策略。
空 streamId 请求仍被兼容，便于旧客户端或简单客户端走默认观察流；客户端一旦携带 streamId，就必须精确匹配。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
StateSync focused tests: Passed 69
Backend focused tests: Passed 37
```

## 2026-06-09：BaseTick / 客户端恢复后的 1w quick 压测复测

本轮在客户端 push 订阅、snapshot 恢复桥、Backend StateDelta BaseTick 之后，重新运行默认 quick 压测：

```bash
scripts/run-local-load-suite.sh --quick
```

产物：

```text
artifacts/load/20260608T163110Z-quick/local-composite-count.json
artifacts/load/20260608T163110Z-quick/ecs-shape.json
```

local-composite 结果：

```text
players=10,000 stateEntities=10,000 ticks=5 shards=4
passed=true
tick-ms avg=74.576 p50=67.742 p95=131.272 max=131.272
actor-enqueue p95=53.143
runtime-tick p95=12.228
statesync-delta p95=7.771
aoi-query p95=36.880
persistence p95=24.052
allocated=30.85MB heap-after=43.79MB gc0=3 gc1=3 gc2=1
```

ecs-shape 结果：

```text
entities=10,000 components=100 systems=100 ticks=5 shards=4
passed=true
setup=2,185.075ms
tick-ms avg=141.244 p50=141.688 p95=173.132 max=173.132
allocated=327.87MB heap-after=209.03MB gc0=23 gc1=22 gc2=4
```

由于本次 ecs-shape p95 比上一轮偏高，单独复跑一轮 ecs-shape：

```bash
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode ecs-shape --state-entities 10000 --ticks 5 --shards 4 --json-output /tmp/yuocore-ecs-shape-repeat.json --fail-p95-ms 250 --fail-max-ms 500
```

复跑结果：

```text
tick-ms avg=134.121 p50=137.546 p95=164.673 max=164.673
allocated=327.59MB heap-after=208.42MB gc0=23 gc1=22 gc2=4
```

判断：

```text
local-composite 与前序 quick 结果同一量级，StateDelta BaseTick 和客户端恢复桥没有进入热路径，也未造成明显退化。
ecs-shape 首轮 p95=173ms，复跑回到 165ms 左右，属于 5 tick quick 的短样本波动范围。
当前仍未发现比既有 ECS 100组件/100系统扫描更严重的新热点。
长期性能判断仍应看 duration-mode soak，而不是单轮 5 tick quick。
```

## 2026-06-09：BaseTick / 客户端恢复后的 30 秒 soak 复测

本轮继续运行 30 秒持续压测：

```bash
YUO_LOAD_DURATION_SECONDS=30 scripts/run-local-load-suite.sh --soak
```

产物：

```text
artifacts/load/20260608T163327Z-soak/local-composite-count.json
artifacts/load/20260608T163327Z-soak/local-composite-count.csv
artifacts/load/20260608T163327Z-soak/ecs-shape.json
```

local-composite 结果：

```text
players=10,000 stateEntities=10,000 duration=30s shards=4
passed=true
actualTicks=1,427 ticksPerSecond=47.54
tick-ms avg=21.035 p50=16.740 p95=34.881 max=125.020
actor-enqueue p95=16.471
runtime-tick p95=3.312
statesync-delta p95=8.604
aoi-query p95=11.361
persistence p95=16.563
allocated=8,648.58MB heap-after=40.29MB gc0=591 gc1=394 gc2=86
```

ecs-shape 结果：

```text
entities=10,000 components=100 systems=100 duration=30s shards=4
passed=true
actualTicks=310 ticksPerSecond=10.33
setup=2,150.338ms
tick-ms avg=96.768 p50=94.758 p95=110.250 max=192.165
allocated=311.55MB heap-after=198.35MB gc0=23 gc1=22 gc2=5
```

判断：

```text
30 秒 soak 仍在当前阈值内。
local-composite p95=34.881ms，比前序 30 秒 smoke 的约 36ms 同一量级，未发现 BaseTick / 客户端恢复桥引发服务端热路径退化。
ecs-shape p95=110.250ms，也与前序 duration-mode 的约 106ms 同一量级。
local-composite 30 秒分配约 8.65GB，GC 次数仍高；长期商业级运行仍应优先看 StateSync payload writer/buffer pooling、Persistence 批量编码和业务快照复用。
```

## 2026-06-09：Backend StateDelta BaseTick

本轮补齐 Backend 示例推送流的连续性信息：

```text
GameStateSyncPushDispatcher
  -> 为每个 Gateway session 记录 lastStateTick
  -> StateSnapshot 发送后更新 lastStateTick
  -> StateDelta 发送时把上一帧 lastStateTick 写入 BaseTick
  -> StateDelta 发送后更新 lastStateTick
  -> ClearSession 同时清理 snapshot map 和 state tick
```

收益：

```text
客户端 Mirror 已支持 BaseTick 缺口检测。
之前 Backend delta 不带 BaseTick，客户端只能按 Tick 是否倒退做弱校验。
现在 snapshot -> delta -> delta 可以形成连接级连续流；如果客户端漏应用某个 delta，后续 delta 会触发 GapDetected，从而走 StateSnapshotRequest 兜底恢复。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 32
Solution build: succeeded, 0 warnings, 0 errors
Solution tests: all listed test projects passed
```

边界：

```text
BaseTick 当前仍是连接级 serverSeq，不是正式 MapFiber/AOI stream tick。
它能让客户端检测本连接同步流缺口，但不提供历史 delta replay。
正式 MMO 若要低成本补包，还需要服务端保存每个 AOI stream 的短期 delta ring buffer。
```

## 2026-06-09：客户端 StateSync Snapshot 恢复桥

本轮补齐 Mirror 发现 Delta 缺口后的最小恢复路径：

```text
YuoClientStateSyncReceiver
  -> 新增 ApplyStateSnapshot(StateSnapshotMessage)
  -> 新增 ApplyStateDelta(StateDeltaMessage)
  -> 请求响应拿到的 snapshot 可以不再伪造成 server envelope

YuoClientStateSyncRecovery
  -> ShouldRequestSnapshot(result)
  -> RequestSnapshotAsync(playerId, knownTick)
  -> 复用 YuoMmoClient.RequestStateSnapshotAsync
  -> 把返回的 StateSnapshotMessage 应用到 Mirror

Web TS SDK
  -> shouldRequestStateSnapshot(result)
  -> recoverStateSyncSnapshot(client, receiver, playerId, knownTick)
```

设计边界：

```text
这是缺口后的“拉当前全量 snapshot 并覆盖 Mirror”能力。
它不实现 delta replay、ack、断线重连、指数退避或订阅恢复。
恢复请求仍走外部 Gateway 协议 StateSnapshotRequest，不暴露 ActorId、FiberId、LocationKey 或服务端 Entity。
```

验证：

```bash
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-pack-smoke.sh
```

结果：

```text
Client.StateSync focused tests: Passed 7
TypeScript compile: succeeded
TypeScript smoke: yuocore-client-ts smoke passed
Solution build: succeeded, 0 warnings, 0 errors
Solution tests: all listed test projects passed
Pack smoke: packages=15
```

剩余边界：

```text
正式 MMO 仍需要结合连接重连、订阅恢复、ack/replay 和 Gateway 侧历史 delta 策略。
当前恢复是主动拉全量快照，适合作为安全兜底，不是高频补包方案。
```

## 2026-06-09：客户端 StateSync Push 订阅桥

本轮在基础 push 订阅之上补了一层轻量组合：

```text
YuoCore.Client
  -> 新增 IYuoClientPushTransport
  -> 表示“既能 request/response，也能订阅 server push”的可选客户端传输能力

YuoCore.Client.StateSync
  -> 新增 YuoClientStateSyncSubscription
  -> Attach(IYuoClientPushTransport, YuoClientStateSyncReceiver, onReceive)
  -> 自动把 requestId=0 的 StateSnapshot / StateDelta 应用到客户端 Mirror

clients/web/yuocore-client-ts
  -> 新增 subscribeStateSyncPushes(...)
  -> 组合 subscribePush + YuoStateSyncReceiver
```

设计边界：

```text
IYuoClientPushTransport 仍只属于客户端外部协议层，不引用 WebSocket、Backend、Gateway、Actor、Runtime 或 ECS。
YuoClientStateSyncSubscription 不处理重连、不做 ack/replay、不负责业务表现对象，只把推送应用到 Mirror。
Unity WebSocket 适配通过实现 IYuoClientPushTransport 接入该桥；Web TS 通过结构化 subscribePush 接入。
```

验证：

```bash
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-pack-smoke.sh
```

结果：

```text
Client focused tests: Passed 8
Client.StateSync focused tests: Passed 6
Unity client adapter build: succeeded
TypeScript compile: succeeded
TypeScript smoke: yuocore-client-ts smoke passed
Solution build: succeeded, 0 warnings, 0 errors
Solution tests: all listed test projects passed
Pack smoke: packages=15
```

剩余边界：

```text
该桥解决“推送如何进入 Mirror”的基础组合问题。
完整 MMO 客户端仍需要 DuplexSession 级别的重连、订阅恢复、snapshot 自动重拉、ack/replay 和本地表现对象绑定。
```

## 2026-06-08：客户端 WebSocket Push 订阅雏形

本轮把客户端侧 `requestId=0` 服务端推送处理从测试专用能力推进到可选客户端适配层：

```text
YuoCore.Client
  -> 新增 YuoClientServerMessage
  -> 解析 YuoServerEnvelopeV1
  -> 识别 RequestId=0 的 server push
  -> 提供 ReadClientRequestId 给平台 transport 做响应匹配

clients/unity/YuoCore.UnityClient
  -> YuoClientWebSocketTransport 改为后台 receive loop
  -> SendAsync 只按 requestId 完成对应响应
  -> requestId=0 的消息触发 PushReceived

clients/web/yuocore-client-ts
  -> YuoWebSocketTransport.subscribePush(handler)
  -> requestId=0 的 envelope 不再被静默丢弃
```

设计边界：

```text
YuoCore.Client 仍不实现 WebSocket/TCP/KCP，只提供协议消息解析。
Unity WebSocket 适配层仍只引用 YuoCore.Client，不引用 Backend、Gateway、Actor、Runtime、ECS 或 UnityEngine。
TS WebSocket transport 只暴露外部协议 envelope，不暴露 Gateway session、ActorId、FiberId、LocationKey 或服务端 Entity。
```

验证：

```bash
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-pack-smoke.sh
```

结果：

```text
Client focused tests: Passed 8
Unity client adapter build: succeeded
TypeScript compile: succeeded
TypeScript smoke: yuocore-client-ts smoke passed
Solution build: succeeded, 0 warnings, 0 errors
Solution tests: all listed test projects passed
Pack smoke: packages=15
```

剩余边界：

```text
当前只是基础 push 订阅，不是完整可靠流。
还没有统一的高级 DuplexSession，也没有客户端 ack、delta replay、自动 snapshot 重拉调度。
StateSync 应用仍由业务或 YuoCore.Client.StateSync 组合完成，不在 transport 内强绑。
```

## 2026-06-08：真实 WebSocket 状态广播闭环

本轮补齐两客户端真实 `/ws` 状态广播测试：

```text
Observer Client
  -> Login
  -> EnterMap
  -> 接收 StateSnapshot 推送

Mover Client
  -> Login
  -> EnterMap
  -> Move

Observer Client
  -> 接收 requestId=0 的 StateDelta 推送
  -> YuoClientStateSyncReceiver 应用到本地 Mirror
  -> Mirror 中的 mover 坐标和 LastInputSeq 更新
```

覆盖结果：

```text
GameWebSocketEndpoint 的 push pump 能通过真实 TestServer WebSocket 把 StateSnapshot / StateDelta 发给客户端。
测试客户端区分 request/response 和 requestId=0 的 server push，避免把推送误当成普通请求响应。
YuoClientStateSyncReceiver 可以消费服务端 envelope，并把 PlayerWorldState 写入纯客户端 Mirror。
这证明 Backend 示例的最小状态广播链路已经跨过真实 WebSocket，而不仅是 dispatcher/push pump 单测。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 32
Client.StateSync focused tests: Passed 5
Solution build: succeeded, 0 warnings, 0 errors
Solution tests: all listed test projects passed
```

边界：

```text
该测试使用 TestServer WebSocket，不是浏览器 DOM WebSocket 自动化。
当前 C# 生产客户端仍保持 request/response transport 抽象；测试中的双工客户端只是为了证明服务端 push 线协议和 StateSync receiver 能闭环。
正式 Web/Unity SDK 后续应补一个可订阅 push 的双工 transport 抽象，避免业务层自己处理 requestId=0 envelope。
```

## 2026-06-08：真实 WebSocket + C# 客户端 SDK 闭环

本轮补齐一个不经过外部端口的真实 WebSocket 端到端测试：

```text
WebApplicationFactory/TestServer
  -> /ws
  -> GameWebSocketEndpoint
  -> YuoGatewayDispatchPipeline
  -> GameGatewayProtobufDispatcher
  -> GameClientMessageDispatcher
  -> GameRuntime
```

测试客户端使用 `YuoCore.Client.YuoMmoClient` 和一个 TestServer WebSocket transport，发送真实 `YuoClientEnvelopeV1` 二进制 protobuf。

覆盖结果：

```text
Login -> EnterMap -> Move -> Disconnect 可以通过真实 /ws 路径完成。
响应按 YuoServerEnvelopeV1 返回，并由 YuoClientSession 校验 requestId、opcode 和 protobuf payload。
Disconnect 不再要求 Web/Unity 客户端知道 Gateway 内部 sessionId。
Gateway bridge 会用当前 YuoGatewaySession.SessionId 归一化 DisconnectRequest.SessionId，避免客户端伪造其他连接 session。
```

同步调整：

```text
YuoCore.Client.YuoMmoClient.DisconnectAsync 的 sessionId 改为可选。
clients/web/yuocore-client-ts 的 YuoMmoClient.disconnect 的 sessionId 改为可选。
TS smoke 改为覆盖空 sessionId 的 disconnect 请求。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 31
Client focused tests: Passed 6
Unity client adapter build: succeeded
TypeScript compile: succeeded
TypeScript smoke: yuocore-client-ts smoke passed
Solution build: succeeded, 0 warnings, 0 errors
Solution tests: all listed test projects passed
```

边界：

```text
这证明 Backend /ws 和 C# 客户端 SDK 的最小 request/response 闭环可用。
它仍不是浏览器真实 WebSocket 自动化测试，也不是 Unity Editor 运行时测试；当前只证明 Unity 可选适配包能编译，且 C# 客户端安全子集能通过真实 TestServer WebSocket。
```

## 2026-06-08：Backend MMO v1 持久化恢复闭环复测

本轮补齐 Backend 示例宿主的 v1 协议闭环测试：

```text
Hello -> EnterMap -> Move -> Disconnect -> 保存离线快照 -> 新 GameRuntime -> Hello 恢复快照
```

覆盖结果：

```text
Gateway v1 bridge 能通过 Protobuf envelope 处理登录、进图、移动和断线。
Disconnect 会把当前地图、UnitId、坐标、LastInputSeq 和 Online=false 写入快照存储。
新 Runtime 使用同一个 SnapshotPersistenceService 后，Hello 同一 playerId 能恢复地图位置并重新标记 Online=true。
该测试走 YuoGatewayDispatchPipeline + GameGatewayProtobufDispatcher + GameClientMessageDispatcher，不直接调用 GameRuntime 私有路径。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 30
```

边界：

```text
这仍是 Backend 示例宿主内的最小 MMO 闭环，不代表分布式迁移、可靠 outbox replay、长时间 soak 或商业级网络压测已经完成。
WebSocket / Unity SDK 的真实端到端连接还需要后续单独验证。
```

## 2026-06-08：一键本地压测套件入口

本轮新增 `scripts/run-local-load-suite.sh`，把已有 `tools/YuoCore.LoadTests` 的关键模式收束成一个稳定入口：

```bash
scripts/run-local-load-suite.sh --quick
scripts/run-local-load-suite.sh --soak
```

覆盖范围：

```text
local-composite：覆盖 Actor command 入队、GameRuntime tick、StateSync delta、AOI 查询和 Persistence 队列 drain。
ecs-shape：覆盖 1w 实体、100 组件、100 system、多 shard 的极端 ECS 调度形态。
产物目录：artifacts/load/<timestamp>-quick 或 artifacts/load/<timestamp>-soak。
```

设计边界：

```text
脚本只调用 tools/YuoCore.LoadTests，不引入新的运行时依赖。
artifacts/load 已被 .gitignore 忽略，避免性能结果和 CSV 污染提交。
默认阈值是本机退化门禁，不是商业级 SLA。
--soak 才用于观察长时间抖动、GC 和分段热点；--quick 只用于快速发现明显退化。
```

可调参数：

```text
YUO_LOAD_PLAYERS
YUO_LOAD_STATE_ENTITIES
YUO_LOAD_SHARDS
YUO_LOAD_TICKS
YUO_LOAD_DURATION_SECONDS
YUO_LOAD_AOI_OBSERVERS
YUO_LOAD_PERSISTENCE_PER_TICK
```

## 2026-06-08：默认 1w quick 压测复测

本轮在新增 pack smoke、StateSnapshotRequest、Backend WebSocket 推送背压后，重新运行默认本地 quick 压测：

```bash
scripts/run-local-load-suite.sh --quick
```

产物：

```text
artifacts/load/20260608T125221Z-quick/local-composite-count.json
artifacts/load/20260608T125221Z-quick/ecs-shape.json
```

local-composite 结果：

```text
players=10,000 stateEntities=10,000 ticks=5 shards=4 aoi=count persistence=10,000/tick
passed=true
tick-ms avg=73.295 p50=68.952 p95=125.098 max=125.098
actor-enqueue p95=50.182
runtime-tick p95=12.100
statesync-delta p95=7.674
aoi-query p95=36.956
persistence p95=25.127
allocated=32.38MB heap-after=45.29MB gc0=3 gc1=3 gc2=1
```

ecs-shape 结果：

```text
entities=10,000 components=100 systems=100 ticks=5 shards=4
passed=true
setup=2,184.923ms
tick-ms avg=136.610 p50=150.710 p95=166.787 max=166.787
allocated=324.67MB heap-after=205.71MB gc0=23 gc1=22 gc2=4
```

判断：

```text
两个 quick 模式都在当前阈值内。
local-composite 的主要分段成本仍是 actor enqueue、AOI observer 查询和 persistence drain，未发现新增协议/pack/Gateway 背压代码导致明显退化。
ecs-shape 仍显著重于业务综合路径，100 组件 / 100 system 极端形态下分配和 GC 压力较高。
当前不直接做破坏性 ECS 调度优化；预编译执行器、组件组合缓存、system 批调度仍需要单独设计和对照 benchmark，不能牺牲 system try/catch 异常隔离和动态组件语义。
这仍是 5 tick quick，不是商业级长期 soak。
```

## 2026-06-08：30 秒 duration-mode soak 烟测

本轮运行 30 秒持续压测：

```bash
YUO_LOAD_DURATION_SECONDS=30 scripts/run-local-load-suite.sh --soak
```

产物：

```text
artifacts/load/20260608T125608Z-soak/local-composite-count.json
artifacts/load/20260608T125608Z-soak/local-composite-count.csv
artifacts/load/20260608T125608Z-soak/ecs-shape.json
```

local-composite 结果：

```text
players=10,000 stateEntities=10,000 duration=30s shards=4 aoi=count persistence=10,000/tick
passed=true
actualTicks=1,363 ticksPerSecond=45.43
tick-ms avg=22.012 p50=17.111 p95=37.517 max=128.214
去掉前 10 tick 后：samples=1,353 p50=17.072 p95=36.139 max=76.961
actor-enqueue p95=18.859
runtime-tick p95=3.300
statesync-delta p95=13.342
aoi-query p95=12.754
persistence p95=13.717
allocated=8,678.00MB heap-after=55.75MB gc0=592 gc1=414 gc2=84
```

ecs-shape 结果：

```text
entities=10,000 components=100 systems=100 duration=30s shards=4
passed=true
actualTicks=322 ticksPerSecond=10.72
setup=2,031.530ms
tick-ms avg=93.288 p50=90.955 p95=106.554 max=174.290
allocated=326.94MB heap-after=207.75MB gc0=23 gc1=22 gc2=4
```

判断：

```text
30 秒 soak smoke 通过当前阈值。
local-composite 第 1 tick 是最大值，属于冷启动/预热峰值；去掉前 10 tick 后 p95 约 36ms。
local-composite 的持续分段热点是 actor-enqueue、statesync-delta、AOI query 和 persistence drain，但单段 p95 仍在当前 smoke 阈值内。
local-composite 30 秒内分配 8.7GB，GC 次数较高。当前负载会为每个 entity/tick 构造状态 payload，并为每个 persistence command 构造事件 payload；这既是压测真实成本的一部分，也会放大 GC 噪声。
如果后续要把这条路径推向商业级长稳运行，应优先设计 payload writer、buffer pooling、持久化事件批量编码或业务层快照复用，而不是先改 ECS system 异常隔离或 Location/Actor 语义。
ecs-shape 在 duration-mode 下 p95 约 106ms，低于本轮 5 tick quick 的 p95；仍需更长 soak 和独立 BenchmarkDotNet 对照确认。
```

## 2026-06-08：payload profile 对照

本轮给 `tools/YuoCore.LoadTests --mode local-composite` 增加：

```text
--payload-profile allocated
--payload-profile shared-input
```

含义：

```text
allocated：保持原压测语义，每个 entity/tick 和 persistence command 都构造自己的输入 payload。
shared-input：每个 tick 复用输入 payload，再交给 SetPayload / YuoEventRecord；用于估算输入序列化临时对象占比。
```

验证：

```bash
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj --disable-build-servers
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 100 --ticks 2 --shards 2 --state-entities 100 --aoi-observers 8 --persistence-per-tick 100 --aoi-result-mode count --payload-profile allocated --json-output /tmp/yuocore-payload-allocated.json --fail-p95-ms 1000 --fail-max-ms 1000
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 100 --ticks 2 --shards 2 --state-entities 100 --aoi-observers 8 --persistence-per-tick 100 --aoi-result-mode count --payload-profile shared-input --json-output /tmp/yuocore-payload-shared.json --fail-p95-ms 1000 --fail-max-ms 1000
```

1w quick 对照：

```bash
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --payload-profile allocated --json-output /tmp/yuocore-1w-allocated-profile.json --fail-p95-ms 250 --fail-max-ms 500
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --payload-profile shared-input --json-output /tmp/yuocore-1w-shared-input.json --fail-p95-ms 250 --fail-max-ms 500
```

结果：

```text
allocated: tick p95=127.746ms persistence p95=24.439ms allocated=32.38MB
shared-input: tick p95=125.100ms persistence p95=18.738ms allocated=29.32MB
```

判断：

```text
shared-input 只降低约 9.5% 分配，说明输入 BitConverter.GetBytes 不是唯一大头。
YuoNetworkStateComponent.SetPayload 会复制 payload；delta 构建、事件记录对象和持久化命令对象仍会产生分配。
该 profile 是诊断工具，不应作为“业务已经优化”的证明。
真正要降低长期 GC，需要设计 StateSync writer/source generator、可复用 payload buffer、批量 persistence encoding 或对象池化策略。
```

## 2026-06-08：StateSync owned payload API

本轮给 `YuoNetworkStateComponent` 增加：

```csharp
SetPayloadOwned(byte[] payload)
```

语义：

```text
SetPayload(ReadOnlySpan<byte>) 保持复制输入，适合调用方还要继续复用/修改输入 buffer。
SetPayloadOwned(byte[]) 表示调用方把 byte[] 所有权交给组件，之后不能再修改该数组。
这是 additive API，不改变已有调用方行为。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj --disable-build-servers
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --payload-profile allocated --json-output /tmp/yuocore-1w-owned-payload.json --fail-p95-ms 250 --fail-max-ms 500
```

结果：

```text
StateSync focused tests: Passed 56
owned payload 1w quick: tick p95=136.157ms allocated=30.85MB
原复制路径基线约 allocated=32.38MB
```

判断：

```text
该 API 可以避免“调用方已经生成最终 byte[] 后又被 SetPayload 复制一次”的浪费。
本轮 1w quick 中分配约降低 4.7%，属于安全小优化，不是质变。
长期 GC 大头仍需要从 payload writer/source generator、批量 persistence encoding 和对象池化继续处理。
```

## 2026-06-08：Persistence 批量入队 API

本轮给 `YuoPersistenceQueue` 增加：

```csharp
ValueTask EnqueueBatchAsync(
    IReadOnlyList<YuoPersistenceCommand> commands,
    CancellationToken cancellationToken = default)
```

语义：

```text
这是 additive API，不改变 EnqueueAsync 的单条入队行为。
批量入口仍逐条写入 bounded Channel，保持队列容量、背压、取消和统计语义不变。
队列未满时优先走 TryWrite 热路径，避免批量保存场景中每条命令都进入异步等待路径。
命令集合会先做 null 校验；发现 null 时不写入任何命令，避免半批脏数据进入队列。
```

压测工具新增：

```text
--persistence-enqueue-mode single
--persistence-enqueue-mode batch
```

`scripts/run-local-load-suite.sh` 可通过环境变量切换：

```bash
YUO_LOAD_PERSISTENCE_ENQUEUE=batch scripts/run-local-load-suite.sh --quick
```

验证：

```bash
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 20 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --payload-profile allocated --persistence-enqueue-mode single --json-output /tmp/yuocore-1w-persistence-single-20t-v3.json --fail-p95-ms 250 --fail-max-ms 500
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 20 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --payload-profile allocated --persistence-enqueue-mode batch --json-output /tmp/yuocore-1w-persistence-batch-20t-v3.json --fail-p95-ms 250 --fail-max-ms 500
```

结果：

```text
single: tick p95=93.855ms persistence p95=23.331ms allocated=121.86MB
batch:  tick p95=86.270ms persistence p95=21.517ms allocated=122.05MB
```

判断：

```text
批量入队可以减少一部分队列写入调度成本，但本轮 1w/20tick 只属于小幅收益。
分配基本持平，说明主要 GC 压力仍来自命令、事件记录、payload 和 StateSync delta 构造，不在 Channel 入队本身。
该 API 适合作为 Persistence 写回聚合器、停服 drain、未来 outbox replay 的基础能力；不能作为“持久化路径已完成性能优化”的证明。
后续若要继续降低长期 GC，应优先设计批量事件编码、幂等事件 id、payload writer/source generator 和对象池策略。
```

## 2026-06-08：C# MMO 客户端 facade

本轮在 `YuoCore.Client` 增加 `YuoMmoClient`：

```text
LoginAsync
ClickAsync
GetSnapshotAsync
EnterMapAsync
MoveAsync
DisconnectAsync
RequestStateSnapshotAsync
```

设计边界：

```text
YuoMmoClient 只依赖 IYuoClientTransport、YuoClientSession 和 YuoCore.Protocol。
它不引用 Unity、WebSocket、Backend、Gateway、Actor、Runtime 或 ECS。
Unity Editor / Standalone 可通过 YuoCore.UnityClient 的 YuoClientWebSocketTransport 直接复用该 facade。
WebGL 或浏览器仍应使用 Web/JS transport；facade 的职责是业务协议 helper，不是网络实现。
```

验证：

```bash
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
```

结果：

```text
Client focused tests: Passed 6
```

判断：

```text
这是客户端易用性能力，不改变协议 opcode、不改 protobuf，也不改变服务端职责。
它让 Unity/C# 客户端可以直接调用登录、点击、进地图、移动、断线和主动快照请求，不再在业务代码里重复传 parser。
```

## 2026-06-08：NuGet pack smoke 入口

本轮新增 `scripts/run-pack-smoke.sh`：

```text
默认打包 15 个代表性可发布库，覆盖核心、Actor/Runtime、协议/客户端、StateSync、Persistence、Config、Server。
输出目录为 artifacts/packages/pack-smoke-<timestamp>。
打包后扫描 nuspec，防止核心/客户端/可选服务端库误带 Backend、ASP.NET、Npgsql、Redis、Serilog、Dapper、OpenTelemetry。
```

验证：

```bash
bash -n scripts/run-pack-smoke.sh
YUO_PACK_PROJECTS='src/YuoCore.Ecs/YuoCore.Ecs.csproj' scripts/run-pack-smoke.sh
scripts/run-pack-smoke.sh
```

当前结果：

```text
单包 smoke: YuoCore.Ecs nupkg/snupkg 生成成功。
默认 smoke: packages=15，通过 nuspec 禁止依赖扫描。
```

边界：

```text
YuoCore.Backend 仍保持 IsPackable=false，不作为 NuGet 库发布。
pack smoke 是发布前结构验证，不替代 dotnet build/test、Web/Unity SDK 验证或性能压测。
```

## 2026-06-08：Backend WebSocket 推送背压

本轮把 Backend 示例的 StateSync 推送循环拆成有界队列：

```text
GameWebSocketPushPump 负责从 GameStateSyncPushDispatcher 定期 drain 推送。
生产者把推送写入 bounded Channel。
消费者串行调用 IYuoClientConnection.SendAsync。
队列达到 MaxQueuedMessages 时主动关闭 WebSocket，客户端应重连并重新拉 snapshot。
单次发送超过 SendTimeoutMs 时主动关闭 WebSocket，避免慢连接长期占住发送路径。
```

配置：

```json
"Gateway": {
  "WebSocketPush": {
    "PollIntervalMs": 50,
    "MaxQueuedMessages": 256,
    "SendTimeoutMs": 5000
  }
}
```

职责边界：

```text
该能力只存在于 YuoCore.Backend 示例宿主。
YuoCore.Gateway 仍只保留连接、会话、入站/出站消息抽象。
Ecs / Runtime / Actor / StateSync / Protocol 不引用 WebSocket、ASP.NET Core 或 Channel 背压策略。
```

取舍：

```text
慢客户端丢连接，不丢服务端世界状态。
断开后的恢复依赖已有重连、Hello 恢复和 StateSnapshot 首包推送。
这是示例宿主的保护策略，不是完整商业级 ack/replay；正式项目仍需要客户端 ack、snapshot request、delta replay 或按地图 tick 的推送流。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

当前结果：

```text
Backend focused tests: Passed 25
新增覆盖 push queue overflow 关闭慢客户端、push send timeout 关闭慢客户端。
```

## 2026-06-08：StateSnapshotRequest 最小重拉入口

本轮新增 v1 协议请求：

```text
StateSnapshotRequest
YuoCoreProtocolOpCodes.StateSnapshotRequest = 8
```

语义：

```text
客户端 Mirror 发现 delta 缺口后，可以通过 v1 envelope 发送 StateSnapshotRequest。
Backend Gateway bridge 校验 session 已绑定 playerId，并拒绝 player_id 与 session 不匹配的请求。
响应使用 StateSnapshotMessage，requestId 保持原请求 id。
该请求不进入旧版 legacy ServerEnvelope，因为 legacy oneof 没有 StateSnapshotMessage 响应槽位。
```

边界：

```text
StateSnapshotRequest 仍属于外部协议和 Backend 示例桥接能力。
它不改变 YuoCore.StateSync.Abstractions 的纯 Mirror/Delta 模型。
它不提供历史 delta replay；known_tick 当前只作为客户端上报上下文保留。
```

验证：

```bash
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-pack-smoke.sh
```

当前结果：

```text
Protocol focused tests: Passed 22
Backend focused tests: Passed 26
Client focused tests: Passed 5
Web TypeScript typecheck: Passed
Web TypeScript smoke: Passed
```

## 2026-06-08：LoadTests 增加 ECS Shape 模式

本轮把 100 组件 / 100 system / 多 Fiber 的 ECS 极端 shape 压测接入 `tools/YuoCore.LoadTests`：

```text
新增 --mode ecs-shape。
复用 YuoCore.Benchmarks 中的 ParallelShapeWorkload，避免复制 100 个组件和 100 个 system 后产生漂移。
支持 --state-entities、--ticks、--duration-mode、--shards、--json-output、--fail-p95-ms、--fail-max-ms。
该模式只存在于 tools 项目，不影响 src 核心包、Unity 客户端或单机导入路径。
```

冒烟验证：

```bash
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj --disable-build-servers
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode ecs-shape --state-entities 100 --ticks 2 --shards 2 --json-output /tmp/yuocore-ecs-shape-smoke.json --fail-p95-ms 1000 --fail-max-ms 1000
```

```text
ecs-shape smoke: entities=100 components=100 systems=100 shards=2
setup=155.006ms avg=2.361ms p95=2.699ms max=2.699ms
```

1w 短基线：

```bash
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode ecs-shape --state-entities 10000 --ticks 5 --shards 4 --json-output /tmp/yuocore-ecs-shape-1w.json --fail-p95-ms 1000 --fail-max-ms 1500
```

```text
ecs-shape 1w short: entities=10,000 components=100 systems=100 shards=4
setup=2,130.336ms
avg=124.545ms p50=132.592ms p95=156.796ms max=156.796ms
ticks-per-second=8.02
allocated=327.50MB heap-after=198.36MB gc0=24 gc1=23 gc2=5
```

判断：

```text
该结果与此前 BenchmarkDotNet shape 记录处于同一量级，没有发现近期 Gateway/Persistence/StateSync 工作导致 ECS shape 退化。
这是 5 tick 短基线，不是长期 soak。
极端 100 组件 + 100 system 的质变优化仍需围绕预编译执行器、组件组合缓存或调度结构评估，不能牺牲 system 异常隔离和动态组件语义。
```

## 2026-06-08：Backend WebSocket 登录恢复与断线保存

本轮把 Backend 示例的 WebSocket 协议分发接入快照持久化：

```text
GameClientMessageDispatcher 可选接收 SnapshotPersistenceService。
Hello 带已有 playerId 时，如果 Runtime 里没有该玩家，会先从 cache/repository 读取快照并 TryRestorePlayer。
DisconnectRequest 成功下线后，会保存当前离线快照。
GameWebSocketEndpoint 在 socket 异常关闭或正常关闭时，按 sessionId 下线并 best-effort 保存离线快照。
```

边界：

```text
持久化接入仍只在 YuoCore.Backend 示例宿主内，不进入 Ecs / Runtime / Actor / Gateway 核心包。
GameRuntime 仍只暴露快照读写能力，不引用数据库、Redis、ASP.NET Core 或 WebSocket。
显式 Disconnect 使用请求 cancellationToken；socket close 保存不复用 RequestAborted，因为关闭时它通常已经触发。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 21
新增覆盖 Hello 先恢复已存快照、Disconnect 保存离线 live snapshot。
```

补充：可选 durable outbox 装配

```text
Backend 新增 PlayerSnapshotPersistenceCodec，把 YuoPlayerSnapshotDto 转为通用 YuoSnapshotRecord。
PlayerSnapshotStoreAdapter 把 Persistence.Hosting 的 IYuoSnapshotStore 写回适配到 IPlayerSnapshotRepository。
SnapshotPersistenceService 在注入 YuoDurablePersistenceQueue 时，会先写 cache，再把 SaveSnapshotCommand 写入 durable queue；未注入时保持原直接 repository 保存。
Program 支持 Storage:DurableOutboxEnabled=true 时装配 YuoPersistenceQueue、YuoFilePersistenceOutbox、YuoDurablePersistenceQueue、YuoBackendPersistenceReplayService 和 YuoPersistenceBackgroundService。
示例要求 DurableOutboxEnabled 必须同时开启 PostgresEnabled，避免 outbox 成功 ack 到 Noop repository。
```

新增验证：

```text
Player snapshot codec roundtrip。
SnapshotPersistenceService durable 模式会 enqueue SaveSnapshotCommand，不直接调用 repository.SaveAsync。
```

## 2026-06-08：Backend 示例 StateSync 广播推送

本轮补齐 Backend 示例的最小服务端推送链路：

```text
GameGatewayProtobufDispatcher 在 Hello / Snapshot 响应后把 YuoGatewaySession 绑定到 playerId。
GameRuntime 新增 TryGetWorldSnapshot(playerId)，用于无副作用读取玩家当前地图完整快照，不 drain 事件队列。
GameStateSyncPushDispatcher 在 session 首次进入某个 map 时先推送 StateSnapshotMessage。
GameStateSyncPushDispatcher 从 GameRuntime.DrainEvents(playerId) 读取已经排好的世界事件。
世界事件被转换为通用 StateDeltaMessage，后续移动/离开按 delta 推送。
GameWebSocketEndpoint 增加连接级发送锁和后台 push loop，避免请求响应与广播推送并发写同一个 WebSocket。
WebSocket 断开时按 sessionId 调用 TryDisconnect；如果玩家已经用新 session 重连，旧连接关闭不会误下线。
```

边界：

```text
推送转换器放在 YuoCore.Backend 示例宿主内，不进入 Ecs / Runtime / Actor / Gateway 核心包。
它不持有 ECS Entity，也不做 AOI 判定，只消费 Runtime 已经为目标玩家排好的事件。
StateSnapshot / StateDelta payload 当前用 PlayerWorldState protobuf bytes 表示 player 状态，正式项目后续应由协议生成器或组件 writer 统一生成。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 19
覆盖登录后 session 绑定、进图后初始 StateSnapshot、观察者接收移动事件 StateDelta、v1 envelope 编解码、坏包错误响应、MMO 示例闭环。
```

## 2026-06-08：Web TypeScript 客户端 SDK 雏形

本轮补齐 Web 客户端最小 SDK：

```text
新增 clients/web/yuocore-client-ts。
提供 YuoClientSession、YuoWebSocketTransport、YuoProtocolOpCodes。
提供 YuoClientEnvelopeV1 / YuoServerEnvelopeV1 最小 protobuf 编解码。
提供 request/response codec 抽象，业务 protobuf 类型由 protobuf-ts/protobufjs/buf 等生成工具接入。
浏览器 WebSocket transport 根据 request_id 维护 pending request。
```

边界：

```text
Web SDK 不引用服务端 Actor、Fiber、Location、GatewaySession、Entity、数据库或 ASP.NET Core。
它只实现外部协议会话和 transport，不实现游戏 UI、渲染或具体业务 protobuf 生成代码。
```

验证：

```bash
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

结果：

```text
TypeScript strict typecheck: Passed
Web TypeScript SDK runtime smoke: Passed
```

## 2026-06-08：客户端 SDK 请求超时

本轮补齐 Web/Unity 客户端协议 SDK 的单次请求超时能力：

```text
YuoCore.Client 新增 YuoClientSessionOptions.RequestTimeout。
YuoClientSession.CallAsync 在请求发送前创建 linked CancellationTokenSource，把超时传给 IYuoClientTransport。
clients/web/yuocore-client-ts 的 session.call 兼容旧 AbortSignal 参数，并新增 { signal, timeoutMs } 选项。
YuoWebSocketTransport 在 socket.send 抛错时清理 pending request，避免连接异常造成 pending 泄漏。
```

边界：

```text
超时能力仍停留在客户端 SDK 层。
YuoCore.Client 只依赖 Abstractions / Protocol，不引用 Gateway、Backend、Actor、Runtime、ECS、ASP.NET Core、WebSocket、数据库或 Redis。
Web TypeScript SDK 只依赖浏览器 WebSocket/AbortController 能力，不暴露服务端 Actor/Fiber/Location 概念。
```

验证：

```bash
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

## 2026-06-08：Unity 客户端 WebSocket 适配

本轮新增 Unity 可选客户端适配：

```text
clients/unity/YuoCore.UnityClient
YuoClientWebSocketTransport
```

职责：

```text
实现 IYuoClientTransport，把 YuoCore.Client 的 bytes request/response 接到 ClientWebSocket。
适合 Unity Editor / Standalone 快速接入 Gateway WebSocket。
Unity WebGL 不使用 ClientWebSocket，应由 JS/WebGL 平台适配实现 IYuoClientTransport。
```

边界：

```text
该项目只引用 YuoCore.Client。
不引用 UnityEngine、YuoCore.Ecs、Actor、Runtime、Gateway、Backend、ASP.NET Core、数据库或 Redis。
```

验证：

```bash
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
```

护栏：

```text
Architecture tests 新增 Unity_client_adapter_stays_on_client_transport_boundary。
它会校验 YuoCore.UnityClient 只能项目引用 YuoCore.Client，且源码不引用 UnityEngine、服务端 Actor/ECS/Gateway/Backend/Runtime/Persistence 等命名空间。
```

## 2026-06-08：StateSync Protobuf 桥接包

本轮新增通用 StateSync 协议 DTO 和可选转换包：

```text
yuocore.proto 新增 NetworkEntityState、StateChangeMessage、StateSnapshotMessage、StateDeltaMessage。
YuoCoreProtocolOpCodes 新增 StateSnapshot=106、StateDelta=107。
YuoCore.StateSync.Protocol 新增 YuoStateSyncProtocolMapper。
```

职责：

```text
Protocol 只定义外部 DTO 和 opcode。
StateSync 继续只维护 YuoStateSnapshot / YuoStateDelta / YuoStateSyncMirror 等纯数据模型，不引用 Protocol。
StateSync.Protocol 负责两边转换，供 Gateway、Unity 或 C# 客户端按需引用。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Protocol.Tests/YuoCore.StateSync.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
```

## 2026-06-08：Persistence 文件 outbox/WAL 基础

本轮补齐持久化可靠性基础：

```text
YuoPersistence 新增 IYuoPersistenceOutbox、YuoFilePersistenceOutbox、YuoDurablePersistenceQueue、YuoOutboxPersistenceCommand。
YuoDurablePersistenceQueue 写入顺序是：先 append 本地文件 outbox，再 enqueue 内存队列。
YuoFilePersistenceOutbox 支持 replay 未 ack 命令、AcknowledgeThroughAsync 和 CompactAsync。
YuoPersistenceBackgroundService 在 Snapshot/Event store 写入成功后 ack outbox sequence。
业务写入成功后的 ack 不再使用 stoppingToken，避免 StopAsync 刚好打断 ack 导致重启重复 replay。
```

验证：

```bash
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
```

结果：

```text
Persistence focused tests: Passed 13
Persistence.Hosting focused tests: Passed 11
```

边界：

```text
这是本地文件 outbox/WAL 基础，不引入 PostgreSQL、Redis、Hosting 或数据库依赖到 YuoCore.Persistence。
它提升崩溃恢复能力，但不是 exactly-once；业务 store 仍需要幂等 key、版本号或事件去重来处理“store 成功但 ack 失败”的重复 replay。
数据库 outbox 表结构、事务边界和事件去重策略已记录到 docs/risk-and-decisions.md 等待裁定。
```

## 2026-06-08：local-composite 压测 JSON 与阈值失败

本轮增强 `tools/YuoCore.LoadTests --mode local-composite`：

```text
新增 --json-output <path>，输出机器可读 LocalCompositeResult。
新增 --csv-output <path>，输出逐 tick 分段耗时趋势。
新增 --duration-mode，使 local-composite 按 --duration 秒数持续运行，用于本地 soak。
新增 --fail-p95-ms <ms>，tick p95 超阈值时返回 exit code 2。
新增 --fail-max-ms <ms>，tick max 超阈值时返回 exit code 2。
JSON 输出新增 ActualTicks、DurationMode、ElapsedSeconds、TicksPerSecond 和 Runtime 指标。
Runtime 指标包含 GC Gen0/1/2、heap after、ThreadPool 线程数、pending work item 和 completed delta。
控制台原有人类可读输出保持不变。
浮点参数使用 invariant culture 解析，避免不同系统区域设置影响脚本。
```

验证：

```bash
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj --disable-build-servers
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 100 --ticks 3 --shards 2 --state-entities 100 --aoi-observers 8 --persistence-per-tick 100 --aoi-result-mode count --json-output /tmp/yuocore-load-result.json --fail-p95-ms 1000 --fail-max-ms 1000
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 50 --duration 1 --duration-mode --shards 2 --state-entities 50 --aoi-observers 4 --persistence-per-tick 50 --aoi-result-mode count --json-output /tmp/yuocore-duration-load.json --csv-output /tmp/yuocore-duration-load.csv --fail-p95-ms 1000 --fail-max-ms 1000
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10 --ticks 1 --shards 1 --state-entities 10 --aoi-observers 2 --persistence-per-tick 10 --aoi-result-mode count --json-output /tmp/yuocore-load-fail.json --fail-p95-ms 0.001
```

结果：

```text
pass gate: exit code 0，JSON Passed=true
duration-mode smoke: exit code 0，ActualTicks=2320，CSV generated
fail gate: exit code 2，JSON Passed=false
```

意义：

```text
长期 soak 和 CI 不再只能依赖控制台文本。
后续可以把 JSON/CSV 结果接入历史趋势、Prometheus 对照、或按 p95/max 阈值做自动失败。
CSV 会额外记录逐 tick 明细和 per-tick allocated 总量，应作为诊断模式；正式性能趋势优先使用不开 CSV 的 JSON/gate 路径。
```

补充修正：

```text
最初实现会在不开 CSV 时也采集 per-tick allocated 明细，可能污染常规压测路径。
已改为只有传入 --csv-output 时才保留逐 tick 明细并采集 per-tick allocated 总量。
默认 JSON/gate 路径只保留汇总样本和运行前后 Runtime 指标。
```

轻量路径复测：

```bash
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --json-output /tmp/yuocore-1w-count-load.json --fail-p95-ms 250 --fail-max-ms 500
```

```text
1w count short: avg=66.992ms p95=119.813ms max=119.813ms
actor=50,000 processed=50,000 state=50,000 persistence=50,000 pending=0
allocated=32.40MB gc0=3 gc1=3 gc2=1
```

Backend StateSync 推送改动后的当前复测：

```bash
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --json-output /tmp/yuocore-1w-count-load-latest.json --fail-p95-ms 250 --fail-max-ms 500
```

```text
1w count short latest: avg=75.162ms p95=129.996ms max=129.996ms
actor-enqueue avg=26.286ms p95=52.358ms
runtime-tick avg=7.864ms p95=12.639ms
statesync-delta avg=4.747ms p95=8.491ms
aoi-query avg=24.825ms p95=37.595ms
persistence avg=11.438ms p95=25.962ms
actor-commands=50,000 processed=50,000 persistence-drained=50,000 queue-pending=0
```

判断：

```text
本轮 Backend push 改动没有进入 local-composite 热路径，压测仍通过当前短阈值。
主要耗时仍集中在 actor-enqueue 和 aoi-query，后续优化应先分析这两段是否能在不改变语义的前提下降低分配和查找成本。
该结果只有 5 ticks，不能替代长时间 soak 或商业级容量承诺。
```

## 2026-06-08：NuGet 拆包与版本策略落地

本轮补齐可裁剪库发布规则：

```text
src/Directory.Build.props 统一 PackageId、VersionPrefix、RepositoryUrl、PackageOutputPath、symbol package 和 README 打包规则。
YuoCore.Backend 明确 IsPackable=false，它是 ASP.NET Core 示例宿主，不作为库包发布。
新增 docs/nuget-packaging.md，记录核心包、Actor/Fiber 包、客户端/Gateway 包、游戏域/StateSync 包、持久化/服务端装配包和开发期 Analyzer 包。
新增 Architecture tests，锁定公共 NuGet 元数据、Backend 不可 pack、NuGet 文档覆盖所有 src 项目。
```

验证：

```bash
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet pack src/YuoCore.Ecs/YuoCore.Ecs.csproj -c Release --disable-build-servers
dotnet pack src/YuoCore.Client/YuoCore.Client.csproj -c Release --disable-build-servers
dotnet pack src/YuoCore.Persistence.Hosting/YuoCore.Persistence.Hosting.csproj -c Release --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

结果：

```text
Architecture focused tests: Passed 14
Pack smoke: YuoCore.Ecs.0.1.0.nupkg/snupkg created
Pack smoke: YuoCore.Client.0.1.0.nupkg/snupkg created
Pack smoke: YuoCore.Persistence.Hosting.0.1.0.nupkg/snupkg created
Full build: Build succeeded, 0 warnings, 0 errors
Full tests: Passed all test projects
```

## 2026-06-08：阶段性 1w quick load 复测

本轮在以下改动后运行 quick load suite：

```text
Actor/Gateway 错误契约
Location lock token 与 token 化迁移
Persistence CompactOutboxOnStop
NuGet pack smoke
阶段性 C# full build/test
```

验证：

```bash
scripts/run-local-load-suite.sh --quick
```

产物：

```text
artifacts/load/20260608T152921Z-quick/local-composite-count.json
artifacts/load/20260608T152921Z-quick/ecs-shape.json
```

local-composite 结果：

```text
players=10,000 stateEntities=10,000 ticks=5 shards=4
tick-ms avg=76.113 p50=65.845 p95=129.945 max=129.945
actor-enqueue p95=49.992
runtime-tick p95=12.190
statesync-delta p95=7.806
aoi-query p95=37.753
persistence p95=22.234
allocated=30.85MB heap-after=43.79MB gc0=3 gc1=3 gc2=1
```

ecs-shape 结果：

```text
entities=10,000 components=100 systems=100 ticks=5 shards=4
setup=2,011.565ms
tick-ms avg=122.964 p50=125.284 p95=144.810 max=144.810
allocated=331.79MB heap-after=213.38MB gc0=23 gc1=22 gc2=4
```

判断：

```text
quick load 通过当前阈值。
local-composite p95 与此前 125ms 左右的 quick 结果同量级，没有发现错误契约、Location token 或 Persistence compact API 带来明显热路径退化。
ecs-shape p95 约 145ms，仍处于此前 100 组件 / 100 system quick 波动区间。
当前不做新的 ECS 调度优化；100 组件 / 100 system 的长期优化仍需单独设计预编译执行器、组件组合缓存或 system 批调度，不能破坏异常隔离和动态组件语义。
这仍是 5 tick quick，不是商业级长期 soak。
```

## 2026-06-08：阶段性 NuGet pack smoke

本轮在以下 API/模块边界变化后重新运行 pack smoke：

```text
YuoCore.Abstractions: YuoException / YuoError.BadRequest
YuoCore.Actor: ActorError.ErrorCode / ActorErrorCodes
YuoCore.Location: LockToken 与 token 化 Unlock/Migrate
YuoCore.Persistence.Hosting: CompactOutboxOnStop
```

验证：

```bash
scripts/run-pack-smoke.sh
```

结果：

```text
packages=15
pack-smoke-output=artifacts/packages/pack-smoke-20260608T152435Z
```

判断：

```text
15 个代表性可发布包均生成成功。
pack smoke 的 nuspec 禁止依赖扫描通过，未发现 Backend、ASP.NET Core、Redis、Npgsql、Dapper、Serilog、OpenTelemetry 等上层依赖污染核心/客户端/可选库。
artifacts/packages 已被 .gitignore 忽略，本轮不提交生成包。
```

## 2026-06-08：阶段性 C# 全量 build/test

本轮在以下跨模块能力完成后做阶段性全量验证：

```text
ActorError.ErrorCode / ActorErrorCodes
YuoException(YuoError) 与 Gateway pipeline 业务错误保留
Location lock token API
YuoActorLocationMigrationCoordinator token 化迁移
Persistence CompactOutboxOnStop
Web TypeScript MMO facade 另行完成 TS typecheck/smoke
```

验证：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
```

结果：

```text
Full build: Build succeeded, 0 warning, 0 error
Full tests: Passed all test projects
```

说明：

```text
该验证证明本轮 C# 接口扩展没有破坏现有项目编译和测试。
它不是性能压测，也不替代后续 1w 玩家/100 组件/100 system 的 LoadTests。
```

## 2026-06-08：Persistence Stop 后可选 compact outbox

本轮给 `YuoPersistenceWriterOptions` 增加：

```text
CompactOutboxOnStop
```

语义：

```text
默认 false，避免停服时突然增加文件 IO。
启用后，YuoPersistenceBackgroundService.StopAsync 会先按 DrainOnStop drain 队列，再调用 IYuoPersistenceOutbox.CompactAsync。
Compact 只清理已经 ack-through 的 outbox entry；失败 sequence 以及失败之后的 entry 仍会保留，避免数据丢失。
```

职责边界：

```text
该能力位于 YuoCore.Persistence.Hosting 可选包。
YuoCore.Persistence 只提供 outbox 抽象和文件实现，不依赖 Microsoft.Extensions.Hosting。
是否在停服时 compact 由宿主显式配置，不影响单机/Unity 只使用基础 Persistence 抽象的场景。
```

验证：

```bash
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
```

结果：

```text
Persistence.Hosting focused tests: Passed 13
Persistence focused tests: Passed 17
```

备注：

```text
本轮并行跑两个测试项目时，基础 Persistence 测试构建阶段出现一次 MSB3026 文件占用重试 warning，随后自动重试成功，测试通过。
```

## 2026-06-08：Location lock token 兼容式增强

本轮增强 `YuoCore.Location` 的迁移锁语义：

```text
YuoLocationEntry.LockToken
IYuoLocationRegistry.UnlockAsync(key, owner, lockToken, ...)
IYuoLocationRegistry.MigrateAsync(key, owner, lockToken, expectedAddress, newAddress, ...)
InMemoryYuoLocationRegistry LockAsync 自动生成 lock token。
CachingYuoLocationRegistry 透传 token API 并在成功后刷新缓存。
```

设计边界：

```text
旧 owner-only UnlockAsync / MigrateAsync 保留，避免打断已有调用。
新 token 重载用于正式迁移流程，防止同一个 owner 的旧流程误解锁或误迁移当前锁。
锁过期、解锁、迁移成功都会清理 LockToken。
这仍是进程内 Location 行为基准；Redis/PostgreSQL 分布式实现需要用同样语义落库。
```

验证：

```bash
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
```

结果：

```text
Location focused tests: Passed 19
Actor focused tests: Passed 45
```

## 2026-06-08：Actor Location 迁移协调器切换 token 路径

本轮把 `YuoActorLocationMigrationCoordinator` 的标准流程从 owner-only 迁移切到 token 化迁移：

```text
LockAsync(key, owner, ttl)
  -> 读取 YuoLocationEntry.LockToken
  -> MigrateAsync(key, owner, lockToken, expectedAddress, newAddress)
  -> 成功后失效本进程 sender cache
```

失败清理也改为：

```text
UnlockAsync(key, owner, lockToken)
```

设计边界：

```text
Coordinator 是 Actor/Location 的本进程标准迁移流程，适合服务端单进程/同机多 Fiber 使用。
如果某个 Location 实现 Lock 成功但没有返回 lock token，Coordinator 会 best-effort owner-only unlock 后返回失败，避免长时间残留锁。
分布式广播、跨进程 fencing 日志和 Redis/PostgreSQL 原子 token 校验仍属于可选分布式实现。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --disable-build-servers
```

结果：

```text
Actor focused tests: Passed 47
Location focused tests: Passed 19
```

## 2026-06-08：Actor 稳定错误契约

本轮增强 `YuoCore.Actor` 的错误模型：

```text
ActorError.Code：保留 HTTP-like 数字大类，便于快速判断 400 / 404 / 408 / 409 / 500。
ActorError.ErrorCode：新增稳定字符串错误码，便于客户端协议、日志、指标和跨进程消息使用。
ActorErrorCodes：集中定义 bad_request / timeout / not_found / conflict / business_rule / handler_failed / internal_error。
ActorError.Business(...)：用于 handler 主动返回业务规则错误。
ActorError.Internal(...)：用于框架内部状态不一致或类型错配。
```

职责边界：

```text
该能力只在 YuoCore.Actor 核心包内，不依赖 Protocol、Gateway、ASP.NET Core、WebSocket 或数据库。
ActorFiber 原本就会把 ActorException 原样转换成 ActorEnvelope.Failure；本轮只是让业务错误拥有稳定字符串码。
普通 handler 异常仍会被 ActorError.HandlerFailed 包装成 500，保持 system/handler 异常隔离。
```

示例：

```csharp
throw new ActorException(ActorError.Business(
    "wallet.insufficient_gold",
    "Wallet does not have enough gold."));
```

调用方收到：

```text
Code = 400
ErrorCode = wallet.insufficient_gold
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
```

结果：

```text
Actor focused tests: Passed 45
```

## 2026-06-08：Gateway 保留通用业务错误码

本轮新增 `YuoException`：

```text
YuoException(YuoError)
YuoException(YuoError, Exception)
YuoError.BadRequest(...)
```

语义：

```text
YuoException 表示跨模块可预期业务失败。
Gateway pipeline 捕获 YuoException 时，会把其中的 YuoError 原样放进 YuoGatewayOutboundMessage.Error。
普通异常仍会被包装成 YuoError.Failed("gateway dispatch failed.", detail)，避免程序错误伪装成业务错误。
```

职责边界：

```text
YuoException 位于 YuoCore.Abstractions，不依赖 Actor、Gateway、Protocol、ASP.NET Core 或数据库。
YuoCore.Gateway 只依赖 Abstractions，不捕获 ActorException，也不需要知道 ActorError。
Actor 层仍使用 ActorException/ActorError；如果某个宿主要把 Actor 错误转为 Gateway 错误，应在宿主适配层显式转换。
```

验证：

```bash
dotnet build src/YuoCore.Abstractions/YuoCore.Abstractions.csproj --disable-build-servers
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj --disable-build-servers
```

结果：

```text
Abstractions build: Succeeded netstandard2.1 / net10.0
Gateway focused tests: Passed 11
```

## 2026-06-08：Web TypeScript MMO 客户端 facade

本轮补齐 `clients/web/yuocore-client-ts` 的示例 MMO 调用面：

```text
YuoMmoClient.login
YuoMmoClient.click
YuoMmoClient.getSnapshot
YuoMmoClient.enterMap
YuoMmoClient.move
YuoMmoClient.disconnect
YuoMmoClient.requestStateSnapshot
```

职责边界：

```text
Web TS SDK 只依赖 envelope、opcode、业务 protobuf DTO 的最小 codec 和 transport 抽象。
客户端仍然只看到 playerId、mapId、inputSeq、坐标、Snapshot/Delta 等外部协议数据。
不暴露 ActorId、FiberId、LocationKey、YuoEntity、Gateway session 内部对象或 Backend 类型。
不引入 ASP.NET Core、数据库、Redis、Actor、Runtime、ECS 依赖。
```

实现说明：

```text
新增 decodeClientEnvelopeV1，和 encodeClientEnvelopeV1 对称，便于本地 loopback transport、测试和开发期诊断。
YuoMmoClient 是薄封装，内部仍走 YuoClientSession.call，因此 requestId/clientSeq、timeout、error response 和 opcode mismatch 语义一致。
当前 TypeScript 业务 codec 是手写的最小示例实现，只覆盖现有 yuocore.proto 的非负数值路径；正式项目仍应接入 proto -> TypeScript codegen。
```

验证：

```bash
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

结果：

```text
TypeScript typecheck: Passed
yuocore-client-ts smoke: Passed
```

smoke 覆盖：

```text
通用 YuoClientSession request/response。
StateSnapshot / StateDelta Mirror 应用与 gap 检测。
YuoMmoClient 通过 loopback transport 完成 login/click/snapshot/enterMap/move/disconnect/stateSnapshot。
loopback transport 会解码客户端 envelope 和业务 request，再按 opcode 返回对应 server envelope。
```

## 2026-06-08：StateSync 纯模型拆出 Abstractions

本轮把客户端和服务端共享的纯状态同步能力从 `YuoCore.StateSync` 拆到 `YuoCore.StateSync.Abstractions`：

```text
YuoEntityState / YuoStateSnapshot
YuoStateDelta / YuoStateChange
YuoReplicatedStateTable
YuoStateSyncMirror
YuoStateDeltaCompressor
YuoGridAoiQuery / YuoGridAoiTargetIndex
YuoAoiVisibilityTracker
```

新的职责边界：

```text
YuoCore.StateSync.Abstractions 不依赖 ECS、Protocol、Gateway、网络或服务端宿主。
YuoCore.StateSync 保留 NetworkIdentity、NetworkState、AoiComponent、Entity snapshot builder 和 Entity AOI query。
YuoCore.StateSync.Protocol 改为依赖 StateSync.Abstractions + Protocol，不再依赖 ECS 适配包。
```

兼容目标：

```text
YuoCore.StateSync.Abstractions: net10.0;netstandard2.1
YuoCore.StateSync.Protocol: net10.0;netstandard2.1
YuoCore.StateSync: net10.0
```

性能影响：

```text
这是包边界拆分，不改变 AOI 排序语义、Delta 合并规则或 Mirror 缺口处理。
协议桥为了 netstandard2.1 兼容使用 ByteString.CopyFrom(byte[])，会在 payload 转换处显式复制；该复制属于协议序列化边界，不能替代热路径里的状态缓存。
```

验证：

```bash
dotnet build src/YuoCore.StateSync.Abstractions/YuoCore.StateSync.Abstractions.csproj --disable-build-servers
dotnet build src/YuoCore.StateSync.Protocol/YuoCore.StateSync.Protocol.csproj --disable-build-servers
dotnet build src/YuoCore.StateSync/YuoCore.StateSync.csproj --disable-build-servers
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.StateSync.Protocol.Tests/YuoCore.StateSync.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
```

当前结果：

```text
StateSync.Abstractions build: passed
StateSync.Protocol build: passed
StateSync build: passed
StateSync tests: Passed 54
StateSync.Protocol tests: Passed 3
Architecture tests: Passed 15
```

## 2026-06-08：C# 客户端 StateSync 推送接收器

本轮新增 `YuoCore.Client.StateSync` 可选包，用于 Unity/C# 客户端处理服务端主动推送的状态同步消息：

```text
YuoClientStateSyncReceiver
YuoClientStateSyncReceiveResult
YuoClientStateSyncMessageKind
```

职责边界：

```text
输入：YuoServerEnvelopeV1 或其二进制 bytes。
识别：YuoCoreProtocolOpCodes.StateSnapshot / StateDelta。
转换：StateSnapshotMessage / StateDeltaMessage -> YuoStateSnapshot / YuoStateDelta。
应用：调用 YuoStateSyncMirror.ApplySnapshot / ApplyDelta。
忽略：非 StateSync opcode 返回 Ignored，让调用方继续交给其他业务分发器。
```

边界控制：

```text
YuoCore.Client.StateSync 不引用 ECS、Gateway、Actor、Runtime、Backend、数据库或网络实现。
Unity 需要 C# 客户端 Mirror 时导入 Client + Client.StateSync + Unity transport。
如果客户端已有自定义消息分发器，可以只导入 StateSync.Protocol 和 StateSync.Abstractions。
```

性能影响：

```text
这是客户端分发能力，不改变服务端热路径。
每次推送会做一次 server envelope parse 和 protobuf payload parse；这属于客户端协议边界成本。
后续如果 Unity 高频状态包成为热点，再考虑批量 envelope 或 Span/池化 decode，但不能把网络实现塞进 StateSync 纯层。
```

验证：

```bash
dotnet build src/YuoCore.Client.StateSync/YuoCore.Client.StateSync.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
```

当前结果：

```text
Client.StateSync focused tests: Passed 5
```

## 2026-06-08：Actor.Ecs handler 扫描报告

本轮增强 `YuoActorHandlerRegistry` 的启动期扫描能力：

```text
YuoActorHandlerScanReport
AddFromAssemblies(...)
AddFromAssemblyWithReport(...)
AddFromAssembliesWithReport(...)
```

职责边界：

```text
YuoCore.Actor 仍然只保留纯 Actor 路由、信封、transport 和 LocationSender。
YuoCore.Actor.Ecs 负责把 Actor 消息入口绑定到 YuoEntity，并提供 ECS handler 的反射扫描。
扫描只建议在启动阶段执行；dispatcher 热路径仍然只是 Type -> handler 字典查询。
```

为什么需要报告：

```text
无参 handler 可以直接通过 AddFromAssembly 注册。
需要 DI/factory 的 handler 会被跳过，报告会列出 SkippedHandlerTypes。
ServerHost 后续自动装配时可以把报告写入日志/启动诊断，避免“handler 没注册但启动没提示”的问题。
```

性能影响：

```text
只增加启动期反射扫描统计，不改变消息分发热路径。
运行中不要热修改 handler registry，避免 dispatcher 读路径被迫加锁。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
```

当前结果：

```text
Actor focused tests: Passed 44
```

## 2026-06-08：ServerHost 接入 Actor handler 扫描

本轮把上一节的 handler 扫描报告接入 `YuoServerHost`：

```text
YuoServerHost.AddActorHandlersFromAssembly(...)
YuoServerHost.AddActorHandlersFromAssemblies(...)
YuoServerHost.ActorHandlerScanReports
```

职责边界：

```text
ServerHost 负责进程级装配，可以引用 Actor.Ecs。
Actor.Core 仍不引用 Ecs，也不关心 handler 扫描。
业务可以在 Start() 前扫描 Game/Backend 程序集；Start() 后禁止再修改 handler registry。
```

为什么要禁止 Start 后扫描：

```text
dispatcher 热路径依赖无锁 Type -> handler 字典查询。
启动后动态注册 handler 会让多 Fiber 并发读写边界变复杂。
需要热更新 handler 时应作为单独能力设计，不能混进基础 ServerHost。
```

验证：

```bash
dotnet test tests/YuoCore.Server.Tests/YuoCore.Server.Tests.csproj --disable-build-servers
```

当前结果：

```text
Server focused tests: Passed 5
```

## 2026-06-08：Web TypeScript SDK StateSync Mirror

本轮增强 `clients/web/yuocore-client-ts`：

```text
YuoStateSyncMirror
YuoStateSyncReceiver
decodeStateSnapshotMessage
decodeStateDeltaMessage
YuoNetworkEntityState / YuoStateSnapshot / YuoStateDelta
```

职责边界：

```text
TypeScript SDK 负责 V1 envelope、request/response transport 和通用 StateSync Snapshot/Delta 应用。
SDK 不绑定业务 protobuf 生成代码，不解析 payload 内部业务结构，不创建 UI 或渲染对象。
payload 仍是 Uint8Array，由业务层决定如何反序列化和映射到表现对象。
```

与 C# 客户端对齐：

```text
C# 侧有 YuoCore.Client.StateSync。
Web 侧有 YuoStateSyncReceiver。
两边都处理 StateSnapshot / StateDelta opcode，并在 BaseTick 缺口时进入 snapshotRequired 状态。
```

验证：

```bash
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

当前结果：

```text
TypeScript typecheck: passed
TypeScript smoke: passed
```

## 2026-06-08：Outbox ack 不再越过失败 sequence

发现的问题：

```text
YuoPersistenceBackgroundService 在 StopOnCommandError=false 时会继续处理后续命令。
如果 outbox sequence 1 写业务存储失败，但 sequence 2 成功，旧逻辑会 ack through 2。
重启 replay 时 sequence 1 会被跳过，造成持久化数据丢失。
```

修复：

```text
YuoPersistenceBackgroundService 记录第一个失败的 outbox sequence。
一旦 sequence N 失败，后续 sequence >= N 即使业务写入成功，也不会执行 AcknowledgeThroughAsync。
这样重启 replay 会至少重放失败命令和其后的成功命令。
```

取舍：

```text
该策略可能导致后续已成功命令在重启后重复写入。
这是 outbox/WAL 的正确保守语义：宁可重复，不可丢失。
业务存储层应尽量做幂等写入，或者后续引入更精细的 per-entry ack/compaction 结构。
```

验证：

```bash
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
```

当前结果：

```text
Persistence.Hosting focused tests: Passed 12
```

## 2026-06-08：Location 锁阻止删除

发现的问题：

```text
InMemoryYuoLocationRegistry 的 Lock 可以阻止 Register/Migrate 冲突，但 RemoveAsync 没有检查 IsLocked。
迁移期间如果旧流程误调用 RemoveAsync，可能删除仍在迁移保护中的 location。
```

修复：

```text
RemoveAsync 在锁未过期时返回 false。
锁过期后会先 ClearExpiredLockIfNeeded，再允许删除。
```

语义：

```text
Location lock 表示“该 key 的所有权正在迁移或被保护”。
Register、Migrate、Remove 都属于写操作，必须尊重锁。
当前 Remove API 没有 owner 参数，因此不能允许锁持有者删除；需要 owner-aware remove 时应新增显式 API。
```

验证：

```bash
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --disable-build-servers
```

当前结果：

```text
Location focused tests: Passed 17
```

边界：

```text
本轮只补库发布规则，不把 Backend、数据库、Redis、ASP.NET Core 或 WebSocket 依赖下沉到核心包。
artifacts/ 是本地 pack 输出目录，已加入 .gitignore。
```

## 2026-06-08：Actor Handler 工厂扫描与业务错误码

本轮补齐 Actor handler 注册能力：

```text
YuoActorHandlerRegistry.AddFromAssembly(Assembly, Func<Type, IYuoActorHandler?>) 支持宿主工厂。
默认 AddFromAssembly(Assembly) 继续只创建无参 handler。
ActorError.BadRequest 使用 400 表达业务参数错误。
Backend GameRuntime 已使用工厂扫描注册依赖 runtime 的 handler。
```

性能影响：

```text
扫描只在启动阶段执行。
运行时 handler 查找仍是 Dictionary<Type, IYuoActorHandler> 查询。
没有引入反射 dispatch、DI 容器或每消息分配。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Actor focused tests: Passed 42
Backend focused tests: Passed 16
```

## 2026-06-08：Backend MMO 示例闭环第一版

本轮补齐示例宿主的 MMO 最小闭环：

```text
登录/重连：LoginPlayer 绑定 session，并保留玩家原有地图和位置。
进地图：TryEnterMap 通过 Actor 消息进入目标 Fiber mailbox 后修改 Presence。
移动：TryMove 通过 Actor 消息更新 Transform，并用 inputSeq 拒绝旧输入。
广播：同 map 在线玩家拥有独立事件队列，可通过 DrainEvents 拉取 enter/move/leave。
断线：TryDisconnect 校验 session 后标记离线并广播 leave。
保存恢复：PlayerSnapshotDto、PostgreSQL schema、Redis JSON 缓存都覆盖 map/position/online/inputSeq。
协议：Protobuf v1 opcode 增加 EnterMap、Move、Disconnect、WorldSnapshot。
```

性能影响：

```text
本轮先保证职责边界和行为闭环。
TryEnterMap/TryMove 为了同步返回 WorldSnapshot，会立即 Tick 目标 shard 一次；这适合示例和测试，不应作为高频商业移动最终形态。
广播当前按 map 内玩家线性扫描，适合小规模示例；大地图商业项目需要接入 YuoCore.StateSync 的 AOI 查询和 delta 压缩。
事件队列按玩家内存保存，后续需要容量上限、背压和断线期间保留策略。
```

后续需要继续抽象：

```text
把 Backend 内的 MMO runtime 能力沉淀到可选 YuoCore.Game/YuoCore.Server 适配层。
将 Gateway 响应从“同步 WorldSnapshot”演进为 command accepted + tick broadcast。
用 StateSync/AOI 代替当前 map 全量玩家扫描。
把持久化可靠 drain/outbox/WAL 作为下一批能力继续实现。
```

验证：

```bash
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

结果：

```text
Protocol focused tests: Passed 22
Backend focused tests: Passed 16
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Persistence StopAsync drain

本轮增强持久化后台写回的停服行为：

```text
YuoPersistenceQueue.TryDequeueBatch(maxCount)
YuoPersistenceBackgroundService.StopAsync(...)
YuoPersistenceWriterOptions.DrainOnStop
YuoPersistenceWriterOptions.MaxStopDrainBatches
```

行为：

```text
正常运行时仍由 ExecuteAsync 等待 DequeueBatchAsync。
StopAsync 触发后，先让 BackgroundService 的执行循环退出，再非阻塞 drain 当前队列里已到达的命令。
drain 复用原有写入、重试和 failure sink 逻辑。
MaxStopDrainBatches 限制 drain 上限，避免停服期间持续写入导致进程无法退出。
```

性能影响：

```text
正常热路径不变：EnqueueAsync、DequeueBatchAsync、后台批处理循环没有新增额外等待。
TryDequeueBatch 只在 StopAsync 或显式调用时使用。
停止路径会按已到达命令数量执行同步 drain，属于停服可靠性成本，不进入 Tick 热路径。
```

剩余风险：

```text
这不是 durable outbox/WAL。
如果进程崩溃、容器被强杀或机器断电，内存队列仍然会丢失。
商业级可靠保存还需要 PostgreSQL outbox、append-only 文件 WAL 或 Redis stream 等可选实现，并配套启动恢复和幂等写入。
```

验证：

```bash
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

结果：

```text
Persistence focused tests: Passed 10
Persistence.Hosting focused tests: Passed 10
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：YuoCore.Client 协议 SDK 雏形

本轮新增可选客户端协议 SDK：

```text
YuoCore.Client
IYuoClientTransport
YuoClientSession
YuoClientOutboundMessage
YuoClientInboundMessage<TResponse>
YuoClientProtocolException
```

能力：

```text
EncodeRequest<TRequest> 根据 YuoCoreProtocolCatalog 查 opcode，生成 YuoClientEnvelopeV1。
CallAsync<TRequest,TResponse> 通过抽象 transport 发 bytes，并校验 request/response 类型契约。
DecodeResponse<TResponse> 解析 YuoServerEnvelopeV1，校验 requestId 和 opcode，服务端 ErrorResponse 转 YuoClientProtocolException。
```

Unity/Web 边界：

```text
YuoCore.Abstractions / YuoCore.Protocol / YuoCore.Client 已多目标 net10.0;netstandard2.1。
YuoCore.Client 不实现具体 WebSocket，不引用 Gateway/Backend。
Unity 可以用该 C# 包；Web TypeScript SDK 后续按相同 envelope/opcode/requestId/clientSeq 语义实现。
```

性能影响：

```text
服务端热路径无变化。
客户端每次请求会创建 protobuf envelope bytes，属于客户端协议开销。
该包不进入 ECS Tick、Actor mailbox、LocationSender 或 Persistence 热路径。
```

剩余工作：

```text
还没有正式 TypeScript SDK。
还没有 Unity WebSocket/TCP 适配实现。
还没有客户端自动重连、心跳、pending request 超时管理和状态同步 mirror 聚合入口。
```

验证：

```bash
dotnet build src/YuoCore.Client/YuoCore.Client.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

结果：

```text
Client multi-target build: Build succeeded
Client focused tests: Passed 4
Protocol focused tests: Passed 22
Architecture focused tests: Passed 11
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：1w 综合 MMO 短压入口

本轮扩展 `tools/YuoCore.LoadTests`：

```text
原有 WebSocket 压测模式保留。
新增 --mode local-composite。
新增 --aoi-result-mode sorted|count，默认 sorted。
local-composite 在本进程内同时压：
  GameRuntime Actor click command
  Runtime 多 shard Tick
  StateSync dirty delta
  StateSync AOI grid query
  PersistenceQueue enqueue + TryDequeueBatch drain
```

默认长期入口：

```bash
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 20 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000
```

本轮短测命令：

```bash
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode sorted
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count
dotnet run -c Release --project benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -- --perf-smoke --entities 10000 --ticks 5 --shards 4
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

短测结果：

```text
local-composite sorted:
tick-ms avg=153.522 p50=130.219 p95=281.959 max=281.959
segment-ms actor-enqueue avg=22.916 p95=43.705
segment-ms runtime-tick avg=9.018 p95=11.450
segment-ms statesync-delta avg=5.515 p95=9.969
segment-ms aoi-query avg=108.223 p95=196.821
segment-ms aoi-project avg=4.095 p95=11.588
segment-ms aoi-index avg=1.472 p95=3.373
segment-ms aoi-observers avg=102.524 p95=181.208
segment-ms persistence avg=7.845 p95=20.005
actor-commands=50,000 processed=50,000 players=10,000
state-changes=50,000 visible-results=1,130,650
persistence-drained=50,000 queue-pending=0 allocated-mb=72.28

local-composite count:
tick-ms avg=81.258 p50=75.745 p95=131.108 max=131.108
segment-ms aoi-query avg=26.566 p95=40.682
segment-ms aoi-observers avg=20.210 p95=23.747
allocated-mb=32.38

100组件/100system perf-smoke:
tick-ms avg=116.619 p50=118.035 p95=131.193 max=131.193
entity-tick/s=85,749

Full build: Build succeeded
Full tests: Passed all test projects
```

性能判断：

```text
这只是短测，不是长期 soak，也不是商业级承诺。
当前 sorted 语义下，1w 综合路径 p95 约 282ms；主要热点是 AOI observer 查询里的完整可见列表构建和排序。
新增 YuoGridAoiTargetIndex.CountVisible 只用于只需要数量/阈值的路径，不改变 QueryVisible 的稳定排序语义。
count 旁路下 1w 综合路径 p95 约 131ms，说明列表分配和排序是当前 AOI 高压场景的大头。
如果要把实时 MMO 的 AOI 做到商业级，应继续做订阅表 / enter-leave 增量 / 脏 cell 更新，而不是直接删除 QueryVisible 排序。
100组件/100system shape 本轮 p95 约 131ms；仍需长期 soak 和更真实业务 system 才能作为商业级承诺。
```

边界：

```text
LoadTests 是工具项目，可以引用 Backend/Game/StateSync/Persistence。
核心 Ecs/Runtime/Actor 不新增上层依赖。
```

后续建议：

```text
local-composite 已拆出 Actor enqueue、Runtime tick、StateSync delta、AOI project/index/observer、Persistence 分段耗时。
local-composite 已支持 JSON 输出和 p95/max 阈值失败。
长期 soak 仍应增加 duration 模式、CSV 输出、GC/线程池指标和 Prometheus 对照。
AOI 下一步应优先设计可选的 MapFiber AOI 订阅表，不应在没有裁定时改变 QueryVisible 默认排序语义。
```

## 已落地

### Actor 消息入口

已新增：

- `YuoActorHandler<TMessage>`
- `YuoActorRequestHandler<TRequest, TResponse>`
- `YuoActorHandlerRegistry`
- `YuoEntityActorRegistry`
- `YuoEntityActorDispatcher`

当前语义：

```text
ActorEnvelope
  -> ActorId 找 YuoEntity
  -> Message 类型找 Handler
  -> Handler 在目标执行上下文内直接操作 ECS Component
```

这对应 ET 的：

```text
MessageLocationHandler<Unit, Request, Response>
```

区别是 YuoCore 没把它混进 `YuoSystem.Run`，而是保留为独立消息入口。

### Fiber 内 Actor endpoint

已新增：

- `YuoFiberActorEndpoint`

作用：

```text
Actor 消息不在独立 Task 里改 ECS。
Actor 消息排入目标 YuoFiber mailbox。
目标 YuoFiber Tick 时先 drain mailbox，再执行 YuoWorld.Update。
```

这是当前最关键的线程安全边界。

### Actor 超时

`ActorRouter.InvokeAsync` 已增加 timeout 重载。

语义：

```text
调用方超时
  -> 抛 ActorException
  -> ErrorCode 408
```

目标 Fiber 如果后续才处理完，不能覆盖调用方的超时结果。

### RpcId

`ActorEnvelope` 已增加递增 `RpcId`。

语义：

```text
Request 生成 RpcId
Success / Failure 保留原 RpcId
```

`RequestId` 仍保留为外部关联 ID；`RpcId` 用于框架内部请求-响应匹配语义。

### Backend command loop 接入 Actor

`GameRuntime.TryApplyClick` 保留原同步直接逻辑，用于现有 API 和测试。

新增：

- `GameRuntime.TryEnqueueClick`
- `ApplyClickActorMessage`
- `ApplyClickActorHandler`

`GameLoopService` 已改为：

```text
ClickCommand
  -> TryEnqueueClick
  -> ActorRouter.Send
  -> YuoFiberActorEndpoint
  -> 目标 GameShard mailbox
  -> Tick 时修改组件
```

这样后台命令路径已经走消息机制，不再从 command loop 直接改玩家组件。

### Backend command queue request/response

本轮继续把 Backend 示例宿主的命令边界拆清楚：

```text
YuoCore.Game 仍只保留纯 YuoGameCommand DTO，不引用 Channel / TaskCompletionSource / ASP.NET。
YuoCore.Backend.GameCommandQueue 新增 ExecuteAsync(command)，用于 Gateway 等待业务结果。
YuoQueuedGameCommand 包装 command + 可选 completion，fire-and-forget 和 request/response 共用同一个 bounded Channel。
GameCommandProcessor 集中处理 YuoGameCommand -> GameRuntime，不再把业务 switch 写在 GameLoopService 里。
GameLoopService 只负责定时 drain 队列、调用 processor、回填 result，然后 tick runtime。
```

当前 Gateway 示例行为：

```text
GameOptions.GatewayRequestsUseCommandQueue=true 时，Click / EnterMap / Move / Disconnect 通过 GameCommandQueue.ExecuteAsync 等待 GameLoopService 处理。
未传 IOptions<GameOptions> 的轻量测试/无宿主用法仍走 GameRuntime 直接路径，避免没有后台循环时挂起。
Click 的等待结果会在本轮 runtime.Tick 后完成，返回已经应用本次 click 的 PlayerSnapshot。
未启用队列模式的兼容路径仍保持 fire-and-forget 入队并返回当前 snapshot。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 29
```

### Location 缓存与失效重试

已新增：

- `CachingYuoLocationRegistry`
- `IYuoLocationCacheInvalidator`
- `IActorLocationInvalidator`

当前语义：

```text
首次 Resolve：
  读 inner registry
  写入本地缓存

后续 Resolve：
  优先读缓存

投递失败 NotFound：
  ActorRouter 失效旧地址缓存
  重新 Resolve
  如果地址变化则重试一次
```

这对应 ET 的 ActorLocationSender 缓存策略：

```text
平时不每次查 Location。
发送失败后再重新查。
```

### 进程内 / 跨进程传输边界

已新增：

- `ProcessAwareYuoTransport`
- `YuoTransportException`
- `YuoProtocolEnvelopeCodec`
- `SerializedYuoTransport`

当前语义：

```text
address.ProcessId == localProcessId
  -> localTransport

address.ProcessId != localProcessId
  -> remoteTransport

remoteTransport 未配置
  -> 明确抛 YuoTransportException
```

这里还不是完整 TCP/QUIC/KCP 实现，但跨进程传输的选择边界已经固定。
后续真实网络层只需要实现 `IYuoTransport<YuoActorAddress>` 并注入 remoteTransport。

`SerializedYuoTransport` 会把 `YuoProtocolEnvelope` 编码成 bytes 再解码后交给底层 transport。
它用于进程内模拟远端链路，避免内存 transport 共享 payload 引用导致测试结果不符合真实跨进程语义。

### ServerHost 集成 Actor / Location / Transport

`YuoServerHost` 已增强：

- 按 `StartConfig` 启动 Fiber
- 为每个 Fiber 创建 `YuoFiberActorEndpoint`
- 持有 `ActorHandlers`
- 持有 `ActorEntities`
- 持有 `ActorLocations`
- 持有 `ActorRouter`
- 提供 `RegisterLocalEntityActorAsync`

注册本地 Entity Actor 时会同时完成：

```text
ActorId -> YuoEntity
ActorId -> ActorAddress
业务 LocationKey -> YuoActorAddress
ActorAddress -> YuoFiberActorEndpoint
```

## 验证结果

全量构建：

```text
dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.
0 Warning(s)
0 Error(s)
```

全量测试：

```text
dotnet test YuoCore.slnx --no-build
Passed: 32
Failed: 0
Skipped: 0
```

测试覆盖新增内容：

- Actor request 路由到目标 `YuoEntity`
- Actor timeout 转成 `ActorException`
- ActorEnvelope response/failure 保留 request RpcId
- `YuoFiberActorEndpoint` 在目标 Fiber mailbox 中执行 handler
- Backend queued click 在 Tick 后生效
- 原同步 click 行为不变
- 1w 玩家 seed + tick 仍通过
- Location 缓存失效后能重新读取 inner registry 新地址
- ActorRouter 遇到 stale endpoint 后能失效 Location 缓存并重试一次
- ProcessAwareTransport 能按 ProcessId 分流本地/远端 transport
- SerializedYuoTransport 能切断远端 payload 引用共享
- ServerHost 能注册本地 Entity Actor 并写入业务 Location

## 压测结果

以下都是 BenchmarkDotNet Dry 单轮结果，只用于快速定位热点，不等同正式统计报告。

### 1w 实体 + 100 组件 + 100 system + 4 Fiber 并行

命令：

```bash
dotnet run -c Release --project benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -- \
  --filter '*ParallelShape*' \
  --job Dry \
  --warmupCount 1 \
  --iterationCount 1
```

结果：

| Method | Entities | Shards | Mean | Allocated |
| --- | ---: | ---: | ---: | ---: |
| `ParallelShapeTick1` | 10000 | 4 | 139.9 ms | 5.72 MB |
| `ParallelShapeTickBatch100` | 10000 | 4 | 10.649 s | 146.09 KB |

结论：

```text
多 Fiber 分片并行可以工作。
100 组件 + 100 system 的极端 ECS 压测仍然很重。
主要压力在 ECS system 扫描、组件查找和每 tick 的系统执行量。
```

### 1w 玩家点击命令路径

命令：

```bash
dotnet run -c Release --project benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -- \
  --filter '*ActorCommand*' \
  --job Dry \
  --warmupCount 1 \
  --iterationCount 1
```

结果：

| Method | Players | Shards | Mean | Ratio | Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| `DirectApplyAndTickClicks` | 10000 | 4 | 12.34 ms | 1.00 | 2.26 KB |
| `ActorQueueAndTickClicks` | 10000 | 4 | 38.67 ms | 3.14 | 2162.50 KB |

结论：

```text
Actor 消息路径当前约为直接路径 3.1 倍。
每 1w 条点击消息约分配 2.1 MB。
这是目前最明确的消息层热点。
```

## 已直接优化

### 去掉同步桥接中的 AsTask

位置：

```text
GameRuntime.TryEnqueueClick
```

调整：

```text
ValueTask.AsTask().GetAwaiter().GetResult()
  -> ValueTask.GetAwaiter().GetResult()
```

原因：

```text
避免每条同步投递都把 ValueTask 包成 Task。
语义不变，风险低。
```

### YuoFiberActorEndpoint fire-and-forget 快速路径

位置：

```text
YuoFiberActorEndpoint.SendAsync
```

调整：

```text
避免外层 async 状态机。
同步完成的 handler 走 ValueTask.CompletedTask。
异步 handler 才进入 await 慢路径。
```

结果：

Dry 基准没有稳定改善，说明主要开销不在这两个点。

## 严重性能热点

### 热点 1：每条 Actor 消息都会产生对象和路由成本

当前路径：

```text
TryEnqueueClick
  -> new ApplyClickActorMessage
  -> ActorRouter.SendAsync
  -> Location Resolve
  -> InMemoryTransport ResolveEndpoint
  -> YuoFiberActorEndpoint.SendAsync
  -> YuoFiber.PostAsync
  -> Channel 写入 Func
  -> Tick DrainMailbox
  -> Dispatcher 查 Entity
  -> Dispatcher 查 Handler
  -> Handler 改组件
```

1w 条消息时，这些小开销会被放大。

可直接优化空间：

```text
缓存 playerId -> PlayerRef -> ActorAddress，避免每条命令查 Location。
为本进程本 shard 命令提供快速投递 API。
```

需要裁定的优化：

```text
是否引入批量消息。
是否引入无分配 mailbox item。
是否让高频同进程命令绕过 ActorRouter 的通用路径，直接按 PlayerRef 投递到 YuoFiber。
```

这些会影响 API 分层，需要你裁定。

### 热点 2：YuoFiber mailbox 当前用 Func 委托

当前 mailbox 存：

```text
Func<YuoFiber, CancellationToken, ValueTask>
```

问题：

```text
每条消息通常会捕获 envelope / dispatcher / state。
高频消息会产生委托和闭包压力。
```

可选方案：

```text
把 mailbox item 改成结构化类型：
  enum MailboxItemKind
  ActorEnvelope
  Command
  Callback
```

优点：

```text
减少闭包和委托分配。
更容易做批量 drain。
```

风险：

```text
会扩大 Runtime API 改动。
可能影响当前 PostAsync/CallAsync 简洁性。
```

建议：

```text
需要你裁定后再改。
```

### 热点 3：ECS 极端 shape 压测每 tick 成本仍高

1w 实体、100 组件、100 system 是非常重的 case。

当前结果：

```text
单 tick 159 ms
100 tick 10.515 s
```

直接优化方向：

```text
继续减少系统执行中的重复组件字典查找。
对固定组件组合生成缓存执行器。
为高频系统构建 entity/component tuple 缓存。
```

需要裁定的方向：

```text
是否接受更强的系统注册/预编译机制。
是否允许牺牲一点动态组件灵活性换取更高 tick 性能。
是否要把极端 100 组件 shape 作为主优化目标。
```

这个会改变 ECS 内核策略，不建议擅自大改。

## 当前待裁定项

1. 高频后端命令是否允许走“本进程快速路径”

当前完全 ActorRouter 路径更统一，但慢。

可选：

```text
外部/跨进程：
  ActorRouter

本进程本 shard 高频命令：
  PlayerRef -> YuoFiber.PostCommand
```

这是性能和架构纯度之间的取舍。

2. mailbox 是否从 Func 改成结构化队列项

这能减少分配，但会改 Runtime 核心 API。

3. 是否引入批量消息

例如：

```text
BatchClickMessage
  List<(ActorId, Amount)>
```

优点是吞吐高。

缺点是 handler 设计复杂，错误隔离和单 Actor 顺序语义要重新定义。

4. ECS 是否做预编译执行器

100 system shape 下收益可能大，但会让 ECS 更复杂。

## 建议下一步

优先级：

1. 保留当前正确架构，不急着牺牲语义换性能。
2. 增加更正式的 Benchmark job，不只看 Dry。
3. 针对 Actor 消息分配做 profiler/MemoryDiagnoser 深入确认。
4. 如果确认后端命令确实需要 1w+/tick 级别吞吐，再裁定 mailbox 结构化和本进程快速路径。

当前实现已经具备：

```text
正确的所有权边界
Actor 消息入口
Fiber mailbox 内执行
Backend command loop 接入
全量测试通过
基础压测数据
```

## 2026-06-08 快速多 shard shape 压测

本轮新增 `YuoCore.Benchmarks --perf-smoke`，用于不启动完整 BenchmarkDotNet job 的快速回归。

命令：

```bash
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

覆盖：

```text
10,000 实体
100 个组件
100 个 system
4 个 YuoFiber / YuoWorld shard
TickParallelAsync 多 shard 调度
```

结果：

```text
tick-ms: avg=111.402 p50=104.248 p95=146.043 max=159.925
entity-tick/s: 89,765
```

判断：

```text
这是极端 ECS shape 压测，不等同真实放置挂机业务负载。
它适合用于观察 ECS system 扫描、组件查找和多 shard 调度的趋势。
当前 p95 超过 100ms，说明如果真实业务接近这个复杂度，需要继续研究预编译执行器、组件组合缓存或更细粒度的系统调度。
```

## 2026-06-08 复测：多 shard shape 压测

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

结果：

```text
tick-ms: avg=111.940 p50=105.666 p95=145.135 max=152.458
entity-tick/s: 89,333
```

判断：

```text
本次复测与上一轮 avg=111.402ms / p95=146.043ms 基本一致，没有发现近期 Actor、Analyzer、Persistence 辅助能力导致 ECS shape 压测退化。
性能热点判断仍不变：极端 100 组件 + 100 system shape 下，主要风险在 ECS system 扫描、组件组合查找和后续是否引入预编译执行器。
当前没有足够证据支持牺牲 ECS 动态能力或 System 异常隔离语义来换取激进优化。
```

## 2026-06-08 复测：取消语义修复后多 shard shape 压测

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

结果：

```text
tick-ms: avg=109.666 p50=99.599 p95=146.922 max=167.084
entity-tick/s: 91,186
```

判断：

```text
本次复测在 Location / Actor / Network / Gateway / Backend / Runtime 取消语义修复之后执行。
结果与上一轮 avg=111.940ms / p95=145.135ms / 89,333 entity-tick/s 处于同一水平，没有看到取消契约修复造成 ECS shape 压测退化。
p95 仍约 145ms，极端 100 组件 + 100 system shape 的核心热点判断不变：后续若要质变，需要研究预编译执行器、组件组合缓存或系统调度结构，而不是牺牲 System 异常隔离。
```

## 2026-06-08 复测：架构护栏与协议 catalog 后多 shard shape 压测

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

结果：

```text
tick-ms: avg=100.171 p50=93.492 p95=140.918 max=148.759
entity-tick/s: 99,830
```

判断：

```text
本次复测发生在 Gateway/ECS Analyzer 护栏和 Protocol opcode catalog 落地之后。
这些改动都不在 ECS Tick 热路径上，结果没有出现退化，当前数值还略好于上一轮 109.666ms / 91,186 entity-tick/s。
仍不能把这次单次 smoke 当作质变优化；核心热点判断不变：极端 100 组件 + 100 system shape 下，后续质变仍依赖预编译执行器、组件组合缓存或系统调度结构。
```

## 2026-06-08 复测：Runtime mailbox 出队取消修复后多 shard shape 压测

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

结果：

```text
tick-ms: avg=106.097 p50=94.236 p95=164.749 max=170.755
entity-tick/s: 94,253
```

判断：

```text
本次复测发生在 YuoFiber.DrainMailboxAsync 改为出队前检查取消之后。
该改动是正确性修复，不是吞吐优化；结果与上一轮 100.171ms / 99,830 entity-tick/s 处于同一量级，没有看到数量级退化。
p95 和 max 更高，说明单次 smoke 仍存在明显波动，不能作为正式性能结论。
后续如果继续优化 Runtime mailbox，应单独设计结构化 mailbox item、批量 drain 或高频同机消息快速路径，并保护 Post/Call 的取消语义和异常传播语义。
```

## 2026-06-08 复测：Runtime.Actor 拆包与 Gateway 取消修复后多 shard shape 压测

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

结果：

```text
tick-ms: avg=103.292 p50=95.699 p95=139.928 max=154.309
entity-tick/s: 96,813
```

判断：

```text
本次复测发生在 Runtime.Actor 拆包、Runtime.Actor.Tests 独立、核心依赖护栏和 Gateway clientSeq 取消回滚修复之后。
这些改动主要影响项目边界和 Gateway 接入层，不在 ECS Tick 热路径。
结果与上一轮 106.097ms / 94,253 entity-tick/s 同量级，没有看到 ECS shape 压测退化。
```

## 2026-06-08 复测：Actor/Analyzer/StateSync 护栏后多 shard shape 压测

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 100 --shards 4
```

20 tick 连续三次结果：

```text
run1: avg=215.282 p50=203.595 p95=277.185 max=344.717 entity-tick/s=46,451
run2: avg=113.442 p50=106.394 p95=145.233 max=154.109 entity-tick/s=88,151
run3: avg=137.713 p50=123.513 p95=201.740 max=203.954 entity-tick/s=72,615
```

100 tick 长样本结果：

```text
tick-ms: avg=96.298 p50=92.551 p95=109.290 max=164.760
entity-tick/s: 103,845
```

判断：

```text
本次复测发生在 Actor timeout/stale retry 修复、LocationSender timeout/cancellation 修复、Protocol/Gateway Analyzer 护栏、AI 指南护栏和 StateSync delta 校验之后。
这些改动都不在 ECS Tick 热路径，100 tick 长样本没有看到稳定退化。
20 tick smoke 的首次结果明显偏慢，后两次回到历史区间，说明短样本容易受调度、系统负载或首次运行噪声影响；后续记录性能结论时应优先看更长 tick 数或多轮统计。
核心热点判断仍不变：极端 100 组件 + 100 system shape 的质变优化仍应围绕预编译执行器、组件组合缓存、系统调度结构或结构化 mailbox 批处理评估，而不能牺牲 System 异常隔离和动态组件语义。
```

## 2026-06-08：Persistence 队列指标快照修正

本轮只修正观测层：

```text
YuoPersistenceQueueStats.Sanitize 会把 PendingCount 快照夹紧到非负。
BacklogRatio 会夹紧到 0..1。
GetStats 返回已清洗的指标快照。
```

原因：

```text
YuoPersistenceQueue 使用 Channel。
WriteAsync 成功返回后才更新入队统计；高并发下 reader 可能先出队并扣减 pending。
这个窗口不影响实际命令流，但可能让指标出现负积压或负 backlog ratio。
```

边界：

```text
没有改变 Channel 写入/读取顺序。
没有改变停服取消语义。
没有引入 outbox/WAL/可靠 drain。
可靠停机写回仍需要后续由宿主策略裁定。
```

验证：

```bash
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 8
```

## 2026-06-08：Gateway serverSeq 单调性修正

本轮修正 Gateway 连接层序号边界：

```text
如果内层 dispatcher 返回 ServerSeq=0，Pipeline 继续调用 session.NextServerSeq。
如果内层 dispatcher 返回非 0 ServerSeq，Pipeline 保留该值，并调用 AdvanceServerSeqAtLeast 推进 session 下限。
```

原因：

```text
Gateway Pipeline 是 clientSeq/serverSeq 的通用保护层。
旧 protobuf bridge 或过渡期 dispatcher 可能提前填好 ServerSeq。
如果 session 不观察这个值，下一条自动分配响应可能从更小序号开始，客户端会看到 serverSeq 回退。
```

边界：

```text
不改变 Gateway 外部消息结构。
不覆盖内层非 0 ServerSeq，避免破坏当前 Backend bridge。
不让 Gateway 依赖 ECS、Actor 或 Runtime。
```

验证：

```bash
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 8
```

## 2026-06-08：Protocol response/error envelope 状态修正

本轮修正内部协议 envelope 工厂：

```text
YuoProtocolEnvelope.Response 显式设置 Error=null。
YuoProtocolEnvelope.Response / Failure 对 request 做空值保护。
YuoProtocolEnvelope.Failure 对 error 做空值保护。
```

原因：

```text
Response 原来通过 request with 复制字段。
如果 request 来自不可信解码 frame，且携带了 Error，正常 Response 会继承 Error。
这会形成 Kind=Response 但 Error 不为空的混合状态，影响 Network/Actor 上层错误判断。
```

边界：

```text
不改变 envelope wire shape。
不引入任何服务端宿主或网络实现依赖。
不改变 TargetId/SenderId 交换规则。
```

验证：

```bash
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 13
```

## 2026-06-08：StateSync AOI observer tick 单调性修正

本轮修正 AOI 可见性 tracker 的 tick 游标：

```text
YuoAoiVisibilityTracker.BuildDelta 在更新 observer 可见集合前校验 tick。
同一个 observerNetworkId 的 tick 必须严格递增。
重复或倒退 tick 会抛 InvalidOperationException。
```

原因：

```text
tracker 会把上一轮 tick 写入 YuoStateDelta.BaseTick。
如果错误 tick 也能推进内部状态，客户端镜像侧会收到不可信 BaseTick 序列，缺口检测和 snapshot 重拉判断会变差。
```

边界：

```text
不改变 AOI 查询和 delta 数据结构。
不引入订阅式 Cell 缓存。
不改变 StateSync 与 ECS 的依赖方向。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 32
```

## 2026-06-08 复测：Gateway/Protocol/Persistence/StateSync 护栏后多 shard shape 压测

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 100 --shards 4
```

结果：

```text
perf-smoke: entities=10,000 ticks=100 shards=4
tick-ms: avg=91.548 p50=88.511 p95=111.312 max=148.513
entity-tick/s: 109,232
```

判断：

```text
本次复测发生在 Persistence 队列指标快照、Gateway serverSeq 单调性、Protocol response/error envelope 状态、StateSync AOI observer tick 校验之后。
这些改动都不在 ECS shape Tick 热路径。
与上一轮 100 tick 长样本 avg=96.298ms / p95=109.290ms / entity-tick/s=103,845 相比，没有观察到稳定退化。
核心性能热点判断不变：真正的质变优化仍应围绕 ECS 预编译执行器、组件组合缓存、系统调度结构或 Runtime mailbox 批处理单独设计，不能牺牲 System 异常隔离和动态组件语义。
```

## 2026-06-08：LocationSender cached address 取消优先级修正

本轮修正 Actor LocationSender 的取消快路径：

```text
ExecuteWithLocationRetryAsync 每轮开始检查 cancellationToken。
ResolveAsync 在读取 sender cache 前检查 cancellationToken。
```

原因：

```text
Location registry 和 transport 都已经遵守“取消优先于副作用”。
但 sender 自己的 _addressCache 命中时，原实现会先返回地址，再调用 requestFactory/messageFactory。
如果调用方 token 已取消，业务 factory 仍可能被执行，违背取消请求不进入业务层的边界。
```

边界：

```text
不改变 stale retry、locked retry 或 timeout 映射。
不改变业务 key -> ActorId 的消息模型。
不引入分布式 Location。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 26
```

## 2026-06-08：StartConfig ServiceDiscovery lease TTL 预检修正

本轮修正 StartConfig 发布到服务发现时的本地预检：

```text
RegisterStartConfigServicesAsync 在 PendingServiceRegistration 阶段校验 lease TTL。
所有服务的 endpoint、metadata、lease TTL 都成功解析后，才进入 discovery.RegisterAsync。
```

原因：

```text
service-specific leaseTtlSelector 可以为不同服务返回不同 TTL。
原实现只预解析 endpoint/metadata，不预检 TTL。
如果后一个服务 TTL 非法，可能在前一个服务已经注册后才失败，留下半发布服务目录。
```

边界：

```text
不改变 ServiceDiscovery 接口。
不引入远程服务发现或事务语义。
不让 YuoCore.Server 反向依赖 ServiceDiscovery；适配仍保留在 YuoCore.Server.ServiceDiscovery。
```

验证：

```bash
dotnet test tests/YuoCore.Server.ServiceDiscovery.Tests/YuoCore.Server.ServiceDiscovery.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 9
```

## 2026-06-08：ServerHost 本地 Actor 注册一致性修正

本轮修正 `YuoServerHost.RegisterLocalEntityActorAsync`：

```text
业务 Location.RegisterAsync 的 YuoResult 必须成功。
ActorLocation 或业务 Location 注册失败时，会回滚 ActorEntities、ActorTransport endpoint 和 ActorLocations。
```

原因：

```text
ServerHost 是 Entity 进入 Actor/Location 世界的装配边界。
如果本地 entity/endpoint 已注册，但业务 LocationKey 注册失败，系统会出现“ActorId 能路由，业务 key 不能路由”的半注册状态。
这对玩家登录、换图、断线重连和迁移恢复都很危险。
```

边界：

```text
不引入分布式事务。
不改变 Actor handler 或 Fiber mailbox 语义。
只保证当前进程本地表和当前 Location 抽象调用失败时尽量回滚。
```

验证：

```bash
dotnet test tests/YuoCore.Server.Tests/YuoCore.Server.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 3
```

## 2026-06-08：Timer Update 取消优先

本轮修正手动驱动 Timer 的更新入口：

```text
YuoTimerScheduler.UpdateAsync(now, cancellationToken) 在读取 due timer 和推进 CurrentTime 前检查取消。
取消时 CurrentTime 保持原值。
```

原因：

```text
TimerScheduler 可被 Unity 主循环、单机循环、FiberTimerHost 或 Actor retry delay 使用。
如果宿主取消本帧更新，Timer 不应静默推进内部时间。
```

边界：

```text
不改变手动驱动模型。
不改变 callback 已经开始执行后的失败隔离和 due batch 语义。
不引入任何 Runtime/Actor 依赖。
```

验证：

```bash
dotnet test tests/YuoCore.Timers.Tests/YuoCore.Timers.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Timers focused tests: Passed 12
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Actor.Ecs dispatcher/handler 入口守卫

本轮修正 Actor 到 ECS 适配层的参数契约：

```text
YuoEntityActorDispatcher 构造依赖为 null 时立即抛 ArgumentNullException。
DispatchAsync 对 null envelope 早失败。
YuoActorHandler / YuoActorRequestHandler 对 null entity/message 早失败。
YuoActorHandlerRegistry.AddFromAssembly / TryGet 对 null 参数早失败。
```

原因：

```text
Actor.Ecs 是消息层进入 ECS Entity 的边界。
这里的错误应该清晰指向适配层输入，而不是延迟到 entity resolve、message.GetType 或 handler 内部。
```

边界：

```text
不改变 Actor.Core 与 Ecs 的模块依赖方向。
不改变业务 handler RunAsync 签名。
不改变 handler registry 的启动期扫描模型。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Actor focused tests: Passed 35
Full build: Build succeeded
Full tests: Passed all test projects
```

附加测试稳定性修正：

```text
Router_invoke_timeout_covers_stale_retry_resolve 的 timeout 从 20ms 放宽到 200ms。
原因是全量并发测试中，20ms 可能在进入第二次 Resolve 前被调度抖动耗尽。
该用例仍然验证 timeout 覆盖 stale retry resolve，只是不再依赖极窄时间窗口。
```

## 2026-06-08：Actor Router/Transport 入口守卫

本轮修正 Actor 核心层入口参数契约：

```text
ActorRouter 构造依赖为 null 时立即抛 ArgumentNullException。
SendAsync / InvokeAsync 对 null message/request 早失败，且不会触发 Location Resolve。
InMemoryActorTransport 拒绝 null endpoint 和 null envelope。
ActorEnvelope 工厂方法拒绝 null message/request/request envelope/error。
```

原因：

```text
Actor 是 Fiber 间通信的核心层。
基础设施参数错误应在路由和 mailbox 前暴露，避免被误判为 Location 缓存、stale address 或 handler 错误。
```

边界：

```text
不改变 LocationSender / ActorRouter 的 404 stale retry。
不改变 ActorEnvelope 字段结构。
不引入 ECS、Runtime 或远程网络依赖。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Actor focused tests: Passed 32
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Runtime FiberManager 顺序 tick dispose 守卫

本轮修正 Runtime 生命周期一致性：

```text
YuoFiberManager.TickAllAsync 在 dispose 后会明确抛 ObjectDisposedException。
TickAllParallelAsync 原本已有相同守卫。
```

原因：

```text
FiberManager 是同机多 Fiber 调度入口。
dispose 后顺序 tick 静默 no-op 会掩盖宿主生命周期错误。
这属于运行时入口一致性问题，不需要改 mailbox item 结构或 ECS tick 语义。
```

边界：

```text
不改变 Fiber 内部串行执行模型。
不改变 mailbox drain 顺序、预算和异常传播。
不引入 Actor、Network 或 Server 依赖。
```

验证：

```bash
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Runtime focused tests: Passed 13
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：StartConfig null 输入结果化校验

本轮修正 Config 运行时校验的错误可解释性：

```text
YuoStartConfigLoader.Validate(null) 返回 YuoResult.Fail。
Services/Fibers 集合为 null 时返回明确错误。
Services/Fibers 内部列表项为 null 时返回明确错误。
```

原因：

```text
StartConfig 是单进程 MMO、同机多 Fiber 和可选分布式拓扑的入口。
坏配置应该在预检阶段形成稳定错误，而不是 NullReferenceException。
这也让 Server.ServiceDiscovery 这类可选适配可以依赖统一 Validate 契约。
```

边界：

```text
不改变 DTO 字段和 JSON Schema。
不把 Config 反向依赖到 Server、ServiceDiscovery 或 Backend。
```

验证：

```bash
dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Config focused tests: Passed 8
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：入口守卫后 1w 实体压测烟测

本轮在完成 StateSync、Protocol、ServiceDiscovery、Gateway、Network 多个入口守卫后，重新运行 1w entity / 100 tick / 4 shard 压测烟测。

命令：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 100 --shards 4
```

结果：

```text
Run 1:
tick-ms: avg=97.695 p50=94.876 p95=115.697 max=171.043
entity-tick/s: 102,359

Run 2:
tick-ms: avg=96.449 p50=93.555 p95=120.279 max=186.384
entity-tick/s: 103,682
```

对比上一条已记录烟测：

```text
previous:
tick-ms: avg=91.548 p50=88.511 p95=111.312 max=148.513
entity-tick/s: 109,232
```

判断：

```text
当前两次结果比 previous 慢约 5% 到 7%。
本轮修改集中在客户端镜像、协议解码、服务发现、Gateway、Network 参数入口，不在 ECS tick 热路径。
暂无证据表明这是严重性能热点，更像运行环境波动或 benchmark 噪声。
后续如果连续多轮烟测维持 100k entity-tick/s 附近，应再用 BenchmarkDotNet 正式 profile ECS/Runtime tick 热路径。
```

## 2026-06-08：Network transport 入口守卫

本轮修正 Network 可选模块的基础设施参数契约：

```text
InMemoryYuoTransport 注册 endpoint 时拒绝 null。
InMemoryYuoTransport 和 ProcessAwareYuoTransport 在发送/调用前拒绝 null envelope。
ProcessAwareYuoTransport 构造时拒绝空 localProcessId 和 null localTransport。
```

原因：

```text
Network 当前主要用于本机内存传输和远端序列化边界模拟。
它是 Actor/Location 之外的可选传输层，基础参数错误应该在进入路由和 endpoint 前暴露。
这样后续替换 TCP/WebSocket/KCP 实现时，可以沿用同一入口契约。
```

边界：

```text
不新增真实远程网络。
不改变缺失 endpoint 仍抛 YuoTransportException 的语义。
不改变 ProcessAware 对 local/remote 的选择规则。
```

验证：

```bash
dotnet test tests/YuoCore.Network.Tests/YuoCore.Network.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Network focused tests: Passed 13
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Gateway dispatch pipeline 入口守卫

本轮修正 Gateway pipeline 的基础设施参数契约：

```text
YuoGatewayDispatchPipeline 构造时拒绝 null dispatcher。
DispatchAsync 在处理 clientSeq 前拒绝 null message。
```

原因：

```text
Gateway 是 Web/Unity 客户端请求进入服务端的第一层业务无关管线。
基础设施错误应该以清晰参数异常暴露，而不是延迟成 NullReferenceException。
null message 也不能推进 LastClientSeq 或进入业务 dispatcher。
```

边界：

```text
不改变正常消息返回、serverSeq 分配、clientSeq 去重和取消回滚语义。
不依赖 Actor、ECS、WebSocket 或 ASP.NET Core。
```

验证：

```bash
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Gateway focused tests: Passed 10
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：ServiceDiscovery 查询条件快照化

本轮修正内存服务发现的查询/订阅入口：

```text
QueryAsync 会先复制 query metadata。
Subscribe 会保存 query metadata 的快照，而不是外部字典引用。
null query 会明确抛 ArgumentNullException。
```

原因：

```text
ServiceDiscovery 是可选服务端目录，但它的内存实现也会用于单进程 MMO 和测试。
订阅条件如果跟随外部字典变化，会导致服务变更通知难以复现。
这属于路由输入隔离问题，应在 ServiceDiscovery 内部解决，不放到上层 ServerHost。
```

边界：

```text
不新增分布式实现。
不改变注册、续租、过期清理和通知的语义。
不引入任何网络或 Hosting 依赖。
```

验证：

```bash
dotnet test tests/YuoCore.ServiceDiscovery.Tests/YuoCore.ServiceDiscovery.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
ServiceDiscovery focused tests: Passed 14
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Protocol envelope 解码帧校验

本轮修正通用协议 envelope 的解码边界：

```text
YuoProtocolEnvelopeCodec.Decode 现在会拒绝 version=0 的 frame。
Decode 也会拒绝未知 YuoMessageKind。
```

原因：

```text
Protocol 是 Web/Unity/服务端共享边界。
非法 frame 不应该在反序列化后继续流入 Network、Actor 或 Gateway。
这类校验属于协议通用能力，不应该放到 Backend 或某个具体 transport 里。
```

边界：

```text
不改变现有 JSON frame 字段。
不校验业务 opcode，避免 codec 依赖消息目录和业务注册表。
不引入网络、运行时或服务端宿主依赖。
```

验证：

```bash
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

## 2026-06-08：Actor 本机消息入口守卫

本轮收紧 Actor 本机通信入口：

```text
DelegateActorMessageDispatcher 构造和 DispatchAsync 显式拒绝 null。
ActorFiber 构造、SendAsync、InvokeAsync 显式拒绝 null。
YuoFiberActorEndpoint 构造、SendAsync、InvokeAsync 显式拒绝 null。
```

性能影响：

```text
新增检查位于公开入口和装配边界。
正常消息派发只增加一次空值检查，不改变 Channel mailbox、ActorRouter、LocationSender、handler 调度和 Runtime Fiber Tick 热路径。
对 1w 实体 ECS 压测、StateSync 构建、AOI 查询等主热路径没有影响。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Runtime.Actor.Tests/YuoCore.Runtime.Actor.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Actor focused tests: Passed 40
Runtime.Actor focused tests: Passed 4
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Runtime FiberOptions null 守卫

本轮收紧 Runtime 装配入口：

```text
YuoFiberManager.CreateFiber(YuoFiberOptions) 现在显式拒绝 null options。
```

性能影响：

```text
只影响 Fiber 创建期。
Tick、mailbox drain、并行 Tick 热路径不变。
```

验证：

```bash
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Runtime focused tests: Passed 14
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：SerializedTransport 显式入口校验

本轮收紧 Network 序列化传输边界：

```text
SerializedYuoTransport 构造时拒绝 null innerTransport。
SendAsync / InvokeAsync 在编解码前拒绝 null envelope。
```

性能影响：

```text
正常路径只多一次 null 判断。
序列化 round-trip 成本不变。
收益是远端语义模拟层的错误边界和 InMemory/ProcessAware transport 保持一致。
```

验证：

```bash
dotnet test tests/YuoCore.Network.Tests/YuoCore.Network.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Network focused tests: Passed 15
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Persistence 后台装配早失败

本轮收紧 Persistence 后台写回边界：

```text
YuoPersistenceBackgroundService 构造时拒绝 null queue / snapshotStore / eventStore。
InMemoryYuoPersistenceDeadLetterStore.AppendAsync 拒绝 null record。
```

性能影响：

```text
后台服务依赖检查只发生在构造期。
dead-letter 写入正常路径多一次 null 判断。
不改变队列热路径、批处理 drain、重试次数或失败隔离语义。
```

验证：

```bash
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Persistence focused tests: Passed 9
Persistence.Hosting focused tests: Passed 8
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：StartConfig metadata 校验

本轮收紧 StartConfig 配置契约：

```text
root / service / fiber Metadata 不能为 null。
metadata key 不能为 null/空白。
metadata value 不能为 null。
JSON Schema 对 metadata propertyNames 增加 minLength = 1。
```

性能影响：

```text
只影响启动配置校验。
不会影响 Runtime、Actor、ECS 或 Gateway 热路径。
收益是服务发布前能提前发现坏配置，避免进入 ServiceDiscovery 时才失败。
```

验证：

```bash
dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Config focused tests: Passed 11
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：ServiceDiscovery metadata 输入守卫

本轮收紧服务发现 metadata 契约：

```text
RegisterAsync 拒绝 null/空白 metadata key。
RegisterAsync 拒绝 null metadata value。
QueryAsync / Subscribe 对 metadata filter 使用同样校验。
```

性能影响：

```text
metadata 原本就会被复制为快照；本轮只在复制循环中增加 key/value 判断。
查询匹配路径不变。
收益是服务发现索引和订阅筛选不会接收坏键值，便于未来分布式实现复用同一行为基准。
```

验证：

```bash
dotnet test tests/YuoCore.ServiceDiscovery.Tests/YuoCore.ServiceDiscovery.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
ServiceDiscovery focused tests: Passed 18
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：LocationSender 工厂 null 结果守卫

本轮收紧 Actor LocationSender 边界：

```text
YuoActorLocationSender 构造时拒绝 null locations / transport。
messageFactory 返回 null 会得到 ActorException 400。
requestFactory 返回 null 会得到 ActorException 400。
```

性能影响：

```text
构造依赖检查只发生一次。
Send/Invoke 正常路径多一次 null 判断，发生在消息对象已经由业务 factory 创建之后。
收益是业务消息工厂错误不会进入 transport，也不会表现为空引用。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Actor focused tests: Passed 38
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Location 适配器 null 依赖早失败

本轮收紧 Location/Actor 装配契约：

```text
CachingYuoLocationRegistry 构造时拒绝 null inner registry。
YuoLocationActorRegistry 构造时拒绝 null location registry。
```

性能影响：

```text
只影响构造期。
运行时 Resolve/Register/Invalidate 路径没有额外分支。
收益是宿主装配错误会在启动阶段暴露，而不是延迟到第一次 Actor 路由或 Location 查询。
```

验证：

```bash
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Location focused tests: Passed 16
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：AOI 非有限半径防御

本轮修正 AOI 查询的坏参数性能风险：

```text
YuoGridAoiQuery / YuoGridAoiTargetIndex 现在拒绝 NaN / Infinity / <= 0 的 cellSize。
QueryVisible 对 NaN / Infinity / <= 0 的 ViewRadius 返回空结果。
```

性能影响：

```text
正常路径只多两个 float.IsFinite 判断。
防止 Infinity ViewRadius 被转换成超大 cellRadius 后展开极大范围扫描。
这是防御性性能修复，不改变有限半径下的 AOI 查询结果。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
StateSync focused tests: Passed 52
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：StateSync snapshot/delta 集合守卫

本轮收紧 StateSync 坏包处理：

```text
YuoReplicatedStateTable 在提交前拒绝 null Entities / Changes 集合。
YuoReplicatedStateTable 在提交前拒绝 null EntityState / StateChange 元素。
YuoStateDeltaCompressor 在压缩前拒绝 null Changes / StateChange。
YuoStateSyncMirror 会把这些错误结果化为 InvalidSnapshot / InvalidDelta。
```

性能影响：

```text
正常路径多一次集合 null 判断和元素 null 判断。
这些检查发生在原本就需要遍历整批 snapshot/delta 的路径上，不改变算法复杂度。
收益是客户端镜像层不会因为坏同步包抛出空引用，也不会破坏旧状态。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
StateSync focused tests: Passed 43
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Protocol registry 描述符输入守卫

本轮收紧 `YuoMessageRegistry.Create` 的协议注册契约：

```text
拒绝 null descriptor。
拒绝 null MessageType。
拒绝未知 YuoMessageDirection。
```

原因：

```text
Protocol 是 Web、Unity、Gateway 和内部消息共享的契约层。
注册表应该在启动/生成阶段暴露坏描述，而不是让错误延迟到 Gateway 路由或客户端协议映射阶段。
```

性能影响：

```text
只影响 registry 创建期。
运行时 TryGet/Get 查询路径不变。
```

验证：

```bash
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Protocol focused tests: Passed 19
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Result 失败态 null error 守卫

本轮修正 `YuoCore.Abstractions` 的基础错误契约：

```text
YuoResult.Fail(null!) 和 YuoResult<T>.Fail(null!) 现在会直接抛 ArgumentNullException。
失败 Result 必须携带 YuoError；否则 IsSuccess 会因为 Error 为 null 被误判为成功。
```

性能影响：

```text
正常失败路径只多一次入口 null guard。
成功路径不受影响。
这属于基础契约修复，不是性能优化；收益在于避免 Actor、Gateway、StateSync、Persistence 等上层模块传播错误状态。
```

验证：

```bash
dotnet test tests/YuoCore.Abstractions.Tests/YuoCore.Abstractions.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
Abstractions focused tests: Passed 4
Full build: Build succeeded
Full tests: Passed all test projects
```

结果：

```text
Protocol focused tests: Passed 16
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：StateSync Mirror 无效 snapshot 结果化

本轮修正客户端镜像协调器的错误契约：

```text
YuoStateSyncMirror.ApplySnapshot 现在会把无效 snapshot 转成 Rejected/InvalidSnapshot。
调用方可以继续使用统一的 YuoStateSyncApplyResult 判断错误。
底层 YuoReplicatedStateTable 仍保留直接抛异常的快速失败语义。
```

原因：

```text
Mirror 面向 Web/Unity 客户端 SDK 或 Gateway 侧镜像协调。
它已经对 delta 做结果化处理，snapshot 也应该遵守同一契约。
坏 snapshot 不能破坏旧表，也不能在缺口恢复阶段错误清除 SnapshotRequired 状态。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

结果：

```text
StateSync focused tests: Passed 35
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：StateSync snapshot 原子校验修正

本轮修正客户端镜像表的完整快照应用：

```text
YuoReplicatedStateTable.ApplySnapshot 在清空旧状态前校验 snapshot。
重复 NetworkId 会被拒绝。
坏 snapshot 被拒绝时，旧 States 和 LastTick 保持不变。
```

原因：

```text
Delta 已经是先校验整批 change 再提交。
完整 snapshot 原来直接 Clear 后逐个写入，重复 NetworkId 会静默覆盖。
客户端重拉 snapshot 是缺口恢复的关键路径，不能让坏包破坏已有镜像状态。
```

边界：

```text
不改变 StateSync 数据结构。
不引入客户端表现层或网络层依赖。
不改变服务端 snapshot builder 行为。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
```

结果：

```text
Passed: 33
```

## 2026-06-08：YuoCore.Game 领域包上移

本轮把不依赖宿主的挂机/MMO 示例领域类型从 Backend 上移到 `YuoCore.Game`：

```text
YuoIdlePlayerComponent
YuoPlayerPresenceComponent
YuoPlayerTransformComponent
YuoIdleWalletComponent
YuoIdleProgressComponent
YuoRuntimeStatsComponent
YuoIdleTickSystem
YuoRuntimeStatsSystem
YuoPlayerSnapshotDto
YuoWorldSnapshotDto
YuoWorldEventDto
YuoGameCommand 及其派生命令
```

职责边界：

```text
YuoCore.Game 可以被单机、Unity 模拟环境、服务端 GameShard 引用。
YuoCore.Backend 只保留 ASP.NET 宿主、WebSocket 分发、后台队列、数据库/Redis 适配和示例 GameRuntime。
YuoCore.Game 不引用 ASP.NET Core、Dapper、Npgsql、Redis、OpenTelemetry、Gateway 或 Backend。
```

性能影响：

```text
这是职责拆分，不是热路径优化。
类型命名和项目边界调整不改变 ECS system 扫描、Actor mailbox、LocationSender 或 StateSync 热路径。
新增的 YuoCore.Game.Tests 只验证纯 ECS World 下的系统可运行性，不代表 1w 玩家性能结论。
```

发现的问题：

```text
上移后内部 Actor 消息 EnterMapRequest 与 protobuf 外部协议 EnterMapRequest 发生命名冲突。
已把内部消息改为 YuoEnterMapActorRequest / YuoMoveUnitActorRequest，外部协议名保持不变。
这个问题说明内部消息和外部协议必须长期命名隔离，避免 Unity/Web SDK 生成类型与服务端内部类型污染同一命名空间。
```

验证：

```bash
dotnet test tests/YuoCore.Game.Tests/YuoCore.Game.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
Game focused tests: Passed 2
Backend focused tests: Passed 16
Architecture focused tests: Passed 11
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-09：StateSync 短历史 replay + snapshot fallback

本轮补上 StateSync 缺口恢复的第一版可靠路径：

```text
YuoCore.StateSync.Abstractions
  新增 YuoStateDeltaReplayBuffer。
  按固定容量保存纯 YuoStateDelta。
  支持 ReplayAfter(knownTick)，只有 BaseTick 连续时才返回 replay。
  历史窗口覆盖不到、tick 非法或链路不连续时返回 MissingHistory/InvalidKnownTick。

YuoCore.Protocol
  新增 StateSyncRecoverRequest。
  新增 StateSyncRecoverResponse。
  新增 opcode:
    StateSyncRecoverRequest = 9
    StateSyncRecover = 108

YuoCore.Backend
  GameStateSyncPushDispatcher 把 StateSync tick 从 WebSocket serverSeq 分离。
  StateSync tick 变成按玩家观察流递增，断线重连后可以用旧 knownTick 继续 replay。
  有连续历史时返回 recover response + deltas。
  历史缺失或 knownTick=0 时返回 recover response + full snapshot。

YuoCore.Client
  YuoMmoClient 新增 RecoverStateSyncAsync。

YuoCore.Client.StateSync
  YuoClientStateSyncRecovery 新增 RecoverAsync。
  recover response 内如果有 snapshot，先应用 snapshot。
  然后按顺序应用 deltas。

clients/web/yuocore-client-ts
  同步新增 recover request/response、opcode、手写 codec、recoverStateSync helper。
```

边界说明：

```text
Replay buffer 是纯 StateSync 数据结构，不依赖 ECS、Gateway、Backend、WebSocket 或 Protobuf。
Backend 只作为示例宿主，把纯 delta 历史接到 Protobuf recover 协议。
Gateway 核心仍不依赖 Protobuf。
Web/Unity 只共享外部协议，不暴露 ActorId、FiberId、LocationKey 或服务端 Entity 引用。
```

当前仍不是完整可靠流：

```text
还没有 client ack。
还没有按 AOI stream id 分区的正式同步流。
还没有服务端按 ack 裁剪历史。
还没有历史 delta 持久化。
重连后的订阅恢复仍依赖客户端主动发 StateSyncRecoverRequest。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers
scripts/run-pack-smoke.sh
scripts/run-local-load-suite.sh --quick
```

结果：

```text
StateSync focused tests: Passed 59
Client focused tests: Passed 8
Client.StateSync focused tests: Passed 8
Backend focused tests: Passed 34
Architecture focused tests: Passed 15
Protocol focused tests: Passed 22
StateSync.Protocol focused tests: Passed 3
TypeScript compile: Passed
Web SDK smoke: Passed
Full build: Build succeeded, 0 warnings, 0 errors
Full tests: Passed all test projects
Pack smoke: packages=15
Pack output: artifacts/packages/pack-smoke-20260608T164937Z
Load output: artifacts/load/20260608T164937Z-quick
Local composite quick:
  players=10,000
  ticks=5
  p95 tick=163.464ms
  actor-enqueue p95=69.600ms
  runtime-tick p95=14.987ms
  statesync-delta p95=10.456ms
  aoi-query p95=42.441ms
  persistence p95=26.482ms
  allocated=30.88MB
ECS shape quick:
  entities=10,000
  components=100
  systems=100
  p95 tick=193.701ms
  allocated=322.65MB
```

## 2026-06-09：StateSync ACK 协议与 Backend 记录

本轮在短历史 replay 之后补上 ACK 契约：

```text
YuoCore.Protocol
  新增 StateSyncAckRequest。
  新增 StateSyncAckResponse。
  新增 opcode:
    StateSyncAckRequest = 10
    StateSyncAck = 109

YuoCore.Backend
  GameGatewayProtobufDispatcher 支持 StateSyncAckRequest。
  GameStateSyncPushDispatcher 按 Gateway session 记录已确认 tick。
  ack_tick <= 0 会返回 accepted=false。
  ack_tick 超过当前服务端同步流 latest_tick 会返回 accepted=false。
  重复或倒退 ack 不会降低服务端已经记录的 session ack。

YuoCore.Client
  YuoMmoClient 新增 AckStateSyncAsync。

clients/web/yuocore-client-ts
  新增 ackStateSync。
  同步新增 ACK request/response 的手写 codec。
```

边界说明：

```text
ACK 当前属于外部协议和 Backend 示例适配层，不进入 Ecs、Runtime、Actor 或 Gateway 核心。
Backend 只记录 session ack，还不根据 ack 裁剪 ReplayBuffer。
后续可以基于 session/player ack 推导安全裁剪点，但要等 streamId / AOI stream 分区落地后再做。
```

验证：

```bash
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
scripts/run-pack-smoke.sh
scripts/run-local-load-suite.sh --quick
```

结果：

```text
Client focused tests: Passed 8
Backend focused tests: Passed 36
Protocol focused tests: Passed 22
TypeScript compile: Passed
Web SDK smoke: Passed
Architecture focused tests: Passed 15
Full build: Build succeeded, 0 warnings, 0 errors
Full tests: Passed all test projects
Pack smoke: packages=15
Pack output: artifacts/packages/pack-smoke-20260608T172425Z
```

## 2026-06-09：StateSync streamId 与基于 ACK 的短历史裁剪

本轮把 StateSync 从隐式玩家观察流推进到显式 streamId：

```text
YuoCore.Protocol
  StateSnapshotMessage 增加 stream_id。
  StateDeltaMessage 增加 stream_id。
  StateSyncRecoverRequest / StateSyncRecoverResponse 增加 stream_id。
  StateSyncAckResponse 增加 stream_id。

YuoCore.StateSync.Abstractions
  YuoStateDeltaReplayBuffer 新增 PruneThrough(tick)。
  该方法只裁剪纯 delta 历史，不知道玩家、session、网络连接或 Backend。

YuoCore.Backend
  GameStateSyncPushDispatcher 为示例玩家观察流生成稳定 streamId：player:{playerId:N}。
  Snapshot / Delta / Recover / ACK response 都会携带该 streamId。
  Recover 请求如果指定了不匹配的 streamId，会被拒绝。
  ACK 请求如果指定了不匹配的 streamId，会返回 accepted=false。
  Backend 记录 session -> player 绑定和 session ack tick。
  只有同一玩家观察流下所有已知活跃 session 都 ack 后，才会按最小 ack tick 裁剪 replay buffer。

YuoCore.Client / Web TS SDK
  RecoverStateSyncAsync / recoverStateSync 支持可选 streamId。
  TS StateSnapshot / StateDelta / Recover / Ack 类型同步暴露 streamId。
```

取舍：

```text
当前 streamId 仍是 Backend 示例生成的玩家观察流，不是最终 AOI stream id。
ACK 裁剪只作用于内存 replay buffer，不做持久化。
ClearSession 会移除该 session 的 ack 状态，避免断开的旧 session 长期阻止裁剪。
如果所有活跃 session 都 ack 到某 tick，旧于等于该 tick 的 delta 会被移除；更旧客户端会 fallback full snapshot。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

结果：

```text
StateSync focused tests: Passed 60
Backend focused tests: Passed 37
Client focused tests: Passed 8
Protocol focused tests: Passed 22
TypeScript compile: Passed
Web SDK smoke: Passed
Pack smoke: packages=15
Pack output: artifacts/packages/pack-smoke-20260608T173535Z
Quick load output: artifacts/load/20260608T173535Z-quick
Quick local-composite: players=10,000 ticks=5 shards=4 p95=144.108ms allocated=30.88MB
Quick ecs-shape: entities=10,000 components=100 systems=100 ticks=5 shards=4 p95=167.919ms allocated=324.25MB
```

压测判断：

```text
streamId 生成、ACK stream 校验和 replay buffer 裁剪没有引入新的包依赖污染；15 个代表性 NuGet 包仍可打包。
quick local-composite 仍通过当前 1w 玩家/实体短压测阈值，热点仍集中在 actor-enqueue、AOI observers 查询和 persistence drain。
quick ecs-shape 仍通过 100 组件 + 100 system 极端形态短压测阈值，setup 与分配成本仍明显高于业务综合路径。
本轮没有发现 streamId/ACK 裁剪造成的质变级性能退化；长期风险仍是 StateSync payload、AOI 查询和 ECS shape 分配。
```

## 2026-06-09：Backend AOI stream delta 聚合原语

本轮把 Backend 示例从“只能按 AOI stream 生成当前 snapshot 分组”推进到“可以按 stream 连续生成 delta”：

```text
YuoCore.Backend.Game
  新增 GameStateSyncProjection。
  它把 YuoPlayerWorldStateDto 投影为 StateSync 的 YuoEntityState 或协议层 NetworkEntityState。
  Snapshot push、事件 delta push 和 AOI stream delta 聚合共用同一套 networkId/kind/version/payload 规则。

  新增 GameAoiStreamDeltaAggregator。
  它消费 GameRuntime.BuildAoiStreamSnapshots(mapId, cellSize) 的只读 DTO。
  内部使用 YuoStateStreamDeltaTracker 按 streamId 独立维护上一帧版本。
  玩家进入新 AOI cell 时，新 stream 会生成 Enter。
  玩家离开旧 AOI cell 时，即使旧 stream 当前 snapshot 已经消失，也会生成一次空集合 delta，从而产出 Leave。

YuoCore.Backend.WebSockets
  GameStateSyncPushDispatcher 新增 DrainAoiStreamPushes。
  这是可选旁路入口，不替换现有 DrainPushes 的玩家观察流。
  它把 GameAoiStreamDeltaAggregator 生成的 per-stream delta 转成 StateDeltaMessage。
  每条 AOI stream 都写入独立 replay history，并更新该 stream 的 latest tick。
  Gateway session 会被绑定到当前活跃 AOI stream 集合，为后续 ACK / 裁剪策略铺路。
  StateSync ACK 现在会接受 session 已订阅的显式 streamId，因此 AOI stream 可以复用现有 ACK response 和 replay 裁剪链路。
  AOI stream Recover 在短历史缺失时，会用同一 stream 记录过的 cellSize 重建当前 full snapshot。
  如果该 AOI cell 当前已经没有实体，会返回空 snapshot，客户端可以用它清理对应 stream mirror。
  新 Gateway session 显式携带 AOI streamId recover 时，如果玩家当前仍在该 stream 的 mapId 内，会自动订阅该 stream 并恢复。
  GameOptions 新增 StateSyncAoiCellSize，作为进程启动后尚未见过该 stream 时的 recover fallback cellSize。
  GameOptions 新增 StateSyncAoiRecoverRadiusCells，限制新 session 只能恢复玩家当前 cell 附近的 AOI stream。

YuoCore.StateSync.Abstractions
  YuoStateSyncStreamIds 新增 TryParseMapAoi。
  它只解析 map:{mapId}:aoi:{aoiId} 字符串，不依赖地图对象、AOI 索引或网络连接。

YuoCore.Backend 配置
  appsettings.json / appsettings.schema.json 增加 Game.StateSyncAoiCellSize。
  appsettings.json / appsettings.schema.json 增加 Game.StateSyncAoiRecoverRadiusCells。
  该配置只属于 Backend 示例，不进入 StateSync.Abstractions。

YuoCore.Client.StateSync / Web TS SDK
  C# 新增 YuoClientStateSyncRecoverResult 和 RecoverDetailedAsync。
  TS 新增 YuoStateSyncRecoverApplyResult 和 recoverStateSyncDetailed。
  C# 新增 YuoClientStateSyncMultiStreamRecovery。
  TS 新增 recoverStateSyncMultiStream / recoverStateSyncMultiStreamDetailed。
  旧 RecoverAsync / recoverStateSync 仍返回 apply results 数组，保持兼容。
  当服务端返回 reason=stream_not_observable 且没有 snapshot/delta 时，详细 API 会暴露 reason、streamId 和空 results。

YuoCore.UnityClient
  README 更新为可选 StateSync 用法。
  Unity transport 包仍只引用 YuoCore.Client，不强制引入 YuoCore.Client.StateSync。
  需要 Mirror / Recover / ACK helper 时，由 Unity 项目显式引用 YuoCore.Client.StateSync。
```

职责边界：

```text
StateSync.Abstractions 仍只提供纯 stream delta tracker，不知道 Player、WebSocket、Gateway 或 Protobuf 业务消息。
Backend 负责把游戏状态投影成可同步 payload，因此 Protocol 依赖没有进入 Ecs/Runtime/Actor/StateSync 核心。
当前新增的是服务端 AOI stream 聚合与旁路 push 原语，还没有把默认 WebSocket push 正式切换到共享 AOI stream。
现有示例仍使用 player:{playerId:N} 玩家观察流，避免在订阅迁移策略完全落地前影响稳定闭环。
AOI stream 的 replay 成功路径已经能复用短历史；短历史缺失时也能返回 stream 级 full snapshot fallback。
如果 stream 没有被当前进程观察过，fallback 会使用 Game.StateSyncAoiCellSize 重建当前 stream snapshot。
如果 stream 对应 cell 超出玩家当前 cell 的 recover 半径，Backend 会拒绝该 recover 请求。
拒绝时仍返回 StateSyncRecoverResponse，reason=stream_not_observable，而不是让 Gateway 生成泛化 error。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 42
StateSync focused tests: Passed 107
Client.StateSync focused tests: Passed 14
TypeScript compile: Passed
Web SDK smoke: Passed
Unity client build: Build succeeded, 0 warnings, 0 errors
Solution build: Build succeeded, 0 warnings, 0 errors
新增测试覆盖：玩家从 cell_0_0 移动到 cell_1_0 后，旧 stream 产生 Leave，新 stream 产生 Enter，且两个 stream 的 baseTick 独立。
新增测试覆盖：DrainAoiStreamPushes 能把 AOI stream delta 转为协议 StateDelta，并按 streamId 写入 replay history。
新增测试覆盖：已订阅 AOI stream 的 ACK 会被接受，并能裁剪该 stream 的 replay history。
新增测试覆盖：AOI stream replay history 被裁剪后，新 session 只携带 streamId 也能重新订阅并收到 full snapshot fallback。
新增测试覆盖：从未通过 DrainAoiStreamPushes 观察过的 AOI stream，也能通过 Game.StateSyncAoiCellSize 完成 recover snapshot fallback。
新增测试覆盖：超出 Game.StateSyncAoiRecoverRadiusCells 的远格 AOI stream recover 会返回 reason=stream_not_observable。
新增测试覆盖：C# / TS 客户端单流和多流详细 recover API 会保留 reason=stream_not_observable，并返回空应用结果。
```

### 2026-06-09 AOI Recover 可见性策略解耦

变更：

```text
YuoCore.Backend.Game
  新增 IGameAoiVisibilityPolicy。
  新增 GameAoiVisibilityContext。
  新增 DefaultGameAoiVisibilityPolicy。

YuoCore.Backend.WebSockets
  GameStateSyncPushDispatcher 不再直接把 AOI recover 可见性写死为网格半径判断。
  CanRecoverAoiStream 现在只负责解析 streamId、读取玩家快照、选择 cellSize，然后委托 IGameAoiVisibilityPolicy。
  默认策略仍保留原行为：同地图，并且目标 AOI cell 位于玩家当前 cell 的 StateSyncAoiRecoverRadiusCells 半径内。
```

职责边界：

```text
StateSync.Abstractions 仍只保存 stream 命名、cell 解析和 delta/mirror 原语。
是否允许某个玩家恢复某条 AOI stream 属于 Backend/Map 游戏业务，不进入 StateSync 核心。
正式 MMO 可以替换 IGameAoiVisibilityPolicy，把地形遮挡、地图相位、隐身、阵营、权限和副本状态接入 recover 判定。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 43
新增测试覆盖：自定义 IGameAoiVisibilityPolicy 可以拒绝原本在网格半径内的 AOI stream recover，并返回 reason=stream_not_observable。
```

### 2026-06-09 StateSync 多流订阅集合消息

变更：

```text
YuoCore.Protocol
  yuocore.proto 新增 StateSubscriptionMessage。
  新增 opcode 110: StateSubscription。
  该消息只描述客户端当前应保留的 streamIds、reason 和 resetMissing，不携带 Gateway session、连接对象或 Actor 地址。

YuoCore.Backend.WebSockets
  DrainAoiStreamPushes 在 AOI stream 集合变化时，会先推送 StateSubscriptionMessage(reset_missing=true)。
  客户端收到该消息后可以删除不再订阅的旧 stream mirror，避免玩家切换 AOI cell 后旧 cell 状态残留。

YuoCore.Client.StateSync
  YuoClientStateSyncMessageKind 新增 Subscription。
  YuoClientStateSyncMultiStreamReceiver 新增 ApplyStateSubscription。
  reset_missing=true 时会保留消息中的 streamIds，并删除本地 MirrorSet 中缺失的旧 stream。

Web TS SDK
  新增 YuoStateSubscription。
  新增 encodeStateSubscriptionMessage / decodeStateSubscriptionMessage。
  YuoStateSyncMultiStreamReceiver 支持 stateSubscription push，并按 resetMissing 清理旧 stream。
```

职责边界：

```text
订阅集合消息属于 StateSync 协议层和客户端同步层。
它不进入 Ecs/Runtime/Actor，不要求单机项目引入 Gateway 或 WebSocket。
Gateway 只负责转发该消息；真正订阅集合应由 Map/Room 或 AOI coordinator 决定。
```

验证：

```bash
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
Client.StateSync focused tests: Passed 15
Backend focused tests: Passed 43
Protocol focused tests: Passed 22
TypeScript compile: Passed
Web SDK smoke: Passed
Solution build: Build succeeded, 0 warnings, 0 errors
新增测试覆盖：C# 多流 receiver 收到订阅集合后会删除 missing stream。
新增 smoke 覆盖：TS 多流 receiver 收到 StateSubscription push 后会删除 missing stream。
新增测试覆盖：Backend AOI stream 集合变化时会先发 StateSubscription，再发各 stream delta。
```

### 2026-06-09 AOI Push Coordinator 与可选 WebSocket AOI 模式

变更：

```text
YuoCore.Backend.WebSockets
  新增 GameAoiStreamPushFrame / GameAoiStreamPushItem。
  GameStateSyncPushDispatcher 拆分出 PublishAoiStreamFrame 和 CreateAoiStreamSessionPushes。
  PublishAoiStreamFrame 负责把某 map/tick 的 AOI delta 写入 replay history，并生成可复用 payload。
  CreateAoiStreamSessionPushes 负责按 session 绑定订阅集合、发送 StateSubscriptionMessage，并为同一 payload 分配各自的 serverSeq。

  新增 GameAoiStreamPushCoordinator。
  它按 mapId + cellSize 缓存最近一个已发布 frame。
  同一 map/tick 内多个 session drain 时，会复用同一 frame，不会重复调用 GameAoiStreamDeltaAggregator，也不会重复写 replay history。

  GameWebSocketPushOptions 新增 StateSyncMode。
  默认值为 PlayerStream，保持原玩家观察流行为。
  显式配置 AoiStream 时，GameWebSocketEndpoint 会走 GameAoiStreamPushCoordinator.DrainPushes。
```

配置：

```json
{
  "Gateway": {
    "WebSocketPush": {
      "StateSyncMode": "PlayerStream"
    }
  }
}
```

职责边界：

```text
coordinator 不持有 WebSocket，不直接访问 ECS Entity。
coordinator 只消费 GameRuntime 暴露的 DTO 快照，并通过 GameStateSyncPushDispatcher 发布协议 payload。
默认配置仍不强制启用 AOI stream，因此已有 Web/Unity 客户端不会被静默切换同步语义。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 44
Solution build: Build succeeded, 0 warnings, 0 errors
新增测试覆盖：两个 session 在同一 map/tick 使用 AOI coordinator drain 时，得到相同 stream/tick 的 delta，且 replay history 只写入一次。
```

### 2026-06-09 AOI Live Push 可见性过滤

变更：

```text
YuoCore.Backend.WebSockets
  GameStateSyncPushDispatcher.CreateAoiStreamSessionPushes 新增 filterToObservableStreams 参数。
  启用过滤时，会复用现有 IGameAoiVisibilityPolicy 判断 session 玩家是否可观察某条 AOI stream。
  不可观察的 stream 不会进入该 session 的 StateSubscriptionMessage，也不会下发对应 StateDelta。

  GameAoiStreamPushCoordinator 现在调用 CreateAoiStreamSessionPushes(..., filterToObservableStreams: true)。
  因此 WebSocket AoiStream 模式不再默认把整张 map 的所有 AOI stream 下发给每个玩家。
```

职责边界：

```text
AOI frame 仍按 map/tick 全量生成一次并写入 replay history。
session 下发阶段再根据玩家可见性过滤 stream。
这样 Map 侧聚合和 Gateway session 下发分离，同时避免每个 session 重新聚合 delta。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 45
Solution build: Build succeeded, 0 warnings, 0 errors
新增测试覆盖：两个玩家位于超过可见半径的不同 AOI cell 时，各自 session 只收到自己可观察的 stream 订阅和 delta。
```

### 2026-06-09 AOI 脏 Stream Delta 模式

变更：

```text
YuoCore.StateSync.Abstractions
  YuoStateStreamDeltaTracker 新增 TryBuildDirtyDelta。
  它只在 stream 出现 Enter/Update/Leave 时提交 Delta。
  如果当前实体集合与上一条已发送 Delta 完全一致，则返回 false，并且不推进该 stream 的 lastTick。

YuoCore.Backend.Game
  GameAoiStreamDeltaAggregator 新增 BuildDirtyDeltas。
  默认 BuildDeltas 行为不变，仍可生成空 delta 维持每 tick 连续。
  BuildDirtyDeltas 会跳过无变化 stream，同时保证下一条非空 delta 的 BaseTick 指向最后实际发送过的 tick。

YuoCore.Backend.WebSockets
  GameAoiStreamPushCoordinator 改为使用 BuildDirtyDeltas。
  因此 AoiStream 模式下，普通 tick 前进但 AOI 状态无变化时，不会继续向客户端推空 StateDelta。
```

安全性说明：

```text
不能简单在发送层丢弃空 delta。
如果 tracker 内部 lastTick 已经推进，而客户端没有收到该 tick，下一条 delta 的 BaseTick 就会指向客户端不存在的版本，导致 recover 缺口。
TryBuildDirtyDelta 的关键点是“无变化不提交”，所以跳过空 tick 后仍能保持 BaseTick 连续。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
StateSync focused tests: Passed 108
Backend focused tests: Passed 46
Solution build: Build succeeded, 0 warnings, 0 errors
新增测试覆盖：TryBuildDirtyDelta 在 stream 未变化时不推进 tick，下一次 update 的 BaseTick 仍等于最后实际发送 tick。
新增测试覆盖：AOI coordinator 在 tick 前进但 stream 未变化时不发送 delta，下一次移动 delta 的 BaseTick 仍接上初始 delta tick。
```

### 2026-06-09 AOI Dirty 线索与 Frame 去重

变更：

```text
YuoCore.Game
  新增 YuoAoiDirtyPlayerDto。
  它只描述玩家 AOI 相关状态的前后 map/position，不包含 Entity、WebSocket 或 Gateway session。

YuoCore.Backend.Game
  GameRuntime 新增 DrainAoiDirtyPlayers。
  EnterMap / Move / Disconnect 会记录 dirty player DTO。
  Runtime 仍只暴露游戏 DTO，不依赖 Gateway、Protocol 或 WebSocket。

YuoCore.Backend.WebSockets
  GameAoiStreamPushCoordinator 会消费 Runtime 的 dirty player DTO。
  无 dirty 且已有 cached frame 时，不再重建 AOI snapshots。
  coordinator 新增 per-session last frame tick，避免 push pump 多次轮询时重复给同一 session 发送同一 frame。
  WebSocket 断开时会同步清理 coordinator 的 session frame 游标。
```

当前效果：

```text
有 dirty：coordinator 重新构建该 map 的 AOI frame，并写入必要 delta。
无 dirty：coordinator 直接复用 cached frame；如果该 session 已经收到过此 frame，则返回空列表。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 48
Solution build: Build succeeded, 0 warnings, 0 errors
新增测试覆盖：同一个 session 不会重复收到同一 cached AOI frame。
新增测试覆盖：Runtime 会为 enter/move/disconnect 记录 AOI dirty entries，且 Drain 后清空。
```

### 2026-06-09 AOI Dirty Stream 局部发布

变更：

```text
YuoCore.Backend.Game
  GameRuntime 新增 BuildAoiStreamSnapshots(mapId, cellSize, streamIds)。
  该 API 只返回指定 streamIds 的 AOI snapshots。
  请求的 stream 即使当前没有玩家，也会返回空 snapshot，用于生成 Leave。

  GameAoiStreamDeltaAggregator 新增 BuildDirtyDeltasForStreams。
  它只处理传入的 dirty stream snapshots，不会把未传入的非 dirty stream 当作 missing。

YuoCore.Backend.WebSockets
  GameAoiStreamPushCoordinator 在已有 cached frame 且存在 dirty stream 时，只构建 dirty stream snapshots。
  cached frame 的 ActiveStreamIds 会按 dirty stream 的空/非空结果增量更新。
  replay history 只追加 dirty stream 的 delta，不再因为一个 dirty 玩家触发所有 stream 的 delta 计算。
```

当前效果：

```text
首次或无 cached frame：仍构建整张 map 的 AOI frame。
已有 cached frame 且有 dirty：只对 dirty stream 发布 delta。
已有 cached frame 且无 dirty：不构建 snapshot，不发布新 frame。
```

验证：

```bash
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
```

结果：

```text
Backend focused tests: Passed 49
Solution build: Build succeeded, 0 warnings, 0 errors
新增测试覆盖：两个远距离 AOI stream 初始都有 delta 后，只移动其中一个 stream，另一个 stream 不会收到新 delta，replay history 数量也不变。
```

### 2026-06-09 AOI Stream 1w 压测与旧事件广播裁剪

背景：

```text
新增 aoi-streams 压测模式后，第一次 1w 玩家 / 4 shard / 256 observer / 5 tick 运行没有崩溃，
但性能严重不合格：

tick p95: 48,837.992 ms
move p95: 36,535.242 ms
coordinator-drain p95: 12,302.746 ms
allocated: 10,194.97 MB
```

热点判断：

```text
MoveActorHandler 每次 Move 都会调用 EnqueueMapEvent。
EnqueueMapEvent 会遍历同地图所有在线玩家，把旧 YuoWorldEventDto 推进每个玩家的事件队列。

1w 玩家同 tick 全部移动时，这会变成：

10,000 次移动 * 10,000 玩家广播扫描

也就是典型 O(N^2) 旧广播成本。
AOI Stream 模式已经通过 StateDelta/StateSubscription 下发状态，不需要这条旧 WorldEvent 广播路径。
```

变更：

```text
GameOptions 新增 WorldEventsEnabled，默认 true。
默认保持旧 HTTP / PlayerStream 行为不变。

aoi-streams 压测显式设置 WorldEventsEnabled = false。
这样 StateSync AOI Stream 路径只测 Actor Move、dirty stream、coordinator drain 和 Gateway outbound message，
不会混入旧全图 WorldEvent 广播成本。

GameAoiStreamPushCoordinator 消费 dirty hints 时改为 DrainAoiDirtyPlayers(int.MaxValue)，
避免 1w 玩家同 tick 全动时被默认 4096 上限拆成多次滞后处理。
```

优化后结果：

```text
命令：
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- \
  --mode aoi-streams --players 10000 --ticks 5 --shards 4 --aoi-observers 256 \
  --json-output artifacts/load/20260608T194618Z-quick/aoi-streams.json \
  --fail-p95-ms 250 --fail-max-ms 500

结果：
tick avg: 185.638 ms
tick p50: 205.372 ms
tick p95: 220.428 ms
tick max: 220.428 ms
move p95: 39.841 ms
coordinator-drain p95: 188.401 ms
pushes: 7,458
deltas: 7,424
subscriptions: 34
allocated: 185.08 MB
heap-after: 44.06 MB
ticks-per-second: 5.39
```

追加低风险优化：

```text
GameStateSyncPushDispatcher.CreateAoiStreamSessionPushes 在 live push 可见性过滤时，
原先会对同一个 session 的每个 active stream 调用一次 runtime.TryGetSnapshot(playerId)。
当 active stream 和 observer 数量上来后，这会产生重复 GameRuntime 锁竞争和 DTO 构建。

已改为每个 session 只读取一次 YuoPlayerSnapshotDto，
再用同一个 snapshot 判断该 session 对所有 active stream 的可见性。
```

追加验证：

```text
命令：
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- \
  --mode aoi-streams --players 10000 --ticks 5 --shards 4 --aoi-observers 256 \
  --json-output artifacts/load/20260608T194618Z-quick/aoi-streams-after-snapshot-cache.json \
  --fail-p95-ms 250 --fail-max-ms 500

结果：
tick avg: 163.204 ms
tick p95: 212.014 ms
move p95: 50.586 ms
coordinator-drain p95: 161.284 ms
allocated: 168.36 MB
ticks-per-second: 6.13
```

最终 quick 套件验证：

```text
命令：
scripts/run-local-load-suite.sh --quick

输出目录：
artifacts/load/20260608T201042Z-quick

local-composite:
tick p95: 170.482 ms
allocated: 30.88 MB

aoi-streams:
tick p95: 207.859 ms
move p95: 52.003 ms
coordinator-drain p95: 155.695 ms
allocated: 168.38 MB

ecs-shape:
tick p95: 174.384 ms
allocated: 327.87 MB

三段均通过当前 quick 阈值。
```

### 2026-06-09 文件 Dead-Letter Store

背景：

```text
Persistence 已经具备：

YuoPersistenceQueue
YuoDurablePersistenceQueue
YuoFilePersistenceOutbox
YuoPersistenceBackgroundService
YuoPersistenceDeadLetterFailureSink

但现有 dead-letter store 只有 InMemoryYuoPersistenceDeadLetterStore。
它适合测试和进程内兜底，不提供重启后的失败记录保留。
```

变更：

```text
YuoCore.Persistence 新增 YuoFilePersistenceDeadLetterStore。
它使用 JSONL 文件追加写入，不依赖 PostgreSQL、Redis、Hosting 或 ASP.NET。
支持持久化并读取：

SaveSnapshotCommand
AppendEventCommand
YuoOutboxPersistenceCommand

其中 outbox wrapper 会保留 OutboxSequence 和 Inner command。
```

设计边界：

```text
该实现属于可选单机/小服可靠失败记录。
它不是分布式 dead-letter 队列，也不负责报警、重试调度或后台补偿。
正式商业服可以继续替换为 PostgreSQL 表、对象存储或专门的消息失败队列。
```

验证：

```text
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
```

结果：

```text
Persistence tests: Passed 19
Persistence.Hosting tests: Passed 13
新增测试覆盖：
  文件 dead-letter 记录可跨实例读取。
  outbox dead-letter 能保留 sequence 和 inner command。
```

最终 quick 复验：

```text
命令：
scripts/run-local-load-suite.sh --quick

输出目录：
artifacts/load/20260608T203325Z-quick

local-composite:
tick p95: 140.889 ms

aoi-streams:
tick p95: 180.095 ms
coordinator-drain p95: 118.663 ms
coordinator-first-session p95: 96.253 ms
allocated: 166.40 MB

ecs-shape:
tick p95: 143.764 ms

Solution build:
dotnet build YuoCore.sln --disable-build-servers
0 warnings, 0 errors
```

### 2026-06-09 State Stream Ordered Delta 热路径

诊断：

```text
AOI stream snapshot 已经按 UnitId / NetworkId 稳定排序。
但 GameAoiStreamDeltaAggregator 进入 YuoStateStreamDeltaTracker 时，tracker 原入口仍会再次 OrderBy(NetworkId)。
这会让 AOI frame 发布路径出现重复排序。
```

变更：

```text
YuoStateStreamDeltaTracker 保留原有 BuildDelta / TryBuildDirtyDelta 语义不变。
新增 BuildDeltaOrdered / TryBuildDirtyDeltaOrdered。
这些入口要求调用方保证 currentStates 已按 NetworkId 升序排列。

GameAoiStreamDeltaAggregator 已切到 Ordered 入口。
这样普通 StateSync 调用仍由 tracker 自己排序，AOI 聚合热路径避免二次排序。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
```

结果：

```text
StateSync focused tests: Passed 108
Backend focused tests: Passed 50
```

压测：

```text
命令：
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- \
  --mode aoi-streams --players 10000 --ticks 5 --shards 4 --aoi-observers 256 \
  --json-output artifacts/load/20260608T194618Z-quick/aoi-streams-ordered-delta.json \
  --fail-p95-ms 250 --fail-max-ms 500

结果：
tick p95: 184.439 ms
move p95: 61.813 ms
coordinator-drain p95: 132.418 ms
coordinator-first-session p95: 99.114 ms
allocated: 166.40 MB
```

结论：

```text
Ordered Delta 不是质变优化，但能减少 AOI frame 发布路径中的重复排序和少量分配。
它属于安全优化：默认入口不变，只有明确知道输入有序的 AOI 聚合路径使用 Ordered 入口。
```

最终 quick 复验：

```text
命令：
scripts/run-local-load-suite.sh --quick

输出目录：
artifacts/load/20260608T202700Z-quick

aoi-streams:
tick p95: 181.024 ms
coordinator-drain p95: 136.774 ms
coordinator-first-session p95: 97.143 ms
allocated: 166.40 MB

local-composite:
tick p95: 227.954 ms

ecs-shape:
tick p95: 137.484 ms

三段均通过当前 quick 阈值。
```

结论：

```text
这次优化是质变级。
主要收益不是来自微调数据结构，而是把旧全图事件广播从 AOI Stream 路径中裁剪出去。

旧 WorldEvent 仍保留为可选兼容能力。
新 MMO 状态同步路径应优先使用 AOI Stream / StateDelta，不应依赖每次移动都对全图玩家写事件队列。
```

剩余热点：

```text
当前 1w 压测已经通过 quick 阈值，但 coordinator-drain p95 仍有 155ms 到 161ms 左右。
这说明下一阶段热点已经从 Move 旧广播转移到 AOI session 下发、订阅过滤、StateDelta payload 构建和 dirty stream snapshot 生成。

GameRuntime.BuildAoiStreamSnapshots(mapId, cellSize, streamIds) 目前仍会枚举 map 内玩家后筛选目标 stream。
如果后续 sparse movement 或更大 map 压测显示 drain 仍高，应在 Map/Room 层维护 stream -> player/unit 索引，
让 dirty stream snapshot 从“扫描地图玩家”变成“按 stream 直接取玩家集合”。
```

### 2026-06-09 AOI Stream 索引与 Drain Breakdown

诊断：

```text
aoi-streams 新增 drain breakdown 后，1w 玩家 / 256 observer 下显示：

coordinator-first-session p95: 141.385 ms
coordinator-remaining-sessions p95: 39.232 ms

第一位 observer 的 drain 会触发 dirty frame 发布、dirty stream snapshot 和 delta 构建；
后续 observer 主要复用 frame 并做 session 可见性过滤。
因此主要热点在 frame 发布，不在 256 个 session 的复用下发。
```

变更：

```text
GameRuntime 新增 Backend 示例层 AOI stream 索引：

mapId -> streamId -> playerId set
playerId -> current stream

TryRestorePlayer 和 Move/Enter dirty 路径会维护索引。
BuildAoiStreamSnapshots(mapId, cellSize, streamIds) 在 cellSize 与 GameOptions.StateSyncAoiCellSize 一致时走索引。
cellSize 不一致时仍回退旧全图扫描，避免不同 AOI 配置下索引语义错误。
```

中间失败：

```text
第一版索引每次移动都 remove/add，即使玩家仍在同一个 cell。
1w 全移动压测变差：

tick p95: 344.910 ms
move p95: 111.175 ms
coordinator-drain p95: 233.733 ms

裁定：
索引更新必须先判断 stream 是否变化；同 stream 移动不能写索引。
```

修正：

```text
UpdateAoiIndex 增加同 map/stream 早退。
YuoGridAoiStreamResolver 改为 GameRuntime 内缓存，不在每次移动时 new。
新增 Backend 测试覆盖：玩家跨 cell 移动后，指定 old/new stream snapshot 能正确表现旧 stream 为空、新 stream 有玩家。
```

最终单项结果：

```text
命令：
dotnet run -c Release --no-build --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- \
  --mode aoi-streams --players 10000 --ticks 5 --shards 4 --aoi-observers 256 \
  --json-output artifacts/load/20260608T194618Z-quick/aoi-streams-indexed-resolver-cache.json \
  --fail-p95-ms 250 --fail-max-ms 500

结果：
tick p95: 165.643 ms
move p95: 43.216 ms
coordinator-drain p95: 132.426 ms
coordinator-first-session p95: 97.218 ms
coordinator-remaining-sessions p95: 56.636 ms
allocated: 168.17 MB
```

最终 quick 套件复验：

```text
命令：
scripts/run-local-load-suite.sh --quick

输出目录：
artifacts/load/20260608T202110Z-quick

aoi-streams:
tick p95: 186.148 ms
move p95: 49.231 ms
coordinator-drain p95: 136.765 ms
coordinator-first-session p95: 108.329 ms
coordinator-remaining-sessions p95: 45.051 ms
allocated: 168.17 MB

local-composite:
tick p95: 150.407 ms

ecs-shape:
tick p95: 158.547 ms

三段均通过当前 quick 阈值。
```

### 2026-06-09 WebSocket 保存恢复端到端闭环

本轮补充了一个更贴近真实 MMO 的 `/ws` 端到端测试：

```text
tests/YuoCore.Backend.Tests/WebSocketEndpointEndToEndTests.cs

流程：
1. 启动第一个 TestServer Backend。
2. 使用 YuoMmoClient 通过 V1 envelope 登录、进地图、移动。
3. 通过 DisconnectRequest 断开，Gateway 会把客户端传入的 sessionId 归一化为当前连接的 YuoGatewaySession.SessionId。
4. GameClientMessageDispatcher 执行下线并通过 SnapshotPersistenceService 保存 Online=false 的快照。
5. 销毁第一个 TestServer，模拟 Backend 进程重启。
6. 启动第二个 TestServer，共享同一个测试快照仓储，但使用全新的 GameRuntime。
7. 使用同一个 playerId 登录，Backend 先从持久化层恢复快照，再执行 LoginPlayer。
8. GetSnapshot 验证 mapId、坐标和 lastInputSeq 被恢复，同时在线状态被新 session 正确置为 true。
```

职责边界：

```text
测试只通过外部 WebSocket 协议驱动，不直接访问 GameRuntime 修改状态。
测试仓储只替换 IPlayerSnapshotRepository / IPlayerSnapshotCache，不进入 Ecs、Runtime、Actor、Gateway 或 Client SDK。
Backend 示例证明的是单进程重启后的保存恢复闭环；正式商业部署仍需要 PostgreSQL/Redis/Outbox 的组合验证。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers

结果：
Backend focused tests: Passed 51
```

### 2026-06-09 Actor Handler 注册期契约校验

本轮收紧了 `YuoActorHandlerRegistry.Add` 的注册契约：

```text
注册 handler 时立即校验：

1. MessageType 必须实现 IActorMessage；
2. 普通 IActorMessage handler 不能声明 ResponseType；
3. IActorRequest<TResponse> handler 必须声明 ResponseType；
4. ResponseType 必须和 IActorRequest<TResponse> 的 TResponse 完全一致。
```

原因：

```text
YuoActorHandler<TMessage> / YuoActorRequestHandler<TRequest,TResponse> 这两个基类天然满足契约。
但 YuoCore.Actor.Ecs 暴露了 IYuoActorHandler，后续业务或 DI 工厂可以自定义 handler。
如果注册表不校验，错误会延迟到某条 Actor 消息真正派发时才暴露，排查成本高，并且可能表现为响应类型错配。
```

边界：

```text
该校验只发生在启动/扫描/注册阶段，不进入每条消息的热路径。
它不改变 ActorFiber mailbox 串行模型，也不改变 ActorRouter 的 Send/Invoke 协议。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Runtime.Actor.Tests/YuoCore.Runtime.Actor.Tests.csproj --disable-build-servers

结果：
Actor focused tests: Passed 49
Architecture focused tests: Passed 15
Runtime.Actor focused tests: Passed 4
```

### 2026-06-09 Location 迁移锁期间 Sender 缓存失效

本轮修复了一个 Location/Actor 迁移边界：

```text
旧行为：
YuoActorLocationSender 会缓存 LocationKey -> YuoActorAddress。
YuoActorLocationMigrationCoordinator 在迁移成功后才失效 sender cache。

风险：
如果 sender 已经缓存旧地址，迁移协调器拿到 Lock 但还没完成 Migrate，
这段窗口内 sender 可能不再 Resolve Location，直接把消息发到旧 Actor。
这样会绕过 locked location，破坏迁移期间的所有权边界。
```

新行为：

```text
YuoActorLocationMigrationCoordinator 在 LockAsync 成功后立即调用 InvalidateLocalSenderCaches(key, expectedAddress)。
YuoActorLocationSender 命中本地 cached address 时，默认仍会重新 Resolve 一次 Location 元数据。
如果发现 Location 已被锁定、地址已变化或 key 已不存在，会丢弃本地缓存，不会继续把消息发到旧 Actor。
如果迁移成功，新请求会解析到新地址。
如果迁移仍在进行，新请求会重新 Resolve，看到 locked entry 后按 YuoActorLocationSenderOptions.MaxLockedResolveRetries 等待或返回 409。
如果迁移失败并释放锁，新请求会重新解析旧地址。
```

新增测试：

```text
Migrate_invalidates_sender_cache_immediately_after_lock
LocationSender_rejects_locked_location_even_when_old_address_is_cached

测试流程：
1. sender 先访问一次，缓存 oldAddress。
2. Location 进入 locked migration 状态。
3. 迁移未完成期间再次通过 sender Invoke。
4. 断言该 Invoke 不会命中旧 Actor，而是等待 locked location 或返回 409。
5. coordinator 场景下放行迁移后，Invoke 命中新 Actor。
```

边界：

```text
YuoActorLocationSenderOptions.ValidateCachedAddressOnUse 默认 true。
如果高频单机项目显式关闭该选项，则会恢复旧的“缓存命中不查 Location”行为，必须由调用方保证没有迁移锁语义。
跨进程缓存失效仍需要可选分布式 Location/ServiceDiscovery/Transport 层保证 Resolve 能读到最新 locked entry，或使用短 TTL / 失效广播。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers

结果：
Actor focused tests: Passed 51
Location focused tests: Passed 19
Architecture focused tests: Passed 15
Build succeeded，0 warnings，0 errors
Solution tests: Passed 508
```

### 2026-06-09 默认 Load Suite 纳入 pooled WebSocket 编码

本轮把真实发送前的 V1 envelope pooled 编码纳入 `scripts/run-local-load-suite.sh` 默认 quick/soak 压测。

新增：

```text
scripts/run-local-load-suite.sh
  -> 默认 YUO_LOAD_POOLED_ENCODE=true
  -> 默认 YUO_LOAD_COMPARE_PREVIOUS=true
  -> 可通过 YUO_LOAD_BASELINE_DIR 指定显式 baseline
  -> 在 aoi-streams 后额外运行 aoi-streams-pooled-encode
  -> suite 结束后自动生成 summary.md 和 comparison.md
  -> 使用 tools/YuoCore.LoadTests --mode aoi-streams --pooled-encode-pushes

scripts/summarize-load-results.sh
  -> summary 表新增编码 p95 ms / 编码分配 MB / 编码字节 MB
  -> 即使 websocket-encode 不是最大热点，也能在摘要里看到真实编码成本
  -> summary 顶部写入 generated_at_utc / os / cpu / logical_cpus / memory_mb / dotnet_version / git_head / git_state
```

裁定：

```text
纯 aoi-streams 只能覆盖 AOI frame / dirty stream / subscription drain。
生产 WebSocket 发送还必须把 YuoGatewayOutboundMessage 编成二进制 envelope。
因此默认 load suite 必须同时保留纯 AOI drain 和 pooled encode 两个视角：

1. 纯 AOI drain 用来看状态同步调度与可见性成本；
2. pooled encode 用来看真实发送前协议编码和 ArrayPool 路径成本。

如果本地只想快速看非网络逻辑，可显式设置 YUO_LOAD_POOLED_ENCODE=false。
如果本轮不需要和上一轮对比，可显式设置 YUO_LOAD_COMPARE_PREVIOUS=false。
```

验证：

```text
scripts/run-local-load-suite.sh --quick

输出：
artifacts/load/20260609T011443Z-quick

summary:
aoi-streams-pooled-encode: passed=yes tick-p95=181.215ms allocated=105.317MB encode-p95=28.795ms encode-allocated=0.073MB encoded-bytes=84.462MB hotspot=drain p95=137.889ms
aoi-streams:               passed=yes tick-p95=178.012ms allocated=105.249MB
ecs-shape:                 passed=yes tick-p95=140.645ms allocated=328.223MB
local-composite-count:     passed=yes tick-p95=130.708ms allocated=30.853MB

bash -n scripts/run-local-load-suite.sh scripts/summarize-load-results.sh
结果：通过

scripts/summarize-load-results.sh artifacts/load/20260609T011443Z-quick
结果：summary 已写入环境元数据

YUO_LOAD_PLAYERS=100 YUO_LOAD_STATE_ENTITIES=100 YUO_LOAD_TICKS=1 YUO_LOAD_AOI_OBSERVERS=8 YUO_LOAD_PERSISTENCE_PER_TICK=100 scripts/run-local-load-suite.sh --quick
结果：通过，并自动输出 comparison.md
输出：artifacts/load/20260609T013601Z-quick
```

观察：

```text
在 1w players / 1w state entities / 4 shards / 5 ticks / 256 AOI observers 下，
pooled encode 的额外 GC 分配约 0.073MB，说明当前 pooled send 编码路径没有重新引入大分配。
tick p95 从纯 aoi-streams 的 178.012ms 到 pooled encode 的 181.215ms，短样本下属于同量级。
当前主要热点仍是 coordinator drain，而不是 WebSocket envelope 编码。
本次环境：WSL2 / AMD Ryzen 7 5800X / 16 logical CPUs / 约 48GB memory / .NET 10.0.108 / git dirty。
```

### 2026-06-09 Actor LocationSender 选项变更后的 NuGet Consumer Smoke

本轮在 `YuoActorLocationSenderOptions` 新增默认开启的 `ValidateCachedAddressOnUse` 后，重新运行 NuGet 包消费验证。

验证：

```text
scripts/run-pack-consumer-smoke.sh

结果：
packages=27
CoreNet10 consumer: build succeeded
UnityClientNetStandard consumer: build succeeded
ServerNet10 consumer: build succeeded
输出：artifacts/packages/consumer-smoke-20260609T012008Z
```

裁定：

```text
该选项属于 YuoCore.Actor 包的公共配置契约。
复跑 consumer smoke 证明当前 27 个可发布包仍可打包，且 Core / Unity 风格客户端 / Server 可选组合三个导入画像都能通过 NuGet restore/build。
本次验证不改变版本号；正式发布前仍需要按 docs/nuget-packaging.md 的版本策略统一裁定。
```

补充：

```text
scripts/run-pack-consumer-smoke.sh 的 CoreNet10 probe 已增强：

new YuoActorLocationSenderOptions(ValidateCachedAddressOnUse: true)
options.ValidateCachedAddressOnUse

这让 consumer smoke 能直接发现该 public API 被删、改名或构造签名不兼容。
```

验证：

```text
bash -n scripts/run-pack-consumer-smoke.sh
YUO_CONSUMER_PACKAGE_DIR=artifacts/packages/consumer-smoke-20260609T012008Z/packages scripts/run-pack-consumer-smoke.sh

结果：
CoreNet10 consumer: build succeeded，且直接编译 ValidateCachedAddressOnUse
UnityClientNetStandard consumer: build succeeded
ServerNet10 consumer: build succeeded
输出：artifacts/packages/consumer-smoke-20260609T012834Z
```

### 2026-06-09 Load 结果 Baseline 对比入口

本轮新增 `scripts/compare-load-results.sh`，用于对比两个 `artifacts/load/<run>` 目录里的同名 JSON。

能力：

```text
scripts/compare-load-results.sh <baseline-dir> <current-dir> [--output <summary.md>]

输出 markdown 表：
1. baseline/current passed；
2. config match；
3. tick p95 前后值和 delta；
4. allocated MB 前后值和 delta；
5. encode p95 前后值和 delta；
6. 当前有、baseline 没有的场景标记为 baseline missing；
7. baseline 有、当前没有的场景标记为 current missing。
```

裁定：

```text
这是人工趋势对比入口，不默认按 delta 失败。
原因是 quick 结果受机器负载、温度、WSL 状态、JIT/GC 波动影响较大；过早加入硬阈值会让短样本误报。
真正要 CI 失败，应先积累同硬件多轮 baseline，再按场景配置 p95/alloc/encode 的容忍区间。
delta 只有在 config match=yes 时才有直接对比意义；不同 players/entities/ticks/observers 的结果只能说明脚本链路可用。
```

验证：

```text
chmod +x scripts/compare-load-results.sh
bash -n scripts/compare-load-results.sh
scripts/compare-load-results.sh artifacts/load/20260608T223144Z-quick artifacts/load/20260609T011443Z-quick --output artifacts/load/20260609T011443Z-quick/comparison-vs-20260608T223144Z.md
临时目录模拟 current 缺失场景

结果：
load-comparison=artifacts/load/20260609T011443Z-quick/comparison-vs-20260608T223144Z.md
current 缺失场景可显示为 current pass=missing
comparison 表会显示 config match；小规模 smoke 和 1w baseline 对比时为 no
```

观察：

```text
aoi-streams: tick p95 180.754ms -> 178.012ms，allocated 166.397MB -> 105.249MB
ecs-shape: tick p95 152.381ms -> 140.645ms，allocated 基本持平
local-composite-count: tick p95 131.511ms -> 130.708ms，allocated 基本持平
aoi-streams-pooled-encode 是新增场景，旧 baseline 没有同名结果
```

### 2026-06-09 Persistence 停服 In-Flight 命令失败记录

本轮修复了一个停服可靠性边界：

```text
旧行为：
YuoPersistenceBackgroundService.ExecuteAsync 从队列中取出 batch 后，用 stoppingToken 执行实际写入。
Host StopAsync 会取消 stoppingToken。
如果某条命令已经从队列出队，但写入仍在进行，停服取消可能让该命令抛出 OperationCanceledException。
此时命令已经不在队列中，StopAsync 后续 drain pending queue 也看不到它。
```

新行为：

```text
当 ExecuteAsync 中的 in-flight 命令因为 stoppingToken 被取消而失败时：

1. 如果命令来自 outbox，先 MarkOutboxAckBlocked，避免后续 outbox ack 越过这条命令；
2. 使用 CancellationToken.None 调用 failure sink，尽量记录这条已经出队但未完成的命令；
3. 结束后台循环，StopAsync 继续 drain 仍在队列里的 pending 命令。
```

结果：

```text
启用 outbox 时：该命令不会被 ack，重启后可以 replay。
启用 dead-letter/failure sink 时：即使没有 outbox，也能留下失败记录，避免静默丢失。
未启用 outbox 且未配置 failure sink 时：仍无法保证崩溃级恢复，风险已记录为宿主裁剪项。
```

新增测试：

```text
Background_service_reports_inflight_command_canceled_by_stop

测试模拟 event store 已开始写入，然后 StopAsync 取消 stoppingToken。
断言 failure sink 收到这条 AppendEventCommand，且队列 pending 计数为 0。
```

验证：

```text
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers

结果：
Persistence.Hosting focused tests: Passed 14
Persistence focused tests: Passed 19
```

### 2026-06-09 NuGet Pack Smoke 覆盖全部可发布包

本轮把 `scripts/run-pack-smoke.sh` 从“代表性包 smoke”升级为“全部应发布库 smoke”。

旧覆盖范围：

```text
只覆盖 Abstractions / Ecs / Actor / Runtime / Location / Protocol / Client /
Client.StateSync / StateSync / Persistence / Config / Server 等代表性包。
```

新覆盖范围：

```text
覆盖 src 下全部应发布库，排除 YuoCore.Backend 示例宿主。

新增纳入：
Tasks / Timers
Actor.Ecs / Actor.Timers
Runtime.Actor / Runtime.Timers
Gateway / Network / Game
ServiceDiscovery / Server.ServiceDiscovery
Analyzers
```

这样可以更早发现以下问题：

```text
1. 可选包发布时才暴露的 ProjectReference 断裂；
2. Gateway / Network / Game 等包误带 Backend、ASP.NET Core、DB、Redis、日志或遥测依赖；
3. Analyzer 包无法独立 pack；
4. README、Description、ReleaseNotes 或 symbol package 缺失。
```

验证：

```text
scripts/run-pack-smoke.sh

结果：
packages=27
pack-smoke-output=artifacts/packages/pack-smoke-20260608T210734Z
未发现禁止依赖模式：
YuoCore.Backend
Microsoft.AspNetCore
Npgsql
StackExchange.Redis
Serilog
Dapper
OpenTelemetry
```

### 2026-06-09 NuGet Consumer Smoke

本轮新增 `scripts/run-pack-consumer-smoke.sh`，用于证明包不是只“能 pack”，而是能被全新项目通过 NuGet 正常安装和编译。

### 2026-06-09 Backend Persistence 指标导出闭环

修正点：

```text
YuoCore.Persistence.Hosting 已经提供 YuoPersistenceQueueMetrics，但 Backend 示例宿主只配置了 Prometheus endpoint，
没有 AddMeter(YuoPersistenceQueueMetrics.MeterName)，也没有确保 metrics 对象在宿主启动时被创建。
```

本轮调整：

```text
1. 新增 YuoPersistenceQueueMetricsHostedService；
2. DurableOutboxEnabled 时注册 YuoPersistenceQueueMetrics 和对应 hosted service；
3. Backend OpenTelemetry Metrics 增加 AddMeter(YuoPersistenceQueueMetrics.MeterName)；
4. Architecture 测试锁住 Backend 宿主必须继续导出该可选 meter。
```

职责边界：

```text
Persistence 核心包仍不依赖 Microsoft.Extensions.Hosting、OpenTelemetry 或 Prometheus。
Persistence.Hosting 只依赖 Hosting 抽象和 .NET Meter。
OpenTelemetry / Prometheus 仍只出现在 Backend 示例宿主。
```

### 2026-06-09 客户端最小重连恢复组合器

本轮新增 `YuoClientStateSyncReconnectRecovery`。

它解决的问题：

```text
YuoMmoClientConnection 已经能重连同一个 playerId；
YuoClientStateSyncRecovery 已经能按 knownTick / streamId 请求 replay 或 snapshot；
但 Web/Unity 调用方仍需要手写“重连后从当前 Mirror 游标恢复”的胶水逻辑。
```

当前语义：

```text
1. 调用 connection.ReconnectAsync()，复用已有 PlayerId；
2. 从 receiver.Mirror.Table.LastTick 读取 knownTick；
3. 从 receiver.LastStreamId 读取当前 StateSync stream；
4. 调用 StateSync recover 并把返回的 delta/snapshot 应用回同一个 receiver。
```

职责边界：

```text
该组合器不持有 WebSocket，不实现退避重试，不决定 UI 提示，不接触 Gateway/Backend/ECS。
真正的断线检测和重试策略仍由 Web/Unity 宿主决定。
```

### 2026-06-09 Redis Cache Refresh 失败可观测性

本轮补齐 Backend 持久化适配层的一个静默失败点：

```text
PlayerSnapshotStoreAdapter 在 PostgreSQL 写入成功后会 best-effort 刷新 Redis cache。
之前 Redis/cache SetAsync 失败会被直接吞掉，虽然不会影响 outbox ack，但也没有任何观测出口。
```

调整：

```text
1. PlayerSnapshotStoreAdapter 可选接收 ILogger<PlayerSnapshotStoreAdapter>；
2. cache refresh 失败时记录 Warning，包含 playerId 和原始异常；
3. 仍不抛出异常，保持 PostgreSQL durable truth / Redis best-effort cache 的裁定；
4. 测试覆盖 repository 已保存、cache 失败不外抛、warning 已记录。
```

边界：

```text
该能力只在 YuoCore.Backend 适配层。
YuoCore.Persistence 核心仍不依赖日志、Redis、PostgreSQL 或 OpenTelemetry。
```

追加修复：

```text
SnapshotPersistenceService 旧顺序是先 cache.SetAsync，再 durableQueue.EnqueueAsync / repository.SaveAsync。
这会让 Redis 故障阻止真正持久化，和“PostgreSQL / outbox 是 durable truth，Redis 是 best-effort cache”的裁定冲突。
```

新顺序：

```text
1. DurableOutboxEnabled=true：先写 durable outbox / queue，再 best-effort 刷新 cache；
2. DurableOutboxEnabled=false：先写 repository，再 best-effort 刷新 cache；
3. cache 刷新失败只记录 Warning，不让 Redis 故障阻止保存。
```

读取侧追加收口：

```text
SnapshotPersistenceService.GetAsync 现在会在 cache.GetAsync 异常时记录 Warning 并回落 repository。
如果调用方 cancellationToken 已取消，则 OperationCanceledException 继续抛出，不被当成 cache 故障吞掉。
```

写后 cache refresh 追加收口：

```text
repository / durable outbox 已提交后，cache refresh 属于纯 best-effort。
因此 cache.SetAsync 即使抛 OperationCanceledException，也只记录 Warning，不让调用方以为 durable save 失败，
也不阻止 YuoPersistenceBackgroundService 在 repository 保存成功后 ack outbox。
```

### 2026-06-09 WebSocket 非显式断线保存验证

本轮补了 `/ws` 端到端测试，覆盖客户端不发送 DisconnectRequest、直接关闭 WebSocket 的场景。

验证链路：

```text
1. TestServer 建立 WebSocket；
2. YuoMmoClient 登录、进地图、移动；
3. 客户端直接 CloseAsync socket；
4. GameWebSocketEndpoint finally 调用 TryDisconnect；
5. 使用 CancellationToken.None 保存离线快照；
6. 新 TestServer 使用同一 playerId 登录；
7. restored snapshot 保留 mapId、坐标和 lastInputSeq，并重新 Online=true。
```

边界：

```text
这证明的是正常 socket close / endpoint finally 的 best-effort 保存。
进程崩溃、机器断电或宿主被 kill 仍依赖周期保存、durable outbox、数据库事务和重启 replay。
```

覆盖画像：

```text
YuoCoreConsumer.CoreNet10
  net10.0
  YuoCore.Ecs / YuoCore.Runtime / YuoCore.Actor

YuoCoreConsumer.UnityClientNetStandard
  netstandard2.1
  YuoCore.Client.StateSync

YuoCoreConsumer.ServerNet10
  net10.0
  YuoCore.Server / YuoCore.Persistence.Hosting / YuoCore.Gateway / YuoCore.Network / YuoCore.Game
```

实现细节：

```text
脚本会为每个 profile 生成独立 NuGet.config。
本地包源使用绝对路径，避免临时项目目录改变后找不到 artifacts/packages。
consumer 代码使用 C# 8 兼容语法，避免 netstandard2.1 默认语言版本导致误报。
```

验证：

```text
YUO_CONSUMER_PACKAGE_DIR=artifacts/packages/pack-smoke-20260608T210734Z scripts/run-pack-consumer-smoke.sh

结果：
CoreNet10: Build succeeded, 0 warnings, 0 errors
UnityClientNetStandard: Build succeeded, 0 warnings, 0 errors
ServerNet10: Build succeeded, 0 warnings, 0 errors
consumer-smoke-output=artifacts/packages/consumer-smoke-20260608T211712Z
```

### 2026-06-09 Load Suite Summary

本轮新增 `scripts/summarize-load-results.sh`，并把它接入 `scripts/run-local-load-suite.sh`。

目的：

```text
load suite 原本会输出多个 JSON：
local-composite-count.json
aoi-streams.json
ecs-shape.json

这些 JSON 适合机器读取，但长测后很难一眼看到：
1. 哪个场景失败；
2. tick p95/max 是否接近阈值；
3. 当前主要热点分段是什么；
4. 分配量大概在哪个量级。
```

新行为：

```text
run-local-load-suite.sh 在三项压测结束后自动调用 summarize-load-results.sh。
summary.md 会写入同一个 artifacts/load/<timestamp>-quick|soak 目录。
如果任意 JSON 的 Passed=false，summary 脚本返回 exit code 2。
```

热点选择规则：

```text
local-composite:
  从 SegmentsMs 中选择 p95 最大的分段。

aoi-streams:
  在 move / drain / publish-drain / session-drain 中选择 p95 最大的分段。

ecs-shape:
  没有子分段，使用 tick 自身。
```

验证：

```text
scripts/summarize-load-results.sh artifacts/load/20260608T203325Z-quick

结果：
load-summary=artifacts/load/20260608T203325Z-quick/summary.md
```

追加默认 quick 验证：

```text
scripts/run-local-load-suite.sh --quick

结果：
load-suite-output=artifacts/load/20260608T212221Z-quick
load-summary=artifacts/load/20260608T212221Z-quick/summary.md

summary:
aoi-streams             passed=yes tick-p95=177.104ms allocated=166.42MB hotspot=drain p95=126.960ms
ecs-shape               passed=yes tick-p95=130.695ms allocated=328.24MB hotspot=tick p95=130.695ms
local-composite-count   passed=yes tick-p95=128.053ms allocated=30.853MB hotspot=actor-enqueue p95=50.513ms
```

### 2026-06-09 Postgres/Redis Snapshot Persistence 集成验证

本轮补了 `SnapshotPersistenceService + PostgresPlayerSnapshotRepository + RedisPlayerSnapshotCache` 的 Testcontainers 组合测试。

新增覆盖：

```text
Snapshot_persistence_uses_redis_cache_and_falls_back_to_postgres_when_testcontainers_enabled

流程：
1. 启动 PostgreSQL Testcontainer；
2. 启动 Redis Testcontainer；
3. 创建 player_snapshots schema；
4. 通过 SnapshotPersistenceService.SaveAsync 保存玩家快照；
5. 验证 Redis cache 能读到保存结果；
6. 删除 Redis key 后验证 GetAsync 能从 PostgreSQL 回落；
7. 手动写入 PostgreSQL 版本和 Redis 版本；
8. 验证 GetAsync 优先返回 Redis；
9. 再次删除 Redis key 后验证返回 PostgreSQL 版本。
```

实际发现并修复的问题：

```text
PostgresPlayerSnapshotRepository.GetAsync 原本依赖 Dapper 直接把 timestamptz 映射到 PlayerSnapshotRow(DateTimeOffset UpdatedAt) 构造参数。
真实 PostgreSQL + Npgsql 下，Dapper 看到的是 DateTime，导致构造函数匹配失败。
同时 PostgreSQL 未加引号的 alias 会被小写化，长期也容易让 Dapper 构造映射变脆。
```

修复：

```text
1. SELECT alias 改为双引号形式，例如 updated_at as "UpdatedAt"；
2. PlayerSnapshotRow.UpdatedAt 改为 DateTime；
3. ToDto 时统一转换成 UTC DateTimeOffset。
```

验证：

```text
RUN_TESTCONTAINERS=1 dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~PostgresRepositoryTests --disable-build-servers

结果：
Passed: 2

dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers

结果：
Passed: 52
```

### 2026-06-09 Durable Outbox + PostgreSQL BackgroundService 集成验证

本轮继续补上 `YuoDurablePersistenceQueue + YuoFilePersistenceOutbox + YuoPersistenceBackgroundService + PostgresPlayerSnapshotRepository` 的真实容器组合测试。

新增覆盖：

```text
Durable_outbox_background_service_writes_to_postgres_and_replays_unacked_command_when_testcontainers_enabled
```

流程：

```text
第一段：正常写回与 ack
1. 启动 PostgreSQL Testcontainer；
2. 创建 player_snapshots schema；
3. 创建 YuoPersistenceQueue；
4. 创建 YuoFilePersistenceOutbox；
5. 创建 YuoDurablePersistenceQueue；
6. 启动 YuoPersistenceBackgroundService；
7. durable.EnqueueAsync(SaveSnapshotCommand)；
8. 等待 PostgresPlayerSnapshotRepository 能读到快照；
9. StopAsync 后验证 outbox replay 为空。

第二段：模拟重启 replay 未 ack 命令
1. 直接向同一个文件 outbox append 新 SaveSnapshotCommand，但不进入内存队列；
2. 用新的 YuoDurablePersistenceQueue 读取同一个 outbox；
3. ReplayPendingAsync 把未 ack 命令放回新内存队列；
4. 启动新的 YuoPersistenceBackgroundService；
5. 等待 PostgreSQL 读到 replay 出来的快照；
6. StopAsync 后验证 outbox replay 为空。
```

该测试证明：

```text
1. durable queue 确实是先写 outbox，再进入内存队列；
2. BackgroundService 写入真实 PostgreSQL 成功后会 ack outbox；
3. 未 ack 命令能在“新队列/新服务”中 replay；
4. replay 后再次写入真实 PostgreSQL，并在成功后 ack；
5. outbox 逻辑没有把 PostgreSQL/Redis 依赖下沉到 YuoCore.Persistence 或 Persistence.Hosting。
```

验证：

```text
RUN_TESTCONTAINERS=1 dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~PostgresRepositoryTests --disable-build-servers

结果：
Passed: 3

dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers

结果：
Passed: 53
```

### 2026-06-09 Durable Replay 后 Redis Cache 刷新

本轮把 durable outbox 写回后的 Redis cache 一致性策略补到 Backend 适配层。

实现：

```text
PlayerSnapshotStoreAdapter
  -> 构造函数可选接收 IPlayerSnapshotCache
  -> SaveAsync 先写 IPlayerSnapshotRepository
  -> repository 写入成功后 best-effort 调用 cache.SetAsync
  -> cache 失败不会阻止 outbox ack
```

裁定：

```text
PostgreSQL 是 durable truth。
Redis 是重连/读取加速缓存。

因此后台 replay 写 PostgreSQL 成功后应尽量刷新 Redis，避免旧缓存继续遮住新 DB 状态。
但 Redis 抖动不能让已落库的 outbox 命令反复重放；否则 Redis 变成 durable commit 的一部分，会把缓存故障扩大成持久化积压。
```

测试增强：

```text
Durable_outbox_background_service_writes_to_postgres_and_replays_unacked_command_when_testcontainers_enabled

新增 stale cache 场景：
1. replay 前先向 Redis 写入同 playerId 的旧快照；
2. outbox replay 后由 BackgroundService 写 PostgreSQL；
3. PlayerSnapshotStoreAdapter best-effort 刷新 Redis；
4. 测试断言 Redis 中的 Name / MapId / LastInputSeq 已变成 replay 快照。
```

验证：

```text
RUN_TESTCONTAINERS=1 dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --filter FullyQualifiedName~PostgresRepositoryTests --disable-build-servers

结果：
Passed: 3

dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers

结果：
Passed: 53

dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers

结果：
Passed: 15
```

### 2026-06-09 WebSocket pooled envelope 编码回归保护

本轮在 Backend focused tests 中补充 `GameWebSocketEndpoint.RentServerMessage` 的直接回归测试。

覆盖：

```text
WebSocket_endpoint_rents_v1_server_envelope_without_losing_fields
WebSocket_endpoint_rented_v1_error_envelope_keeps_error_contract
WebSocket_endpoint_rented_legacy_message_keeps_legacy_envelope
```

验证重点：

```text
1. V1 无错误热路径走 pooled encoded message 后，version / opcode / requestId / serverSeq / payload 仍能被 YuoServerEnvelopeV1 正确解析。
2. V1 Error 路径仍保留 ErrorResponse 契约，并且 opcode 改为 ErrorResponse，避免手写 writer 绕过错误兼容路径。
3. legacy 模式仍把 Payload 当旧 ServerEnvelope 转发，不强行套 V1 envelope。
```

裁定：

```text
真实 TestServer WebSocket 和 browser-smoke 已经覆盖生产发送路径，但它们不直接断言 pooled buffer 中的 protobuf 字段。
本测试用于锁住刚刚优化过的高风险编码热路径，避免后续为了降低分配再次破坏协议兼容性。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers

结果：
Passed: 57

dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build --disable-build-servers

结果：
Build succeeded，0 warnings，0 errors
Passed: 507
```

### 2026-06-09 Unity WebSocket transport pending 释放加固

本轮加固 `clients/unity/YuoCore.UnityClient/YuoClientWebSocketTransport`。

改动点：

```text
1. CloseAsync 开始后立即标记 transport closing，后续 SendAsync 不再允许进入 pending 表。
2. CloseAsync / Dispose / receive loop 终止都会释放所有 pending request。
3. receive loop 即使是正常退出，只要 socket 已不再 Open，也会失败 pending request，避免客户端请求永久悬挂。
4. requestId=0 明确保留给服务端 push，Unity transport 不再允许客户端用 requestId=0 发送普通 request。
5. 修改保持在 Unity 可选适配包内，不向 YuoCore.Client / Protocol / Ecs / Actor 反向引入 WebSocket 依赖。
```

新增测试：

```text
tests/YuoCore.UnityClient.Tests

Close_async_fails_pending_request_without_server_response
Server_close_fails_pending_request_without_client_timeout
```

测试方式：

```text
测试内启动真实 Kestrel WebSocket server，Unity transport 通过 ClientWebSocket 连接。
覆盖客户端主动关闭、服务端主动关闭两条路径，断言没有服务端 response 时 pending request 会快速失败，而不是依赖请求 timeout。
```

验证：

```text
dotnet test tests/YuoCore.UnityClient.Tests/YuoCore.UnityClient.Tests.csproj --disable-build-servers

结果：
Passed: 2

dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers

结果：
Build succeeded，0 warnings，0 errors
```

### 2026-06-09 断线流程改为 Actor/Fiber 内处理

本轮把 `GameRuntime.TryDisconnect` 从直接修改玩家组件，改为发送 `DisconnectActorMessage` 到目标玩家 Actor。

改动前：

```text
TryDisconnect
  -> lock GameRuntime._gate
  -> 直接读取/修改 YuoPlayerPresenceComponent
  -> 直接 EnqueueAoiDirty / EnqueueMapEvent
```

改动后：

```text
TryDisconnect
  -> 查 playerRef / ActorId
  -> ActorRouter.InvokeAsync<bool>(DisconnectActorMessage)
  -> Tick 目标 GameShard Fiber
  -> DisconnectActorHandler 在目标 Fiber 内校验 sessionId 并修改 ECS 组件
  -> 返回是否断线成功
```

职责裁定：

```text
Gateway / HTTP / 停服清理可以继续调用 TryDisconnect，但它们不再直接拥有 ECS 写权限。
TryDisconnect 只负责投递和等待目标 Fiber 的 Actor request。
sessionId 校验仍在目标 Actor 内完成，错误 session 不会把玩家置离线，也不会产生 leave 事件。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers

结果：
Passed: 64
```

### 2026-06-09 Load baseline 对比阈值与 1w quick 复测

本轮增强 `scripts/compare-load-results.sh` 和 `scripts/run-local-load-suite.sh`。

新增能力：

```text
compare-load-results.sh
  -> --fail-tick-p95-delta-pct <percent>
  -> --fail-alloc-delta-pct <percent>

run-local-load-suite.sh
  -> YUO_LOAD_FAIL_TICK_P95_DELTA_PCT
  -> YUO_LOAD_FAIL_ALLOC_DELTA_PCT
```

语义：

```text
默认仍只生成 comparison.md，不因为性能波动失败。
只有显式设置阈值时，且 baseline/current 的 scenario config match=yes，才检查 tick p95 和 allocated MB 的相对退化。
这避免小规模 smoke、不同 ticks、不同 observers 的结果被错误拿来判定性能退化。
```

脚本验证：

```text
普通 comparison：通过
高阈值 comparison：通过
同目录 + 负阈值 comparison：按预期 exit-code=2

小规模 load suite：
YUO_LOAD_PLAYERS=100
YUO_LOAD_STATE_ENTITIES=100
YUO_LOAD_TICKS=1
YUO_LOAD_AOI_OBSERVERS=8
YUO_LOAD_PERSISTENCE_PER_TICK=100
YUO_LOAD_COMPARE_PREVIOUS=false
scripts/run-local-load-suite.sh --quick

结果：
load-suite-output=artifacts/load/20260609T022816Z-quick
```

随后运行默认 1w quick，并指定最近同配置 baseline：

```text
YUO_LOAD_BASELINE_DIR=artifacts/load/20260609T011443Z-quick
YUO_LOAD_FAIL_TICK_P95_DELTA_PCT=200
YUO_LOAD_FAIL_ALLOC_DELTA_PCT=200
scripts/run-local-load-suite.sh --quick
```

结果目录：

```text
artifacts/load/20260609T022906Z-quick
```

summary：

```text
aoi-streams-pooled-encode: passed=yes tick-p95=193.146ms allocated=105.319MB encode-p95=28.389ms encode-allocated=0.078MB encoded-bytes=84.462MB hotspot=drain p95=150.142ms
aoi-streams:               passed=yes tick-p95=180.418ms allocated=105.271MB hotspot=drain p95=133.947ms
ecs-shape:                 passed=yes tick-p95=160.418ms allocated=327.483MB hotspot=tick p95=160.418ms
local-composite-count:     passed=yes tick-p95=123.905ms allocated=30.853MB hotspot=actor-enqueue p95=46.299ms
```

与 `20260609T011443Z-quick` 对比：

```text
aoi-streams: tick p95 178.012ms -> 180.418ms，+1.352%；allocated +0.021%
aoi-streams-pooled-encode: tick p95 181.215ms -> 193.146ms，+6.584%；allocated +0.002%；encode p95 28.795ms -> 28.389ms
ecs-shape: tick p95 140.645ms -> 160.418ms，+14.059%；allocated -0.226%
local-composite-count: tick p95 130.708ms -> 123.905ms，-5.205%；allocated 基本持平
```

裁定：

```text
本轮断线 Actor 化和 Unity transport 加固后，1w quick 没有出现严重性能退化。
ecs-shape p95 上升 14% 但仍在历史短样本波动范围内，且分配未上升；暂不直接优化 ECS 调度。
主要热点判断不变：AOI drain、100组件/100system tick、actor enqueue。
后续如果 soak 或多轮同配置 baseline 显示 ecs-shape 稳定恶化，再进入 ECS 调度专项，而不是牺牲 system 异常隔离。
```

### 2026-06-09 Persistence 停服关闭写端语义

本轮强化 `YuoPersistenceQueue` 与 `YuoPersistenceBackgroundService` 的停服可靠性。

问题：

```text
StopAsync 会 drain 当前队列，但队列没有关闭写端。
如果停服期间仍有生产者继续 EnqueueAsync，命令可能写在 drain 之后，形成无人处理的悬挂队列项。
```

改动：

```text
YuoPersistenceQueue
  -> 新增 TryComplete(Exception? error = null)
  -> 关闭写端后，未来 EnqueueAsync / EnqueueBatchAsync 抛 ChannelClosedException
  -> 已进入队列的命令仍可 TryDequeueBatch / DequeueBatchAsync drain

YuoPersistenceBackgroundService
  -> StopAsync 默认先调用 queue.TryComplete()
  -> 然后 base.StopAsync 停止后台循环
  -> 再按 DrainOnStop drain 已入队命令
  -> ExecuteAsync 识别 ChannelClosedException 并正常退出
```

配置：

```text
YuoPersistenceWriterOptions.CompleteQueueOnStop = true
```

默认值为 `true`，用于正式宿主的安全停服。
如果自定义宿主确实需要 StopAsync 后继续接管同一个 queue，可以显式设置为 `false`。

验证：

```text
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
结果：Passed: 20

dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
结果：Passed: 16
```

### 2026-06-09：Persistence 停服语义 NuGet Consumer 验证

在 `YuoPersistenceQueue.TryComplete` 和 `YuoPersistenceWriterOptions.CompleteQueueOnStop` 进入 public API 后，补跑 NuGet consumer smoke：

```text
scripts/run-pack-consumer-smoke.sh
结果：通过。
输出：artifacts/packages/consumer-smoke-20260609T023551Z
```

覆盖：

```text
CoreNet10 consumer restore/build passed
UnityClientNetStandard consumer restore/build passed
ServerNet10 consumer restore/build passed
```

ServerNet10 临时 consumer 直接引用并编译：

```text
YuoPersistenceQueue
YuoPersistenceBackgroundService
YuoPersistenceWriterOptions
YuoPersistenceWriterOptions(CompleteQueueOnStop: false)
YuoPersistenceQueue.TryComplete()
```

结论：

```text
Persistence 停服关闭写端语义不仅通过源码测试，也已通过 NuGet 包消费边界验证。
外部服务端项目可以通过 YuoCore.Persistence.Hosting 组合使用该 API。
```

### 2026-06-09：GameRuntime 单玩家读路径 Actor 化

本轮继续收口 Backend 示例宿主的 ECS 所有权边界。

改动：

```text
GameRuntime.TryGetSnapshot
  -> 不再直接读取目标玩家 Entity 组件
  -> 改为投递 ReadPlayerSnapshotActorMessage
  -> 由目标 GameShard Fiber 内的 ReadPlayerSnapshotActorHandler 生成 YuoPlayerSnapshotDto

GameRuntime.TryGetAoiObserverState
  -> 不再直接读取 Presence/Transform 组件
  -> 改为投递 ReadAoiObserverStateActorMessage
  -> 由目标 GameShard Fiber 内生成轻量 GameAoiObserverState
```

语义变化：

```text
读取快照会先 drain 目标 Fiber mailbox。
因此如果读取前已经投递了 ApplyClick / Move 等消息，读到的是“处理到该读请求为止”的强一致状态。
这更适合 Gateway、保存和状态同步对外语义，但不再支持用 TryGetSnapshot 观察未处理 mailbox 的旧状态。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
结果：Passed: 65
```

剩余边界：

```text
TryGetWorldSnapshot / BuildAoiStreamSnapshots 是跨玩家聚合读路径。
它们已经不再直接扫 Entity 组件，而是读取 GameRuntime 内的 YuoPlayerWorldStateDto 投影表。
后续应把这张投影表迁移到真正的 Map/Room Fiber 所有者内。
```

### 2026-06-09：GameRuntime 世界状态投影表

本轮新增 `GameRuntime` 内部世界状态投影：

```text
Dictionary<Guid, YuoPlayerWorldStateDto> _worldStates
```

更新入口：

```text
CreatePlayer / SeedPlayers
LoginPlayer
TryRestorePlayer
TryApplyClick
ApplyClickActorHandler
EnterMapActorHandler
MoveActorHandler
DisconnectActorHandler
```

读取入口：

```text
BuildMapSnapshot
BuildAoiStreamSnapshots
TryBuildAoiStreamSnapshotsFromIndex
EnqueueMapEvent 的同图在线玩家筛选
```

意义：

```text
跨玩家世界快照和 AOI stream 聚合不再为了读状态反复访问玩家 Entity 组件。
这把 Backend 示例从“全局锁下扫 ECS”推进到“写侧刷新 DTO 投影、读侧读取投影”。
它是未来 Map/Room snapshot projection 的过渡实现，不引入 ASP.NET、数据库、Redis 或网络依赖到核心模块。
```

并发修正：

```text
_worldStates 使用独立 worldStateGate 保护。
写侧在 UpdateWorldStateProjection 中更新投影。
读侧先复制 YuoPlayerWorldStateDto[]，再做地图快照、AOI stream 分组和事件广播筛选。
这样 TickParallelAsync 下多个 shard 同时刷新投影时，不会和跨玩家聚合读发生 Dictionary 并发读写。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
结果：Passed: 65

dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
结果：Passed: 16

YUO_LOAD_PLAYERS=500
YUO_LOAD_STATE_ENTITIES=500
YUO_LOAD_TICKS=2
YUO_LOAD_AOI_OBSERVERS=32
YUO_LOAD_PERSISTENCE_PER_TICK=100
YUO_LOAD_COMPARE_PREVIOUS=false
scripts/run-local-load-suite.sh --quick

结果：Passed
输出：artifacts/load/20260609T025438Z-quick
local-composite-count p95=22.165ms allocated=0.58MB
aoi-streams p95=26.702ms allocated=2.31MB
aoi-streams-pooled-encode p95=28.434ms allocated=2.30MB
ecs-shape p95=17.534ms allocated=15.75MB
```

### 2026-06-09：投影读路径 1w quick 退化与修复

投影表落地后，默认 1w quick 首次暴露严重退化：

```text
YUO_LOAD_BASELINE_DIR=artifacts/load/20260609T022906Z-quick
YUO_LOAD_FAIL_TICK_P95_DELTA_PCT=200
YUO_LOAD_FAIL_ALLOC_DELTA_PCT=200
scripts/run-local-load-suite.sh --quick
```

未优化结果：

```text
artifacts/load/20260609T025715Z-quick
aoi-streams p95=534.177ms
coordinator-drain p95=495.815ms
结果：失败，不能接受。
```

原因分析：

```text
TryGetSnapshot / TryGetAoiObserverState Actor 化后，AOI session drain 热路径每个 session 都会触发一次目标 Actor request 和 Fiber tick。
在 256 observers、5 tick 的 quick 场景里，同一帧会重复 flush 多个 GameShard，导致 coordinator drain 被放大。
此外 click handler 刷新世界投影会让 local-composite 的点击热路径争用 worldStateGate；点击不改变 AOI 可见状态，不应刷新 world projection。
```

修复：

```text
GameRuntime.TryGetProjectedAoiObserverState
  -> 只读 YuoPlayerWorldStateDto 投影
  -> 不触发 Actor request / Fiber tick

GameAoiStreamPushCoordinator.DrainPushes
  -> 用 TryGetProjectedAoiObserverState 读取 mapId

GameStateSyncPushDispatcher.CreateAoiStreamSessionPushes
  -> 默认 AOI query policy 用投影 observer state
  -> 只有自定义旧 IGameAoiVisibilityPolicy 需要完整快照时才走 TryGetSnapshot 慢路径

ApplyClickActorHandler / TryApplyClick
  -> 不再刷新 world projection
  -> 快照保存仍由 TryGetSnapshot 直接读 ECS，点击数据不会丢
```

复测：

```text
artifacts/load/20260609T030633Z-quick

aoi-streams:               p95=210.737ms allocated=101.246MB
aoi-streams-pooled-encode: p95=226.814ms allocated=101.316MB encode-p95=28.998ms
ecs-shape:                 p95=156.187ms allocated=327.427MB
local-composite-count:     p95=144.780ms allocated=30.822MB
```

与 `artifacts/load/20260609T022906Z-quick` 对比：

```text
aoi-streams:               180.418ms -> 210.737ms，+16.805%；allocated -3.823%
aoi-streams-pooled-encode: 193.146ms -> 226.814ms，+17.431%；allocated -3.800%
ecs-shape:                 160.418ms -> 156.187ms，-2.638%
local-composite-count:     123.905ms -> 144.780ms，+16.848%；allocated -0.102%
```

裁定：

```text
退化已从 500ms 级别收回到 200ms 级别，且分配没有增长。
但 aoi-streams / pooled-encode / local-composite 仍有 16%~17% p95 上升，不能视为“性能无影响”。
当前 quick 样本只有 5 tick，短样本抖动较大；下一步应跑多轮或 soak 后再决定是否继续优化。
已知剩余热点仍是 AOI coordinator drain、first-session frame publish 和 actor enqueue。
```
