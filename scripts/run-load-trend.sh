#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODE="quick"

usage() {
  cat <<'USAGE'
Usage:
  scripts/run-load-trend.sh [--quick|--soak]

Environment:
  YUO_LOAD_TREND_RUNS        default: 3
  YUO_LOAD_TREND_OUTPUT_DIR  default: artifacts/load/trend-<timestamp>-<mode>

All YUO_LOAD_* environment variables are passed through to
scripts/run-local-load-suite.sh. The trend runner disables per-run automatic
comparison unless YUO_LOAD_COMPARE_PREVIOUS is explicitly set by the caller.
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

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to summarize load trend results" >&2
  exit 69
fi

RUNS="${YUO_LOAD_TREND_RUNS:-3}"
if ! awk -v value="$RUNS" 'BEGIN { exit !(value ~ /^[0-9]+$/ && value >= 1) }'; then
  echo "YUO_LOAD_TREND_RUNS must be a positive integer, got: $RUNS" >&2
  exit 64
fi

RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)"
OUT_DIR="${YUO_LOAD_TREND_OUTPUT_DIR:-$ROOT_DIR/artifacts/load/trend-$RUN_ID-$MODE}"
RUNS_FILE="$OUT_DIR/runs.txt"
SUMMARY_FILE="$OUT_DIR/trend-summary.md"

mkdir -p "$OUT_DIR"
: > "$RUNS_FILE"

echo "YuoCore load trend"
echo "mode=$MODE runs=$RUNS out=$OUT_DIR"

for ((run = 1; run <= RUNS; run++)); do
  echo "trend-run=$run/$RUNS"
  log_file="$OUT_DIR/run-$run.log"
  if [[ -z "${YUO_LOAD_COMPARE_PREVIOUS+x}" ]]; then
    YUO_LOAD_COMPARE_PREVIOUS=false "$ROOT_DIR/scripts/run-local-load-suite.sh" "--$MODE" | tee "$log_file"
  else
    "$ROOT_DIR/scripts/run-local-load-suite.sh" "--$MODE" | tee "$log_file"
  fi

  run_dir="$(sed -n 's/^load-suite-output=//p' "$log_file" | tail -n 1)"
  if [[ -z "$run_dir" || ! -d "$run_dir" ]]; then
    echo "failed to find load-suite-output in $log_file" >&2
    exit 65
  fi

  echo "$run_dir" >> "$RUNS_FILE"
done

mapfile -t run_dirs < "$RUNS_FILE"
mapfile -t scenario_names < <(
  for directory in "${run_dirs[@]}"; do
    find "$directory" -maxdepth 1 -type f -name '*.json' -printf '%f\n'
  done | sed 's/\.json$//' | sort -u
)

if ((${#scenario_names[@]} == 0)); then
  echo "no JSON result files found in trend run directories" >&2
  exit 65
fi

{
  echo "# YuoCore Load Trend"
  echo
  echo "mode: \`$MODE\`"
  echo "runs: \`$RUNS\`"
  echo "generated_at_utc: \`$(date -u +%Y-%m-%dT%H:%M:%SZ)\`"
  echo
  echo "run directories:"
  echo
  for directory in "${run_dirs[@]}"; do
    echo "- \`$directory\`"
  done
  echo
  echo "| scenario | files | all passed | tick p95 avg | tick p95 min | tick p95 max | alloc MB avg | alloc MB max | encode p95 avg | encode p95 max |"
  echo "| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"

  for scenario in "${scenario_names[@]}"; do
    files=()
    for directory in "${run_dirs[@]}"; do
      file="$directory/$scenario.json"
      if [[ -f "$file" ]]; then
        files+=("$file")
      fi
    done

    if ((${#files[@]} == 0)); then
      continue
    fi

    row="$(jq -s -r --arg scenario "$scenario" '
      def round3: (. * 1000 | round / 1000);
      def avg($items): if ($items | length) == 0 then null else (($items | add) / ($items | length)) end;
      def fmt($value): if $value == null then "-" else ($value | round3 | tostring) end;
      (map(.TickMs.P95)) as $tick |
      (map(.AllocatedMb)) as $alloc |
      (map(.EncodeMs.P95 // 0)) as $encode |
      [
        $scenario,
        (length | tostring),
        (if all(.Passed == true) then "yes" else "no" end),
        fmt(avg($tick)),
        fmt($tick | min),
        fmt($tick | max),
        fmt(avg($alloc)),
        fmt($alloc | max),
        fmt(avg($encode)),
        fmt($encode | max)
      ] | @tsv
    ' "${files[@]}")"

    IFS=$'\t' read -r name count passed tick_avg tick_min tick_max alloc_avg alloc_max encode_avg encode_max <<< "$row"
    echo "| $name | $count | $passed | $tick_avg | $tick_min | $tick_max | $alloc_avg | $alloc_max | $encode_avg | $encode_max |"
  done

  echo
  echo "Notes:"
  echo
  echo "- This trend summary is for repeated local comparison on the same machine."
  echo "- It does not replace soak CSV analysis, p99/GC pause analysis, or production hardware baselines."
  echo "- Use the per-run summary.md files for detailed hotspot diagnostics."
} > "$SUMMARY_FILE"

echo "load-trend-summary=$SUMMARY_FILE"
