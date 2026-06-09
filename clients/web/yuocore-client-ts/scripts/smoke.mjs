import assert from "node:assert/strict";
import {
  YuoClientProtocolError,
  YuoClientSession,
  YuoMmoClient,
  YuoMmoWebSocketSession,
  YuoProtocolOpCodes,
  YuoStateChangeKind,
  YuoStateSyncApplyReason,
  YuoStateSyncApplyStatus,
  YuoStateSyncMultiStreamReceiver,
  YuoStateSyncPushKind,
  YuoStateSyncReceiver,
  YuoWebSocketTransport,
  decodeClickRequest,
  decodeClientEnvelopeV1,
  decodeDisconnectRequest,
  decodeEnterMapRequest,
  decodeHelloRequest,
  decodeMoveRequest,
  decodeSnapshotRequest,
  decodeStateSnapshotRequest,
  decodeStateSyncAckRequest,
  decodeStateSyncRecoverRequest,
  encodePlayerSnapshot,
  encodeStateSnapshotMessage,
  encodeStateSubscriptionMessage,
  encodeStateSyncAckResponse,
  encodeStateSyncRecoverResponse,
  encodeWelcome,
  encodeWorldSnapshot,
  ackStateSyncIfApplied,
  recoverStateSync,
  recoverStateSyncDetailed,
  recoverStateSyncMultiStreamDetailed,
  recoverStateSyncSnapshot,
  readClientEnvelopeRequestId,
  shouldAckStateSync,
  shouldRequestStateSnapshot,
  subscribeStateSyncPushes
} from "../dist/index.js";

class ProtoWriter {
  chunks = [];

  uint32(field, value) {
    this.tag(field, 0);
    this.varint(value);
  }

  uint64(field, value) {
    this.tag(field, 0);
    this.varint(value);
  }

  string(field, value) {
    this.bytes(field, new TextEncoder().encode(value));
  }

  bytes(field, value) {
    this.tag(field, 2);
    this.varint(BigInt(value.length));
    for (const byte of value) {
      this.chunks.push(byte);
    }
  }

  finish() {
    return new Uint8Array(this.chunks);
  }

  tag(field, wireType) {
    this.varint(BigInt((field << 3) | wireType));
  }

  varint(value) {
    let next = value;
    while (next >= 0x80n) {
      this.chunks.push(Number((next & 0x7fn) | 0x80n));
      next >>= 7n;
    }

    this.chunks.push(Number(next));
  }
}

const textCodec = {
  encode(value) {
    return new TextEncoder().encode(value.text);
  },
  decode(bytes) {
    return { text: new TextDecoder().decode(bytes) };
  }
};

const descriptor = {
  requestOpCode: YuoProtocolOpCodes.helloRequest,
  responseOpCode: YuoProtocolOpCodes.welcome,
  request: textCodec,
  response: textCodec
};

const transport = {
  async send(request) {
    const requestId = readClientEnvelopeRequestId(request);
    return encodeServerEnvelope({
      opcode: YuoProtocolOpCodes.welcome,
      requestId,
      serverSeq: 7n,
      payload: textCodec.encode({ text: "welcome" })
    });
  }
};

const session = new YuoClientSession();
const response = await session.call(transport, descriptor, { text: "hello" });

assert.equal(YuoProtocolOpCodes.stateSnapshot, 106);
assert.equal(YuoProtocolOpCodes.stateDelta, 107);
assert.equal(response.requestId, 1n);
assert.equal(response.serverSeq, 7n);
assert.equal(response.opCode, YuoProtocolOpCodes.welcome);
assert.deepEqual(response.payload, { text: "welcome" });

