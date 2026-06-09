#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_DIR="${YUO_CONSUMER_OUTPUT_DIR:-$ROOT_DIR/artifacts/packages/consumer-smoke-$RUN_ID}"
VERSION="${YUO_CONSUMER_PACKAGE_VERSION:-0.1.0}"
PACKAGE_DIR="${YUO_CONSUMER_PACKAGE_DIR:-}"

usage() {
  cat <<'USAGE'
Usage:
  scripts/run-pack-consumer-smoke.sh

Environment overrides:
  YUO_CONSUMER_OUTPUT_DIR       Output directory. Default: artifacts/packages/consumer-smoke-<timestamp>
  YUO_CONSUMER_PACKAGE_DIR      Existing directory that contains YuoCore *.nupkg files.
  YUO_CONSUMER_PACKAGE_VERSION  Package version to install. Default: 0.1.0
  YUO_CONSUMER_NUGET_PACKAGES   Isolated NuGet global packages folder. Default: <output>/.nuget-packages

The smoke creates fresh consumer projects and restores YuoCore packages through NuGet,
not through project references. It verifies the public package graph for core/server
net10.0 usage and Unity-like netstandard2.1 client usage.
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
export NUGET_PACKAGES="${YUO_CONSUMER_NUGET_PACKAGES:-$OUT_DIR/.nuget-packages}"
mkdir -p "$NUGET_PACKAGES"

if [[ -z "$PACKAGE_DIR" ]]; then
  PACKAGE_DIR="$OUT_DIR/packages"
  YUO_PACK_OUTPUT_DIR="$PACKAGE_DIR" "$ROOT_DIR/scripts/run-pack-smoke.sh"
fi

if [[ ! -d "$PACKAGE_DIR" ]]; then
  echo "missing package directory: $PACKAGE_DIR" >&2
  exit 66
fi

PACKAGE_DIR="$(cd "$PACKAGE_DIR" && pwd)"

if ! compgen -G "$PACKAGE_DIR/YuoCore.*.$VERSION.nupkg" > /dev/null; then
  echo "no YuoCore $VERSION packages found in: $PACKAGE_DIR" >&2
  exit 65
fi

echo "YuoCore package consumer smoke"
echo "output=$OUT_DIR"
echo "packages=$PACKAGE_DIR"
echo "version=$VERSION"

write_nuget_config() {
  local profile_dir="$1"
  cat > "$profile_dir/NuGet.config" <<CONFIG
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-yuocore" value="$PACKAGE_DIR" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
CONFIG
}

create_profile() {
  local name="$1"
  local tfm="$2"
  local profile_dir="$OUT_DIR/$name"

  dotnet new classlib \
    --name "$name" \
    --output "$profile_dir" \
    --framework "$tfm" \
    --no-restore \
    > /dev/null

  write_nuget_config "$profile_dir"
  rm -f "$profile_dir/Class1.cs"
  echo "$profile_dir/$name.csproj"
}

add_package() {
  local project="$1"
  local package="$2"
  (cd "$(dirname "$project")" && dotnet add "$project" package "$package" --version "$VERSION")
}

build_profile() {
  local project="$1"
  dotnet build "$project" --configuration Release --disable-build-servers
}

core_project="$(create_profile "YuoCoreConsumer.CoreNet10" "net10.0")"
add_package "$core_project" "YuoCore.Ecs"
add_package "$core_project" "YuoCore.Runtime"
add_package "$core_project" "YuoCore.Actor"
cat > "$(dirname "$core_project")/ConsumerProbe.cs" <<'CS'
using System;
using YuoCore.Actor;
using YuoCore.Ecs;
using YuoCore.Runtime;

namespace YuoCoreConsumer.CoreNet10
{
    public static class ConsumerProbe
    {
        public static Type[] PublicTypes()
        {
            return new[]
            {
                typeof(YuoWorld),
                typeof(YuoEntity),
                typeof(YuoCoreRuntime),
                typeof(ActorId),
                typeof(ActorAddress),
                typeof(YuoActorLocationSender),
                typeof(YuoActorLocationSenderOptions)
            };
        }

        public static bool UsesLocationSenderCacheValidationOption()
        {
            var options = new YuoActorLocationSenderOptions(ValidateCachedAddressOnUse: true);
            return options.ValidateCachedAddressOnUse;
        }
    }
}
CS
build_profile "$core_project"

client_project="$(create_profile "YuoCoreConsumer.UnityClientNetStandard" "netstandard2.1")"
add_package "$client_project" "YuoCore.Client.StateSync"
cat > "$(dirname "$client_project")/ConsumerProbe.cs" <<'CS'
using System;
using YuoCore.Client;
using YuoCore.Client.StateSync;
using YuoCore.Protocol;
using YuoCore.StateSync;

namespace YuoCoreConsumer.UnityClientNetStandard
{
    public static class ConsumerProbe
    {
        public static Type[] PublicTypes()
        {
            return new[]
            {
                typeof(YuoClientSession),
                typeof(YuoMmoClient),
                typeof(YuoMmoClientConnection),
                typeof(YuoClientStateSyncReceiver),
                typeof(YuoStateSyncMirror),
                typeof(HelloRequest)
            };
        }
    }
}
CS
build_profile "$client_project"

server_project="$(create_profile "YuoCoreConsumer.ServerNet10" "net10.0")"
add_package "$server_project" "YuoCore.Server"
add_package "$server_project" "YuoCore.Persistence.Hosting"
add_package "$server_project" "YuoCore.Gateway"
add_package "$server_project" "YuoCore.Network"
add_package "$server_project" "YuoCore.Game"
cat > "$(dirname "$server_project")/ConsumerProbe.cs" <<'CS'
using System;
using YuoCore.Game;
using YuoCore.Gateway;
using YuoCore.Network;
using YuoCore.Persistence;
using YuoCore.Persistence.Hosting;
using YuoCore.Server;

namespace YuoCoreConsumer.ServerNet10
{
    public static class ConsumerProbe
    {
        public static Type[] PublicTypes()
        {
            return new[]
            {
                typeof(YuoServerHost),
                typeof(YuoPersistenceQueue),
                typeof(YuoPersistenceBackgroundService),
                typeof(YuoPersistenceWriterOptions),
                typeof(YuoGatewaySession),
                typeof(IYuoTransport<>),
                typeof(YuoPlayerSnapshotDto)
            };
        }

        public static bool UsesPersistenceStopCompletionOption()
        {
            var options = new YuoPersistenceWriterOptions(CompleteQueueOnStop: false);
            var queue = new YuoPersistenceQueue(capacity: 4);
            return !options.CompleteQueueOnStop &&
                   queue.TryComplete() &&
                   !queue.TryComplete();
        }
    }
}
CS
build_profile "$server_project"

echo "consumer-smoke-output=$OUT_DIR"
