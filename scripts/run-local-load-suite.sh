#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODE="quick"

usage() {
  cat <<'USAGE'
Usage:
  scripts/run-local-load-suite.sh [--quick|--soak]

Environment overrides:
  YUO_LOAD_PLAYERS              default: 10000
  YUO_LOAD_STATE_ENTITIES       default: 10000
  YUO_LOAD_SHARDS               default: 4
  YUO_LOAD_TICKS                default: 5
  YUO_LOAD_DURATION_SECONDS     default: 120
  YUO_LOAD_AOI_OBSERVERS        default: 256
  YUO_LOAD_PERSISTENCE_PER_TICK default: 10000
  YUO_LOAD_PAYLOAD_PROFILE      default: allocated
  YUO_LOAD_PERSISTENCE_ENQUEUE  default: single
  YUO_LOAD_POOLED_ENCODE        default: true
  YUO_LOAD_COMPARE_PREVIOUS     default: true
  YUO_LOAD_BASELINE_DIR         explicit baseline directory for comparison
  YUO_LOAD_FAIL_TICK_P95_DELTA_PCT optional compare failure threshold
  YUO_LOAD_FAIL_ALLOC_DELTA_PCT    optional compare failure threshold

The suite writes JSON/CSV results under artifacts/load/<timestamp>/.
USAGE
}

while (($# > 0)); do
  case "$1" in
    --quick)
      MODE="quick"
      shift
      ;;
    --soak)
      MODE="soak"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "unknown argument: $1" >&2
      usage >&2
      exit 64
      ;;
  esac
done

PLAYERS="${YUO_LOAD_PLAYERS:-10000}"
STATE_ENTITIES="${YUO_LOAD_STATE_ENTITIES:-10000}"
SHARDS="${YUO_LOAD_SHARDS:-4}"
TICKS="${YUO_LOAD_TICKS:-5}"
DURATION_SECONDS="${YUO_LOAD_DURATION_SECONDS:-120}"
AOI_OBSERVERS="${YUO_LOAD_AOI_OBSERVERS:-256}"
PERSISTENCE_PER_TICK="${YUO_LOAD_PERSISTENCE_PER_TICK:-10000}"
PAYLOAD_PROFILE="${YUO_LOAD_PAYLOAD_PROFILE:-allocated}"
PERSISTENCE_ENQUEUE_MODE="${YUO_LOAD_PERSISTENCE_ENQUEUE:-single}"
POOLED_ENCODE="${YUO_LOAD_POOLED_ENCODE:-true}"
COMPARE_PREVIOUS="${YUO_LOAD_COMPARE_PREVIOUS:-true}"
BASELINE_DIR="${YUO_LOAD_BASELINE_DIR:-}"
FAIL_TICK_P95_DELTA_PCT="${YUO_LOAD_FAIL_TICK_P95_DELTA_PCT:-}"
FAIL_ALLOC_DELTA_PCT="${YUO_LOAD_FAIL_ALLOC_DELTA_PCT:-}"

RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_DIR="$ROOT_DIR/artifacts/load/$RUN_ID-$MODE"
PROJECT="$ROOT_DIR/tools/YuoCore.LoadTests/YuoCore.LoadTests.csproj"

mkdir -p "$OUT_DIR"

echo "YuoCore local load suite"
echo "mode=$MODE out=$OUT_DIR"
echo "players=$PLAYERS state-entities=$STATE_ENTITIES shards=$SHARDS ticks=$TICKS duration=$DURATION_SECONDS payload-profile=$PAYLOAD_PROFILE persistence-enqueue=$PERSISTENCE_ENQUEUE_MODE pooled-encode=$POOLED_ENCODE"

dotnet build "$PROJECT" -c Release --disable-build-servers

# 本地综合压测覆盖 Actor command 入队、GameRuntime tick、StateSync delta、AOI、Persistence 队列。
# 阈值用于发现明显退化，不代表商业发布 SLA；长期结论仍需要 duration-mode 和线上硬件基线。
if [[ "$MODE" == "soak" ]]; then
  dotnet run -c Release --no-build --project "$PROJECT" -- \
    --mode local-composite \
    --players "$PLAYERS" \
    --duration "$DURATION_SECONDS" \
    --duration-mode \
    --shards "$SHARDS" \
    --state-entities "$STATE_ENTITIES" \
    --aoi-observers "$AOI_OBSERVERS" \
    --persistence-per-tick "$PERSISTENCE_PER_TICK" \
    --payload-profile "$PAYLOAD_PROFILE" \
    --persistence-enqueue-mode "$PERSISTENCE_ENQUEUE_MODE" \
    --aoi-result-mode count \
    --json-output "$OUT_DIR/local-composite-count.json" \
    --csv-output "$OUT_DIR/local-composite-count.csv" \
    --fail-p95-ms 250 \
    --fail-max-ms 500
else
  dotnet run -c Release --no-build --project "$PROJECT" -- \
    --mode local-composite \
    --players "$PLAYERS" \
    --ticks "$TICKS" \
    --shards "$SHARDS" \
    --state-entities "$STATE_ENTITIES" \
    --aoi-observers "$AOI_OBSERVERS" \
    --persistence-per-tick "$PERSISTENCE_PER_TICK" \
    --payload-profile "$PAYLOAD_PROFILE" \
    --persistence-enqueue-mode "$PERSISTENCE_ENQUEUE_MODE" \
    --aoi-result-mode count \
    --json-output "$OUT_DIR/local-composite-count.json" \
    --fail-p95-ms 250 \
    --fail-max-ms 500