let loopbackServerSeq = 40n;
const mmoTransport = {
  async send(request) {
    const envelope = decodeClientEnvelopeV1(request);
    switch (envelope.opcode) {
      case YuoProtocolOpCodes.helloRequest: {
        const hello = decodeHelloRequest(envelope.payload);
        assert.equal(hello.playerId, "web-player");
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.welcome,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodeWelcome({ playerId: "web-player", message: "ok" })
        });
      }
      case YuoProtocolOpCodes.clickRequest: {
        const click = decodeClickRequest(envelope.payload);
        assert.equal(click.playerId, "web-player");
        assert.equal(click.amount, 3n);
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.playerSnapshot,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodePlayerSnapshot(makePlayerSnapshot({ gold: 13n, clicks: 3n }))
        });
      }
      case YuoProtocolOpCodes.snapshotRequest: {
        const snapshot = decodeSnapshotRequest(envelope.payload);
        assert.equal(snapshot.playerId, "web-player");
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.playerSnapshot,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodePlayerSnapshot(makePlayerSnapshot({ gold: 13n, clicks: 3n }))
        });
      }
      case YuoProtocolOpCodes.enterMapRequest: {
        const enterMap = decodeEnterMapRequest(envelope.payload);
        assert.equal(enterMap.playerId, "web-player");
        assert.equal(enterMap.mapId, 2n);
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.worldSnapshot,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodeWorldSnapshot(makeWorldSnapshot({ x: 0, y: 0, z: 0, kind: "enter" }))
        });
      }
      case YuoProtocolOpCodes.moveRequest: {
        const move = decodeMoveRequest(envelope.payload);
        assert.equal(move.playerId, "web-player");
        assert.equal(move.inputSeq, 9n);
        assert.equal(move.x, 1.5);
        assert.equal(move.y, 0);
        assert.equal(move.z, -2.5);
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.worldSnapshot,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodeWorldSnapshot(makeWorldSnapshot({ x: move.x, y: move.y, z: move.z, kind: "move" }))
        });
      }
      case YuoProtocolOpCodes.disconnectRequest: {
        const disconnect = decodeDisconnectRequest(envelope.payload);
        assert.equal(disconnect.playerId, "web-player");
        assert.equal(disconnect.sessionId, "");
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.playerSnapshot,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodePlayerSnapshot(makePlayerSnapshot({ online: false }))
        });
      }
      case YuoProtocolOpCodes.stateSnapshotRequest: {
        const stateSnapshot = decodeStateSnapshotRequest(envelope.payload);
        assert.equal(stateSnapshot.playerId, "web-player");
        assert.ok(stateSnapshot.knownTick === 55n || stateSnapshot.knownTick === 102n);
        const tick = stateSnapshot.knownTick === 102n ? 103n : 56n;
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.stateSnapshot,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodeStateSnapshotMessage({
            tick,
            streamId: "player:web-player",
            entities: [
              { networkId: 2001n, kind: "player", version: 1, payload: new Uint8Array([9]) }
            ]
          })
        });
      }
      case YuoProtocolOpCodes.stateSyncRecoverRequest: {
        const recover = decodeStateSyncRecoverRequest(envelope.payload);
        assert.equal(recover.playerId, "web-player");
        assert.equal(recover.knownTick, 103n);
        assert.equal(recover.streamId, "player:web-player");
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.stateSyncRecover,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodeStateSyncRecoverResponse({
            replayed: true,
            reason: "Replayed",
            streamId: "player:web-player",
            deltas: [
              {
                tick: 104n,
                baseTick: 103n,
                streamId: "player:web-player",
                changes: [
                  {
                    kind: YuoStateChangeKind.update,
                    networkId: 2001n,
                    state: { networkId: 2001n, kind: "player", version: 2, payload: new Uint8Array([10]) }
                  }
                ]
              }
            ]
          })
        });
      }
      case YuoProtocolOpCodes.stateSyncAckRequest: {
        const ack = decodeStateSyncAckRequest(envelope.payload);
        assert.equal(ack.playerId, "web-player");
        assert.equal(ack.ackTick, 104n);
        assert.ok(ack.streamId === "" || ack.streamId === "player:web-player");
        return encodeServerEnvelope({
          opcode: YuoProtocolOpCodes.stateSyncAck,
          requestId: envelope.requestId,
          serverSeq: ++loopbackServerSeq,
          payload: encodeStateSyncAckResponse({
            accepted: true,
            ackTick: ack.ackTick,
            latestTick: 104n,
            reason: "accepted",
            streamId: ack.streamId || "player:web-player"
          })
        });
      }
      default:
        throw new Error(`unexpected mmo opcode: ${envelope.opcode}`);
    }
  }
};

const mmoClient = new YuoMmoClient(mmoTransport);
const welcome = await mmoClient.login("web-player");
assert.deepEqual(welcome, { playerId: "web-player", message: "ok" });

