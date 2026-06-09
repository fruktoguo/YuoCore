#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

usage() {
  cat <<'USAGE'
Usage:
  scripts/run-client-sdk-smoke.sh

Runs the focused client SDK verification gate:
  - C# protocol client tests
  - C# StateSync client tests
  - Unity-style netstandard2.1 WebSocket adapter build
  - Web TypeScript SDK smoke
  - Web TypeScript SDK npm tarball consumer smoke

This script verifies client-facing packages/adapters without starting the Backend host.
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

echo "YuoCore client SDK smoke"

dotnet test "$ROOT_DIR/tests/YuoCore.Client.Tests/YuoCore.Client.Tests.csproj" --disable-build-servers
dotnet test "$ROOT_DIR/tests/YuoCore.Client.StateSync.Tests/YuoCore.Client.StateSync.Tests.csproj" --disable-build-servers
dotnet test "$ROOT_DIR/tests/YuoCore.UnityClient.Tests/YuoCore.UnityClient.Tests.csproj" --disable-build-servers
dotnet build "$ROOT_DIR/clients/unity/YuoCore.UnityClient/YuoCore.UnityClient.csproj" --disable-build-servers

npm --prefix "$ROOT_DIR/clients/web/yuocore-client-ts" run smoke
npm --prefix "$ROOT_DIR/clients/web/yuocore-client-ts" run browser-smoke
"$ROOT_DIR/scripts/run-web-sdk-pack-smoke.sh"

echo "client-sdk-smoke=passed"
