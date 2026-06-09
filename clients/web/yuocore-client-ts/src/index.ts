export const YUO_PROTOCOL_VERSION = 1;

export const YuoProtocolOpCodes = {
  helloRequest: 1,
  clickRequest: 2,
  snapshotRequest: 3,
  pingRequest: 4,
  enterMapRequest: 5,
  moveRequest: 6,
  disconnectRequest: 7,
  stateSnapshotRequest: 8,
  stateSyncRecoverRequest: 9,
  stateSyncAckRequest: 10,
  welcome: 101,
  playerSnapshot: 102,
  errorResponse: 103,
  pongResponse: 104,
  worldSnapshot: 105,
  stateSnapshot: 106,
  stateDelta: 107,
  stateSyncRecover: 108,
  stateSyncAck: 109,
  stateSubscription: 110
} as const;

export interface YuoMessageCodec<T> {
  encode(value: T): Uint8Array;
  decode(bytes: Uint8Array): T;
}

export interface YuoClientMessageDescriptor<TRequest, TResponse> {
  requestOpCode: number;
  responseOpCode: number;
  request: YuoMessageCodec<TRequest>;
  response: YuoMessageCodec<TResponse>;
}

export interface YuoClientTransport {
  send(request: Uint8Array, signal?: AbortSignal): Promise<Uint8Array>;
}

export type YuoServerPushHandler = (message: YuoServerEnvelopeV1, bytes: Uint8Array) => void;

export interface YuoClientCallOptions {
  signal?: AbortSignal;
  timeoutMs?: number;
}

export interface YuoClientOutboundMessage {
  requestId: bigint;
  clientSeq: bigint;
  opCode: number;
  bytes: Uint8Array;
}

export interface YuoClientInboundMessage<TResponse> {
  requestId: bigint;
  serverSeq: bigint;
  opCode: number;
  payload: TResponse;
}

export interface YuoClientEnvelopeV1 {
  version: number;
  opcode: number;
  requestId: bigint;
  clientSeq: bigint;
  payload: Uint8Array;
}

export interface YuoServerEnvelopeV1 {
  version: number;
  opcode: number;
  requestId: bigint;
  serverSeq: bigint;
  error?: YuoErrorResponse;
  payload: Uint8Array;
}

export interface YuoErrorResponse {
  code: string;
  message: string;
}

export interface YuoNetworkEntityState {
  networkId: bigint;
  kind: string;
  version: number;
  payload: Uint8Array;
}

export interface YuoStateSnapshot {
  tick: bigint;
  entities: YuoNetworkEntityState[];
  streamId?: string;
}

export const YuoStateChangeKind = {
  unknown: 0,
  enter: 1,
  update: 2,
  leave: 3,
  destroy: 4
} as const;

export type YuoStateChangeKindValue = typeof YuoStateChangeKind[keyof typeof YuoStateChangeKind];

export interface YuoStateChange {
  kind: YuoStateChangeKindValue;
  networkId: bigint;
  state?: YuoNetworkEntityState;
}

export interface YuoStateDelta {
  tick: bigint;
  changes: YuoStateChange[];
  baseTick?: bigint;
  streamId?: string;
}

export interface YuoStateSubscription {
  streamIds: string[];
  reason: string;
  resetMissing: boolean;
}

export const YuoStateSyncApplyStatus = {
  applied: "applied",
  snapshotRequired: "snapshot_required",
  rejected: "rejected"
} as const;

export type YuoStateSyncApplyStatusValue =
  typeof YuoStateSyncApplyStatus[keyof typeof YuoStateSyncApplyStatus];

export const YuoStateSyncApplyReason = {
  none: "none",
  gapDetected: "gap_detected",
  snapshotAlreadyRequired: "snapshot_already_required",
  staleDelta: "stale_delta",
  invalidDelta: "invalid_delta",
  staleSnapshot: "stale_snapshot",
  invalidSnapshot: "invalid_snapshot"
} as const;

export type YuoStateSyncApplyReasonValue =
  typeof YuoStateSyncApplyReason[keyof typeof YuoStateSyncApplyReason];

export interface YuoStateSyncApplyResult {
  status: YuoStateSyncApplyStatusValue;
  reason: YuoStateSyncApplyReasonValue;
  lastAppliedTick?: bigint;
  incomingTick?: bigint;
  message?: string;
}

export const YuoStateSyncPushKind = {
  ignored: "ignored",
  snapshot: "snapshot",
  delta: "delta",
  subscription: "subscription"
} as const;

export type YuoStateSyncPushKindValue = typeof YuoStateSyncPushKind[keyof typeof YuoStateSyncPushKind];

export interface YuoStateSyncPushResult {
  kind: YuoStateSyncPushKindValue;
  opcode: number;
  requestId: bigint;
  serverSeq: bigint;
  tick?: bigint;
  streamId?: string;
  applyResult?: YuoStateSyncApplyResult;
}

export interface YuoHelloRequest {
  playerId?: string;
}

export interface YuoWelcome {
  playerId: string;
  message: string;
}

export interface YuoClickRequest {
  playerId: string;
  amount: bigint | number;
}

export interface YuoSnapshotRequest {
  playerId: string;
}

export interface YuoEnterMapRequest {
  playerId: string;
  mapId: bigint | number;
}

export interface YuoMoveRequest {
  playerId: string;
  inputSeq: bigint | number;
  x: number;
  y: number;
  z: number;
}

export interface YuoDisconnectRequest {
  playerId: string;
  sessionId: string;
}

export interface YuoStateSnapshotRequest {
  playerId: string;
  knownTick?: bigint | number;
}

export interface YuoStateSyncRecoverRequest {
  playerId: string;
  knownTick?: bigint | number;
  streamId?: string;
}

export interface YuoStateSyncRecoverResponse {
  replayed: boolean;
  reason: string;
  snapshot?: YuoStateSnapshot;
  deltas: YuoStateDelta[];
  streamId?: string;
}

export interface YuoStateSyncRecoverApplyResult {
  replayed: boolean;
  reason: string;
  streamId?: string;
  results: YuoStateSyncPushResult[];
}

export interface YuoStateSyncAckRequest {
  playerId: string;
  ackTick: bigint | number;
  streamId?: string;
}

export interface YuoStateSyncAckResponse {
  accepted: boolean;
  ackTick: bigint;
  latestTick: bigint;
  reason: string;
  streamId?: string;
}

export interface YuoPlayerSnapshot {
  playerId: string;
  name: string;
  gold: bigint;
  exp: bigint;
  level: number;
  power: bigint;
  clicks: bigint;
  updatedUnixMs: bigint;
  mapId: bigint;
  unitId: bigint;
  x: number;
  y: number;
  z: number;
  online: boolean;
  lastInputSeq: bigint;
}

export interface YuoPlayerWorldState {
  playerId: string;
  name: string;
  mapId: bigint;
  unitId: bigint;
  x: number;
  y: number;
  z: number;
  online: boolean;
  lastInputSeq: bigint;
  updatedUnixMs: bigint;
}

export interface YuoWorldEvent {
  kind: string;
  player?: YuoPlayerWorldState;
  createdUnixMs: bigint;
}

export interface YuoWorldSnapshot {
  mapId: bigint;
  tick: bigint;
  players: YuoPlayerWorldState[];
  events: YuoWorldEvent[];
}

