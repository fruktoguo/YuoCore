# YuoCore NuGet 包拆分与版本策略

本文档记录 YuoCore 的 NuGet 拆包边界、版本策略和打包验证入口。目标是让单机、Unity、Web 客户端和 MMO 服务端都能按需导入，而不是被迫携带 ASP.NET Core、数据库、Redis、WebSocket 或分布式网络实现。

## 基本规则

```text
包名等于项目名，例如 YuoCore.Ecs。
所有 src 下的可打包库继承 src/Directory.Build.props 的公共 NuGet 元数据。
默认版本前缀为 0.1.0。
包输出目录为 artifacts/packages。
同时生成 nupkg 和 snupkg。
README.md 会作为 PackageReadmeFile 进入包。
```

`YuoCore.Backend` 是 ASP.NET Core 示例宿主，不作为 NuGet 库发布。它可以引用各种可选服务端模块和基础设施包，但不能被核心模块引用。

## 包分层

### 最小核心包

这些包可以用于单机、Unity 本地逻辑或服务端核心逻辑，不应携带服务端宿主依赖。

```text
YuoCore.Abstractions
YuoCore.Ecs
YuoCore.Tasks
YuoCore.Timers
```

### 同机多线程与 Actor 包

这些包负责 Fiber、同进程 Actor、Location 缓存、迁移和重试。它们可以用于服务端，也可以在 Unity/单机中按需导入。

```text
YuoCore.Runtime
YuoCore.Actor
YuoCore.Location
YuoCore.Runtime.Actor
YuoCore.Actor.Ecs
YuoCore.Actor.Timers
YuoCore.Runtime.Timers
```

### 协议、客户端与网关包

这些包用于 Web/Unity 客户端协议、服务端 Gateway 边界和可选网络适配。

```text
YuoCore.Protocol
YuoCore.Client
YuoCore.Client.StateSync
YuoCore.Gateway
YuoCore.Network
```

`YuoCore.Client` 只提供 requestId、clientSeq、opcode、envelope 编解码和 transport 抽象，不直接实现 WebSocket/TCP/KCP，也不引用 Gateway、Backend、Actor、Runtime 或 ECS。

`YuoCore.Client.StateSync` 是可选客户端同步桥接包，用于把服务端 StateSnapshot/StateDelta 推送应用到本地 Mirror；它依赖 `YuoCore.Client` 和 `YuoCore.StateSync.Protocol`，但不依赖 ECS、Gateway 或服务端模块。

### 游戏域与状态同步包

这些包提供可复用的 MMO/挂机示例游戏域、StateSync、Snapshot、Delta 和 AOI 基础能力。

```text
YuoCore.Game
YuoCore.StateSync.Abstractions
YuoCore.StateSync
YuoCore.StateSync.Protocol
```

`YuoCore.Game` 只能承载纯游戏域类型和内部 Actor 消息，不能引用 ASP.NET Core、数据库、Redis、WebSocket 或 Backend。

`YuoCore.StateSync.Abstractions` 承载 Snapshot、Delta、Mirror、AOI 和可见性差分纯模型，面向 Web/Unity 客户端和服务端共用，不依赖 ECS 或 Protocol。

`YuoCore.StateSync` 只承载 ECS 组件和 Entity 适配器，服务端或 Unity 本地 ECS 需要时再导入。

`YuoCore.StateSync.Protocol` 是可选桥接包，用于 Protobuf DTO 与 StateSync 纯数据模型互转；它依赖 `YuoCore.StateSync.Abstractions` 和 `YuoCore.Protocol`，基础 `YuoCore.StateSync` 不能反向引用 Protocol。

### 持久化、配置、发现和服务端装配包

这些包是服务端可选能力。使用者可以按场景选择，不应该污染单机和 Unity 最小路径。

```text
YuoCore.Persistence
YuoCore.Persistence.Hosting
YuoCore.Config
YuoCore.ServiceDiscovery
YuoCore.Server.ServiceDiscovery
YuoCore.Server
```

`YuoCore.Persistence.Hosting` 是 `BackgroundService` 适配包；基础 `YuoCore.Persistence` 只保留抽象、命令和队列。

### 开发期包

```text
YuoCore.Analyzers
```

