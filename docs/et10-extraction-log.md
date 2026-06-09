# ET10 能力吸收记录

本文档记录每轮从 ET10 分析、提取并迁入 YuoCore 的能力。

## 2026-06-08：Web TypeScript 客户端 SDK 雏形

来源参考：

```text
ET 的客户端和服务端共享协议，但客户端不应该引用服务端 Scene/Fiber/Actor/数据库宿主。
YuoCore 已有 C# YuoCore.Client，本轮补上 Web TypeScript 最小 SDK，使 Web 和 Unity 在协议语义上对齐。
```

YuoCore 落地：

```text
clients/web/yuocore-client-ts/package.json
clients/web/yuocore-client-ts/tsconfig.json
clients/web/yuocore-client-ts/src/index.ts
clients/web/yuocore-client-ts/README.md
docs/import-profiles.md
docs/web-unity-mmo-architecture.md
docs/ai-development-guide.md
README.md
```

实现：

```text
YuoClientSession 管理 requestId/clientSeq、encodeRequest、decodeResponse 和 call。
YuoProtocolOpCodes 与 C# YuoCoreProtocolOpCodes 保持一致。
YuoWebSocketTransport 基于 request_id 管理 pending request/response。
内置最小 protobuf varint/length-delimited 编解码，只处理 YuoClientEnvelopeV1/YuoServerEnvelopeV1/ErrorResponse。
业务消息通过 YuoMessageCodec<T> 注入，不绑定具体 protobuf 生成器。
```

边界控制：

```text
Web SDK 不引用服务端 ActorId、FiberId、LocationKey、GatewaySession 或 Entity。
Web SDK 不包含 ASP.NET Core、数据库、Redis、服务端宿主或游戏渲染/UI。
clients/web/yuocore-client-ts/dist 和 node_modules 已加入 .gitignore。
```

验证：

```bash
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

## 2026-06-08：Persistence outbox ack 阻断语义

来源参考：

```text
ET/Actor 风格的可靠消息和持久化队列必须优先保证“不丢”。
在 ack-through 模型下，一旦越过失败 sequence，就会把失败消息永久跳过。
```

YuoCore 落地：

```text
src/YuoCore.Persistence.Hosting/YuoPersistenceBackgroundService.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
docs/risk-and-decisions.md
```

修复：

```text
YuoPersistenceBackgroundService 记录第一个失败 outbox sequence。
后续成功命令如果 sequence >= 失败 sequence，不再执行 AcknowledgeThroughAsync。
```

边界控制：

```text
基础 Persistence 仍不依赖 Hosting。
Hosting 适配层只修正 ack 语义，不引入数据库、Redis 或日志实现。
```

风险和取舍：

```text
修复后可能 replay 已成功的后续命令。
这是可接受的保守语义：重复写入可通过幂等解决，丢失写入不可接受。
更细粒度去重需要后续设计 per-entry ack 或区间 ack。
```

验证：

```text
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
```

## 2026-06-08：Location lock 保护 Remove

来源参考：

```text
ET 的 Location 迁移依赖 lock 防止旧地址和新地址并发覆盖。
YuoCore 的 lock 也必须覆盖所有写操作，包括删除。
```

YuoCore 落地：

```text
src/YuoCore.Location/InMemoryYuoLocationRegistry.cs
tests/YuoCore.Location.Tests/YuoLocationRegistryTests.cs
docs/risk-and-decisions.md
```

修复：

```text
RemoveAsync 对未过期 lock 返回 false。
过期 lock 会先被清理并递增 version，然后才允许删除。
```

边界控制：

```text
不新增分布式实现。
不改变 Register/Migrate/Unlock 接口。
如果未来需要 owner-aware 删除，另开显式 API。
```

验证：

```text
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --disable-build-servers
```

结果：

```text
TypeScript strict typecheck: Passed
Web TypeScript SDK runtime smoke: Passed
```

## 2026-06-08：客户端 SDK 请求超时

来源参考：

```text
ET 风格 request/call 需要有统一超时边界。
客户端 SDK 不应该要求每个业务调用都手写 CancellationTokenSource / AbortController，否则 Web 和 Unity 行为很容易分叉。
```

YuoCore 落地：

```text
src/YuoCore.Client/YuoClientSessionOptions.cs
src/YuoCore.Client/YuoClientSession.cs
tests/YuoCore.Client.Tests/YuoClientSessionTests.cs
clients/web/yuocore-client-ts/src/index.ts
clients/web/yuocore-client-ts/scripts/smoke.mjs
clients/web/yuocore-client-ts/README.md
```

实现：

```text
C# YuoClientSessionOptions.RequestTimeout 通过 linked CancellationTokenSource 传给 IYuoClientTransport。
TypeScript session.call 支持旧 AbortSignal 参数，也支持 { signal, timeoutMs }。
YuoWebSocketTransport 在 socket.send 抛错时清理 pending request。
```

边界控制：

```text
不把 WebSocket、Gateway、Backend、Actor、Runtime 或 ECS 依赖引入 YuoCore.Client。
超时只是客户端 call 层预算；具体网络实现仍由 Unity/Web transport 负责。
```

验证：

```bash
dotnet test tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

## 2026-06-08：Unity 客户端 WebSocket 适配

来源参考：

```text
ET 的客户端可以共享协议语义，但不同平台的连接实现应留在客户端适配层。
Unity 需要一个最小可编译 transport 示例，但不能让 YuoCore.Client 直接依赖 UnityEngine 或具体网络实现。
```

YuoCore 落地：

```text
clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj
clients/unity/YuoCore.UnityClient/YuoClientWebSocketTransport.cs
clients/unity/YuoCore.UnityClient/README.md
```

实现：

```text
YuoClientWebSocketTransport 实现 IYuoClientTransport。
ConnectAsync 建立 ClientWebSocket 连接并使用 yuocore.v1 子协议。
SendAsync 串行发送一个 binary request 并接收一个 binary response。
CloseAsync / Dispose 管理连接生命周期。
```

边界控制：

```text
该项目只引用 YuoCore.Client。
不引用 UnityEngine、服务端 Actor/Fiber/Location/ECS、Gateway、Backend、数据库或 Redis。
Unity WebGL 需要单独平台适配，不使用 ClientWebSocket。
```

验证：

```bash
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
```

## 2026-06-08：客户端 StateSync 推送接收器

来源参考：

```text
ET 的客户端和服务端都通过统一消息分发进入业务处理。
YuoCore 的 C# 客户端也需要一个可选接收器，专门承接 Gateway 主动推送的 Snapshot/Delta，
否则 request/response SDK 和状态同步推送会混在同一个 API 里。
```

YuoCore 落地：

```text
src/YuoCore.Client.StateSync/YuoClientStateSyncReceiver.cs
tests/YuoCore.Client.StateSync.Tests/YuoClientStateSyncReceiverTests.cs
docs/module-dependency-matrix.md
docs/import-profiles.md
docs/nuget-packaging.md
docs/ai-development-guide.md
```

修复：

```text
新增 YuoCore.Client.StateSync 可选包。
它识别 StateSnapshot / StateDelta opcode，把 protobuf DTO 转成 StateSync 纯模型，并应用到 YuoStateSyncMirror。
非 StateSync opcode 返回 Ignored，避免抢占业务消息分发。
错误 envelope 抛 YuoClientProtocolException，保持客户端协议错误模型统一。
```

边界控制：

```text
Client.StateSync 只依赖 Client 和 StateSync.Protocol。
不引用 ECS、Gateway、Actor、Runtime、Backend、UnityEngine、WebSocket 或数据库。
```

验证：

```text
dotnet test tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj --disable-build-servers
```

## 2026-06-08：Actor handler 启动期扫描报告

来源参考：

```text
ET 的消息 handler 在启动时统一收集，业务运行时只做消息类型到 handler 的快速分发。
YuoCore 已经有 AddFromAssembly，但缺少可观测扫描结果；对大型 MMO 来说，启动时知道哪些 handler 被注册/跳过很重要。
```

YuoCore 落地：

```text
src/YuoCore.Actor.Ecs/Handlers/YuoActorHandlerRegistry.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

修复：

```text
新增 YuoActorHandlerScanReport。
新增 AddFromAssemblies / AddFromAssemblyWithReport / AddFromAssembliesWithReport。
保留原 AddFromAssembly 兼容入口。
```

边界控制：

```text
扫描能力仍在 Actor.Ecs，不进入纯 Actor。
纯 Actor 不依赖 ECS，ECS handler 才能拿 YuoEntity 直接操作组件。
反射扫描只用于启动期，消息分发热路径不增加锁或反射。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
```

## 2026-06-08：ServerHost 统一 Actor handler 启动装配入口

来源参考：

```text
ET 的 Scene/Fiber 启动阶段会集中完成系统和消息 handler 收集。
YuoCore 的 ServerHost 也应该提供一个正式入口，而不是让宿主到处手动 new registry / Add handler。
```

YuoCore 落地：

```text
src/YuoCore.Server/YuoServerHost.cs
tests/YuoCore.Server.Tests/YuoServerHostTests.cs
```

修复：

```text
新增 AddActorHandlersFromAssembly / AddActorHandlersFromAssemblies。
新增 ActorHandlerScanReports，保存启动期扫描报告。
Start 后拒绝继续扫描 handler。
```

边界控制：

```text
该能力属于 Server 装配层。
Actor.Core 不知道 ECS handler，Actor.Ecs 仍是 Entity handler 的实际归属。
运行期消息分发仍然通过 YuoEntityActorDispatcher 的字典查找，不引入反射。
```

验证：

```text
dotnet test tests/YuoCore.Server.Tests/YuoCore.Server.Tests.csproj --disable-build-servers
```

## 2026-06-08：Web SDK StateSync Mirror

来源参考：

```text
ET 的客户端接收服务端广播后，会在本地维护表现层可消费的状态。
YuoCore Web SDK 不应该知道 ECS/Actor/Fiber，但应该能消费通用 Snapshot/Delta 协议形状。
```

YuoCore 落地：

```text
clients/web/yuocore-client-ts/src/index.ts
clients/web/yuocore-client-ts/scripts/smoke.mjs
clients/web/yuocore-client-ts/README.md
docs/import-profiles.md
```

修复：

```text
新增 YuoStateSyncMirror 和 YuoStateSyncReceiver。
新增 StateSnapshotMessage / StateDeltaMessage 的最小 protobuf decode。
smoke 覆盖 snapshot、delta、ignored push 和 BaseTick gap。
```

边界控制：

```text
Web SDK 不引入服务端 ActorId/FiberId/LocationKey。
不引入 ECS，不解析业务 payload，不创建 UI。
业务 protobuf 生成代码仍由上层接入。
```

验证：

```text
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
```

护栏：

```text
Architecture tests 新增 Unity_client_adapter_stays_on_client_transport_boundary。
它会阻止 Unity 适配层引用 UnityEngine、服务端 Actor/ECS/Gateway/Backend/Runtime/Persistence 等命名空间。
```

## 2026-06-08：StateSync Protobuf 桥接包

来源参考：

```text
ET 风格协议层应能承载通用状态同步消息，但 StateSync 纯逻辑不应该反向依赖外部 Protobuf。
Web/Unity 客户端需要稳定的 StateSnapshot/StateDelta DTO，不能只依赖示例 WorldSnapshot。
```

YuoCore 落地：

```text
src/YuoCore.Protocol/Contracts/yuocore.proto
src/YuoCore.Protocol/YuoCoreProtocolCatalog.cs
src/YuoCore.StateSync.Protocol/YuoStateSyncProtocolMapper.cs
tests/YuoCore.StateSync.Protocol.Tests/YuoStateSyncProtocolMapperTests.cs
```

实现：

```text
新增 NetworkEntityState、StateChangeMessage、StateSnapshotMessage、StateDeltaMessage。
新增 StateSnapshot=106、StateDelta=107 opcode，并同步 Web TS SDK opcode 常量。
YuoStateSyncProtocolMapper 支持 snapshot/delta 双向转换，保留 delta BaseTick。
转换后的 delta 可直接交给 YuoStateSyncMirror 应用。
```

边界控制：

```text
YuoCore.StateSync 仍不引用 Protocol。
YuoCore.StateSync.Protocol 是可选桥接包，只引用 StateSync 和 Protocol。
不把 Gateway、Backend、WebSocket、数据库或 Redis 引入 StateSync 核心。
```

验证：

```bash
dotnet test tests/YuoCore.StateSync.Protocol.Tests/YuoCore.StateSync.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
```

## 2026-06-08：Persistence 文件 outbox/WAL 基础

来源参考：

```text
ET 的服务端消息和状态写入通常强调先进入可靠队列/日志，再由当前执行上下文或后台服务处理。
YuoCore 的 PersistenceQueue 已经隔离慢 IO，但只有内存队列；进程崩溃时已入队未写出的命令会丢失。
```

YuoCore 落地：

```text
src/YuoCore.Persistence/YuoPersistenceQueue.cs
src/YuoCore.Persistence/YuoPersistenceOutbox.cs
src/YuoCore.Persistence.Hosting/YuoPersistenceBackgroundService.cs
tests/YuoCore.Persistence.Tests/YuoPersistenceQueueTests.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
docs/risk-and-decisions.md
```

实现：

```text
IYuoPersistenceOutbox 定义 Append / Replay / AcknowledgeThrough / Compact。
YuoFilePersistenceOutbox 使用本地长度前缀二进制帧保存 SaveSnapshotCommand / AppendEventCommand。
YuoDurablePersistenceQueue 先 append outbox，再 enqueue YuoOutboxPersistenceCommand 到内存队列。
重启后 ReplayPendingAsync 可以把未 ack 命令重新放入队列。
YuoPersistenceBackgroundService 写入 Snapshot/Event store 成功后 ack outbox sequence。
业务写入成功后的 ack 不使用 stoppingToken，避免停服取消打断本地 ack 造成重复 replay。
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

边界控制：

```text
基础 Persistence 包没有新增 PostgreSQL、Redis、ASP.NET Core、Hosting 或远程网络依赖。
文件 outbox 是崩溃恢复基础，不是 exactly-once；如果 store 成功但 ack 失败，仍可能 replay，业务存储必须做幂等。
数据库 outbox 表结构、事务边界、事件去重和指标暴露仍记录为待裁定项。
```

## 2026-06-08：local-composite 压测 gate 化

来源参考：

```text
ET 类框架长期演进不能只看一次性控制台输出。
YuoCore 的目标包含 1w 玩家/实体、100 组件、100 system、多 Fiber/Actor/StateSync/Persistence 的长期压测，因此需要机器可读结果和阈值失败。
```

YuoCore 落地：

```text
tools/YuoCore.LoadTests/Program.cs
docs/implementation-and-performance-notes.md
```

实现：

```text
local-composite 新增 --json-output <path>。
local-composite 新增 --csv-output <path>，输出逐 tick 分段耗时。
local-composite 新增 --duration-mode，按 --duration 秒数持续运行。
local-composite 新增 --fail-p95-ms <ms> 和 --fail-max-ms <ms>。
超过阈值时输出 threshold-failed，并设置 exit code 2。
JSON 输出包含 tick stats、segment stats、actor/state/persistence 计数、allocated MB、Runtime GC/ThreadPool 指标和阈值配置。
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

边界控制：

```text
LoadTests 是工具项目，可以引用 Backend/Game/StateSync/Persistence。
这次没有把压测、JSON 或阈值逻辑下沉到 Ecs/Runtime/Actor 核心。
CSV 逐 tick 明细是诊断模式；默认不开 CSV 时不会采集 per-tick allocated 明细，避免额外采样污染常规压测。
```

后续修正：

```text
初版 duration/CSV 实现会在不开 CSV 时也构造逐 tick 明细。
已改为只有 --csv-output 存在时才采集 LocalCompositeTickSample 和 per-tick allocated。
默认 JSON/gate 路径只保留汇总样本和运行前后 Runtime GC/ThreadPool 指标。
```

轻量路径复测：

```text
local-composite 1w count short: avg=66.992ms p95=119.813ms max=119.813ms
actor=50,000 processed=50,000 state=50,000 persistence=50,000 pending=0
allocated=32.40MB gc0=3 gc1=3 gc2=1
```

## 2026-06-08：NuGet 拆包与版本策略

来源参考：

```text
ET 的能力是按模块组合使用，但 YuoCore 还要兼顾单机、Unity、Web 客户端和 MMO 服务端。
因此模块拆分不能只停留在源码目录层面，也需要落实到 NuGet 包边界和 pack 验证。
```

YuoCore 落地：

```text
src/Directory.Build.props
src/YuoCore.Backend/YuoCore.Backend.csproj
docs/nuget-packaging.md
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
.gitignore
```

实现：

```text
src 下的库统一继承 PackageId、VersionPrefix、RepositoryUrl、PackageOutputPath、符号包和 README 打包规则。
YuoCore.Backend 标记 IsPackable=false，明确只是 ASP.NET Core 示例宿主。
docs/nuget-packaging.md 记录核心包、Actor/Fiber 包、客户端/Gateway 包、游戏域/StateSync 包、持久化/服务端装配包和 Analyzer 包。
Architecture tests 增加 NuGet 元数据、Backend 不可 pack、NuGet 文档覆盖所有 src 项目的护栏。
artifacts/ 加入 .gitignore，避免本地 nupkg/snupkg 输出进入工作树。
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
YuoCore.Ecs pack smoke: nupkg/snupkg created
YuoCore.Client pack smoke: nupkg/snupkg created
YuoCore.Persistence.Hosting pack smoke: nupkg/snupkg created
Full build: Build succeeded, 0 warnings, 0 errors
Full tests: Passed all test projects
```

边界控制：

```text
Backend、数据库、Redis、ASP.NET Core、WebSocket 和远程分布式实现仍然不会进入 Ecs/Runtime/Actor 核心。
NuGet 规则服务于可裁剪导入，而不是把所有模块打成一个大包。
```

## 2026-06-08：Actor Handler 工厂扫描与业务错误码

参考思想：

```text
ET10 的 handler 扫描在启动阶段完成，运行时只走类型表查询。
YuoCore 也需要自动扫描能力，但不能为了 DI 把 Microsoft.Extensions.DependencyInjection 强行塞进 Actor 核心。
```

本轮落地：

```text
src/YuoCore.Actor/Abstractions/ActorError.cs
src/YuoCore.Actor.Ecs/Handlers/YuoActorHandlerRegistry.cs
src/YuoCore.Backend/Game/GameRuntime.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

实现：

```text
ActorError 新增 BadRequest(400)，用于业务参数无效。
YuoActorHandlerRegistry.AddFromAssembly 新增 factory overload。
无参 AddFromAssembly 继续保留，内部复用默认无参构造工厂。
Backend GameRuntime 改为通过 AddFromAssembly(factory) 注册 ApplyClick/EnterMap/Move handler。
```

边界控制：

```text
不引入 DI 包到 Actor.Core 或 Actor.Ecs。
扫描仍建议只在启动阶段执行，运行时 dispatch 路径仍是无锁字典查询。
有依赖的 handler 由宿主提供工厂构造，Actor 核心只知道 IYuoActorHandler。
```

验证：

```bash
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj --disable-build-servers
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

结果：

```text
Actor focused tests: Passed 42
Backend focused tests: Passed 16
Full build: Build succeeded
Full tests: Passed all test projects
```

## 2026-06-08：Persistence 停服 drain

来源参考：

```text
ET 的服务停服需要尽量把当前执行上下文里已经接收的关键消息处理完，不能随取消直接丢弃内存队列。
YuoCore 的 PersistenceQueue 是 GameShard 到慢 IO 的隔离层，也需要基本的停服 drain 语义。
```

问题：

```text
YuoPersistenceBackgroundService 收到 StopAsync 后，BackgroundService 会取消 ExecuteAsync 的 stoppingToken。
原循环在取消后直接退出，队列里已经到达但还没写入的 SaveSnapshotCommand / AppendEventCommand 可能留在内存里。
```

YuoCore 落地：

```text
src/YuoCore.Persistence/YuoPersistenceQueue.cs
src/YuoCore.Persistence.Hosting/YuoPersistenceBackgroundService.cs
tests/YuoCore.Persistence.Tests/YuoPersistenceQueueTests.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
```

修复：

```text
YuoPersistenceQueue 新增 TryDequeueBatch(maxCount)，只 drain 当前已到达命令，不等待新命令。
YuoPersistenceBackgroundService.StopAsync 在 base.StopAsync 后执行可配置 drain。
新增 YuoPersistenceWriterOptions.DrainOnStop 和 MaxStopDrainBatches，默认开启 drain，并限制最大批次数，避免停服被持续写入拖死。
Stop drain 复用原有 ExecuteCommandWithRetryAsync、StopOnCommandError 和 failure sink 语义。
```

边界控制：

```text
基础 YuoCore.Persistence 不新增 Hosting、日志、数据库或 Redis 依赖。
Hosting drain 只在 YuoCore.Persistence.Hosting 可选包中实现。
这是进程内内存队列 drain，不是 durable outbox/WAL；进程崩溃、机器断电、容器被 SIGKILL 时仍需要后续持久化 outbox 或 WAL。
```

验证：

```text
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

## 2026-06-08：Web/Unity 客户端协议 SDK 雏形

来源参考：

```text
ET 的客户端和服务端共享协议定义，但客户端不应该引用服务端 Actor/Fiber/Scene/数据库宿主。
YuoCore 需要让 Web 和 Unity 共享 request/response、opcode、错误码和 envelope 语义。
```

问题：

```text
此前只有 Backend WebSocket endpoint 和 Protocol 目录。
Unity/Web 客户端没有一个可复用的协议会话层，容易把 WebSocket、Gateway、Backend 或 Actor 概念误带到客户端。
同时 YuoCore.Protocol 只 target net10.0，Unity 导入不友好。
```

YuoCore 落地：

```text
src/YuoCore.Client
tests/YuoCore.Client.Tests
src/YuoCore.Abstractions/YuoCore.Abstractions.csproj
src/YuoCore.Protocol/YuoCore.Protocol.csproj
```

修复：

```text
新增 YuoCore.Client，可选客户端 SDK 包。
YuoClientSession 负责 requestId/clientSeq、opcode 查表、YuoClientEnvelopeV1 编码、YuoServerEnvelopeV1 解码、错误 envelope 转异常。
IYuoClientTransport 只表达 bytes request -> bytes response，不绑定 WebSocket/TCP/KCP。
YuoCore.Abstractions、YuoCore.Protocol、YuoCore.Client 多目标到 net10.0;netstandard2.1。
为 netstandard2.1 增加 IsExternalInit shim，保证 record 类型可编译。
```

边界控制：

```text
YuoCore.Client 只依赖 Abstractions 和 Protocol。
它不引用 Gateway、Backend、Actor、Runtime、ECS、数据库、Redis 或 ASP.NET Core。
Unity 可实现 IYuoClientTransport 接入自己的网络库；Web TypeScript SDK 后续应按相同 envelope/opcode/requestId/clientSeq 语义实现。
```

验证：

```text
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

## 2026-06-08：1w 综合压测入口

YuoCore 落地：

```text
tools/YuoCore.LoadTests/Program.cs
tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj
benchmarks/YuoCore.Benchmarks/Program.cs
```

修复：

```text
LoadTests 保留 WebSocket 模式。
新增 --mode local-composite，用于本地综合压 GameRuntime Actor 命令、StateSync delta/AOI 和 PersistenceQueue drain。
100组件/100system 仍由 benchmarks/YuoCore.Benchmarks --perf-smoke 覆盖。
```

验证命令：

```text
dotnet build tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj --disable-build-servers
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 100 --ticks 3 --shards 2 --state-entities 100 --aoi-observers 8 --persistence-per-tick 100
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode sorted
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 5 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count
dotnet run -c Release --project benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -- --perf-smoke --entities 10000 --ticks 5 --shards 4
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

结果：

```text
LoadTests build: Build succeeded
local-composite 100 players smoke: actor=300 processed=300 state=300 persistence=300 pending=0
local-composite 1w sorted short: avg=153.522ms p95=281.959ms actor=50,000 processed=50,000 state=50,000 persistence=50,000 pending=0
local-composite 1w sorted hotspot: aoi-observers avg=102.524ms p95=181.208ms
local-composite 1w count short: avg=81.258ms p95=131.108ms aoi-observers avg=20.210ms p95=23.747ms
100组件/100system short: avg=116.619ms p95=131.193ms entity-tick/s=85,749
Full build: Build succeeded
Full tests: Passed all test projects
```

风险记录：

```text
短测证明入口可用，但不是长期 soak。
当前极端组合 p95 仍偏高；分段后主要热点是 sorted AOI observer 查询里的完整列表构建与排序。
已新增 YuoGridAoiTargetIndex.CountVisible 作为只计数/阈值路径的低风险旁路；没有改变 QueryVisible 的默认稳定排序语义。
如果目标是高频实时 MMO tick，需要继续做 AOI 订阅表、enter/leave 增量、脏 cell 更新，以及更真实的 StateSync 广播压测。
```

## 2026-06-08：Backend MMO 示例闭环第一版

参考思想：

```text
ET10 的精华不是把所有业务都塞进 ECS，而是让 Gateway、Actor、Fiber、Location、Persistence 各自守住边界。
YuoCore 当前先在 Backend 示例宿主内打通可运行闭环，再把稳定通用能力上移到库模块。
```

本轮落地：

```text
src/YuoCore.Backend/Game/GameRuntime.cs
src/YuoCore.Backend/Game/Components.cs
src/YuoCore.Backend/Game/GameContracts.cs
src/YuoCore.Backend/Game/GameLoopService.cs
src/YuoCore.Backend/WebSockets/GameClientMessageDispatcher.cs
src/YuoCore.Backend/WebSockets/GameGatewayProtobufDispatcher.cs
src/YuoCore.Backend/Infrastructure/PlayerSnapshotRepository.cs
src/YuoCore.Protocol/Contracts/yuocore.proto
src/YuoCore.Protocol/YuoCoreProtocolCatalog.cs
tests/YuoCore.Backend.Tests/GameRuntimeTests.cs
tests/YuoCore.Backend.Tests/GameClientMessageDispatcherTests.cs
tests/YuoCore.Protocol.Tests/YuoMessageRegistryTests.cs
```

实现：

```text
新增玩家 Presence/Transform 组件，支持在线状态、session、mapId、unitId、坐标和 inputSeq。
GameRuntime 新增 LoginPlayer、TryEnterMap、TryMove、TryDisconnect、DrainEvents、TryRestorePlayer。
进入地图和移动通过 Actor 消息投递到目标 Fiber mailbox 后执行，Gateway 不直接改 ECS。
新增 map 内广播事件队列，移动和进入地图会向同地图在线玩家投递 WorldEvent。
PlayerSnapshotDto 扩展地图/位置/在线/inputSeq 字段，PostgreSQL schema 和 upsert 同步扩展。
Protobuf 新增 EnterMapRequest、MoveRequest、DisconnectRequest、WorldSnapshot、PlayerWorldState、WorldEvent。
Gateway v1 opcode 支持 enter/move/disconnect，并返回 WorldSnapshot payload。
```

