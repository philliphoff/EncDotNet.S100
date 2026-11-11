# EncDotNet.S100.Rendering.Engine

`EncDotNet.S100.Rendering.Engine` composites S-100 vector scenes into a map
view. It holds the tile compositor: the EPSG:3857 tile grid, raster tile jobs on
the work scheduler, the process-wide tile memory pool, the warm disk tile cache,
GPU texture residency, prediction and pre-warm, the overscale curtain, and the
coverage clipping and overlap suppression between chart cells. It also holds the
single-surface compositor and the rendering options, `ChartSessionOptions`.

The engine doesn't depend on Mapsui or a UI framework. It draws with SkiaSharp
through [`EncDotNet.S100.Renderers.Skia`](../EncDotNet.S100.Renderers.Skia/README.md)
and consumes scenes from
[`EncDotNet.S100.Rendering.Scene`](../EncDotNet.S100.Rendering.Scene/README.md).
To show charts on a Mapsui map, use
[`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/README.md),
which adapts the engine to Mapsui; you don't need to reference this package
yourself. Reference it directly when you write an adapter for another host.

The engine is the first step of the staged migration in the
[map rendering architecture](../../docs/design/map-render-architecture.md)
design note (slice&#160;7). A host creates a `ChartSession` per map (slice&#160;8);
the tile compositor's entry points are still process-wide static classes.

## Install

```bash
dotnet add package EncDotNet.S100.Rendering.Engine
```

## Drive the engine from a host

A host identifies each layer to the engine by an owner token: any object, held
weakly, that the engine never calls into. The Mapsui adapter uses the Mapsui
layer itself. A host:

1. Installs a repaint callback once at startup with
   `LayerRepaint.SetHandler`. The engine calls it, from any thread, with the
   owner token of a layer whose tiles finished rasterising or whose view is out
   of date.
2. Binds each layer's scene with `S100VectorTileRenderer.BindScene(owner, scene)`
   (or `S100VectorSceneRenderer.BindScene` for the single-surface mode). Pass a
   product layer set and style hash to share tiles through the warm disk cache.
3. On every frame, calls `S100VectorTileRenderer.Render(canvas, view, frame)`
   with the frame's `ViewState` (centre, resolution and rotation in EPSG:3857,
   size in device-independent pixels) and a `LayerFrame` (owner, name, opacity
   and data extent). The canvas's scale is the device scale.

```csharp
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

LayerRepaint.SetHandler(owner => ((MyLayer)owner).Invalidate());

S100VectorTileRenderer.BindScene(layer, scene);

void Paint(SKCanvas canvas, MyLayer layer, MyView v) =>
    S100VectorTileRenderer.Render(
        canvas,
        new ViewState(v.CenterX, v.CenterY, v.Resolution, v.Rotation, v.Width, v.Height),
        new LayerFrame(layer, layer.Name, layer.Opacity, layer.Extent));
```

Call `S100VectorTileRenderer.ShutdownAndDrain` before the process exits, so no
tile job is still in native Skia code when `libSkiaSharp` unloads. To measure how
long a view takes to settle, turn on `S100VectorTileRenderer.SettleTrackingEnabled`
and call `EvaluateSettle` with a `SettleLayer` per host layer.

## Predict what the view needs next

Each `ChartSession` has a `DemandPlanner` (map rendering slice&#160;9). A host
reports every view with `Observe`, and, when it starts one, a fling or an
animation with `BeginFling` or `BeginAnimation`. The planner's
`PredictionModel` weighs a known destination first, then the pan velocity and
zoom rate, the route ahead of own ship (`SetInputs`), and the places the view
rested at. Chart layers add the views a second ahead to their predicted tiles.
A host's cell loader reads `Plan()`: its `CellViews` reach a few seconds ahead.
It reports the cells the view waits for to `Arrivals`, so the chart stack can
hold a compose until they land (`ChartSessionOptions.StackHoldMs`). `Stats`
counts slot and cell hits, and predicted tiles and cells used or wasted.

```csharp
using EncDotNet.S100.Rendering.Engine.Prediction;

var planner = session.Planner;
planner.Observe(view, Stopwatch.GetTimestamp());
planner.BeginAnimation(target, TimeSpan.FromMilliseconds(600), Stopwatch.GetTimestamp());

foreach (var predicted in planner.Plan().CellViews)
{
    // Load the cells under predicted.View, likeliest and soonest first.
}
```

The Mapsui adapter does all of this for a map: `MapsuiMapNavigator` reports
views and the animations it starts, and the Avalonia adapter reports flings.

## Render images and tile sets

Hosts that draw a whole image or a tile set, rather than a live map, use
`ChartRenderer`. The `s100 render`, `s100 tiles` and `s100 mcp serve` commands
and the library's `PngS100DatasetRenderer` all go through it.

- `ChartRenderer.PortrayAsync` portrays each dataset as its own `Portray` job
  on the work scheduler, in the `Visible` class, so several cells portray in
  parallel.
- `ChartCompositor.Prepare` puts the portrayed datasets in S-98 order, applies
  the inter-product rules, and lowers them to a `ChartComposition`. A finer
  chart cell hides an overlapping coarser one with the viewer's NTS coverage
  model: each cell's footprint is its coverage areas unioned and normalised,
  and a finer cell takes part only where its footprint meets the coarser one.
- `ChartComposition.Render(viewport, deviceScale)` draws one view.
  `ChartCompositeOptions.DeviceScale` (or the `deviceScale` argument) makes a
  HiDPI image of the same device-independent view.
- `ChartRenderer.RenderTilesAsync` renders XYZ tile blocks with a
  `ChartTileRenderer`, as `Raster` jobs in the `Visible` class, queued in the
  order they're written. Per-tile work such as encoding runs on the job, and the
  results reach the writer in order. A layer that one finer cell's coverage
  hides across a whole tile isn't drawn for that tile.

```csharp
var compositor = new ChartCompositor(
    new ProjNetCrsTransformFactory(),
    new InteroperabilityAuthority(ProductRegistry.Default.DisplayPlanes));

using SKBitmap image = await ChartRenderer.RenderAsync(
    [new ChartPortrayalRequest(processorA, contextA), new ChartPortrayalRequest(processorB, contextB)],
    compositor,
    new ChartCompositeOptions { Width = 1024, Height = 768, DeviceScale = 2f });
```

## Options

`ChartSessionOptions` holds the rendering options. The engine reads the
process-wide instance, `ChartEngine.Options`, which
`ChartSessionOptions.FromEnvironment()` seeds from the environment variables
below on first use. A variable that is set pins its option: later writes are
ignored and the matching `…EnvExplicit` property is `true`, so a perf run keeps
the value it started with. SoundCharts shows most options under **Settings** >
**Advanced** > **Base-plane rendering**.

| Option | Environment variable | Default | Takes effect |
|---|---|---|---|
| `Profile` | `S100_PERF_PROFILE` | `Auto` | Re-derives the budgets that aren't pinned |
| `SceneMode` | `S100_VECTOR_SCENE_MODE` (`single`) | `Tiled` | On the next layer build |
| `TileGutterDip` | `S100_VECTOR_TILE_GUTTER` | 64 DIP | On newly rasterised tiles |
| `TileBudgetMb` | `S100_VECTOR_TILE_BUDGET_MB` | By tier | On the next tile insert |
| `TilePredictionEnabled` | `S100_VECTOR_TILE_PREDICT` | On | Next frame |
| `TileCrossBandPrewarmEnabled` | `S100_VECTOR_TILE_XBAND` | On, off on `LowEnd` | Next frame |
| `TileMetatileEnabled` | `S100_VECTOR_TILE_METATILE` | Off | On the next tile job |
| `TileDiskCacheEnabled` | `S100_VECTOR_TILE_DISK` | On | Restart |
| `TileDiskMb` | `S100_VECTOR_TILE_DISK_MB` | By tier | Restart |
| `TileDiskDirectory` | `S100_VECTOR_TILE_DISK_DIR` | OS temp folder | Restart |
| `TileGpuResidencyEnabled` | `S100_VECTOR_TILE_GPU` | Off | Next frame |
| `TileGpuBudgetMb` | `S100_VECTOR_TILE_GPU_MB` | By tier | On the next upload |
| `SkiaGpuResourceMb` | `S100_SKIA_GPU_RESOURCE_MB` | By tier | When the host creates its GPU context (read-only) |
| `LiveBaseEnabled` | `S100_VECTOR_TILE_LIVE_BASE` | On | On the next scene bind |
| `SceneMarginPx` | `S100_VECTOR_SCENE_MARGIN` | 256 px | Next frame |

Two diagnostic switches stay environment-only: `S100_VECTOR_TILE_DIAG=1` and
`S100_VECTOR_SCENE_DIAG=1` write compositor summaries to stderr. The tile job
threads come from the work scheduler in `EncDotNet.S100.Core`
(`S100_WORK_THREADS`).

## Internals

The base plane is the chart's area fills, contours and lines. The scene path is
the only base-plane path: the `VectorScene` is drawn by
`S100VectorTileRenderer` (default) or `S100VectorSceneRenderer`. The earlier
Mapsui feature and style path, its caches, line simplification and line
level-of-detail pyramid have been removed; see the repository history for them.

### Single-surface renderer

`S100VectorSceneRenderer` is the single-surface compositor. It rasterises the
scene with `SkiaDisplayListRenderer` on a worker thread, then swaps in and blits
the finished `SKImage` on the UI thread (Appendix B). It renders the viewport
plus a margin (`S100_VECTOR_SCENE_MARGIN`, default 256 DIP) at device scale, so
a pan within that margin is only a translated blit.

Select it with `S100_VECTOR_SCENE_MODE=single`,
`ChartEngine.Options.SceneMode = VectorSceneMode.Single`, or in SoundCharts
**Settings** > **Advanced** > **Base-plane rendering** > **Scene mode** >
**Single surface**. `MapsuiDisplayListRenderer` then tags the layer with
`S100VectorSceneRenderer.RendererName` and binds the scene with `BindScene`.

The worker is latest-wins: a superseded request is dropped, never published. It
applies scale visibility, converting the EPSG:3857 resolution back to an S-100
denominator (`ScaleDenominatorFor`), so the same SCAMIN detail shows and hides
as on the live frame. Rotated viewports draw nothing; this renderer is north-up
only. On publish it asks the host for a repaint through `LayerRepaint`.
`SceneRasterizeDuration` (worker) and `SceneCompositeDuration` (UI blit)
measure the two halves. In an 18-step gesture script on the PDB01 cell, the
worst on-screen frame went from about 409 ms on the retired Mapsui path to about
5 ms (Appendix B).

### Tiled base plane

`S100VectorTileRenderer`, the default, renders the scene into a pyramid of
cached tiles (Appendix C). It divides the world into a power-of-two EPSG:3857
grid anchored at the world origin (`TileGrid`, 256-DIP tiles, XYZ convention)
and rasterises each visible tile on a worker. Because the grid doesn't move with
the viewport, a pan at constant zoom reuses every interior tile and rasterises
only the newly exposed edge.

**Antimeridian.** Tile enumeration (`TileGrid.VisibleTileRange` and
`PredictedTiles`) clamps only the Y (latitude) index at the poles and leaves the
X (longitude) index unclamped, apart from a 4096-column guard. A dataset kept in
continuous longitudes across the antimeridian, such as the US NWS S-411 sea-ice
product (about 175°E to 225°E), tiles into columns past the last world column,
whose `TileWorldBounds` map east of +180°. `RasterizeTile` sets `EnableSeamWrap`
to `false` on its `SkiaDisplayListRenderer`, so large continuous polygons aren't
wrapped back across the world.

**Compositing.** Each frame, the UI thread snaps the resolution to the nearest
band and blits the best available tile for every visible slot, each clipped to
its core. Tiles are rasterised with a gutter (`S100_VECTOR_TILE_GUTTER`, default
64 DIP), so strokes stay continuous across tile edges and no hole shows, but a
cached tile keeps only its core plus a 2-pixel apron for bilinear sampling
(`ApronPx`). Storing the whole gutter took 2.25 times the core's pixels; the
apron takes about 1.03 times. The target band is
drawn on top. While it's incomplete, cached tiles from the single nearest other
band are drawn underneath, within `MaxFallbackBandDistance` (2) bands, so zoom
transitions don't show symbols at several sizes.

**Cache and workers.** Finished tiles go into the layer's `TileCache`. Every
layer's cache charges one process-wide `TileMemoryPool` with one LRU order and
one native-byte budget (`S100_VECTOR_TILE_BUDGET_MB`, default set by the
[performance profile](#performance-profile)). When the pool is full, the tiles
drawn least recently go first, whichever layer holds them, so a culled, hidden
or off-screen layer's tiles go before any visible layer's. Each layer pins its
visible tiles every frame, so they're never evicted mid-frame. A pin lapses
once other layers have drawn for 2 seconds without this one, so a layer that
stops drawing can't hold its tiles. GPU textures have a second pool
(`S100_VECTOR_TILE_GPU_MB`). All cache access goes through
the layer lock, so a worker can't dispose an image the compositor is drawing.
Each pending tile is a job on the process-wide work scheduler
(`EncDotNet.S100.Core.Scheduling.WorkScheduler.Shared`): visible tiles run
before predicted ones, and those before cross-band pre-warm, and within a class
the tiles nearest the view centre run first, whichever layer they belong to.
The scheduler owns one worker thread per logical core but one
(`S100_WORK_THREADS` pins the count), so the UI thread keeps a core, and no
layer has workers of its own. The pending sets are rebuilt every frame, and a
tile that leaves them drops its job before it starts.
`TileRasterizeDuration` (worker) and `TileCompositeDuration` (UI) measure the
two halves; `TileColdLatency` measures how long a cold tile takes to appear,
queue wait included.

**Rotation.** A rotated viewport, such as one left by a trackpad pinch, is
composited north-up to an off-screen surface, and that image is rotated about
the screen centre by an angle taken from `ViewState.WorldToScreenXY`, which follows Mapsui's rotation convention. Tile
selection grows to the rotated viewport's bounding box
(`TileGrid.RotatedCoverSize`) so the corners are covered. Compositing north-up
first keeps tile joins and the edge between fallback and target bands
axis-aligned, so zooming while rotated doesn't show seams (Appendix F.8).

**Device scale.** Tiles are rasterised at the frame's device scale
(`canvas.TotalMatrix.ScaleX`): a 256-DIP tile plus its two gutters is
rasterised at 384 px at 1x and 768 px at 2x, and stored at 260 px and 516 px. A cached tile's pixel size records its scale. A tile of
another size is drawn only as a placeholder while a replacement rasterises, and
never counts as a hit. Moving a window between a Retina and a standard display
re-rasterises once. The disk cache is split by pixel size too.

**Off-screen renders.** A host that renders the live layers to an image at a
different scale, such as an Avalonia `RenderTargetBitmap` capture at 96 dpi or a
print preview, wraps the call in `S100VectorTileRenderer.BeginOffscreenRender()`.
Inside that per-thread scope, `Render` composites only cached tiles, scaled to
the target, and the live overlay. It doesn't schedule tiles, change the device
scale, viewport or velocity the live workers use, change which tiles are
protected from eviction, or touch GPU textures. `AvaloniaControlCapture` does
this, so captures don't leave 1x tiles behind for a 2x window to keep drawing.

**Empty tiles.** A tile whose bounds and gutter touch no base op of the cell is
never scheduled, cached, stored or drawn (`BaseSpatialIndex.Intersects`). It
would be fully transparent, so the output is the same. A cell's tile work is
proportional to the part of the viewport it covers.

**Tiles hidden by a finer cell.** A coarser cell's tile that one finer, drawn
cell covers completely is never scheduled, cached, stored or drawn; the coverage
clip would erase all of it. `CoverageClip.GetHiddenCoverage` returns the finer
coverages active at the current resolution. A tile is skipped when its core,
padded by 2 DIP at the coarsest resolution its band is shown at, lies inside one
of them; the padding keeps the clip's anti-aliased edge. The test uses one
coverage at a time, not their union: each finer cell is clipped separately, so
a faint line of the coarser cell remains where two finer cells meet, and
skipping across that join would change the picture. When one finer coverage
hides the whole viewport, the layer's live overlay is skipped too. Invalid
coverages never hide anything. Counters: `s100.render.tile.hidden.skipped` and
`s100.render.tile.layer.hidden.skipped`.

In the 18-step gesture script on PDB01, on-screen `frameDurationMs` stayed
bounded: p50 about 7.7 ms, p90 about 34 ms, maximum about 37 ms (the worst
frames are zoom-out fallback blits), against about 409 ms on the retired Mapsui
path. Pans held at about 3–8 ms with no visible seams (Appendix C).

### Performance profile

`MachineProfile` sizes the tile caches and worker pools for the host. A
`PerformanceProfile` tier seeds the hot, GPU and disk budgets. `Auto`, the
default, picks the tier from logical cores and available memory: `LowEnd` for
4 or fewer cores or 8 GB or less, `Balanced` for 8 or fewer cores or 16 GB or
less, otherwise `HighEnd`. Set `S100_PERF_PROFILE` (`Auto`, `LowEnd`,
`Balanced` or `HighEnd`) to choose a tier. The individual budget variables
still override each budget.

| Setting | `LowEnd` | `Balanced` | `HighEnd` |
|---|---|---|---|
| Hot tile cache, all layers (`S100_VECTOR_TILE_BUDGET_MB`) | 512 MB | 1 GB | 1.5 GB |
| GPU tile cache, all layers (`S100_VECTOR_TILE_GPU_MB`) | 256 MB | 512 MB | 768 MB |
| Disk tile cache (`S100_VECTOR_TILE_DISK_MB`) | 256 MB | 384 MB | 512 MB |
| Skia GPU resource cache (`S100_SKIA_GPU_RESOURCE_MB`) | 128 MB | 256 MB | 512 MB |
| Cross-band pre-warm (`S100_VECTOR_TILE_XBAND`) | Off | On | On |

SoundCharts shows the profile and the detected tier under **Settings** >
**Advanced**.

Skia keeps an uploaded texture for every raster tile it blits in the host GPU
context's resource cache (`ChartEngine.Options.SkiaGpuResourceMb`). That
cache must hold the visible tiles of every layer, or every tile is uploaded
again every frame. Avalonia's default, about 28 MB, holds only a couple of dozen
Retina tiles, so pass this budget to `SkiaOptions.MaxGpuResourceSizeBytes`.
SoundCharts does this at startup.

### Symbol and sounding overlay

Base tiles contain only area fills, contours and lines.
`S100VectorTileRenderer.PartitionScene` sends `PointPaintOp` and `TextPaintOp`
to an overlay scene, which is drawn live every frame over the composited tiles
with `SkiaDisplayListRenderer.RenderOnto`. This is required for correctness. A
tile is rasterised once per band and drawn scaled to the current resolution, so
symbols baked into tiles would grow and shrink during a zoom instead of keeping
the constant screen size S-100 requires. Drawn against the live viewport, their
pixel sizes stay constant at any zoom. Under rotation, the overlay rotates with
the tile composite (Appendix F.11).

`S100VectorTileRenderer.TryGetPartitionedScene(layer, out base, out overlay)`
returns a layer's base and overlay scenes for checking without pixels.
`MultiProductParityTests` uses it to check that point symbols never suppress
labels.

Because the overlay redraws every symbol and sounding each frame, the costs
described in
[How `SkiaDisplayListRenderer` draws a scene](../EncDotNet.S100.Renderers.Skia/README.md#how-skiadisplaylistrenderer-draws-a-scene)
matter here: symbol pictures are cached, off-screen point and text ops are
skipped first, and fonts and paints are reused. `DrawOverlay` passes a cull
rectangle widened to the rotated viewport's bounding box, so nothing visible is
dropped under rotation.

### Prediction

To avoid showing cold tiles during a pan or zoom, the tiled renderer rasterises
tiles before they scroll into view (Appendix D). Each frame it estimates the
viewport centre's velocity as a moving average of frame-to-frame changes
(`VelocityEstimator`, EPSG:3857 m/s) and builds a warm set
(`TileGrid.PredictedTiles`): a one-tile ring around the visible range, a fan
along the direction of travel whose depth grows with speed (0.5 s ahead, up to 4
tiles), and the centre tiles of the bands above and below, so a zoom step finds
them ready.

The warm set is a separate, lower-priority queue (`PendingPredicted`). Workers
drain visible misses (`PendingVisible`) first, so prediction never delays a tile
on screen. The set is rebuilt, which cancels the old one, every frame.

A predicted tile doesn't request a repaint when it's published; only visible
tiles do (`ShouldRequestRedraw`). A repaint for an off-screen tile would change
nothing but would start a frame that predicts and publishes the next tile, a
repaint loop that never lets the map settle (Appendix F.7).

Prediction is on by default; `S100_VECTOR_TILE_PREDICT=0` turns it off. In a
20-step pan on PDB01, frames showing cold tiles fell from 58% to 16% with
prediction on, at about a 32% prediction hit rate. The remainder was the cold
start; the steady pan itself had none.

Telemetry: `s100.render.tile.prediction.hits` and `.rasterized` count
predictions, and the `s100.render.tile.cold.exposure` histogram measures cold
tiles shown. `s100.render.tile.cold.latency` (ms) is the time from a visible
tile first being seen cold to its publication, queue wait included;
`s100.render.tile.rasterize.duration` is the raster time alone; and
`s100.render.tile.visible.queue.depth` is the number of cold misses a gesture
creates. A slow tiling worker shows high cold latency and a deep queue with
cheap paints; slow Mapsui paints show low cold latency and long paint times.

### Cross-band pre-warm

Prediction warms only the centre tiles of the neighbouring bands, so a zoom
across a band boundary still waits for most of the new band. When a layer is
otherwise idle, cross-band pre-warm rasterises the whole viewport in both
neighbouring bands (`TileGrid.CrossBandPrewarmTiles`).

It runs in a third, lowest-priority queue (`PendingCrossBand`), behind visible
and predicted tiles. It's queued only on frames with no cold visible misses and
while the hot cache is below 75% of its budget, so it never evicts the current
tiles; visible target-band tiles are also protected with `TileCache.Protect`.
The set starts at the centre and is capped at 24 tiles per frame. Like
prediction, its tiles never request a repaint and are rebuilt every frame; a
later zoom that uses one counts as a prediction hit.

Cross-band pre-warm is on by default except on the `LowEnd` tier, where you can
still turn it on. `S100_VECTOR_TILE_XBAND=0` (`CrossBandPrewarmEnabled`) turns
it off. To measure a zoom, read time-to-fill at the new band from
`s100.render.tile.cold.latency` and `s100.render.tile.prediction.hits`.

### Metatile raster jobs

The tiled renderer can take pending tiles from one aligned 2×2 block in one
priority tier, rasterise them together, and split the result back into ordinary
tile cache and disk entries. SCAMIN visibility is checked for each row; a job
splits by row when visibility differs. Per-tile cold latency, prediction
accounting, eviction and repaint behaviour don't change.

Metatiling is experimental and off by default. Turn it on with
`S100_VECTOR_TILE_METATILE=1`, `ChartEngine.Options.TileMetatileEnabled`, or
SoundCharts' **Batch adjacent tiles** setting. Measure it with
`s100.render.metatile.rasterize.duration`, `.slice.duration`, `.tiles`, `.jobs`
and `.fallbacks`. The fallback counter has a `reason` tag: `sparse`, `disk`,
`scamin` or `dimension`. Leave it off unless A/B runs on real cells show lower total raster
time without worse cold latency, paint time, memory or pixels.

### Idle memory compaction

Cell loads, portrayal pre-warm and tile rasterisation allocate gigabytes in
bursts, and Server GC keeps the memory it committed for them. A host can start
`IdleMemoryCompactor` (SoundCharts does) to give it back. Once at least 512 MB
has been allocated since the last compaction and the process has been quiet for
3 seconds (allocating under 32 MB/s, no visible tiles waiting, no view change),
it runs one blocking, compacting gen-2 collection, at most once a minute. On
.NET 10, Server GC decommits the freed memory only a step at a time during later
collections, so the compactor then runs a gen-0 collection (about 1 ms) once a
second while the process stays quiet, until the committed size is within
256 MB of the heap or a minute has passed. Set `S100_IDLE_GC=0` to turn it off.

### Disk tile cache

Below the in-memory cache, `TileDiskCache` keeps PNG-encoded tiles on disk
(Appendix E). They survive a layer rebuild, so switching back to a palette reuses
them, and a restart. A tile missing from memory is read from disk on the worker
before it's rasterised again. Visible raster results are published immediately,
then offered to a bounded, process-wide write queue; predicted results are
stored only once they become visible. One low-priority writer encodes and stores
tiles atomically. Duplicate or overflow work is dropped rather than blocking a
render worker.

The cache directory is namespaced by
`SHA-256(productLayerSet | styleStateHash)`. `MapsuiDisplayListRenderer`
computes `styleStateHash` from the palette, the symbol and text scales, and the
drawing instructions, which already encode display category, safety contour and
every feature and portrayal selection. Any change to these gives a new
namespace, so a tile is never served for a different palette or mariner state.
Orphaned tiles are removed by the size-limited LRU sweep. The palette is hashed
by `DescribePalette`, its `Name` plus its ordered colours, not by
`ColorPalette.ToString()`, which has no override and would make every palette
look the same (Appendix F.9). The renderer also adds the tile's pixel size to
the namespace (`TileDiskCache.NamespaceFor(ns, tilePixelSize)`, for example
`…-768px`), so a 1x tile is never served to a 2x frame. Bumping
`TileDiskCache.FormatVersion` makes older stored tiles misses.

The cache works like `DiskPortrayalInstructionCache`: atomic temp-file-and-move
writes, LRU eviction by modification time down to a byte budget, and any error
treated as a miss. Reads don't wait for writes. The queue drains on a normal
exit; if the process is killed, pending writes can be lost.

| Variable | Default |
|---|---|
| `S100_VECTOR_TILE_DISK` | On |
| `S100_VECTOR_TILE_DISK_DIR` | A subdirectory of the OS temp directory |
| `S100_VECTOR_TILE_DISK_MB` | Set by the [performance profile](#performance-profile) |

Telemetry: `s100.render.tile.disk.hits`, `.writes`,
`s100.render.tile.disk.write_queue.depth` and `.discarded`. On PDB01, a Day,
Night, Day palette switch produced two separate namespaces; 163 tiles were
stored and 198 served from disk on the switch back instead of being
rasterised again. On a 360-step navigation route over 16 overlapping UK S-101
cells, moving writes off the render workers cut tile p95 from 781 ms to 49 ms,
frame p95 from 23 ms to 12 ms, and viewport-command p95 from 92 ms to 2.3 ms,
while completing 735 background writes. With the disk cache off, the same route
measured 30 ms, 8.8 ms and 3.1 ms.

### GPU texture residency

GPU residency keeps composited tiles as GPU textures so a steady pan or zoom
doesn't upload the same pixels every frame. A profile of a steady pan without it
put 98% of render-thread native time in `BlitTile` → `SKCanvas.DrawImage`,
re-uploading unchanged tiles (Appendix F). With residency, the first time a
raster tile is composited it's promoted with `SKImage.ToTextureImage(GRContext)`
into a per-layer GPU `TileCache`, and later frames draw the resident texture.
`s100.render.tile.gpu.uploads` and `.hits` track reuse.

Residency only applies to GPU-backed surfaces. `SKCanvas.Context` is `null` on a
software surface, and the renderer then uses the raster path. On an Apple
silicon Metal surface, a steady pan went from about 38 ms to about 3 ms per frame
with a 96–99% GPU hit rate.

It's off by default (`S100_VECTOR_TILE_GPU`; budget `S100_VECTOR_TILE_GPU_MB`).
On a paced pan and zoom route over 13 UK S-101 cells, eager `ToTextureImage`
promotion and texture churn could block the compositor thread: the slowest frame
was 3,155 ms and p95 591 ms with residency, against 123–160 ms and 75–79 ms
without.

### GPU resource lifetime and shutdown

- **Thread confinement.** GPU-backed `SKImage`s must be created and freed on the
  render thread, which owns the GPU context. Freeing one on the finalizer thread
  crashes Skia's native GPU backend. All GPU texture changes go through
  `ManageGpuResidency` and `BlitTile`, on the render thread under the layer
  lock. A closed dataset, a palette change that swaps in a new layer, or a layer
  collected by the GC leaves a `TileState` that never renders again. So every
  GPU texture cache is held by a process-wide registry with a strong reference
  to the cache and a weak reference to its layer. When the layer is collected,
  the next render disposes the orphaned cache on the render thread. On PDB01,
  four close-all and reopen cycles ran with no crash, frames at 6–9 ms and a
  96% GPU hit rate.
- **Deferred disposal.** `SKCanvas.DrawImage` is deferred: Skia reads the
  texture when it flushes, after the render method returns, so a texture must
  outlive the frame that drew it. The GPU `TileCache` is built with
  `deferDisposal: true`: evicted, replaced or cleared textures are freed at the
  start of the next frame's `Composite` (`DrainPendingDisposals()`), after the
  earlier frame has flushed. Drawing fallback tiles only from the single nearest
  band also bounds the number of draws per frame, so zooming out to the whole
  world doesn't composite the entire cache at once.
- **Faults.** The paint block and the rasterisation worker reset their state
  through one guarded path, so an exception during paint drops one frame
  (`s100.render.tile.faults`) instead of leaving a blank chart. On PDB01, with
  GPU residency on and off, zooming in, out to the whole world and back in
  rendered correctly with no crash or blank frame.
- **Rotation surfaces.** The off-screen composite of a rotated frame, a
  GPU-backed `SKSurface` and its `SKImage` snapshot, follows the same rule.
  Normally the next `Composite` frees the previous pair. When a layer is torn
  down with a rotated frame still set, for example when a palette change swaps
  in new layers, the pair is mirrored into the layer's `GpuRegistryEntry`, so
  `ReconcileGpuCaches` frees it on the render thread. Only the small GPU pair is
  kept alive; the CPU tile cache stays on the weakly held `TileState` and can be
  collected. Software rotation surfaces can be finalized on any thread and
  aren't mirrored.
- **Shutdown.** Workers call native Skia, so the process mustn't unload
  `libSkiaSharp` while a worker is rasterising; that crashes with `SIGSEGV`.
  `S100VectorTileRenderer.ShutdownAndDrain(timeout)`, backed by
  `WorkerDrainGate`, sets a permanent draining flag and waits for running workers
  to finish. Each worker registers before it starts, and a refused worker returns
  before calling Skia. SoundCharts calls it from
  `IClassicDesktopStyleApplicationLifetime.ShutdownRequested`, which Avalonia
  raises on every exit path. `WorkerDrainGateTests` cover the synchronization.
- **Diagnostics.** Set `S100_VECTOR_TILE_DIAG=1` to write a compositor summary to
  stderr about once a second: target-band completeness, fallback bands drawn,
  and cache and GPU residency, plus a line whenever the layer draws nothing.

## Stability and versioning

The supported surface of this package is `ChartSessionOptions`, `ChartEngine`,
`ViewState`, `WorldRect`, `LayerFrame`, `LayerRepaint`, `SettleLayer` and the
public members of `S100VectorTileRenderer` and `S100VectorSceneRenderer`.
Internal and undocumented types can change at any time. The package is a
preview: the static entry points are expected to move onto a per-map session.

All `EncDotNet.S100.*` packages share one version, taken from the release git
tag. Versioning follows [Semantic Versioning](https://semver.org/): from `1.0.0`
on, a breaking change to the supported surface comes only in a major version.
Below `1.0.0`, a minor version can include breaking changes, and the release
notes list them.
