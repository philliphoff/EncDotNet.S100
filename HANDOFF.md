# Viewer rendering performance handoff

This document is temporary working context for the draft PR. Remove it before
the PR is marked ready for review.

## Goal

Improve interactive rendering latency while panning and zooming across large
S-101 exchange sets without replacing the existing per-layer tile queues and
workers unless measurements justify that architectural change.

Tracking issue: #537.

## Implemented

### Rendering diagnostics and repeatable stress testing

- Added tile-job and stage telemetry, including visible/predicted work,
  queueing, rasterization, publication, persistence, and discard attribution.
- Added `viewer-stress` to `EncDotNet.S100.PerfRunner` for paced pan/zoom routes.
- Added tile-report analysis to `EncDotNet.S100.PerfReport`.
- Added exact slowest-frame attribution to `get_render_stats`.
- Added slow Mapsui paint and tiled-composite spans.
- Split tiled-composite timing into setup, GPU-cache management, overlap
  clipping, base-tile compositing, and live overlay stages.
- Reset each stress cycle's render-stat window so startup and previous cycles
  cannot contaminate the result.

### Asynchronous disk persistence

- Replaced synchronous PNG persistence on tile workers with a bounded,
  deduplicated, low-priority write-behind queue.
- Visible tiles are published before persistence.
- Predicted tiles are persisted only after they become visible.
- Snapshot/copy, PNG encoding, and file writes happen on the persistence worker.
- Generation guards prevent newer scene pixels from entering an older cache
  namespace.
- Normal process exit drains pending persistence work.
- Added persistence queue depth, discard, encode, and write telemetry.

Measured on the original large UK route:

| Metric | Before | After |
|---|---:|---:|
| Tile P95 | 781 ms | 48.8 ms |
| Frame P95 | 23.1 ms | 12.4 ms |
| `set_viewport` P95 | 91.7 ms | 2.3 ms |
| Route duration | 44.6 s | 37.1 s |

The async-cache route effectively matched disk-disabled performance while
retaining a warm persistent cache.

### GPU-residency outlier investigation

The remaining rare map-paint outlier was reproduced with a stable warm 13-cell
UK S-101 fixture. Stage timing ruled out:

- `TileState.Sync` contention,
- tile raster workers,
- disk persistence,
- GPU-cache reconciliation,
- overlap clipping, and
- the live overlay.

Nearly all of each multi-second paint was base-tile blitting. An A/B run using
the same cells and paced route isolated the explicit
`SKImage.ToTextureImage(GRContext)` residency path:

| Configuration | Frame max | Frame P95 | Worst base composite |
|---|---:|---:|---:|
| GPU residency enabled | 3,155 ms | 591 ms | 2,375 ms |
| GPU residency disabled, cycle 1 | 160 ms | 75 ms | 72 ms |
| GPU residency disabled, cycle 2 | 123 ms | 79 ms | 65 ms |

Both paths still call `SKCanvas.DrawImage`; disabling residency only bypasses
explicit promotion and the resident-texture cache. The evidence therefore
points to synchronous promotion and texture churn monopolizing the compositor
thread.

This branch changes GPU texture residency to **off by default**. It remains:

- available in **Settings → Map → GPU texture residency**,
- persisted when a user explicitly changes it, and
- opt-in through `S100_VECTOR_TILE_GPU=1`.

Existing profiles that already persisted `TileGpuResidencyEnabled=true` retain
that explicit preference.

## Practical benchmark fixture

The real chart data is intentionally not part of the repository. The source
machine used UK S-101 cells from the user's IC-ENC chart collection.

The stable high-load fixture contains 13 cells and excludes:

- `101GB0050242S` because pattern clipping fails with an unrelated
  NetTopologySuite mixed-dimension overlay exception,
- `101GB0050242H` because cold portrayal preprocessing can stall for minutes,
- `101GB00602793` because cold loading is exceptionally slow, and
- `101GB0062022A` to retain the stable 13-cell set.

Runtime tracing materially perturbs this workload when `dotnet-trace` performs
its rundown. Do not treat frames overlapping trace stop/rundown as renderer
behavior. Prefer the built-in telemetry first.

## Architecture decision

Keep the layer-local raster queues and workers for now. They preserve scene and
layer locality, and the completed measurements do not implicate that structure.
The global mechanisms proposed below should govern admission and staleness
without moving raster execution into a global worker pool.

## Next increments

1. Add viewport epochs and stale-work cancellation/checks.
2. Add process-wide admission control for speculative work while preserving
   per-layer queues and workers.
3. Add adaptive prediction budgets.
4. Protect visible and speculative cache segments independently.
5. Revisit GPU residency only with adaptive or strictly per-frame-budgeted
   promotion that cannot monopolize a paint.
6. Benchmark each increment with the same paced route and compare frame/tile
   distributions, queue depth, discarded work, and navigation wall time.

## Important files

- `src/EncDotNet.S100.Renderers.Mapsui/S100VectorTileRenderer.cs`
  - tiled worker/publish/composite pipeline,
  - async persistence integration,
  - slow-composite attribution,
  - GPU promotion path in `BlitTile`.
- `src/EncDotNet.S100.Renderers.Mapsui/TileDiskCache.cs`
  - bounded asynchronous write-behind persistence.
- `src/EncDotNet.S100.Renderers.Mapsui/TileCache.cs`
  - tile snapshots and cache support.
- `src/EncDotNet.S100.Renderers.Mapsui/RenderingOptimizations.cs`
  - GPU residency now defaults off.
- `src/EncDotNet.S100.Viewer/Diagnostics/RenderActivityMonitor.cs`
  - slowest-frame retention and slow-paint spans.
- `tools/EncDotNet.S100.PerfRunner/ViewerStressCommand.cs`
  - repeatable viewer stress driver.
- `tools/EncDotNet.S100.PerfReport/TileReportCommand.cs`
  - tile telemetry report.
- `docs/observability.md`
  - telemetry schema and interpretation.
- `docs/mcp-server.md`
  - `get_render_stats` result shape.

## Validation completed

- Solution formatting verification passed.
- `RenderingOptimizationsTests`: 35 passed.
- `SettingsViewModelRenderSubsystemTests`: 6 passed.
- Full Viewer tests: 1,626 passed, 3 skipped.
- Full Pipelines tests: 1,224 passed, 4 skipped.
- Release Viewer build succeeded with no warnings or errors.

Use the repository's `viewer-evaluation` skill before resuming real-world
viewer measurements.