边界控制：

```text
MMO 示例仍放在 YuoCore.Backend，不反向污染 YuoCore.Ecs、YuoCore.Runtime、YuoCore.Actor。
Gateway dispatcher 只做协议解析和 runtime 命令调用，不持有 WebSocket，也不直接操作 YuoEntity。
PostgreSQL/Redis 仍只在 Backend Infrastructure 中出现，核心库不依赖数据库包。
PlayerWorldState/WorldEvent 是 WorldSnapshot 内部子消息，不分配独立 opcode。
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

## 2026-06-08：Actor 本机消息入口守卫

参考思想：

```text
ET10 的 Actor/Fiber 消息入口承担线程所有权边界，装配错误和坏消息应该在入口明确失败。
YuoCore 需要兼顾单机、Unity、Web 和 MMO 后端，因此同机 Actor 模块必须保持轻量，但不能让 null 输入进入 mailbox 后变成后台异常或静默吞掉。
```

问题：

```text
DelegateActorMessageDispatcher 没有拒绝 null 委托，也没有拒绝 null envelope。
ActorFiber 没有拒绝 null dispatcher/nodeId，也会把 null envelope 写入 mailbox。
YuoFiberActorEndpoint 是 Runtime 到 Actor 的可选桥接层，主构造参数和公开 Send/Invoke 入口缺少显式守卫。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Fibers/IActorMessageDispatcher.cs
src/YuoCore.Actor/Fibers/ActorFiber.cs
src/YuoCore.Runtime.Actor/YuoFiberActorEndpoint.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
tests/YuoCore.Runtime.Actor.Tests/YuoFiberActorEndpointTests.cs
```

修复：

```text
DelegateActorMessageDispatcher 在构造和 DispatchAsync 入口显式拒绝 null。
ActorFiber 在构造、SendAsync 和 InvokeAsync 入口显式拒绝 null。
YuoFiberActorEndpoint 改为显式构造函数，保存已校验字段，并在 SendAsync/InvokeAsync 入口拒绝 null envelope。
```

边界控制：

```text
不改变 ActorRouter、LocationSender、stale route retry 或 mailbox 串行派发语义。
不改变 fire-and-forget handler 异常不会打穿 Fiber 的策略。
不把 ECS、Runtime、Network、Gateway 或分布式实现引入 Actor 核心。
Runtime.Actor 仍然只是可选桥接包。
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

## 2026-06-08：Runtime FiberOptions 装配守卫

参考思想：

```text
ET10 的 Fiber/Scene 创建属于运行时拓扑装配入口，错误配置应在创建阶段明确失败。
YuoCore.Runtime 要同时服务单机、Unity 模拟和 MMO 后端，因此 Fiber 创建入口不能把 null options 延迟成空引用。
```

问题：

```text
YuoFiberManager.CreateFiber(YuoFiberOptions options) 进入私有 CreateFiber 后直接访问 options.Id。
传入 null options 时会抛 NullReferenceException，错误信息无法指向装配参数。
```

YuoCore 落地：

```text
src/YuoCore.Runtime/YuoFiberManager.cs
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
```

修复：

```text
CreateFiber 私有入口先调用 ArgumentNullException.ThrowIfNull(options)。
新增测试覆盖 null options。
```

边界控制：

```text
不改变 FiberId 分配和不复用规则。
不改变 mailbox 结构、Tick 顺序或并行 Tick 语义。
不引入 Actor、Network、Gateway 或服务端依赖到 Runtime。
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

## 2026-06-08：SerializedTransport 入口守卫

参考思想：

```text
ET10 的内外网传输边界会把消息序列化后再进入远端处理。
YuoCore.Network 的 SerializedYuoTransport 是当前远端语义模拟层，应在进入 codec/inner transport 前明确拒绝坏依赖和坏 envelope。
```

问题：

```text
SerializedYuoTransport 构造时没有显式拒绝 null innerTransport。
SendAsync/InvokeAsync 传入 null envelope 时会由 codec 间接抛错，错误边界不如其他 transport 清晰。
```

YuoCore 落地：

```text
src/YuoCore.Network/SerializedYuoTransport.cs
tests/YuoCore.Network.Tests/ProcessAwareYuoTransportTests.cs
```

修复：

```text
构造时拒绝 null innerTransport。
SendAsync/InvokeAsync 在 round-trip 前显式拒绝 null envelope。
新增测试确认 null envelope 不会进入 inner transport。
```

边界控制：

```text
不改变 JSON envelope codec。
不改变本地/远端 process 选择规则。
不引入真实 socket、ASP.NET Core 或分布式依赖。
```

验证：

```text
dotnet test tests/YuoCore.Network.Tests/YuoCore.Network.Tests.csproj --disable-build-servers
Passed: 15

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Persistence 装配与 dead-letter 守卫

参考思想：

```text
ET10 的异步保存链路会把游戏逻辑和慢 IO 解耦，但后台写回的装配错误必须在启动阶段暴露。
YuoCore.Persistence 是可选服务端能力，基础队列和 Hosting 适配要保持简单，同时避免坏依赖或坏 dead-letter 数据延迟污染后台循环。
```

问题：

```text
YuoPersistenceBackgroundService 使用 primary constructor 接收 queue / snapshotStore / eventStore，但没有显式 null guard。
坏依赖会延迟到 ExecuteAsync 或 ExecuteCommandAsync 才空引用。
InMemoryYuoPersistenceDeadLetterStore.AppendAsync 可以写入 null record，导致后续 GetRecords 消费者遇到坏数据。
```

YuoCore 落地：

```text
src/YuoCore.Persistence.Hosting/YuoPersistenceBackgroundService.cs
src/YuoCore.Persistence/InMemoryYuoPersistenceDeadLetterStore.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
tests/YuoCore.Persistence.Tests/YuoPersistenceQueueTests.cs
```

修复：

```text
后台服务构造时拒绝 null queue / snapshotStore / eventStore。
dead-letter store 追加时拒绝 null record。
新增 focused tests 覆盖装配和 dead-letter 输入契约。
```

边界控制：

```text
不改变队列容量、批处理、重试和 StopOnCommandError 语义。
不引入数据库、日志或 Hosting 依赖到基础 Persistence 包。
不处理可靠 shutdown drain/outbox；该语义仍属于后续需谨慎设计的持久化可靠性能力。
```

验证：

```text
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj --disable-build-servers
Passed: 9

dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj --disable-build-servers
Passed: 8

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：StartConfig metadata 契约守卫

参考思想：

```text
ET10 的 StartConfig 是进程拓扑和服务发布的入口。
YuoCore.Config 虽然是可选服务端配置层，但它应该在发布到 ServiceDiscovery 前拒绝坏 metadata，避免错误延迟到服务发现适配层。
```

问题：

```text
YuoStartConfigLoader.Validate 原来检查了 node/process/fiber/service 基本字段，但没有拒绝 null metadata 集合、空 metadata key 或 null metadata value。
schemas/startconfig.schema.json 只要求 metadata value 是 string，没有明确禁止空 property name。
Server.ServiceDiscovery 会把 StartConfig metadata 合并后注册到 ServiceDiscovery，坏 metadata 应该在 Config 契约层先失败。
```

YuoCore 落地：

```text
src/YuoCore.Config/YuoStartConfig.cs
schemas/startconfig.schema.json
tests/YuoCore.Config.Tests/YuoStartConfigTests.cs
```

修复：

```text
Validate 拒绝 root/service/fiber 的 null Metadata 集合。
Validate 拒绝空白 metadata key 和 null metadata value。
JSON Schema 为 root/service/fiber metadata 增加 propertyNames.minLength = 1。
```

边界控制：

```text
不强制 service.endpoint，因为 Server.ServiceDiscovery 仍支持从 outerEndpoint/innerEndpoint 回退。
不引入 ServiceDiscovery 包到 Config。
不改变 StartConfig DTO 字段集合。
```

验证：

```text
dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj --disable-build-servers
Passed: 11

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：ServiceDiscovery metadata 契约守卫

参考思想：

```text
ET10 的服务发现依赖服务类型和元数据进行路由筛选。
YuoCore.ServiceDiscovery 虽然当前是内存实现，但它定义了未来可选分布式服务发现的行为基准，metadata 不能接受半坏键值。
```

问题：

```text
RegisterAsync 会复制 metadata，但没有显式拒绝空 key 或 null value。
QueryAsync / Subscribe 会快照 query metadata，但同样没有校验坏筛选条件。
坏 metadata 可能让订阅匹配、服务替换通知和未来分布式实现出现不一致行为。
```

YuoCore 落地：

```text
src/YuoCore.ServiceDiscovery/InMemoryYuoServiceDiscovery.cs
tests/YuoCore.ServiceDiscovery.Tests/InMemoryYuoServiceDiscoveryTests.cs
```

修复：

```text
注册服务 metadata 时拒绝 null/空白 key。
注册服务 metadata 时拒绝 null value。
查询和订阅 metadata filter 使用同一套快照校验。
```

边界控制：

```text
不改变 service name/kind/endpoint 语义。
不改变 lease、prune、subscribe 通知模型。
不引入远程网络、Actor、Runtime 或 Backend 依赖。
```

验证：

```text
dotnet test tests/YuoCore.ServiceDiscovery.Tests/YuoCore.ServiceDiscovery.Tests.csproj --disable-build-servers
Passed: 18

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Actor LocationSender 工厂结果守卫

参考思想：

```text
ET10 的 MessageLocationSender 会把业务消息封入路由流程，但业务 handler 和 sender 边界仍要拒绝无效消息。
YuoCore 的 LocationSender 是稳定业务 key 到 Actor 消息的关键入口，不能让 null 消息/请求进入 TargetActorId 访问或 transport。
```

问题：

```text
YuoActorLocationSender 构造时没有显式拒绝 null locations / transport。
messageFactory 或 requestFactory 返回 null 时，会在访问 TargetActorId 时空引用。
这会让业务工厂错误表现成低层 NullReferenceException，难以判断是路由、Location 还是业务参数问题。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

修复：

```text
构造时拒绝 null IYuoLocationRegistry / IActorTransport。
messageFactory 返回 null 时抛 ActorException 400。
requestFactory 返回 null 时抛 ActorException 400。
```

边界控制：

```text
不改变 stale retry、locked retry、timeout 或 cache invalidation 语义。
不改变 message DTO 仍由业务 factory 创建的模型。
不引入 ECS、Runtime 或远程网络依赖。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
Passed: 38

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Location 适配器装配守卫

参考思想：

```text
ET10 的 Location/Actor 路由链路在启动装配阶段应该尽早暴露缺失依赖。
YuoCore 的 Location 缓存包装器和 Actor 适配器都是可选模块边界，不能把坏依赖延迟到第一次消息发送时才空引用。
```

问题：

```text
CachingYuoLocationRegistry 使用 primary constructor 接收 inner registry，但没有显式 null guard。
YuoLocationActorRegistry 同样接收 IYuoLocationRegistry，但 null 会延迟到 Register/Resolve/Unregister/Invalidate 时才失败。
这类错误属于宿主装配问题，应在构造时失败，方便单机、Unity 模拟和服务器 Host 更快定位。
```

YuoCore 落地：

```text
src/YuoCore.Location/CachingYuoLocationRegistry.cs
src/YuoCore.Actor/Location/YuoLocationActorRegistry.cs
tests/YuoCore.Location.Tests/YuoLocationRegistryTests.cs
```

修复：

```text
CachingYuoLocationRegistry 构造时拒绝 null inner。
YuoLocationActorRegistry 构造时拒绝 null locations。
新增对应构造契约测试。
```

边界控制：

```text
不改变 Location 注册、锁、迁移和缓存失效语义。
不改变 ActorId 到 YuoLocationKey 的映射。
不引入 Runtime、ECS、Network 或分布式实现依赖。
```

验证：

```text
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --disable-build-servers
Passed: 16

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：AOI 几何参数有限值守卫

参考思想：

```text
ET10 的 AOI Cell 查询依赖稳定的 cell 半径和观察半径。
YuoCore.StateSync 的 AOI 目前是 Web/Unity/MMO 共用的无状态查询器，因此必须避免坏半径触发超大网格扫描。
```

问题：

```text
YuoGridAoiQuery / YuoGridAoiTargetIndex 只拒绝 cellSize <= 0，NaN 和 Infinity 会漏过。
QueryVisible 只拒绝 ViewRadius <= 0，Infinity 会导致 cellRadius 极大，理论上可能把一帧卡死。
NaN 半径也会进入后续网格计算，行为不明确。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoAoi.cs
tests/YuoCore.StateSync.Tests/YuoAoiTests.cs
```

修复：

```text
cellSize 必须是 finite 且 > 0。
ViewRadius 必须是 finite 且 > 0，否则本次查询直接返回空可见集。
新增 NaN / PositiveInfinity / NegativeInfinity 覆盖测试。
```

边界控制：

```text
不改变 AOI Cell 算法。
不改变 phase、距离排序、自身过滤规则。
不引入 Game/Gateway/Protocol 依赖。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
Passed: 52

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：StateSync 坏包集合守卫

参考思想：

```text
ET10 的状态同步和消息流在进入业务处理前会先保证协议数据结构完整。
YuoCore.StateSync 面向 Web/Unity 客户端镜像和服务端 AOI，同样应该把坏 snapshot/delta 稳定归类为无效同步数据。
```

问题：

```text
YuoReplicatedStateTable 已经能在重复 NetworkId、缺少 State、BaseTick 缺口时保持原子性。
但 snapshot.Entities、snapshot.Entities 内元素、delta.Changes、delta.Changes 内元素如果为 null，会在 foreach 或字段访问处空引用。
YuoStateSyncMirror 只结果化 InvalidOperationException，空引用会漏出到客户端/SDK 层。
YuoStateDeltaCompressor 对 null changes 也会空引用。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoReplicatedStateTable.cs
src/YuoCore.StateSync/YuoStateDeltaCompressor.cs
tests/YuoCore.StateSync.Tests/YuoReplicatedStateTableTests.cs
tests/YuoCore.StateSync.Tests/YuoStateSyncMirrorTests.cs
tests/YuoCore.StateSync.Tests/YuoStateDeltaCompressorTests.cs
```

修复：

```text
Snapshot 应用前检查 Entities 集合和每个 EntityState。
Delta 应用前检查 Changes 集合和每个 StateChange。
Compressor 压缩前检查 Changes 集合和每个 StateChange。
Mirror 继续把这些无效数据转成 InvalidSnapshot/InvalidDelta，且不修改旧状态。
```

边界控制：

```text
不改变 Snapshot/Delta record 形状。
不引入协议、Gateway 或表现层依赖。
不改 AOI、压缩合并规则或 LastTick 推进规则。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
Passed: 43

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Protocol Registry 描述符入口守卫

参考思想：

```text
ET10 的协议工具链会在生成/注册阶段固定 opcode、消息类型和路由方向，运行时不应该接受半坏的协议描述。
YuoCore 目前保留手写 registry 作为 Web、Unity、Gateway 和内部消息的共享过渡层，因此注册入口也需要承担同类边界校验。
```

问题：

```text
YuoMessageRegistry.Create 原来会拒绝非法 opcode 和重复 opcode/type。
但 descriptors 集合里如果混入 null descriptor、null MessageType 或未知 YuoMessageDirection，会在后续访问中空引用，或把无效方向注册进协议目录。
这类坏描述会影响 Web/Unity 共享协议、Gateway 路由和后续 Source Generator 接管时的生成输入质量。
```

YuoCore 落地：

```text
src/YuoCore.Protocol/YuoMessageRegistry.cs
tests/YuoCore.Protocol.Tests/YuoMessageRegistryTests.cs
```

修复：

```text
Create 遍历描述符时显式拒绝 null descriptor。
显式拒绝 null MessageType。
新增 YuoMessageDirection 白名单校验，只接受 ClientToGateway、GatewayToClient、Internal。
```

边界控制：

```text
不改变 opcode/type 双向索引结构。
不引入 Gateway、Backend 或网络传输依赖。
不把 Protobuf 生成逻辑塞进运行时 registry；这里只固定注册表输入契约。
```

验证：

```text
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
Passed: 19

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Abstractions Result 失败契约收紧

参考思想：

```text
ET10 的基础运行时错误会在边界处尽早暴露，而不是把坏状态继续传入后续调度、消息或业务层。
YuoCore.Abstractions 是所有模块都可能依赖的最底层契约，Result 失败态必须始终携带明确错误。
```

问题：

```text
YuoResult.Fail(null!) 和 YuoResult<T>.Fail(null!) 原来可以构造 Error 为 null 的结果。
由于 IsSuccess 只根据 Error 是否为 null 判断，这会产生“调用 Fail 却得到成功结果”的隐式坏状态。
上层 Actor、Gateway、StateSync、Persistence 如果统一按 YuoResult 判断错误，会被这种坏结果误导。
```

YuoCore 落地：

```text
src/YuoCore.Abstractions/YuoResult.cs
tests/YuoCore.Abstractions.Tests/YuoResultTests.cs
```

修复：

```text
Fail 工厂入口增加 ArgumentNullException.ThrowIfNull(error)。
新增 Abstractions 独立测试项目，覆盖泛型和非泛型 Result 的 null error、失败态和成功态。
```

边界控制：

```text
不改变 YuoResult 的结构布局。
不引入任何上层模块依赖。
不改变可预期失败继续用 Result、程序错误继续抛异常的分层原则。
```

验证：

```text
dotnet test tests/YuoCore.Abstractions.Tests/YuoCore.Abstractions.Tests.csproj --disable-build-servers
Passed: 4

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Timer Update 取消优先

参考思想：

```text
ET10 的手动驱动 Timer 属于当前执行上下文的一部分。
宿主取消本帧调度时，Timer 更新入口应该在改变内部时间和出队 due timer 前响应取消。
```

问题：

```text
YuoTimerScheduler.UpdateAsync(now, cancellationToken) 原来只在 DequeueDueEntries 的 due timer 循环中检查取消。
如果没有 due timer，已取消 token 不会抛异常。
如果有 due timer，CurrentTime 可能先被推进到 now 后才抛取消。
```

YuoCore 落地：

```text
src/YuoCore.Timers/YuoTimerScheduler.cs
tests/YuoCore.Timers.Tests/YuoTimerSchedulerTests.cs
```

修复：

```text
UpdateAsync(DateTimeOffset now, CancellationToken) 入口先调用 cancellationToken.ThrowIfCancellationRequested。
新增测试确认取消时不会推进 CurrentTime。
```

边界控制：

```text
不改变 TimerScheduler 手动驱动模型。
不改变 due batch 已开始执行后的现有行为。
不引入 Runtime、Actor 或 Hosting 依赖。
```

验证：

```text
dotnet test tests/YuoCore.Timers.Tests/YuoCore.Timers.Tests.csproj --disable-build-servers
Passed: 12

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Actor.Ecs dispatcher/handler 入口守卫

参考思想：

```text
ET10 的 MessageHandler 进入业务 Entity 前，会先经过消息分发层和目标解析层。
YuoCore.Actor.Ecs 作为 Actor 消息进入 ECS Entity 的适配层，也应该在边界处明确拒绝无效输入。
```

问题：

```text
YuoEntityActorDispatcher 构造依赖为 null 时会延迟为空引用。
DispatchAsync 传入 null envelope 时没有明确错误。
YuoActorHandler / YuoActorRequestHandler 类型检查假设 entity 和 message 非 null。
YuoActorHandlerRegistry.AddFromAssembly / TryGet 缺少 null 参数契约。
```

YuoCore 落地：

```text
src/YuoCore.Actor.Ecs/Handlers/IYuoActorHandler.cs
src/YuoCore.Actor.Ecs/Handlers/YuoEntityActorDispatcher.cs
src/YuoCore.Actor.Ecs/Handlers/YuoActorHandlerRegistry.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

修复：

```text
Dispatcher 构造依赖改为显式 readonly 字段并增加 null guard。
DispatchAsync 对 null envelope 早失败。
Handler.HandleAsync 对 null entity/message 早失败。
HandlerRegistry 反射扫描和 TryGet 对 null 参数早失败。
```

边界控制：

```text
不改变 Actor.Core 与 ECS 的拆包边界。
不改变 handler 注册表的无锁读取路径。
不改变业务 handler 的 RunAsync 签名。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
Passed: 35

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Actor Router/Transport 入口守卫

参考思想：

```text
ET10 的 Actor 消息入口会在进入路由、mailbox 和业务 handler 前完成基础契约检查。
YuoCore Actor 层也应该把 null 依赖、null message/request 和 null envelope 挡在路由前。
```

问题：

```text
ActorRouter 构造依赖为 null 时会延迟为空引用。
SendAsync/InvokeAsync 传入 null message/request 时，会在 TargetActorId 访问处空引用，且错误不说明入口参数。
InMemoryActorTransport 注册 null endpoint 或发送 null envelope 时，也缺少明确错误。
ActorEnvelope 工厂方法没有统一 null 参数守卫。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Routing/ActorRouter.cs
src/YuoCore.Actor/Transport/InMemoryActorTransport.cs
src/YuoCore.Actor/Abstractions/ActorEnvelope.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

修复：

```text
ActorRouter 构造时检查 locationRegistry / transport。
SendAsync / InvokeAsync 在 resolve 前检查 message/request。
InMemoryActorTransport 注册 endpoint 和发送 envelope 时做 null 检查。
ActorEnvelope.ForMessage / ForRequest / Success / Failure 增加 null guard。
```

边界控制：

```text
不改变 ActorId / ActorEnvelope 业务模型。
不改变 Location 404 失效重试语义。
不引入 ECS、Runtime 或 Network 依赖。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
Passed: 32

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Runtime FiberManager 顺序 tick dispose 守卫

参考思想：

```text
ET10 的运行时调度入口应在生命周期错误时早失败。
YuoCore 的 FiberManager 直接暴露给单机、Unity 模拟和服务端宿主，dispose 后继续 tick 不应静默成功。
```

问题：

```text
YuoFiberManager.TickAllParallelAsync 已经会在 dispose 后抛 ObjectDisposedException。
TickAllAsync 没有相同守卫，dispose 后因为 _fibers 已清空，会静默 no-op。
```

YuoCore 落地：

```text
src/YuoCore.Runtime/YuoFiberManager.cs
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
```

修复：

```text
TickAllAsync 入口调用 ThrowIfDisposed。
新增测试覆盖直接使用 YuoFiberManager 时，dispose 后顺序 tick 明确抛 ObjectDisposedException。
```

边界控制：

```text
不改变 Fiber mailbox 结构。
不改变 TickAllParallelAsync 语义。
不改变 YuoCoreRuntime 的外层 dispose 守卫。
```

验证：

```text
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj --disable-build-servers
Passed: 13

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：StartConfig null 输入结果化校验

参考思想：

```text
ET10 的 StartConfig/进程拓扑入口强调启动早失败和可解释错误。
YuoCore 的配置校验也应该把坏配置收敛为 YuoResult，而不是在校验阶段抛 NullReferenceException。
```

问题：

```text
YuoStartConfigLoader.Validate 原来假设 config、Services、Fibers 和列表项都非 null。
JSON 或手写配置如果传入 null 集合/列表项，启动预检会空引用，错误不可解释。
```

YuoCore 落地：

```text
src/YuoCore.Config/YuoStartConfig.cs
tests/YuoCore.Config.Tests/YuoStartConfigTests.cs
```

修复：

```text
Validate 接受 nullable config 并返回 YuoResult.Fail。
Services 为 null 时返回明确错误。
Fibers 为 null 或为空时返回明确错误。
Service/Fiber 列表项为 null 时返回明确错误。
```

边界控制：

```text
不改变 JSON Schema 文件。
不改变 StartConfig DTO 字段。
不引入 Server、ServiceDiscovery 或 Backend 依赖。
```

验证：

```text
dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj --disable-build-servers
Passed: 8

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Network transport 入口守卫

参考思想：

```text
ET10 的内部消息传输层会在路由前区分基础设施错误和业务错误。
YuoCore 的本机 transport 也应该在 endpoint 查找、进程选择和调用下游前完成明确参数检查。
```

问题：

```text
InMemoryYuoTransport.Register 对 null endpoint 没有明确契约。
SendAsync / InvokeAsync 如果 envelope 为 null，错误会在 endpoint 或后续 codec 中暴露。
ProcessAwareYuoTransport 构造参数和 envelope 也缺少明确检查。
```

YuoCore 落地：

```text
src/YuoCore.Network/InMemoryYuoTransport.cs
src/YuoCore.Network/ProcessAwareYuoTransport.cs
tests/YuoCore.Network.Tests/ProcessAwareYuoTransportTests.cs
```

修复：

```text
InMemoryYuoTransport.Register 拒绝 null endpoint。
InMemoryYuoTransport.SendAsync / InvokeAsync 在 endpoint lookup 前拒绝 null envelope。
ProcessAwareYuoTransport 构造时拒绝空 localProcessId 和 null localTransport。
ProcessAwareYuoTransport 在 route selection 前拒绝 null envelope。
```

边界控制：

```text
不改变本地/远端 route selection。
不引入真实远程网络实现。
不改变 transport exception 的缺失 endpoint 语义。
```

验证：

```text
dotnet test tests/YuoCore.Network.Tests/YuoCore.Network.Tests.csproj --disable-build-servers
Passed: 13

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Gateway dispatch pipeline 入口守卫

参考思想：

```text
ET10 的网络/消息入口会尽早把基础设施错误挡在业务 handler 之前。
YuoCore Gateway pipeline 也应该在进入内层 dispatcher 前完成明确的参数契约检查。
```

问题：

```text
YuoGatewayDispatchPipeline 已经检查 session 和 cancellation。
但构造时 inner dispatcher 为 null、DispatchAsync 的 message 为 null 时，错误会延迟为 NullReferenceException。
这类错误既不利于 AI/开发者定位，也可能在 clientSeq 检查后才暴露。
```

YuoCore 落地：

```text
src/YuoCore.Gateway/YuoGatewayDispatchPipeline.cs
tests/YuoCore.Gateway.Tests/YuoGatewayDispatchPipelineTests.cs
```

修复：

```text
构造函数入口保存 _inner，并对 null dispatcher 抛 ArgumentNullException。
DispatchAsync 在 clientSeq 处理前检查 message。
新增测试确认 null message 不进入 inner dispatcher，也不推进 LastClientSeq。
```

边界控制：

```text
不改变 Gateway 正常消息处理语义。
不改变异常转 error response 的业务异常策略。
不接触 ECS、Actor、WebSocket 或 Backend。
```

验证：

