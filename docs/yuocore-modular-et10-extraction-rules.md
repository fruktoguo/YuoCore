# YuoCore 可裁剪运行时与 ET10 吸收规则

本文档是后续持续演进 YuoCore 的硬性约束。任何分析 ET10、迁移能力、重构模块、优化性能或新增项目，都必须先遵守这里的分层和依赖规则。

## 一、总体目标

YuoCore 的目标是成为可裁剪的通用游戏运行时，而不是单纯 MMO 后端框架。

它必须同时兼顾：

- 单机 C# 游戏
- Unity 客户端
- Web 客户端协议/SDK
- 同机器多线程服务端
- 单进程 MMO 后端
- 可选分布式 MMO 后端

核心要求：

```text
单机和 Unity 可以只导入少量核心包。
MMO 后端可以组合 Gateway、StateSync、Persistence、ServerHost 等扩展包。
分布式网络、服务发现、数据库、ASP.NET Core 都必须是可选能力，不能污染核心。
```

## 二、模块分层

### Core 必选层

这些模块应该尽量轻量、平台无关、可嵌入。

| 模块 | 职责 | 禁止依赖 |
| --- | --- | --- |
| `YuoCore.Abstractions` | Error、Result、Clock、基础契约 | ECS、Actor、Network、数据库、ASP.NET Core |
| `YuoCore.Tasks` | 轻量 awaitable、池化 completion source、fire-and-forget 异常出口 | ECS、Actor、Network、数据库、ASP.NET Core |
| `YuoCore.Ecs` | World、Entity、Component、System、生命周期 | Runtime、Actor、Gateway、Network、Protocol、Persistence、Backend |

`YuoCore.Ecs` 必须保持单机可用。现有 `YuoWorld.Instance`、直接 `YuoWorld.Update()`、Unity 内直接使用 ECS 的路径不能被破坏。

### Runtime 可选层

| 模块 | 职责 | 要求 |
| --- | --- | --- |
| `YuoCore.Runtime` | Fiber、同进程多 World、多线程分片、Tick 调度 | 可依赖 ECS，但不能依赖 Actor、远程网络、Gateway、数据库 |
| `YuoCore.Runtime.Actor` | 把 Actor endpoint 接到 Fiber mailbox 的可选适配 | 可依赖 Runtime + Actor，但不能反向污染 Runtime |
| `YuoCore.Actor` / `Actor.Core` | 同进程 Actor、mailbox、Send/Call、Transport、LocationSender、Timeout | 只负责本机内部通讯，不强制引入 ECS 或跨机器网络 |
| `YuoCore.Actor.Ecs` | ActorId 到 YuoEntity 的本机映射、Entity handler/dispatcher | 可依赖 Actor + Ecs，但不能反向污染 Actor.Core |
| `YuoCore.Location` | 业务 ID 到运行时地址、缓存、失效、迁移锁 | 内存实现默认，分布式实现可选 |

Actor 的核心能力应该保留同进程/同机器多 Fiber 通讯。跨进程、跨机器、服务发现、远程传输必须作为可选扩展。

### Client 共享层

| 模块 | 职责 | 要求 |
| --- | --- | --- |
| `YuoCore.Protocol` | Protobuf、opcode、错误码、外部协议、内部 envelope | Unity/Web/服务端可共享；不能依赖 Backend |
| `YuoCore.StateSync` | Snapshot、Delta、NetworkIdentity、AOI 接口 | 客户端应用子集应可独立使用 |

Unity 可以引用客户端安全子集，但不能被迫引用 ASP.NET Core、数据库、Redis、Docker、服务端宿主。

### Server 可选层

| 模块 | 职责 | 要求 |
| --- | --- | --- |
| `YuoCore.Gateway` | Session、连接抽象、鉴权入口、协议转内部消息 | 不直接修改 ECS 组件 |
| `YuoCore.Game` | Player、Map、Unit、Combat、Inventory 等领域层 | 可选；不能依赖 Backend |
| `YuoCore.Persistence` | Snapshot/Event store 抽象、写回队列 | 数据库实现拆成可选包 |
| `YuoCore.Server` | ServerHost、StartConfig、服务装配 | 服务端专用 |
| `YuoCore.Backend` | ASP.NET Core 示例宿主、HTTP、WebSocket、OpenAPI、监控 | 示例/宿主，不是核心 |

### Distributed 可选层

这些能力只在分布式服务器需要时启用：

- 远程 `NetworkTransport`
- 跨机器 Actor
- 分布式 Location
- ServiceDiscovery
- 远程 StartConfig/服务注册
- Redis/PostgreSQL Location 实现

它们不能成为 `Ecs`、`Runtime`、`Actor.Core` 的必需依赖。

### AI 开发友好横切层

ET10 对 AI 友好的关键不是减少规则，而是把规则变成可读取、可路由、可生成、可校验的工程护栏。YuoCore 后续必须吸收这部分思想，但不能让 AI 工具链污染运行时核心。

