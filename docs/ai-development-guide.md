# YuoCore AI 开发指南

本文档是给 AI、脚手架和后来维护者使用的任务入口。目标是减少“文件放错包”“依赖倒灌”“只写代码不验证”这类高频错误。

## 一、先判断任务类型

| 任务 | 优先落点 | 必看文档 |
| --- | --- | --- |
| Result、Error、Clock 等基础契约 | `src/YuoCore.Abstractions` | `docs/module-dependency-matrix.md` |
| ECS 组件、System、生命周期 | `src/YuoCore.Ecs` | `docs/module-dependency-matrix.md` |
| Fiber、tick、多 World 调度 | `src/YuoCore.Runtime` | `docs/yuocore-et-essence-final-design.md` |
| Fiber mailbox 接入 Actor endpoint | `src/YuoCore.Runtime.Actor` | `docs/module-dependency-matrix.md` |
| Fiber 与 TimerScheduler 桥接 | `src/YuoCore.Runtime.Timers` | `docs/risk-and-decisions.md` |
| 纯 Actor、Router、Transport | `src/YuoCore.Actor` | `docs/risk-and-decisions.md` |
| Entity Actor handler/dispatcher/handler 扫描 | `src/YuoCore.Actor.Ecs` | `docs/import-profiles.md` |
| Actor 重试等待接入 TimerScheduler | `src/YuoCore.Actor.Timers` | `docs/risk-and-decisions.md` |
| Location、地址缓存、迁移锁 | `src/YuoCore.Location` | `docs/risk-and-decisions.md` |
| 可选传输、序列化边界、进程选择 | `src/YuoCore.Network` | `docs/risk-and-decisions.md` |
| 外部协议、Protobuf、opcode | `src/YuoCore.Protocol` | `docs/web-unity-mmo-architecture.md` |
| Web/Unity 客户端协议 SDK | `src/YuoCore.Client` | `docs/import-profiles.md` |
| C# 客户端 StateSync 推送处理 | `src/YuoCore.Client.StateSync` | `docs/import-profiles.md` |
| Unity WebSocket 客户端适配 | `clients/unity/YuoCore.UnityClient` | `docs/import-profiles.md` |
| Web TypeScript 客户端 SDK | `clients/web/yuocore-client-ts` | `docs/import-profiles.md` |
| Web/Unity 客户端接入边界 | `src/YuoCore.Gateway` | `docs/web-unity-mmo-architecture.md` |
| MMO 领域对象和内部消息 | `src/YuoCore.Game` | `docs/web-unity-mmo-architecture.md` |
| Snapshot、Delta、Mirror、纯 AOI | `src/YuoCore.StateSync.Abstractions` | `docs/module-dependency-matrix.md` |
| StateSync ECS 组件和 Entity 适配 | `src/YuoCore.StateSync` | `docs/risk-and-decisions.md` |
| StateSync Protobuf 转换 | `src/YuoCore.StateSync.Protocol` | `docs/module-dependency-matrix.md` |
| 保存队列、快照/事件抽象 | `src/YuoCore.Persistence` | `docs/risk-and-decisions.md` |
| Persistence 后台写回 Hosting 适配 | `src/YuoCore.Persistence.Hosting` | `docs/risk-and-decisions.md` |
| 手动驱动计时器 | `src/YuoCore.Timers` | `docs/risk-and-decisions.md` |
| StartConfig、配置契约 | `src/YuoCore.Config` | `schemas/` |
| 服务发现接口和内存实现 | `src/YuoCore.ServiceDiscovery` | `docs/risk-and-decisions.md` |
| StartConfig 到 ServiceDiscovery 的可选适配 | `src/YuoCore.Server.ServiceDiscovery` | `docs/risk-and-decisions.md` |
| 服务端装配 | `src/YuoCore.Server` | `docs/import-profiles.md` |
| ASP.NET Core 示例宿主 | `src/YuoCore.Backend` | `docs/web-unity-mmo-architecture.md` |
| 高频内部异步原语 YuoTask | `src/YuoCore.Tasks` | `docs/risk-and-decisions.md` |
| Analyzer / 开发期护栏 | `src/YuoCore.Analyzers` | `docs/yuocore-modular-et10-extraction-rules.md` |
| 项目依赖边界护栏 | `tests/YuoCore.Architecture.Tests` | `docs/module-dependency-matrix.md` |
| NuGet 拆包、版本、pack smoke | `src/Directory.Build.props`、各 `*.csproj` | `docs/nuget-packaging.md` |
| 压测 / Benchmark | `benchmarks/`、`tools/` | `docs/implementation-and-performance-notes.md` |

## 二、依赖边界

核心原则：

