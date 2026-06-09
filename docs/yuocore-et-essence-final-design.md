# YuoCore 吸收 ET 精华后的最终架构方案

本文档定义 YuoCore 后续的最终架构方向。它不是照搬 ET，而是吸收 ET 最有价值的后端思想，并和现有 Yuo ECS 保持兼容。

核心结论：

```text
ECS 负责数据和逻辑。
Fiber 负责所有权和执行上下文。
Actor 消息负责跨所有权边界通信。
Location 负责稳定业务 ID 到运行时地址的映射。
ServerHost 负责按配置启动进程、Fiber 和服务。
```

YuoCore 的目标不是把 ECS 改成 ET，而是把现有 ECS 放进一个可多线程、多进程、可部署的运行时架构里。

## 一、最终原则

### 1. Entity 不是线程安全对象

`YuoEntity`、`YuoComponent`、`YuoWorld` 默认不做多线程并发写保护。

规则：

```text
当前 Fiber 拥有的 Entity：
  可以直接读写。

其他 Fiber / 其他进程拥有的 Entity：
  不能直接读写，只能发消息。
```

判断方式：

```csharp
entity.World == currentFiber.World
```

只有满足这个条件，业务代码才可以直接操作组件。

### 2. 多线程并行粒度是 Fiber，不是 System

不在同一个 `YuoWorld` 里并行跑 system。

正确方式：

```text
GameShardFiber-0
  YuoWorld.Update()

GameShardFiber-1
  YuoWorld.Update()

GameShardFiber-2
  YuoWorld.Update()

GameShardFiber-3
  YuoWorld.Update()
```

多个 Fiber 可以并行 Tick，但每个 Fiber 内部仍然是单线程 ECS。

这样保留你的 ECS 写法：

```csharp
public override void Run(PlayerComponent player, WalletComponent wallet)
{
    wallet.Gold += player.Power;
}
```

不用在每个 component 上加锁，也不用要求所有 system 都线程安全。

### 3. 消息机制不是 ECS 的一部分

Actor 消息层和 ECS 不是同一个东西。

`YuoSystem.Run` 是 ECS 逻辑入口：

```text
当前 World 正在跑某类 system。
每个匹配 Entity 执行一次。
```

`YuoActorHandler.Run` 是消息入口：

```text
某个 request 被投递到目标 Actor。
handler 拿到目标 Entity、request、response。
```

两者关系：

```text
Actor 消息把请求送到目标 Fiber。
目标 Fiber 内部再直接操作 ECS。
```

不要把所有消息都设计成 ECS system，也不要让 ECS system 负责网络、RpcId、Location、超时和回包。

## 二、核心概念

| 概念 | YuoCore 名称 | 职责 |
| --- | --- | --- |
| 全局运行时 | `YuoCoreRuntime` | 管理所有 Fiber，提供顺序 Tick 和并行 Tick |
| 执行上下文 | `YuoFiber` | 拥有 mailbox 和一个 `YuoWorld` |
| ECS 世界 | `YuoWorld` | 管理 Entity、Component、System、生命周期 |
| 游戏对象 | `YuoEntity` | ECS 实体，属于某个 `YuoWorld` |
| 数据模块 | `YuoComponent` | 挂在 Entity 上的数据 |
| ECS 逻辑 | `YuoSystem` | 当前 World 内的组件逻辑 |
| 消息目标 | Actor | 可被地址定位的执行对象，可以是 Entity，也可以是服务 |
| 消息地址 | `YuoActorAddress` | Node / Process / Fiber / Actor |
| 稳定定位 | `YuoLocation` | playerId、roomId、guildId 到 ActorAddress |
| 启动配置 | `YuoStartConfig` | 进程、服务、Fiber、Shard 配置 |
| 服务宿主 | `YuoServerHost` | 根据配置启动 Runtime 和 Fiber |

## 三、项目结构

最终项目结构：