export const YuoMmoDescriptors = {
  hello: {
    requestOpCode: YuoProtocolOpCodes.helloRequest,
    responseOpCode: YuoProtocolOpCodes.welcome,
    request: { encode: encodeHelloRequest, decode: decodeHelloRequest },
    response: { encode: encodeWelcome, decode: decodeWelcome }
  } satisfies YuoClientMessageDescriptor<YuoHelloRequest, YuoWelcome>,
  click: {
    requestOpCode: YuoProtocolOpCodes.clickRequest,
    responseOpCode: YuoProtocolOpCodes.playerSnapshot,
    request: { encode: encodeClickRequest, decode: decodeClickRequest },
    response: { encode: encodePlayerSnapshot, decode: decodePlayerSnapshot }
  } satisfies YuoClientMessageDescriptor<YuoClickRequest, YuoPlayerSnapshot>,
  snapshot: {
    requestOpCode: YuoProtocolOpCodes.snapshotRequest,
    responseOpCode: YuoProtocolOpCodes.playerSnapshot,
    request: { encode: encodeSnapshotRequest, decode: decodeSnapshotRequest },
    response: { encode: encodePlayerSnapshot, decode: decodePlayerSnapshot }
  } satisfies YuoClientMessageDescriptor<YuoSnapshotRequest, YuoPlayerSnapshot>,
  enterMap: {
    requestOpCode: YuoProtocolOpCodes.enterMapRequest,
    responseOpCode: YuoProtocolOpCodes.worldSnapshot,
    request: { encode: encodeEnterMapRequest, decode: decodeEnterMapRequest },
    response: { encode: encodeWorldSnapshot, decode: decodeWorldSnapshot }
  } satisfies YuoClientMessageDescriptor<YuoEnterMapRequest, YuoWorldSnapshot>,
  move: {
    requestOpCode: YuoProtocolOpCodes.moveRequest,
    responseOpCode: YuoProtocolOpCodes.worldSnapshot,
    request: { encode: encodeMoveRequest, decode: decodeMoveRequest },
    response: { encode: encodeWorldSnapshot, decode: decodeWorldSnapshot }
  } satisfies YuoClientMessageDescriptor<YuoMoveRequest, YuoWorldSnapshot>,
  disconnect: {
    requestOpCode: YuoProtocolOpCodes.disconnectRequest,
    responseOpCode: YuoProtocolOpCodes.playerSnapshot,
    request: { encode: encodeDisconnectRequest, decode: decodeDisconnectRequest },
    response: { encode: encodePlayerSnapshot, decode: decodePlayerSnapshot }
  } satisfies YuoClientMessageDescriptor<YuoDisconnectRequest, YuoPlayerSnapshot>,
  stateSnapshot: {
    requestOpCode: YuoProtocolOpCodes.stateSnapshotRequest,
    responseOpCode: YuoProtocolOpCodes.stateSnapshot,
    request: { encode: encodeStateSnapshotRequest, decode: decodeStateSnapshotRequest },
    response: { encode: encodeStateSnapshotMessage, decode: decodeStateSnapshotMessage }
  } satisfies YuoClientMessageDescriptor<YuoStateSnapshotRequest, YuoStateSnapshot>,
  stateSyncRecover: {
    requestOpCode: YuoProtocolOpCodes.stateSyncRecoverRequest,
    responseOpCode: YuoProtocolOpCodes.stateSyncRecover,
    request: { encode: encodeStateSyncRecoverRequest, decode: decodeStateSyncRecoverRequest },
    response: { encode: encodeStateSyncRecoverResponse, decode: decodeStateSyncRecoverResponse }
  } satisfies YuoClientMessageDescriptor<YuoStateSyncRecoverRequest, YuoStateSyncRecoverResponse>,
  stateSyncAck: {
    requestOpCode: YuoProtocolOpCodes.stateSyncAckRequest,
    responseOpCode: YuoProtocolOpCodes.stateSyncAck,
    request: { encode: encodeStateSyncAckRequest, decode: decodeStateSyncAckRequest },
    response: { encode: encodeStateSyncAckResponse, decode: decodeStateSyncAckResponse }
  } satisfies YuoClientMessageDescriptor<YuoStateSyncAckRequest, YuoStateSyncAckResponse>
} as const;

export class YuoClientProtocolError extends Error {
  public readonly code: string;

  public constructor(code: string, message: string) {
    super(message);
    this.name = "YuoClientProtocolError";
    this.code = code || "protocol_error";
  }
}

export class YuoStateSyncMirror {
  private readonly statesByNetworkId = new Map<string, YuoNetworkEntityState>();

  public lastTick?: bigint;
  public snapshotRequired = false;
  public lastGap?: { lastAppliedTick?: bigint; expectedBaseTick: bigint; deltaTick: bigint };

  public get states(): ReadonlyMap<string, YuoNetworkEntityState> {
    return this.statesByNetworkId;
  }

  public applySnapshot(snapshot: YuoStateSnapshot): YuoStateSyncApplyResult {
    if (this.lastTick !== undefined && snapshot.tick < this.lastTick) {
      return this.result(
        YuoStateSyncApplyStatus.rejected,
        YuoStateSyncApplyReason.staleSnapshot,
        snapshot.tick,
        "state snapshot tick is older than local mirror tick."
      );
    }

    if (this.snapshotRequired && this.lastGap && snapshot.tick < this.lastGap.expectedBaseTick) {
      return this.result(
        YuoStateSyncApplyStatus.snapshotRequired,
        YuoStateSyncApplyReason.staleSnapshot,
        snapshot.tick,
        "state snapshot does not advance past the gap point."
      );
    }

    const seen = new Set<string>();
    for (const state of snapshot.entities) {
      const key = state.networkId.toString();
      if (seen.has(key)) {
        return this.result(
          YuoStateSyncApplyStatus.rejected,
          YuoStateSyncApplyReason.invalidSnapshot,
          snapshot.tick,
          `state snapshot contains duplicate network id: ${state.networkId}.`
        );
      }

      seen.add(key);
    }

    this.statesByNetworkId.clear();
    for (const state of snapshot.entities) {
      this.statesByNetworkId.set(state.networkId.toString(), state);
    }

    this.lastTick = snapshot.tick;
    this.snapshotRequired = false;
    this.lastGap = undefined;
    return this.result(YuoStateSyncApplyStatus.applied, YuoStateSyncApplyReason.none, snapshot.tick);
  }

  public applyDelta(delta: YuoStateDelta): YuoStateSyncApplyResult {
    if (this.snapshotRequired) {
      return this.result(
        YuoStateSyncApplyStatus.snapshotRequired,
        YuoStateSyncApplyReason.snapshotAlreadyRequired,
        delta.tick,
        "state snapshot is required before applying more deltas."
      );
    }

    if (this.lastTick !== undefined && delta.tick <= this.lastTick) {
      return this.result(
        YuoStateSyncApplyStatus.rejected,
        YuoStateSyncApplyReason.staleDelta,
        delta.tick,
        "state delta tick must increase."
      );
    }

    if (delta.baseTick !== undefined && this.lastTick !== delta.baseTick) {
      this.snapshotRequired = true;
      this.lastGap = {
        lastAppliedTick: this.lastTick,
        expectedBaseTick: delta.baseTick,
        deltaTick: delta.tick
      };
      return this.result(
        YuoStateSyncApplyStatus.snapshotRequired,
        YuoStateSyncApplyReason.gapDetected,
        delta.tick,
        "state delta base tick mismatch."
      );
    }

    for (const change of delta.changes) {
      switch (change.kind) {
        case YuoStateChangeKind.enter:
        case YuoStateChangeKind.update:
          if (!change.state) {
            return this.result(
              YuoStateSyncApplyStatus.rejected,
              YuoStateSyncApplyReason.invalidDelta,
              delta.tick,
              `${change.kind} change must carry entity state: ${change.networkId}.`
            );
          }

          if (change.state.networkId !== change.networkId) {
            return this.result(
              YuoStateSyncApplyStatus.rejected,
              YuoStateSyncApplyReason.invalidDelta,
              delta.tick,
              `state change network id mismatch: ${change.networkId}.`
            );
          }

          break;
        case YuoStateChangeKind.leave:
        case YuoStateChangeKind.destroy:
          break;
        default:
          return this.result(
            YuoStateSyncApplyStatus.rejected,
            YuoStateSyncApplyReason.invalidDelta,
            delta.tick,
            `unknown state change kind: ${change.kind}.`
          );
      }
    }

    for (const change of delta.changes) {
      const key = change.networkId.toString();
      if (change.kind === YuoStateChangeKind.enter || change.kind === YuoStateChangeKind.update) {
        this.statesByNetworkId.set(key, change.state!);
      } else {
        this.statesByNetworkId.delete(key);
      }
    }

    this.lastTick = delta.tick;
    return this.result(YuoStateSyncApplyStatus.applied, YuoStateSyncApplyReason.none, delta.tick);
  }

  public tryGet(networkId: bigint): YuoNetworkEntityState | undefined {
    return this.statesByNetworkId.get(networkId.toString());
  }

  public clear(): void {
    this.statesByNetworkId.clear();
    this.lastTick = undefined;
    this.snapshotRequired = false;
    this.lastGap = undefined;
  }

  private result(
    status: YuoStateSyncApplyStatusValue,
    reason: YuoStateSyncApplyReasonValue,
    incomingTick?: bigint,
    message?: string
  ): YuoStateSyncApplyResult {
    return {
      status,
      reason,
      lastAppliedTick: this.lastTick,
      incomingTick,
      message
    };
  }
}