```text
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj --disable-build-servers
Passed: 10

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：ServiceDiscovery 查询条件快照化

参考思想：

```text
ET10 的服务目录和消息路由入口会把外部输入转成稳定的内部路由信息。
订阅条件属于运行时路由契约，注册后不应再受调用方外部可变字典影响。
```

问题：

```text
InMemoryYuoServiceDiscovery.RegisterAsync 已经会复制 service metadata。
但 QueryAsync / Subscribe 接收的 YuoServiceQuery 会直接使用外部 Metadata 引用。
尤其 Subscribe 会长期保存这个 query，调用方如果订阅后修改原字典，订阅条件会漂移。
```

YuoCore 落地：

```text
src/YuoCore.ServiceDiscovery/InMemoryYuoServiceDiscovery.cs
tests/YuoCore.ServiceDiscovery.Tests/InMemoryYuoServiceDiscoveryTests.cs
```

修复：

```text
新增 SnapshotQuery。
QueryAsync 和 Subscribe 入口都会复制 query metadata。
空 metadata 统一归一为 null。
QueryAsync / Subscribe 对 null query 明确抛 ArgumentNullException。
```

边界控制：

```text
不改变 IYuoServiceDiscovery 接口。
不引入后台线程、远程网络或分布式注册中心。
不改变服务 descriptor 的发布语义。
```

验证：

```text
dotnet test tests/YuoCore.ServiceDiscovery.Tests/YuoCore.ServiceDiscovery.Tests.csproj --disable-build-servers
Passed: 14

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：Protocol envelope 解码帧校验

参考思想：

```text
ET10 的消息层会在进入业务 handler 前完成基础消息合法性筛选。
YuoCore 的 Protocol codec 也应该在反序列化边界拒绝明显非法的 envelope frame。
```

问题：

```text
YuoProtocolEnvelopeCodec.Decode 原来只做 JSON 反序列化。
如果输入 frame 携带未知 YuoMessageKind 枚举值，会被构造成 envelope 继续进入 Network/Actor 模拟远端链路。
如果 frame 缺失 version，会以 ushort 默认值 0 进入系统。
```

YuoCore 落地：

```text
src/YuoCore.Protocol/YuoProtocolEnvelopeCodec.cs
tests/YuoCore.Protocol.Tests/YuoMessageRegistryTests.cs
```

修复：

```text
Decode 后增加 ValidateFrame。
Version 必须非 0。
Kind 必须是 Message / Request / Response / Error 之一。
新增测试覆盖 codec round trip、未知 Kind、缺失 Version。
```

边界控制：

```text
不改变 envelope JSON 字段。
不校验业务 opcode 是否存在，opcode 仍由 YuoMessageRegistry/上层协议路由处理。
不绑定 WebSocket、Actor、Location 或 Backend。
```

验证：

```text
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
Passed: 16

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：StateSync Mirror 无效 snapshot 结果化

参考思想：

```text
ET10 风格的客户端/镜像恢复链路应把协议顺序和数据合法性错误收敛成明确状态。
调用方据此决定是否重拉 snapshot、丢弃包或记录错误，而不是让表现层直接承接底层异常。
```

问题：

```text
YuoStateSyncMirror.ApplyDelta 已经会把 InvalidOperationException 转成 Rejected/InvalidDelta。
但 ApplySnapshot 在 Table.ApplySnapshot 校验失败时仍会直接抛异常。
Web/Unity 客户端接入 Mirror 时，坏 snapshot 会跳出统一 ApplyResult 契约。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoStateSyncMirror.cs
tests/YuoCore.StateSync.Tests/YuoStateSyncMirrorTests.cs
```

修复：

```text
新增 YuoStateSyncApplyReason.InvalidSnapshot。
Mirror.ApplySnapshot 捕获镜像表的 InvalidOperationException，并返回 Rejected/InvalidSnapshot。
坏 snapshot 不会修改旧镜像状态。
如果 Mirror 处于 SnapshotRequired 状态，坏 snapshot 也不会清除 LastGap。
```

边界控制：

```text
YuoReplicatedStateTable 仍保持直接抛异常的底层契约。
不改变 snapshot/delta 数据结构。
不改变外部协议和线格式。
不引入 WebSocket、Unity 或服务端依赖。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
Passed: 35

dotnet build YuoCore.slnx --disable-build-servers
Build succeeded.

dotnet test YuoCore.slnx --no-build
Passed: all test projects.
```

## 2026-06-08：StateSync snapshot 重复 NetworkId 原子拒绝

参考思想：

```text
ET10 的状态同步要求客户端镜像只接受一致的完整状态。
Delta 已经有 BaseTick 和变更校验，完整 snapshot 也应该在提交前完成契约校验。
```

问题：

```text
YuoReplicatedStateTable.ApplySnapshot 原来会先 Clear，再逐个写入 snapshot.Entities。
如果 snapshot 内存在重复 NetworkId，后一个状态会静默覆盖前一个状态。
更重要的是，未来如果 snapshot 校验变多，先 Clear 再发现错误会破坏本地旧镜像。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoReplicatedStateTable.cs
tests/YuoCore.StateSync.Tests/YuoReplicatedStateTableTests.cs
```

修复：

```text
ApplySnapshot 在清表前调用 ValidateSnapshot。
重复 NetworkId 会抛 InvalidOperationException。
新增测试确认坏 snapshot 被拒绝时，旧表和 LastTick 不变。
```

边界控制：

```text
不改变 snapshot/delta 数据结构。
不引入 Unity/Web 表现层依赖。
不改变服务端状态生成逻辑，只增强客户端镜像契约。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
Passed: 33
```

## 2026-06-08：ServerHost 本地 Actor 注册回滚

参考思想：

```text
ET10 的 Actor 暴露依赖 Mailbox、Location 和实体所有权同时成立。
Actor 被外部可见之前，不能只注册了一部分运行时表，否则消息可能路由到半初始化对象。
```

问题：

```text
YuoServerHost.RegisterLocalEntityActorAsync 会先注册本地 EntityActorRegistry 和 InMemoryActorTransport endpoint，
再注册 ActorLocation，最后注册业务 LocationKey。
原实现没有检查业务 Location.RegisterAsync 的 YuoResult。
如果业务 Location 被锁或版本冲突，方法仍可能返回成功，留下 Actor 可用但业务 key 不可用的半注册状态。
如果 ActorLocation 或业务 Location 注册抛异常，本地 entity/endpoint 也没有回滚。
```

YuoCore 落地：

```text
src/YuoCore.Server/YuoServerHost.cs
tests/YuoCore.Server.Tests/YuoServerHostTests.cs
```

修复：

```text
检查业务 Location.RegisterAsync 返回值。
ActorLocation 或业务 Location 注册失败时，回滚 ActorEntities、ActorTransport endpoint 和 ActorLocations。
新增测试覆盖业务 Location 被锁时不会留下可路由 Actor。
```

边界控制：

```text
不改变 YuoCore.Server 的项目依赖。
不引入分布式事务。
只保证本进程内存表和当前 Location 抽象调用失败时的一致性。
```

验证：

```text
dotnet test tests/YuoCore.Server.Tests/YuoCore.Server.Tests.csproj --disable-build-servers
Passed: 3
```

## 2026-06-08：StartConfig ServiceDiscovery lease TTL 预检

参考思想：

```text
ET10 的启动拓扑发布应尽量早失败。
服务发现注册属于对外可见副作用，配置错误不应该导致前几个服务已经发布、后续服务才失败。
```

问题：

```text
RegisterStartConfigServicesAsync 已经会先预解析所有 endpoint 和 metadata。
但 service-specific leaseTtlSelector 返回非法 TTL 时，原实现不会在预解析阶段校验。
如果第一个服务 TTL 合法、第二个服务 TTL 非法，可能先发布第一个服务，再由第二个 discovery.RegisterAsync 抛异常。
```

YuoCore 落地：

```text
src/YuoCore.Server.ServiceDiscovery/YuoStartConfigServiceDiscoveryExtensions.cs
tests/YuoCore.Server.ServiceDiscovery.Tests/YuoStartConfigServiceDiscoveryExtensionsTests.cs
```

修复：

```text
PendingServiceRegistration 构建阶段校验 lease TTL。
所有服务 endpoint、metadata、lease TTL 全部通过后才开始写 ServiceDiscovery。
新增测试确认第二个服务 TTL 非法时不会发布任何服务。
```

边界控制：

```text
不改变 IYuoServiceDiscovery 接口。
不把 ServiceDiscovery 引入 YuoCore.Server 核心包。
不实现分布式服务发现事务；这里只处理本地配置预处理阶段的半发布风险。
```

验证：

```text
dotnet test tests/YuoCore.Server.ServiceDiscovery.Tests/YuoCore.Server.ServiceDiscovery.Tests.csproj --disable-build-servers
Passed: 9
```

## 2026-06-08：LocationSender cache hit 取消优先级

参考思想：

```text
ET10 的 Actor/Location 调用链需要让取消和超时在进入业务处理前生效。
即使命中本地缓存，也不能因为快路径跳过 CancellationToken 检查。
```

问题：

```text
YuoActorLocationSender.ResolveAsync 在命中 _addressCache 时直接返回地址。
如果调用方 token 已取消，后续 transport 仍会拒绝投递，但 messageFactory/requestFactory 已经可能被调用。
业务 factory 可能有分配、计数或其他副作用，取消请求不应该进入这一步。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

修复：

```text
ExecuteWithLocationRetryAsync 每轮开始先检查 cancellationToken。
ResolveAsync 在读取 sender cache 前检查 cancellationToken。
新增测试确认 cached address 场景下，取消请求不会调用 requestFactory。
```

边界控制：

```text
不改变 stale retry 次数。
不改变 locked location retry 和 timeout 映射。
不引入远程网络或分布式 Location 实现。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --disable-build-servers
Passed: 26
```

## 2026-06-08：StateSync AOI observer tick 单调护栏

参考思想：

```text
ET10 的状态同步和 Actor/Fiber Tick 都依赖明确的时间游标。
每个观察者的增量同步必须按 tick 单调推进，否则客户端缺口检测和重拉 snapshot 策略会失去依据。
```

问题：

```text
YuoAoiVisibilityTracker 会按 observer 记录上一轮可见集合和上一轮 tick，并把上一轮 tick 写入 delta.BaseTick。
原实现没有拒绝同一 observer 的重复或倒退 tick。
如果调用方传错 tick，tracker 会更新可见集合和 base tick 游标，导致后续客户端看到混乱的 delta 序列。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoAoiVisibilityTracker.cs
tests/YuoCore.StateSync.Tests/YuoAoiVisibilityTrackerTests.cs
```

修复：

```text
BuildDelta 在修改 observer 状态前校验 tick。
同一 observer 的 tick 必须严格递增。
校验失败时抛 InvalidOperationException，并保持可见集合与 BaseTick 游标不变。
```

边界控制：

```text
不改变 AOI 查询接口。
不引入 Gateway、Protocol 或网络依赖。
不实现订阅式 AOI Cell，只补当前无状态 tracker 的游标安全。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
Passed: 32
```

## 2026-06-08：Protocol response 工厂错误泄漏保护

参考思想：

```text
ET10 的消息层把 request/response/error 视为明确的 envelope 状态。
同一个请求帧即使经过跨进程反序列化，也不能让旧错误元信息污染正常响应。
```

问题：

```text
YuoProtocolEnvelope.Response 通过 request with 生成响应。
如果 request 是从外部 frame 解码出来，并且意外带有 Error，正常 Response 会继承这个 Error。
这会让上层看到 Kind=Response 但 Error 不为空的混合状态。
```

YuoCore 落地：

```text
src/YuoCore.Protocol/YuoProtocolEnvelope.cs
tests/YuoCore.Protocol.Tests/YuoMessageRegistryTests.cs
```

修复：

```text
Response 工厂显式清空 Error。
Response / Failure 工厂对 request 做空值保护。
Failure 工厂对 error 做空值保护。
```

边界控制：

```text
不改变 envelope 字段结构。
不引入 Gateway、Network 或 Backend 依赖。
不改变正常 Request/Response 的路由字段交换语义。
```

验证：

```text
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj --disable-build-servers
Passed: 13
```

## 2026-06-08：Gateway serverSeq 单调下限保护

参考思想：

```text
ET10 的 Gate/Session 连接层需要保证客户端可见序号由连接所有者统一维护。
业务 handler 可以产生响应内容，但连接序号不能因为某个桥接层临时填值而回退。
```

问题：

```text
YuoGatewayDispatchPipeline 默认会在内层 dispatcher 未填 ServerSeq 时分配 session.NextServerSeq。
但如果某个旧协议 bridge 返回了非 0 ServerSeq，Pipeline 会直接保留该值，却不会推进 YuoGatewaySession.LastServerSeq。
下一条自动分配的响应可能从更小序号开始，导致 Web/Unity 客户端看到 serverSeq 回退。
```

YuoCore 落地：

```text
src/YuoCore.Gateway/YuoGatewaySession.cs
src/YuoCore.Gateway/YuoGatewayDispatchPipeline.cs
tests/YuoCore.Gateway.Tests/YuoGatewayDispatchPipelineTests.cs
```

修复：

```text
YuoGatewaySession 增加内部 AdvanceServerSeqAtLeast。
Pipeline 在保留内层非 0 ServerSeq 时，同步推进 session 的 serverSeq 下限。
后续由 Pipeline 自动分配的响应会继续单调递增。
```

边界控制：

```text
不改变外部 envelope。
不强制覆盖内层已经设置的 ServerSeq，兼容当前 Backend protobuf bridge。
Gateway 仍不直接操作 ECS，也不引入 Actor/Runtime 依赖。
```

验证：

```text
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj --disable-build-servers
Passed: 8
```

## 2026-06-08：Persistence 队列指标快照非负化

参考思想：

```text
ET10 的后台队列和服务进程需要能长期观测。
指标必须表达可操作状态，不能把无害的并发统计窗口暴露成负积压，误导 Prometheus 告警或容量判断。
```

问题：

```text
YuoPersistenceQueue 使用 Channel 做命令传递。
入队统计在 WriteAsync 成功后更新；极端并发下，单 reader 可能先出队并扣减 pending，再由 writer 增加 pending。
这会产生极短暂的负 PendingCount 观测窗口，不影响命令执行，但会污染指标和 backlog ratio。
```

YuoCore 落地：

```text
src/YuoCore.Persistence/YuoPersistenceQueue.cs
tests/YuoCore.Persistence.Tests/YuoPersistenceQueueTests.cs
```

修复：

```text
YuoPersistenceQueueStats 增加 Sanitize。
GetStats 对 PendingCount 做非负快照。
BacklogRatio 夹紧到 0..1。
补充单元测试，确认负 pending 快照不会导出负 backlog ratio，也不会修改累计入队/出队计数。
```

边界控制：

```text
不改变 Channel 写入/读取语义。
不改变停服 drain 策略。
不引入 outbox、WAL 或生产级 dead letter 存储。
可靠停机写回仍是 docs/risk-and-decisions.md 中的待裁定项。
```

验证：

```text
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj
Passed: 8
```

## 2026-06-08：核心 Markdown 代码块闭合护栏

参考思想：

```text
长期架构演进中，文档本身也是 AI 和维护者的执行入口。
如果 fenced code block 没闭合，后续规则、风险和命令会被渲染成错误结构，AI 也容易误读边界。
```

问题：

```text
docs/risk-and-decisions.md 的 Timers / Runtime.Actor 相邻段落存在代码块闭合位置不清晰的问题。
这种问题不影响编译，但会影响长期 goal 的规则可读性。
```

YuoCore 落地：

```text
docs/risk-and-decisions.md
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
```

修复：

```text
修正 Timers 与 Runtime.Actor 段落的 fenced code block 结构。
新增 Core_markdown_documents_have_balanced_fenced_code_blocks 架构测试。
测试覆盖 docs/*.md 和根 AGENTS.md。
```

边界控制：

```text
这是开发期文档护栏。
不引入 Markdown parser，只检查最容易破坏渲染的 fenced code block 成对数量。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
Passed: 11
```

## 2026-06-08：Runtime.Actor Send 异常隔离

参考思想：

```text
ET10 的 Actor/Fiber 消息处理必须隔离单条消息异常。
Call/Request 可以把异常转换成 response error；Send/fire-and-forget 没有响应通道，但也不能因为一个 handler 抛异常打断整个 Fiber mailbox。
```

问题：

```text
YuoFiberActorEndpoint.InvokeAsync 已经会把 ActorException/Exception 转成 ActorEnvelope.Failure。
但 SendAsync 通过 fiber.PostAsync 执行 DispatchSendAsync，handler 异常会从 Fiber.DrainMailboxAsync 继续抛出，导致当前 Tick 被打断，并阻止后续 mailbox 消息执行。
```

YuoCore 落地：

```text
src/YuoCore.Runtime.Actor/YuoFiberActorEndpoint.cs
tests/YuoCore.Runtime.Actor.Tests/YuoFiberActorEndpointTests.cs
```

修复：

```text
Send/fire-and-forget 路径捕获 ActorException 和普通 Exception。
同步完成快路径仍保留；异步 handler 抛异常时也会在 await helper 内被隔离。
后续 mailbox 消息继续执行。
```

边界控制：

```text
不改变 InvokeAsync 的 request/response 错误传播。
不改变 YuoFiber 的基础 mailbox 数据结构。
当前仍未强制接入日志/指标；Send 异常观测后续应由可选运行时监控或宿主层处理。
```

验证：

```text
dotnet test tests/YuoCore.Runtime.Actor.Tests/YuoCore.Runtime.Actor.Tests.csproj
Passed: 2
```

## 2026-06-08：StartConfig Schema 字段漂移反射测试

参考思想：

```text
ET10 的启动配置是服务拓扑的契约。
YuoCore 也需要让 JSON Schema 和运行时 DTO 自动互相校验，避免新增字段后 schema 没跟上，导致部署配置和代码认知不一致。
```

问题：

```text
原测试用手写数组检查 startconfig.schema.json 是否包含 root/service/fiber 字段。
如果后续 DTO 新增字段，而测试数组也忘记更新，测试不会失败。
```

YuoCore 落地：

```text
tests/YuoCore.Config.Tests/YuoStartConfigTests.cs
```

修复：

```text
StartConfigSchemaCoversCurrentServiceAndFiberFields 改为反射：
YuoStartConfig
YuoServiceConfig
YuoFiberConfig
再把 PascalCase 属性名转换为当前 JSON camelCase 字段名，并与 schema properties 精确比对。
```

边界控制：

```text
不引入 JSON Schema validation 运行时包。
仍只在测试层做契约漂移检查。
如果未来引入 JsonPropertyName 等自定义命名，需要同步扩展测试 helper。
```

验证：

```text
dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj
Passed: 6
```

## 2026-06-08：StartConfig 服务发现注册前预解析

参考思想：

```text
ET10 的启动拓扑配置需要尽量早失败。
服务发布属于对外可见副作用，如果配置中某个服务 endpoint 无法解析，不应该先发布前几个服务再失败，留下半发布拓扑。
```

问题：

```text
YuoStartConfigServiceDiscoveryExtensions.RegisterStartConfigServicesAsync 原来是一边解析 service endpoint，一边注册到 ServiceDiscovery。
如果第二个服务缺少 endpoint，而第一个服务已注册成功，调用方会看到异常，但服务目录已经被部分修改。
```

YuoCore 落地：

```text
src/YuoCore.Server.ServiceDiscovery/YuoStartConfigServiceDiscoveryExtensions.cs
tests/YuoCore.Server.ServiceDiscovery.Tests/YuoStartConfigServiceDiscoveryExtensionsTests.cs
```

修复：

```text
批量注册拆成两段：
1. 先对所有 service 解析 endpoint、metadata 和 lease ttl。
2. 全部成功后，才逐个调用 IYuoServiceDiscovery.RegisterAsync。
```

边界控制：

```text
这不是分布式事务。
如果底层 RegisterAsync 自身在写入后失败，仍需要宿主层清理或未来 outbox/事务型服务发现实现。
当前修复只消除本地配置预处理阶段导致的半注册。
```

验证：

```text
dotnet test tests/YuoCore.Server.ServiceDiscovery.Tests/YuoCore.Server.ServiceDiscovery.Tests.csproj
Passed: 8
```

## 2026-06-08：ServiceDiscovery 同名服务形状变更通知

参考思想：

```text
ET10 的服务发现/配置拓扑强调订阅者看到的是“某类服务集合”的变化，而不是只看服务名是否存在。
如果一个服务从 Map 变成 Gateway，或 metadata 从 zone=1 变成 zone=2，旧查询集合应该收到离开通知，新查询集合应该收到进入通知。
```

问题：

```text
InMemoryYuoServiceDiscovery 允许同名服务重新注册。
原实现只按新 descriptor 发布 Updated。
如果 kind 或 metadata 改变，旧 kind/旧 metadata 的订阅者不会收到 Removed，可能继续认为旧服务仍在自己的查询集合中。
```

YuoCore 落地：

```text
src/YuoCore.ServiceDiscovery/InMemoryYuoServiceDiscovery.cs
tests/YuoCore.ServiceDiscovery.Tests/InMemoryYuoServiceDiscoveryTests.cs
```

修复：

```text
把 kind + metadata 视为服务发现形状。
同名服务重新注册且形状不变：发布 Updated。
同名服务重新注册且形状变化：先按旧 descriptor 发布 Removed，再按新 descriptor 发布 Registered。
```

边界控制：

```text
仍是内存实现。
不引入远程服务发现、主从选举、fencing 或后台线程。
只修正订阅通知语义，保留 Query 结果以当前注册表为准。
```

验证：

```text
dotnet test tests/YuoCore.ServiceDiscovery.Tests/YuoCore.ServiceDiscovery.Tests.csproj
Passed: 11
```

## 2026-06-08：Network 缺失 Endpoint 错误归一

参考思想：

```text
ET10 的通信层会把路由、投递、目标不存在等问题收敛成消息/网络层可识别的错误，而不是让普通业务异常泄漏到上层。
YuoCore 的 Network 包虽然仍是可选边界层，也应该让传输错误有稳定类型，便于宿主统一记录、重试或转换成协议错误。
```

问题：

```text
ProcessAwareYuoTransport 在远端 transport 未配置时抛 YuoTransportException。
但 InMemoryYuoTransport 在本地 endpoint 缺失时抛 InvalidOperationException。
这会让同一类“传输目标不存在”错误在本地/远端路径上表现不一致。
```

YuoCore 落地：

```text
src/YuoCore.Network/InMemoryYuoTransport.cs
tests/YuoCore.Network.Tests/ProcessAwareYuoTransportTests.cs
```

修复：

```text
InMemoryYuoTransport.Resolve 缺失 endpoint 时改为抛 YuoTransportException。
ProcessAware 本地路径会保留该异常类型，远端未配置路径也继续抛 YuoTransportException。
```

边界控制：

```text
不引入真实远程网络。
不改变 envelope、序列化或取消语义。
只收敛 Network 可选层的错误类型。
```

验证：

```text
dotnet test tests/YuoCore.Network.Tests/YuoCore.Network.Tests.csproj
Passed: 9
```

## 2026-06-08：StateSync Delta 防止 NetworkId 错配污染镜像

参考思想：

```text
ET10 的状态同步/消息分发都强调目标身份由框架边界解析，业务 payload 不能把状态写到错误目标。
客户端镜像侧同样需要先验证一整批 delta，再提交变更，避免坏包造成半应用或错位应用。
```

问题：

```text
YuoStateChange 同时包含 change.NetworkId 和 change.State.NetworkId。
原实现只检查 Enter/Update 必须携带 State，没有检查二者一致。
如果坏包把 State.NetworkId=2 放到 change.NetworkId=1 的 Update 中，镜像表会把 2 的状态写到 key=1 下，后续 Web/Unity 表现层可能读到错位实体。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoReplicatedStateTable.cs
tests/YuoCore.StateSync.Tests/YuoReplicatedStateTableTests.cs
```

修复：

```text
ValidateDeltaChanges 在应用任何 change 前检查 Enter/Update 的 State.NetworkId 必须等于 change.NetworkId。
不匹配时抛 InvalidOperationException，整批 delta 不会修改镜像表，也不会推进 LastTick。
```

边界控制：

```text
不改变 Snapshot/Delta 的公开契约。
不新增 Gateway、Protocol、网络或 Unity 依赖。
只收紧无效输入的拒绝语义。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
Passed: 31
```

## 2026-06-08：AI 指南覆盖架构类 Analyzer 诊断

参考思想：

```text
ET10 的 AI 友好入口不只是列出规则，还要让自动化在遇到编译期护栏时知道为什么错、应该改到哪里。
Analyzer 只报错不够，AI 任务入口也必须解释架构诊断背后的职责边界。
```

问题：

```text
YUO0006 / YUO0007 已经能在编译期拦截 Gateway 直接引用 ECS、Protocol DTO 泄漏运行时类型。
但如果 AI 开发指南没有同步提到这些诊断，后续自动化仍可能只看到错误 ID，不理解应该转成 command/Actor 消息或纯数据 DTO。
```

YuoCore 落地：

```text
docs/ai-development-guide.md
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
```

修复：

```text
AI 指南常见错误新增 YUO0007 说明。
新增 Ai_development_guide_mentions_architecture_analyzer_diagnostics 架构测试。
测试会读取 AnalyzerReleases.Shipped.md / AnalyzerReleases.Unshipped.md 中 Category=Architecture 的诊断 ID，并要求这些 ID 出现在 AI 指南中。
```

边界控制：

```text
这是开发期文档 + 测试护栏。
不新增运行时依赖。
不要求 Usage 类诊断全部写进 AI 指南，只强制架构边界诊断进入任务入口。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
Passed: 10
```

## 2026-06-08：协议契约禁止泄漏运行时内部类型

参考思想：

```text
ET10 的网络协议最终以 opcode + DTO 进入消息分发，但协议对象本身不应该暴露连接对象、Fiber、Actor 地址或 Entity 引用。
目标 Entity 由 ActorId / Location / Mailbox 在框架内部解析，业务协议只携带纯业务参数。
```

问题：

```text
YuoCore 已经拆分 Gateway、Protocol、Actor、Runtime、ECS，但仅靠模块依赖矩阵不能阻止业务在自己的 Protocol/Contracts 命名空间里定义 DTO 时引用 ActorId、YuoEntity、YuoLocationKey 等内部类型。
这会污染 Web/Unity 共享协议，也会让客户端误以为可以感知服务端运行时地址。
```

YuoCore 落地：

```text
src/YuoCore.Analyzers/ProtocolBoundaryAnalyzer.cs
YUO0007 ProtocolContractMustNotUseRuntimeTypes
```

规则：

```text
位于 YuoCore.Protocol、*.Protocol、*.Contracts 等协议契约命名空间下的类型，不能引用：
YuoCore.Ecs
YuoCore.Actor
YuoCore.Runtime
YuoCore.Location
YuoCore.Gateway
及其子命名空间类型。
数组、泛型参数、继承链中的运行时类型也会被拦截。
```

设计取舍：

