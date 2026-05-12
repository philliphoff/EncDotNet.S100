# EncDotNet.S100.PerfReport

Reads `.jsonl` telemetry files produced by
[PerfRunner](../EncDotNet.S100.PerfRunner/) and generates markdown
summaries or diffs.

## Usage

```bash
# Summarise a single run
dotnet run --project tools/EncDotNet.S100.PerfReport -- summarise perf-runs/20260509-050000-s124-vector.jsonl

# Write summary to a file
dotnet run --project tools/EncDotNet.S100.PerfReport -- summarise run.jsonl --out summary.md

# Diff baseline vs candidate
dotnet run --project tools/EncDotNet.S100.PerfReport -- diff baseline.jsonl candidate.jsonl

# Write diff to a file
dotnet run --project tools/EncDotNet.S100.PerfReport -- diff baseline.jsonl candidate.jsonl --out diff.md
```

## `summarise` output

- Span tree: top-N spans by total duration (name, count, total, mean, max).
- Per histogram: count, sum, min, max.
- Per counter: name, unit, total value.

## `diff` output

For every shared instrument and span name:

| Delta | Meaning |
|-------|---------|
| ❌ | ≥ 5% regression (higher is worse) |
| ✅ | ≥ 10% improvement (lower is better) |
| ▫️ | < 5% change — stable |

### Example diff output

```
## Span duration totals

| Span | Baseline (ms) | Candidate (ms) | Delta | Status |
|------|--------------|----------------|-------|--------|
| s100.pipeline.vector.process | 142.50 | 135.20 | -5.1% | ▫️ |
| s100.pipeline.vector.stage.lua | 89.30 | 72.10 | -19.3% | ✅ |
| s100.render.frame | 53.20 | 63.10 | +18.6% | ❌ |
```

## `gate` output

The `gate` command compares all `.jsonl` files in a baseline directory
against matching files in a candidate directory:

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- gate \
    /tmp/perf-baseline/interleaved \
    /tmp/perf-candidate/interleaved \
    --threshold 10 \
    --min-abs 100 \
    --mad-k 3.0 \
    --retry-zone-mult 2.0 \
    --out gate-report.md
```

### Evaluation model

When the candidate `.jsonl` contains `perf.iteration` activities (the
modern path produced by `tools/perf/interleave.sh`), gating uses
**median + MAD** rather than mean-of-sum:

- `base_med = median(baseline iteration durations)`
- `cand_med = median(candidate iteration durations)`
- `mad_base = median(|x − base_med|)` over baseline iterations
- `pct_delta = (cand_med − base_med) / base_med × 100`
- `z = (cand_med − base_med) / max(mad_base, ε)`

A scenario is flagged as a regression only when **all** of:
- `pct_delta ≥ --threshold` (default 10%)
- `z ≥ --mad-k` (default 3.0)
- `base_med ≥ --min-abs` (default 100ms)

The `z ≥ mad-k` requirement keeps the gate quiet on inherently noisy
scenarios — a 12% delta on a scenario with 8% MAD is below z = 3.0
and won't fire.

### Suspicious-zone retry

`--retry-zone-mult <F>` (default 2.0) splits the regression band into
two tiers:

| Status | Condition | Action |
|--------|-----------|--------|
| ✅ pass | `pct_delta < threshold` OR `z < mad-k` OR `base_med < min-abs` | none |
| ⚠️ suspicious | over the gate but `pct_delta < F·threshold` OR `z < F·mad-k` | name appended to `${out}.suspicious.txt`; **exit 0** |
| ❌ regressed | `pct_delta ≥ F·threshold` AND `z ≥ F·mad-k` | exit 2 |

The CI workflow reads `suspicious.txt`; if non-empty, it re-runs the
interleave loop for only those scenarios (5 more rounds → +20 samples
per side) and re-invokes `gate` with `--retry-zone-mult 1.0` so the
final pass is binary.

When no `perf.iteration` records are present, the gate falls back to
the legacy mean-of-sum-of-spans comparison so older baselines and
ad-hoc runs still work.

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | All scenarios pass or merely suspicious |
| 1 | Input error (missing dirs, no matching files) |
| 2 | One or more scenarios regressed — fail |

## Notes

- The tool reads schema version 1 files only. It will refuse files with
  a mismatched version header.
- No charts in this version — tables only. Visualisation can be added
  later; the priority is output that flows into PR descriptions.