export interface YuoStateSyncStreamCursor {
  streamId: string;
  lastTick?: bigint;
  snapshotRequired: boolean;
}

export class YuoStateSyncStreamMirrorSet {
  private readonly mirrorsByStreamId = new Map<string, YuoStateSyncMirror>();

  public get count(): number {
    return this.mirrorsByStreamId.size;
  }

  public get streamIds(): string[] {
    return [...this.mirrorsByStreamId.keys()];
  }

  public getOrCreate(streamId: string): YuoStateSyncMirror {
    const normalized = normalizeStateSyncStreamId(streamId);
    let mirror = this.mirrorsByStreamId.get(normalized);
    if (!mirror) {
      mirror = new YuoStateSyncMirror();
      this.mirrorsByStreamId.set(normalized, mirror);
    }

    return mirror;
  }

  public tryGet(streamId: string): YuoStateSyncMirror | undefined {
    return this.mirrorsByStreamId.get(normalizeStateSyncStreamId(streamId));
  }

  public applySnapshot(streamId: string, snapshot: YuoStateSnapshot): YuoStateSyncApplyResult {
    return this.getOrCreate(streamId).applySnapshot(snapshot);
  }

  public applyDelta(streamId: string, delta: YuoStateDelta): YuoStateSyncApplyResult {
    return this.getOrCreate(streamId).applyDelta(delta);
  }

  public getCursor(streamId: string): YuoStateSyncStreamCursor {
    const normalized = normalizeStateSyncStreamId(streamId);
    const mirror = this.mirrorsByStreamId.get(normalized);
    return {
      streamId: normalized,
      lastTick: mirror?.lastTick,
      snapshotRequired: mirror?.snapshotRequired ?? false
    };
  }

  public remove(streamId: string): boolean {
    return this.mirrorsByStreamId.delete(normalizeStateSyncStreamId(streamId));
  }

  public clear(): void {
    this.mirrorsByStreamId.clear();
  }
}

export class YuoStateSyncReceiver {
  public lastStreamId?: string;

  public constructor(public readonly mirror = new YuoStateSyncMirror()) {
  }

  public applyServerEnvelope(bytesOrEnvelope: Uint8Array | YuoServerEnvelopeV1): YuoStateSyncPushResult {
    const envelope = bytesOrEnvelope instanceof Uint8Array
      ? decodeServerEnvelopeV1(bytesOrEnvelope)
      : bytesOrEnvelope;

    if (envelope.error && envelope.error.code.length > 0) {
      throw new YuoClientProtocolError(envelope.error.code, envelope.error.message);
    }

    switch (envelope.opcode) {
      case YuoProtocolOpCodes.stateSnapshot: {
        const snapshot = decodeStateSnapshotMessage(envelope.payload);
        return this.applyStateSnapshot(snapshot, envelope.requestId, envelope.serverSeq);
      }
      case YuoProtocolOpCodes.stateDelta: {
        const delta = decodeStateDeltaMessage(envelope.payload);
        return this.applyStateDelta(delta, envelope.requestId, envelope.serverSeq);
      }
      default:
        return {
          kind: YuoStateSyncPushKind.ignored,
          opcode: envelope.opcode,
          requestId: envelope.requestId,
          serverSeq: envelope.serverSeq
        };
    }
  }

  public applyStateSnapshot(
    snapshot: YuoStateSnapshot,
    requestId = 0n,
    serverSeq = 0n
  ): YuoStateSyncPushResult {
    const applyResult = this.mirror.applySnapshot(snapshot);
    if (snapshot.streamId && snapshot.streamId.trim().length > 0) {
      this.lastStreamId = snapshot.streamId;
    }

    return {
      kind: YuoStateSyncPushKind.snapshot,
      opcode: YuoProtocolOpCodes.stateSnapshot,
      requestId,
      serverSeq,
      tick: snapshot.tick,
      streamId: snapshot.streamId || undefined,
      applyResult
    };
  }

  public applyStateDelta(
    delta: YuoStateDelta,
    requestId = 0n,
    serverSeq = 0n
  ): YuoStateSyncPushResult {
    const applyResult = this.mirror.applyDelta(delta);
    if (delta.streamId && delta.streamId.trim().length > 0) {
      this.lastStreamId = delta.streamId;
    }

    return {
      kind: YuoStateSyncPushKind.delta,
      opcode: YuoProtocolOpCodes.stateDelta,
      requestId,
      serverSeq,
      tick: delta.tick,
      streamId: delta.streamId || undefined,
      applyResult
    };
  }

  public clear(): void {
    this.mirror.clear();
    this.lastStreamId = undefined;
  }
}

export class YuoStateSyncMultiStreamReceiver {
  public constructor(
    public readonly streams = new YuoStateSyncStreamMirrorSet(),
    private readonly defaultStreamId = ""
  ) {
  }

  public applyServerEnvelope(bytesOrEnvelope: Uint8Array | YuoServerEnvelopeV1): YuoStateSyncPushResult {
    const envelope = bytesOrEnvelope instanceof Uint8Array
      ? decodeServerEnvelopeV1(bytesOrEnvelope)
      : bytesOrEnvelope;

    if (envelope.error && envelope.error.code.length > 0) {
      throw new YuoClientProtocolError(envelope.error.code, envelope.error.message);
    }

    switch (envelope.opcode) {
      case YuoProtocolOpCodes.stateSnapshot:
        return this.applyStateSnapshot(decodeStateSnapshotMessage(envelope.payload), envelope.requestId, envelope.serverSeq);
      case YuoProtocolOpCodes.stateDelta:
        return this.applyStateDelta(decodeStateDeltaMessage(envelope.payload), envelope.requestId, envelope.serverSeq);
      case YuoProtocolOpCodes.stateSubscription:
        return this.applyStateSubscription(decodeStateSubscriptionMessage(envelope.payload), envelope.requestId, envelope.serverSeq);
      default:
        return {
          kind: YuoStateSyncPushKind.ignored,
          opcode: envelope.opcode,
          requestId: envelope.requestId,
          serverSeq: envelope.serverSeq
        };
    }
  }

  public applyStateSnapshot(
    snapshot: YuoStateSnapshot,
    requestId = 0n,
    serverSeq = 0n
  ): YuoStateSyncPushResult {
    const streamId = this.resolveStreamId(snapshot.streamId);
    const applyResult = this.streams.applySnapshot(streamId, snapshot);
    return {
      kind: YuoStateSyncPushKind.snapshot,
      opcode: YuoProtocolOpCodes.stateSnapshot,
      requestId,
      serverSeq,
      tick: snapshot.tick,
      streamId,
      applyResult
    };
  }

  public applyStateDelta(
    delta: YuoStateDelta,
    requestId = 0n,
    serverSeq = 0n
  ): YuoStateSyncPushResult {
    const streamId = this.resolveStreamId(delta.streamId);
    const applyResult = this.streams.applyDelta(streamId, delta);
    return {
      kind: YuoStateSyncPushKind.delta,
      opcode: YuoProtocolOpCodes.stateDelta,
      requestId,
      serverSeq,
      tick: delta.tick,
      streamId,
      applyResult
    };
  }

  public applyStateSubscription(
    subscription: YuoStateSubscription,
    requestId = 0n,
    serverSeq = 0n
  ): YuoStateSyncPushResult {
    if (subscription.resetMissing) {
      const retained = new Set(
        subscription.streamIds
          .filter((streamId) => streamId.trim().length > 0)
          .map((streamId) => normalizeStateSyncStreamId(streamId))
      );
      for (const streamId of this.streams.streamIds) {
        if (!retained.has(streamId)) {
          this.streams.remove(streamId);
        }
      }
    }

    return {
      kind: YuoStateSyncPushKind.subscription,
      opcode: YuoProtocolOpCodes.stateSubscription,
      requestId,
      serverSeq
    };
  }

  public clear(): void {
    this.streams.clear();
  }

  private resolveStreamId(streamId?: string): string {
    if (streamId && streamId.trim().length > 0) {
      return streamId;
    }

    if (this.defaultStreamId.trim().length > 0) {
      return this.defaultStreamId;
    }

    throw new Error("StateSync stream id is required for multi-stream receiver.");
  }
}

export interface YuoPushSubscribable {
  subscribePush(handler: YuoServerPushHandler): () => void;
}

export interface YuoStateSyncSubscription {
  receiver: YuoStateSyncReceiver;
  unsubscribe(): void;
}