```text
Ecs 不依赖 Runtime / Actor / Gateway / Network / Protocol / Persistence / Backend。
Actor.Core 不依赖 Ecs；Entity Actor 能力放在 Actor.Ecs。
Gateway 不直接改 ECS 组件。
Backend 是示例宿主，不被任何核心模块引用。
Analyzer 是开发期包，不被运行时模块引用。
```

允许的典型方向：

```text
Abstractions <- 多数模块
Ecs <- Runtime / StateSync / Game / Actor.Ecs / Server
Actor <- Actor.Ecs / Runtime.Actor / Game / Server / Backend
Protocol <- Backend / Network / LoadTests
Gateway <- Backend
Persistence <- Backend 或 Persistence.Hosting 适配层
```

如果需要跨层访问，优先新增抽象或适配包，不要直接反向引用。

## 三、常见错误

- 在 `YuoCore.Ecs` 里引用 ASP.NET Core、WebSocket、数据库、Redis、Protobuf。
- 在基础 `YuoCore.Runtime` 里引用 Actor/Location；Fiber 到 Actor endpoint 的桥接应放在 `YuoCore.Runtime.Actor`。
- 在 Gateway handler 里直接拿 `YuoEntity` / `YuoComponent` 改组件；`YUO0006` 会拦截 `IYuoGatewayDispatcher` 对 ECS 类型的直接引用。
- 在 Protocol/Contracts DTO 里暴露 `ActorId`、`YuoEntity`、`YuoLocationKey`、`YuoFiber`、`YuoGatewaySession` 等运行时对象；`YUO0007` 会拦截这类协议契约泄漏，外部协议应只携带纯业务数据。
- 在 `YuoCore.Client` 里直接实现 WebSocket/TCP/KCP 或引用 `YuoCore.Gateway` / `YuoCore.Backend`；客户端 SDK 只保留协议会话和 transport 抽象，具体网络实现放 Unity/Web 适配层。
- 在 `YuoCore.Client.StateSync` 里引用 ECS、Gateway、Actor、Runtime 或 Backend；它只负责 StateSync 推送 envelope 到 Mirror 的客户端桥接。
- 在 `clients/unity/YuoCore.UnityClient` 里引用服务端 Actor/ECS/Gateway/Backend；Unity transport 只负责字节级 request/response。
- 在公共 API 上默认返回 `YuoTask`，导致 Web/ASP.NET/SDK 边界不符合 .NET 生态预期。
- 在基础 `YuoCore.Persistence` 里直接引用 `Microsoft.Extensions.Hosting`，应放到 `YuoCore.Persistence.Hosting`。
- 只引用 `YuoCore.Actor` 却使用 `YuoEntityActorDispatcher`，应增加 `YuoCore.Actor.Ecs` 引用。
- 新增 Analyzer 后忘记 `AnalyzerReleases.Unshipped.md`。
- 新增 Protobuf 消息后忘记同步 `YuoCoreProtocolCatalog` / opcode 常量，导致 Backend、Unity、Web 使用不同编号。
- 在 `YuoCore.StateSync.Abstractions` 里引用 ECS、Protocol、Gateway 或网络；它必须保持 Web/Unity/服务端共享纯模型。
- 在 `YuoCore.StateSync` 里直接引用 `YuoCore.Protocol`；协议转换应放到 `YuoCore.StateSync.Protocol`。
- 新增模块或项目引用后忘记更新 `YuoCore.sln`、依赖矩阵、导入组合、提取日志和 `YuoCore.Architecture.Tests`。
- 修改 StartConfig DTO 或运行时校验后忘记同步 `schemas/startconfig.schema.json` 和 Config 契约测试。
- 性能优化改掉 System try/catch 异常隔离语义。

## 四、验证命令

常规验证：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

一键本地压测套件：

```bash
scripts/run-local-load-suite.sh --quick
scripts/run-local-load-suite.sh --soak
scripts/run-load-trend.sh --quick
```

说明：

```text
--quick 是本地回归门禁，默认覆盖 1w 玩家/实体、local-composite 和 100 组件/100 system 的 ecs-shape。
--soak 使用 duration-mode，并输出 CSV，适合观察长时间抖动、GC 和分段热点。
run-load-trend.sh 会重复执行 quick/soak 多轮并生成 trend-summary.md，适合判断短样本 p95 是否只是抖动。
产物写入 artifacts/load/<timestamp>/，该目录已在 .gitignore 中忽略。
每次 suite 结束会生成 summary.md，汇总 tick p95/max、分配量和每个场景的主要热点。
trend 产物写入 artifacts/load/trend-<timestamp>-<mode>/，其中 runs.txt 记录每轮目录。
可通过 YUO_LOAD_PLAYERS、YUO_LOAD_STATE_ENTITIES、YUO_LOAD_SHARDS 等环境变量缩放规模。
可通过 YUO_LOAD_PAYLOAD_PROFILE=allocated|shared-input 对照 payload 构造分配。
可通过 YUO_LOAD_PERSISTENCE_ENQUEUE=single|batch 对照 Persistence 队列入队模式。
可通过 YUO_LOAD_TREND_RUNS 控制 trend 轮数，默认 3。
```

