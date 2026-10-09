# Observability

The EncDotNet.S100 libraries and SoundCharts report logs, traces and metrics
through the standard .NET diagnostics APIs. You can see what they're doing in
any OpenTelemetry-compatible tool without changing their code.

| Signal | API the libraries use | When nothing subscribes |
|---|---|---|
| Logs | [`Microsoft.Extensions.Logging.Abstractions`](https://learn.microsoft.com/dotnet/core/extensions/logging) `ILogger<T>` | `NullLogger<T>` is used when you don't supply an `ILoggerFactory`. |
| Traces | [`System.Diagnostics.ActivitySource`](https://learn.microsoft.com/dotnet/api/system.diagnostics.activitysource) | Inert: `StartActivity` returns `null`. |
| Metrics | [`System.Diagnostics.Metrics.Meter`](https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation) | Inert until a `MeterListener` subscribes. |

SoundCharts exports all three over OTLP, so a collector such as the .NET
Aspire dashboard, Jaeger, the OpenTelemetry Collector or a
Prometheus, Tempo and Loki stack can receive them directly.

## Collect telemetry in your own app

The libraries are already instrumented. Subscribe to their sources and meters
in your app's composition root. This example uses the `OpenTelemetry` and
`OpenTelemetry.Exporter.OpenTelemetryProtocol` packages:

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("EncDotNet.S100.*")
    .AddOtlpExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter("EncDotNet.S100.*")
    .AddOtlpExporter()
    .Build();
```

Wildcard names need OpenTelemetry SDK 1.10 or later.

## Collect telemetry from SoundCharts

SoundCharts sets up OpenTelemetry in `ViewerObservability.AddS100Observability`.
The OTLP exporter reads the standard environment variables:

| Variable | Default |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` (gRPC) |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` |
| `OTEL_SERVICE_NAME` | `EncDotNet.S100.Viewer` |
| `OTEL_RESOURCE_ATTRIBUTES` | (none) |

If no collector is running, the exporter retries in the background and the
app keeps working.

### Use the Aspire AppHost

[`src/EncDotNet.S100.AppHost`](https://github.com/philliphoff/EncDotNet.S100/tree/main/src/EncDotNet.S100.AppHost)
is a [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) host project. It
starts the Aspire dashboard and then starts SoundCharts with the OTLP
endpoint, service name and resource attributes set. It doesn't need Docker.

```bash
dotnet run --project src/EncDotNet.S100.AppHost
```

The console prints a dashboard login URL, such as
`http://localhost:15069/login?t=…`. Open it to see the app's structured logs,
traces and metrics. Closing either the AppHost console or the app window shuts
down both.

The AppHost project doesn't use central package management.

### Use the Aspire dashboard in Docker

To run the dashboard without the AppHost, start it in Docker:

```bash
docker run --rm -it -p 18888:18888 -p 4317:4317 \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Then, in another terminal, start SoundCharts:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
  dotnet run --project src/EncDotNet.S100.Viewer
```

Open `http://localhost:18888` to see logs, traces and metrics.

### Use Jaeger for traces

```bash
docker run --rm -p 16686:16686 -p 4317:4317 \
  jaegertracing/all-in-one:latest
```

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
  dotnet run --project src/EncDotNet.S100.Viewer
```

Open the Jaeger UI at `http://localhost:16686`.

### Write to the console or a file

| Setting | Effect |
|---|---|
| `ENC_DOTNET_OTEL_CONSOLE=1` | Also writes logs, traces and metrics to standard output. The AppHost sets this, so they appear in the dashboard's console view. |
| `ENC_DOTNET_OTEL_FILE=<path>` | Also writes traces and metrics to `<path>` as newline-delimited JSON, the format the PerfReport tool reads. |
| `--log-file <PATH>` | Appends structured logs to a file. |
| `-v`, `--verbose` | Lowers the log level from Information to Debug. |

## Names

- Each library has one static `Telemetry` class that holds its
  `ActivitySource`, `Meter` and instruments.
- Source and meter names match the assembly name, for example
  `EncDotNet.S100.Datasets.S101` or `EncDotNet.S100.Renderers.Mapsui`.
  SoundCharts publishes as `EncDotNet.S100.Viewer`.
- Activity, metric and tag names are lowercase and dotted, under `s100.`:
  `s100.dataset.open`, `s100.pipeline.vector.process`,
  `s100.hdf5.read.bytes`, `s100.viewport.zoom`.
- Tag-key constants are in `EncDotNet.S100.Diagnostics.TelemetryTags`, in
  the `EncDotNet.S100.Core` package.

## Traces

A typical SoundCharts command produces this span tree:

```text
s100.viewer.command (kind=Internal, command="dataset.open")
  └─ s100.dataset.open
      ├─ s100.exchangeset.parse
      ├─ s100.featurecatalogue.parse
      ├─ s100.hdf5.file.open
      ├─ s100.hdf5.open{kind=group|dataset}         (× N)
      └─ s100.hdf5.dataset.read                     (× N)
  └─ s100.pipeline.vector.process                   [gc.gen0/1/2.delta tags]
      ├─ s100.pipeline.vector.stage.feature_xml
      ├─ s100.pipeline.vector.stage.rule_select
      ├─ s100.pipeline.vector.stage.xslt
      │   └─ s100.xslt.transform{rule=…}            (× N)
      ├─ s100.pipeline.vector.stage.lua
      │   └─ s100.lua.execute
      ├─ s100.pipeline.vector.stage.assemble
      ├─ s100.pipeline.vector.stage.viewing_groups
      └─ s100.pipeline.vector.stage.sort
  └─ s100.pipeline.coverage.process                 [gc.gen0/1/2.delta tags]
      ├─ s100.pipeline.coverage.stage.resolve
      └─ s100.pipeline.coverage.stage.read
  └─ s100.render.frame
  └─ s100.render.coverage.frame
  └─ s100.asset.read{kind=file|zip}                 (× N)
  └─ s100.xslt.compile{rule=…}                      (× N, per catalogue)
```

- The Lua engine creates a per-rule activity only when a listener subscribes
  to it, so a large ENC doesn't flood the trace pipeline. The
  `s100.lua.rule.invoke.count` counter records rule volume instead.
- The `gc.gen0.delta`, `gc.gen1.delta` and `gc.gen2.delta` tags on pipeline
  parent spans are process-wide `GC.CollectionCount` differences. Use them for
  rough comparisons, not exact attribution.

### Tiled renderer spans

When tracing is on, the tiled renderer in `EncDotNet.S100.Renderers.Mapsui`
adds these spans:

- **`s100.render.tile.job`**, one per worker job, with child spans
  `disk_read`, `rasterize` and `publish`. Job tags include the tile keys,
  priority (visible, predicted or cross-band), queue wait, active worker
  counts, cache outcome, viewport epoch, stale and published counts,
  stale-before-raster counts and persistence enqueue results. Raster spans
  include the candidate paint operation count and output size.
- **`s100.render.tile.cache.persist`**, a root span for each background
  cache write, with child spans `cache.encode` and `cache.file_write`. Cache
  writes run on a bounded, low-priority writer after the tile is published.
  The byte budget is enforced from an in-memory LRU index inside
  `cache.file_write`, so no span enumerates the cache directory. The index
  rescans the directory only on the first write, every ten minutes, or when it
  notices an external deletion, such as **Clear caches** in settings.
- **`s100.render.tile.composite.slow`**, for tiled-layer composites that take
  50 ms or more. Tags give the layer name, time waiting for the layer-state
  lock, time spent holding it, cold exposure and visible queue depth. Use it to
  tell lock contention apart from slow cache or GPU work inside the lock.
- **`s100.map.paint.slow`**, from SoundCharts, for paints that take 50 ms or
  more. Tags split the paint into summed instrumented style-renderer time and
  an uninstrumented remainder, and include the paint sequence and draw-call
  count. The sequence and completion time match
  `get_render_stats.window.slowestFrame` from the MCP server, so you can match
  a slow frame to tile jobs, cache writes and runtime traces.

These spans are inert unless an `ActivityListener` subscribes, so ordinary
runs don't allocate trace records.

To rank slow tile jobs and break down their latency from first visible enqueue
to publish, run PerfReport's `tile-report` command on a trace file:

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- \
  tile-report path/to/tiles.jsonl --out path/to/tiles.md
```

## Metrics

Each table lists the instruments one meter publishes. The meter name is the
package name in the heading.

### Pipeline (`EncDotNet.S100.Core`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.pipeline.duration` | histogram | `ms` | `s100.pipeline.stage`, `s100.product` |
| `s100.pipeline.stage.duration` | histogram | `ms` | `s100.pipeline.stage` |
| `s100.pipeline.stage.instructions.count` | histogram | `{instructions}` | `s100.pipeline.stage` |
| `s100.pipeline.features.in` | histogram | `{features}` | `s100.product` |
| `s100.pipeline.drawinginstructions.out` | histogram | `{instructions}` | `s100.product` |
| `s100.coverage.cells` | histogram | `{cells}` | `s100.product` |
| `s100.xslt.transform.duration` | histogram | `ms` | `s100.xslt.rule` |
| `s100.xslt.compile.duration` | histogram | `ms` | `s100.xslt.rule` |
| `s100.lua.execute.duration` | histogram | `ms` | — |
| `s100.lua.features.count` | counter | `{features}` | — |
| `s100.lua.instructions.emitted.count` | histogram | `{instructions}` | — |

The same meter also publishes:

- `s100.lua.feature.instructions.count`: drawing instructions the Lua
  executor emits for one feature type in one pass, tagged with
  `s100.feature.type` and `s100.product`.
- `s100.vector.index.build.duration`, `s100.vector.index.features.count`,
  `s100.vector.index.query.duration` and `s100.vector.index.returned.count`:
  the cost and selectivity of the spatial index over a vector source's
  features.
- `s100.coverage.pyramid.build.duration` and
  `s100.coverage.overview.level_selected`: the cost of building a coverage
  overview pyramid, and the pyramid level each coverage read uses (0 is the
  base grid).

### Asset I/O (`EncDotNet.S100.Core`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.asset.read.duration` | histogram | `ms` | `s100.asset.kind` |
| `s100.asset.bytes.read.count` | counter | `By` | `s100.asset.kind` |

### Catalogue resolution (`EncDotNet.S100.Datasets.Pipelines`)

`s100.catalogue.match.count` counts catalogue resolutions, tagged with the
product and catalogue versions and the kind of match.

### HDF5 (`EncDotNet.S100.Hdf5.PureHdf`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.hdf5.read.bytes` | counter | `By` | — |
| `s100.hdf5.read.duration` | histogram | `ms` | — |

### Lua rules (`EncDotNet.S100.Scripting.MoonSharp`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.lua.rule.invoke.count` | counter | `{calls}` | `s100.lua.rule`, `s100.result` |
| `s100.lua.rule.invoke.duration` | histogram | `ms` | `s100.lua.rule` |

### Mapsui renderer (`EncDotNet.S100.Renderers.Mapsui`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.render.frame.duration` | histogram | `ms` | — |
| `s100.render.instructions.processed.count` | counter | `{instructions}` | — |
| `s100.render.styles.applied.count` | counter | `{styles}` | — |
| `s100.symbol.resolve.duration` | histogram | `ms` | `s100.symbol.result`, `s100.product` |
| `s100.render.metatile.rasterize.duration` | histogram | `ms` | — |
| `s100.render.metatile.slice.duration` | histogram | `ms` | — |
| `s100.render.metatile.tiles` | histogram | `{tile}` | — |
| `s100.render.metatile.jobs` | counter | `{job}` | — |
| `s100.render.metatile.fallbacks` | counter | `{fallback}` | `reason` |
| `s100.render.tile.rasterize.duration` | histogram | `ms` | — |
| `s100.render.tile.cold.latency` | histogram | `ms` | — |
| `s100.render.tile.visible.queue.depth` | histogram | `{tile}` | — |
| `s100.render.tile.speculation.deferred` | counter | `{worker}` | `priority` |
| `s100.render.tile.disk.write_queue.depth` | histogram | `{tile}` | — |
| `s100.render.tile.disk.write_queue.discarded` | counter | `{tile}` | `reason` |

The symbol and pattern cache counters are listed under
[Cache counters](#cache-counters).

- **Metatiles.** The metatile metrics appear only when
  `S100_VECTOR_TILE_METATILE=1` is set or the **Batch adjacent tiles** setting
  is on. `rasterize.duration` is the time for the whole batched job,
  `slice.duration` is the cost of copying it into tiles, and `tiles` is the
  number of tiles per batch. The fallback `reason` is `sparse`, `disk`,
  `scamin`, `dimension` or `scale`. `scale` means integer pixel geometry
  can't preserve the single-tile projection at a fractional device scale.
  `s100.render.tile.rasterize.duration` stays comparable with batching on or
  off: a batched job divides its total time across the tiles it produced.
- **Speculation.** `s100.render.tile.speculation.deferred` counts predicted
  or cross-band worker admissions rejected while any layer still has visible
  cold work. A non-zero count means visible-first scheduling is protecting the
  current viewport. If it keeps growing after the visible queue drains, a
  layer's active registration is stale.
- **Disk write queue.** Queue overflow and duplicate requests are discarded,
  not pushed back onto render workers. The discard `reason` can also be
  `stale`, when a queued tile leaves the viewport before its snapshot,
  encoding or file commit.

The same meter also publishes:

- `s100.render.scene.rasterize.duration`, `s100.render.scene.composite.duration`
  and `s100.render.tile.composite.duration`: off-thread rasterization and
  UI-thread composite times.
- `s100.render.tile.cold.exposure`: visible tiles missing from the cache at
  each composite.
- `s100.render.tile.prediction.rasterized` and
  `s100.render.tile.prediction.hits`: speculative tiles, and the ones that
  later became visible while cached. Their ratio is the prediction hit rate.
- `s100.render.tile.hidden.skipped` and
  `s100.render.tile.layer.hidden.skipped`: tiles and whole layer frames
  skipped because finer coverage hides them.
- `s100.render.tile.disk.hits`, `s100.render.tile.disk.writes`,
  `s100.render.tile.gpu.uploads` and `s100.render.tile.gpu.hits`: disk cache
  and GPU texture use.
- `s100.render.tile.faults`: render-thread paint faults caught and turned
  into a dropped frame. It stays at zero in normal operation.
- `s100.layer.getfeatures.duration`, `s100.layer.getfeatures.visible.count`,
  `s100.layer.getfeatures.total.count`, `s100.layer.getfeatures.calls.count`,
  `s100.layer.getfeatures.fps` and `s100.layer.frame.interval`: per-frame
  feature filtering cost and frame rate for Mapsui memory layers.

### Skia renderer (`EncDotNet.S100.Renderers.Skia`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.render.coverage.duration` | histogram | `ms` | — |
| `s100.coverage.cells.processed.count` | counter | `{cells}` | — |

### SoundCharts (`EncDotNet.S100.Viewer`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.viewer.command.duration` | histogram | `ms` | `s100.viewer.command` |
| `s100.map.paint.duration` | histogram | `ms` | — |
| `s100.map.paint.interval` | histogram | `ms` | — |

The meter also publishes `s100.map.paint.style.calls` and
`s100.map.paint.style.duration` (style-renderer draw calls and time per paint,
tagged by style, layer, point bucket and feature class), and
`s100.viewer.viewinggroup.toggled` and `s100.viewer.displayplane.toggled`
(how often the user changes a viewing-group or display-plane override).

### Cache counters

Each cache between the catalogues, dataset processors and renderer has a hit
counter and a miss counter. Each records `1` per event, so the sums give the
hit rate.

| Instrument | Meter | Unit | Tags |
|---|---|---|---|
| `s100.symbol.cache.hit.count` | `EncDotNet.S100.Renderers.Mapsui` | `{hits}` | `s100.product` |
| `s100.symbol.cache.miss.count` | `EncDotNet.S100.Renderers.Mapsui` | `{misses}` | `s100.product` |
| `s100.pattern.cache.hit.count` | `EncDotNet.S100.Renderers.Mapsui` | `{hits}` | `s100.product` |
| `s100.pattern.cache.miss.count` | `EncDotNet.S100.Renderers.Mapsui` | `{misses}` | `s100.product` |
| `s100.portrayal.cache.hit.count` | `EncDotNet.S100.Portrayals` | `{hits}` | `s100.product`, `s100.asset.kind` |
| `s100.portrayal.cache.miss.count` | `EncDotNet.S100.Portrayals` | `{misses}` | `s100.product`, `s100.asset.kind` |
| `s100.lua.source.cache.hit.count` | `EncDotNet.S100.Portrayals` | `{hits}` | `s100.product` |
| `s100.lua.source.cache.miss.count` | `EncDotNet.S100.Portrayals` | `{misses}` | `s100.product` |
| `s100.featurecatalogue.cache.hit.count` | `EncDotNet.S100.Features` | `{events}` | `s100.product` |
| `s100.featurecatalogue.cache.miss.count` | `EncDotNet.S100.Features` | `{events}` | `s100.product` |

- `s100.product` is the product specification name: `S-101`, `S-124`,
  `S-131` and so on.
- On the portrayal counters, `s100.asset.kind` is one of `xslt`, `svg`,
  `line_style`, `area_fill`, `palette`, `lua_script` or `lua_source`. The
  asset I/O metrics use the same tag name with different values, so tell them
  apart by instrument name.
- The Lua source cache records both its own `s100.lua.source.cache.*`
  counter and an `s100.portrayal.cache.*` counter with
  `s100.asset.kind=lua_source`. Dashboards that group on the portrayal counter
  still see Lua sources.
- Each dataset processor sets `s100.product` on the symbol and pattern
  counters when it creates its `MapsuiDisplayListRenderer`.

A PerfReport `summarise` report shows these counters as rows like:

```text
| Metric | Sum |
| s100.symbol.cache.hit.count[s100.product=S-101] | 4123 |
| s100.symbol.cache.miss.count[s100.product=S-101] | 38 |
| s100.portrayal.cache.hit.count[s100.product=S-101][s100.asset.kind=svg] | 412 |
| s100.lua.source.cache.hit.count[s100.product=S-101] | 24 |
| s100.featurecatalogue.cache.hit.count[s100.product=S-101] | 19 |
```

In a warm run, the symbol and portrayal hit counts are much larger than the
miss counts, and the feature catalogue hit count is above zero, because every
dataset opened after the first reuses the cached catalogue. Use
`PerfReport diff <baseline.jsonl> <candidate.jsonl>` to compare two runs.

## Test instrumentation

[`TelemetrySmokeTests.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tests/EncDotNet.S100.Pipelines.Tests/TelemetrySmokeTests.cs)
asserts on emitted activities with a plain `ActivityListener`; it doesn't
need the OpenTelemetry SDK. Use the same pattern when you add instrumentation:
register a listener, call the API, and assert on `OperationName` and tags.

## What's not included

- **Log sinks in the libraries.** Use the OTLP log exporter, or add any
  `Microsoft.Extensions.Logging` provider, such as Serilog, NLog or the
  console, in your app.
- **Allocation and GC profiling.** The GC count tags give rough comparisons
  only. For allocation profiling, use `dotnet-counters`, `dotnet-trace` or
  EventPipe.
- **Dashboards.** The project doesn't ship Aspire, Grafana or Tempo
  dashboards. The instrument names and tags above are meant to be read
  directly.

## Performance baselines

The [PerfRunner](../tools/EncDotNet.S100.PerfRunner/README.md) `baseline`
command runs every scenario and records its telemetry. Committed baselines are
in `tools/EncDotNet.S100.PerfRunner/baselines/<git-sha>/`, and
`baselines/CURRENT` holds the SHA of the latest one.

| File | Contents |
|---|---|
| `SUMMARY.md` | Git SHA, branch, commit subject, UTC time, runtime details (OS, architecture, CPU count, .NET version), and the mean and P95 of each scenario's primary span. It also says whether the run used only synthetic data or the full corpus. |
| `<scenario>.jsonl` | Raw telemetry in the [JSONL schema version 1](../tools/EncDotNet.S100.PerfRunner/README.md#jsonl-schema-version-1): span, histogram and counter records from the measured iterations. |
| `<scenario>.md` | The scenario's minimum, P50, P90, P95, P99, maximum and mean iteration durations. |

### Compare a run with a baseline

Use the [PerfReport](../tools/EncDotNet.S100.PerfReport/README.md) tool.
`summarise` lists the top spans by total duration and every histogram and
counter. `diff` compares a baseline and a candidate side by side and marks
regressions and improvements.

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- summarise \
    tools/EncDotNet.S100.PerfRunner/baselines/<sha>/s101-portray-warm.jsonl
```

```bash
dotnet run --project tools/EncDotNet.S100.PerfReport -- diff \
    tools/EncDotNet.S100.PerfRunner/baselines/<sha>/s101-portray-warm.jsonl \
    path/to/candidate/s101-portray-warm.jsonl
```

The default scenarios use the small synthetic datasets in `tests/datasets/`.
They exercise the same code paths as real data, so they show trends between
commits, but their absolute times are much lower than for a real ENC or
bathymetry grid. A single laptop run is noisy; the CI gate below gives more
reliable comparisons.

### CI performance gate

The [`perf.yml`](https://github.com/philliphoff/EncDotNet.S100/blob/main/.github/workflows/perf.yml)
workflow runs on every pull request to `main`. It doesn't use the committed
baselines:

1. It builds PerfRunner from the base branch and from the pull request.
2. It runs both in alternating rounds (5 rounds of 4 iterations per side,
   after 3 warm-up iterations) on the same runner, so both see the same noise.
3. It runs PerfReport `gate`, which compares medians and fails the check when
   a scenario regresses by 10% or more and the change is large relative to the
   run-to-run spread. Spans and metrics with a baseline median under 100 ms
   aren't gated.
4. It re-runs any scenarios close to the threshold for 5 more rounds, then
   gates them again.
5. It posts the report as a comment on the pull request.

For the gate's options and evaluation model, see the
[PerfRunner](../tools/EncDotNet.S100.PerfRunner/README.md) and
[PerfReport](../tools/EncDotNet.S100.PerfReport/README.md) READMEs.

### Update the committed baseline

After you merge a performance improvement, record a new baseline:

```bash
dotnet run --project tools/EncDotNet.S100.PerfRunner -- baseline
```

Review `baselines/<new-sha>/SUMMARY.md`, write the new SHA to
`tools/EncDotNet.S100.PerfRunner/baselines/CURRENT`, and commit the new
directory and `CURRENT`.