export function subscribeStateSyncPushes(
  transport: YuoPushSubscribable,
  receiver = new YuoStateSyncReceiver(),
  onReceive?: (result: YuoStateSyncPushResult) => void
): YuoStateSyncSubscription {
  const unsubscribe = transport.subscribePush(message => {
    const result = receiver.applyServerEnvelope(message);
    if (result.kind !== YuoStateSyncPushKind.ignored) {
      onReceive?.(result);
    }
  });

  return { receiver, unsubscribe };
}

export function shouldRequestStateSnapshot(result: YuoStateSyncPushResult): boolean {
  return result.applyResult?.status === YuoStateSyncApplyStatus.snapshotRequired;
}

export function shouldAckStateSync(result: YuoStateSyncPushResult): boolean {
  return result.kind !== YuoStateSyncPushKind.ignored &&
    result.tick !== undefined &&
    result.tick > 0n &&
    result.applyResult?.status === YuoStateSyncApplyStatus.applied;
}

export async function ackStateSyncIfApplied(
  client: YuoMmoClient,
  playerId: string,
  result: YuoStateSyncPushResult,
  options?: AbortSignal | YuoClientCallOptions
): Promise<YuoStateSyncAckResponse | undefined> {
  if (!shouldAckStateSync(result)) {
    return undefined;
  }

  return client.ackStateSync(playerId, result.tick!, result.streamId ?? "", options);
}

export async function recoverStateSyncSnapshot(
  client: YuoMmoClient,
  receiver: YuoStateSyncReceiver,
  playerId: string,
  knownTick: bigint | number = receiver.mirror.lastTick ?? 0n,
  options?: AbortSignal | YuoClientCallOptions
): Promise<YuoStateSyncPushResult> {
  const snapshot = await client.requestStateSnapshot(playerId, knownTick, options);
  return receiver.applyStateSnapshot(snapshot, 0n, snapshot.tick);
}

export async function recoverStateSync(
  client: YuoMmoClient,
  receiver: YuoStateSyncReceiver,
  playerId: string,
  knownTick: bigint | number = receiver.mirror.lastTick ?? 0n,
  streamIdOrOptions: string | AbortSignal | YuoClientCallOptions = receiver.lastStreamId ?? "",
  options?: AbortSignal | YuoClientCallOptions
): Promise<YuoStateSyncPushResult[]> {
  return (await recoverStateSyncDetailed(
    client,
    receiver,
    playerId,
    knownTick,
    streamIdOrOptions,
    options
  )).results;
}

export async function recoverStateSyncDetailed(
  client: YuoMmoClient,
  receiver: YuoStateSyncReceiver,
  playerId: string,
  knownTick: bigint | number = receiver.mirror.lastTick ?? 0n,
  streamIdOrOptions: string | AbortSignal | YuoClientCallOptions = receiver.lastStreamId ?? "",
  options?: AbortSignal | YuoClientCallOptions
): Promise<YuoStateSyncRecoverApplyResult> {
  const streamId = typeof streamIdOrOptions === "string"
    ? streamIdOrOptions
    : receiver.lastStreamId ?? "";
  const callOptions = typeof streamIdOrOptions === "string"
    ? options
    : streamIdOrOptions;
  const response = await client.recoverStateSync(playerId, knownTick, streamId, callOptions);
  const results: YuoStateSyncPushResult[] = [];
  const responseStreamId = response.streamId && response.streamId.trim().length > 0
    ? response.streamId
    : undefined;
  if (response.snapshot) {
    if (responseStreamId && !response.snapshot.streamId) {
      response.snapshot.streamId = responseStreamId;
    }

    results.push(receiver.applyStateSnapshot(response.snapshot, 0n, response.snapshot.tick));
  }

  for (const delta of response.deltas) {
    if (responseStreamId && !delta.streamId) {
      delta.streamId = responseStreamId;
    }

    results.push(receiver.applyStateDelta(delta, 0n, delta.tick));
  }

  return {
    replayed: response.replayed,
    reason: response.reason,
    streamId: responseStreamId,
    results
  };
}

export async function recoverStateSyncMultiStream(
  client: YuoMmoClient,
  receiver: YuoStateSyncMultiStreamReceiver,
  playerId: string,
  streamId: string,
  knownTick: bigint | number = receiver.streams.getCursor(streamId).lastTick ?? 0n,
  options?: AbortSignal | YuoClientCallOptions
): Promise<YuoStateSyncPushResult[]> {
  return (await recoverStateSyncMultiStreamDetailed(
    client,
    receiver,
    playerId,
    streamId,
    knownTick,
    options
  )).results;
}

export async function recoverStateSyncMultiStreamDetailed(
  client: YuoMmoClient,
  receiver: YuoStateSyncMultiStreamReceiver,
  playerId: string,
  streamId: string,
  knownTick: bigint | number = receiver.streams.getCursor(streamId).lastTick ?? 0n,
  options?: AbortSignal | YuoClientCallOptions
): Promise<YuoStateSyncRecoverApplyResult> {
  if (!streamId || streamId.trim().length === 0) {
    throw new Error("StateSync stream id is required for multi-stream recover.");
  }

  const response = await client.recoverStateSync(playerId, knownTick, streamId, options);
  const results: YuoStateSyncPushResult[] = [];
  const responseStreamId = response.streamId && response.streamId.trim().length > 0
    ? response.streamId
    : streamId;
  if (response.snapshot) {
    if (!response.snapshot.streamId) {
      response.snapshot.streamId = responseStreamId;
    }

    results.push(receiver.applyStateSnapshot(response.snapshot, 0n, response.snapshot.tick));
  }

  for (const delta of response.deltas) {
    if (!delta.streamId) {
      delta.streamId = responseStreamId;
    }

    results.push(receiver.applyStateDelta(delta, 0n, delta.tick));
  }

  return {
    replayed: response.replayed,
    reason: response.reason,
    streamId: responseStreamId,
    results
  };
}

export class YuoClientSession {
  private nextRequestId = 0n;
  private nextClientSeq = 0n;

  public encodeRequest<TRequest, TResponse>(
    descriptor: YuoClientMessageDescriptor<TRequest, TResponse>,
    request: TRequest
  ): YuoClientOutboundMessage {
    const requestId = ++this.nextRequestId;
    const clientSeq = ++this.nextClientSeq;
    const envelope: YuoClientEnvelopeV1 = {
      version: YUO_PROTOCOL_VERSION,
      opcode: descriptor.requestOpCode,
      requestId,
      clientSeq,
      payload: descriptor.request.encode(request)
    };

    return {
      requestId,
      clientSeq,
      opCode: descriptor.requestOpCode,
      bytes: encodeClientEnvelopeV1(envelope)
    };
  }

  public decodeResponse<TResponse>(
    descriptor: YuoClientMessageDescriptor<unknown, TResponse>,
    responseBytes: Uint8Array,
    expectedRequestId?: bigint
  ): YuoClientInboundMessage<TResponse> {
    const envelope = decodeServerEnvelopeV1(responseBytes);
    if (expectedRequestId !== undefined && envelope.requestId !== expectedRequestId) {
      throw new YuoClientProtocolError(
        "request_mismatch",
        `Expected response for request ${expectedRequestId}, got ${envelope.requestId}.`
      );
    }

    if (envelope.error && envelope.error.code.length > 0) {
      throw new YuoClientProtocolError(envelope.error.code, envelope.error.message);
    }

    if (envelope.opcode !== descriptor.responseOpCode) {
      throw new YuoClientProtocolError(
        "opcode_mismatch",
        `Expected opcode ${descriptor.responseOpCode}, got ${envelope.opcode}.`
      );
    }

    return {
      requestId: envelope.requestId,
      serverSeq: envelope.serverSeq,
      opCode: envelope.opcode,
      payload: descriptor.response.decode(envelope.payload)
    };
  }

  public async call<TRequest, TResponse>(
    transport: YuoClientTransport,
    descriptor: YuoClientMessageDescriptor<TRequest, TResponse>,
    request: TRequest,
    signalOrOptions?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoClientInboundMessage<TResponse>> {
    const options = normalizeCallOptions(signalOrOptions);
    const abort = createCallAbortSignal(options);
    const outbound = this.encodeRequest(descriptor, request);
    try {
      const responseBytes = await transport.send(outbound.bytes, abort.signal);
      return this.decodeResponse(descriptor, responseBytes, outbound.requestId);
    } finally {
      abort.dispose();
    }
  }
}

export class YuoMmoClient {
  public constructor(
    private readonly transport: YuoClientTransport,
    private readonly session = new YuoClientSession()
  ) {
  }