const clicked = await mmoClient.click("web-player", 3n);
assert.equal(clicked.gold, 13n);
assert.equal(clicked.clicks, 3n);

const currentSnapshot = await mmoClient.getSnapshot("web-player");
assert.equal(currentSnapshot.playerId, "web-player");
assert.equal(currentSnapshot.online, true);

const entered = await mmoClient.enterMap("web-player", 2n);
assert.equal(entered.mapId, 2n);
assert.equal(entered.events[0]?.kind, "enter");

const moved = await mmoClient.move("web-player", 9n, 1.5, 0, -2.5);
assert.equal(moved.players[0]?.x, 1.5);
assert.equal(moved.players[0]?.z, -2.5);
assert.equal(moved.events[0]?.kind, "move");

const stateSnapshotResponse = await mmoClient.requestStateSnapshot("web-player", 55n);
assert.equal(stateSnapshotResponse.tick, 56n);
assert.equal(stateSnapshotResponse.streamId, "player:web-player");
assert.equal(stateSnapshotResponse.entities[0]?.networkId, 2001n);

const disconnected = await mmoClient.disconnect("web-player");
assert.equal(disconnected.online, false);

const stateSync = new YuoStateSyncReceiver();
const snapshotResult = stateSync.applyServerEnvelope(encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.stateSnapshot,
  requestId: 0n,
  serverSeq: 10n,
  payload: encodeStateSnapshot({
    tick: 100n,
    streamId: "player:web-player",
    entities: [
      { networkId: 1001n, kind: "player", version: 1, payload: new Uint8Array([1, 2]) }
    ]
  })
}));

assert.equal(snapshotResult.kind, YuoStateSyncPushKind.snapshot);
assert.equal(snapshotResult.streamId, "player:web-player");
assert.equal(snapshotResult.applyResult?.status, YuoStateSyncApplyStatus.applied);
assert.equal(shouldAckStateSync(snapshotResult), true);
assert.equal(stateSync.mirror.tryGet(1001n)?.version, 1);

const deltaResult = stateSync.applyServerEnvelope(encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.stateDelta,
  requestId: 0n,
  serverSeq: 11n,
  payload: encodeStateDelta({
    tick: 101n,
    baseTick: 100n,
    streamId: "player:web-player",
    changes: [
      {
        kind: YuoStateChangeKind.update,
        networkId: 1001n,
        state: { networkId: 1001n, kind: "player", version: 2, payload: new Uint8Array([3]) }
      }
    ]
  })
}));

assert.equal(deltaResult.kind, YuoStateSyncPushKind.delta);
assert.equal(deltaResult.streamId, "player:web-player");
assert.equal(deltaResult.applyResult?.status, YuoStateSyncApplyStatus.applied);
assert.equal(stateSync.mirror.tryGet(1001n)?.version, 2);

const ignoredResult = stateSync.applyServerEnvelope(encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.pongResponse,
  requestId: 0n,
  serverSeq: 12n,
  payload: new Uint8Array()
}));
assert.equal(ignoredResult.kind, YuoStateSyncPushKind.ignored);

const gapResult = stateSync.applyServerEnvelope(encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.stateDelta,
  requestId: 0n,
  serverSeq: 13n,
  payload: encodeStateDelta({
    tick: 103n,
    baseTick: 102n,
    changes: [
      {
        kind: YuoStateChangeKind.update,
        networkId: 1001n,
        state: { networkId: 1001n, kind: "player", version: 3, payload: new Uint8Array([4]) }
      }
    ]
  })
}));
assert.equal(gapResult.applyResult?.status, YuoStateSyncApplyStatus.snapshotRequired);
assert.equal(gapResult.applyResult?.reason, YuoStateSyncApplyReason.gapDetected);
assert.equal(stateSync.mirror.snapshotRequired, true);
assert.equal(shouldRequestStateSnapshot(gapResult), true);
assert.equal(shouldAckStateSync(gapResult), false);