| 能力 | 建议模块/位置 | 要求 |
| --- | --- | --- |
| AI 任务路由文档 | `AGENTS.md`、`docs/ai-development-guide.md` 或等价入口 | 描述任务分类、文件落点、验证命令、禁止事项 |
| 专项开发规则 | `docs/` 或 `.narrafork/skills` | 按 code、actor、gateway、protocol、statesync、test、build 分流 |
| Roslyn Analyzer | `YuoCore.Analyzers` | 编译期阻止架构违规；不被业务运行时依赖 |
| Source Generator | `YuoCore.Generators` | 生成 Actor handler、System、Protocol、Component 绑定等样板 |
| 模板/脚手架 | `tools/` 或可选 CLI | 生成规范文件结构，降低 AI 放错位置概率 |
| 自动验证入口 | solution scripts / CI | 固定 build、test、protocol generation、Unity/Web SDK generation 流程 |

AI 友好能力是开发期能力，默认不能成为 `YuoCore.Ecs`、`YuoCore.Runtime`、`YuoCore.Actor`、`YuoCore.Protocol` 的运行时依赖。

## 三、推荐导入组合

### 单机最小组合

```text
YuoCore.Abstractions
可选：YuoCore.Tasks
YuoCore.Ecs
```

### Unity 本地 ECS

```text
YuoCore.Abstractions
YuoCore.Ecs
可选：YuoCore.Runtime
```

### Unity 联机客户端

```text
YuoCore.Abstractions
YuoCore.Protocol
YuoCore.StateSync 客户端子集
可选：YuoCore.Ecs
```

### Web 客户端

```text
Protobuf/TypeScript 生成代码
协议文档
状态同步格式
```

Web 不直接引用服务端 Actor、Fiber、Location。

### 单进程 MMO 服务端

```text
YuoCore.Abstractions
YuoCore.Tasks
YuoCore.Ecs
YuoCore.Runtime
YuoCore.Runtime.Actor
YuoCore.Actor
YuoCore.Actor.Ecs
YuoCore.Location
YuoCore.Protocol
YuoCore.Gateway
YuoCore.Game
YuoCore.StateSync
YuoCore.Persistence
YuoCore.Server
YuoCore.Backend
```

### 分布式 MMO 服务端

在单进程 MMO 组合上额外加入：

```text
远程 NetworkTransport
ServiceDiscovery
分布式 Location 实现
跨进程 Actor transport
```

## 四、ET10 吸收规则

分析 ET10 时，不照搬实现，只吸收可通用化思想。

每个 ET10 能力必须先分类：

```text
Core 通用能力
单机/Unity 可用能力
同进程服务端能力
MMO 服务端可选能力
分布式服务器可选能力
业务示例能力
不适合迁移能力
```

优先吸收运行时能力：

- Fiber 执行所有权
- Actor 消息模型
- ActorLocationSender / MessageLocationSender 思想
- Location 缓存、失效、迁移锁、重试
- EntitySystem 与 MessageHandler 分离
- StartConfig / 进程拓扑
- ServiceDiscovery 思想
- StateSync / AOI / Delta
- 异步保存、后台队列、批处理
- Fiber / mailbox / Actor 调用监控指标

优先吸收 AI 开发友好能力：

- `AGENTS.md` / skill index 式任务路由，让 AI 先按任务选择规则，而不是全量读文档。
- 分层开发规则：code、async、actor、gateway、protocol、statesync、persistence、build、test、git 各有独立入口。
- Roslyn Analyzer 护栏：禁止跨层依赖、禁止 Gateway 直接改 ECS、禁止协议 DTO 引用 Entity、禁止运行时静态可变状态、限制 Actor/Location 错误用法。
- Source Generator 护栏：自动生成 handler 注册、system 注册、协议 opcode 映射、组件/子实体合法挂载检查，减少手写样板。
- 固定脚手架：新增模块、消息、Actor handler、Gateway handler、StateSync 消息时由模板生成标准文件结构。
- 固定验证闭环：每类任务明确 build/test/codegen 命令，AI 修改后能自检并根据错误回修。
- 错误可解释性：Analyzer 诊断必须给出规则原因和推荐修复方向，而不是只报低层编译错误。

迁移前必须检查：

- 是否污染 `YuoCore.Ecs`
- 是否让 Unity 被迫引用服务端包
- 是否让单机项目被迫引用网络包
- 是否把分布式网络变成核心依赖
- 是否破坏现有 `YuoWorld` 直接使用方式
- 是否改变 System 异常隔离语义
- 是否影响组件生命周期语义
- 是否把开发期 Analyzer/Generator/AI 文档变成业务运行时依赖
- 是否只写了文字规则但没有计划后续用 Analyzer、Generator、模板或测试固化
- 是否让 AI 能明确知道文件落点、依赖方向、验证命令和常见错误