  public async login(
    playerId = "",
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoWelcome> {
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.hello,
      { playerId },
      options);
    return response.payload;
  }

  public async click(
    playerId: string,
    amount: bigint | number = 1,
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoPlayerSnapshot> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.click,
      { playerId, amount },
      options);
    return response.payload;
  }

  public async getSnapshot(
    playerId: string,
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoPlayerSnapshot> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.snapshot,
      { playerId },
      options);
    return response.payload;
  }

  public async enterMap(
    playerId: string,
    mapId: bigint | number,
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoWorldSnapshot> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.enterMap,
      { playerId, mapId },
      options);
    return response.payload;
  }

  public async move(
    playerId: string,
    inputSeq: bigint | number,
    x: number,
    y: number,
    z: number,
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoWorldSnapshot> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.move,
      { playerId, inputSeq, x, y, z },
      options);
    return response.payload;
  }

  public async disconnect(
    playerId: string,
    sessionId = "",
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoPlayerSnapshot> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.disconnect,
      { playerId, sessionId },
      options);
    return response.payload;
  }

  public async requestStateSnapshot(
    playerId: string,
    knownTick: bigint | number = 0,
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoStateSnapshot> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.stateSnapshot,
      { playerId, knownTick },
      options);
    return response.payload;
  }

  public async recoverStateSync(
    playerId: string,
    knownTick: bigint | number = 0,
    streamId = "",
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoStateSyncRecoverResponse> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.stateSyncRecover,
      { playerId, knownTick, streamId },
      options);
    return response.payload;
  }

  public async ackStateSync(
    playerId: string,
    ackTick: bigint | number,
    streamId = "",
    options?: AbortSignal | YuoClientCallOptions
  ): Promise<YuoStateSyncAckResponse> {
    assertNonEmpty(playerId, "playerId");
    const response = await this.session.call(
      this.transport,
      YuoMmoDescriptors.stateSyncAck,
      { playerId, ackTick, streamId },
      options);
    return response.payload;
  }
}

export class YuoWebSocketTransport implements YuoClientTransport {
  private readonly pending = new Map<string, PendingRequest>();
  private readonly pushHandlers = new Set<YuoServerPushHandler>();

  public constructor(private readonly socket: WebSocket) {
    socket.binaryType = "arraybuffer";
    socket.addEventListener("message", event => this.handleMessage(event));
    socket.addEventListener("close", () => this.rejectAll(new Error("websocket closed")));
    socket.addEventListener("error", () => this.rejectAll(new Error("websocket error")));
  }

  public static async connect(url: string, protocols: string | string[] = "yuocore.v1"): Promise<YuoWebSocketTransport> {
    const socket = new WebSocket(url, protocols);
    await new Promise<void>((resolve, reject) => {
      socket.addEventListener("open", () => resolve(), { once: true });
      socket.addEventListener("error", () => reject(new Error("websocket connect failed")), { once: true });
    });

    return new YuoWebSocketTransport(socket);
  }

  public send(request: Uint8Array, signal?: AbortSignal): Promise<Uint8Array> {
    if (this.socket.readyState !== WebSocket.OPEN) {
      return Promise.reject(new Error("websocket is not open"));
    }

    const requestId = readClientEnvelopeRequestId(request).toString();
    return new Promise<Uint8Array>((resolve, reject) => {
      const abort = () => {
        this.pending.delete(requestId);
        reject(new DOMException("request aborted", "AbortError"));
      };

      if (signal?.aborted) {
        abort();
        return;
      }

      signal?.addEventListener("abort", abort, { once: true });
      const pending: PendingRequest = {
        resolve: bytes => {
          signal?.removeEventListener("abort", abort);
          resolve(bytes);
        },
        reject: error => {
          signal?.removeEventListener("abort", abort);
          reject(error);
        }
      };
      this.pending.set(requestId, pending);

      try {
        this.socket.send(toArrayBuffer(request));
      } catch (error) {
        this.pending.delete(requestId);
        signal?.removeEventListener("abort", abort);
        reject(error instanceof Error ? error : new Error(String(error)));
      }
    });
  }

  public subscribePush(handler: YuoServerPushHandler): () => void {
    this.pushHandlers.add(handler);
    return () => {
      this.pushHandlers.delete(handler);
    };
  }

  public close(code = 1000, reason = "client closed"): void {
    if (this.socket.readyState === WebSocket.OPEN || this.socket.readyState === WebSocket.CONNECTING) {
      this.socket.close(code, reason);
    }
  }

  private handleMessage(event: MessageEvent): void {
    const bytes = toUint8Array(event.data);
    const envelope = decodeServerEnvelopeV1(bytes);
    const pending = this.pending.get(envelope.requestId.toString());
    if (!pending) {
      if (envelope.requestId === 0n) {
        for (const handler of this.pushHandlers) {
          handler(envelope, bytes);
        }
      }

      return;
    }

    this.pending.delete(envelope.requestId.toString());
    pending.resolve(bytes);
  }

  private rejectAll(error: Error): void {
    for (const pending of this.pending.values()) {
      pending.reject(error);
    }

    this.pending.clear();
  }
}

export interface YuoMmoWebSocketSessionOptions {
  url: string;
  protocols?: string | string[];
  requestTimeoutMs?: number;
  transportFactory?: (url: string, protocols: string | string[]) => Promise<YuoClosableClientTransport>;
}

export type YuoClosableClientTransport = YuoClientTransport & {
  close(code?: number, reason?: string): void;
};

export class YuoMmoWebSocketSession {
  private transport?: YuoClosableClientTransport;
  private mmoClient?: YuoMmoClient;
  private currentPlayerId = "";

  public constructor(private readonly options: YuoMmoWebSocketSessionOptions) {
  }

  public get playerId(): string {
    return this.currentPlayerId;
  }

  public get client(): YuoMmoClient {
    if (!this.mmoClient) {
      throw new Error("YuoMmoWebSocketSession is not connected");
    }

    return this.mmoClient;
  }

  public async connect(playerId = this.currentPlayerId): Promise<YuoWelcome> {
    this.close();

    const factory = this.options.transportFactory ?? YuoWebSocketTransport.connect;
    this.transport = await factory(
      this.options.url,
      this.options.protocols ?? "yuocore.v1");
    this.mmoClient = new YuoMmoClient(this.transport);

    const welcome = await this.mmoClient.login(playerId, this.callOptions());
    this.currentPlayerId = welcome.playerId;
    return welcome;
  }

  public async reconnect(): Promise<YuoWelcome> {
    if (!this.currentPlayerId) {
      throw new Error("cannot reconnect before first login");
    }

    return this.connect(this.currentPlayerId);
  }

  public async disconnect(): Promise<YuoPlayerSnapshot> {
    if (!this.currentPlayerId) {
      throw new Error("cannot disconnect before first login");
    }

    const snapshot = await this.client.disconnect(this.currentPlayerId, "", this.callOptions());
    this.close();
    return snapshot;
  }

  public close(): void {
    this.transport?.close();
    this.transport = undefined;
    this.mmoClient = undefined;
  }

  private callOptions(): YuoClientCallOptions | undefined {
    return this.options.requestTimeoutMs === undefined
      ? undefined
      : { timeoutMs: this.options.requestTimeoutMs };
  }
}

export function encodeClientEnvelopeV1(envelope: YuoClientEnvelopeV1): Uint8Array {
  const writer = new ProtoWriter();
  writer.uint32(1, BigInt(envelope.version));
  writer.uint32(2, BigInt(envelope.opcode));
  writer.uint64(3, envelope.requestId);
  writer.uint64(4, envelope.clientSeq);
  writer.bytes(5, envelope.payload);
  return writer.finish();
}

