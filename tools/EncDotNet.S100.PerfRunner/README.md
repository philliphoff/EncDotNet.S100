# EncDotNet.S100.PerfRunner

Scripted performance scenario runner for EncDotNet.S100 pipelines and
renderers. Produces reproducible, comparable telemetry files that can be
summarised and diffed by the companion
[PerfReport](../EncDotNet.S100.PerfReport/) tool.

## Quick start

```bash
# Run the S-124 vector scenario with defaults
dotnet run --project tools/EncDotNet.S100.PerfRunner -- s124-vector

# List all available scenarios
dotnet run --project tools/EncDotNet.S100.PerfRunner -- list

# Full options
dotnet run --project tools/EncDotNet.S100.PerfRunner -- s101-portray-warm \
    --corpus tests/datasets \
    --out ./perf-runs \
    --warmup 3 \
    --iterations 20 \
    --tag branch=main \
    --tag commit=abc1234

# Saturate a running viewer's tile workers through its MCP endpoint
dotnet run --project tools/EncDotNet.S100.PerfRunner -- viewer-stress \
    --port-file /tmp/viewer-stress/mcp.url \
    --bbox 49.8,-6.5,59.0,2.0 \
    --zoom-min 6 --zoom-max 12 \
    --steps 96 --cycles 5 --step-delay-ms 0 \
    --out /tmp/viewer-stress
```

## Scenarios

| Name | Description |
|------|-------------|
| `s101-portray-cold` | Single cold-start S-101 parse + portray. No warmup — captures first-pass Lua/XSLT compile cost. |
| `s101-portray-warm` | S-101 portrayal pipeline only (no render) with warmup. Pure pipeline throughput. |
| `s101-render-warm` | S-101 pipeline + headless Mapsui layer build (no Avalonia UI thread, no Skia GPU raster pass). |
| `s102-coverage` | S-102 HDF5 bathymetry: coverage pipeline + render. |
| `s124-vector` | S-124 GML navigational warnings: XSLT-only vector pipeline. |
| `s201-vector` | S-201 GML AtoN information: XSLT-only vector pipeline. |
| `exchange-set-open` | Open a synthetic exchange set and walk all datasets. |

## Interpreting warm numbers: pipeline vs live viewer render

PerfRunner warm scenarios currently measure **library-side pipeline work**
(parse/portray + headless Mapsui layer construction). They do **not** drive
the Avalonia windowing/binding loop or the live Skia GPU render path used by
`EncDotNet.S100.Viewer`.

That means:

- `s101-portray-warm` and `s101-real-warm` capture repeated portrayal + layer
  build cost.
- They do **not** include the viewer's steady-state warm raster cost in
  `Mapsui.Rendering.Skia.MapRenderer.RenderToBitmapStream`.

Use PerfRunner to track pipeline regressions, and run a live viewer trace when
you need warm-rasterise numbers.

### Reusable live viewer stress run

Build the Release viewer, then launch the binary with isolated settings/cache,
the tiled render subsystem, MCP, and file telemetry:

```bash
mkdir -p /tmp/viewer-stress
ENC_DOTNET_OTEL_FILE=/tmp/viewer-stress/tiles.jsonl \
S100_RENDER_SUBSYSTEM=B \
src/EncDotNet.S100.Viewer/bin/Release/net10.0/<rid>/EncDotNet.S100.Viewer \
  --data-dir /tmp/viewer-stress/data \
  --mcp --mcp-port-file /tmp/viewer-stress/mcp.url \
  /path/to/exchange-set
```

Run `viewer-stress` from another terminal. The route is a deterministic WGS-84
snake over `--bbox`; when `--bbox` is omitted, the command uses
`list_datasets` to union the bounds of every loaded dataset. Zoom follows a
triangle wave from `--zoom-min` to `--zoom-max` and back. Each cycle
deliberately issues viewport changes without waiting for render idle, so
`--step-delay-ms 0` creates maximum queue pressure. At the cycle boundary the
command waits for the live map to settle, samples `get_render_stats`, and writes
a JSON manifest containing every requested viewport and MCP round-trip time.
The rolling render-stat window is reset before each cycle, so startup paints and
earlier cycles cannot contaminate that cycle's maxima or percentiles.

For a visible approximation of normal navigation, use
`--scenario navigation --step-delay-ms 100`. This route performs separate
incremental pan and zoom legs instead of changing position and zoom together.

Analyze the trace directly:

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- \
  tile-report /tmp/viewer-stress/tiles.jsonl \
  --out /tmp/viewer-stress/tile-report.md

