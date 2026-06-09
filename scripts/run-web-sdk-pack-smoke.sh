#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK_DIR="$ROOT_DIR/clients/web/yuocore-client-ts"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_DIR="${YUO_WEB_SDK_PACK_OUTPUT_DIR:-$ROOT_DIR/artifacts/packages/web-sdk-pack-smoke-$RUN_ID}"

usage() {
  cat <<'USAGE'
Usage:
  scripts/run-web-sdk-pack-smoke.sh

Environment overrides:
  YUO_WEB_SDK_PACK_OUTPUT_DIR  Output directory. Default: artifacts/packages/web-sdk-pack-smoke-<timestamp>

The smoke builds the Web TypeScript SDK, runs npm pack, installs the produced tarball into
a fresh temporary consumer project, typechecks an import, and verifies runtime ESM import.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if (($# > 0)); then
  echo "unknown argument: $1" >&2
  usage >&2
  exit 64
fi

mkdir -p "$OUT_DIR"

echo "YuoCore Web SDK pack smoke"
echo "output=$OUT_DIR"

npm --prefix "$SDK_DIR" run build

# npm pack 对当前工作目录比较敏感；这里进入 SDK 包目录执行，避免 npm
# 把仓库根目录误判成待打包包目录。
tarball_name="$(cd "$SDK_DIR" && npm pack --pack-destination "$OUT_DIR" | tail -n 1)"
tarball="$OUT_DIR/$tarball_name"
if [[ ! -f "$tarball" ]]; then
  echo "npm pack did not produce tarball: $tarball" >&2
  exit 65
fi

consumer_dir="$OUT_DIR/consumer"
mkdir -p "$consumer_dir/src"

cat > "$consumer_dir/package.json" <<JSON
{
  "name": "yuocore-web-sdk-consumer-smoke",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "scripts": {
    "typecheck": "tsc -p tsconfig.json --noEmit",
    "runtime": "node src/runtime.mjs"
  },
  "dependencies": {
    "@yuocore/client": "file:$tarball"
  },
  "devDependencies": {
    "typescript": "^6.0.2"
  }
}
JSON

cat > "$consumer_dir/tsconfig.json" <<'JSON'
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "NodeNext",
    "moduleResolution": "NodeNext",
    "lib": ["ES2020", "DOM"],
    "strict": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*.ts"]
}
JSON

cat > "$consumer_dir/src/index.ts" <<'TS'
import {
  YUO_PROTOCOL_VERSION,
  YuoClientSession,
  YuoMmoClient,
  YuoMmoWebSocketSession,
  YuoProtocolOpCodes,
  YuoStateSyncMultiStreamReceiver,
  YuoStateSyncReceiver,
  shouldRequestStateSnapshot
} from "@yuocore/client";

const session = new YuoClientSession();
const receiver = new YuoStateSyncReceiver();
const multiReceiver = new YuoStateSyncMultiStreamReceiver();
const version: number = YUO_PROTOCOL_VERSION;
const stateDeltaOpcode: number = YuoProtocolOpCodes.stateDelta;
const clientCtor: typeof YuoMmoClient = YuoMmoClient;
const websocketSessionCtor: typeof YuoMmoWebSocketSession = YuoMmoWebSocketSession;

void session;
void receiver;
void multiReceiver;
void version;
void stateDeltaOpcode;
void clientCtor;
void websocketSessionCtor;
void shouldRequestStateSnapshot;
TS

cat > "$consumer_dir/src/runtime.mjs" <<'JS'
import { YUO_PROTOCOL_VERSION, YuoProtocolOpCodes } from "@yuocore/client";

if (YUO_PROTOCOL_VERSION !== 1) {
  throw new Error(`unexpected protocol version: ${YUO_PROTOCOL_VERSION}`);
}

if (YuoProtocolOpCodes.stateDelta !== 107) {
  throw new Error(`unexpected stateDelta opcode: ${YuoProtocolOpCodes.stateDelta}`);
}
JS

npm --prefix "$consumer_dir" install --ignore-scripts
npm --prefix "$consumer_dir" run typecheck
npm --prefix "$consumer_dir" run runtime

echo "web-sdk-tarball=$tarball"
echo "web-sdk-consumer=$consumer_dir"