Analyzer 只应作为开发期依赖引入，用于守住异步用法和架构边界。运行时包不能依赖它的输出程序集。

## 版本策略

当前处于 `0.x` 阶段：

```text
0.1.x：架构拆分、核心 API 和示例闭环仍在快速迭代。
0.2.x：协议、Actor、Location、StateSync 和 Persistence 的公共 API 进入较稳定阶段。
1.0.0：完成长期压测、Unity/Web SDK 验证、持久化恢复策略和分布式可选实现边界后再发布。
```

破坏性变更规则：

```text
0.x 阶段允许破坏性变更，但必须写入 docs/et10-extraction-log.md 或 docs/risk-and-decisions.md。
公共协议 opcode、Protobuf 字段编号、Location 迁移语义、Actor request/response/error 契约一旦发布，应尽量保持向后兼容。
如果必须破坏兼容，必须同时更新协议文档、测试和迁移说明。
```

## 打包验证

常规验证：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

包 smoke：

```bash
scripts/run-pack-smoke.sh
YUO_CONSUMER_PACKAGE_DIR=artifacts/packages/pack-smoke-<timestamp> scripts/run-pack-consumer-smoke.sh
dotnet pack src/YuoCore.Ecs/YuoCore.Ecs.csproj -c Release --disable-build-servers
dotnet pack src/YuoCore.Client/YuoCore.Client.csproj -c Release --disable-build-servers
dotnet pack src/YuoCore.Persistence.Hosting/YuoCore.Persistence.Hosting.csproj -c Release --disable-build-servers
```

`scripts/run-pack-smoke.sh` 默认打包当前所有应发布的 `src` 库，排除 `YuoCore.Backend` 示例宿主：

```text
Abstractions / Tasks / Timers / Ecs
Actor / Actor.Ecs / Actor.Timers
Runtime / Runtime.Actor / Runtime.Timers
Location
Protocol / Client / Client.StateSync / Gateway / Network
Game
StateSync.Abstractions / StateSync / StateSync.Protocol
Persistence / Persistence.Hosting
Config
ServiceDiscovery / Server / Server.ServiceDiscovery
Analyzers
```

脚本还会扫描生成的 nuspec，禁止这些库误带以下宿主级依赖：

```text
YuoCore.Backend
Microsoft.AspNetCore
Npgsql
StackExchange.Redis
Serilog
Dapper
OpenTelemetry
```

这组 smoke 的目的不是只证明几个代表性包可用，而是提前暴露任意可选模块发布时的依赖泄漏、README 打包缺失、符号包生成失败或 Analyzer 打包问题。

如果未来新增 `src/YuoCore.*` 可发布项目，必须同步加入 `scripts/run-pack-smoke.sh`。如果某个项目只是示例宿主或工具，应显式设置：

```text
<IsPackable>false</IsPackable>
```

`YuoCore.Backend` 不应被 pack；它是示例宿主和集成测试目标。

## Consumer Smoke

`scripts/run-pack-consumer-smoke.sh` 会创建全新的临时 consumer 项目，并通过 NuGet 安装本地包，而不是使用源码 `ProjectReference`。它覆盖三个导入画像：

```text
YuoCoreConsumer.CoreNet10
  TFM: net10.0
  包：YuoCore.Ecs / YuoCore.Runtime / YuoCore.Actor
  目标：证明最小核心和同机多线程/Actor 基础包可以作为普通 .NET 10 库导入。

YuoCoreConsumer.UnityClientNetStandard
  TFM: netstandard2.1
  包：YuoCore.Client.StateSync
  目标：证明 Unity 风格客户端可以只导入协议客户端、YuoMmoClientConnection 和 StateSync Mirror，不携带 ECS、Gateway、Actor、Runtime 或 Backend。

YuoCoreConsumer.ServerNet10
  TFM: net10.0
  包：YuoCore.Server / YuoCore.Persistence.Hosting / YuoCore.Gateway / YuoCore.Network / YuoCore.Game
  目标：证明服务端可选组合可以用包方式导入，且宿主依赖仍停留在 Backend 示例之外。
```

默认情况下，consumer smoke 会先调用 `scripts/run-pack-smoke.sh` 生成本地包；如果已经有包目录，可以指定：