```text
这是开发期 Analyzer，不进入运行时包。
它只检查明确类型引用，不分析序列化字段属性或复杂工厂方法。
允许协议 DTO 使用 string/int/byte[]/数组等纯数据表达外部契约。
内部 Actor 消息如果确实需要 ActorId，应放在 Game/Actor 层，不放在外部 Protocol/Contracts 命名空间。
```

验证：

```text
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
Passed: 31
```

## 2026-06-08：LocationSender 调用取消与 Timeout 语义收敛

参考思想：

```text
ET10 的 MessageLocationSender 面向业务 key 发消息，调用方不直接持有运行时地址。
因此它的 timeout / retry / stale cache / migration lock 等待应该由同一个发送入口统一管理。
```

问题：

```text
YuoActorLocationSender.InvokeAsync 已有 timeout 参数，但使用 linked CancellationTokenSource 后，
如果是调用方 cancellationToken 先取消，也可能被误报成 ActorException(408 timeout)。
这会让上层无法区分“调用者主动取消”和“目标调用超时”。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

修复：

```text
InvokeAsync 记录调用方原始 cancellationToken。
只有 timeout token 已取消且调用方原始 token 没取消时，才转换为 ActorError.Timeout。
调用方取消继续抛 OperationCanceledException。
timeout 预算继续覆盖 Location Resolve、locked resolve retry 等待、stale retry 和 transport invoke。
```

边界控制：

```text
不改变无 timeout 的默认行为。
不新增网络、计时器或 Hosting 依赖。
SendAsync 仍保持 fire-and-forget 语义，不强行引入请求型 timeout。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj
```

## 2026-06-08：Gateway 与 ECS 职责边界 Analyzer 护栏

参考思想：

```text
ET10 中客户端连接、Session、消息分发和实体逻辑虽然都以 Entity/Component 组织，
但职责上仍然区分网络入口、消息路由和目标实体处理。
客户端入口不应该绕过 Actor/Location/Fiber 所有权直接修改游戏实体。
```

YuoCore 落地：

```text
src/YuoCore.Analyzers/GatewayBoundaryAnalyzer.cs
YUO0006 GatewayDispatcherMustNotUseEcs
```

规则：

```text
实现 IYuoGatewayDispatcher 的类型不能直接引用 YuoCore.Ecs 类型。
同时会识别继承自 YuoComponent / YuoEntity 等 ECS 类型的业务组件。
集合泛型、数组、主构造参数、字段、局部变量、方法签名等类型引用都会被拦截。
```

设计取舍：

```text
这是开发期 Analyzer，不进入运行时包。
它只阻止明确的 ECS 类型泄漏，不分析复杂业务调用链，避免过度误报。
Gateway 仍可依赖 command dispatcher、Actor sender 或 Game facade，把客户端请求转发到真正拥有状态的 Fiber。
```

测试：

```text
tests/YuoCore.Analyzers.Tests/YuoTaskUsageAnalyzerTests.cs
  Reports_gateway_dispatcher_primary_constructor_ecs_dependency
  Reports_gateway_dispatcher_component_dependency_through_inheritance
  Reports_gateway_dispatcher_ecs_dependency_inside_collection_type
  Reports_gateway_dispatcher_ecs_dependency_inside_array_type
  Allows_gateway_dispatcher_to_reference_plain_array_type
  Allows_gateway_dispatcher_to_depend_on_command_dispatcher
```

验证：

```text
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
Passed: 25

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)
```

后续可继续分析：

```text
是否需要把“Protocol DTO 不引用 Entity/Component/ActorAddress”也固化为 Analyzer。
是否需要 Generator/模板生成标准 Gateway handler -> command/Actor sender 的代码骨架。
是否需要在 Backend 示例中进一步把旧 oneof protobuf 桥替换为正式 opcode + bytes envelope。
```

## 2026-06-08：协议 opcode catalog 与消息注册表

参考模块：

```text
references/ET10/Packages/cn.etetet.proto/
```

ET10 关键思想：

```text
Proto 文件名携带起始 opcode。
生成代码会产生 Opcode 常量、Message 特性和 ResponseType 信息。
运行时可以按 opcode/type 做统一分发，而不是在每个 handler 或网络桥里手写数字。
```

YuoCore 落地：

```text
src/YuoCore.Protocol/YuoMessageRegistry.cs
src/YuoCore.Protocol/YuoCoreProtocolCatalog.cs
```

设计取舍：

```text
当前不直接搬 ET 的 Proto2CS 工具，也不引入新的 Source Generator。
先提供轻量 YuoMessageRegistry，集中维护 opcode、消息类型、方向和 response type。
当前 yuocore.proto 仍是 MVP oneof 协议，所以 YuoCoreProtocolCatalog 额外提供 oneof case -> opcode 的过渡映射。
Backend 的 GameGatewayProtobufDispatcher 改为从 catalog 推断 opcode，不再自己写 1/2/3/4 魔法数字。
```

边界：

```text
这不是最终外部 envelope。
正式 Web/Unity 协议仍应升级为 version + opcode + requestId + clientSeq + payload bytes。
后续 Source Generator 可以替代手写 catalog，但 catalog 的职责边界应保留在 Protocol 层。
```

测试：

```text
tests/YuoCore.Protocol.Tests/YuoMessageRegistryTests.cs
  Protocol_catalog_covers_all_generated_payload_messages 会反射当前生成的 Protobuf payload 类型，防止新增消息时漏配 opcode。
```

验证：

```text
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj
Passed: 8

dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj
Passed: 10
```

## 2026-06-08：Web/Unity 外部 Protobuf Envelope V1 契约

参考思想：

```text
ET 的外部消息最终通过 opcode + payload 进入统一分发。
客户端和服务端共享协议契约，但不暴露 ActorId、FiberId、LocationKey 等服务端内部地址。
```

YuoCore 落地：

```text
src/YuoCore.Protocol/Contracts/yuocore.proto
  YuoClientEnvelopeV1
  YuoServerEnvelopeV1
```

字段：

```text
Client: version / opcode / request_id / client_seq / payload bytes
Server: version / opcode / request_id / server_seq / error / payload bytes
```

设计取舍：

```text
只新增共享 Protobuf 契约和测试，不立刻替换 Backend 旧 oneof WebSocket 链路。
Gateway 项目仍不依赖 Protocol，保持连接抽象可裁剪。
Backend 后续可以在 WebSocket endpoint 层解析 V1 envelope，再把 payload 转成 YuoGatewayInboundMessage。
```

测试：

```text
tests/YuoCore.Protocol.Tests/YuoMessageRegistryTests.cs
  Client_envelope_v1_round_trips_opcode_sequence_and_payload
  Server_envelope_v1_round_trips_error_and_payload
```

验证：

```text
dotnet test tests/YuoCore.Protocol.Tests/YuoCore.Protocol.Tests.csproj
Passed: 11
```

## 2026-06-08：Backend WebSocket 双协议兼容入口

吸收目标：

```text
在不破坏旧前端的前提下，让服务端入口开始接受正式 opcode + payload bytes envelope。
这对应 ET 的统一 opcode 分发思想，但仍保持 Backend 作为宿主适配层，不让 Gateway 核心依赖 Protobuf。
```

YuoCore 落地：

```text
src/YuoCore.Backend/WebSockets/GameWebSocketEndpoint.cs
src/YuoCore.Backend/WebSockets/GameGatewayProtobufDispatcher.cs
```

当前行为：

```text
旧客户端：
  ClientEnvelope oneof bytes
  -> YuoGatewayInboundMessage(opcode=0, payload=ClientEnvelope bytes)
  -> 返回旧 ServerEnvelope bytes

V1 客户端：
  YuoClientEnvelopeV1(version/opcode/requestId/clientSeq/payload)
  -> YuoGatewayInboundMessage(opcode=请求 opcode, payload=具体请求 bytes)
  -> 返回 YuoServerEnvelopeV1，payload 是具体响应 bytes
```

设计取舍：

```text
GameGatewayProtobufDispatcher 仍保留旧 oneof 适配，方便现有示例和前端不立刻迁移。
Gateway 包仍不依赖 Protocol；双协议识别和 Protobuf 转换只在 Backend 适配层。
V1 payload 当前会转换成旧 ClientEnvelope 后复用 GameClientMessageDispatcher，后续正式业务 dispatcher 可以直接按 opcode 解析 payload。
```

测试：

```text
tests/YuoCore.Backend.Tests/GameClientMessageDispatcherTests.cs
  Gateway_bridge_accepts_v1_payload_and_returns_v1_payload
  WebSocket_endpoint_decodes_v1_client_envelope
  WebSocket_endpoint_encodes_v1_server_envelope
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj
Passed: 13

dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
Passed: 27
```

## 2026-06-08：AOI 单帧 Target Index

参考模块：

```text
references/ET10/Packages/cn.etetet.aoi/
```

ET10 关键思想：

```text
AOI 不应该对每个观察者都全量扫描全部单位。
地图实体会按 Cell 组织，观察者只检查视野覆盖的少量 Cell。
Enter / Leave 事件可以基于可见集合变化生成。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoAoi.cs
  YuoGridAoiTargetIndex
```

设计取舍：

```text
只提取 Cell 索引复用思想，不引入 ET 的长期订阅表、EntityRef 和事件系统。
YuoGridAoiTargetIndex 是单帧不可变快照：MapFiber 可以在移动结算后构建一次，然后给多个 observer 查询。
YuoGridAoiQuery 继续保留原 IYuoAoiQuery 入口，内部通过临时 index 保持兼容。
StateSync 仍不依赖 Gateway、Backend、Protocol 或 Game 具体 Transform 组件。
```

测试：

```text
tests/YuoCore.StateSync.Tests/YuoAoiTests.cs
  Grid_target_index_can_be_reused_by_multiple_observers
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
Passed: 29
```

## 2026-06-08：StateSync Mirror Delta 原子校验

问题：

```text
YuoReplicatedStateTable.ApplyDelta 原来先校验 tick/baseTick，然后边遍历 change 边修改本地状态。
如果同一批 delta 前半部分合法、后半部分存在无效 Update/Enter，客户端镜像会被部分写入，LastTick 又不会推进。
这会造成 Web/Unity 客户端本地状态与服务端 delta 序列不一致。
```

YuoCore 修复：

```text
src/YuoCore.StateSync/YuoReplicatedStateTable.cs
```

语义：

```text
先校验整批 change：
  Enter / Update 必须携带 State。
  Leave / Destroy 可以不携带 State。
  未知 ChangeKind 直接拒绝。

全部校验通过后，才开始修改状态表并推进 LastTick。
```

测试：

```text
tests/YuoCore.StateSync.Tests/YuoReplicatedStateTableTests.cs
  Invalid_delta_is_rejected_before_mutating_any_state
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
Passed: 30
```

## 2026-06-08：Persistence 停服 Drain 语义风险记录

分析对象：

```text
src/YuoCore.Persistence/YuoPersistenceQueue.cs
src/YuoCore.Persistence.Hosting/YuoPersistenceBackgroundService.cs
```

结论：

```text
当前后台写回服务会先从 Channel 取出 batch，再逐条写入 SnapshotStore / EventStore。
如果 stoppingToken 在 batch 中途取消，尚未处理的同批命令已经离开 Channel。
这类命令不会自动回队列，也不一定进入 dead letter。
```

未直接修改原因：

```text
如果改成取消后仍强行写完整个 batch，会改变 shutdown 语义。
数据库慢 IO 或故障时，服务可能长时间无法停止。
如果要商业级可靠，正确方向更可能是持久化 outbox / WAL / 可靠消息队列，而不是简单忽略 CancellationToken。
```

已记录：

```text
docs/risk-and-decisions.md
```

## 2026-06-08：Fiber Mailbox 出队前取消检查

问题：

```text
YuoFiber.DrainMailboxAsync 原来先 TryRead 从 Channel 取出 action，再检查 CancellationToken。
如果外部 tick 取消正好发生在出队后、执行前，mailbox item 会离开队列但没有执行。
这和之前 Timer due batch 的“破坏性出队后取消导致任务丢失”是同类问题。
```

YuoCore 修复：

```text
src/YuoCore.Runtime/YuoFiber.cs
```

语义：

```text
DrainMailboxAsync 在每次 TryRead 前检查外部取消。
如果取消已经发生，不会读取 Channel，因此已排队 Post/Call 留在 mailbox 中，后续 tick 仍可处理。
一旦 action 已经出队，就不会因为外部 tick 取消而静默丢弃；action 仍会收到 cancellationToken，长耗时逻辑可自行决定是否中止。
```

测试：

```text
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
  FiberDrainCancellationBeforeReadDoesNotDropQueuedPost
```

验证：

```text
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj
Passed: 13
```

## 2026-06-08：架构护栏与协议 catalog 后的 ECS shape 性能复测

复测原因：

```text
Gateway/ECS Analyzer 护栏和 Protocol opcode catalog 都属于开发期/协议边界能力。
需要确认它们没有意外进入 ECS Tick 热路径，也顺手刷新 1w 实体 / 100 组件 / 100 system / 4 shard 的当前基线。
```

命令：

```text
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

结果：

```text
tick-ms: avg=100.171 p50=93.492 p95=140.918 max=148.759
entity-tick/s: 99,830
```

结论：

```text
没有看到架构护栏或协议 catalog 导致 ECS shape 压测退化。
本次单次 smoke 优于上一轮 avg=109.666ms / 91,186 entity-tick/s，但不能据此判断为结构性质变。
核心性能风险仍在 ECS system 扫描、组件组合查找和后续是否引入预编译执行器。
```

## 2026-06-08：MessageLocationSender -> YuoActorLocationSender

参考模块：

```text
references/ET10/Packages/cn.etetet.actorlocation/
```

ET10 关键思想：

- `MessageLocationSender` 按业务实体 ID 缓存目标 ActorId。
- 首次发送时通过 Location 查询目标。
- 发送失败或目标不存在时清理缓存。
- 对可迁移 Actor 重新查询 Location 并重试。
- 业务层不直接管理 Actor 所在 Fiber/进程。

YuoCore 落地：

```text
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
```

设计取舍：

- YuoCore 版本使用 `YuoLocationKey`，不是 ET 的 locationType + entityId。
- 当前只实现同进程/同机器可用版本，依赖 `IYuoLocationRegistry` 和 `IActorTransport`。
- 不引入远程网络，不引入 ServiceDiscovery，不污染 `YuoCore.Ecs`。
- 通过 `Func<ActorId, IActorMessage>` / `Func<ActorId, IActorRequest<T>>` 创建消息，业务侧不需要缓存运行时地址。
- 发送/调用遇到 404 时失效缓存并重查一次。

测试：

```text
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
  LocationSender_invokes_by_business_key_and_retries_stale_cached_address
```

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 39
Failed: 0
Skipped: 0
```

后续可继续分析：

- ET10 `MessageLocationSender` 的按 entityId 串行锁。
- 多次退避重试策略。
- Location 迁移锁和 Actor move 期间的消息顺序语义。
- 远程 transport 可选化后是否需要区分 local sender / remote sender。

## 2026-06-08：Actor.Core 去除 Protocol 残留依赖

分析来源：

```text
docs/yuocore-modular-et10-extraction-rules.md
docs/module-dependency-matrix.md
```

问题：

```text
YuoCore.Actor.csproj 仍引用 YuoCore.Protocol，但 Actor 源码没有使用协议类型。
这会让使用 Actor.Core 的单机/Unity/本机服务端场景携带不必要协议包。
```

处理：

```text
移除 YuoCore.Actor -> YuoCore.Protocol 项目引用。
```

结果：

```text
当时 Actor.Core 只依赖 Ecs 和 Location；后续已继续拆出 Actor.Ecs，使 Actor.Core 不再依赖 Ecs。
远程协议和 envelope 仍保留在 Protocol/Network 侧，不污染 Actor.Core。
```

## 2026-06-08：ServiceDiscovery 通用接口与内存实现

参考模块：

```text
references/ET10/Packages/cn.etetet.servicediscovery/
references/ET10/Packages/cn.etetet.startconfig/
```

ET10 关键思想：

- 服务以名称、类型、ActorId/Endpoint、metadata 注册。
- 服务发现支持按类型和 metadata 查询。
- 服务变更可以通知订阅方。
- 分布式版本包含 agent、proxy、heartbeat、master failover 和 fencing。

YuoCore 落地：

```text
src/YuoCore.ServiceDiscovery/
```

设计取舍：

- 只提取服务描述、注册、注销、查询、订阅通知。
- 当前提供 `InMemoryYuoServiceDiscovery`，不引入远程网络。
- 不引入主从选举、心跳代理、分布式 fencing。
- 不污染 `Ecs`、`Runtime`、`Actor.Core`。

测试：

```text
tests/YuoCore.ServiceDiscovery.Tests/
```

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 41
Failed: 0
Skipped: 0
```

后续可继续分析：

- StartConfig 自动发布服务到 ServiceDiscovery。
- Gateway/Map/Persistence 服务启动时注册，停止时注销。
- 分布式 ServiceDiscovery 的 heartbeat、epoch、fencing 是否值得作为可选包实现。

## 2026-06-08：StartConfig metadata 与 endpoint 扩展

参考模块：

```text
references/ET10/Packages/cn.etetet.startconfig/
```

ET10 关键思想：

- StartProcessConfig 描述进程所在机器、内外网地址和端口。
- StartSceneConfig 描述进程内 scene/fiber 的类型、zone、端口和路由身份。
- 配置可按进程、sceneName、sceneType、zone 查询。

YuoCore 落地：

```text
src/YuoCore.Config/YuoStartConfig.cs
schemas/startconfig.schema.json
```

变更：

- `YuoStartConfig` 增加 `ProcessKind`、`InnerEndpoint`、`OuterEndpoint`、`Metadata`。
- `YuoServiceConfig` 增加 `Metadata`。
- `YuoFiberConfig` 增加 `Metadata`。
- 校验重复 service name。

设计取舍：

- 不照搬 ET 的 Machine/Scene/Zone 多表结构。
- 用轻量 metadata 支持后续按 zone、map、service kind 查询。
- 不让 StartConfig 强制依赖 ServiceDiscovery 或远程网络。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 42
Failed: 0
Skipped: 0
```

## 2026-06-08：StateSync Dirty Delta

参考模块：

```text
references/ET10/Packages/cn.etetet.statesync/
```

ET10 关键思想：

- 状态同步不应每帧全量发送。
- 服务端应区分全量快照和增量变更。
- 客户端只应用可见实体的状态变化。

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoStateSnapshotBuilder.cs
```

变更：

- 新增 `IYuoStateDeltaBuilder`。
- 新增 `YuoDirtyStateDeltaBuilder`。
- 只收集带 `YuoNetworkIdentityComponent` 和 dirty `YuoNetworkStateComponent` 的实体。
- 默认构建 delta 后清理 dirty 标记。

设计取舍：

- 暂不实现 AOI。
- 暂不实现字段级差分。
- 暂不实现压缩。
- 先把 Snapshot 和 Delta 的职责拆开，为后续 AOI/压缩/客户端应用做基础。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 43
Failed: 0
Skipped: 0
```

## 2026-06-08：PersistenceQueue 批量 drain

参考思想：

```text
ET 后台服务和 MMO 持久化通常不会一条命令一次数据库 IO，而是通过队列削峰并批处理。
```

YuoCore 落地：

```text
src/YuoCore.Persistence/YuoPersistenceQueue.cs
```

变更：

- 新增 `DequeueBatchAsync(maxCount)`。
- 空闲时等待第一条命令，避免忙等。
- 第一条之后只 drain 当前已到达命令，不额外等待满批次，避免增加尾延迟。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 44
Failed: 0
Skipped: 0
```

## 2026-06-08：Persistence.Hosting 后台写回适配

参考思想：

```text
ET 的 DB/后台服务不会让地图逻辑直接等待慢 IO，而是把保存意图投递给后台执行者。
YuoCore 中基础 Persistence 应保持为抽象和队列，Hosting/ASP.NET Core 相关能力放在可选适配包。
```

YuoCore 落地：

```text
src/YuoCore.Persistence.Hosting/
tests/YuoCore.Persistence.Hosting.Tests/
```

变更：

- 新增 `YuoPersistenceBackgroundService`。
- 新增 `YuoPersistenceWriterOptions`，支持配置批量读取数量和单条错误是否中断后台循环。
- 新增 `IYuoPersistenceFailureSink`，让宿主在不中断后台循环的情况下接收单条写回失败。
- `SaveSnapshotCommand` 转发到 `IYuoSnapshotStore.SaveAsync`。
- `AppendEventCommand` 转发到 `IYuoEventStore.AppendAsync`。
- 默认吞掉单条写回异常，避免一个坏命令阻断后续保存；生产日志、重试和 dead letter 可通过失败 sink 或更完整 Hosting 包补齐。

设计取舍：

- `YuoCore.Persistence` 不引用 `Microsoft.Extensions.Hosting`。
- `YuoCore.Persistence.Hosting` 是可选服务端包，单机和 Unity 不需要导入。
- 失败 sink 只是一层可选抽象，不强制引用 Serilog、OpenTelemetry、PostgreSQL 或 Redis。
- 当前只做串行单 reader 写回，先保证所有权清晰和行为可测，不提前绑定 PostgreSQL/Redis 实现。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 81
Failed: 0
Skipped: 0
```

## 2026-06-08：Gateway Dispatch Pipeline

参考思想：

```text
ET 的消息入口会统一处理路由、错误和请求上下文，业务 handler 不应该重复处理基础框架规则。
```

YuoCore 落地：

```text
src/YuoCore.Gateway/YuoGatewayDispatchPipeline.cs
```

变更：

- 新增 `YuoGatewayDispatchPipeline`。
- 统一处理 `clientSeq` 去重。
- 自动分配 `serverSeq`。
- 将普通异常转成 `YuoError` 出站消息。

设计取舍：

- 不绑定 ASP.NET Core / WebSocket。
- 不处理具体业务协议。
- 不直接修改 ECS。
- Gateway 业务 dispatcher 可以按需套用该 pipeline。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 46
Failed: 0
Skipped: 0
```

## 2026-06-08：StateSync 网格 AOI 查询基础

参考模块：

```text
references/ET10/Packages/cn.etetet.aoi/
```

ET10 关键思想：

- AOI 不应该每次全地图扫描。
- 地图空间可以按 Cell 分桶。
- 观察者的可见范围和当前 Cell 共同决定候选集。
- enter/leave、See/BeSee 和广播是 AOI 的上层事件语义，不应和基础查询混成一个不可裁剪模块。

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoAoi.cs
```

变更：

- 新增 `IYuoAoiQuery`。
- 新增 `YuoGridAoiQuery`，按 X/Z 平面 cell 过滤候选目标。
- 新增 `YuoAoiComponent`，作为 ECS Entity 到 AOI 查询的轻量投影。
- 新增 `YuoEntityAoiQuery`，读取 `YuoNetworkIdentityComponent` + `YuoAoiComponent` 进行查询。

设计取舍：

- 不依赖 `YuoCore.Game` 的 TransformComponent，避免 StateSync 绑定具体游戏域。
- 不维护长期订阅表。
- 不发布 enter/leave 事件。
- 不接入 Gateway 推送和协议 envelope。
- phase 隔离先用字符串，后续可按项目需要改成枚举或配置表生成。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 50
Failed: 0
Skipped: 0
```

## 2026-06-08：Actor.Ecs 可选拆包

参考思想：

```text
ET 把 Actor 消息系统借 Entity/Component 组织，但职责上 Actor 消息层和 ECS 逻辑层不是同一件事。
```

YuoCore 落地：

```text
src/YuoCore.Actor.Ecs/
```

变更：

- 新增 `YuoCore.Actor.Ecs` 项目。
- 将 `YuoEntityActorRegistry`、`YuoEntityActorDispatcher`、`YuoActorHandler<TMessage>`、`YuoActorRequestHandler<TRequest, TResponse>` 移入 `Actor.Ecs`。
- `YuoCore.Actor` 移除对 `YuoCore.Ecs` 的项目引用。
- `YuoCore.Server`、`YuoCore.Backend` 和相关测试显式引用 `YuoCore.Actor.Ecs`。
- 新增 `ActorDependencyTests`，防止 `YuoCore.Actor` 重新引用 `YuoCore.Ecs`。

设计取舍：