const multiStream = new YuoStateSyncMultiStreamReceiver();
const firstStream = "map:1001:aoi:cell_1";
const secondStream = "map:1001:aoi:cell_2";
const firstMultiSnapshot = multiStream.applyStateSnapshot({
  tick: 10n,
  streamId: firstStream,
  entities: [
    { networkId: 3001n, kind: "player", version: 1, payload: new Uint8Array([1]) }
  ]
});
const secondMultiSnapshot = multiStream.applyStateSnapshot({
  tick: 3n,
  streamId: secondStream,
  entities: [
    { networkId: 4001n, kind: "player", version: 1, payload: new Uint8Array([1]) }
  ]
});
const secondMultiDelta = multiStream.applyStateDelta({
  tick: 4n,
  baseTick: 3n,
  streamId: secondStream,
  changes: [
    {
      kind: YuoStateChangeKind.update,
      networkId: 4001n,
      state: { networkId: 4001n, kind: "player", version: 2, payload: new Uint8Array([2]) }
    }
  ]
});
assert.equal(firstMultiSnapshot.applyResult?.status, YuoStateSyncApplyStatus.applied);
assert.equal(secondMultiSnapshot.applyResult?.status, YuoStateSyncApplyStatus.applied);
assert.equal(secondMultiDelta.applyResult?.status, YuoStateSyncApplyStatus.applied);
assert.equal(multiStream.streams.getCursor(firstStream).lastTick, 10n);
assert.equal(multiStream.streams.getCursor(secondStream).lastTick, 4n);

const firstMultiGap = multiStream.applyStateDelta({
  tick: 12n,
  baseTick: 11n,
  streamId: firstStream,
  changes: [
    {
      kind: YuoStateChangeKind.update,
      networkId: 3001n,
      state: { networkId: 3001n, kind: "player", version: 2, payload: new Uint8Array([2]) }
    }
  ]
});
assert.equal(firstMultiGap.applyResult?.status, YuoStateSyncApplyStatus.snapshotRequired);
assert.equal(multiStream.streams.getCursor(firstStream).snapshotRequired, true);
assert.equal(multiStream.streams.getCursor(secondStream).snapshotRequired, false);
const subscriptionResult = multiStream.applyServerEnvelope(encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.stateSubscription,
  requestId: 0n,
  serverSeq: 200n,
  payload: encodeStateSubscriptionMessage({
    streamIds: [secondStream],
    reason: "aoi_moved",
    resetMissing: true
  })
}));
assert.equal(subscriptionResult.kind, YuoStateSyncPushKind.subscription);
assert.equal(multiStream.streams.tryGet(firstStream), undefined);
assert.notEqual(multiStream.streams.tryGet(secondStream), undefined);
assert.throws(() => new YuoStateSyncMultiStreamReceiver().applyStateSnapshot({
  tick: 1n,
  entities: []
}));

const recoveredResult = await recoverStateSyncSnapshot(mmoClient, stateSync, "web-player", 102n);
assert.equal(recoveredResult.kind, YuoStateSyncPushKind.snapshot);
assert.equal(recoveredResult.applyResult?.status, YuoStateSyncApplyStatus.applied);
assert.equal(stateSync.mirror.snapshotRequired, false);
assert.equal(stateSync.mirror.tryGet(2001n)?.version, 1);

const replayResults = await recoverStateSync(mmoClient, stateSync, "web-player", 103n);
assert.equal(replayResults.length, 1);
assert.equal(replayResults[0]?.kind, YuoStateSyncPushKind.delta);
assert.equal(replayResults[0]?.applyResult?.status, YuoStateSyncApplyStatus.applied);
assert.equal(replayResults[0]?.streamId, "player:web-player");
assert.equal(stateSync.mirror.tryGet(2001n)?.version, 2);

const autoAckResponse = await ackStateSyncIfApplied(mmoClient, "web-player", replayResults[0]);
assert.equal(autoAckResponse?.accepted, true);
assert.equal(autoAckResponse?.ackTick, 104n);
assert.equal(autoAckResponse?.streamId, "player:web-player");