dotnet run --project tools/EncDotNet.S100.PerfReport -- \
  chrome-trace /tmp/viewer-stress/tiles.jsonl \
  --out /tmp/viewer-stress/tile-timeline.json
```

`tile-report` reports P50/P95/P99 end-to-end tile latency and classifies each
job by its dominant queue, raster, disk-read, disk-write, publish, or
uninstrumented cost. Open the Chrome trace in Perfetto for the worker timeline.
For method-level CPU attribution inside a costly raster span, simultaneously
attach `dotnet-trace` to the viewer process and convert the resulting
`.nettrace` to Speedscope.

### Live viewer warm-render trace recipe

```bash
# 1) Start the viewer normally and load the target dataset.
dotnet run --project src/EncDotNet.S100.Viewer

# 2) In another shell, attach dotnet-trace to the viewer process.
dotnet-trace collect --process-id <viewer-pid> \
    --providers Microsoft-DotNETCore-SampleProfiler
```

Interact with the map (pan/zoom/repaint) to collect warm frames, then inspect
the trace for `Mapsui.Rendering.Skia.MapRenderer.RenderToBitmapStream`.

## Profiling (CPU and allocation traces)

The PerfRunner can capture an in-process EventPipe trace alongside the
`.jsonl` telemetry, suitable for opening in PerfView, Visual Studio's
performance profiler, or — after conversion — Speedscope / Perfetto.

```bash
# CPU sampling profile of the S-131 portrayal pipeline.
dotnet run --project tools/EncDotNet.S100.PerfRunner -- s131-portray-warm \
    --warmup 3 --iterations 20 --profile cpu

# Allocation profile (GC / AllocationTick events).
dotnet run --project tools/EncDotNet.S100.PerfRunner -- s101-portray-cold \
    --warmup 0 --iterations 1 --profile alloc

# Same flags work on `baseline`. Profiling is mutually exclusive with --append.
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline \
    --scenarios s131-portray-warm --profile cpu
```

Output: `<basename>.nettrace` next to the `.jsonl`. Convert to a
flamegraph viewable in <https://www.speedscope.app>:

```bash
dotnet tool install -g dotnet-trace
dotnet-trace convert ./perf-runs/<basename>.nettrace --format speedscope
```

Notes:
- Profiling adds measurable overhead (typically 2-10% for `cpu`,
  20-40% for `alloc`). **Profiled runs are not baselines**: do not
  gate CI against `--profile` outputs.
- Profiling wraps **measured iterations only** so the trace is not
  polluted with JIT and first-touch costs from warmup. For cold
  scenarios (warmup=0, iterations=1) the trace covers the entire run.
- For sub-100ms scenarios, raise the sampling interval with
  `--profile-sampling-interval-ms 5` to reduce overhead.

## Output

Each normal scenario run produces two files in the output directory:

- `<timestamp>-<scenario>.jsonl` — newline-delimited JSON telemetry
  (spans + metrics).
- `<timestamp>-<scenario>.md` — markdown summary with iteration
  statistics.

`viewer-stress` instead produces `<timestamp>-viewer-stress.json`, a manifest
of the driven route, MCP timings, render-idle result, and render statistics.
The viewer process writes its own span/metric `.jsonl` to the path configured by
`ENC_DOTNET_OTEL_FILE`.

### `.jsonl` schema (version 1)

Every line is a JSON object with a `kind` discriminator:

```jsonc
// First line — schema header
{"kind":"header","version":1,"startedAtUtc":"2026-05-09T05:00:00Z"}

// Span line
{"kind":"span","name":"s100.pipeline.vector.stage.lua",
 "traceId":"…","spanId":"…","parentSpanId":"…",
 "startUnixNs":…,"endUnixNs":…,"durationMs":13.4,
 "status":"Ok",
 "tags":{"s100.pipeline.stage":"lua","s100.product":"S-101"}}

// Metric line (histogram)
{"kind":"metric","name":"s100.pipeline.duration",
 "instrument":"histogram","unit":"ms",
 "tags":{"s100.product":"S-101"},
 "buckets":[{"sum":142.5,"count":20,"min":5.1,"max":12.3}]}

// Metric line (counter)
{"kind":"metric","name":"s100.symbol.cache.hit.count",
 "instrument":"counter","unit":"{hits}",
 "tags":{"s100.product":"S-101"},"value":48}