- 保留命名空间 `YuoCore.Actor`，减少业务代码 using 变化。
- 用项目引用表达可选能力边界：纯 Actor 不需要 ECS，Entity Actor 才导入 `Actor.Ecs`。
- `YuoCore.Server` 仍依赖 `Actor.Ecs`，因为当前 ServerHost 的职责就是装配 Fiber/World/Entity Actor。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 51
Failed: 0
Skipped: 0
```

## 2026-06-08：ET10 AI 开发友好机制吸收规则

参考模块：

```text
references/ET10/Packages/cn.etetet.harness/
references/ET10/Packages/cn.etetet.sourcegenerator/
```

ET10 关键思想：

- 通过 `AGENTS.md` 和 skill index 把 AI 任务先路由到 code、async、build、test、UnityBridge、Luban 等专项规则。
- 通过 Roslyn Analyzer 把异步、Entity 引用、静态状态、包依赖、组件挂载关系等高频错误转成编译期诊断。
- 通过 Source Generator 生成 EntitySystem 等注册样板，减少手写错误。
- 通过固定构建、测试、导出和 UnityBridge 入口形成 AI 可执行的验证闭环。

YuoCore 落地：

```text
docs/yuocore-modular-et10-extraction-rules.md
```

变更：

- 新增 AI 开发友好横切层，明确 Analyzer、Generator、任务路由文档、脚手架和验证入口都是开发期能力。
- 将 AI 友好机制加入 ET10 优先吸收能力。
- 每轮 ET10 能力吸收时，必须同步分析是否需要 AI 规则、Analyzer、Generator、模板或测试固化。
- 禁止把开发期工具链变成运行时核心依赖。

设计取舍：

- 不照搬 ET10 harness 和 skill 文件结构。
- 先把 AI 友好能力写入长期吸收规则，后续再按 YuoCore 实际模块逐步实现 Analyzer、Generator 和脚手架。
- 保持单机、Unity、Web、服务端导入路径不被开发期工具污染。

验证：

```text
文档规则更新，无需运行构建。
```

## 2026-06-08：ETTask 性能思想与 YuoTask 实验模块

参考模块：

```text
references/ET10/Packages/cn.etetet.core/Scripts/Core/Share/ETTask/
references/ET10/Packages/cn.etetet.sourcegenerator/DotNet~/ET.SourceGenerator/Analyzer/ETTaskAnalyzer.cs
```

ET10 关键思想：

- ETTask 用轻量 awaitable 和对象池降低高频异步分配。
- ETTask 支持 `Coroutine()` 风格 fire-and-forget，适合游戏流程和 Actor 消息内部协程。
- ETTask 必须配合规则和 Analyzer，防止未 await、未 Coroutine、`async void` 等误用。

YuoCore 落地：

```text
src/YuoCore.Tasks/
tests/YuoCore.Tasks.Tests/
benchmarks/YuoCore.Benchmarks/Tasks/
```

变更：

- 新增 `YuoCore.Tasks` 独立基础模块，multi-target `netstandard2.1;net10.0`，不依赖 ECS/Runtime/Actor/Backend。
- 新增 `YuoTask` / `YuoTask<T>`、`YuoTaskCompletionSource` / `YuoTaskCompletionSource<T>` 和 `YuoTaskExceptionHandler`。
- 新增 `YuoTaskMethodBuilder` / `YuoTaskMethodBuilder<T>`，支持内部方法直接声明 `async YuoTask` / `async YuoTask<T>`。
- 内部使用自定义池化 completion source、线程本地小池和全局后备池，减少高频完成路径的通用抽象开销。
- 支持 await、结果完成、异常完成、取消完成、`Forget()` / `Coroutine()`、局部/全局异常处理、池化 source 复用。
- 补齐 `TrySetResult` / `TrySetException` / `TrySetCanceled`，用于并发完成场景避免重复完成直接抛异常。
- 新增 `YuoTask.WaitAll` / `YuoTask.WaitAny` 组合能力。
- 新增 `FromTask` / `FromValueTask` / `AsTask` 非热路径桥接能力；这些 API 允许分配，不作为一帧高频路径。
- 新增 `TaskPrimitiveBenchmarks`，对比默认 `Task`、`TaskCompletionSource`、`ValueTask`、ETTask-like、YuoTask 的同步完成、池化完成、取消/异常路径、桥接路径和并发完成。
- 补充中文注释，明确 YuoTask 是内部高频路径可选工具，不是公共 API 默认替代品。

设计取舍：

- `YuoTask` 作为内部高频基础模块，不直接替换现有 `Task` / `ValueTask` API。
- 对外 Web/ASP.NET/数据库/SDK 边界仍优先使用标准 `Task` / `ValueTask`。
- YuoTask 不再提供 `AsValueTask()` 转换；跨边界时可使用 `FromTask` / `AsTask` 显式桥接，但不应把桥接 API 放入热路径。
- 取消语义使用标准 `CancellationToken` / `OperationCanceledException`，暂不引入 ET 式自定义 cancellation token。
- Benchmark 中的 `EtTaskLike` 是最小兼容实现，不是 ET10 原版 ETTask，结果只能用于趋势对比。
- “0GC”必须以具体 benchmark 的 `Allocated` 数据为准，不在文档中做绝对承诺。

测试：

```text
tests/YuoCore.Tasks.Tests/YuoTaskTests.cs
```

覆盖：

- 已完成任务 await。
- 泛型结果 await。
- completion source 完成、非泛型完成与异常传播。
- async method builder 同步完成、挂起恢复和异常传播。
- `Forget()` 全局异常转发和局部异常处理器。
- fire-and-forget 全局异常处理器自身抛错时不会打穿续体路径。
- source 池复用。
- source 复用后的旧 token 失效。
- `TrySet*` 重复完成语义。
- `SetCanceled` / `FromCanceled` 取消传播。
- `WaitAll` / `WaitAny` 组合等待。
- `FromTask` / `AsTask` 桥接。
- 多 source 并发完成。

验证：

```text
dotnet build src/YuoCore.Tasks/YuoCore.Tasks.csproj --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet build src/YuoCore.Analyzers/YuoCore.Analyzers.csproj --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet build tools/YuoCore.TaskPerf/YuoCore.TaskPerf.csproj --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Tasks.Tests/YuoCore.Tasks.Tests.csproj --no-build
Passed: 30
Failed: 0
Skipped: 0

dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj --no-build
Passed: 10
Failed: 0
Skipped: 0

dotnet run -c Release --project tools/YuoCore.TaskPerf/YuoCore.TaskPerf.csproj
YuoTask.FromResult: 36.43 ns/op, 0.00 B/op
YuoTask pooled: 74.21 ns/op, 0.00 B/op
YuoTask TrySetResult: 55.55 ns/op, 0.00 B/op
YuoTask FromTask: 86.84 ns/op, 72.00 B/op
YuoTask WaitAll small: 140.90 ns/op, 88.00 B/op
YuoTask SetCanceled: 5,329.98 ns/op, 409.27 B/op
Parallel YuoTask pooled: 15.52 ns/op, 0.04 B/op
```

备注：同步池化路径目前仍比最小 ETTask-like 慢一些；`TrySetResult` 路径更快但语义较弱。`FromTask`、`WaitAll`、`SetCanceled` 属于桥接/组合/异常控制流，不应作为热路径。后续是否把 Runtime/Actor 热路径切换到 YuoTask 仍需要结合真实业务 benchmark 裁定。

后续可继续分析：

- 是否需要继续把池按 Fiber 分片，避免极端多线程场景下回落到全局池竞争。
- 是否需要 Analyzer 做更深的数据流分析，例如字段保存后遗失、复杂控制流重复 await。
- 是否将 Runtime/Actor 的高频内部路径从 `Task` 局部替换为 `YuoTask`，需先跑完整 benchmark 数据后决定。

## 2026-06-08：YuoTask Roslyn Analyzer 护栏

参考模块：

```text
references/ET10/Packages/cn.etetet.sourcegenerator/DotNet~/ET.SourceGenerator/Analyzer/ETTaskAnalyzer.cs
```

ET10 关键思想：

- 自定义任务原语必须配套 Analyzer，否则 AI 和人工都容易写出“调用了但没 await/Coroutine”的代码。
- Analyzer 是开发期护栏，不能成为 ECS、Actor、Runtime 的运行时依赖。
- 诊断信息应该说明原因和修复方向。

YuoCore 落地：

```text
src/YuoCore.Analyzers/
tests/YuoCore.Analyzers.Tests/
```

变更：

- 新增 `YuoCore.Analyzers`，目标框架 `netstandard2.0`。
- 新增 `YuoTaskUsageAnalyzer`。
- 新增诊断 `YUO0001`：`YuoTask` 调用结果必须被消费。
- 新增诊断 `YUO0002`：禁止普通 `async void`，事件处理器形状除外。
- 新增诊断 `YUO0003`：禁止 `YuoTask.GetAwaiter().GetResult()` 同步消费。
- 新增 analyzer release tracking 文件，保持 Roslyn analyzer 项目 0 warning。
- 新增 analyzer 测试，覆盖裸调用报错，以及 await、Forget、Coroutine、return、赋值等允许路径。
- 覆盖普通 `async void` 报错、事件处理器形状放行、`YuoTask.GetAwaiter().GetResult()` 报错。

设计取舍：

- 当前只做最小高价值规则，不扫描复杂数据流。
- Analyzer 不被任何运行时模块引用。
- 规则直接匹配 `YuoCore.Tasks.YuoTask` / `YuoTask<T>`，避免误伤普通 `Task` / `ValueTask`。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 111
Failed: 0
Skipped: 0
```

## 2026-06-08：多 shard ECS shape 快速压测入口

参考思想：

```text
ET 类框架的性能验证不能只依赖完整 BenchmarkDotNet，架构迭代时还需要可快速重复的 smoke 压测命令。
```

YuoCore 落地：

```text
benchmarks/YuoCore.Benchmarks/Program.cs
```

变更：

- 新增 `--perf-smoke` 模式。
- 复用 `ParallelShapeWorkload.ParallelShapeRuntime`。
- 输出 tick 平均值、p50、p95、max 和 entity-tick/s。
- 默认面向 1w 实体、100 组件、100 system、多 shard 场景。

验证：

```text
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
tick-ms: avg=111.402 p50=104.248 p95=146.043 max=159.925
entity-tick/s: 89,765
```

风险记录：

```text
该结果是极端 ECS shape，不代表真实挂机业务。
如果真实业务接近此复杂度，需要继续分析 ECS 预编译执行器、组件组合缓存和系统调度策略。
```

## 2026-06-08：AI 开发入口文档

参考思想：

```text
ET10 通过 AGENTS.md、Analyzer、Generator 和专项规则降低 AI 修改大型框架时的走偏概率。
```

YuoCore 落地：

```text
AGENTS.md
docs/ai-development-guide.md
```

变更：

- 新增仓库级 AI 开发入口。
- 新增任务路由表，说明 ECS、Runtime、Actor、Actor.Ecs、Gateway、StateSync、Analyzer 等任务的文件落点。
- 明确依赖边界、常见错误和验证命令。

验证：

```text
文档入口更新，无需重新构建。
```

## 2026-06-08：StartConfig 到 ServiceDiscovery 可选适配

参考思想：

```text
ET 的启动配置和服务发现是协作关系，但不应让所有单机/本机运行时默认背上分布式服务发现依赖。
```

YuoCore 落地：

```text
src/YuoCore.Server.ServiceDiscovery/
tests/YuoCore.Server.ServiceDiscovery.Tests/
```

变更：

- 新增 `YuoCore.Server.ServiceDiscovery` 可选适配包。
- 新增 `RegisterStartConfigServicesAsync`，将 `YuoStartConfig.Services` 发布到 `IYuoServiceDiscovery`。
- 新增 `UnregisterStartConfigServicesAsync`。
- 自动合并 nodeId、processId、processKind、innerEndpoint、outerEndpoint、process metadata 和 service metadata。
- Gateway service 未显式配置 endpoint 时使用 `OuterEndpoint`，其他 service 使用 `InnerEndpoint`。

设计取舍：

- 不让 `YuoCore.Server` 默认引用 `YuoCore.ServiceDiscovery`。
- 不实现 heartbeat、lease、fencing，这些属于分布式实现包。
- 适配层只处理 StartConfig 到 discovery 的发布，不接管 ServerHost 生命周期。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 72
Failed: 0
Skipped: 0
```

## 2026-06-08：YuoActorLocationSender 可配置 stale retry

参考思想：

```text
ET MessageLocationSender 在目标 Actor 迁移或地址过期时，会失效缓存、重新定位并重试，避免业务层缓存运行时地址。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

变更：

- 新增 `YuoActorLocationSenderOptions`。
- 支持 `MaxStaleRetries` 和 `RetryDelay`。
- 默认仍为 1 次 stale retry，避免默认 tail latency 变化。
- 每次 retry 重新 resolve 地址并重新调用 message/request factory，支持 LocationKey 映射到新 ActorId 的迁移场景。
- 新增测试覆盖多次 stale resolve 后才成功的迁移窗口。

设计取舍：

- 不把重试策略做成全局静态配置。
- 不在 Actor.Core 引入分布式 transport 概念。
- 不默认启用多次退避，业务可按调用链路自行配置。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 73
Failed: 0
Skipped: 0
```

## 2026-06-08：项目依赖矩阵测试护栏

参考模块：

```text
references/ET10/Packages/cn.etetet.sourcegenerator/
references/ET10/Packages/cn.etetet.sourcegenerator/DotNet~/ET.SourceGenerator/README_PackageAccessAnalyzer.md
```

ET10 关键思想：

- 架构边界不能只靠文档约定，需要 Analyzer、Generator、测试或脚手架固化。
- Package 之间的访问和依赖关系应该可检查，防止后续为了方便产生反向依赖。
- 工具链属于开发期护栏，不应该污染运行时核心。

YuoCore 落地：

```text
tests/YuoCore.Architecture.Tests/
```

变更：

- 新增 `YuoCore.Architecture.Tests`，不引用任何 YuoCore 运行时项目。
- 解析 `src/**/*.csproj`，检查直接 `ProjectReference` 是否符合当前依赖矩阵。
- 检查 `YuoCore.Abstractions`、`YuoCore.Ecs`、`YuoCore.Tasks` 没有项目引用和 NuGet 包引用。
- 检查 `YuoCore.Persistence.Hosting`、`YuoCore.Server.ServiceDiscovery`、`YuoCore.Backend` 这类可选宿主/适配包没有被核心或普通运行时包反向引用。

设计取舍：

- 先用 xUnit 固化项目级依赖，低风险且不增加运行时包体。
- 暂不做 Roslyn 跨项目成员访问 Analyzer；那会涉及更复杂的 MSBuild/符号上下文。
- 该测试是强约束，未来确实新增模块依赖时必须同步维护文档和白名单。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 84
Failed: 0
Skipped: 0
```

## 2026-06-08：FiberId 同 manager 生命周期内不复用

参考文档：

```text
references/ET10/docs/superpowers/specs/2026-05-05-fiber-id-unification-design.md
```

ET10 关键思想：

- FiberId 会出现在 ActorId、延迟消息、队列项、异步回调和外部缓存中。
- 删除 Fiber 后如果复用相同 FiberId，旧消息可能误投递到新 Fiber。
- 运行时 id 安全优先于短生命周期 Fiber 的槽位回收。

YuoCore 落地：

```text
src/YuoCore.Runtime/YuoFiberManager.cs
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
```

变更：

- `YuoFiberManager` 增加已使用 id 集合。
- 自动创建 Fiber 时跳过已经使用过的 id。
- 显式 `YuoFiberOptions.Id` 如果当前存在或曾经使用过，会抛出明确异常。
- `RemoveFiber` 只移除当前 Fiber，不释放 id 给同一个 manager 复用。

设计取舍：

- 保留 `YuoFiberOptions.Id`，兼容现有 StartConfig 显式 fiber id。
- 暂不引入 ET 的 zone + localSlot 编码，避免一次性破坏 StartConfig、ServerHost 和现有测试。
- 先解决最危险的“同一运行时内删除后复用 id”问题。

测试：

```text
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
  FiberManagerDoesNotReuseRemovedExplicitFiberId
  AutoFiberIdsSkipIdsThatWereReservedExplicitly
```

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj --no-build
Passed: 10
Failed: 0
Skipped: 0

dotnet test YuoCore.slnx --no-build
Passed: 86
Failed: 0
Skipped: 0
```

## 2026-06-08：Location 锁 TTL 与惰性过期清理

参考模块：

```text
references/ET10/Packages/cn.etetet.actorlocation/
references/ET10/Packages/cn.etetet.actorlocation/Scripts/Hotfix/Test/Actorlocation_LockTimeout_AutoUnlock_Test.cs
references/ET10/Packages/cn.etetet.actorlocation/Scripts/Hotfix/Test/Actorlocation_Get_LockRetry_Test.cs
```

ET10 关键思想：

- Location 迁移锁不能永久阻塞，必须有超时清理能力。
- 锁过期后再次读取 Location 时应清理锁状态。
- Location 锁定期间的读取和迁移需要可重试，避免业务层拿到不稳定路由。

YuoCore 落地：

```text
src/YuoCore.Location/
tests/YuoCore.Location.Tests/YuoLocationRegistryTests.cs
```

变更：

- `YuoLocationEntry` 新增 `LockExpiresAt`。
- `IYuoLocationRegistry.LockAsync` 新增可选 `TimeSpan? ttl` 参数。
- `InMemoryYuoLocationRegistry` 在 Register / Resolve / Lock / Unlock / Remove 前惰性清理过期锁。
- 锁过期清理会递增 `Version`。
- `CachingYuoLocationRegistry` 发现缓存中的锁已过期时会删除缓存并回源刷新。

设计取舍：

- 不引入后台 Timer，避免单机/Unity 场景多一个隐藏线程或 HostedService。
- 不把 TTL 设计成强制参数，保留永久锁用于明确由业务流程 Unlock 的迁移场景。
- 暂不改变 Resolve 在未过期锁期间的返回语义；后续应结合 ActorLocationSender 的 retry budget 设计“锁定中请重试”的错误模型。

测试：

```text
ExpiredLockIsClearedBeforeReplacement
ResolveCleansExpiredLockAndIncrementsVersion
CachingRegistryRefreshesExpiredLockFromInnerRegistry
```

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj --no-build
Passed: 8
Failed: 0
Skipped: 0

dotnet test YuoCore.slnx --no-build
Passed: 89
Failed: 0
Skipped: 0
```

## 2026-06-08：ActorLocationSender 处理 locked Location 重试

参考模块：

```text
references/ET10/Packages/cn.etetet.actorlocation/Scripts/Hotfix/Test/Actorlocation_Get_LockRetry_Test.cs
references/ET10/Packages/cn.etetet.actorlocation/Scripts/Model/Server/MessageLocationSender.cs
```

ET10 关键思想：

- Location 正在迁移时，读取方不应该直接拿旧地址继续发消息。
- 调用方应按有限预算等待 Location 解锁并重查。
- 预算耗尽时应该给出可识别错误，而不是把迁移窗口伪装成普通业务失败。

YuoCore 落地：

```text
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

变更：

- `ActorError` 新增 `Conflict` 工厂，使用 409 表示目标暂不可用。
- `YuoActorLocationSenderOptions` 新增 `MaxLockedResolveRetries` 和 `LockedResolveRetryDelay`。
- sender cache 未命中时，如果 Location entry 处于 locked 状态，会按配置等待并重新 Resolve。
- locked 重试耗尽后抛 409，不会向 locked entry 的旧地址发送消息。

设计取舍：

- 不改变 `IYuoLocationRegistry.ResolveAsync` 的返回类型，避免基础 Location 接口一次性变复杂。
- 不默认等待 locked Location，默认行为是快速返回 409；需要迁移等待的调用链显式配置 retry budget。
- 已有 sender cache 的旧地址不会被 Location lock 自动清理，迁移流程仍需调用 `Invalidate` 或后续引入统一迁移 API。

测试：

```text
LocationSender_rejects_locked_location_without_sending_to_old_address
LocationSender_retries_locked_location_until_migration_completes
```

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --no-build
Passed: 12
Failed: 0
Skipped: 0

dotnet test YuoCore.slnx --no-build
Passed: 91
Failed: 0
Skipped: 0
```

## 2026-06-08：StartConfig JSON Schema 契约护栏

参考模块：

```text
references/ET10/Packages/cn.etetet.startconfig/
```

ET10 关键思想：

- 启动拓扑配置是运行时架构的一部分，不能只靠代码里的默认值和校验。
- 配置契约需要能被工具、AI 和运维侧读取。
- 配置 DTO、运行时校验和外部契约必须同步，否则多进程部署会出现启动前不可见的漂移。

YuoCore 落地：

```text
schemas/startconfig.schema.json
tests/YuoCore.Config.Tests/YuoStartConfigTests.cs
```

变更：

- `startconfig.schema.json` 的 required 字段补充 `processKind`，与 `YuoStartConfigLoader.Validate` 对齐。
- 新增测试解析 schema，检查 required 字段包含 `nodeId`、`processId`、`processKind`、`fibers`。
- 新增测试检查 root、service、fiber schema 属性覆盖当前 StartConfig DTO 字段。

设计取舍：

- 暂不引入 JSON Schema validation NuGet 包，避免测试护栏变成运行时依赖误导。
- 当前测试先防止最常见的字段/required 漂移；复杂语义仍由运行时 Validate 覆盖。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj --no-build
Passed: 5
Failed: 0
Skipped: 0

dotnet test YuoCore.slnx --no-build
Passed: 93
Failed: 0
Skipped: 0
```

## 2026-06-08：YuoTask 取消、TrySet 与局部异常处理

参考模块：

```text
references/ET10/Packages/cn.etetet.core/Scripts/Core/Share/ETTask/
```

ET10 关键思想：

- 轻量任务原语需要支持取消、异常和 fire-and-forget 场景，否则调用方会退回到 TaskCompletionSource。
- completion source 在竞态完成场景下需要 TrySet 系列 API，避免重复完成直接抛异常。
- fire-and-forget 的异常出口要可控，不能让一个未 await 协程把运行时打穿。

YuoCore 落地：

```text
src/YuoCore.Tasks/
tests/YuoCore.Tasks.Tests/YuoTaskTests.cs
```

变更：

- 新增 `YuoTask.FromCanceled` / `YuoTask<T>.FromCanceled`。
- 新增 `YuoTaskCompletionSource.TrySetResult`、`TrySetException`、`TrySetCanceled`。
- 新增 canceled 状态，await 时抛出 `OperationCanceledException`。
- 新增 Completing 中间态，避免重复 TrySet 覆盖首次完成写入的结果或异常。
- `Forget` / `Coroutine` 支持传入局部异常处理器；未传入时仍走 `YuoTaskExceptionHandler.UnhandledException`。

设计取舍：

- Set 系列 API 保持重复完成时抛异常，适合发现框架内部错误。
- TrySet 系列 API 用于多来源竞态完成，返回 false 表示已经被其它路径完成。
- 局部异常处理器自身抛异常会被吞掉，避免 fire-and-forget 异常处理再次制造未观察异常。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Tasks.Tests/YuoCore.Tasks.Tests.csproj --no-build
Passed: 30
Failed: 0
Skipped: 0

dotnet test YuoCore.slnx --no-build
Passed: 109
Failed: 0
Skipped: 0
```

## 2026-06-08：手动驱动 Timers 模块

参考模块：

```text
references/ET10/Packages/cn.etetet.core/Scripts/Core/Share/Timer/TimerComponent.cs
references/ET10/Packages/cn.etetet.core/Scripts/Core/Share/Timer/TimerComponentSystem.cs
references/ET10/Packages/cn.etetet.core/Scripts/Hotfix/Test/Timer/
```

ET10 关键思想：

- Timer 属于当前 Fiber/Scene 的执行上下文，由主循环推进，而不是随意开后台线程。
- 短等待适合 `WaitAsync` / `WaitTillAsync`，长期或回调式逻辑适合一次性/重复 timer。
- Timer 回调失败不能破坏整个运行时，至少需要可观测的失败出口。

YuoCore 落地：

```text
src/YuoCore.Timers/
tests/YuoCore.Timers.Tests/
```

变更：

- 新增 `YuoTimerScheduler`，支持 `DelayAsync`、`WaitUntilAsync`、`ScheduleOnce`、`ScheduleRepeated` 和 `Remove`。
- 新增 `YuoTimerId`，避免业务直接把普通 long 和 timer 标识混用。
- 新增 `CallbackFailed` 事件和 `YuoTimerSchedulerOptions`，默认隔离单个回调异常，保留可配置的 fail-fast 入口。
- 调度器不创建后台线程，宿主必须显式调用 `UpdateAsync(now)` 推进时间。

设计取舍：

- `YuoCore.Timers` 只依赖 `YuoCore.Abstractions`，避免污染单机、Unity 和纯同机多线程路径。
- 当前先不让 `YuoCore.Runtime` 强依赖 Timers，后续可以做 `Runtime.Timers` 或可选注入。
- 当前实现以行为正确和边界清晰为先，尚未对海量 timer 做 benchmark。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 116
Failed: 0
Skipped: 0
```

## 2026-06-08：Runtime.Timers 可选 Fiber 桥接

参考思想：

```text
ET 的 TimerComponent 挂在 Scene/Fiber 执行上下文里，由该上下文推进。
YuoCore 不把 Timers 强塞进 Runtime，而是用可选桥接包连接 Fiber Tick。
```

YuoCore 落地：

```text
src/YuoCore.Runtime.Timers/
tests/YuoCore.Runtime.Timers.Tests/
```

变更：

- 新增 `YuoFiberTimerHost`。
- 支持 `ScheduleOnce`、`ScheduleRepeated`、`DelayAsync`、`WaitUntilAsync`。
- `TickAsync(now)` 先推进 timer，再执行 `Fiber.TickAsync`；timer 回调如果 `PostAsync` 到 Fiber mailbox，可以在同一帧被消费。

设计取舍：

- 基础 `YuoCore.Runtime` 不引用 `YuoCore.Timers`，保持可裁剪。
- `YuoFiberTimerHost` 不创建后台线程，也不接管 Fiber 生命周期。
- 当前只提供显式 host，暂不做全局 Fiber -> Timer 自动注册，避免隐藏状态。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Runtime.Timers.Tests/YuoCore.Runtime.Timers.Tests.csproj
Passed: 2
Failed: 0
Skipped: 0

dotnet test YuoCore.slnx --no-build
Passed: 118
Failed: 0
Skipped: 0
```

## 2026-06-08：ActorLocationSender 可注入重试等待

参考思想：

```text
ET 的 Fiber 内等待通常通过 TimerComponent 完成，不应让所有内部重试都硬编码到线程池 delay。
YuoCore 当前保持 Actor.Core 轻量，不直接依赖 Timers，而是先抽出可替换 delay provider。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/IActorDelayProvider.cs
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

变更：

- 新增 `IActorDelayProvider`。
- 新增默认 `TaskActorDelayProvider`，保持旧行为兼容。
- `YuoActorLocationSenderOptions` 新增 `DelayProvider`。
- stale endpoint retry 和 locked location retry 都改走 provider。
- 新增测试验证 stale retry 的等待会调用注入 provider。

设计取舍：

- `YuoCore.Actor` 仍不引用 `YuoCore.Timers`。
- 当前只提供注入点，不直接新增 Actor.Timers 适配包，避免一次性扩大依赖面。
- 默认选项仍不等待 locked Location，避免无意放大 tail latency。

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj --no-build
Passed: 13
Failed: 0
Skipped: 0

dotnet test YuoCore.slnx --no-build
Passed: 119
Failed: 0
Skipped: 0
```

## 2026-06-08：Timer 相对时间基准修正

问题：

```text
YuoTimerScheduler 支持宿主通过 UpdateAsync(now) 手动推进时间，
但 DelayAsync / ScheduleOnce / ScheduleRepeated 仍使用 IYuoClock.UtcNow 作为相对时间基准。
如果宿主用手动时间推进，UpdateAsync(now) 之后再创建相对 timer，due time 可能落在旧时间线上。
```

变更：

- `YuoTimerScheduler` 新增 `CurrentTime`。
- `UpdateAsync(now)` 会更新 `CurrentTime`。
- `DelayAsync`、`WaitUntilAsync`、`ScheduleOnce`、`ScheduleRepeated` 改为基于 `CurrentTime` 判断和计算 due time。
- 新增测试覆盖“手动推进后创建相对 timer，必须以最后一次推进时间为基准”。

设计取舍：

- 这仍然是手动驱动模型，不新增后台线程。
- `IYuoClock` 只作为默认时间来源；一旦宿主显式传入 `now`，调度器内部以该时间线继续计算相对 delay。

验证：

```text
dotnet test tests/YuoCore.Timers.Tests/YuoCore.Timers.Tests.csproj
Passed: 6
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 120
Failed: 0
Skipped: 0
```

## 2026-06-08：Timer 性能烟测入口

背景：

```text
Timers 已经具备基础行为和 Runtime 可选桥接，
但接入 MMO/Fiber 前需要有海量 timer 的调度与触发基准。
```

YuoCore 落地：

```text
benchmarks/YuoCore.Benchmarks/TimerSchedulerBenchmarks.cs
benchmarks/YuoCore.Benchmarks/Program.cs
```

变更：

- `YuoCore.Benchmarks` 新增 `YuoCore.Timers` 引用。
- 新增 `TimerSchedulerBenchmarks`，提供 BenchmarkDotNet 正式基准。
- 新增 `--timer-smoke --timers N` 快速入口，用于 CI/本地快速观察。

本轮 smoke：

