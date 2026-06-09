#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOAD_DIR="${1:-}"

usage() {
  cat <<'USAGE'
Usage:
  scripts/summarize-load-results.sh [artifacts/load/<run-dir>]

If no directory is provided, the latest artifacts/load/* directory is used.
The script writes summary.md into the selected load result directory.
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if (($# > 1)); then
  echo "too many arguments" >&2
  usage >&2
  exit 64
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required to summarize load result JSON files" >&2
  exit 69
fi

if [[ -z "$LOAD_DIR" ]]; then
  LOAD_DIR="$(find "$ROOT_DIR/artifacts/load" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort | tail -n 1 || true)"
  if [[ -z "$LOAD_DIR" ]]; then
    echo "no load result directory found under artifacts/load" >&2
    exit 66
  fi
fi

if [[ ! -d "$LOAD_DIR" ]]; then
  echo "missing load result directory: $LOAD_DIR" >&2
  exit 66
fi

mapfile -t json_files < <(find "$LOAD_DIR" -maxdepth 1 -type f -name '*.json' | sort)
if ((${#json_files[@]} == 0)); then
  echo "no JSON result files found in: $LOAD_DIR" >&2
  exit 65
fi

summary="$LOAD_DIR/summary.md"
failed=0
generated_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
dotnet_version="$(dotnet --version 2>/dev/null || echo "unknown")"
os_info="$(uname -srmo 2>/dev/null || uname -a 2>/dev/null || echo "unknown")"
cpu_model="$(lscpu 2>/dev/null | awk -F: '/^Model name:/ { gsub(/^[ \t]+/, "", $2); print $2; exit }')"
cpu_model="${cpu_model:-unknown}"
logical_cpus="$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo "unknown")"
memory_mb="$(awk '/^MemTotal:/ { printf "%.0f", $2 / 1024 }' /proc/meminfo 2>/dev/null || true)"
memory_mb="${memory_mb:-unknown}"
git_head="$(git -C "$ROOT_DIR" rev-parse --short HEAD 2>/dev/null || echo "unknown")"
git_state="unknown"
if git -C "$ROOT_DIR" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  if [[ -n "$(git -C "$ROOT_DIR" status --porcelain 2>/dev/null)" ]]; then
    git_state="dirty"
  else
    git_state="clean"
  fi
fi

{
  echo "# YuoCore Load Summary"
  echo
  echo "目录：\`$LOAD_DIR\`"
  echo
  echo "环境："
  echo
  echo "- generated_at_utc: \`$generated_at\`"
  echo "- os: \`$os_info\`"
  echo "- cpu: \`$cpu_model\`"
  echo "- logical_cpus: \`$logical_cpus\`"
  echo "- memory_mb: \`$memory_mb\`"
  echo "- dotnet_version: \`$dotnet_version\`"
  echo "- git_head: \`$git_head\`"
  echo "- git_state: \`$git_state\`"
  echo
  echo "| 场景 | 通过 | ticks | tick p95 ms | tick max ms | 分配 MB | 编码 p95 ms | 编码分配 MB | 编码字节 MB | 主要热点 | 热点 p95 ms |"
  echo "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: |"

  for file in "${json_files[@]}"; do
    scenario="$(basename "$file" .json)"
    row="$(jq -r --arg scenario "$scenario" '
      def fnum($v): if $v == null then "-" else ($v | tonumber | . * 1000 | round / 1000 | tostring) end;
      def bytesmb($v): if $v == null then "-" else (($v | tonumber) / 1024 / 1024 * 1000 | round / 1000 | tostring) end;
      def hotspot:
        if .SegmentsMs then
          (.SegmentsMs
            | to_entries
            | map({ name: .key, p95: (.value.P95 // 0) })
            | sort_by(.p95)
            | reverse
            | .[0])
        elif .DrainMs then
          ([
            { name: "move", p95: (.MoveMs.P95 // 0) },
            { name: "drain", p95: (.DrainMs.P95 // 0) },
            { name: "websocket-encode", p95: (.EncodeMs.P95 // 0) },
            { name: "publish-drain", p95: (.PublishDrainMs.P95 // 0) },
            { name: "session-drain", p95: (.SessionDrainMs.P95 // 0) }
          ] | sort_by(.p95) | reverse | .[0])
        else
          { name: "tick", p95: (.TickMs.P95 // 0) }
        end;
      hotspot as $hot |
      [
        $scenario,
        (if .Passed then "yes" else "no" end),
        ((.ActualTicks // .Ticks // 0) | tostring),
        fnum(.TickMs.P95),
        fnum(.TickMs.Max),
        fnum(.AllocatedMb),
        fnum(.EncodeMs.P95),
        fnum(.EncodeAllocatedMb),
        bytesmb(.EncodedBytes),
        $hot.name,
        fnum($hot.p95)
      ] | @tsv
    ' "$file")"

    IFS=$'\t' read -r scenario_name passed ticks p95 max allocated encode_p95 encode_allocated encoded_bytes hotspot hotspot_p95 <<< "$row"
    echo "| $scenario_name | $passed | $ticks | $p95 | $max | $allocated | $encode_p95 | $encode_allocated | $encoded_bytes | $hotspot | $hotspot_p95 |"
    if [[ "$passed" != "yes" ]]; then
      failed=1
    fi
  done

  echo
  echo "说明："
  echo
  echo "- 主要热点按每个场景内的分段 p95 选择；没有分段的场景使用 tick 本身。"
  echo "- quick 阈值用于发现明显退化，不代表商业发布 SLA。"
  echo "- soak 结果应结合 CSV、硬件配置和 GC 指标单独分析。"
} > "$summary"

echo "load-summary=$summary"

if ((failed != 0)); then
  echo "one or more load scenarios failed; see $summary" >&2
  exit 2
fi