const rejectedRecoverClient = new YuoMmoClient({
  async send(request) {
    const envelope = decodeClientEnvelopeV1(request);
    assert.equal(envelope.opcode, YuoProtocolOpCodes.stateSyncRecoverRequest);
    const recover = decodeStateSyncRecoverRequest(envelope.payload);
    assert.equal(recover.playerId, "web-player");
    assert.equal(recover.streamId, "map:1001:aoi:default.cell_9_0");
    return encodeServerEnvelope({
      opcode: YuoProtocolOpCodes.stateSyncRecover,
      requestId: envelope.requestId,
      serverSeq: 700n,
      payload: encodeStateSyncRecoverResponse({
        replayed: false,
        reason: "stream_not_observable",
        streamId: "map:1001:aoi:default.cell_9_0",
        deltas: []
      })
    });
  }
});
const rejectedRecover = await recoverStateSyncDetailed(
  rejectedRecoverClient,
  new YuoStateSyncReceiver(),
  "web-player",
  1n,
  "map:1001:aoi:default.cell_9_0");
assert.equal(rejectedRecover.replayed, false);
assert.equal(rejectedRecover.reason, "stream_not_observable");
assert.equal(rejectedRecover.streamId, "map:1001:aoi:default.cell_9_0");
assert.equal(rejectedRecover.results.length, 0);

const rejectedMultiRecover = await recoverStateSyncMultiStreamDetailed(
  rejectedRecoverClient,
  new YuoStateSyncMultiStreamReceiver(),
  "web-player",
  "map:1001:aoi:default.cell_9_0",
  1n);
assert.equal(rejectedMultiRecover.replayed, false);
assert.equal(rejectedMultiRecover.reason, "stream_not_observable");
assert.equal(rejectedMultiRecover.streamId, "map:1001:aoi:default.cell_9_0");
assert.equal(rejectedMultiRecover.results.length, 0);

const ackResponse = await mmoClient.ackStateSync("web-player", 104n);
assert.equal(ackResponse.accepted, true);
assert.equal(ackResponse.ackTick, 104n);
assert.equal(ackResponse.latestTick, 104n);
assert.equal(ackResponse.streamId, "player:web-player");

assert.throws(
  () => session.decodeResponse(
    descriptor,
    encodeServerEnvelope({
      opcode: YuoProtocolOpCodes.welcome,
      requestId: 99n,
      serverSeq: 8n,
      payload: textCodec.encode({ text: "wrong" })
    }),
    1n
  ),
  error => error instanceof YuoClientProtocolError && error.code === "request_mismatch"
);

assert.throws(
  () => session.decodeResponse(
    descriptor,
    encodeServerEnvelope({
      opcode: YuoProtocolOpCodes.errorResponse,
      requestId: 1n,
      serverSeq: 9n,
      error: { code: "bad_request", message: "expected smoke error" },
      payload: new Uint8Array()
    }),
    1n
  ),
  error => error instanceof YuoClientProtocolError && error.code === "bad_request"
);

let timeoutSignalObserved = false;
const hangingTransport = {
  send(_request, signal) {
    return new Promise((_resolve, reject) => {
      signal?.addEventListener("abort", () => {
        timeoutSignalObserved = signal.aborted;
        reject(signal.reason ?? new Error("request aborted"));
      }, { once: true });
    });
  }
};

await assert.rejects(
  () => session.call(hangingTransport, descriptor, { text: "timeout" }, { timeoutMs: 1 }),
  error => error instanceof Error && /timed out|aborted/.test(error.message)
);
assert.equal(timeoutSignalObserved, true);

class FakeWebSocket {
  binaryType = "blob";
  listeners = new Map();
  readyState = WebSocket.OPEN;
  closed = false;
  closeCode = 0;
  closeReason = "";

  addEventListener(type, handler) {
    const handlers = this.listeners.get(type) ?? [];
    handlers.push(handler);
    this.listeners.set(type, handlers);
  }

  emit(type, data) {
    for (const handler of this.listeners.get(type) ?? []) {
      handler({ data });
    }
  }

  send() {
  }

  close(code, reason) {
    this.closed = true;
    this.closeCode = code;
    this.closeReason = reason;
    this.readyState = WebSocket.CLOSED;
  }
}