export function decodeClientEnvelopeV1(bytes: Uint8Array): YuoClientEnvelopeV1 {
  const reader = new ProtoReader(bytes);
  const envelope: YuoClientEnvelopeV1 = {
    version: 0,
    opcode: 0,
    requestId: 0n,
    clientSeq: 0n,
    payload: new Uint8Array()
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        envelope.version = Number(reader.varint());
        break;
      case 2:
        envelope.opcode = Number(reader.varint());
        break;
      case 3:
        envelope.requestId = reader.varint();
        break;
      case 4:
        envelope.clientSeq = reader.varint();
        break;
      case 5:
        envelope.payload = reader.bytes();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return envelope;
}

export function decodeServerEnvelopeV1(bytes: Uint8Array): YuoServerEnvelopeV1 {
  const reader = new ProtoReader(bytes);
  const envelope: YuoServerEnvelopeV1 = {
    version: 0,
    opcode: 0,
    requestId: 0n,
    serverSeq: 0n,
    payload: new Uint8Array()
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        envelope.version = Number(reader.varint());
        break;
      case 2:
        envelope.opcode = Number(reader.varint());
        break;
      case 3:
        envelope.requestId = reader.varint();
        break;
      case 4:
        envelope.serverSeq = reader.varint();
        break;
      case 5:
        envelope.error = decodeErrorResponse(reader.bytes());
        break;
      case 6:
        envelope.payload = reader.bytes();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return envelope;
}

export function readClientEnvelopeRequestId(bytes: Uint8Array): bigint {
  const reader = new ProtoReader(bytes);
  while (!reader.eof()) {
    const tag = reader.tag();
    if (tag.field === 3) {
      return reader.varint();
    }

    reader.skip(tag.wireType);
  }

  throw new YuoClientProtocolError("missing_request_id", "Client envelope did not contain request_id.");
}

export function encodeHelloRequest(request: YuoHelloRequest): Uint8Array {
  const writer = new ProtoWriter();
  if (request.playerId) {
    writer.string(1, request.playerId);
  }

  return writer.finish();
}

export function decodeHelloRequest(bytes: Uint8Array): YuoHelloRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoHelloRequest = {};
  while (!reader.eof()) {
    const tag = reader.tag();
    if (tag.field === 1) {
      request.playerId = reader.string();
    } else {
      reader.skip(tag.wireType);
    }
  }

  return request;
}

export function encodeClickRequest(request: YuoClickRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  writer.uint64(2, toBigInt(request.amount));
  return writer.finish();
}

export function decodeClickRequest(bytes: Uint8Array): YuoClickRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoClickRequest = { playerId: "", amount: 0n };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        request.playerId = reader.string();
        break;
      case 2:
        request.amount = reader.varint();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return request;
}

export function encodeSnapshotRequest(request: YuoSnapshotRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  return writer.finish();
}

export function decodeSnapshotRequest(bytes: Uint8Array): YuoSnapshotRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoSnapshotRequest = { playerId: "" };
  while (!reader.eof()) {
    const tag = reader.tag();
    if (tag.field === 1) {
      request.playerId = reader.string();
    } else {
      reader.skip(tag.wireType);
    }
  }

  return request;
}

export function encodeEnterMapRequest(request: YuoEnterMapRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  writer.uint64(2, toBigInt(request.mapId));
  return writer.finish();
}

export function decodeEnterMapRequest(bytes: Uint8Array): YuoEnterMapRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoEnterMapRequest = { playerId: "", mapId: 0n };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        request.playerId = reader.string();
        break;
      case 2:
        request.mapId = reader.varint();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return request;
}

export function encodeMoveRequest(request: YuoMoveRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  writer.uint64(2, toBigInt(request.inputSeq));
  writer.float(3, request.x);
  writer.float(4, request.y);
  writer.float(5, request.z);
  return writer.finish();
}

export function decodeMoveRequest(bytes: Uint8Array): YuoMoveRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoMoveRequest = { playerId: "", inputSeq: 0n, x: 0, y: 0, z: 0 };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        request.playerId = reader.string();
        break;
      case 2:
        request.inputSeq = reader.varint();
        break;
      case 3:
        request.x = reader.float();
        break;
      case 4:
        request.y = reader.float();
        break;
      case 5:
        request.z = reader.float();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return request;
}

export function encodeDisconnectRequest(request: YuoDisconnectRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  writer.string(2, request.sessionId);
  return writer.finish();
}

export function decodeDisconnectRequest(bytes: Uint8Array): YuoDisconnectRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoDisconnectRequest = { playerId: "", sessionId: "" };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        request.playerId = reader.string();
        break;
      case 2:
        request.sessionId = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return request;
}

export function encodeStateSnapshotRequest(request: YuoStateSnapshotRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  writer.uint64(2, toBigInt(request.knownTick ?? 0));
  return writer.finish();
}

export function decodeStateSnapshotRequest(bytes: Uint8Array): YuoStateSnapshotRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoStateSnapshotRequest = { playerId: "", knownTick: 0n };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        request.playerId = reader.string();
        break;
      case 2:
        request.knownTick = reader.varint();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return request;
}

export function encodeStateSyncRecoverRequest(request: YuoStateSyncRecoverRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  writer.uint64(2, toBigInt(request.knownTick ?? 0));
  writer.string(3, request.streamId ?? "");
  return writer.finish();
}

export function decodeStateSyncRecoverRequest(bytes: Uint8Array): YuoStateSyncRecoverRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoStateSyncRecoverRequest = { playerId: "", knownTick: 0n };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        request.playerId = reader.string();
        break;
      case 2:
        request.knownTick = reader.varint();
        break;
      case 3:
        request.streamId = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return request;
}

export function encodeStateSyncAckRequest(request: YuoStateSyncAckRequest): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, request.playerId);
  writer.uint64(2, toBigInt(request.ackTick));
  writer.string(3, request.streamId ?? "");
  return writer.finish();
}

export function decodeStateSyncAckRequest(bytes: Uint8Array): YuoStateSyncAckRequest {
  const reader = new ProtoReader(bytes);
  const request: YuoStateSyncAckRequest = { playerId: "", ackTick: 0n, streamId: "" };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        request.playerId = reader.string();
        break;
      case 2:
        request.ackTick = reader.varint();
        break;
      case 3:
        request.streamId = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return request;
}

export function encodeWelcome(welcome: YuoWelcome): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, welcome.playerId);
  writer.string(2, welcome.message);
  return writer.finish();
}

export function decodeWelcome(bytes: Uint8Array): YuoWelcome {
  const reader = new ProtoReader(bytes);
  const welcome: YuoWelcome = { playerId: "", message: "" };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        welcome.playerId = reader.string();
        break;
      case 2:
        welcome.message = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return welcome;
}

export function encodePlayerSnapshot(snapshot: YuoPlayerSnapshot): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, snapshot.playerId);
  writer.string(2, snapshot.name);
  writer.uint64(3, snapshot.gold);
  writer.uint64(4, snapshot.exp);
  writer.uint32(5, BigInt(snapshot.level));
  writer.uint64(6, snapshot.power);
  writer.uint64(7, snapshot.clicks);
  writer.uint64(8, snapshot.updatedUnixMs);
  writer.uint64(9, snapshot.mapId);
  writer.uint64(10, snapshot.unitId);
  writer.float(11, snapshot.x);
  writer.float(12, snapshot.y);
  writer.float(13, snapshot.z);
  writer.uint32(14, snapshot.online ? 1n : 0n);
  writer.uint64(15, snapshot.lastInputSeq);
  return writer.finish();
}

export function decodePlayerSnapshot(bytes: Uint8Array): YuoPlayerSnapshot {
  const reader = new ProtoReader(bytes);
  const snapshot: YuoPlayerSnapshot = {
    playerId: "",
    name: "",
    gold: 0n,
    exp: 0n,
    level: 0,
    power: 0n,
    clicks: 0n,
    updatedUnixMs: 0n,
    mapId: 0n,
    unitId: 0n,
    x: 0,
    y: 0,
    z: 0,
    online: false,
    lastInputSeq: 0n
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1: snapshot.playerId = reader.string(); break;
      case 2: snapshot.name = reader.string(); break;
      case 3: snapshot.gold = reader.varint(); break;
      case 4: snapshot.exp = reader.varint(); break;
      case 5: snapshot.level = Number(reader.varint()); break;
      case 6: snapshot.power = reader.varint(); break;
      case 7: snapshot.clicks = reader.varint(); break;
      case 8: snapshot.updatedUnixMs = reader.varint(); break;
      case 9: snapshot.mapId = reader.varint(); break;
      case 10: snapshot.unitId = reader.varint(); break;
      case 11: snapshot.x = reader.float(); break;
      case 12: snapshot.y = reader.float(); break;
      case 13: snapshot.z = reader.float(); break;
      case 14: snapshot.online = reader.varint() !== 0n; break;
      case 15: snapshot.lastInputSeq = reader.varint(); break;
      default: reader.skip(tag.wireType); break;
    }
  }

  return snapshot;
}