```text
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --timer-smoke --timers 100000

timer-smoke: timers=100,000
schedule-ms: 69.927
fire-ms: 251.711
callbacks: 100,000
fire-timer/s: 397,280
```

判断：

```text
当前 PriorityQueue 版本可以承载 10 万级一次性 timer smoke。
同一时刻集中触发 10 万 callback 需要约 250ms，不能放进单个游戏帧热路径。
后续 MMO 使用时应按 shard/时间片拆散到期 timer，或把超大批定时任务放到后台调度执行者。
```

## 2026-06-08：AOI 可见性差分 Tracker

背景：

```text
YuoGridAoiQuery 已能算出当前 observer 可见目标，
但 StateSync 还缺少从“当前可见列表”到 Enter / Update / Leave delta 的稳定转换。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoAoiVisibilityTracker.cs
tests/YuoCore.StateSync.Tests/YuoAoiVisibilityTrackerTests.cs
```

变更：

- 新增 `YuoAoiVisibilityTracker`。
- 按 observerNetworkId 维护上一轮可见 NetworkId 与版本。
- 当前可见但上一轮不可见：输出 `Enter`，携带完整 `YuoEntityState`。
- 当前可见且版本变化：输出 `Update`。
- 上一轮可见但当前不可见或缺少同步状态：输出 `Leave`。
- 支持 `GetVisibleNetworkIds`、`RemoveObserver`、`Clear`。

设计取舍：

- Tracker 不维护 AOI Cell，不接 Gateway，也不接外部协议。
- 输入仍是 `IYuoAoiQuery` 的结果和状态表，MapFiber 可以自行决定何时构建状态表。
- 这不是完整订阅式 AOI；长期 Cell 订阅、See/BeSee 缓存、压缩和客户端应用器仍是后续能力。

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
Passed: 9
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 123
Failed: 0
Skipped: 0
```

## 2026-06-08：StateDelta 批内压缩器

背景：

```text
Gateway 推送前，同一个 NetworkId 可能在同一批 delta 中出现多次变化。
如果不压缩，客户端会收到 Enter 后立刻 Update、Enter 后立刻 Leave 这类无效消息。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoStateDeltaCompressor.cs
tests/YuoCore.StateSync.Tests/YuoStateDeltaCompressorTests.cs
```

变更：

- 新增 `YuoStateDeltaCompressor`。
- `Enter + Update` 合并为携带最新状态的 `Enter`。
- `Enter + Leave/Destroy` 直接移除。
- `Update + Leave/Destroy` 保留终态。
- 保持首个 NetworkId 出现顺序，方便客户端稳定应用。

设计取舍：

- 这里只做批内变化合并，不做 payload 字段级差分。
- 不接协议层，不接 Gateway，保持为 StateSync 纯数据工具。

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
Passed: 13
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 127
Failed: 0
Skipped: 0
```

## 2026-06-08：Replicated State Table

背景：

```text
StateSync 已能生成 snapshot / delta，
但客户端或镜像侧还缺少一个最小状态表来稳定应用这些同步数据。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoReplicatedStateTable.cs
tests/YuoCore.StateSync.Tests/YuoReplicatedStateTableTests.cs
```

变更：

- 新增 `YuoReplicatedStateTable`。
- `ApplySnapshot` 会替换整个 NetworkId -> State 表。
- `ApplyDelta` 支持 Enter / Update / Leave / Destroy。
- Enter / Update 缺少 State 时直接抛错，避免客户端吞掉坏协议。

设计取舍：

- 这是纯 C# 状态表，不创建 Unity GameObject，也不绑定 Web 视图。
- Web TypeScript 侧应用器和 Unity 表现对象池后续作为客户端 SDK 能力处理。

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
Passed: 16
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 130
Failed: 0
Skipped: 0
```

## 2026-06-08：Persistence.Hosting 写回重试

背景：

```text
后台写回默认会隔离单条命令失败，但生产环境常见的是 PostgreSQL/Redis 瞬时抖动。
如果没有按命令重试，临时失败会过早进入失败出口。
```

YuoCore 落地：

```text
src/YuoCore.Persistence.Hosting/YuoPersistenceBackgroundService.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
```

变更：

- `YuoPersistenceWriterOptions` 新增 `MaxAttempts` 和 `RetryDelay`。
- 默认 `MaxAttempts = 1`，保持旧行为不变。
- 每条命令独立重试；全部尝试失败后才进入失败 sink 或按 `StopOnCommandError` 中断。
- 停服取消不参与重试，直接传播。

设计取舍：

- Hosting 包内使用 `Task.Delay` 即可；它本来就是服务端/Worker 适配层，不污染基础 Persistence。
- 当前仍没有可靠 dead letter 存储，也没有批量数据库写入优化。

验证：

```text
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj
Passed: 5
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 131
Failed: 0
Skipped: 0
```

## 2026-06-08：PersistenceQueue 积压观测

背景：

```text
后台写回队列如果只负责削峰，却无法观测积压，就很难判断数据库抖动、保存风暴或消费者卡死。
ET 类服务端通常会把队列长度、高水位和处理速率作为运行时健康信息暴露给宿主。
```

YuoCore 落地：

```text
src/YuoCore.Persistence/YuoPersistenceQueue.cs
tests/YuoCore.Persistence.Tests/YuoPersistenceQueueTests.cs
```

变更：

- 新增 `YuoPersistenceQueueStats`。
- `YuoPersistenceQueue.GetStats()` 返回 `Capacity`、`PendingCount`、`TotalEnqueued`、`TotalDequeued` 和 `HighWatermark`。
- `BacklogRatio` 可用于宿主层判断当前队列积压比例。
- 入队只在 `Channel.WriteAsync` 成功后计数，等待满队列期间被取消不会污染统计。
- 单条出队和批量出队都会更新累计出队数量。

设计取舍：

- 统计能力放在基础 `YuoCore.Persistence`，因为单机、Unity、本机多线程和服务器都可能需要看写回积压。
- 不在基础包中引用 OpenTelemetry、Prometheus、Serilog 或 ASP.NET Core。
- 后续服务端指标导出应放在 Backend 或可选 Hosting 适配层，直接读取 `GetStats()`。

验证：

```text
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj
Passed: 5
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 134
Failed: 0
Skipped: 0
```

## 2026-06-08：ServiceDiscovery 内存 lease 与心跳

参考思想：

```text
ET 类多进程架构中，服务目录不能只靠永久注册。
进程掉线、Map/Gateway 重启或服务迁移后，目录需要 lease/heartbeat/ttl 来避免旧地址长期可见。
```

YuoCore 落地：

```text
src/YuoCore.ServiceDiscovery/
tests/YuoCore.ServiceDiscovery.Tests/InMemoryYuoServiceDiscoveryTests.cs
```

变更：

- `YuoServiceDescriptor` 新增 `LeaseExpiresAt` 和 `LeaseTtl`。
- `IYuoServiceDiscovery.RegisterAsync` 支持可选 `leaseTtl`。
- 新增 `RenewAsync`，用于服务心跳续约。
- 新增 `PruneExpiredAsync`，用于宿主显式清理过期服务。
- `QueryAsync` 会惰性清理已过期服务，避免普通查询读到旧目录。
- 过期清理会向匹配订阅发布 `Removed` 事件。

设计取舍：

- 仍然是内存实现，不引入分布式 agent/proxy、远程网络或持久化目录。
- 不创建后台线程；Unity、单机和同机多线程场景不会被隐藏 HostedService 污染。
- 服务器宿主可以用 BackgroundService、Fiber timer 或自己的主循环定期调用 `RenewAsync` 和 `PruneExpiredAsync`。
- `YuoCore.Server` 仍不直接依赖 `YuoCore.ServiceDiscovery`，StartConfig 发布能力保持在可选适配包中。

验证：

```text
dotnet test tests/YuoCore.ServiceDiscovery.Tests/YuoCore.ServiceDiscovery.Tests.csproj
Passed: 8
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 136
Failed: 0
Skipped: 0
```

## 2026-06-08：Timer 回调自等待护栏

背景：

```text
手动驱动 Timer 的 UpdateAsync 会等待当前到期回调执行完。
如果某个回调里 await 同一个 YuoTimerScheduler 的未来 Delay/WaitUntil，
当前 UpdateAsync 会等回调，下一次 UpdateAsync 又进不来，容易形成自锁。
```

YuoCore 落地：

```text
src/YuoCore.Timers/YuoTimerScheduler.cs
tests/YuoCore.Timers.Tests/YuoTimerSchedulerTests.cs
```

变更：

- `YuoTimerScheduler` 在执行回调时记录当前正在更新的 scheduler。
- 回调内等待同一个 scheduler 的未来时间点会直接抛出 `InvalidOperationException`。
- 默认配置下该异常进入已有 `CallbackFailed` 观测出口，不会卡住后续 timer。
- 立即完成的 past/zero delay 不受影响。

设计取舍：

- 不改成后台线程或 fire-and-forget 回调，避免破坏 Fiber/Unity 主循环的所有权语义。
- 业务需要“回调后再延迟执行”时，应使用 `ScheduleOnce` 或 Post 到所属 Fiber，而不是在 timer 回调里 await 自己。
- 这个护栏会在大量 timer 回调路径上写入 `AsyncLocal`，需要用 timer smoke 观察性能量级。

验证：

```text
dotnet test tests/YuoCore.Timers.Tests/YuoCore.Timers.Tests.csproj
Passed: 7
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 137
Failed: 0
Skipped: 0

dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --timer-smoke --timers 100000
timer-smoke: timers=100,000
schedule-ms: 70.217
fire-ms: 266.322
callbacks: 100,000
fire-timer/s: 375,486
```

性能备注：

```text
该护栏在每个 timer 回调前后写入 AsyncLocal。
当前 10 万同帧回调 smoke 仍保持约 37.5 万 callbacks/s，
相比此前约 25.17 ms/万回调的小样本记录有小幅变慢，但没有数量级退化。
后续如果大量 timer 回调成为热路径，应优先做同帧批次切片，而不是移除自锁护栏。
```

## 2026-06-08：Actor.Timers 可选延迟适配

背景：

```text
YuoActorLocationSender 已经支持 IActorDelayProvider，
但默认实现仍是 Task.Delay。
Fiber/Unity/单机模拟环境更希望重试等待由宿主手动推进的 TimerScheduler 控制，
同时又不能让 YuoCore.Actor 强制依赖 Timers。
```

YuoCore 落地：

```text
src/YuoCore.Actor.Timers/
tests/YuoCore.Actor.Timers.Tests/
```

变更：

- 新增 `YuoCore.Actor.Timers` 可选项目。
- 新增 `YuoTimerActorDelayProvider`，把 `IActorDelayProvider.DelayAsync` 转发到 `YuoTimerScheduler.DelayAsync`。
- 新增测试覆盖手动推进完成、取消和非正数 delay 快速返回。
- 架构依赖测试允许 `Actor.Timers -> Actor + Timers`，但仍禁止核心包反向引用可选适配包。

设计取舍：

- `YuoCore.Actor` 仍不引用 `YuoCore.Timers`。
- `YuoCore.Runtime` 仍不引用 `YuoCore.Actor.Timers`。
- 宿主需要时显式创建 provider 并放入 `YuoActorLocationSenderOptions.DelayProvider`。
- 如果在 timer 回调里等待同一个 scheduler，上一条 Timer 自等待护栏会快速失败，避免隐藏死锁。

验证：

```text
dotnet test tests/YuoCore.Actor.Timers.Tests/YuoCore.Actor.Timers.Tests.csproj
Passed: 3
Failed: 0
Skipped: 0

dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
Passed: 3
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 140
Failed: 0
Skipped: 0
```

## 2026-06-08：Persistence dead letter 契约与 Hosting sink

背景：

```text
后台写回经过重试后仍失败时，只靠日志或临时 failure sink 不够。
生产环境通常需要把最终失败命令写入 dead letter，后续由人工或补偿任务处理。
```

YuoCore 落地：

```text
src/YuoCore.Persistence/YuoPersistenceContracts.cs
src/YuoCore.Persistence.Hosting/YuoPersistenceDeadLetterFailureSink.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
```

变更：

- 新增 `YuoPersistenceDeadLetterRecord`。
- 新增 `IYuoPersistenceDeadLetterStore`。
- 新增 `YuoPersistenceDeadLetterFailureSink`，把 `IYuoPersistenceFailureSink` 的最终失败命令转写为 dead letter record。
- dead letter record 记录原始命令、命令类型、异常类型、异常消息和失败时间。

设计取舍：

- 核心 Persistence 只定义契约，不绑定 PostgreSQL、Redis、文件或 Serilog。
- Hosting sink 只做桥接；具体可靠落库由宿主实现 `IYuoPersistenceDeadLetterStore`。
- 失败出口如果自身失败，仍由 `YuoPersistenceBackgroundService` 既有保护逻辑隔离，避免写回循环被 secondary failure 打断。

验证：

```text
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj
Passed: 6
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 141
Failed: 0
Skipped: 0
```

## 2026-06-08：StateSync Delta BaseTick 校验

背景：

```text
客户端只按 Tick 应用 Delta 时，很难判断中间是否丢过包。
MMO 状态同步需要在发现本地状态与 Delta 基线不一致时，尽快触发重新拉取 Snapshot。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoStateSnapshot.cs
src/YuoCore.StateSync/YuoReplicatedStateTable.cs
src/YuoCore.StateSync/YuoStateSyncGapException.cs
tests/YuoCore.StateSync.Tests/YuoReplicatedStateTableTests.cs
```

变更：

- `YuoStateDelta` 新增可选 `BaseTick`。
- `YuoReplicatedStateTable` 新增 `LastTick`。
- `ApplySnapshot` 会设置 `LastTick`。
- `ApplyDelta` 会拒绝不递增的旧 delta。
- 当 delta 携带 `BaseTick` 时，状态表会在修改状态前检查本地 `LastTick` 是否匹配。
- 基线不匹配时抛出 `YuoStateSyncGapException`，调用方可据此丢弃本地镜像并请求新 snapshot。

设计取舍：

- `BaseTick` 是可选字段，旧 delta 创建方式不需要立刻改。
- 不在 StateSync 核心里绑定 Gateway、Web 或 Unity 重连流程。
- 后续外部协议应把 `BaseTick` 带进 protobuf delta，客户端 SDK 捕获 gap 后发起 snapshot 重拉。

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
Passed: 20
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 145
Failed: 0
Skipped: 0
```

## 2026-06-08：Timer 到期批次限流

背景：

```text
10 万 timer 同一帧到期时，即使单个回调很轻，也会把当前 UpdateAsync 拉长。
MMO shard 更需要可选的时间片策略，把超大 due 批次拆到多个 tick 消费。
```

YuoCore 落地：

```text
src/YuoCore.Timers/YuoTimerSchedulerOptions.cs
src/YuoCore.Timers/YuoTimerScheduler.cs
tests/YuoCore.Timers.Tests/YuoTimerSchedulerTests.cs
```

变更：

- `YuoTimerSchedulerOptions` 新增 `MaxDueTimersPerUpdate`。
- 默认 `null`，保持旧行为：一次 `UpdateAsync` 消费所有已到期 timer。
- 设置正整数后，每次 `UpdateAsync` 最多消费指定数量的 due timer。
- 剩余 due timer 保留在队列中，下一次 `UpdateAsync` 继续消费。
- 非法预算会在 scheduler 构造时抛出。

设计取舍：

- 不自动开后台线程，也不自动丢弃 timer。
- 限流是宿主显式选择，避免单机或小规模项目的 timer 行为突然变化。
- 对状态同步、Location retry 等逻辑，预算过小会增加尾延迟，需要由 shard 配置按业务类型裁定。

验证：

```text
dotnet test tests/YuoCore.Timers.Tests/YuoCore.Timers.Tests.csproj
Passed: 9
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 147
Failed: 0
Skipped: 0

dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --timer-smoke --timers 100000
timer-smoke: timers=100,000
schedule-ms: 64.187
fire-ms: 263.164
callbacks: 100,000
fire-timer/s: 379,990
```

性能备注：

```text
首次实现预算时，默认不限流路径也带了预算判断，10 万 timer smoke fire-ms 约 283.901。
随后拆出默认无预算循环，默认路径恢复到 fire-ms 约 263.164。
因此限流能力默认不应明显拖慢不启用预算的项目。
```

## 2026-06-08：StartConfig ServiceDiscovery lease 发布与续约

背景：

```text
ServiceDiscovery 已支持 lease/renew，但 StartConfig 发布适配仍只能永久注册。
多进程宿主需要用同一份 StartConfig 发布服务，并周期性续约，避免目录里长期保留已退出进程。
```

YuoCore 落地：

```text
src/YuoCore.Server.ServiceDiscovery/YuoStartConfigServiceDiscoveryExtensions.cs
tests/YuoCore.Server.ServiceDiscovery.Tests/YuoStartConfigServiceDiscoveryExtensionsTests.cs
```

变更：

- `RegisterStartConfigServicesAsync(config, leaseTtl)` 支持按统一 TTL 注册 StartConfig 中的 services。
- 新增 `RenewStartConfigServicesAsync`，按 StartConfig 中的 service name 批量续约。
- 旧的无 TTL 注册方法保持不变。

设计取舍：

- 该能力仍位于 `YuoCore.Server.ServiceDiscovery` 可选包中。
- 不让 `YuoCore.Server` 默认依赖 `YuoCore.ServiceDiscovery`。
- 当前是统一 lease ttl；不同服务类型的独立 ttl 策略后续可由宿主分批调用或扩展配置。

验证：

```text
dotnet test tests/YuoCore.Server.ServiceDiscovery.Tests/YuoCore.Server.ServiceDiscovery.Tests.csproj
Passed: 4
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 148
Failed: 0
Skipped: 0
```

## 2026-06-08：ActorLocationSender 重试等待策略

背景：

```text
ActorLocationSender 之前只有固定 RetryDelay / LockedResolveRetryDelay。
跨进程或迁移抖动场景通常需要指数退避和 jitter，但默认路径不能突然变慢。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/IActorRetryDelayPolicy.cs
src/YuoCore.Actor/Location/YuoActorLocationSender.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

变更：

- 新增 `IActorRetryDelayPolicy`。
- 新增 `ActorRetryDelayContext`，包含 LocationKey、失败原因、attempt 和 base delay。
- 新增 `ExponentialActorRetryDelayPolicy`，支持指数退避、最大 delay 和可选 jitter。
- `YuoActorLocationSenderOptions` 新增 `RetryDelayPolicy`。
- stale address retry 和 locked location retry 都会通过策略计算最终等待时间。

设计取舍：

- 默认 `RetryDelayPolicy = null`，保持原固定 delay 行为。
- `IActorDelayProvider` 仍只负责怎么等待，策略只负责等多久。
- jitter 使用显式配置，避免默认路径引入随机尾延迟。
- 不在 Actor.Core 引入 Timer、网络或分布式 transport 依赖。

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj
Passed: 16
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 151
Failed: 0
Skipped: 0
```

## 2026-06-08：Location 原子迁移 API

背景：

```text
之前 Location 迁移需要 Unlock 后再 Register。
这会在两步之间留下窗口：其他调用方可能看到已解锁但还没迁移完成的旧地址。
```

YuoCore 落地：

```text
src/YuoCore.Location/IYuoLocationRegistry.cs
src/YuoCore.Location/InMemoryYuoLocationRegistry.cs
src/YuoCore.Location/CachingYuoLocationRegistry.cs
tests/YuoCore.Location.Tests/YuoLocationRegistryTests.cs
```

变更：

- `IYuoLocationRegistry` 新增 `MigrateAsync`。
- 内存实现要求当前 entry 存在、处于锁定状态、锁 owner 匹配、当前地址等于 expectedAddress。
- 校验通过后在同一个临界区内切换到 newAddress、清理锁、递增 Version。
- 缓存包装器迁移成功后同步刷新缓存。

设计取舍：

- 该能力属于 Location 基础语义，不依赖 Actor、Runtime、网络或数据库。
- 当前只保证内存实现的原子性；Redis/PostgreSQL 分布式实现仍需独立包提供事务或 compare-and-swap。
- ActorLocationSender 的旧缓存仍需要迁移流程调用 Invalidate，或后续做统一迁移广播。

验证：

```text
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj
Passed: 11
Failed: 0
Skipped: 0

dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj
Passed: 16
Failed: 0
Skipped: 0

dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)

dotnet test YuoCore.slnx --no-build
Passed: 154
Failed: 0
Skipped: 0
```

## 2026-06-08：Backend WebSocket 接入 Gateway Pipeline

来源参考：

```text
ET 的 Gate / Session / MessageDispatcher 分层。
客户端连接先进入网关和会话层，再分发到业务消息处理器。
```

吸收点：

```text
Gateway 层处理连接级横切能力，例如序号、异常隔离、统一错误。
业务 dispatcher 只处理已经解码后的游戏请求。
网络 endpoint 只负责 bytes 收发和连接生命周期。
```

YuoCore 落地：

```text
src/YuoCore.Backend/WebSockets/GameGatewayProtobufDispatcher.cs
src/YuoCore.Backend/WebSockets/GameWebSocketEndpoint.cs
tests/YuoCore.Backend.Tests/GameClientMessageDispatcherTests.cs
```

实现说明：

```text
新增 GameGatewayProtobufDispatcher，把旧 ClientEnvelope / ServerEnvelope protobuf oneof 适配到 IYuoGatewayDispatcher。
GameWebSocketEndpoint 每条 WebSocket 连接创建 YuoGatewaySession，并通过 YuoGatewayDispatchPipeline 分发。
外部线协议保持不变，避免现在破坏 Web 示例；正式 opcode + bytes envelope 后续单独升级。
```

边界控制：

```text
YuoCore.Gateway 仍不依赖 Protobuf、ASP.NET Core、ECS 或 Backend。
旧协议适配只放在 YuoCore.Backend 示例宿主内。
clientSeq 仍未进入旧协议，当前 bridge 对旧包使用 clientSeq=0，表示暂不启用输入序号校验。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj
```

## 2026-06-08：StateSync 缺口恢复协调器

来源参考：

```text
ET 的客户端/服务端状态同步流程通常不会在丢包后继续盲目应用增量。
发现基线不连续时，应切换到等待完整状态的恢复路径。
```

吸收点：

```text
Delta 的 BaseTick 不匹配不是普通业务错误，而是镜像状态已经不可信。
缺口发生后，后续 delta 应先被挡住，直到新的完整 snapshot 覆盖本地镜像。
恢复状态应该是 StateSync 层的通用语义，不应该绑定 WebSocket、Unity 或 Gateway。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoStateSyncMirror.cs
tests/YuoCore.StateSync.Tests/YuoStateSyncMirrorTests.cs
```

实现说明：

```text
新增 YuoStateSyncMirror，内部复用 YuoReplicatedStateTable。
ApplyDelta 捕获 YuoStateSyncGapException 后进入 IsSnapshotRequired 状态，并记录 LastGap。
处于 SnapshotRequired 时继续收到 delta 会返回 SnapshotAlreadyRequired，不再修改本地状态表。
ApplySnapshot 会覆盖状态表并清除缺口状态，之后可继续应用新 delta。
```

边界控制：

```text
StateSync 仍然只提供纯 C# 镜像协调能力。
它不发网络请求，不引用 Gateway，不引用 Unity，也不引用 ASP.NET Core。
Web / Unity / Gateway SDK 后续只需要根据 RequiresSnapshot 触发自己的 snapshot 重拉流程。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
```

## 2026-06-08：修复 YuoTask.WaitAny 池化 Source 迟到写入风险

来源参考：

```text
ET 风格池化 awaitable 的核心风险是生命周期必须极其清晰；完成源归还池后，不能再被旧观察者写入。
```

问题：

```text
YuoTask.WaitAny 之前会租用一个 YuoTaskCompletionSource<int>，并把它传给每个 ObserveAnyAsync 后台观察协程。
第一个任务完成后，调用方 await WaitAny 会让这个 source 归还池。
其他较晚完成的观察协程仍持有旧 source 引用，之后调用 TrySetResult / TrySetException 时，可能写穿到已经复用给其他任务的 source。
```

YuoCore 落地：

```text
src/YuoCore.Tasks/YuoTaskCombinators.cs
tests/YuoCore.Tasks.Tests/YuoTaskTests.cs
```

实现说明：

```text
WaitAny 改为把传入 YuoTask 转成标准 Task / Task<T>，再用 Task.WhenAny 管理观察生命周期。
新增回归测试：WaitAny 返回后，较晚完成的原任务不能完成一个随后租到的无关 YuoTaskCompletionSource<int>。
```

边界控制：

```text
这是正确性优先修复，会让 WaitAny 保持桥接/组合工具定位，不作为每帧热路径零分配 API。
WaitAll 仍是顺序消费所有传入任务的简单组合，不涉及后台观察者持有已归还 source。
```

验证：

```text
dotnet test tests/YuoCore.Tasks.Tests/YuoCore.Tasks.Tests.csproj
```

## 2026-06-08：Analyzer 禁止 YuoTask.AsTask 同步阻塞绕路

来源参考：

```text
ET 风格异步链路应保持异步消费，不能通过桥接 API 绕回同步阻塞。
```

问题：

```text
YUO0003 已能拦截 DoAsync().GetAwaiter().GetResult()。
但 DoAsync().AsTask().GetAwaiter().GetResult() 会先把 YuoTask 转成 Task，旧检测只看 GetAwaiter 的直接目标类型，因此漏掉这条同步阻塞绕路。
```

YuoCore 落地：

```text
src/YuoCore.Analyzers/YuoTaskUsageAnalyzer.cs
tests/YuoCore.Analyzers.Tests/YuoTaskUsageAnalyzerTests.cs
```

实现说明：

```text
YUO0003 增强为同时识别 YuoTask.AsTask().GetAwaiter().GetResult()。
规则号不变，因为这是同一类“同步消费 YuoTask”的误用。
```

边界控制：

```text
不禁止正常 await task.AsTask() 或把 AsTask 用在非热路径桥接。
只拦截最终落到 GetResult 的同步阻塞链路。
```

验证：

```text
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
```

## 2026-06-08：StateSync 纯模型与 ECS 适配拆包

来源参考：

```text
ET 风格里网络同步、表现镜像、业务 Entity 和运行时宿主虽然经常都挂在 Entity 体系下，
但真正可复用的是“状态包形状、缺口检测、AOI 可见集合和 Delta 语义”。
这些能力不应该强制 Unity/Web 客户端导入服务端 ECS。
```

YuoCore 落地：

```text
src/YuoCore.StateSync.Abstractions
src/YuoCore.StateSync
src/YuoCore.StateSync.Protocol
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
docs/module-dependency-matrix.md
docs/import-profiles.md
docs/nuget-packaging.md
docs/ai-development-guide.md
```

修复：

```text
新增 YuoCore.StateSync.Abstractions，承载 Snapshot、Delta、Mirror、AOI 查询、可见性差分等纯模型。
YuoCore.StateSync 改为只承载 ECS 组件和 Entity 适配器。
YuoCore.StateSync.Protocol 改为依赖 StateSync.Abstractions + Protocol，并支持 netstandard2.1。
```