const fakeSocket = new FakeWebSocket();
const webSocketTransport = new YuoWebSocketTransport(fakeSocket);
const receivedPushes = [];
const unsubscribePush = webSocketTransport.subscribePush((message, bytes) => {
  receivedPushes.push({ message, bytes });
});
const pushBytes = encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.stateDelta,
  requestId: 0n,
  serverSeq: 900n,
  payload: encodeStateDelta({
    tick: 900n,
    changes: [
      {
        kind: YuoStateChangeKind.update,
        networkId: 2001n,
        state: { networkId: 2001n, kind: "player", version: 2, payload: new Uint8Array([8]) }
      }
    ]
  })
});
fakeSocket.emit("message", pushBytes);
assert.equal(receivedPushes.length, 1);
assert.equal(receivedPushes[0].message.requestId, 0n);
assert.equal(receivedPushes[0].message.opcode, YuoProtocolOpCodes.stateDelta);
assert.deepEqual(receivedPushes[0].bytes, pushBytes);
unsubscribePush();
fakeSocket.emit("message", pushBytes);
assert.equal(receivedPushes.length, 1);
webSocketTransport.close(1000, "smoke done");
assert.equal(fakeSocket.closed, true);
assert.equal(fakeSocket.closeCode, 1000);
assert.equal(fakeSocket.closeReason, "smoke done");

const sessionTransports = [];
const managedSession = new YuoMmoWebSocketSession({
  url: "ws://example.test/ws",
  requestTimeoutMs: 5000,
  transportFactory: async (url, protocols) => {
    const transport = {
      closed: false,
      url,
      protocols,
      closeCode: 0,
      closeReason: "",
      async send(request) {
        const envelope = decodeClientEnvelopeV1(request);
        switch (envelope.opcode) {
          case YuoProtocolOpCodes.helloRequest: {
            const hello = decodeHelloRequest(envelope.payload);
            return encodeServerEnvelope({
              opcode: YuoProtocolOpCodes.welcome,
              requestId: envelope.requestId,
              serverSeq: 950n,
              payload: encodeWelcome({ playerId: hello.playerId || "web-player", message: "ok" })
            });
          }
          case YuoProtocolOpCodes.snapshotRequest: {
            const snapshot = decodeSnapshotRequest(envelope.payload);
            return encodeServerEnvelope({
              opcode: YuoProtocolOpCodes.playerSnapshot,
              requestId: envelope.requestId,
              serverSeq: 951n,
              payload: encodePlayerSnapshot(makePlayerSnapshot({ playerId: snapshot.playerId, gold: 25n }))
            });
          }
          case YuoProtocolOpCodes.disconnectRequest: {
            const disconnect = decodeDisconnectRequest(envelope.payload);
            return encodeServerEnvelope({
              opcode: YuoProtocolOpCodes.playerSnapshot,
              requestId: envelope.requestId,
              serverSeq: 952n,
              payload: encodePlayerSnapshot(makePlayerSnapshot({ playerId: disconnect.playerId, online: false }))
            });
          }
          default:
            throw new Error(`unexpected managed session opcode: ${envelope.opcode}`);
        }
      },
      close(code = 1000, reason = "client closed") {
        this.closed = true;
        this.closeCode = code;
        this.closeReason = reason;
      }
    };
    sessionTransports.push(transport);
    return transport;
  }
});
const managedWelcome = await managedSession.connect();
assert.equal(managedWelcome.playerId, "web-player");
assert.equal(managedSession.playerId, "web-player");
assert.equal(sessionTransports[0].url, "ws://example.test/ws");
assert.equal(sessionTransports[0].protocols, "yuocore.v1");
const managedSnapshot = await managedSession.client.getSnapshot("web-player");
assert.equal(managedSnapshot.gold, 25n);
const managedReconnect = await managedSession.reconnect();
assert.equal(managedReconnect.playerId, "web-player");
assert.equal(sessionTransports[0].closed, true);
assert.equal(sessionTransports.length, 2);
const managedDisconnected = await managedSession.disconnect();
assert.equal(managedDisconnected.online, false);
assert.equal(sessionTransports[1].closed, true);

const stateSyncSocket = new FakeWebSocket();
const stateSyncTransport = new YuoWebSocketTransport(stateSyncSocket);
const subscribedResults = [];
const stateSyncSubscription = subscribeStateSyncPushes(
  stateSyncTransport,
  new YuoStateSyncReceiver(),
  result => subscribedResults.push(result));