```bash
YUO_CONSUMER_PACKAGE_DIR=artifacts/packages/pack-smoke-20260608T210734Z scripts/run-pack-consumer-smoke.sh
```

为了避免相同 `0.1.0` 版本的旧本地包从全局 NuGet cache 命中，consumer smoke 默认会把 `NUGET_PACKAGES` 指向本次输出目录下的隔离目录：

```text
artifacts/packages/consumer-smoke-<timestamp>/.nuget-packages
```

如需覆盖，可设置：

```bash
YUO_CONSUMER_NUGET_PACKAGES=/path/to/isolated-cache scripts/run-pack-consumer-smoke.sh
```

最近一次验证：

```text
scripts/run-pack-smoke.sh
packages=27
结果：通过，未发现禁止依赖模式。
输出：artifacts/packages/pack-smoke-20260608T210734Z
补充：本次 smoke 已覆盖全部应发布库，不再只覆盖代表性包。

YUO_CONSUMER_PACKAGE_DIR=artifacts/packages/pack-smoke-20260608T210734Z scripts/run-pack-consumer-smoke.sh
结果：通过，三个临时 consumer 项目均可 restore/build。
输出：artifacts/packages/consumer-smoke-20260608T211712Z

scripts/run-pack-consumer-smoke.sh
结果：通过，三个临时 consumer 项目均可 restore/build；Unity 风格 consumer 已覆盖 YuoMmoClientConnection。
输出：artifacts/packages/consumer-smoke-20260608T221843Z

scripts/run-pack-smoke.sh
结果：通过，packages=27，未发现 Backend、ASP.NET Core、Redis、Npgsql、Dapper、Serilog、OpenTelemetry 等禁止依赖模式。
输出：artifacts/packages/pack-smoke-20260608T231615Z

scripts/run-pack-consumer-smoke.sh
结果：通过，packages=27，三个临时 consumer 项目均可 restore/build；使用本次输出目录下的隔离 NuGet cache。
输出：artifacts/packages/consumer-smoke-20260608T231923Z

scripts/run-pack-smoke.sh
结果：通过，packages=27，包含 YuoCore.Gateway 的 readonly record struct 出站消息形状；禁止依赖扫描通过。
输出：artifacts/packages/pack-smoke-20260608T234210Z

scripts/run-pack-consumer-smoke.sh
结果：通过，packages=27，CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build；ServerNet10 已覆盖 YuoCore.Gateway 包安装。
输出：artifacts/packages/consumer-smoke-20260608T234450Z

scripts/run-pack-consumer-smoke.sh
结果：通过，packages=27，CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build；本轮覆盖 YuoCore.Game 的 readonly record struct dirty DTO 包形状。
输出：artifacts/packages/consumer-smoke-20260609T000619Z

scripts/run-pack-consumer-smoke.sh
结果：通过，packages=27，CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build；本轮覆盖 YuoCore.StateSync.Abstractions 的订阅 no-change 快路径在 netstandard2.1 和 net10.0 包中的消费编译。
输出：artifacts/packages/consumer-smoke-20260609T001841Z

scripts/run-pack-consumer-smoke.sh
结果：通过，packages=27，CoreNet10 / UnityClientNetStandard / ServerNet10 三个临时 consumer 均可 restore/build；本轮覆盖 YuoCore.Actor 的 YuoActorLocationSenderOptions 迁移锁缓存校验选项包形状。
输出：artifacts/packages/consumer-smoke-20260609T012008Z

YUO_CONSUMER_PACKAGE_DIR=artifacts/packages/consumer-smoke-20260609T012008Z/packages scripts/run-pack-consumer-smoke.sh
结果：通过，CoreNet10 consumer probe 已直接编译 YuoActorLocationSenderOptions(ValidateCachedAddressOnUse: true)，并读取 ValidateCachedAddressOnUse 属性；UnityClientNetStandard / ServerNet10 consumer 仍可 restore/build。
输出：artifacts/packages/consumer-smoke-20260609T012834Z
```

## Web TypeScript SDK npm Smoke

Web 客户端 SDK 不走 NuGet，而是作为独立 npm 包验证：