export function encodePlayerWorldState(state: YuoPlayerWorldState): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, state.playerId);
  writer.string(2, state.name);
  writer.uint64(3, state.mapId);
  writer.uint64(4, state.unitId);
  writer.float(5, state.x);
  writer.float(6, state.y);
  writer.float(7, state.z);
  writer.uint32(8, state.online ? 1n : 0n);
  writer.uint64(9, state.lastInputSeq);
  writer.uint64(10, state.updatedUnixMs);
  return writer.finish();
}

export function decodePlayerWorldState(bytes: Uint8Array): YuoPlayerWorldState {
  const reader = new ProtoReader(bytes);
  const state: YuoPlayerWorldState = {
    playerId: "",
    name: "",
    mapId: 0n,
    unitId: 0n,
    x: 0,
    y: 0,
    z: 0,
    online: false,
    lastInputSeq: 0n,
    updatedUnixMs: 0n
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1: state.playerId = reader.string(); break;
      case 2: state.name = reader.string(); break;
      case 3: state.mapId = reader.varint(); break;
      case 4: state.unitId = reader.varint(); break;
      case 5: state.x = reader.float(); break;
      case 6: state.y = reader.float(); break;
      case 7: state.z = reader.float(); break;
      case 8: state.online = reader.varint() !== 0n; break;
      case 9: state.lastInputSeq = reader.varint(); break;
      case 10: state.updatedUnixMs = reader.varint(); break;
      default: reader.skip(tag.wireType); break;
    }
  }

  return state;
}

export function encodeWorldEvent(event: YuoWorldEvent): Uint8Array {
  const writer = new ProtoWriter();
  writer.string(1, event.kind);
  if (event.player) {
    writer.bytes(2, encodePlayerWorldState(event.player));
  }

  writer.uint64(3, event.createdUnixMs);
  return writer.finish();
}

export function decodeWorldEvent(bytes: Uint8Array): YuoWorldEvent {
  const reader = new ProtoReader(bytes);
  const event: YuoWorldEvent = { kind: "", createdUnixMs: 0n };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        event.kind = reader.string();
        break;
      case 2:
        event.player = decodePlayerWorldState(reader.bytes());
        break;
      case 3:
        event.createdUnixMs = reader.varint();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return event;
}

export function encodeWorldSnapshot(snapshot: YuoWorldSnapshot): Uint8Array {
  const writer = new ProtoWriter();
  writer.uint64(1, snapshot.mapId);
  writer.uint64(2, snapshot.tick);
  for (const player of snapshot.players) {
    writer.bytes(3, encodePlayerWorldState(player));
  }

  for (const event of snapshot.events) {
    writer.bytes(4, encodeWorldEvent(event));
  }

  return writer.finish();
}

