# @yuocore/client

YuoCore Web 客户端协议 SDK 雏形。

它只负责：

```text
YuoClientEnvelopeV1 / YuoServerEnvelopeV1 编解码
requestId / clientSeq 自增
opcode 常量
request/response codec 抽象
浏览器 WebSocket request/response transport
StateSnapshot / StateDelta 通用推送接收和本地 Mirror
MMO 示例协议的最小 facade：登录、点击、快照、进地图、移动、断线、请求状态快照、StateSync recover、StateSync ack
```

它不包含：

```text
服务端 ActorId / FiberId / LocationKey
ASP.NET Core / 数据库 / Redis
具体 protobuf 生成代码
游戏渲染或 UI
完整业务 payload 反序列化 codegen
```

## MMO 示例 facade

`YuoMmoClient` 是给 Web 前端快速接入当前 Backend 示例用的薄封装。它只组合 `YuoClientSession`、`YuoClientTransport` 和当前 `yuocore.proto` 的最小手写 codec，不暴露服务端内部的 `ActorId`、`FiberId` 或 `LocationKey`。

```ts
import {
  YuoMmoClient,
  YuoMmoWebSocketSession,
  YuoStateSyncReceiver,
  YuoWebSocketTransport
} from "@yuocore/client";

const transport = await YuoWebSocketTransport.connect("ws://localhost:8080/ws");
const client = new YuoMmoClient(transport);

const welcome = await client.login("player-1");
const snapshot = await client.click(welcome.playerId, 1n);
const world = await client.enterMap(welcome.playerId, 1n);
const moved = await client.move(welcome.playerId, 1n, 10, 0, 5);

const receiver = new YuoStateSyncReceiver();
const state = await client.requestStateSnapshot(welcome.playerId, receiver.mirror.lastTick ?? 0n);
receiver.mirror.applySnapshot(state);
const recovered = await client.recoverStateSync(welcome.playerId, receiver.mirror.lastTick ?? 0n);
if (recovered.deltas.length > 0) {
  await client.ackStateSync(welcome.playerId, recovered.deltas.at(-1)!.tick);
}

const session = new YuoMmoWebSocketSession({
  url: "ws://localhost:8080/ws",
  requestTimeoutMs: 5000
});

await session.connect();
await session.client.enterMap(session.playerId, 1n);
await session.disconnect();
await session.reconnect();
```

当前手写 codec 只覆盖示例协议里的非负数值路径；正式项目建议从 `src/YuoCore.Protocol/Contracts/yuocore.proto` 生成 TypeScript codec，避免手写字段漂移。

## 通用请求

业务消息需要由 protobuf 生成工具提供 codec：

```ts
const session = new YuoClientSession();
const transport = await YuoWebSocketTransport.connect("ws://localhost:8080/ws");

const helloDescriptor = {
  requestOpCode: YuoProtocolOpCodes.helloRequest,
  responseOpCode: YuoProtocolOpCodes.welcome,
  request: HelloRequestCodec,
  response: WelcomeCodec
};

const welcome = await session.call(transport, helloDescriptor, { playerId: "" });
```

单次请求可以传入 `AbortSignal`，也可以使用会话层超时：

```ts
const welcome = await session.call(
  transport,
  helloDescriptor,
  { playerId: "" },
  { timeoutMs: 5000 }
);
```

服务端主动推送的 StateSync 消息可以交给 `YuoStateSyncReceiver`：

```ts
const receiver = new YuoStateSyncReceiver();

const result = receiver.applyServerEnvelope(serverEnvelopeBytes);
if (result.kind === YuoStateSyncPushKind.snapshot ||
    result.kind === YuoStateSyncPushKind.delta) {
  const entity = receiver.mirror.tryGet(1001n);
}
```

`payload` 仍然是业务自定义 bytes；Web 表现层应在 Mirror 之上自行绑定渲染对象。

如果 `YuoStateSyncReceiver` 返回 `snapshot_required`，可以调用 `recoverStateSync` 优先尝试短历史 replay；成功应用到本地 Mirror 后再调用 `ackStateSync`。当前 ACK 只让 Backend 示例记录客户端确认 tick，历史裁剪仍属于后续可靠流能力。

当前验证入口：

```bash
npx tsc -p clients/web/yuocore-client-ts/tsconfig.json --noEmit
npm --prefix clients/web/yuocore-client-ts run smoke
npm --prefix clients/web/yuocore-client-ts run browser-smoke
scripts/run-web-sdk-pack-smoke.sh
scripts/run-web-backend-browser-smoke.sh
```

`browser-smoke` 会启动本机 Chrome/Chromium headless，通过 HTTP 加载 `dist/index.js` 并执行浏览器 ESM 导入断言。它验证 SDK 能被真实浏览器导入，但不启动 Backend，也不验证 UI、重连或真实 WebSocket 会话。

`run-web-backend-browser-smoke.sh` 会启动真实 `YuoCore.Backend`，再用 Chrome headless 里的 Web SDK 连接 `/ws`，执行登录、进地图、移动、拉取状态快照、断线保存，并用同一个 playerId 重新登录验证地图和坐标恢复。它验证浏览器 SDK 和 Backend WebSocket 协议能联通，也覆盖手动重连恢复 smoke，但仍不等价于完整 UI、自动重连状态机或多浏览器兼容测试。