```

## Adding a new scenario

1. Create a class implementing `IPerfScenario` under `Scenarios/`.
2. Register it in `ScenarioRegistry.cs`:

```csharp
Register(() => new MyNewScenario());
```

3. Use `SharedInfrastructure.CreatePipelineFactory()` to get a factory
   pre-configured with all bundled portrayal catalogues, Lua engine,
   and CRS transforms.

## Notes

- The runner sets `ENC_DOTNET_OTEL_FILE` to capture telemetry from all
  `EncDotNet.S100.*` activity sources and meters.
- Cold scenarios run a single iteration with no warmup by design — they
  measure start-up overhead.
- Warm scenarios discard warmup iterations and report distribution stats
  so random noise is bounded.
- Adopting an OTLP collector is a future option; this in-process file
  exporter avoids an out-of-process dependency.
## Baseline runs

The `baseline` command runs **all** registered scenarios in series
with fixed parameters and writes results into a git-SHA-keyed
subdirectory:

```bash
# Run baseline with defaults (warmup 3, iterations 20)
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline

# Custom output directory
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline \
    --out tools/EncDotNet.S100.PerfRunner/baselines
```

### Output layout

```
baselines/
  CURRENT                          ← plain text file with the SHA
  <git-sha>/
    SUMMARY.md                     ← environment + per-scenario headline
    s101-portray-cold.jsonl
    s101-portray-cold.md
    s101-portray-warm.jsonl
    s101-portray-warm.md
    …
```

### Comparing your branch to baseline

```bash
# 1. On your branch, produce a fresh baseline
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline --out /tmp/perf

# 2. Diff each scenario against the committed baseline
BASELINE_SHA=$(cat tools/EncDotNet.S100.PerfRunner/baselines/CURRENT)
for s in s101-portray-cold s101-portray-warm s101-render-warm \
         s102-coverage s124-vector exchange-set-open; do
    dotnet run --project tools/EncDotNet.S100.PerfReport -- diff \
        tools/EncDotNet.S100.PerfRunner/baselines/$BASELINE_SHA/$s.jsonl \
        /tmp/perf/*/$s.jsonl
done
```

> **Noise floor caveat:** A single laptop run is informational, not
> authoritative. Timing values vary with background load, thermal
> throttling, and system state. See [CI gating](#ci-gating) for
> automated regression detection on every PR.

### Corpus

All default scenarios use synthetic fixtures under `tests/datasets/`.
For larger real-world datasets, run
`tools/EncDotNet.S100.PerfRunner/scripts/fetch-corpus.sh` and set
`ENC_DOTNET_PERF_CORPUS` to the cache directory. See
[`tests/perf/corpus/INDEX.md`](../../tests/perf/corpus/INDEX.md) for
the full corpus inventory.

## CI gating (interleaved + median/MAD)

The `.github/workflows/perf.yml` workflow runs on every PR to `main`:

1. Builds the **base** branch's PerfRunner into `/tmp/perf-bin/base/`
   and the **candidate** branch's into `/tmp/perf-bin/cand/` so both
   binaries are simultaneously available.
2. Calls `tools/perf/interleave.sh` which runs **5 rounds × 4
   iterations per side**, alternating which side leads each round
   (random order) so both base and candidate observe the same noise
   distribution. Each measured iteration is wrapped in a
   `perf.iteration` activity tagged with `perf.scenario`,
   `perf.round`, `perf.iter`, and `perf.side`.
3. Runs `perfreport gate` with median + MAD evaluation
   (`--threshold 10 --min-abs 100 --mad-k 3.0 --retry-zone-mult 2.0`).
   Scenarios in the suspicious zone are written to
   `${out}.suspicious.txt` rather than failing immediately.
4. If any scenarios are suspicious, re-runs interleave for **only
   those scenarios** for 5 more rounds, then re-gates with
   `--retry-zone-mult 1.0` (no second retry).
5. Posts a markdown summary to the PR.

### Per-iteration baseline flags

The `baseline` command supports the orchestrator contract:

| Flag | Purpose |
|------|---------|
| `--append` | Append to existing per-scenario `.jsonl` files instead of overwriting; suppresses per-scenario `.md` and `SUMMARY.md` regeneration. |
| `--round-tag <N>` | Stamp every `perf.iteration` activity with `perf.round=<N>`. |
| `--side <baseline\|candidate>` | Stamp every iteration with `perf.side`. |
| `--scenarios <csv>` | Restrict the run to the named scenarios (used by retry). |
| `--out-subdir <name>` | Override the git-SHA-derived subdirectory so the orchestrator owns the layout. |

To update the committed baseline after merging perf improvements:

```bash
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline
# Review baselines/<new-sha>/SUMMARY.md, then commit and update CURRENT.
```

## Notes