export function decodeWorldSnapshot(bytes: Uint8Array): YuoWorldSnapshot {
  const reader = new ProtoReader(bytes);
  const snapshot: YuoWorldSnapshot = {
    mapId: 0n,
    tick: 0n,
    players: [],
    events: []
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        snapshot.mapId = reader.varint();
        break;
      case 2:
        snapshot.tick = reader.varint();
        break;
      case 3:
        snapshot.players.push(decodePlayerWorldState(reader.bytes()));
        break;
      case 4:
        snapshot.events.push(decodeWorldEvent(reader.bytes()));
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return snapshot;
}

export function encodeStateSnapshotMessage(snapshot: YuoStateSnapshot): Uint8Array {
  const writer = new ProtoWriter();
  writer.uint64(1, snapshot.tick);
  for (const entity of snapshot.entities) {
    writer.bytes(2, encodeNetworkEntityState(entity));
  }
  writer.string(3, snapshot.streamId ?? "");

  return writer.finish();
}

export function encodeStateSyncRecoverResponse(response: YuoStateSyncRecoverResponse): Uint8Array {
  const writer = new ProtoWriter();
  writer.bool(1, response.replayed);
  writer.string(2, response.reason);
  if (response.snapshot) {
    writer.bytes(3, encodeStateSnapshotMessage(response.snapshot));
  }

  for (const delta of response.deltas) {
    writer.bytes(4, encodeStateDelta(delta));
  }
  writer.string(5, response.streamId ?? "");

  return writer.finish();
}

export function decodeStateSyncRecoverResponse(bytes: Uint8Array): YuoStateSyncRecoverResponse {
  const reader = new ProtoReader(bytes);
  const response: YuoStateSyncRecoverResponse = {
    replayed: false,
    reason: "",
    deltas: []
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        response.replayed = reader.bool();
        break;
      case 2:
        response.reason = reader.string();
        break;
      case 3:
        response.snapshot = decodeStateSnapshotMessage(reader.bytes());
        break;
      case 4:
        response.deltas.push(decodeStateDeltaMessage(reader.bytes()));
        break;
      case 5:
        response.streamId = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return response;
}

export function encodeStateSyncAckResponse(response: YuoStateSyncAckResponse): Uint8Array {
  const writer = new ProtoWriter();
  writer.bool(1, response.accepted);
  writer.uint64(2, response.ackTick);
  writer.uint64(3, response.latestTick);
  writer.string(4, response.reason);
  writer.string(5, response.streamId ?? "");
  return writer.finish();
}

export function decodeStateSyncAckResponse(bytes: Uint8Array): YuoStateSyncAckResponse {
  const reader = new ProtoReader(bytes);
  const response: YuoStateSyncAckResponse = {
    accepted: false,
    ackTick: 0n,
    latestTick: 0n,
    reason: ""
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        response.accepted = reader.bool();
        break;
      case 2:
        response.ackTick = reader.varint();
        break;
      case 3:
        response.latestTick = reader.varint();
        break;
      case 4:
        response.reason = reader.string();
        break;
      case 5:
        response.streamId = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return response;
}

export function encodeNetworkEntityState(entity: YuoNetworkEntityState): Uint8Array {
  const writer = new ProtoWriter();
  writer.uint64(1, entity.networkId);
  writer.string(2, entity.kind);
  writer.uint32(3, BigInt(entity.version));
  writer.bytes(4, entity.payload);
  return writer.finish();
}

export function decodeNetworkEntityState(bytes: Uint8Array): YuoNetworkEntityState {
  const reader = new ProtoReader(bytes);
  const state: YuoNetworkEntityState = {
    networkId: 0n,
    kind: "",
    version: 0,
    payload: new Uint8Array()
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        state.networkId = reader.varint();
        break;
      case 2:
        state.kind = reader.string();
        break;
      case 3:
        state.version = Number(reader.varint());
        break;
      case 4:
        state.payload = reader.bytes();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return state;
}

export function decodeStateSnapshotMessage(bytes: Uint8Array): YuoStateSnapshot {
  const reader = new ProtoReader(bytes);
  const snapshot: YuoStateSnapshot = {
    tick: 0n,
    entities: []
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        snapshot.tick = reader.varint();
        break;
      case 2:
        snapshot.entities.push(decodeNetworkEntityState(reader.bytes()));
        break;
      case 3:
        snapshot.streamId = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return snapshot;
}

export function encodeStateDelta(delta: YuoStateDelta): Uint8Array {
  const writer = new ProtoWriter();
  writer.uint64(1, delta.tick);
  for (const change of delta.changes) {
    writer.bytes(2, encodeStateChangeMessage(change));
  }

  if (delta.baseTick !== undefined) {
    writer.uint64(3, delta.baseTick);
  }
  writer.string(4, delta.streamId ?? "");

  return writer.finish();
}

export function encodeStateChangeMessage(change: YuoStateChange): Uint8Array {
  const writer = new ProtoWriter();
  writer.uint32(1, BigInt(change.kind));
  if (change.state) {
    writer.bytes(2, encodeNetworkEntityState(change.state));
  }

  writer.uint64(3, change.networkId);
  return writer.finish();
}

export function decodeStateChangeMessage(bytes: Uint8Array): YuoStateChange {
  const reader = new ProtoReader(bytes);
  let state: YuoNetworkEntityState | undefined;
  let networkId = 0n;
  let networkIdSet = false;
  let kind: YuoStateChangeKindValue = YuoStateChangeKind.unknown;

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        kind = Number(reader.varint()) as YuoStateChangeKindValue;
        break;
      case 2:
        state = decodeNetworkEntityState(reader.bytes());
        break;
      case 3:
        networkId = reader.varint();
        networkIdSet = true;
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return {
    kind,
    networkId: networkIdSet ? networkId : state?.networkId ?? 0n,
    state
  };
}

export function decodeStateDeltaMessage(bytes: Uint8Array): YuoStateDelta {
  const reader = new ProtoReader(bytes);
  const delta: YuoStateDelta = {
    tick: 0n,
    changes: []
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        delta.tick = reader.varint();
        break;
      case 2:
        delta.changes.push(decodeStateChangeMessage(reader.bytes()));
        break;
      case 3:
        delta.baseTick = reader.varint();
        break;
      case 4:
        delta.streamId = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return delta;
}

export function encodeStateSubscriptionMessage(subscription: YuoStateSubscription): Uint8Array {
  const writer = new ProtoWriter();
  for (const streamId of subscription.streamIds) {
    writer.string(1, streamId);
  }

  writer.string(2, subscription.reason);
  writer.bool(3, subscription.resetMissing);
  return writer.finish();
}

export function decodeStateSubscriptionMessage(bytes: Uint8Array): YuoStateSubscription {
  const reader = new ProtoReader(bytes);
  const subscription: YuoStateSubscription = {
    streamIds: [],
    reason: "",
    resetMissing: false
  };

  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        subscription.streamIds.push(reader.string());
        break;
      case 2:
        subscription.reason = reader.string();
        break;
      case 3:
        subscription.resetMissing = reader.bool();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return subscription;
}

function decodeErrorResponse(bytes: Uint8Array): YuoErrorResponse {
  const reader = new ProtoReader(bytes);
  const error: YuoErrorResponse = { code: "", message: "" };
  while (!reader.eof()) {
    const tag = reader.tag();
    switch (tag.field) {
      case 1:
        error.code = reader.string();
        break;
      case 2:
        error.message = reader.string();
        break;
      default:
        reader.skip(tag.wireType);
        break;
    }
  }

  return error;
}

function toUint8Array(data: unknown): Uint8Array {
  if (data instanceof ArrayBuffer) {
    return new Uint8Array(data);
  }

  if (data instanceof Uint8Array) {
    return data;
  }

  if (data instanceof Blob) {
    throw new Error("Blob websocket messages are not supported; set binaryType to arraybuffer.");
  }

  throw new Error("Unsupported websocket message type.");
}

function toArrayBuffer(bytes: Uint8Array): ArrayBuffer {
  const buffer = new ArrayBuffer(bytes.byteLength);
  new Uint8Array(buffer).set(bytes);
  return buffer;
}

function normalizeCallOptions(signalOrOptions?: AbortSignal | YuoClientCallOptions): YuoClientCallOptions {
  if (!signalOrOptions) {
    return {};
  }

  if (isAbortSignal(signalOrOptions)) {
    return { signal: signalOrOptions };
  }

  return signalOrOptions;
}

function createCallAbortSignal(options: YuoClientCallOptions): { signal?: AbortSignal; dispose(): void } {
  const timeoutMs = options.timeoutMs;
  if (timeoutMs === undefined || timeoutMs === null) {
    return { signal: options.signal, dispose: () => undefined };
  }

  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) {
    throw new RangeError("timeoutMs must be a positive finite number.");
  }

  const controller = new AbortController();
  const abortFromParent = () => controller.abort(options.signal?.reason);
  if (options.signal?.aborted) {
    abortFromParent();
  } else {
    options.signal?.addEventListener("abort", abortFromParent, { once: true });
  }

  const timer = setTimeout(() => controller.abort(new Error("request timed out")), timeoutMs);
  return {
    signal: controller.signal,
    dispose: () => {
      clearTimeout(timer);
      options.signal?.removeEventListener("abort", abortFromParent);
    }
  };
}

function isAbortSignal(value: AbortSignal | YuoClientCallOptions): value is AbortSignal {
  return typeof (value as AbortSignal).aborted === "boolean" &&
    typeof (value as AbortSignal).addEventListener === "function";
}

function assertNonEmpty(value: string, name: string): void {
  if (!value || value.trim().length === 0) {
    throw new TypeError(`${name} cannot be empty.`);
  }
}

function normalizeStateSyncStreamId(streamId: string): string {
  if (!streamId || streamId.trim().length === 0) {
    throw new TypeError("StateSync stream id cannot be empty.");
  }

  return streamId.trim();
}

function toBigInt(value: bigint | number): bigint {
  if (typeof value === "bigint") {
    return value;
  }

  if (!Number.isFinite(value) || !Number.isSafeInteger(value) || value < 0) {
    throw new RangeError("protobuf integer value must be a non-negative safe integer.");
  }

  return BigInt(value);
}

interface PendingRequest {
  resolve(bytes: Uint8Array): void;
  reject(error: Error): void;
}

interface ProtoTag {
  field: number;
  wireType: number;
}

class ProtoWriter {
  private readonly chunks: number[] = [];

  public uint32(field: number, value: bigint): void {
    this.tag(field, 0);
    this.varint(value);
  }

  public uint64(field: number, value: bigint): void {
    this.tag(field, 0);
    this.varint(value);
  }

  public bool(field: number, value: boolean): void {
    this.tag(field, 0);
    this.varint(value ? 1n : 0n);
  }

  public bytes(field: number, value: Uint8Array): void {
    this.tag(field, 2);
    this.varint(BigInt(value.length));
    for (const byte of value) {
      this.chunks.push(byte);
    }
  }

  public string(field: number, value: string): void {
    this.bytes(field, new TextEncoder().encode(value));
  }

  public float(field: number, value: number): void {
    if (!Number.isFinite(value)) {
      throw new RangeError("protobuf float value must be finite.");
    }

    this.tag(field, 5);
    const buffer = new ArrayBuffer(4);
    new DataView(buffer).setFloat32(0, value, true);
    for (const byte of new Uint8Array(buffer)) {
      this.chunks.push(byte);
    }
  }

  public finish(): Uint8Array {
    return new Uint8Array(this.chunks);
  }

  private tag(field: number, wireType: number): void {
    this.varint(BigInt((field << 3) | wireType));
  }

  private varint(value: bigint): void {
    if (value < 0n) {
      throw new RangeError("protobuf varint cannot be negative");
    }

    let next = value;
    while (next >= 0x80n) {
      this.chunks.push(Number((next & 0x7fn) | 0x80n));
      next >>= 7n;
    }

    this.chunks.push(Number(next));
  }
}

class ProtoReader {
  private offset = 0;

  public constructor(private readonly bytesValue: Uint8Array) {
  }

  public eof(): boolean {
    return this.offset >= this.bytesValue.length;
  }

  public tag(): ProtoTag {
    const key = Number(this.varint());
    return {
      field: key >> 3,
      wireType: key & 0x7
    };
  }

  public varint(): bigint {
    let shift = 0n;
    let result = 0n;
    while (this.offset < this.bytesValue.length) {
      const byte = this.bytesValue[this.offset++]!;
      result |= BigInt(byte & 0x7f) << shift;
      if ((byte & 0x80) === 0) {
        return result;
      }

      shift += 7n;
      if (shift > 70n) {
        throw new YuoClientProtocolError("invalid_varint", "Protobuf varint is too long.");
      }
    }

    throw new YuoClientProtocolError("unexpected_eof", "Unexpected end of protobuf varint.");
  }

  public bool(): boolean {
    return this.varint() !== 0n;
  }

  public bytes(): Uint8Array {
    const length = Number(this.varint());
    if (!Number.isSafeInteger(length) || length < 0 || this.offset + length > this.bytesValue.length) {
      throw new YuoClientProtocolError("invalid_length", "Invalid protobuf length-delimited field.");
    }

    const value = this.bytesValue.slice(this.offset, this.offset + length);
    this.offset += length;
    return value;
  }

  public string(): string {
    return new TextDecoder().decode(this.bytes());
  }

  public float(): number {
    if (this.offset + 4 > this.bytesValue.length) {
      throw new YuoClientProtocolError("unexpected_eof", "Unexpected end of protobuf fixed32 field.");
    }

    const value = new DataView(
      this.bytesValue.buffer,
      this.bytesValue.byteOffset + this.offset,
      4).getFloat32(0, true);
    this.offset += 4;
    return value;
  }

  public skip(wireType: number): void {
    switch (wireType) {
      case 0:
        this.varint();
        return;
      case 2:
        this.bytes();
        return;
      case 5:
        if (this.offset + 4 > this.bytesValue.length) {
          throw new YuoClientProtocolError("unexpected_eof", "Unexpected end of protobuf fixed32 field.");
        }

        this.offset += 4;
        return;
      default:
        throw new YuoClientProtocolError("unsupported_wire_type", `Unsupported protobuf wire type ${wireType}.`);
    }
  }
}
