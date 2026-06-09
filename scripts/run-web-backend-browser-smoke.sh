#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SDK_DIR="$ROOT_DIR/clients/web/yuocore-client-ts"
BACKEND_PROJECT="$ROOT_DIR/src/YuoCore.Backend/YuoCore.Backend.csproj"

usage() {
  cat <<'USAGE'
Usage:
  scripts/run-web-backend-browser-smoke.sh

Builds the Web TypeScript SDK and sample Backend, then starts the real Backend host
on a random local port. A temporary browser page imports the SDK in Chrome/Chromium
headless and runs the minimal WebSocket MMO loop:
  login -> enter map -> move -> state snapshot -> disconnect

This verifies the browser SDK against the real ASP.NET Core /ws endpoint without
requiring PostgreSQL or Redis.
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

echo "YuoCore Web Backend browser smoke"

npm --prefix "$SDK_DIR" run build
dotnet build "$BACKEND_PROJECT" --disable-build-servers
node "$SDK_DIR/scripts/backend-browser-smoke.mjs"

echo "web-backend-browser-smoke=passed"
