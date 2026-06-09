#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_DIR="${YUO_PACK_OUTPUT_DIR:-$ROOT_DIR/artifacts/packages/pack-smoke-$RUN_ID}"

DEFAULT_PROJECTS=(
  "src/YuoCore.Abstractions/YuoCore.Abstractions.csproj"
  "src/YuoCore.Tasks/YuoCore.Tasks.csproj"
  "src/YuoCore.Timers/YuoCore.Timers.csproj"
  "src/YuoCore.Ecs/YuoCore.Ecs.csproj"
  "src/YuoCore.Actor/YuoCore.Actor.csproj"
  "src/YuoCore.Actor.Ecs/YuoCore.Actor.Ecs.csproj"
  "src/YuoCore.Actor.Timers/YuoCore.Actor.Timers.csproj"
  "src/YuoCore.Runtime/YuoCore.Runtime.csproj"
  "src/YuoCore.Runtime.Actor/YuoCore.Runtime.Actor.csproj"
  "src/YuoCore.Runtime.Timers/YuoCore.Runtime.Timers.csproj"
  "src/YuoCore.Location/YuoCore.Location.csproj"
  "src/YuoCore.Protocol/YuoCore.Protocol.csproj"
  "src/YuoCore.Client/YuoCore.Client.csproj"
  "src/YuoCore.Client.StateSync/YuoCore.Client.StateSync.csproj"
  "src/YuoCore.Gateway/YuoCore.Gateway.csproj"
  "src/YuoCore.Network/YuoCore.Network.csproj"
  "src/YuoCore.Game/YuoCore.Game.csproj"
  "src/YuoCore.StateSync.Abstractions/YuoCore.StateSync.Abstractions.csproj"
  "src/YuoCore.StateSync/YuoCore.StateSync.csproj"
  "src/YuoCore.StateSync.Protocol/YuoCore.StateSync.Protocol.csproj"
  "src/YuoCore.Persistence/YuoCore.Persistence.csproj"
  "src/YuoCore.Persistence.Hosting/YuoCore.Persistence.Hosting.csproj"
  "src/YuoCore.Config/YuoCore.Config.csproj"
  "src/YuoCore.ServiceDiscovery/YuoCore.ServiceDiscovery.csproj"
  "src/YuoCore.Server/YuoCore.Server.csproj"
  "src/YuoCore.Server.ServiceDiscovery/YuoCore.Server.ServiceDiscovery.csproj"
  "src/YuoCore.Analyzers/YuoCore.Analyzers.csproj"
)

FORBIDDEN_NUSPEC_PATTERNS=(
  "YuoCore.Backend"
  "Microsoft.AspNetCore"
  "Npgsql"
  "StackExchange.Redis"
  "Serilog"
  "Dapper"
  "OpenTelemetry"
)

usage() {
  cat <<'USAGE'
Usage:
  scripts/run-pack-smoke.sh

Environment overrides:
  YUO_PACK_OUTPUT_DIR  Output directory. Default: artifacts/packages/pack-smoke-<timestamp>
  YUO_PACK_PROJECTS    Space-separated project paths. Default: representative packable library set.

The smoke packs all currently publishable src libraries except the Backend sample host,
then scans generated nuspec files for forbidden host-only dependencies.
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

if [[ -n "${YUO_PACK_PROJECTS:-}" ]]; then
  # shellcheck disable=SC2206
  PROJECTS=($YUO_PACK_PROJECTS)
else
  PROJECTS=("${DEFAULT_PROJECTS[@]}")
fi

echo "YuoCore pack smoke"
echo "output=$OUT_DIR"

for project in "${PROJECTS[@]}"; do
  full_path="$ROOT_DIR/$project"
  if [[ ! -f "$full_path" ]]; then
    echo "missing project: $project" >&2
    exit 66
  fi

  echo "packing $project"
  dotnet pack "$full_path" \
    -c Release \
    --disable-build-servers \
    -p:PackageOutputPath="$OUT_DIR"
done

mapfile -t packages < <(find "$OUT_DIR" -maxdepth 1 -type f -name '*.nupkg' | sort)
if ((${#packages[@]} == 0)); then
  echo "no packages were produced" >&2
  exit 65
fi

for package in "${packages[@]}"; do
  nuspec="$(unzip -p "$package" '*.nuspec')"
  for pattern in "${FORBIDDEN_NUSPEC_PATTERNS[@]}"; do
    if grep -q "$pattern" <<<"$nuspec"; then
      echo "forbidden dependency pattern '$pattern' found in $(basename "$package")" >&2
      exit 67
    fi
  done
done

echo "packages=${#packages[@]}"
echo "pack-smoke-output=$OUT_DIR"
