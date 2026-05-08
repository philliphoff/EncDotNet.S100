# Observability

EncDotNet.S100 is instrumented end-to-end with the standard .NET
diagnostic primitives so a performance spike — or any production
deployment — can see what the libraries and the viewer are doing
without changing code.

| Concern | API used in the libraries | Default behaviour |
|---|---|---|
| Logs    | [`Microsoft.Extensions.Logging.Abstractions`](https://learn.microsoft.com/dotnet/core/extensions/logging) `ILogger<T>` | `NullLogger<T>` when no `ILoggerFactory` is supplied. |
| Traces  | [`System.Diagnostics.ActivitySource`](https://learn.microsoft.com/dotnet/api/system.diagnostics.activitysource) | Inert (`StartActivity` returns `null`) until something subscribes. |
| Metrics | [`System.Diagnostics.Metrics.Meter`](https://learn.microsoft.com/dotnet/core/diagnostics/metrics-instrumentation) | Inert until a `MeterListener` subscribes. |

The viewer composes these into an OpenTelemetry pipeline that exports
via OTLP, so any modern collector — .NET Aspire dashboard, Jaeger,
Prometheus + Tempo + Loki, the OpenTelemetry Collector, etc. — can
ingest the data without adapter code.

## Naming conventions

- One static `Telemetry` class per library exposing
  `ActivitySource`, `Meter`, and instrument fields.
- ActivitySource / Meter names mirror the assembly name, e.g.
  `EncDotNet.S100.Datasets.S101`,
  `EncDotNet.S100.Renderers.Mapsui`, `EncDotNet.S100.Viewer`.
- Activity / metric / tag names are **lowercase dotted**, namespaced
  under `s100.` (`s100.dataset.open`, `s100.pipeline.vector.process`,
  `s100.hdf5.read.bytes`, `s100.viewport.zoom`).
- Tag-key constants live in
  `EncDotNet.S100.Diagnostics.TelemetryTags` (in the Core library).

## Span tree (typical viewer command)

```
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

The Lua per-rule activity is gated on listener subscription so a
busy ENC does not flood the trace pipeline. Rule volume is captured
instead by the `s100.lua.rule.invoke.count` counter.

GC delta tags (`gc.gen0.delta`, `gc.gen1.delta`, `gc.gen2.delta`) on
pipeline parent spans are process-wide `GC.CollectionCount` deltas —
useful for orders-of-magnitude comparisons, not exact attribution.

## Metrics catalogue

### Pipeline metrics (`EncDotNet.S100.Core`)

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
| `s100.xslt.cache.hit.count` | counter | `{hits}` | — |
| `s100.xslt.cache.miss.count` | counter | `{misses}` | — |

### Asset I/O metrics (`EncDotNet.S100.Core`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.asset.read.duration` | histogram | `ms` | `s100.asset.kind` |
| `s100.asset.bytes.read.count` | counter | `By` | `s100.asset.kind` |

### HDF5 metrics (`EncDotNet.S100.Hdf5.PureHdf`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.hdf5.read.bytes` | counter | `By` | — |
| `s100.hdf5.read.duration` | histogram | `ms` | — |

### Lua metrics (`EncDotNet.S100.Datasets.S101`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.lua.execute.duration` | histogram | `ms` | — |
| `s100.lua.features.count` | histogram | `{features}` | — |
| `s100.lua.instructions.emitted.count` | histogram | `{instructions}` | — |
| `s100.lua.rule.invoke.count` | counter | `{calls}` | `s100.lua.rule`, `s100.result` |
| `s100.lua.rule.invoke.duration` | histogram | `ms` | `s100.lua.rule` |

### Mapsui renderer metrics (`EncDotNet.S100.Renderers.Mapsui`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.render.frame.duration` | histogram | `ms` | — |
| `s100.render.instructions.processed.count` | histogram | `{instructions}` | — |
| `s100.render.styles.applied.count` | histogram | `{styles}` | — |
| `s100.symbol.resolve.duration` | histogram | `ms` | `s100.symbol.result` |
| `s100.symbol.cache.hit.count` | counter | `{hits}` | — |
| `s100.symbol.cache.miss.count` | counter | `{misses}` | — |

### Skia renderer metrics (`EncDotNet.S100.Renderers.Skia`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.render.coverage.frame.duration` | histogram | `ms` | — |
| `s100.coverage.cells.processed.count` | histogram | `{cells}` | — |

### Viewer metrics (`EncDotNet.S100.Viewer`)

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `s100.viewer.command.duration` | histogram | `ms` | `s100.viewer.command` |

## Wiring it up — the viewer

`EncDotNet.S100.Viewer` already wires OpenTelemetry into its DI
container via `ViewerObservability.AddS100Observability`. The
exporter honours the standard environment variables:

| Variable | Default |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` (gRPC) |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` |
| `OTEL_SERVICE_NAME`           | `EncDotNet.S100.Viewer` |
| `OTEL_RESOURCE_ATTRIBUTES`    | (none) |

When no collector is running the OTLP exporter retries silently —
the viewer keeps working.

### One-step: the Aspire AppHost (recommended)

`src/EncDotNet.S100.AppHost` is a [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/)
host project that orchestrates the dashboard and the viewer in a
single command. It launches the Aspire dashboard, picks free OTLP
ports, and starts the viewer with `OTEL_EXPORTER_OTLP_ENDPOINT` /
`OTEL_SERVICE_NAME` / `OTEL_RESOURCE_ATTRIBUTES` already set:

```sh
dotnet run --project src/EncDotNet.S100.AppHost
```

The console prints a login URL like
`http://localhost:15069/login?t=…` — open it to see structured logs,
traces, and metrics from the running viewer side-by-side. Closing
either the AppHost console or the viewer window shuts both down.

No Docker required. The AppHost project does not participate in
central package management — it is a self-contained orchestration
shim.

### Local Aspire dashboard (Docker, alternative)

If you don't want to run the AppHost, the dashboard can be run
standalone:

```sh
docker run --rm -it -p 18888:18888 -p 4317:4317 \
  mcr.microsoft.com/dotnet/aspire-dashboard:latest

OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
  dotnet run --project src/EncDotNet.S100.Viewer
```

Open `http://localhost:18888` to see structured logs, traces, and
metric scrapes side-by-side.

### Local Jaeger (traces only)

```sh
docker run --rm -p 16686:16686 -p 4317:4317 \
  jaegertracing/all-in-one:latest

OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 \
  dotnet run --project src/EncDotNet.S100.Viewer
```

Jaeger UI: <http://localhost:16686>.

## Wiring it up — your own host

Libraries are already instrumented; just subscribe to the right
sources in your composition root:

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

using var tracer = Sdk.CreateTracerProviderBuilder()
    .AddSource("EncDotNet.S100.*")
    .AddOtlpExporter()
    .Build();

using var meter = Sdk.CreateMeterProviderBuilder()
    .AddMeter("EncDotNet.S100.*")
    .AddOtlpExporter()
    .Build();
```

Wildcard subscription requires OpenTelemetry SDK 1.10 or newer.

## Testing

`tests/EncDotNet.S100.Pipelines.Tests/TelemetrySmokeTests.cs`
demonstrates how to assert on emitted activities using a plain
`ActivityListener` — no SDK required. Use the same pattern when
adding new instrumentation: register a listener, exercise the API,
assert on `OperationName` and tags.

## Non-goals

- **No log file sinks.** Use the OTLP log exporter or wire any
  Microsoft.Extensions.Logging-compatible sink (Serilog, NLog,
  console) into your host.
- **No allocation / GC profiling.** GC collection-count deltas are
  tagged on pipeline parent spans for rough comparison, but for
  detailed allocation profiling use `dotnet-counters`,
  `dotnet-trace`, or `EventPipe` directly.
- **No custom dashboards.** Aspire/Grafana/Tempo dashboards are
  out of scope; the metrics catalogue above is meant to be
  self-describing.
