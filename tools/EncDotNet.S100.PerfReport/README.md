# EncDotNet.S100.PerfReport

PerfReport reads the `.jsonl` telemetry files that
[PerfRunner](../EncDotNet.S100.PerfRunner/README.md) and the viewer write, and
turns them into Markdown summaries, diffs, CI gate reports, tile latency reports
and Chrome traces.

| Command | What it does |
|---|---|
| [`summarise`](#summarise) | Summarize one telemetry file. |
| [`diff`](#diff) | Compare a baseline file with a candidate file. |
| [`gate`](#gate) | Compare every scenario in two folders and fail on regressions. |
| [`tile-report`](#tile-report) | Rank and attribute the viewer's tile render jobs. |
| [`chrome-trace`](#chrome-trace) | Convert spans to the Chrome Trace Event Format. |

Run it from the repository root:

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- summarise perf-runs/20260509-050000-s124-vector.jsonl
```

Every command writes Markdown to standard output, or to a file with
`--out <path>`.

The reader expects the [`.jsonl` schema (version 1)](../EncDotNet.S100.PerfRunner/README.md#jsonl-schema-version-1).
It skips lines that aren't valid JSON, such as a line cut short by a
concurrent write.

## `summarise`

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- summarise run.jsonl
dotnet run --project tools/EncDotNet.S100.PerfReport -- summarise run.jsonl --out summary.md
```

The summary has:

- The 20 spans with the largest total duration, with count, total, mean and
  maximum.
- For each histogram: count, sum, minimum and maximum.
- For each counter: name, unit and total value.

## `diff`

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- diff baseline.jsonl candidate.jsonl
dotnet run --project tools/EncDotNet.S100.PerfReport -- diff baseline.jsonl candidate.jsonl --out diff.md
```

For every span name and instrument in both files, `diff` shows the baseline,
the candidate and the change, with a status. Higher values are worse.

| Status | Meaning |
|---|---|
| ❌ | Regression: 5% or more slower. |
| ✅ | Improvement: 10% or more faster. |
| ▫️ | Stable: any other change. |

Example output:

```text
## Span duration totals

| Span | Baseline (ms) | Candidate (ms) | Delta | Status |
|------|--------------|----------------|-------|--------|
| s100.pipeline.vector.process | 142.50 | 135.20 | -5.1% | ▫️ |
| s100.pipeline.vector.stage.lua | 89.30 | 72.10 | -19.3% | ✅ |
| s100.render.frame | 53.20 | 63.10 | +18.6% | ❌ |
```

## `gate`

`gate` compares every `.jsonl` file in a baseline folder with the file of the
same name in a candidate folder. The CI performance gate uses it; see
[CI performance gate](../EncDotNet.S100.PerfRunner/README.md#ci-performance-gate).

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

| Option | Default | Description |
|---|---|---|
| `--threshold <pct>` | `5` | The regression threshold, as a percentage change in the median. |
| `--min-abs <value>` | `50` | A scenario whose baseline median is below this, in milliseconds, never counts as a regression. |
| `--mad-k <k>` | `3.0` | How many baseline MADs the change in the median must exceed. Used only with per-iteration samples. |
| `--retry-zone-mult <f>` | `2.0` | Marks a scenario as suspicious, instead of regressed, when it's over the gate but under `f` times the threshold or `f` times `--mad-k`. `1.0` turns this off. |
| `--min-samples <n>` | `5` | The fewest per-iteration samples on each side for median and MAD gating. With fewer, the gate falls back to span totals. |
| `--out <path>` | — | Write the report to this file, and write the suspicious scenarios to `<path>.suspicious.txt`. |

### How the gate decides

When the files contain `perf.iteration` spans, which PerfRunner's `baseline`
command writes, the gate compares medians:

- `base_med` is the median of the baseline iteration durations.
- `cand_med` is the median of the candidate iteration durations.
- `mad_base` is the median of `|x − base_med|` over the baseline iterations.
- `pct_delta = (cand_med − base_med) / base_med × 100`.
- `z = (cand_med − base_med) / max(mad_base, ε)`.

A scenario is over the gate only when all of these hold:

- `pct_delta ≥ --threshold`
- `z ≥ --mad-k`
- `base_med ≥ --min-abs`

The `z` test keeps noisy scenarios quiet. For example, a 12% change in a
scenario whose MAD is 8% of its median is below `z = 3.0`, so it passes.

`--retry-zone-mult` (`F`) splits the scenarios that are over the gate into two
groups:

| Status | Condition | Effect |
|---|---|---|
| Pass | `pct_delta < threshold`, or `z < mad-k`, or `base_med < min-abs` | None. |
| Suspicious | Over the gate, and `pct_delta < F × threshold` or `z < F × mad-k` | Listed in `<out>.suspicious.txt`. The gate still exits `0`. |
| Regressed | `pct_delta ≥ F × threshold` and `z ≥ F × mad-k` | The gate exits `2`. |

CI reruns the suspicious scenarios with more rounds, then gates again with
`--retry-zone-mult 1.0`, so the final result is pass or fail.

Files without `perf.iteration` spans, such as older baselines and ad hoc runs,
are compared on their total span durations instead.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Every scenario passed or is suspicious. |
| `1` | Input error, such as a missing folder or no matching files. |
| `2` | At least one scenario regressed. |

## `tile-report`

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- tile-report viewer.jsonl
dotnet run --project tools/EncDotNet.S100.PerfReport -- tile-report viewer.jsonl --top 50 --out tiles.md
```

`tile-report` reads the `s100.render.tile.job` spans that the viewer's tile
renderer writes. To capture them, start the viewer with
`ENC_DOTNET_OTEL_FILE=/path/to/viewer.jsonl`. PerfRunner's `viewer-stress`
command generates repeatable pan and zoom load; see
[Stress a running viewer](../EncDotNet.S100.PerfRunner/README.md#stress-a-running-viewer).

The report has:

- **Latency:** P50, P95 and P99 tile latency, from the first time a tile is
  needed on screen to its publication.
- **Dominant cause:** how many jobs were dominated by each cost: queue,
  raster, disk read, disk write, publish or uninstrumented time.
- **Background persistence:** the duration of cache writes, when the trace
  has them.
- **Slowest jobs:** the `--top` slowest jobs (default 20), with the tiles,
  priority, cache outcome, queue wait, raster, disk read, disk write and
  publish time, uninstrumented time, candidate paint-operation count and
  dominant cause.

## `chrome-trace`

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- chrome-trace run.jsonl
dotnet run --project tools/EncDotNet.S100.PerfReport -- chrome-trace run.jsonl --out timeline.json
```

`chrome-trace` converts the spans in a `.jsonl` file to the
[Chrome Trace Event Format](https://docs.google.com/document/d/1CvAClvFfyA5R-PhYUmn5OOQtYMH4h6I0nSsKchNAySU/preview).
Without `--out`, it writes `<file>.chrome.json` next to the input. Open the
result in any of these:

- `chrome://tracing` in a Chromium-based browser
- [Perfetto UI](https://ui.perfetto.dev)
- [Speedscope](https://www.speedscope.app)

Each trace ID gets its own row, so concurrent scenario and iteration activity
appears side by side. Span tags appear as event arguments when you select a
span.

The output is a timeline of the spans the product code records, such as
pipeline stages, Lua execution, HDF5 reads and renderer frames. It doesn't
sample CPU stacks, so it can't show runtime or library frames between spans.
For a CPU flame graph, run PerfRunner with `--profile cpu`; see
[Capture a CPU or allocation profile](../EncDotNet.S100.PerfRunner/README.md#capture-a-cpu-or-allocation-profile).
