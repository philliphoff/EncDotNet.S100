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

## Notes

- The tool reads schema version 1 files only. It will refuse files with
  a mismatched version header.
- No charts in this version — tables only. Visualisation can be added
  later; the priority is output that flows into PR descriptions.