```text
src/
  YuoCore.Abstractions/
    通用错误、结果、时钟、运行时 ID

  YuoCore.Ecs/
    现有 ECS 核心
    YuoWorld / YuoEntity / YuoComponent / YuoSystem

  YuoCore.Runtime/
    YuoCoreRuntime
    YuoFiber
    YuoFiberManager
    并行 Tick

  YuoCore.Actor/
    ActorId
    ActorAddress
    ActorEnvelope
    ActorRouter
    ActorHandler
    Mailbox
    Send / Call

  YuoCore.Location/
    稳定业务 ID -> ActorAddress
    版本校验
    迁移锁
    地址缓存

  YuoCore.Network/
    进程间传输抽象
    Session
    Transport
    InMemoryTransport
    SerializedTransport
    后续 TCP / WebSocket / KCP / QUIC 可接入

  YuoCore.Protocol/
    协议 envelope
    opcode
    envelope codec
    serializer
    protobuf 契约

  YuoCore.Config/
    StartConfig
    FiberConfig
    ServiceConfig
    JSON Schema

  YuoCore.Server/
    通用服务宿主
    根据 StartConfig 启动 Runtime / Fiber / 服务

  YuoCore.Backend/
    示例挂机游戏后端
    ASP.NET Core
    WebSocket + Protobuf
    PostgreSQL / Redis
    OpenTelemetry / Serilog / Prometheus
```

## 四、运行拓扑

单进程多 Fiber：

```text
YuoServer process
  GatewayFiber
    WebSocket sessions
    玩家连接

  LocationFiber
    playerId -> ActorAddress
    roomId -> ActorAddress

  GameShardFiber-0
    YuoWorld
    Player entities 0..N

  GameShardFiber-1
    YuoWorld
    Player entities 0..N

  PersistenceFiber
    PostgreSQL writer
    Redis cache writer

  SchedulerFiber
    定时任务
    离线收益
```

多进程部署：

```text
Gateway process
  GatewayFiber-0..N

Location process
  LocationFiber

Game process A
  GameShardFiber-0..N

Game process B
  GameShardFiber-0..N

Persistence process
  PersistenceFiber
```

无论单进程还是多进程，业务调用统一走 Actor 地址。

## 五、通信规则

### 1. 当前 Fiber 内部

已经拿到当前 Fiber 拥有的 `YuoEntity`：

```csharp
var wallet = player.GetComponent<WalletComponent>();
wallet.Gold += 100;
```

这是直接操作，不需要消息。

适用场景：

```text
YuoSystem.Run
YuoActorHandler.Run
当前 Fiber 内部 helper
当前 World 内部生命周期
```

### 2. 同进程不同 Fiber

只有目标地址：

```text
YuoActorAddress
  ProcessId = 当前进程
  FiberId = 其他 Fiber
```

框架行为：

```text
ActorRouter
  -> 本地 Fiber mailbox
  -> Channel / ConcurrentQueue
  -> 目标 Fiber drain mailbox
  -> 找到目标 Actor / Entity
  -> 调用 handler
```

业务不需要判断“这是同进程”。

### 3. 跨进程

目标地址不属于当前进程：

```text
YuoActorAddress
  ProcessId != 当前进程
```

框架行为：

```text
ActorRouter
  -> NetworkTransport
  -> ProtocolEnvelope
  -> 序列化
  -> 远端进程
  -> 远端 ActorRouter
  -> 远端 Fiber mailbox
```

业务 handler 写法不变。

进程内模拟远端链路时，也要走一次 `SerializedYuoTransport`。
否则内存 transport 会共享 payload 引用，和真实跨进程“序列化后得到新对象”的语义不同。

### 4. 只有业务 ID

例如：

```text
playerId
roomId
guildId
```

必须先走 Location：

```text
playerId
  -> Location.Resolve(Player, playerId)
  -> YuoActorAddress
  -> ActorRouter.Send / Call
```

业务层不要缓存 `YuoEntity` 引用跨 Fiber 使用。

## 六、消息模型

请求消息是业务 DTO：

```csharp
public sealed record AcceptQuestRequest(long QuestId);

public sealed record AcceptQuestResponse
{
    public int Error { get; set; }
}
```

handler 最终看到：

```csharp
public sealed class AcceptQuestHandler
    : YuoActorHandler<AcceptQuestRequest, AcceptQuestResponse>
{
    protected override ValueTask Run(
        YuoEntity player,
        AcceptQuestRequest request,
        AcceptQuestResponse response,
        CancellationToken cancellationToken)
    {
        var quest = player.GetComponent<PlayerQuestComponent>();
        quest.Accept(request.QuestId);
        return ValueTask.CompletedTask;
    }
}
```

业务只关心：

```text
目标 Entity
request 业务参数
response 回包
```

框架内部维护：

```text
TargetActorId
SenderActorId
RpcId
RequestId
FromFiber
Timeout
Error
```

同进程时可以直接传对象引用，但发送后必须视为不可变消息。

跨进程时消息会序列化成 bytes，远端反序列化成新的 DTO 对象。

