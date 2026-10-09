# EncDotNet.S100.PerfRunner

PerfRunner runs scripted performance scenarios against the EncDotNet.S100
pipelines and renderers, and records the telemetry as files you can compare
across runs. It also drives pan and zoom load against a running SoundCharts
viewer. Use [PerfReport](../EncDotNet.S100.PerfReport/README.md) to summarize,
diff and gate the results.

PerfRunner has three commands:

| Command | What it does |
|---|---|
| `run <scenario>` | Runs one scenario. `run list` lists the scenarios. |
| `baseline` | Runs every scenario with fixed settings into a folder named after the current commit. |
| `viewer-stress` | Drives viewport changes against a running viewer's MCP endpoint. |

## Run a scenario

From the repository root:

```bash
dotnet run --project tools/EncDotNet.S100.PerfRunner -- run list
dotnet run --project tools/EncDotNet.S100.PerfRunner -- run s124-vector
```

With every option:

```bash
dotnet run --project tools/EncDotNet.S100.PerfRunner -- run s101-portray-warm \
    --corpus tests/datasets \
    --out ./perf-runs \
    --warmup 3 \
    --iterations 20 \
    --tag branch=main \
    --tag commit=abc1234
```

| Option | Default | Description |
|---|---|---|
| `--corpus <path>` | `tests/datasets` | The folder the scenarios read their datasets from. |
| `--out <dir>` | `./perf-runs` | The folder for the output files. |
| `--warmup <n>` | `3` | Warmup iterations. They run first and aren't measured. |
| `--iterations <n>` | `20` | Measured iterations. |
| `--tag <key=value>` | — | Extra metadata tags (repeatable). |
| `--profile <mode>` | `none` | `none`, `cpu` or `alloc`. See [Capture a CPU or allocation profile](#capture-a-cpu-or-allocation-profile). |
| `--profile-sampling-interval-ms <ms>` | `1` | CPU sampling interval, for `--profile cpu` only. |

Warm scenarios discard the warmup iterations and report the distribution of the
measured ones, which limits the effect of random noise. Cold scenarios measure
start-up cost, so run them with `--warmup 0 --iterations 1`.

## Scenarios

The scenarios read synthetic fixtures from the corpus folder, except the
real-corpus scenarios.

| Name | What it measures |
|---|---|
| `s101-portray-cold` | Open, parse and portray an S-101 fixture once. With no warmup, it includes the first Lua and XSLT compile. |
| `s101-portray-warm` | The S-101 portrayal pipeline without rendering. |
| `s101-render-warm` | The S-101 pipeline plus a headless Mapsui layer build, with no Avalonia UI thread and no Skia GPU pass. |
| `s101-render-repeat` | Open an S-101 dataset once, then render it several times in each iteration. |
| `s102-coverage` | S-102 bathymetry: coverage pipeline and render. |
| `s102-coverage-open` | S-102: open the file and read the dataset header, without rendering. |
| `s102-coverage-render-large` | S-102: render a synthetic grid of about 1000 × 1000. |
| `s102-coverage-render-repeat` | S-102: open once, then render the large synthetic grid several times in each iteration. |
| `s102-coverage-viewport-fit` | S-102: render a viewport that covers about the full grid. |
| `s102-coverage-viewport-zoomed-in` | S-102: render a viewport of 10% of the grid at 800 × 600. |
| `s102-coverage-viewport-zoomed-out` | S-102: render a viewport of 4 times the grid extent at 800 × 600. |
| `s111-arrow-repeat` | S-111: open the bundled surface-current fixture once, then render it several times in each iteration. |
| `s124-vector` | S-124 navigational warnings: the XSLT vector pipeline. |
| `s201-vector` | S-201 aids to navigation information: the XSLT vector pipeline. |
| `exchange-set-open` | Open a synthetic exchange set and walk all its datasets. |
| `s101-real-cold` | Cold start on a real S-101 cell. |
| `s101-real-warm` | Warm pipeline and headless layer build on a real S-101 cell. |
| `s101-pick-warm` | Warm identify (pick) on a real S-101 cell. |
| `s102-real-warm` | Warm coverage render on a real S-102 file. |
| `s111-real-warm` | Warm surface-current render on a real S-111 file. |

### Real-corpus scenarios

The `*-real-*` and `s101-pick-warm` scenarios use licensed trial data that
isn't in the repository. Set an environment variable to the path of one
dataset file:

| Variable | Used by |
|---|---|
| `ENC_DOTNET_PERF_REAL_S101` | `s101-real-cold`, `s101-real-warm`, `s101-pick-warm` (a `.000` file) |
| `ENC_DOTNET_PERF_REAL_S102` | `s102-real-warm` (an `.h5` file) |
| `ENC_DOTNET_PERF_REAL_S111` | `s111-real-warm` (an `.h5` file) |

When the variable isn't set or the file doesn't exist, the scenario fails with
an error that names the variable. CI doesn't run these scenarios.

## Output

Each `run` writes two files to the output folder:

- `<timestamp>-<scenario>.jsonl`: the telemetry, as newline-delimited JSON
  spans and metrics.
- `<timestamp>-<scenario>.md`: a Markdown summary of the iteration statistics.

The runner sets `ENC_DOTNET_OTEL_FILE` so the file exporter captures every
`EncDotNet.S100.*` activity source and meter. It writes to a file in-process,
so it doesn't need an OpenTelemetry collector.

### `.jsonl` schema (version 1)

Every line is a JSON object with a `kind` field that says what the line holds:

```jsonc
// First line: the schema header
{"kind":"header","version":1,"startedAtUtc":"2026-05-09T05:00:00Z"}

// A span
{"kind":"span","name":"s100.pipeline.vector.stage.lua",
 "traceId":"…","spanId":"…","parentSpanId":"…",
 "startUnixNs":…,"endUnixNs":…,"durationMs":13.4,
 "status":"Ok",
 "tags":{"s100.pipeline.stage":"lua","s100.product":"S-101"}}

// A histogram metric
{"kind":"metric","name":"s100.pipeline.duration",
 "instrument":"histogram","unit":"ms",
 "tags":{"s100.product":"S-101"},
 "buckets":[{"sum":142.5,"count":20,"min":5.1,"max":12.3}]}

// A counter metric
{"kind":"metric","name":"s100.symbol.cache.hit.count",
 "instrument":"counter","unit":"{hits}",
 "tags":{"s100.product":"S-101"},"value":48}
```

## Capture a CPU or allocation profile

`--profile` captures an in-process EventPipe trace next to the `.jsonl` file,
as `<basename>.nettrace`. You can open it in PerfView or the Visual Studio
profiler, or convert it for Speedscope or Perfetto.

```bash
dotnet run --project tools/EncDotNet.S100.PerfRunner -- run s101-portray-warm \
    --warmup 3 --iterations 20 --profile cpu
dotnet run --project tools/EncDotNet.S100.PerfRunner -- run s101-portray-cold \
    --warmup 0 --iterations 1 --profile alloc
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline \
    --scenarios s101-portray-warm --profile cpu
```

`cpu` samples call stacks. `alloc` records GC and allocation-tick events.

To convert a trace to a flame graph for [Speedscope](https://www.speedscope.app):

```bash
dotnet tool install -g dotnet-trace
dotnet-trace convert ./perf-runs/<basename>.nettrace --format speedscope
```

- The trace covers the measured iterations only, so warmup JIT and first-touch
  costs aren't in it. With `--warmup 0 --iterations 1`, it covers the whole
  run.
- Profiling slows the run down, typically by 2–10% for `cpu` and 20–40% for
  `alloc`. Don't use profiled runs as baselines or gate CI on them.
- For scenarios under 100 ms, raise the sampling interval to reduce the
  overhead, for example `--profile-sampling-interval-ms 5`.
- `--profile` can't be combined with `baseline --append`.

## Run a baseline

`baseline` runs every registered scenario in turn, with 3 warmup and 20
measured iterations by default, and writes the results to a folder named after
the short commit SHA:

```bash
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline --out /tmp/perf
```

The default output root is `tools/EncDotNet.S100.PerfRunner/baselines`:

```text
baselines/
  CURRENT                  the SHA of the committed baseline
  <git-sha>/
    SUMMARY.md             environment and a headline for each scenario
    s101-portray-cold.jsonl
    s101-portray-cold.md
    s101-portray-warm.jsonl
    s101-portray-warm.md
    …
```

The real-corpus scenarios report an error unless their environment variables
are set; the other scenarios still run.

| Option | Default | Description |
|---|---|---|
| `--out <dir>` | `tools/EncDotNet.S100.PerfRunner/baselines` | The output root. |
| `--corpus <path>` | `tests/datasets` | The corpus folder. |
| `--warmup <n>` | `3` | Warmup iterations for each scenario. |
| `--iterations <n>` | `20` | Measured iterations for each scenario. |
| `--scenarios <csv>` | all | Run only these scenarios. |
| `--append` | off | Append to the existing `.jsonl` files instead of overwriting them, and don't rewrite the `.md` files or `SUMMARY.md`. |
| `--round-tag <n>` | `1` | Tag every measured iteration with `perf.round=<n>`. |
| `--side <label>` | — | Tag every measured iteration with `perf.side`, such as `baseline` or `candidate`. |
| `--out-subdir <name>` | short SHA | Use this folder name instead of the commit SHA. |
| `--profile <mode>` | `none` | As for `run`. |
| `--profile-sampling-interval-ms <ms>` | `1` | As for `run`. |

`--append`, `--round-tag`, `--side`, `--scenarios` and `--out-subdir` exist for
the CI orchestration described in [CI performance gate](#ci-performance-gate).

Each measured iteration is wrapped in a `perf.iteration` activity, tagged with
`perf.scenario`, `perf.round`, `perf.iter` and, with `--side`, `perf.side`.
PerfReport's `gate` command uses these to compare medians.

### Compare your branch to the committed baseline

1. On your branch, write a fresh baseline:

   ```bash
   dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline --out /tmp/perf
   ```

2. Diff each scenario against the committed baseline:

   ```bash
   BASELINE_SHA=$(cat tools/EncDotNet.S100.PerfRunner/baselines/CURRENT)
   for s in s101-portray-cold s101-portray-warm s101-render-warm \
            s102-coverage s124-vector exchange-set-open; do
       dotnet run --project tools/EncDotNet.S100.PerfReport -- diff \
           tools/EncDotNet.S100.PerfRunner/baselines/$BASELINE_SHA/$s.jsonl \
           /tmp/perf/*/$s.jsonl
   done
   ```

A single laptop run is informational, not authoritative. Timings vary with
background load, thermal throttling and system state. The
[CI performance gate](#ci-performance-gate) checks every pull request.

### Update the committed baseline

After you merge a performance improvement:

1. Run `baseline` with the default output root.
2. Review `baselines/<new-sha>/SUMMARY.md`.
3. Commit the new folder and update `CURRENT` to the new SHA.

### Use a larger corpus

The default scenarios use the synthetic fixtures under `tests/datasets/`. To
download the larger external corpus, run:

```bash
tools/EncDotNet.S100.PerfRunner/scripts/fetch-corpus.sh
```

The script downloads each asset listed in `tests/perf/corpus/corpus.json`,
checks its SHA-256, and caches it in `~/.cache/encdotnet-perf-corpus`. Set
`ENC_DOTNET_PERF_CORPUS` to use a different cache folder. When the variable is
set, `baseline` reports the corpus mode as `full`. For the inventory, see
[`tests/perf/corpus/INDEX.md`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tests/perf/corpus/INDEX.md).

## CI performance gate

The [`perf.yml`](https://github.com/philliphoff/EncDotNet.S100/blob/main/.github/workflows/perf.yml)
workflow runs on every pull request to `main`:

1. It publishes the base branch's PerfRunner and the pull request's PerfRunner
   into separate folders, so both are available at once.
2. [`tools/perf/interleave.sh`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tools/perf/interleave.sh)
   runs 5 rounds of 4 measured iterations on each side, with 3 warmup
   iterations. It randomizes which side goes first in each round, so both sides
   see the same noise.
3. PerfReport's `gate` command compares the medians, with
   `--threshold 10 --min-abs 100 --mad-k 3.0 --retry-zone-mult 2.0`. Scenarios
   in the suspicious zone are written to `<out>.suspicious.txt` instead of
   failing.
4. If any scenario is suspicious, the workflow runs 5 more rounds for only
   those scenarios, then gates again with `--retry-zone-mult 1.0`. There's no
   second retry.
5. It posts the gate report to the pull request.

For how the gate decides, see
[`gate`](../EncDotNet.S100.PerfReport/README.md#gate) in the PerfReport README.

## Stress a running viewer

`viewer-stress` measures the live viewer, which the scenarios don't. It drives
viewport changes through the viewer's MCP endpoint and records render
statistics.

1. Build the viewer in Release, then start it with its own data folder, MCP
   and file telemetry:

   ```bash
   mkdir -p /tmp/viewer-stress
   ENC_DOTNET_OTEL_FILE=/tmp/viewer-stress/tiles.jsonl \
   src/EncDotNet.S100.Viewer/bin/Release/net10.0/<rid>/SoundCharts \
     --data-dir /tmp/viewer-stress/data \
     --mcp --mcp-port-file /tmp/viewer-stress/mcp.url \
     path/to/exchange-set
   ```

2. In another terminal, run `viewer-stress`:

   ```bash
   dotnet run --project tools/EncDotNet.S100.PerfRunner -- viewer-stress \
       --port-file /tmp/viewer-stress/mcp.url \
       --bbox 49.8,-6.5,59.0,2.0 \
       --zoom-min 6 --zoom-max 12 \
       --steps 96 --cycles 5 --step-delay-ms 0 \
       --out /tmp/viewer-stress
   ```

3. Analyze the viewer's trace with PerfReport:

   ```bash
   dotnet run --project tools/EncDotNet.S100.PerfReport -- \
     tile-report /tmp/viewer-stress/tiles.jsonl \
     --out /tmp/viewer-stress/tile-report.md
   dotnet run --project tools/EncDotNet.S100.PerfReport -- \
     chrome-trace /tmp/viewer-stress/tiles.jsonl \
     --out /tmp/viewer-stress/tile-timeline.json
   ```

   `tile-report` gives the P50, P95 and P99 tile latency and the main cause of
   each slow tile job. Open the Chrome trace in Perfetto to see the worker
   timeline.

How a run works:

- The route is a fixed path that snakes across `--bbox` in WGS-84. Without
  `--bbox`, it covers the combined bounds of every loaded dataset, from
  `list_datasets`.
- The zoom level rises from `--zoom-min` to `--zoom-max` and back.
- Within a cycle, viewport changes don't wait for rendering to finish, so
  `--step-delay-ms 0` gives the most queue pressure.
- Before each cycle, the render statistics window is reset, so start-up
  painting and earlier cycles don't affect the cycle's figures. After each
  cycle, the command waits for the map to settle and reads `get_render_stats`.
- The command writes `<timestamp>-viewer-stress.json` to `--out`: every
  requested viewport, the MCP round-trip times, the render-idle result and the
  render statistics. The viewer writes its own spans and metrics to the
  `ENC_DOTNET_OTEL_FILE` path.

To approximate normal navigation, use `--scenario navigation --step-delay-ms
100`. It pans and zooms in separate steps instead of changing both at once.

For method-level CPU detail inside a slow raster span, attach `dotnet-trace`
to the viewer process during the run and convert the `.nettrace` file to
Speedscope.

| Option | Default | Description |
|---|---|---|
| `--endpoint <url>` | — | The viewer's MCP endpoint. Use this or `--port-file`. |
| `--port-file <path>` | — | The file the viewer wrote with `--mcp-port-file`. Use this or `--endpoint`. |
| `--bbox <s,w,n,e>` | all loaded datasets | Route bounds in WGS-84, as south, west, north, east. |
| `--zoom-min <level>` | `6` | Minimum web-mercator zoom level, from 0 to 24. |
| `--zoom-max <level>` | `12` | Maximum web-mercator zoom level, from 0 to 24. |
| `--steps <n>` | `64` | Viewport changes in each cycle. |
| `--cycles <n>` | `3` | Number of times to run the route. |
| `--step-delay-ms <ms>` | `16` | Delay between viewport changes. |
| `--scenario <name>` | `burst` | `burst` snakes with continuous zoom; `navigation` pans and zooms in separate steps. |
| `--idle-timeout-ms <ms>` | `120000` | The longest wait for rendering to settle after each cycle. |
| `--out <dir>` | `./perf-runs` | The folder for the JSON manifest. |

## What the scenarios don't measure

The warm scenarios measure library work: parsing, portrayal and, for the
render scenarios, a headless Mapsui layer build. They don't run the Avalonia
window and binding loop, tile rasterization in the live viewer, or Skia GPU
drawing. Use the scenarios to catch pipeline regressions, and
[`viewer-stress`](#stress-a-running-viewer) with a viewer trace for live
rendering.

To profile the live viewer by hand, start it and attach `dotnet-trace`:

```bash
dotnet run --project src/EncDotNet.S100.Viewer
dotnet-trace collect --process-id <viewer-pid> \
    --providers Microsoft-DotNETCore-SampleProfiler
```

Pan, zoom and repaint the map while the trace runs.

## Add a scenario

1. Add a class that implements `IPerfScenario` under `Scenarios/`. Give it a
   short kebab-case `Name` and a one-line `Description`.
2. Register it in `ScenarioRegistry.cs`:

   ```csharp
   Register(() => new Scenarios.MyNewScenario());
   ```

3. Use `SharedInfrastructure.CreatePipelineFactory()` for a pipeline factory
   with the bundled catalogues, the Lua engine and the CRS transforms.

`run list`, `baseline` and the CI gate pick up every registered scenario.