stateSyncSocket.emit("message", encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.stateSnapshot,
  requestId: 0n,
  serverSeq: 901n,
  payload: encodeStateSnapshot({
    tick: 901n,
    entities: [
      { networkId: 3001n, kind: "player", version: 1, payload: new Uint8Array([1]) }
    ]
  })
}));
assert.equal(subscribedResults.length, 1);
assert.equal(subscribedResults[0].kind, YuoStateSyncPushKind.snapshot);
assert.equal(stateSyncSubscription.receiver.mirror.tryGet(3001n)?.version, 1);
stateSyncSubscription.unsubscribe();
stateSyncSocket.emit("message", encodeServerEnvelope({
  opcode: YuoProtocolOpCodes.stateDelta,
  requestId: 0n,
  serverSeq: 902n,
  payload: encodeStateDelta({
    tick: 902n,
    changes: [
      {
        kind: YuoStateChangeKind.update,
        networkId: 3001n,
        state: { networkId: 3001n, kind: "player", version: 2, payload: new Uint8Array([2]) }
      }
    ]
  })
}));
assert.equal(subscribedResults.length, 1);
assert.equal(stateSyncSubscription.receiver.mirror.tryGet(3001n)?.version, 1);

console.log("yuocore-client-ts smoke passed");

function makePlayerSnapshot(overrides = {}) {
  return {
    playerId: "web-player",
    name: "Web Player",
    gold: 10n,
    exp: 20n,
    level: 2,
    power: 30n,
    clicks: 0n,
    updatedUnixMs: 1780900000000n,
    mapId: 2n,
    unitId: 2001n,
    x: 0,
    y: 0,
    z: 0,
    online: true,
    lastInputSeq: 0n,
    ...overrides
  };
}

function makeWorldSnapshot({ x, y, z, kind }) {
  const player = {
    playerId: "web-player",
    name: "Web Player",
    mapId: 2n,
    unitId: 2001n,
    x,
    y,
    z,
    online: true,
    lastInputSeq: 9n,
    updatedUnixMs: 1780900000001n
  };

  return {
    mapId: 2n,
    tick: 88n,
    players: [player],
    events: [
      {
        kind,
        player,
        createdUnixMs: 1780900000002n
      }
    ]
  };
}

function encodeServerEnvelope(envelope) {
  const writer = new ProtoWriter();
  writer.uint32(1, 1n);
  writer.uint32(2, BigInt(envelope.opcode));
  writer.uint64(3, envelope.requestId);
  writer.uint64(4, envelope.serverSeq);
  if (envelope.error) {
    writer.bytes(5, encodeErrorResponse(envelope.error));
  }

  writer.bytes(6, envelope.payload);
  return writer.finish();
}

function encodeErrorResponse(error) {
  const writer = new ProtoWriter();
  writer.string(1, error.code);
  writer.string(2, error.message);
  return writer.finish();
}

function encodeStateSnapshot(snapshot) {
  const writer = new ProtoWriter();
  writer.uint64(1, snapshot.tick);
  for (const entity of snapshot.entities) {
    writer.bytes(2, encodeNetworkEntityState(entity));
  }

  if (snapshot.streamId) {
    writer.string(3, snapshot.streamId);
  }

  return writer.finish();
}

function encodeStateDelta(delta) {
  const writer = new ProtoWriter();
  writer.uint64(1, delta.tick);
  for (const change of delta.changes) {
    writer.bytes(2, encodeStateChange(change));
  }

  if (delta.baseTick !== undefined) {
    writer.uint64(3, delta.baseTick);
  }

  if (delta.streamId) {
    writer.string(4, delta.streamId);
  }

  return writer.finish();
}

function encodeStateChange(change) {
  const writer = new ProtoWriter();
  writer.uint32(1, BigInt(change.kind));
  if (change.state) {
    writer.bytes(2, encodeNetworkEntityState(change.state));
  }

  writer.uint64(3, change.networkId);
  return writer.finish();
}

function encodeNetworkEntityState(entity) {
  const writer = new ProtoWriter();
  writer.uint64(1, entity.networkId);
  writer.string(2, entity.kind);
  writer.uint32(3, BigInt(entity.version));
  writer.bytes(4, entity.payload);
  return writer.finish();
}