fi

# AOI Stream 压测覆盖 MMO 状态同步的共享 frame、dirty stream、订阅过滤和多 session 下发路径。
# 它关闭旧 WorldEvent 广播，避免把兼容旧接口的全图事件成本混进新 StateSync 路径。
if [[ "$MODE" == "soak" ]]; then
  dotnet run -c Release --no-build --project "$PROJECT" -- \
    --mode aoi-streams \
    --players "$PLAYERS" \
    --duration "$DURATION_SECONDS" \
    --duration-mode \
    --shards "$SHARDS" \
    --aoi-observers "$AOI_OBSERVERS" \
    --json-output "$OUT_DIR/aoi-streams.json" \
    --fail-p95-ms 250 \
    --fail-max-ms 500
else
  dotnet run -c Release --no-build --project "$PROJECT" -- \
    --mode aoi-streams \
    --players "$PLAYERS" \
    --ticks "$TICKS" \
    --shards "$SHARDS" \
    --aoi-observers "$AOI_OBSERVERS" \
    --json-output "$OUT_DIR/aoi-streams.json" \
    --fail-p95-ms 250 \
    --fail-max-ms 500
fi

# 真实 WebSocket 发送前还会进行 envelope 编码。这里使用 pooled encode 模式覆盖生产发送路径的
# ArrayPool<byte> 租用/写入/归还成本，避免只看纯 AOI drain 时漏掉协议编码热点。
case "$POOLED_ENCODE" in
  true|TRUE|1|yes|YES)
    if [[ "$MODE" == "soak" ]]; then
      dotnet run -c Release --no-build --project "$PROJECT" -- \
        --mode aoi-streams \
        --players "$PLAYERS" \
        --duration "$DURATION_SECONDS" \
        --duration-mode \
        --shards "$SHARDS" \
        --aoi-observers "$AOI_OBSERVERS" \
        --pooled-encode-pushes \
        --json-output "$OUT_DIR/aoi-streams-pooled-encode.json" \
        --fail-p95-ms 250 \
        --fail-max-ms 500
    else
      dotnet run -c Release --no-build --project "$PROJECT" -- \
        --mode aoi-streams \
        --players "$PLAYERS" \
        --ticks "$TICKS" \
        --shards "$SHARDS" \
        --aoi-observers "$AOI_OBSERVERS" \
        --pooled-encode-pushes \
        --json-output "$OUT_DIR/aoi-streams-pooled-encode.json" \
        --fail-p95-ms 250 \
        --fail-max-ms 500
    fi
    ;;
  false|FALSE|0|no|NO)
    echo "skip pooled WebSocket encode load scenario"
    ;;
  *)
    echo "invalid YUO_LOAD_POOLED_ENCODE=$POOLED_ENCODE; expected true/false" >&2
    exit 64
    ;;
esac

# ECS shape 压测复用 100 组件 / 100 system 的多 shard benchmark workload。
# 它验证框架在极端组件组合下的调度成本，避免只看业务示例导致 ECS 层热点被遮住。
if [[ "$MODE" == "soak" ]]; then
  dotnet run -c Release --no-build --project "$PROJECT" -- \
    --mode ecs-shape \
    --state-entities "$STATE_ENTITIES" \
    --duration "$DURATION_SECONDS" \
    --duration-mode \
    --shards "$SHARDS" \
    --json-output "$OUT_DIR/ecs-shape.json" \
    --fail-p95-ms 1000 \
    --fail-max-ms 1500
else
  dotnet run -c Release --no-build --project "$PROJECT" -- \
    --mode ecs-shape \
    --state-entities "$STATE_ENTITIES" \
    --ticks "$TICKS" \
    --shards "$SHARDS" \
    --json-output "$OUT_DIR/ecs-shape.json" \
    --fail-p95-ms 1000 \
    --fail-max-ms 1500
fi

"$ROOT_DIR/scripts/summarize-load-results.sh" "$OUT_DIR"

case "$COMPARE_PREVIOUS" in
  true|TRUE|1|yes|YES)
    if [[ -z "$BASELINE_DIR" ]]; then
      BASELINE_DIR="$(find "$ROOT_DIR/artifacts/load" -mindepth 1 -maxdepth 1 -type d -name "*-$MODE" 2>/dev/null \
        | sort \
        | grep -v -F "$OUT_DIR" \
        | tail -n 1 || true)"
    fi

    if [[ -n "$BASELINE_DIR" ]]; then
      compare_args=(
        "$BASELINE_DIR"
        "$OUT_DIR"
        --output "$OUT_DIR/comparison.md"
      )

      if [[ -n "$FAIL_TICK_P95_DELTA_PCT" ]]; then
        compare_args+=(--fail-tick-p95-delta-pct "$FAIL_TICK_P95_DELTA_PCT")
      fi

      if [[ -n "$FAIL_ALLOC_DELTA_PCT" ]]; then
        compare_args+=(--fail-alloc-delta-pct "$FAIL_ALLOC_DELTA_PCT")
      fi

      "$ROOT_DIR/scripts/compare-load-results.sh" "${compare_args[@]}"
    else
      echo "load-comparison=skipped-no-baseline"
    fi
    ;;
  false|FALSE|0|no|NO)
    echo "load-comparison=disabled"
    ;;
  *)
    echo "invalid YUO_LOAD_COMPARE_PREVIOUS=$COMPARE_PREVIOUS; expected true/false" >&2
    exit 64
    ;;
esac

echo "load-suite-output=$OUT_DIR"