边界控制：

```text
StateSync.Abstractions 不引用 ECS、Protocol、Gateway、Actor、Runtime、Backend、数据库或网络。
Unity/Web 客户端需要状态镜像时导入 StateSync.Abstractions；只有服务端或 Unity 本地 ECS 需要 Entity 适配时才导入 StateSync。
```

风险记录：

```text
协议桥在 netstandard2.1 下采用 byte[] 复制进入 ByteString，这是序列化边界成本。
如果后续压测证明 StateSync.Protocol 成为热点，再考虑池化 payload 或批量编码；不能为了零拷贝把 Protobuf 依赖倒灌进 StateSync.Abstractions。
```

验证：

```text
dotnet build src/YuoCore.StateSync.Abstractions/YuoCore.StateSync.Abstractions.csproj --disable-build-servers
dotnet build src/YuoCore.StateSync.Protocol/YuoCore.StateSync.Protocol.csproj --disable-build-servers
dotnet build src/YuoCore.StateSync/YuoCore.StateSync.csproj --disable-build-servers
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.StateSync.Protocol.Tests/YuoCore.StateSync.Protocol.Tests.csproj --disable-build-servers
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --disable-build-servers
```

## 2026-06-08：MMO Game 领域包上移

来源参考：

```text
ET 的业务 Entity/System 可以独立于外层网络、数据库和进程宿主复用。
YuoCore 要同时支持单机、Unity、本机多线程和 MMO 后端，因此纯游戏域类型不能长期停留在 ASP.NET Backend 示例宿主里。
```

问题：

```text
Backend 之前定义了 Player 组件、挂机成长 System、玩家快照 DTO、地图状态 DTO 和内部 command。
这些类型本身不依赖 WebSocket、数据库、Redis 或 ASP.NET，却因为放在 Backend 里导致 Unity/单机无法按需复用。
```

YuoCore 落地：

```text
src/YuoCore.Game/Components/IdleGameComponents.cs
src/YuoCore.Game/Contracts/IdleGameContracts.cs
src/YuoCore.Game/Systems/IdleGameSystems.cs
src/YuoCore.Game/Messages/MmoActorMessages.cs
tests/YuoCore.Game.Tests/YuoIdleGameSystemTests.cs
```

修复：

```text
把挂机示例组件、RuntimeStats 组件、挂机成长 System、玩家快照、世界状态、世界事件和游戏 command 上移到 YuoCore.Game。
Backend 保留 GameRuntime、GameCommandQueue、GameLoopService、WebSocket dispatcher、HTTP Controller、PostgreSQL/Redis 适配。
内部 Actor 示例消息改名为 YuoEnterMapActorRequest / YuoMoveUnitActorRequest，避免和 protobuf 外部协议 EnterMapRequest / MoveRequest 冲突。
```

边界控制：

```text
YuoCore.Game 只依赖 YuoCore.Ecs 和 YuoCore.Actor。
YuoCore.Backend 显式新增对 YuoCore.Game 的项目引用，并在 Architecture 测试白名单中登记。
YuoCore.Game.Tests 只引用 YuoCore.Game，不引用 Backend，证明游戏域包可以脱离 ASP.NET 宿主单独运行。
```

验证：

```text
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

## 2026-06-08：核心 Runtime 路径禁止引用协议/网络/服务端层护栏

分析结论：

```text
YuoCore.Network 当前是协议感知的可选 transport 包，使用 YuoProtocolEnvelope 作为内部传输 envelope。
这对 Backend / 分布式模拟 / 远程 transport 是合理的，但它不能倒灌进 Ecs、Runtime、Actor.Core、Location、Tasks、Timers 等核心路径。
同机 Actor 通讯已经由 YuoCore.Actor 的 InMemoryActorTransport 提供，不需要基础 Runtime 或 Actor.Core 引入 Network/Protocol。
```

YuoCore 落地：

```text
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
  Core_runtime_projects_do_not_reference_protocol_network_or_server_layers
```

护栏语义：

```text
以下核心运行时项目不得引用 Protocol / Network / Gateway / Backend / Server / Persistence / ServiceDiscovery 等可选层：
  YuoCore.Abstractions
  YuoCore.Actor
  YuoCore.Ecs
  YuoCore.Location
  YuoCore.Runtime
  YuoCore.Tasks
  YuoCore.Timers
```

设计取舍：

```text
暂不拆 YuoCore.Network。
它作为“协议 envelope transport”可选包存在，未来真实 TCP/KCP/WebSocket 远程实现可以继续放在 Network 的扩展包中。
当前更重要的是保证单机、Unity 和同机多线程核心路径不被这些可选层污染。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
```

## 2026-06-08：Runtime.Actor 测试边界独立

问题：

```text
YuoFiberActorEndpoint 已经从基础 Runtime 拆到 Runtime.Actor，
但对应测试仍留在 YuoCore.Runtime.Tests。
这会让基础 Runtime 测试项目继续引用 Actor / Actor.Ecs / Runtime.Actor，
从维护视角上削弱“Runtime 不依赖 Actor”的边界。
```

YuoCore 落地：

```text
tests/YuoCore.Runtime.Actor.Tests/
  YuoFiberActorEndpointTests
```

调整：

```text
YuoCore.Runtime.Tests
  只测试 Fiber / Runtime / ECS Tick 行为
  移除 Actor、Actor.Ecs、Runtime.Actor 项目引用

YuoCore.Runtime.Actor.Tests
  显式引用 Runtime.Actor + Actor.Ecs
  专测 ActorEnvelope 进入目标 YuoFiber mailbox 的行为
```

验证：

```text
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj
Passed: 12

dotnet test tests/YuoCore.Runtime.Actor.Tests/YuoCore.Runtime.Actor.Tests.csproj
Passed: 1
```

## 2026-06-08：Runtime.Actor 可选 Fiber-Actor 桥接拆包

来源参考：

```text
ET 的 Fiber 与 Actor 消息经常一起使用，但从 YuoCore 的单机/Unity/Web/MMO 可裁剪目标看，
“Fiber/Tick 调度”与“Actor endpoint 接入 Fiber mailbox”应是两个导入层级。
单机和 Unity 可能只需要多个 World/Fiber，不一定需要 Actor/Location。
```

问题：

```text
YuoFiberActorEndpoint 原来放在 YuoCore.Runtime。
因此基础 Runtime 项目必须引用 YuoCore.Actor，而 Actor 又引用 Location。
这会让只想要 Fiber/Tick 的场景被迫携带 Actor/Location 依赖，违背模块可裁剪原则。
```

YuoCore 落地：

```text
src/YuoCore.Runtime.Actor/
  YuoFiberActorEndpoint
```

调整：

```text
YuoCore.Runtime
  只保留 Fiber、FiberManager、YuoCoreRuntime、Tick 调度
  依赖 Abstractions + Ecs

YuoCore.Runtime.Actor
  依赖 Runtime + Actor
  提供 YuoFiberActorEndpoint
  把 ActorEnvelope 排入目标 YuoFiber mailbox
```

边界控制：

```text
保留 YuoFiberActorEndpoint 的命名空间为 YuoCore.Runtime，降低使用侧 using 变化。
使用方必须通过项目引用显式选择 Runtime.Actor。
YuoCore.Server、YuoCore.Backend 和相关测试显式引用 Runtime.Actor。
基础 Runtime 不再引用 Actor、Location、Protocol、Network、Gateway、Backend。
```

同步文档/护栏：

```text
docs/module-dependency-matrix.md
docs/import-profiles.md
docs/ai-development-guide.md
docs/yuocore-modular-et10-extraction-rules.md
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
YuoCore.slnx
```

验证：

```text
dotnet build YuoCore.slnx --disable-build-servers
0 Warning(s), 0 Error(s)
```

## 2026-06-08：V1 外部协议错误响应回归护栏

来源参考：

```text
ET 的消息分发以 opcode + payload 为中心，错误也应走同一套响应通道。
客户端协议升级为 version/opcode/requestId/payload envelope 后，错误路径不能退回旧 oneof 协议，也不能丢失 requestId。
```

分析结果：

```text
Backend 当前已经能把 V1 envelope 的坏 payload 交给 Gateway pipeline。
pipeline 捕获解析异常后生成 YuoGatewayOutboundMessage.Error。
GameWebSocketEndpoint.EncodeServerMessage 在 V1 模式下会返回 YuoServerEnvelopeV1，并把 opcode 改为 ErrorResponse。
```

YuoCore 落地：

```text
tests/YuoCore.Backend.Tests/GameClientMessageDispatcherTests.cs
```

新增测试固定语义：

```text
V1 ClickRequest + 非法 payload
  -> V1 ServerEnvelope
  -> opcode = ErrorResponse
  -> requestId 保留
  -> serverSeq 推进
  -> payload 为空
  -> Error.Code = failed
```

边界控制：

```text
只补 Backend 可选适配层测试。
Gateway 核心仍不依赖 Protobuf。
Protocol 仍只负责共享契约和 opcode catalog。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj
Passed: 14
```

## 2026-06-08：关闭 ActorFiber 映射为可重试 stale endpoint

来源参考：

```text
ET 的 ActorLocationSender 设计里，Location 缓存可能指向已经迁移、关闭或失效的目标。
发送失败时应尽量映射为统一的“目标不存在 / stale address”语义，再由 LocationSender 或 Router 失效缓存并重新查询。
```

问题：

```text
ActorFiber.DisposeAsync 会关闭内部 Channel。
如果 InMemoryActorTransport 仍然持有这个 endpoint，后续 SendAsync / InvokeAsync 写入 mailbox 时会抛出 ChannelClosedException。
这个异常属于实现细节，不应该泄漏给业务层，也不会被 ActorRouter 当前的 404 stale-location 重试逻辑识别。
```

YuoCore 修复：

```text
src/YuoCore.Actor/Fibers/ActorFiber.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

语义：

```text
关闭后的 ActorFiber mailbox 写入会转换为 ActorException(404)。
ActorRouter 收到 404 后沿用已有逻辑：
  失效旧 Location 缓存
  重新 Resolve
  如果地址变化，则重试一次
```

边界控制：

```text
不改变 IActorEndpoint 接口。
不让 Actor.Core 引入 Runtime/ECS/Network 依赖。
该能力适用于同机内存 transport；未来远程 transport 也应把连接关闭、目标进程不存在等情况映射为同类 404/409 错误，而不是泄漏底层 socket 异常。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj
Passed: 21
```

## 2026-06-08：Runtime mailbox 破坏性出队取消风险修复

来源参考：

```text
ET 风格的 Fiber / Mailbox 核心约束是：目标执行上下文拥有数据，消息进入 mailbox 后应由目标上下文串行处理。
因此 mailbox 不能因为一次外部 tick 取消就在“已经取出但还没执行”的窗口静默丢消息。
```

问题：

```text
YuoFiber.DrainMailboxAsync 原来先 TryRead 从 Channel 取出 action，再检查 CancellationToken。
如果取消正好发生在出队后、执行前，该 mailbox item 会从队列中消失但没有执行。
这会破坏 Actor/Fiber 消息层的可靠性预期。
```

YuoCore 修复：

```text
src/YuoCore.Runtime/YuoFiber.cs
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
```

语义：

```text
每次 TryRead 前先检查外部取消。
如果 tick 已取消，则不从 mailbox 取出 item，等待后续 tick 继续处理。
一旦 item 已经出队，就不再因为外部 tick 取消而静默丢弃；具体业务 action 仍会收到 cancellationToken，可自行处理中止逻辑。
```

边界控制：

```text
不改变 PostAsync / CallAsync 对外 API。
不引入远程网络或分布式语义。
这是 Runtime 同机 Fiber 层正确性修复，仍适用于单机、Unity、Web 后端和 MMO 后端。
```

验证：

```text
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj
dotnet build YuoCore.slnx --disable-build-servers
dotnet test YuoCore.slnx --no-build
```

性能回归：

```text
1w 实体 / 100 组件 / 100 system / 4 shard perf-smoke：
tick-ms: avg=106.097 p50=94.236 p95=164.749 max=170.755
entity-tick/s: 94,253
```

## 2026-06-08：Runtime mailbox tick 预算回归测试

来源参考：

```text
ET/Fiber 模型中，mailbox 必须串行进入所属执行上下文，但突发消息也不能无限饿死当前 tick 的 ECS update。
```

问题：

```text
YuoFiberOptions.MaxMailboxMessagesPerTick 已用于限制每次 DrainMailboxAsync 消费数量。
但测试只验证了 mailbox 先于 World.Update，没有直接验证超出预算的消息会留到后续 tick。
```

YuoCore 落地：

```text
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
```

实现说明：

```text
新增 FiberMailboxBudgetSplitsBurstAcrossTicks。
构造 MaxMailboxMessagesPerTick=1 的 Fiber，连续投递 3 条消息。
每次 TickAsync 只处理一条 mailbox 消息，然后照常执行一次 ECS Update。
```

边界控制：

```text
只补测试，不改变 Runtime 行为。
继续保持单 Fiber 内单 reader mailbox + 单 World 所有权模型。
```

验证：

```text
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj
```

## 2026-06-08：模块依赖矩阵覆盖率护栏

来源参考：

```text
ET/大型框架的包边界需要有文档入口，同时需要自动验证文档没有落后于真实项目结构。
```

问题：

```text
tests/YuoCore.Architecture.Tests 已校验 ProjectReference 和 PackageReference 白名单。
但如果后续新增 src 项目，只要同步了测试白名单，却忘记更新 docs/module-dependency-matrix.md，AI 和维护者仍会缺少模块职责/导入场景说明。
```

YuoCore 落地：

```text
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
```

实现说明：

```text
新增 Module_dependency_matrix_mentions_every_source_project。
测试读取 src 下所有 csproj，要求每个项目名都以反引号形式出现在 docs/module-dependency-matrix.md。
```

边界控制：

```text
这是开发期架构测试，不影响运行时包体。
不要求 import-profiles 覆盖所有模块，因为导入组合文档按场景组织，不是完整模块索引。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
```

## 2026-06-08：YuoTaskCompletionSource 隔离 Continuation 异常

来源参考：

```text
ET/高频异步运行时需要保证完成源状态推进不被外部 continuation 异常破坏；异常应进入统一出口。
```

问题：

```text
YuoTaskCompletionSource.InvokeContinuation 之前直接执行 continuation。
手动注册的 continuation 如果抛异常，可能从 SetResult / SetException 冒出。
runContinuationsAsynchronously=true 时，线程池回调抛异常也缺少统一出口。
```

YuoCore 落地：

```text
src/YuoCore.Tasks/YuoTaskCompletionSource.cs
tests/YuoCore.Tasks.Tests/YuoTaskTests.cs
```

实现说明：

```text
同步和异步 continuation 都通过 InvokeContinuationSafely 执行。
continuation 抛出的异常会进入 YuoTaskExceptionHandler.Publish。
完成源自身仍保持完成状态，调用方后续 await 原任务仍可正常消费结果。
```

边界控制：

```text
不改变 YuoTask 的一次性消费语义。
不引入日志依赖；宿主仍通过 YuoTaskExceptionHandler.UnhandledException 接入 Serilog 或其他日志系统。
```

验证：

```text
dotnet test tests/YuoCore.Tasks.Tests/YuoCore.Tasks.Tests.csproj
```

## 2026-06-08：Analyzer 拦截同一 YuoTask 局部变量多次 await

来源参考：

```text
ET 风格的异步基础设施强调任务消费路径清晰，池化任务尤其不能被当成可重复等待的普通 Task 使用。
```

问题：

```text
YuoTask / YuoTask<T> 当前已经禁止裸调用、同步 GetResult 和字段/属性长期保存。
但如果开发者把一次 YuoTask 调用保存到局部变量后 await 两次，编译期仍不会阻止。
池化 completion source 返回后，第二次 await 可能读到无效状态或掩盖真实异常。
```

YuoCore 落地：

```text
src/YuoCore.Analyzers/YuoTaskUsageAnalyzer.cs
tests/YuoCore.Analyzers.Tests/YuoTaskUsageAnalyzerTests.cs
```

实现说明：

```text
新增 YUO0005。
只检查同一个方法 / 构造函数 / 访问器 / lambda / 本地函数作用域内，同一个 YuoTask 局部变量被 await 多次。
当前是语法级护栏，不证明 if/else 等分支是否互斥；不做跨方法、闭包、集合、字段、ref/out 或复杂别名传播分析。
```

边界控制：

```text
这是编译期护栏，不改变 YuoTask 运行时行为。
重复调用 DoAsync() 并分别 await 不报错，因为它们是不同任务实例。
不同局部变量分别 await 不报错。
```

验证：

```text
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
```

## 2026-06-08：Persistence 增加进程内 Dead Letter Store

来源参考：

```text
ET/服务端运行时通常不会让后台写回失败只停留在日志里，最终失败命令需要进入可检查的失败队列。
```

问题：

```text
YuoCore 已有 IYuoPersistenceDeadLetterStore 契约和 YuoPersistenceDeadLetterFailureSink。
但基础包没有一个可直接使用的内存实现，测试和单机宿主只能各自写 Recording store。
```

YuoCore 落地：

```text
src/YuoCore.Persistence/InMemoryYuoPersistenceDeadLetterStore.cs
tests/YuoCore.Persistence.Tests/YuoPersistenceQueueTests.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
```

实现说明：

```text
新增 InMemoryYuoPersistenceDeadLetterStore。
提供线程安全 AppendAsync、Count、GetRecords 快照读取和 Clear。
GetRecords 返回复制后的稳定快照，避免外部枚举期间和后台写入竞争。
Hosting 的 dead-letter failure sink 测试改为走这个公共实现。
```

边界控制：

```text
它只保存当前进程内存，不承诺持久化可靠性。
不引入 PostgreSQL、Redis、文件 IO、ASP.NET Core 或 OpenTelemetry 依赖。
生产级 dead-letter 存储后续放可选包或宿主实现。
```

验证：

```text
dotnet test tests/YuoCore.Persistence.Tests/YuoCore.Persistence.Tests.csproj
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj
```

## 2026-06-08：Actor Location 迁移协调器

来源参考：

```text
ET 的 Location 迁移不是单纯改地址，还要处理迁移锁、旧地址缓存失效和并发消息投递窗口。
```

问题：

```text
YuoCore 已有 LockAsync、MigrateAsync 和 YuoActorLocationSender.Invalidate。
但业务如果手写迁移流程，容易漏掉 sender cache 失效，导致同进程 sender 继续打到旧 Actor。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/YuoActorLocationMigrationCoordinator.cs
tests/YuoCore.Actor.Tests/YuoActorLocationMigrationCoordinatorTests.cs
```

实现说明：

```text
新增 YuoActorLocationMigrationCoordinator。
迁移流程固定为 Lock -> Migrate -> 对登记的本进程 YuoActorLocationSender 调用 Invalidate(key, oldAddress)。
如果迁移失败，会尝试 Unlock 本轮 owner 持有的锁，避免业务错误把 Location 长时间锁死。
```

边界控制：

```text
该能力仍属于本机 Actor/Location 层，不引入远程网络、服务发现或分布式锁。
它只失效构造时传入的 sender；跨进程广播、fencing epoch 和可靠迁移日志仍是分布式可选层能力。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj
```

## 2026-06-08：StartConfig 运行时校验 mailbox tick 预算

来源参考：

```text
ET 的 StartConfig/进程拓扑配置需要启动早失败；JSON Schema 是外部契约，运行时仍要防御手写对象或测试构造绕过 schema 的情况。
```

问题：

```text
startconfig.schema.json 已要求 fibers[].maxMailboxMessagesPerTick >= 1。
但 YuoStartConfigLoader.Validate 只校验 fiber id、name 和重复 id，没有校验 mailbox tick 预算。
```

YuoCore 落地：

```text
src/YuoCore.Config/YuoStartConfig.cs
tests/YuoCore.Config.Tests/YuoStartConfigTests.cs
```

实现说明：

```text
Validate 新增 MaxMailboxMessagesPerTick <= 0 的 invalid 错误。
测试覆盖手写 YuoStartConfig 绕过 JSON Schema 时仍能被运行时校验拦截。
```

边界控制：

```text
不引入 JSON Schema validation 运行时包。
不改变 schema 字段，也不改变默认值 10_000。
只补齐 schema 和运行时轻量校验之间的一致性。
```

验证：

```text
dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj
```

## 2026-06-08：ServiceDiscovery 订阅通知异常隔离

来源参考：

```text
ET/服务端运行时里的旁路通知不能让单个监听者故障污染核心注册表状态，错误应进入可观测出口而不是破坏主流程。
```

问题：

```text
InMemoryYuoServiceDiscovery.Publish 会同步调用每个订阅者。
如果某个订阅回调抛异常，Register / Renew / Unregister / Prune 调用也会抛，并且后续订阅者收不到通知。
```

YuoCore 落地：

```text
src/YuoCore.ServiceDiscovery/InMemoryYuoServiceDiscovery.cs
tests/YuoCore.ServiceDiscovery.Tests/InMemoryYuoServiceDiscoveryTests.cs
```

实现说明：

```text
InMemoryYuoServiceDiscovery 增加可选 subscriptionErrorHandler。
每个订阅回调独立 try/catch；一个订阅者失败后，其他订阅者仍会收到同一个 YuoServiceChange。
错误出口自身也被隔离，避免错误处理器反向污染服务发现主流程。
```

边界控制：

```text
不引入 Serilog、OpenTelemetry、ASP.NET Core 或后台线程。
内存服务发现仍由宿主显式驱动 lease 和 prune。
分布式服务发现的 agent、fencing 和可靠事件流仍是可选层能力。
```

验证：

```text
dotnet test tests/YuoCore.ServiceDiscovery.Tests/YuoCore.ServiceDiscovery.Tests.csproj
```

## 2026-06-08：StateSync Mirror 拒绝过期 Snapshot

来源参考：

```text
ET/实时同步体系需要处理网络乱序。客户端收到旧 snapshot 时，不能把已经应用的镜像状态回滚到更早 tick。
```

问题：

```text
YuoStateSyncMirror.ApplySnapshot 之前会无条件应用 snapshot。
如果 snapshot 响应晚到，可能覆盖本地已应用的较新 delta。
在缺口恢复状态下，如果 snapshot 没达到 LastGap.ExpectedBaseTick，也会过早清除 SnapshotRequired。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoStateSyncMirror.cs
tests/YuoCore.StateSync.Tests/YuoStateSyncMirrorTests.cs
```

实现说明：

```text
新增 YuoStateSyncApplyReason.StaleSnapshot。
普通状态下，snapshot.Tick < Table.LastTick 会被 Rejected，不修改本地表。
SnapshotRequired 状态下，snapshot.Tick < LastGap.ExpectedBaseTick 会继续返回 SnapshotRequired，不清空 LastGap。
```

边界控制：

```text
只修改客户端/镜像侧协调器，不改变底层 YuoReplicatedStateTable 的原始应用能力。
不绑定 WebSocket、Unity 表现层或 Gateway 重拉策略。
Web/Unity 后续只需要根据 RequiresSnapshot 和 StaleSnapshot reason 决定是否继续等待或重新请求。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
```

## 2026-06-08：YuoTask Analyzer 禁止字段/属性保存

来源参考：

```text
ET 风格的轻量任务适合短生命周期异步链路，不适合被长期保存后重复消费。
YuoTask 是一次性 awaitable，保存到字段或属性很容易导致遗失、重复 await 或池化对象生命周期错误。
```

吸收点：

```text
开发期 Analyzer 应把“看起来已经赋值消费，但实际长期遗失”的路径拦下来。
局部变量仍可用于当前调用链内 await / return / Forget，但字段和属性属于长生命周期存储。
```

YuoCore 落地：

```text
src/YuoCore.Analyzers/YuoDiagnosticIds.cs
src/YuoCore.Analyzers/YuoTaskUsageAnalyzer.cs
src/YuoCore.Analyzers/AnalyzerReleases.Unshipped.md
tests/YuoCore.Analyzers.Tests/YuoTaskUsageAnalyzerTests.cs
```

实现说明：

```text
新增 YUO0004。
YuoTask 返回调用赋给字段、属性或字段/属性 initializer 时会报错。
局部变量、discard、await、return、Forget / Coroutine 仍沿用原规则。
```

边界控制：

```text
Analyzer 是开发期包，不进入运行时依赖。
不改变 YuoTask 运行时语义。
```

验证：

```text
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
```

## 2026-06-08：Source 项目接入 YuoCore.Analyzers

来源参考：

```text
ET 风格的工程护栏不能只停留在 analyzer 项目本身。
如果业务/运行时项目没有实际启用 analyzer，规则不会约束日常开发。
```

吸收点：

```text
Analyzer 应作为编译期工具接入 source 项目，但不能成为运行时依赖。
Tasks 项目内部会实现 Forget/Coroutine 的 async void 桥接，需要暂时排除，避免 analyzer 误伤自身底层实现。
```

YuoCore 落地：

```text
src/Directory.Build.props
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
```

实现说明：

```text
src/Directory.Build.props 通过 ProjectReference 引入 YuoCore.Analyzers。
OutputItemType=Analyzer。
ReferenceOutputAssembly=false。
PrivateAssets=all。
排除 YuoCore.Analyzers 自身和 YuoCore.Tasks。
```

边界控制：

```text
Analyzer 只作为编译期输入，不进入运行时引用。
tests/tools/benchmarks 不受 src/Directory.Build.props 影响，避免压测和 analyzer 测试样例被误伤。
架构测试会检查 props 中的 analyzer 引用形状。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
dotnet build YuoCore.slnx --disable-build-servers
```

## 2026-06-08：Timer 重复回调 StopOnCallbackError 清理

来源参考：

```text
ET 风格的调度器应保证异常路径也不会破坏调度器内部索引。
重复 timer 即使选择向外抛异常，也不能留下已经出队但仍计入 entries 的悬挂状态。
```

问题：

```text
StopOnCallbackError=true 时，重复 timer 回调异常会从 InvokeCallbackAsync 继续抛出。
此时 timer 已经从优先队列取出，但仍保留在 _entries。
后续 UpdateAsync 不会再次触发它，Count 也不会下降。
```

YuoCore 落地：

```text
src/YuoCore.Timers/YuoTimerScheduler.cs
tests/YuoCore.Timers.Tests/YuoTimerSchedulerTests.cs
```

修复：

```text
UpdateAsync 在回调异常向外抛出前，如果当前 entry 是重复 timer，则调用 Remove(timerId)。
这样 StopOnCallbackError=true 保持抛异常语义，同时不会留下不可触发的 timer entry。
```

边界控制：

```text
默认 StopOnCallbackError=false 的失败观测路径不变。
ContinueRepeatedTimerAfterError=false 的吞异常并停止重复 timer 语义不变。
```

验证：

