#!/usr/bin/env bash
set -euo pipefail

BASELINE_DIR="${1:-}"
CURRENT_DIR="${2:-}"
OUTPUT_PATH=""
FAIL_TICK_P95_DELTA_PCT=""
FAIL_ALLOC_DELTA_PCT=""
comparison_failed=0

usage() {
  cat <<'USAGE'
Usage:
  scripts/compare-load-results.sh <baseline-load-dir> <current-load-dir> [--output <summary.md>]
    [--fail-tick-p95-delta-pct <percent>]
    [--fail-alloc-delta-pct <percent>]

Compares matching JSON result files produced by scripts/run-local-load-suite.sh.
The report is informational by default. Failure thresholds are opt-in and only
apply when the compared scenario config matches.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if (($# < 2)); then
  usage >&2
  exit 64
fi

shift 2
while (($# > 0)); do
  case "$1" in
    --output)
      if (($# < 2)); then
        echo "--output requires a path" >&2
        exit 64
      fi
      OUTPUT_PATH="$2"
      shift 2
      ;;
    --fail-tick-p95-delta-pct)
      if (($# < 2)); then
        echo "--fail-tick-p95-delta-pct requires a number" >&2
        exit 64
      fi
      FAIL_TICK_P95_DELTA_PCT="$2"
      shift 2
      ;;
    --fail-alloc-delta-pct)
      if (($# < 2)); then
        echo "--fail-alloc-delta-pct requires a number" >&2
        exit 64
      fi
      FAIL_ALLOC_DELTA_PCT="$2"
      shift 2
      ;;
    *)
      echo "unknown argument: $1" >&2
      usage >&2
      exit 64
      ;;
  esac
done

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to compare load result JSON files" >&2
  exit 69
fi

if [[ ! -d "$BASELINE_DIR" ]]; then
  echo "missing baseline directory: $BASELINE_DIR" >&2
  exit 66
fi

if [[ ! -d "$CURRENT_DIR" ]]; then
  echo "missing current directory: $CURRENT_DIR" >&2
  exit 66
fi

validate_number() {
  local name="$1"
  local value="$2"
  if [[ -z "$value" ]]; then
    return
  fi

  if ! awk -v value="$value" 'BEGIN { exit !(value ~ /^-?[0-9]+([.][0-9]+)?$/) }'; then
    echo "$name must be a number, got: $value" >&2
    exit 64
  fi
}

validate_number "--fail-tick-p95-delta-pct" "$FAIL_TICK_P95_DELTA_PCT"
validate_number "--fail-alloc-delta-pct" "$FAIL_ALLOC_DELTA_PCT"

mapfile -t scenario_names < <(
  for directory in "$BASELINE_DIR" "$CURRENT_DIR"; do
    find "$directory" -maxdepth 1 -type f -name '*.json' -printf '%f\n'
  done | sed 's/\.json$//' | sort -u
)
if ((${#scenario_names[@]} == 0)); then
  echo "no JSON result files found in baseline/current directories" >&2
  exit 65
fi

emit_report() {
  echo "# YuoCore Load Comparison"
  echo
  echo "baseline: \`$BASELINE_DIR\`"
  echo "current: \`$CURRENT_DIR\`"
  echo
  echo "| scenario | baseline pass | current pass | config match | tick p95 ms | tick delta | alloc MB | alloc delta | encode p95 ms | encode delta |"
  echo "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |"

  for scenario in "${scenario_names[@]}"; do
    current_file="$CURRENT_DIR/$scenario.json"
    baseline_file="$BASELINE_DIR/$scenario.json"
    if [[ ! -f "$baseline_file" ]]; then
      echo "| $scenario | missing | $(jq -r 'if .Passed then "yes" else "no" end' "$current_file") | - | - | - | - | - | - | - |"
      continue
    fi

    if [[ ! -f "$current_file" ]]; then
      echo "| $scenario | $(jq -r 'if .Passed then "yes" else "no" end' "$baseline_file") | missing | - | - | - | - | - | - | - |"
      continue
    fi

    row="$(jq -nr \
      --slurpfile baseline "$baseline_file" \
      --slurpfile current "$current_file" \
      --arg scenario "$scenario" '
      def n($v): if $v == null then null else ($v | tonumber) end;
      def f($v): if $v == null then "-" else ($v | tonumber | . * 1000 | round / 1000 | tostring) end;
      def pair($base; $cur): (f($base) + " -> " + f($cur));
      def cfg($x): {
        players: ($x.Players // null),
        entities: ($x.Entities // $x.StateEntities // null),
        shards: ($x.Shards // null),
        observers: ($x.Observers // null),
        componentTypes: ($x.ComponentTypes // null),
        systemTypes: ($x.SystemTypes // null),
        durationMode: ($x.DurationMode // null),
        ticks: ($x.Ticks // null)
      };
      def pct($base; $cur):
        if $base == null or $cur == null or ($base | tonumber) == 0 then
          "-"
        else
          (((($cur | tonumber) - ($base | tonumber)) / ($base | tonumber) * 100) * 1000 | round / 1000 | tostring) + "%"
        end;
      def pctnum($base; $cur):
        if $base == null or $cur == null or ($base | tonumber) == 0 then
          ""
        else
          (((($cur | tonumber) - ($base | tonumber)) / ($base | tonumber) * 100) * 1000 | round / 1000 | tostring)
        end;
      ($baseline[0]) as $b |
      ($current[0]) as $c |
      [
        $scenario,
        (if $b.Passed then "yes" else "no" end),
        (if $c.Passed then "yes" else "no" end),
        (if cfg($b) == cfg($c) then "yes" else "no" end),
        pair(n($b.TickMs.P95); n($c.TickMs.P95)),
        pct(n($b.TickMs.P95); n($c.TickMs.P95)),
        pair(n($b.AllocatedMb); n($c.AllocatedMb)),
        pct(n($b.AllocatedMb); n($c.AllocatedMb)),
        pair(n($b.EncodeMs.P95); n($c.EncodeMs.P95)),
        pct(n($b.EncodeMs.P95); n($c.EncodeMs.P95)),
        pctnum(n($b.TickMs.P95); n($c.TickMs.P95)),
        pctnum(n($b.AllocatedMb); n($c.AllocatedMb))
      ] | @tsv
    ')"

    IFS=$'\t' read -r name base_pass current_pass config_match tick_pair tick_delta alloc_pair alloc_delta encode_pair encode_delta tick_delta_raw alloc_delta_raw <<< "$row"
    echo "| $name | $base_pass | $current_pass | $config_match | $tick_pair | $tick_delta | $alloc_pair | $alloc_delta | $encode_pair | $encode_delta |"

    if [[ "$config_match" == "yes" && -n "$FAIL_TICK_P95_DELTA_PCT" && -n "$tick_delta_raw" ]]; then
      if awk -v actual="$tick_delta_raw" -v threshold="$FAIL_TICK_P95_DELTA_PCT" 'BEGIN { exit !(actual > threshold) }'; then
        echo "regression: $name tick p95 delta ${tick_delta_raw}% > ${FAIL_TICK_P95_DELTA_PCT}%" >&2
        comparison_failed=2
      fi
    fi

    if [[ "$config_match" == "yes" && -n "$FAIL_ALLOC_DELTA_PCT" && -n "$alloc_delta_raw" ]]; then
      if awk -v actual="$alloc_delta_raw" -v threshold="$FAIL_ALLOC_DELTA_PCT" 'BEGIN { exit !(actual > threshold) }'; then
        echo "regression: $name allocated delta ${alloc_delta_raw}% > ${FAIL_ALLOC_DELTA_PCT}%" >&2
        comparison_failed=2
      fi
    fi
  done

  echo
  echo "Notes:"
  echo
  echo "- Positive delta means current is higher than baseline."
  echo "- Missing baseline rows usually mean the current suite added a new scenario."
  echo "- Missing current rows usually mean the current suite stopped producing a previous scenario."
  echo "- Delta values are only comparable when config match is yes."
  if [[ -z "$FAIL_TICK_P95_DELTA_PCT" && -z "$FAIL_ALLOC_DELTA_PCT" ]]; then
    echo "- This report is informational; decide thresholds per hardware and scenario."
  else
    echo "- Failure thresholds are enabled for matching configs only."
  fi
}

if [[ -n "$OUTPUT_PATH" ]]; then
  mkdir -p "$(dirname "$OUTPUT_PATH")"
  emit_report > "$OUTPUT_PATH"
  echo "load-comparison=$OUTPUT_PATH"
else
  emit_report
fi

if ((comparison_failed != 0)); then
  echo "load-comparison-regression=failed" >&2
  exit "$comparison_failed"
fi