handler 不应该关心消息来自本进程还是远端。

## 七、Actor Handler 与 ECS System 的区别

### YuoSystem.Run

用途：ECS 内部逻辑。

```csharp
public sealed class IdleIncomeSystem
    : YuoSystem<PlayerComponent, WalletComponent>, IUpdate
{
    public override void Run(PlayerComponent player, WalletComponent wallet)
    {
        wallet.Gold += player.Power;
    }
}
```

特点：

```text
由 YuoWorld 调度
按 system tag 执行
每个匹配 Entity 执行一次
没有 request / response
不负责跨 Fiber 通信
```

### YuoActorHandler.Run

用途：消息入口。

```csharp
public sealed class ClickPlayerHandler
    : YuoActorHandler<ClickRequest, ClickResponse>
{
    protected override ValueTask Run(
        YuoEntity player,
        ClickRequest request,
        ClickResponse response,
        CancellationToken cancellationToken)
    {
        player.GetComponent<WalletComponent>().Gold += request.Amount;
        return ValueTask.CompletedTask;
    }
}
```

特点：

```text
由 ActorRouter / Mailbox / Dispatcher 调用
收到一条消息执行一次
输入是 Entity + Request + Response
可以 async
负责处理请求和回包
```

最终关系：

```text
外部请求
  -> ActorHandler.Run
  -> 直接改 Component
  -> 或触发 ECS command

每帧逻辑
  -> YuoSystem.Run
```

## 八、Location 设计

Location 解决的是：

```text
稳定业务 ID 怎么找到当前运行时 Actor？
```

例如：

```text
Player:7f8c... -> node-a/game-1/fiber-3/actor-9981
Room:1001      -> node-b/room-2/fiber-1/actor-120
Guild:88       -> node-c/guild-1/fiber-2/actor-55
```

接口语义：

```text
Register(key, address, expectedVersion)
Resolve(key)
Lock(key, owner)
Unlock(key, owner)
Remove(key, expectedVersion)
```

版本用于防止旧地址覆盖新地址。

锁用于迁移：

```text
Lock Player
  -> 阻止并发写 Location
  -> 从旧 shard 保存状态
  -> 在新 shard 创建 Entity
  -> Register 新地址
  -> Unlock
```

业务请求如果遇到 Location 失效：

```text
清缓存
重新 Resolve
重试一次
```

## 九、Gateway 到 GameShard 的标准流程

客户端点击：

```text
Client
  -> WebSocket Gateway
  -> ClickRequest(playerId, amount)
```

Gateway 只知道 `playerId`，不能直接操作玩家 Entity。

流程：

```text
GatewayFiber
  -> Location.Resolve(Player, playerId)
  -> ActorRouter.Call(address, ClickRequest)
  -> GameShardFiber mailbox
  -> PlayerActorHandler.Run(playerEntity, request, response)
  -> 修改 WalletComponent
  -> 返回 ClickResponse
  -> Gateway
  -> Client
```

GameShard 内部挂机收益：

```text
GameShardFiber.Tick
  -> YuoWorld.Update
  -> IdleIncomeSystem.Run(...)
  -> 直接改 WalletComponent
```

一个是消息入口，一个是 ECS tick，两者不要混在一起。

## 十、持久化与缓存

GameShard 不直接做重 IO。

推荐流程：

```text
GameShardFiber
  -> 产生 PlayerSnapshot
  -> 发送给 PersistenceFiber

PersistenceFiber
  -> 批量写 PostgreSQL
  -> 写 Redis snapshot cache
```

原因：

```text
GameShard 保持低延迟
数据库慢查询不会卡住玩家逻辑
写入可以批量化
```

读取策略：

```text
登录时：
  Redis snapshot
  fallback PostgreSQL
  创建 PlayerEntity
  注册 Location

运行时：
  GameShard 内存为准

下线 / 定时：
  Snapshot 写回
```

## 十一、后台任务

后台任务不要直接跨 shard 改玩家。

错误方式：

```text
Scheduler 拿到 PlayerEntity 引用直接改。
```

正确方式：

```text
SchedulerFiber
  -> playerId
  -> Location.Resolve
  -> ActorRouter.Send / Call
  -> PlayerActorHandler
  -> 当前 GameShard 内改组件
```

如果任务只操作全局不可变配置，可以直接读。

如果任务操作玩家、房间、公会、市场等可变状态，必须发给所有者 Actor。

## 十二、单机/前端兼容