NuGet pack smoke：

```bash
scripts/run-pack-smoke.sh
```

说明：

```text
该脚本会打包全部应发布库，并扫描 nuspec，防止核心/客户端/可选服务端库误带 Backend、ASP.NET、Npgsql、Redis、Serilog、Dapper、OpenTelemetry。
产物写入 artifacts/packages/pack-smoke-<timestamp>/，该目录已在 .gitignore 中忽略。
```

NuGet consumer smoke：

```bash
YUO_CONSUMER_PACKAGE_DIR=artifacts/packages/pack-smoke-<timestamp> scripts/run-pack-consumer-smoke.sh
```

说明：

```text
该脚本创建全新临时 consumer 项目，通过 NuGet 安装本地包并编译。
当前覆盖 net10 核心、netstandard2.1 Unity 风格客户端、net10 服务端可选组合三个画像。
```

Web TypeScript 客户端 SDK 专项验证：

```bash
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
npm --prefix clients/web/yuocore-client-ts run browser-smoke
scripts/run-web-sdk-pack-smoke.sh
scripts/run-web-backend-browser-smoke.sh
```

说明：

```text
run smoke 验证 SDK 自身 build 和运行时最小行为。
browser-smoke 启动 Chrome/Chromium headless，通过 HTTP 页面验证浏览器 ESM 导入。
run-web-sdk-pack-smoke.sh 会执行 npm pack，并创建全新临时 TypeScript consumer 项目安装 tarball，验证包的 exports/types/runtime ESM 导入。
run-web-backend-browser-smoke.sh 会启动真实 Backend，并在 Chrome headless 中用 Web SDK 连接 /ws 跑登录、进图、移动、状态快照、断线和同 playerId 手动重连恢复闭环。
```

客户端 SDK 聚合验证：

```bash
scripts/run-client-sdk-smoke.sh
```

说明：

```text
该脚本聚合 C# Client tests、Client.StateSync tests、Unity netstandard2.1 adapter build、Web SDK smoke、Web SDK browser-smoke 和 Web SDK npm tarball consumer smoke。
它不启动 Backend，只验证客户端包和适配层的可导入、可构建、可运行最小边界。
```

Unity 客户端适配专项验证：

```bash
dotnet build clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj --disable-build-servers
```

快速多 shard ECS shape 压测：

```bash
dotnet build benchmarks/YuoCore.Benchmarks/YuoCore.Benchmarks.csproj -c Release --disable-build-servers
dotnet run -c Release --no-build --project benchmarks/YuoCore.Benchmarks -- --perf-smoke --entities 10000 --ticks 20 --shards 4
```

本地综合 MMO 短压：

```bash
scripts/run-local-load-suite.sh --quick
scripts/run-load-trend.sh --quick
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 20 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode sorted --json-output artifacts/load/local-composite-sorted.json
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --ticks 20 --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --json-output artifacts/load/local-composite-count.json --fail-p95-ms 250 --fail-max-ms 500
```

本地综合 MMO soak：

```bash
scripts/run-local-load-suite.sh --soak
dotnet run -c Release --project tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj -- --mode local-composite --players 10000 --duration 120 --duration-mode --shards 4 --state-entities 10000 --aoi-observers 256 --persistence-per-tick 10000 --aoi-result-mode count --json-output artifacts/load/local-composite-soak.json --csv-output artifacts/load/local-composite-soak.csv --fail-p95-ms 250 --fail-max-ms 500
```

YuoTask 专项验证：

```bash
dotnet test tests/YuoCore.Tasks.Tests/YuoCore.Tasks.Tests.csproj --no-build
dotnet test tests/YuoCore.Analyzers.Tests/YuoCore.Analyzers.Tests.csproj --no-build
dotnet run -c Release --project tools/YuoCore.TaskPerf/YuoCore.TaskPerf.csproj
```

项目依赖边界专项验证：

```bash
dotnet test tests/YuoCore.Architecture.Tests/YuoCore.Architecture.Tests.csproj --no-build
```

StartConfig 契约专项验证：

```bash
dotnet test tests/YuoCore.Config.Tests/YuoCore.Config.Tests.csproj --no-build
```

## 五、提交规则

每完成一个完整能力：

1. 更新对应测试。
2. 更新 `docs/et10-extraction-log.md`。
3. 必要时更新 `docs/module-dependency-matrix.md`、`docs/import-profiles.md`、`docs/risk-and-decisions.md`。
4. 运行构建和测试。
5. 本地提交。
6. 确认 `git status --short` 无未提交项。

不要推送，除非用户明确要求。