```text
dotnet test tests/YuoCore.Timers.Tests/YuoCore.Timers.Tests.csproj
```

## 2026-06-08：AOI Visibility Delta 自动携带 BaseTick

来源参考：

```text
增量状态同步需要按接收者维护连续游标。
同一个 Map tick 下，不同观察者的可见集合可能不同，因此 BaseTick 也应按 observer 记录。
```

问题：

```text
YuoAoiVisibilityTracker 已能按 observer 生成 Enter / Update / Leave delta。
但生成的 YuoStateDelta 没有 BaseTick，客户端镜像只能检查 tick 递增，不能检查这条 observer 流是否丢过包。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoAoiVisibilityTracker.cs
tests/YuoCore.StateSync.Tests/YuoAoiVisibilityTrackerTests.cs
```

实现说明：

```text
YuoAoiVisibilityTracker 新增 per-observer lastTick 游标。
首次 BuildDelta 返回 BaseTick=null。
同一 observer 的后续 BuildDelta 会把上一 tick 写入 BaseTick。
RemoveObserver / Clear 会同时清理可见集合和 tick 游标。
```

边界控制：

```text
不引入 Gateway、协议或网络请求。
仍然只负责把当前可见集合转换成 StateSync delta。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
```

## 2026-06-08：PersistenceQueue 指标适配器

来源参考：

```text
ET 服务端运行时通常会把 mailbox、队列积压和后台任务状态暴露给监控系统。
YuoCore 的持久化写回队列也需要可观测，否则数据库抖动时很难判断是 Tick、队列还是存储端瓶颈。
```

吸收点：

```text
队列指标应从基础队列统计读取，而不是散落在后台服务日志里。
指标导出是宿主能力，不能让 Persistence 核心包依赖 OpenTelemetry、Prometheus 或 ASP.NET Core。
```

YuoCore 落地：

```text
src/YuoCore.Persistence.Hosting/YuoPersistenceQueueMetrics.cs
tests/YuoCore.Persistence.Hosting.Tests/YuoPersistenceBackgroundServiceTests.cs
```

实现说明：

```text
新增 YuoPersistenceQueueMetrics。
使用 System.Diagnostics.Metrics.Meter 暴露 pending、high_watermark、enqueued、dequeued 和 backlog_ratio。
指标值直接来自 YuoPersistenceQueue.GetStats()。
```

边界控制：

```text
YuoCore.Persistence 仍不引用 Metrics、OpenTelemetry 或 Hosting。
YuoCore.Persistence.Hosting 只使用 .NET 标准 Meter，不直接依赖 OpenTelemetry。
ASP.NET Core / Worker 宿主需要导出时显式 AddMeter(YuoPersistenceQueueMetrics.MeterName)。
```

验证：

```text
dotnet test tests/YuoCore.Persistence.Hosting.Tests/YuoCore.Persistence.Hosting.Tests.csproj
```

## 2026-06-08：YuoTask Analyzer 覆盖 async void 本地函数与 lambda

来源参考：

```text
ET 风格的异步逻辑需要明确异常出口。
async void 会绕过 Task/YuoTask 的异常处理路径，只应限定在事件处理器入口。
```

吸收点：

```text
开发期 Analyzer 应尽早阻止常见异步误用。
只检查普通方法不够，Action async lambda 和本地 async void 同样会吞掉异常。
```

YuoCore 落地：

```text
src/YuoCore.Analyzers/YuoTaskUsageAnalyzer.cs
tests/YuoCore.Analyzers.Tests/YuoTaskUsageAnalyzerTests.cs
```

实现说明：

```text
YUO0002 现在额外检查 LocalFunctionStatement。
YUO0002 现在检查 ParenthesizedLambdaExpression、SimpleLambdaExpression 和 AnonymousMethodExpression。
async lambda 如果被转换为返回 void 的普通 delegate，会报错。
转换为 EventHandler 形状的 async lambda 继续放行。
```

边界控制：

```text
Analyzer 是开发期包，不进入运行时依赖。
不改变 YuoTask 运行时语义。
事件处理器形状仍按原规则保留，避免误伤 UI/宿主事件入口。
```

验证：

```text
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
```

## 2026-06-08：StartConfig ServiceDiscovery 服务级 Lease 策略

来源参考：

```text
ET 服务端不同服务角色通常有不同存活语义。
Gateway 连接入口需要更快发现失活，Map/Persistence 这类服务可以使用更长心跳预算。
```

吸收点：

```text
StartConfig 发布到 ServiceDiscovery 时，不应只能使用全局统一 lease ttl。
服务级策略应保持在 Server.ServiceDiscovery 可选适配包中，不污染基础 Server 配置模型和 ServiceDiscovery 核心接口。
```

YuoCore 落地：

```text
src/YuoCore.Server.ServiceDiscovery/YuoStartConfigServiceDiscoveryExtensions.cs
tests/YuoCore.Server.ServiceDiscovery.Tests/YuoStartConfigServiceDiscoveryExtensionsTests.cs
```

实现说明：

```text
RegisterStartConfigServicesAsync 新增 Func<YuoServiceConfig, TimeSpan?> overload。
RenewStartConfigServicesAsync 新增 Func<YuoServiceConfig, TimeSpan?> overload。
旧的无 lease 和统一 lease ttl API 保持兼容。
```

边界控制：

```text
YuoCore.Server 仍不直接依赖 YuoCore.ServiceDiscovery。
YuoCore.ServiceDiscovery 接口不需要知道 StartConfig。
策略由宿主显式传入，避免在通用库里硬编码 Gateway / Map 的业务默认值。
```

验证：

```text
dotnet test tests/YuoCore.Server.ServiceDiscovery.Tests/YuoCore.Server.ServiceDiscovery.Tests.csproj
```

## 2026-06-08：架构测试增加 NuGet 依赖白名单

来源参考：

```text
ET 的模块拆分依赖明确，核心运行时不会因为上层宿主能力随意携带数据库、网络或观测包。
YuoCore 要兼顾 Unity、单机和服务端，也需要把 NuGet 包依赖纳入自动护栏。
```

吸收点：

```text
ProjectReference 不是唯一耦合来源。
PackageReference 同样会扩大包体、影响 Unity 可用性，并可能把 ASP.NET/Protobuf/OpenTelemetry 泄漏进核心模块。
```

YuoCore 落地：

```text
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
```

实现说明：

```text
新增 AllowedPackageReferences。
新增 Source_package_references_follow_the_documented_matrix 测试。
Backend、Protocol、Analyzers、Persistence.Hosting 等确实需要 NuGet 的项目显式白名单。
Gateway、Actor、Ecs、Runtime、StateSync 等轻量模块默认不允许新增 NuGet 包。
```

边界控制：

```text
这是测试期护栏，不改变运行时代码。
未来新增包必须显式修改白名单，并在文档里说明为什么不破坏可裁剪架构。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
```

## 2026-06-08：StateDeltaCompressor 保留 BaseTick

来源参考：

```text
状态同步压缩只能改变同一批实体变化的表达方式，不能丢失用于有序恢复的 envelope 元数据。
```

问题：

```text
YuoStateDeltaCompressor 压缩后使用 new YuoStateDelta(delta.Tick, changes)。
这会丢掉 delta.BaseTick，导致后续镜像侧无法继续用 BaseTick 做缺口检测。
```

YuoCore 落地：

```text
src/YuoCore.StateSync/YuoStateDeltaCompressor.cs
tests/YuoCore.StateSync.Tests/YuoStateDeltaCompressorTests.cs
```

修复：

```text
压缩结果保留原 delta.BaseTick。
新增 Compression_preserves_base_tick_for_gap_detection 回归测试。
```

边界控制：

```text
不改变 Enter / Update / Leave / Destroy 的合并语义。
只修正压缩器对状态同步元数据的透传。
```

验证：

```text
dotnet test tests/YuoCore.StateSync.Tests/YuoCore.StateSync.Tests.csproj
```

## 2026-06-08：Location Registry 统一遵守取消令牌

来源参考：

```text
ET 的 Location / ActorLocationSender 语义强调“路由查询是一次可取消的运行时操作”。
YuoCore 的内存 Location 虽然只是本机实现，也应该和未来 Redis/PostgreSQL/远端实现保持同一 API 契约。
```

问题：

```text
InMemoryYuoLocationRegistry 暴露 CancellationToken，但 Register / Resolve / Lock / Unlock / Migrate / Remove 没有检查取消。
CachingYuoLocationRegistry 在 Resolve 缓存命中时会直接返回缓存，导致调用方已经取消的请求仍表现为成功。
```

YuoCore 落地：

```text
src/YuoCore.Location/InMemoryYuoLocationRegistry.cs
src/YuoCore.Location/CachingYuoLocationRegistry.cs
tests/YuoCore.Location.Tests/YuoLocationRegistryTests.cs
```

修复：

```text
所有公开 Location 操作在进入锁、读取缓存或转发 inner 前先调用 cancellationToken.ThrowIfCancellationRequested()。
新增测试覆盖取消后的 Register 不写入、Remove 不删除，以及缓存命中 Resolve 也必须抛 OperationCanceledException。
```

边界控制：

```text
不引入远程网络、数据库、日志或 Hosting 依赖。
不改变 Location 锁、版本号、迁移和缓存失效语义。
这是轻量契约一致性修复，为后续可选分布式 Location 实现提供行为基准。
```

验证：

```text
dotnet test tests/YuoCore.Location.Tests/YuoCore.Location.Tests.csproj
```

## 2026-06-08：Actor 同进程 Registry / Transport 统一遵守取消令牌

来源参考：

```text
ET 的 Actor 通信会把超时、取消和目标投递失败作为不同运行时结果处理。
YuoCore 的同进程 Actor registry / transport 虽然不走网络，也应让取消优先于地址查询和 endpoint 投递。
```

问题：

```text
InMemoryActorLocationRegistry 暴露 CancellationToken，但取消后仍会 Register / Resolve / Unregister。
InMemoryActorTransport 取消后仍会查 endpoint，并可能继续投递到目标 endpoint。
```

YuoCore 落地：

```text
src/YuoCore.Actor/Location/InMemoryActorLocationRegistry.cs
src/YuoCore.Actor/Transport/InMemoryActorTransport.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

修复：

```text
Actor registry 在所有公开操作入口检查 cancellationToken。
Actor in-memory transport 在 endpoint lookup 和 endpoint dispatch 前检查 cancellationToken。
新增测试覆盖取消后的 registry 不写入，以及 transport 不调用 endpoint。
```

边界控制：

```text
不改变 ActorRouter 的 NotFound / Timeout / stale retry 错误语义。
不引入网络、定时器、ECS 或 Hosting 依赖。
只修正同进程 Actor 核心对标准 .NET 取消契约的遵守。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj
```

## 2026-06-08：Network 可选传输层统一遵守取消令牌

来源参考：

```text
ET 的跨进程通信把“取消/超时/远端不可达”区分处理。
YuoCore 的 Network 是可选边界层，即使当前只有内存、序列化包装和进程选择 transport，也应先固化同样的取消优先级。
```

问题：

```text
InMemoryYuoTransport 取消后仍会查 endpoint 并可能投递。
SerializedYuoTransport 取消后仍会先执行 envelope 编解码，再调用 inner transport。
ProcessAwareYuoTransport 取消后仍会选择 local/remote，远端未配置时可能抛 YuoTransportException 而不是 OperationCanceledException。
```

YuoCore 落地：

```text
src/YuoCore.Network/InMemoryYuoTransport.cs
src/YuoCore.Network/SerializedYuoTransport.cs
src/YuoCore.Network/ProcessAwareYuoTransport.cs
tests/YuoCore.Network.Tests/ProcessAwareYuoTransportTests.cs
```

修复：

```text
三个 transport 在入口先检查 cancellationToken。
新增测试覆盖取消后不投递 endpoint、不调用 inner transport，以及不进入远端缺失选择分支。
```

边界控制：

```text
不新增远程网络实现。
不改变 envelope 序列化格式和 ProcessAware 路由规则。
Network 仍然是可选包，不反向污染 ECS / Runtime / Actor.Core。
```

验证：

```text
dotnet test tests/YuoCore.Network.Tests/YuoCore.Network.Tests.csproj
```

## 2026-06-08：StartConfig ServiceDiscovery 适配层取消优先

来源参考：

```text
ET 的服务拓扑注册属于启动/续租流程，取消应该优先于批量注册、续租和注销中的任何后续副作用。
YuoCore 的 Server.ServiceDiscovery 是可选适配包，也需要和底层 InMemory/未来分布式发现实现保持同一取消契约。
```

问题：

```text
YuoStartConfigServiceDiscoveryExtensions 会把 StartConfig.Services 循环注册、续租或注销。
底层 discovery 已遵守 CancellationToken，但适配层自身没有在公开入口和每轮循环前显式检查取消。
取消请求可能先执行配置校验、endpoint 推导或进入下一轮服务处理。
```

YuoCore 落地：

```text
src/YuoCore.Server.ServiceDiscovery/YuoStartConfigServiceDiscoveryExtensions.cs
tests/YuoCore.Server.ServiceDiscovery.Tests/YuoStartConfigServiceDiscoveryExtensionsTests.cs
```

修复：

```text
Register / Renew / Unregister 适配入口先检查 cancellationToken。
批量循环每轮开始前再次检查 cancellationToken，避免取消后继续处理后续服务。
新增测试覆盖取消后不注册服务，以及取消后不注销已注册服务。
```

边界控制：

```text
不让 YuoCore.Server 反向依赖 YuoCore.ServiceDiscovery。
不新增远程服务发现实现。
只修正可选适配层的批量操作取消优先级。
```

验证：

```text
dotnet test tests/YuoCore.Server.ServiceDiscovery.Tests/YuoCore.Server.ServiceDiscovery.Tests.csproj
```

## 2026-06-08：Gateway Pipeline 取消不污染客户端序号

来源参考：

```text
ET 的网络入口会把连接、消息投递和业务处理边界拆开；取消/断开不应该污染业务重放判断。
YuoCore Gateway 是 Web/Unity 客户端进入后端 runtime 的边界，也需要保证取消优先于 clientSeq 接受。
```

问题：

```text
YuoGatewayDispatchPipeline 在检查 CancellationToken 前先调用 session.TryAcceptClientSeq。
如果 dispatch 在取消状态下进入，session 仍可能记录这个 clientSeq，客户端重试同一请求时会被错误判定为 stale replay。
```

YuoCore 落地：

```text
src/YuoCore.Gateway/YuoGatewayDispatchPipeline.cs
tests/YuoCore.Gateway.Tests/YuoGatewayDispatchPipelineTests.cs
```

修复：

```text
Gateway pipeline 入口先调用 cancellationToken.ThrowIfCancellationRequested()。
新增测试覆盖取消后不调用 inner dispatcher，且同一 clientSeq 后续重试仍能被接受。
```

边界控制：

```text
不改变 Gateway dispatcher 的业务接口。
不把 Gateway 反向耦合到 ECS / Actor / Backend。
只修正客户端接入边界的取消与重试语义。
```

验证：

```text
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj
```

## 2026-06-08：ActorRouter Invoke timeout 覆盖 Location Resolve

来源参考：

```text
ET 风格 RPC timeout 应覆盖一次调用的完整路由和投递过程，而不是只覆盖目标 handler 执行。
如果 Location 查询卡住，调用方也应该在同一个 timeout 预算内得到超时错误。
```

问题：

```text
ActorRouter.InvokeAsync(request, timeout) 原来先 ResolveAddressAsync，再创建 timeout CancellationTokenSource。
因此慢 LocationRegistry 不受 timeout 约束，只有 transport.InvokeAsync 阶段会触发 timeout。
```

YuoCore 修复：

```text
src/YuoCore.Actor/Routing/ActorRouter.cs
tests/YuoCore.Actor.Tests/ActorRouterTests.cs
```

语义：

```text
timeout token 在 ResolveAddressAsync 之前创建并启用。
Resolve + transport.Invoke + stale retry 都共享同一个 timeout token。
如果 timeout 触发且调用方原始 cancellationToken 没有取消，统一转换为 ActorException(408)。
```

边界控制：

```text
调用方自己的 cancellationToken 取消仍然抛 OperationCanceledException。
404 stale retry 仍只处理“已经成功 Resolve 出旧地址后，endpoint/transport 返回 404”的情况。
如果 Resolve 本身返回 location missing 404，不会误进入 stale retry。
```

验证：

```text
dotnet test tests/YuoCore.Actor.Tests/YuoCore.Actor.Tests.csproj
Passed: 23
```

## 2026-06-08：Gateway Pipeline 中途取消回滚 clientSeq

来源参考：

```text
ET 风格入口会区分连接取消、消息投递失败和业务处理结果。
客户端请求如果在业务处理中被取消打断，不应该把这次未完成请求永久记为已消费，导致同序号重试被误判为 stale。
```

问题：

```text
Gateway pipeline 已经修复了“入口取消前先接受 clientSeq”的问题。
但如果取消发生在 session.TryAcceptClientSeq 成功之后、inner dispatcher 处理过程中，
pipeline 会抛 OperationCanceledException，同时 LastClientSeq 已经前进。
客户端重试同一 clientSeq 会被错误判定为 stale client sequence。
```

YuoCore 修复：

```text
src/YuoCore.Gateway/YuoGatewaySession.cs
src/YuoCore.Gateway/YuoGatewayDispatchPipeline.cs
tests/YuoCore.Gateway.Tests/YuoGatewayDispatchPipelineTests.cs
```

语义：

```text
TryAcceptClientSeq 新增 internal overload，返回接受前的 previousClientSeq。
Pipeline 捕获由同一个 cancellationToken 触发的 OperationCanceledException 时，尝试把 clientSeq 回滚到 previousClientSeq。
回滚使用 CompareExchange：
  只有 LastClientSeq 仍等于本次 accepted seq 时才回滚。
  如果已经有更高 clientSeq 被接受，则不会覆盖更高进度。
```

边界控制：

```text
普通业务异常会转成 error response，clientSeq 保持已消费语义。
只处理 cancellationToken 已取消的取消异常。
回滚 API 是 Gateway 内部机制，只通过 InternalsVisibleTo 暴露给测试，不作为普通业务会话 API。
不引入 Actor/ECS/Backend 依赖，Gateway 仍只处理连接会话和协议入口通用规则。
```

验证：

```text
dotnet test tests/YuoCore.Gateway.Tests/YuoCore.Gateway.Tests.csproj
Passed: 7
```

## 2026-06-08：Backend 示例 Gateway Bridge 取消优先

来源参考：

```text
ET 的网络入口会把断线/取消和业务错误区分开。
YuoCore.Backend 当前只是示例宿主，但它承载 WebSocket demo 的旧 protobuf bridge，也需要避免取消后继续解析和执行业务副作用。
```

问题：

```text
GameGatewayProtobufDispatcher 在检查 CancellationToken 前会解析 ClientEnvelope。
GameClientMessageDispatcher 在取消后仍可能进入 Hello 分支并创建玩家。
```

YuoCore 落地：

```text
src/YuoCore.Backend/WebSockets/GameGatewayProtobufDispatcher.cs
src/YuoCore.Backend/WebSockets/GameClientMessageDispatcher.cs
tests/YuoCore.Backend.Tests/GameClientMessageDispatcherTests.cs
```

修复：

```text
Backend protobuf bridge 和旧业务 dispatcher 在入口先检查 cancellationToken。
新增测试覆盖取消优先于坏 protobuf 解析，以及取消后不会创建玩家。
```

边界控制：

```text
YuoCore.Backend 仍是示例宿主。
不改变 Gateway / Protocol / Game 核心接口。
不把 ASP.NET Core 或旧 protobuf bridge 反向带入核心模块。
```

验证：

```text
dotnet test tests/YuoCore.Backend.Tests/YuoCore.Backend.Tests.csproj
```

## 2026-06-08：Fiber CallAsync 取消后不执行未开始的调用

来源参考：

```text
ET 的 Fiber/Actor 模型强调执行所有权：跨 Fiber 调用必须排队到目标执行者。
同时，调用方取消后如果目标还未开始处理，不应该再产生业务副作用，否则客户端/上游会认为请求失败但状态已经改变。
```

问题：

```text
YuoFiber.CallAsync 把 action 写入 mailbox 后，调用方如果在目标 Fiber Tick 前取消，等待方会收到 OperationCanceledException。
但 queued action 后续仍会被目标 Fiber 执行，可能修改 World / Entity / Component。
```

YuoCore 落地：

```text
src/YuoCore.Runtime/YuoFiber.cs
tests/YuoCore.Runtime.Tests/YuoRuntimeTests.cs
```

修复：

```text
CallAsync 的 mailbox wrapper 在真正调用业务 action 前检查原始调用方 CancellationToken。
如果调用方已经取消，则 completion 标记取消并跳过 action。
新增测试覆盖取消后再 Tick 不会执行已排队但尚未开始的 action。
```

边界控制：

```text
不改变 PostAsync 的 fire-and-forget 语义。
不强行抢占已经开始执行的 action，避免破坏 Fiber 内部一致性。
不引入 Actor / Network / Hosting 依赖，Runtime 仍保持同机多 Fiber 可选层。
```

验证：

```text
dotnet test tests/YuoCore.Runtime.Tests/YuoCore.Runtime.Tests.csproj
```

## 2026-06-08：取消语义修复后的 ECS shape 性能复测

复测原因：

```text
连续修复 Location、Actor、Network、Gateway、Backend 和 Runtime 的取消语义后，需要确认这些边界检查没有拖慢 1w 实体 / 100 组件 / 100 system / 4 shard 的核心 ECS 压测路径。
```

命令：

```text
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

结果：

```text
tick-ms: avg=109.666 p50=99.599 p95=146.922 max=167.084
entity-tick/s: 91,186
```

结论：

```text
与上一轮 avg=111.940ms / p95=145.135ms / 89,333 entity-tick/s 基本同级。
没有发现取消语义修复导致 ECS shape 压测退化。
核心性能风险仍在 ECS system 扫描、组件组合查找和后续是否引入预编译执行器。
```

## 2026-06-08：Timer Update 出队后不因取消丢失 due timer

来源参考：

```text
ET 的 TimerComponent 属于执行上下文内部调度；一旦某批 due timer 已经进入本帧处理，就不能因为外部取消在中途把 timer 从队列里移除却不执行。
YuoCore 的 TimerScheduler 是手动驱动的单机/Unity/服务端通用模块，也需要保持这个确定性。
```

问题：

```text
YuoTimerScheduler.UpdateAsync 会先 DequeueDueEntries，把 one-shot timer 从 _entries 移除。
随后 foreach 每个 due entry 前再次检查 cancellationToken。
如果第一个 timer callback 取消了本次 Update token，后续已经出队的 due timer 会被跳过，而且已经不在队列里，等价于丢 timer。
```

YuoCore 落地：

```text
src/YuoCore.Timers/YuoTimerScheduler.cs
tests/YuoCore.Timers.Tests/YuoTimerSchedulerTests.cs
```

修复：

```text
取消仍在破坏性出队前检查。
一旦 due batch 已出队，UpdateAsync 会处理完整个已出队批次。
新增测试覆盖第一个 due callback 取消 Update token 后，第二个同批 due timer 仍会执行。
```

边界控制：

```text
MaxDueTimersPerUpdate 仍然限制单次 Update 批次大小。
不创建后台线程。
不改变 callback 自己接收 CancellationToken 的能力；长耗时 callback 仍可自行检查 token。
```

验证：

```text
dotnet test tests/YuoCore.Timers.Tests/YuoCore.Timers.Tests.csproj
```

## 2026-06-08：AI 开发入口覆盖每个 src 模块

来源参考：

```text
ET10 根 AGENTS.md 很薄，但核心作用是把 AI 先导向项目规则入口，避免跳过包依赖、构建、测试和任务路由。
YuoCore 已有 docs/ai-development-guide.md，需要把这个入口从“说明文档”提升为可测试的工程护栏。
```

问题：

```text
docs/ai-development-guide.md 的任务路由表没有覆盖所有 src 项目。
例如 Abstractions、Timers、Network、ServiceDiscovery 和若干可选适配包没有明确落点。
新增模块时已有 module-dependency-matrix 覆盖测试，但 AI 任务入口没有同级测试。
```

YuoCore 落地：

```text
docs/ai-development-guide.md
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
```

修复：

```text
补齐 AI 任务路由表，覆盖 Abstractions、Runtime.Timers、Actor.Timers、Network、Persistence.Hosting、Timers、ServiceDiscovery、Server.ServiceDiscovery 等模块。
新增 Ai_development_guide_routes_every_source_project 架构测试，要求每个 src 项目都在 AI 指南中出现。
```

边界控制：

```text
这是开发期文档和测试护栏，不新增运行时依赖。
不改变模块引用关系。
后续新增 src 项目时，AI 指南、模块矩阵和架构测试会一起约束文件落点。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
```

## 2026-06-08：根 AGENTS 入口强制指向 AI 硬规则文档

来源参考：

```text
ET10 根 AGENTS.md 的核心价值是提供最小入口，并把 AI 导向更完整的项目规则。
YuoCore 也需要保证根 AGENTS.md 不会在后续修改中丢掉任务路由、ET10 吸收规则或模块依赖矩阵入口。
```

YuoCore 落地：

```text
AGENTS.md
tests/YuoCore.Architecture.Tests/ProjectDependencyTests.cs
```

修复：

```text
新增 Root_agents_entry_points_to_required_ai_rule_documents 架构测试。
测试要求根 AGENTS.md 指向：
docs/ai-development-guide.md
docs/yuocore-modular-et10-extraction-rules.md
docs/module-dependency-matrix.md
```

边界控制：

```text
只新增开发期测试护栏。
不改变运行时代码和模块依赖。
```

验证：

```text
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj
```

## 2026-06-08：Analyzer 诊断 ID 必须进入 release notes

来源参考：

```text
ET10 的 AI 友好并不是只写规则，而是让规则变成开发流程中的固定检查。
YuoCore.Analyzers 已经有 AnalyzerReleases.Unshipped.md，但缺少测试确保新增诊断 ID 时同步记录。
```

问题：

```text
新增 YuoDiagnosticIds 常量后，开发者可能忘记更新 AnalyzerReleases.Shipped.md / AnalyzerReleases.Unshipped.md。
这会让诊断规则对 AI 和维护者不够可解释，也不利于后续发布。
```

YuoCore 落地：

```text
tests/YuoCore.Analyzers.Tests/YuoTaskUsageAnalyzerTests.cs
src/YuoCore.Analyzers/AnalyzerReleases.Unshipped.md
```

修复：

```text
新增 Analyzer_release_notes_cover_all_yuo_diagnostic_ids 测试。
测试反射 YuoDiagnosticIds 中所有字符串常量，并要求 shipped/unshipped release notes 至少一个文件包含对应 ID。
```

边界控制：

```text
只新增 Analyzer 测试。
不改变现有诊断行为。
不把 Analyzer 变成运行时依赖。
```

验证：

```text
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj
```