单机模式：

```text
YuoWorld world = new();
world.OnInit();
new YuoEntity();
world.Update();
```

仍然可用。

前端或单机游戏可以不使用：

```text
YuoServerHost
YuoLocation
YuoNetwork
YuoActorRouter
```

也可以使用一个本地 Fiber：

```text
YuoCoreRuntime
  YuoFiber
    YuoWorld
```

单机时大多数对象都在同一个 World，直接操作即可。

后端多 shard 时，才需要严格 Actor 地址和 Location。

## 十三、性能策略

### 1. 并行靠分片

```text
1w 玩家
  -> 分到 4/8/16 个 GameShardFiber
  -> 多 Fiber 并行 Tick
```

每个玩家只属于一个 shard。

### 2. System 内不加锁

system 内部不要做跨 shard 共享写。

需要共享状态时：

```text
只读配置：
  Immutable / readonly，可共享

可变状态：
  拆所有权，发消息
```

### 3. 消息对象发送后不可变

同进程可能直接传对象引用。

规则：

```csharp
var request = new ClickRequest { Amount = 1 };
await router.SendAsync(address, request);

// 发送后不要再修改 request
```

跨进程会序列化，所以天然是拷贝。

为了语义一致，所有消息 DTO 都应按不可变数据设计。

### 4. IO 和 CPU 任务分离

```text
GameShardFiber:
  高频逻辑
  ECS Tick
  玩家命令

PersistenceFiber:
  数据库
  Redis

SchedulerFiber:
  定时任务
  延迟消息

GatewayFiber:
  连接管理
  协议收发
```

## 十四、错误处理

Actor Call 需要框架统一处理：

```text
RpcId
超时
目标不存在
handler 异常
response 类型错误
Location 失效
```

handler 内抛出的异常应该转成错误 response，不应该打崩 Fiber loop。

但 ECS `YuoSystem` 仍保留现有 try-catch 策略：

```text
一个 system 报错，不影响其他 system 继续运行。
```

消息层和 ECS 层都要隔离错误，但隔离位置不同。

## 十五、最终开发规则

### 直接操作 Entity 的情况

```text
当前 YuoSystem.Run 内
当前 YuoActorHandler.Run 内
确定 entity.World == currentFiber.World
当前 World 生命周期内部
```

### 必须发消息的情况

```text
只有 playerId / roomId / guildId
目标可能在其他 shard
目标可能在其他进程
后台任务操作玩家
Gateway 操作 GameShard 玩家
GameShard 通知 Gateway session
GameShard A 操作 GameShard B 的对象
```

### 禁止行为

```text
跨 Fiber 保存 YuoEntity 引用并直接改
跨 World 直接调用组件方法
在 ECS System 内做阻塞数据库 IO
业务层判断本地/远端并写两套逻辑
消息发送后继续修改 request 对象
```

## 十六、落地顺序

### 第一阶段：运行时边界

已完成/应保持：

```text
YuoWorld 支持非静态实例
YuoEntity 显式绑定 World
YuoFiber 拥有一个 World
YuoCoreRuntime 管理多个 Fiber
TickParallelAsync 按 Fiber 并行
```

### 第二阶段：Actor 消息入口

需要完善：

```text
YuoActorHandler<TRequest,TResponse>
YuoActorDispatcher
YuoMailbox
YuoActorEnvelope
Send / Call / Reply
RpcId / Timeout
```

### 第三阶段：Location

需要完善：

```text
业务 key -> ActorAddress
缓存
版本
迁移锁
失效重试
Redis/PostgreSQL 分布式实现
```

### 第四阶段：Network

需要完善：

```text
进程间 transport
ProtocolEnvelope
Protobuf serializer
Session 生命周期
断线重连
心跳
背压
```

### 第五阶段：服务化

需要完善：

```text
StartConfig
ServiceDiscovery
Gateway/Game/Persistence/Scheduler 拆分
Docker compose 多进程部署
OpenTelemetry 指标
压测脚本
```

## 十七、最终一句话

YuoCore 最终应该是：

```text
一个保留原 ECS 手感的服务器运行时框架。

单 Fiber 内像 Unity ECS 一样直接写逻辑。
跨 Fiber / 跨进程像 ET 一样全走 Actor 消息。
用 Location 隐藏对象迁移。
用 Fiber 分片吃满多核。
用 ServerHost 和 StartConfig 组织后端进程。
```

这就是 YuoCore 吸收 ET 精华后的最终形态。