```text
clients/web/yuocore-client-ts
  package name: @yuocore/client
  module: ESM
  exports: ./dist/index.js + ./dist/index.d.ts
```

专项命令：

```bash
npm --prefix clients/web/yuocore-client-ts run smoke
npm --prefix clients/web/yuocore-client-ts run browser-smoke
scripts/run-web-sdk-pack-smoke.sh
scripts/run-web-backend-browser-smoke.sh
```

`scripts/run-web-sdk-pack-smoke.sh` 会执行：

```text
1. 构建 Web TypeScript SDK；
2. 在 SDK 包目录执行 npm pack；
3. 创建全新临时 TypeScript consumer 项目；
4. 通过 file: tarball 安装 @yuocore/client；
5. 执行 TypeScript strict typecheck；
6. 执行 Node ESM runtime import 检查。
```

最近一次验证：

```text
npm --prefix clients/web/yuocore-client-ts run smoke
结果：通过，yuocore-client-ts smoke passed。

npm --prefix clients/web/yuocore-client-ts run browser-smoke
结果：通过，Chrome/Chromium headless 可通过浏览器 ESM 导入 dist/index.js。

scripts/run-web-sdk-pack-smoke.sh
结果：通过，临时 consumer 可安装 tarball、导入 YuoMmoWebSocketSession、完成 typecheck，并通过 runtime ESM import。
输出：artifacts/packages/web-sdk-pack-smoke-20260608T220954Z
tarball：yuocore-client-0.1.0.tgz

scripts/run-web-backend-browser-smoke.sh
结果：通过，Chrome/Chromium headless 中的 Web SDK 可连接真实 Backend /ws，并完成 login -> enter map -> move -> state snapshot -> disconnect -> reconnect -> get snapshot。
```

该 smoke 只证明当前手写 Web SDK 包可发布、可安装、可导入，不代表协议 codegen 已定型。TypeScript protobuf 生成方案仍按 `docs/risk-and-decisions.md` 记录等待后续裁定。

## Client SDK Smoke

如果任务涉及 Web/Unity 客户端 SDK、公用客户端协议、StateSync 客户端 Mirror 或客户端包边界，优先运行聚合门禁：

```bash
scripts/run-client-sdk-smoke.sh
```

该脚本覆盖：

```text
YuoCore.Client.Tests
YuoCore.Client.StateSync.Tests
clients/unity/YuoCore.UnityClient build
clients/web/yuocore-client-ts smoke
clients/web/yuocore-client-ts browser-smoke
scripts/run-web-sdk-pack-smoke.sh
```

最近一次验证：

```text
scripts/run-client-sdk-smoke.sh
结果：通过。
YuoCore.Client.Tests: Passed 9
YuoCore.Client.StateSync.Tests: Passed 15
Unity adapter build: succeeded
Web SDK smoke: passed
Web SDK browser-smoke: passed
Web SDK npm tarball consumer smoke: passed
输出：artifacts/packages/web-sdk-pack-smoke-20260608T232634Z
tarball：yuocore-client-0.1.0.tgz
```

## 最近 NuGet Consumer Smoke

```text
scripts/run-pack-consumer-smoke.sh
结果：通过。
输出：artifacts/packages/consumer-smoke-20260609T023551Z
```

本次 smoke 重新打包并创建全新临时 consumer，覆盖：

```text
CoreNet10
  -> 安装核心组合包并编译。

UnityClientNetStandard
  -> 安装 YuoCore.Client.StateSync 等客户端包并编译 netstandard2.1。

ServerNet10
  -> 安装 YuoCore.Server、YuoCore.Persistence.Hosting、YuoCore.Gateway、YuoCore.Network、YuoCore.Game 并编译 net10.0。
```

ServerNet10 consumer 额外直接编译以下 Persistence 停服公共 API：

```text
new YuoPersistenceWriterOptions(CompleteQueueOnStop: false)
new YuoPersistenceQueue(capacity: 4)
YuoPersistenceQueue.TryComplete()
```

这证明当前 public API 已进入 NuGet 包面，并可被外部服务端项目直接引用。该 smoke 仍只证明安装和编译路径，不等价于正式发布验收；正式发布还需要签名、许可证元数据、逐包 consumer profile 和版本升级兼容测试。