## 五、每轮执行流程

后续每轮都必须按这个流程执行：

1. 分析当前 YuoCore 和 ET10 对应模块。
2. 同时分析 ET10 对该能力的 AI 友好护栏：文档路由、Analyzer、Generator、模板、测试入口。
3. 提取可通用化能力。
4. 判断模块归属和依赖方向。
5. 判断是否应为可选包。
6. 判断是否需要新增或更新 AI 开发规则、Analyzer、Generator、脚手架或验证入口。
7. 只实现低风险、职责清晰、可测试的能力。
8. 补充 xUnit 测试。
9. 必要时补 Benchmark、压测、Analyzer 测试或 Generator 快照测试。
10. 运行：

```bash
dotnet build YuoCore.sln --disable-build-servers
dotnet test YuoCore.sln --no-build
```

11. 更新文档。
12. 记录潜在问题和待裁定项。
13. 当一个完整能力吸收完毕，并且构建/测试/文档都完成后，提交本轮改动。
14. 提交后确认本地无未提交项。
15. 如果还有可安全推进的点，继续下一轮。

## 六、提交规则

用户已授权：每次完整吸收完一个能力后，必须提交本地改动。

提交规则：

- 不推送。
- 提交前必须确认构建和测试状态。
- 提交信息要说明本轮吸收的能力或架构整理内容。
- 提交后运行 `git status --short`，确认本地无未提交项。
- `references/` 作为参考资料不参与实际开发提交，除非用户明确要求。
- 如果存在不能安全解决的问题，必须先记录到文档，再提交。

## 七、Goal 生命周期规则

这个长期 goal 不能由执行者主动标记完成。

即使暂时找不到新的可吸收能力，也必须继续：

- 复查模块依赖风险
- 复查单机/Unity/Web/MMO 导入路径
- 复查 ET10 参考模块
- 查找潜在耦合问题
- 查找可安全优化点
- 无法安全解决的问题写入风险文档

只有用户明确要求停止、完成、暂停或清除 goal 时，才能结束该 goal。

## 八、必须维护的文档

后续应逐步新增或维护：

- `docs/module-dependency-matrix.md`
  - 每个模块依赖谁
  - 是否单机可用
  - 是否 Unity 可用
  - 是否 Web 可用
  - 是否服务端专用
  - 是否分布式可选
  - 是否会增加包体

- `docs/import-profiles.md`
  - 单机最小导入
  - Unity 最小导入
  - Unity 联机客户端导入
  - Web 客户端协议导入
  - 单进程 MMO 服务端导入
  - 分布式 MMO 服务端导入

- `docs/et10-extraction-log.md`
  - 分析了哪个 ET10 模块
  - 提取了什么思想
  - 是否迁移
  - 放到哪个 YuoCore 模块
  - 为什么这样分层
  - 测试结果
  - 是否同步吸收了 AI 友好护栏

- `docs/ai-development-guide.md` 或等价 AI 入口
  - 任务路由
  - 文件落点
  - 依赖边界
  - 常见错误
  - 验证命令
  - Analyzer / Generator / 模板使用方式

- `docs/risk-and-decisions.md`
  - 用户待裁定项
  - 耦合风险
  - 性能取舍
  - API 兼容性风险
  - Unity 包体风险
  - 分布式可选化风险

## 九、禁止事项

禁止：

- 把 ASP.NET Core 引入 `YuoCore.Ecs`
- 把数据库依赖引入 `YuoCore.Ecs`
- 把远程网络依赖引入 `YuoCore.Runtime`
- 让 Unity 必须引用 `YuoCore.Backend`
- 让单机项目必须引用 `YuoCore.Gateway`
- 让 Protocol 暴露不必要的服务端内部实现
- Gateway 直接修改 ECS 组件
- 为性能擅自破坏 System try/catch 异常隔离语义
- 为分布式能力复杂化单机最小路径
- 把 Analyzer、Generator、AI skill、脚手架作为运行时核心依赖
- 只靠口头/文档约定约束 AI 高频错误，而没有后续固化为 Analyzer、Generator、模板或测试的计划
- 新增开发规则但不写清楚任务路由、文件落点、验证命令和错误修复方向
- 未经用户要求推送

## 十、每轮阶段性完成标准

每轮完成时必须满足：

- 代码能构建
- 测试能通过
- 文档有更新
- 依赖方向没有反向污染
- 新能力有明确模块归属
- 可选模块没有变成核心必需依赖
- 单机和 Unity 最小路径没有被破坏
- 记录本轮吸收的 ET10 思想
- 记录本轮是否吸收 AI 友好护栏，以及是否需要 Analyzer/Generator/模板/规则文档跟进
- 新增规则具有明确任务路由、文件落点、验证命令和常见错误说明
- 记录不能擅自做的风险项
- 已提交本轮完整能力改动
- 本地无未提交项
