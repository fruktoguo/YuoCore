# YuoCore.UnityClient

Unity 客户端可选适配层。

这个项目只提供 `IYuoClientTransport` 的 `ClientWebSocket` 实现，方便 Unity Editor / Standalone 直接复用 `YuoCore.Client` 的 request/response、timeout、opcode 和 envelope 语义。

它不包含：

```text
UnityEngine 依赖
YuoCore.Client.StateSync 依赖
服务端 Actor / Fiber / Location / Entity
Gateway / Backend / ASP.NET Core
数据库 / Redis
游戏表现对象或 UI
```

用法示例：

```csharp
using YuoCore.Client;
using YuoCore.Client.Unity;
using YuoCore.Protocol;

var session = new YuoClientSession(new YuoClientSessionOptions
{
    RequestTimeout = TimeSpan.FromSeconds(5)
});

using var transport = await YuoClientWebSocketTransport.ConnectAsync(
    new Uri("ws://localhost:8080/ws"));

var response = await session.CallAsync(
    transport,
    new HelloRequest(),
    Welcome.Parser);
```

更常用的 MMO 示例客户端门面：

```csharp
using YuoCore.Client;
using YuoCore.Client.Unity;

using var transport = await YuoClientWebSocketTransport.ConnectAsync(
    new Uri("ws://localhost:8080/ws"));

transport.PushReceived += message =>
{
    // requestId=0 的服务端推送，例如 StateSnapshot / StateDelta。
    // 可交给 YuoCore.Client.StateSync 的 YuoClientStateSyncReceiver 应用到本地 Mirror。
};

var client = new YuoMmoClient(transport);
var welcome = await client.LoginAsync();
var clicked = await client.ClickAsync(welcome.PlayerId, amount: 1);
var world = await client.EnterMapAsync(welcome.PlayerId, mapId: 1001);
world = await client.MoveAsync(welcome.PlayerId, inputSeq: 1, x: 3, y: 0, z: 4);
```

可选 StateSync 示例：

```csharp
using YuoCore.Client;
using YuoCore.Client.StateSync;
using YuoCore.Client.Unity;

using var transport = await YuoClientWebSocketTransport.ConnectAsync(
    new Uri("ws://localhost:8080/ws"));

var client = new YuoMmoClient(transport);
var receiver = new YuoClientStateSyncMultiStreamReceiver();
var recovery = new YuoClientStateSyncMultiStreamRecovery(client, receiver);

transport.PushReceived += message =>
{
    var result = receiver.ApplyServerEnvelope(message);
    if (YuoClientStateSyncAcks.ShouldAck(result))
    {
        _ = client.AckStateSyncAsync(
            playerId: "player-id",
            ackTick: result.Tick!.Value,
            streamId: result.StreamId);
    }
};

var recovered = await recovery.RecoverDetailedAsync(
    playerId: "player-id",
    streamId: "map:1001:aoi:default.cell_0_0",
    knownTick: 0);
if (recovered.Reason == "stream_not_observable")
{
    // 当前 AOI stream 不可恢复，可重新请求进入地图或等待服务端下发新的订阅。
}

foreach (var result in recovered.Results)
{
    if (YuoClientStateSyncAcks.ShouldAck(result))
    {
        await client.AckStateSyncAsync(
            "player-id",
            result.Tick!.Value,
            result.StreamId);
    }
}
```

说明：

```text
Unity WebGL 不应使用 ClientWebSocket；WebGL 需要用 JS WebSocket 或 Unity WebGL 平台适配实现 IYuoClientTransport。
Unity 本地预测/表现 ECS 可以另外引用 YuoCore.Ecs，但客户端 SDK 不要求引用 ECS。
PushReceived 只暴露外部协议 envelope，不暴露 Gateway session、Actor、Fiber 或服务端 Entity。
StateSync 是可选包；需要 Mirror / Recover / ACK helper 时再引用 YuoCore.Client.StateSync。
StateSync ACK 只确认客户端已成功应用到 Mirror 的 tick，Backend 示例会基于 ACK 裁剪短历史。
```
