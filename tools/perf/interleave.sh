#!/usr/bin/env bash
#
# interleave.sh — Drive the perf gate's interleaved baseline/candidate
# loop. Designed for CI runners where shared-tenant noise can swing
# any single block of measurements by tens of percent. By alternating
# small batches of iterations between the two sides — and randomising
# which side leads each round — both sides see the same noise
# distribution, so the median+MAD comparison in the perf-report `gate`
# command isolates real regressions.
#
# Usage:
#   tools/perf/interleave.sh \
#       --base-runner <path-to-PerfRunner-built-from-base-sha> \
#       --cand-runner <path-to-PerfRunner-built-from-candidate-sha> \
#       --base-out    <baseline-jsonl-output-dir> \
#       --cand-out    <candidate-jsonl-output-dir> \
#       --corpus      <test-corpus-dir> \
#       [--rounds N]  (default: 5)
#       [--iters M]   (default: 4)
#       [--warmup W]  (default: 3)
#       [--scenarios csv]  (default: all)
#
# A "runner" path is the dotnet-built PerfRunner.dll (or the AOT exe);
# the script invokes it via `dotnet <path>` so it works for both.
#
set -euo pipefail

ROUNDS=5
ITERS=4
WARMUP=3
SCENARIOS=""
BASE_RUNNER=""
CAND_RUNNER=""
BASE_OUT=""
CAND_OUT=""
CORPUS=""
SUBDIR="interleaved"

usage() {
    sed -n '2,30p' "$0"
    exit 2
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --base-runner) BASE_RUNNER="$2"; shift 2;;
        --cand-runner) CAND_RUNNER="$2"; shift 2;;
        --base-out)    BASE_OUT="$2";    shift 2;;
        --cand-out)    CAND_OUT="$2";    shift 2;;
        --corpus)      CORPUS="$2";      shift 2;;
        --rounds)      ROUNDS="$2";      shift 2;;
        --iters)       ITERS="$2";       shift 2;;
        --warmup)      WARMUP="$2";      shift 2;;
        --scenarios)   SCENARIOS="$2";   shift 2;;
        --subdir)      SUBDIR="$2";      shift 2;;
        -h|--help)     usage;;
        *) echo "unknown arg: $1" >&2; usage;;
    esac
done

for v in BASE_RUNNER CAND_RUNNER BASE_OUT CAND_OUT CORPUS; do
    if [[ -z "${!v}" ]]; then
        echo "error: --$(echo "$v" | tr '[:upper:]_' '[:lower:]-') is required" >&2
        exit 2
    fi
done

mkdir -p "$BASE_OUT" "$CAND_OUT"

# Resolve final per-round output directories. We pass --out-subdir so
# both sides write into a deterministic, predictable folder regardless
# of which git SHA is checked out — the orchestrator owns the layout.
BASE_OUT_FULL="${BASE_OUT}/${SUBDIR}"
CAND_OUT_FULL="${CAND_OUT}/${SUBDIR}"

# Wipe any pre-existing per-scenario .jsonl files so we start clean.
rm -f "${BASE_OUT_FULL}"/*.jsonl 2>/dev/null || true
rm -f "${CAND_OUT_FULL}"/*.jsonl 2>/dev/null || true

scenario_arg=()
if [[ -n "$SCENARIOS" ]]; then
    scenario_arg=(--scenarios "$SCENARIOS")
fi

run_side() {
    local label="$1" runner="$2" out="$3" round="$4" append_flag="$5"
    local extra=()
    if [[ "$append_flag" == "append" ]]; then
        extra=(--append)
    fi
    echo "::group::round ${round} — ${label}"
    dotnet "$runner" baseline \
        --out "$out" \
        --out-subdir "$SUBDIR" \
        --corpus "$CORPUS" \
        --warmup "$WARMUP" \
        --iterations "$ITERS" \
        --round-tag "$round" \
        --side "$label" \
        ${extra[@]+"${extra[@]}"} \
        ${scenario_arg[@]+"${scenario_arg[@]}"}
    echo "::endgroup::"
}

for round in $(seq 1 "$ROUNDS"); do
    # Randomise the order each round so any global drift across the
    # round (e.g. JIT cache warmup, runner-wide CPU steal trend) is
    # spread evenly across the two sides over many rounds.
    if (( RANDOM % 2 == 0 )); then
        first_label="baseline"; first_runner="$BASE_RUNNER"; first_out="$BASE_OUT"
        second_label="candidate"; second_runner="$CAND_RUNNER"; second_out="$CAND_OUT"
    else
        first_label="candidate"; first_runner="$CAND_RUNNER"; first_out="$CAND_OUT"
        second_label="baseline"; second_runner="$BASE_RUNNER"; second_out="$BASE_OUT"
    fi

    # First call per side overwrites; subsequent calls append.
    if [[ "$round" -eq 1 ]]; then
        first_mode="fresh"; second_mode="fresh"
    else
        first_mode="append"; second_mode="append"
    fi

    run_side "$first_label"  "$first_runner"  "$first_out"  "$round" "$first_mode"
    run_side "$second_label" "$second_runner" "$second_out" "$round" "$second_mode"
done

echo "Interleave complete: $ROUNDS rounds × $ITERS iterations = $((ROUNDS * ITERS)) samples per side."
echo "  baseline jsonl:  $BASE_OUT_FULL"
echo "  candidate jsonl: $CAND_OUT_FULL"
