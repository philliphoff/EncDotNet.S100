# EncDotNet.S100.Renderers.Mapsui

Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection.

## Overview

This library bridges the S-100 portrayal pipeline output to Mapsui map layers, including full CRS projection support (EPSG:3857 Web Mercator). Key types include:

- **`MapsuiCoverageRenderer`** — `ICoverageRenderer<ILayer>` implementation that renders coverage data as a georeferenced raster overlay (S-102 / S-104 / S-111).
- **`MapsuiCoverageArrowRenderer`** — renders current arrows (e.g. from S-111 data) as one vector `PointFeature` per selected grid cell, each carrying an SVG `ImageStyle`. Subsamples dense grids both by a grid cap (`MaxArrowsPerAxis`) and a viewport-aware screen-spacing floor (`MinArrowSpacingPixels`) so arrows stay legible and per-pan draw cost stays bounded.
- **`MapsuiDisplayListRenderer`** — product-agnostic vector renderer that consumes a list of `DrawingInstruction`s plus an `IFeatureGeometryProvider` and produces a `MemoryLayer` of styled point/line/area/text features. Used by every S-100 vector product (S-101, S-124, S-129, S-421); no per-spec subclass is required.
- **`MapsuiDatasetRenderer`** — the entry point that converts a dataset processor's renderer-neutral portrayal output into a Mapsui-owned `MapsuiDatasetResult` (layers + extent). It consumes the `IVectorPortrayalSource` / `ICoveragePortrayalSource` seam exposed by `EncDotNet.S100.Datasets.Pipelines` and owns everything Mapsui-specific: the NTS pattern-clip cache, feature-type tagging, out-of-scale-band cap application, S-101 area/line `ILayer` build, the S-111 arrow renderer, and the Mapsui-typed S-98 layer-stack. This is the seed of the future multi-layer renderer in issue #213 (which will adopt `IS100DatasetRenderer<IReadOnlyList<ILayer>>`); adopting that interface later is purely additive.

> **Dependency direction (issue #189).** This package now references
> `EncDotNet.S100.Datasets.Pipelines` (not the other way round), so that the
> Pipelines assembly — and the headless facade / CLI built on it — stay
> Mapsui-free. As a consequence this package **multi-targets `net10.0` only**
> (it depends on the net10.0-only Pipelines assembly), whereas the rest of the
> libraries multi-target `net8.0;net10.0`. The Mapsui-typed
> `MapsuiDatasetResult` is owned by this package and namespace.

> **CRS transforms** moved to the Mapsui-free **`EncDotNet.S100.Crs.ProjNet`**
> package (`ProjNetCrsTransformFactory`) so headless consumers can reproject
> coverage products without linking a map renderer.

## Initialization

Call `S100MapsuiRendering.Register()` once during application startup, before
installing any diagnostics that wrap Mapsui's renderer registry:

```csharp
S100MapsuiRendering.Register();
```

The method registers every S-100 style and custom-layer renderer in dependency
order and is safe to call repeatedly. Applications must call this entry point
before rendering S-100 layers; render operations do not mutate Mapsui's global
renderer registry implicitly.

## Rendering options

`S100MapsuiOptions` captures Mapsui-specific configuration for a renderer or
future map session. Its defaults are copied from the existing
environment-backed `RenderingOptimizations` store, while explicit values let a
reusable host configure rendering without mutating process-global state:

```csharp
var options = new S100MapsuiOptions
{
    RenderSubsystem = RenderSubsystemKind.TiledScene,
    SceneMode = VectorSceneMode.Tiled,
};
var renderer = new MapsuiDatasetRenderer(
    crsTransformFactory,
    patternClipCache,
    options);
```

The render subsystem and vector-scene mode are the first settings captured by
this object. Other optimization settings will move from
`RenderingOptimizations` incrementally. Omitting `options` preserves the
existing live global behavior used by the Viewer and performance harnesses.

## Layer-band composition

`MapsuiLayerBands` owns the ordered S-100 layer bands of an existing
`Mapsui.Map` without depending on a UI-framework map control:

```csharp
var bands = new MapsuiLayerBands(map);
bands.SetBasemapLayer(basemap);
bands.AddDatasetLayer(datasetLayer);
bands.AddOverlayLayer(validationLayer);
bands.AddToolLayer(measureLayer);
```

The resulting order is always basemap → datasets → overlays → tools.
`ReplaceDatasetLayers` authoritatively replaces or reorders only the dataset
band, leaving the other bands in place. Each corresponding remove method
removes only layers owned by that band. Calls mutate `Map.Layers` immediately;
Avalonia, MAUI, and other UI hosts remain responsible for thread dispatch and
redraw invalidation.

## Viewport navigation

`MapsuiMapNavigator` provides the small navigation surface already used by
S-100 interactive hosts against an existing `Mapsui.Map`:

```csharp
var navigation = new MapsuiMapNavigator(map);
navigation.ZoomToExtent(datasetExtent);
navigation.CenterOn(new GeoPosition(latitude, longitude));
```

It supports padded dataset framing, exact scripted extent or
center/resolution changes, rotation, WGS-84 recentering, and WGS-84 viewport
center reporting. Exact scripted changes are instantaneous; framing and
recentering accept animation durations while preserving their prior defaults.
The adapter does not own the map, duplicate normal Mapsui gestures, marshal to
a UI thread, invalidate a control, or automatically zoom after a load.
Avalonia, MAUI, and other hosts retain those policies and thread-affinity
responsibilities.

`MapsuiDisplayListRenderer` lowers the display list through the **shared,
backend-agnostic vector rendering core** in
`EncDotNet.S100.Rendering.Scene` (`VectorSceneBuilder` → `VectorScene` of
`PaintOp`s). All S-100 Part 9 portrayal-correctness logic — draw ordering,
colour/symbol/line-style resolution, mm→px conversion, text-anchor selection,
and the `lat/lon → EPSG:3857` projection half — lives in that core and is shared
with the headless `SkiaDisplayListRenderer`; this renderer only constructs
Mapsui `IFeature`/style objects from the resolved IR. Pattern fills are the one
exception: they are not yet part of the IR and keep their dedicated pattern
collection / priority-clip / insert phase here.

`MapsuiDisplayListRenderer` honours the relevant S-100 Part 9 conventions:

- Pen widths and text/symbol offsets specified in millimetres on the nominal display surface are converted to screen pixels using the standard `1 px = 0.32 mm` ratio (S-100 Part 9 §3.10.4).
- `<foreground>` / `<background>` colours accept either a palette token or a literal `#RRGGBB` / `RRGGBBAA` hex value, with the optional `transparency` attribute applied as alpha attenuation.
- Text alignment, mm offsets, and `textLine` start/end offsets (Relative or Absolute) are honoured per S-100 Part 9 §11.4.
- `LineStyleProvider`, `SymbolProvider`, and `AreaFillProvider` callbacks let the host project plug in a portrayal catalogue without coupling the renderer to a specific dataset library.
- **Scale-visibility limits are latitude-corrected.** S-100 Part 9 §11.1 scale denominators (per-feature `ScaleMinimum`/`ScaleMaximum`, and the cell-wide out-of-band cap derived from `DataCoverage.minimumDisplayScale`) are *true-scale* values, whereas a Mapsui `resolution` is metres/pixel at the EPSG:3857 equator. Because web-mercator inflates ground distance by `1/cos φ`, the equator-referenced resolution for a denominator is `denom × 0.00028 / cos φ` (`MapsuiDisplayListRenderer.DenominatorToResolution`). Per-feature limits convert at the feature's extent-centre latitude; the cell-wide cap converts at the layer's extent-centre latitude. Omitting the `cos φ` term (the prior behaviour) was only correct on the equator and suppressed detail roughly `1/cos φ` zoom levels too early — at φ ≈ 50.8° (≈ 1.58×) a cell's linework vanished about two-thirds of a zoom level before it should. This now matches the Skia headless backend, which already applies `cos(midLat)`.
- **Cell-wide zoom-out window from the exchange-set catalogue (`ApplyCellScaleWindow`).** Independent of the in-file per-feature cap above, `MapsuiDatasetRenderer.ApplyCellScaleWindow(layers, minimumDisplayScale)` clamps every layer's `MaxVisible` to `DenominatorToResolution(minimumDisplayScale, φ)` at the layer's extent-centre latitude, where `minimumDisplayScale` is the *coarsest permitted* denominator resolved from the cell's `CATALOG.XML` `DataCoverage` entries (max of the per-coverage `minimumDisplayScale` values). It only ever **tightens** an existing `MaxVisible`. Unlike the M_COVR-derived per-feature cap (which applies to the linework sub-layer only), this window suppresses the **whole cell — area fills included** — once you zoom out past the cell's smallest-scale edge, so a finer cell drops out entirely and the coarser cell nested beneath it shows through. This is *hole-safe*: as you zoom out, finer cells (smaller `minimumDisplayScale`) drop first, always leaving a coarser cell underneath (issue #438, Phase 1). The zoom-*in* overlap edge (`maximumDisplayScale`) is **not** wired here because a finer cell usually covers only part of a coarser footprint; suppressing the coarser cell on zoom-in would leave open water blank between finer cells, so that is deferred to a coverage-polygon-clipping phase. The caller (`DatasetLoaderService`) gates this on the mariner `IgnoreScaleMinimum` setting and re-applies it on every re-render. For a **standalone-loaded cell** (no `CATALOG.XML`), the caller falls back to a `minimumDisplayScale` the processor derives from the dataset's own content (`MapsuiDatasetResult.CellMinimumDisplayScale` — S-101 in-file `DataCoverage.minimumDisplayScale`, or the S-57 DSPM compilation scale), so an individually opened `.000` cell hides — with its out-of-scale extent border (issue #446) — exactly as it would inside an exchange set (issue #450 follow-up).
- **Cross-cell coverage clip / "larger-scale-in" overlap suppression (`OverlapSuppression` + `CoverageClip`, issue #438 Phase 2).** The zoom-*in* seam Phase 1 deferred: where a finer, overlapping in-band cell provides coverage, the coarser cell must stop contributing (no depth-area / fill bleed under the harbour cell). This is done as a **true geometry clip** in screen space, not a scale cap, and it is **zoom-aware** — a finer cell only suppresses a coarser cell while the finer cell is itself visible at the current resolution, so zooming out (which drops the finer cell via the Phase 1 window) never leaves a blank hole in the coarser cell. Each cell's EPSG:4326 data-coverage footprint (`VectorPortrayalResult.CoverageAreas`) is projected to EPSG:3857 and carried on `MapsuiDatasetResult.CoverageGeometry` (`MapsuiDatasetRenderer.ToMercatorCoverage`, `Buffer(0)`-normalized). `DatasetLoaderService` builds an `OverlapSuppressionCell` per loaded cell (`Layers`, `Coverage`, `ScaleDenominator` from `MinimumDisplayScale ?? CellMinimumDisplayScale`, and `CutoffResolution` = the cell's Phase 1 `ContentMaxVisibleResolution` drop-out) and calls `OverlapSuppression.Apply(cells)`, which for each cell gathers every **strictly finer** (smaller denominator) overlapping cell `F` as a `FinerCoverage(coverage(F), cutoff(F))` and attaches the list via `CoverageClip.Set(layer, finerCoverages)`. Equal-band siblings never mutually clip (that would erase shared borders); a cell with no finer overlap gets **no** entry (paints in full). Rather than pre-computing `coverage(C) − union(...)` in NTS (expensive per-zoom and `TopologyException`-prone), the custom layer renderers (`S100VectorTileRenderer`, `S100VectorSnapshotRenderer`) call `CoverageClip.BuildActiveDifferencePaths(layer, viewport, resolution)`, which projects an even-odd screen-space `SKPath` for **only** those finer coverages still visible at the live `resolution` (`cutoff is null || resolution <= cutoff`); the renderer then wraps its drawing in `SKCanvas.Save()` + one `ClipPath(path, SKClipOperation.Difference, antialias: true)` per active path + `Restore()`. Successive difference clips compose the union naturally, and each even-odd path preserves the finer cell's no-coverage holes so the coarser cell shows through them (*hole-safe*). The clip is **screen-space** so it never invalidates a cached tile — only the composite clip region varies per frame — and it stores world coordinates so a constant-zoom pan needs no recompute. `DatasetLoaderService` re-runs `Apply` on every load / unload / removal and gates the whole feature on `IgnoreScaleMinimum`.

### Sharing processed-SVG and pattern-tile work across renders

`MapsuiDisplayListRenderer` resolves SVG symbols and rasterises area-fill pattern tiles lazily on first reference. The processed-SVG output depends on the active `ColorPalette` (fill/stroke colours are recoloured against the palette), and pattern-tile rasterisation is comparatively expensive.

When a single dataset is re-rendered repeatedly — typical when toggling palettes, scrubbing time-steps, or changing mariner settings — assign a single `MapsuiRenderAssetCache` instance to the renderer's `AssetCache` property on every `Render()` call:

```csharp
private readonly MapsuiRenderAssetCache _renderAssetCache = new();

// per Render():
var renderer = new MapsuiDisplayListRenderer
{
    Palette = palette,
    AssetCache = _renderAssetCache,
    SymbolProvider = name => catalogue.GetSymbol(name).SvgContent,
    AreaFillProvider = name => catalogue.GetAreaFill(name),
};
```

The cache segments entries per palette (`Day` / `Dusk` / `Night`) so flipping back and forth keeps every palette warm. When `AssetCache` is unset, the renderer falls back to a per-instance cache, which preserves legacy behaviour for ad-hoc / one-shot callers.

### Caching the coverage projection layout across re-renders

`MapsuiCoverageRenderer` reprojects every grid node from the coverage's
native CRS to Web Mercator and derives a node→pixel mapping. That work
depends only on the grid geometry (native CRS, dimensions, and the
affine origin/spacing), so it is **independent of the colour palette,
ECDIS display mode, and the per-cell values**. The renderer caches the
resulting `int[]` node→pixel index array (along with the output raster
dimensions and Mercator extent) keyed on those geometry parameters, and
reuses it whenever the next render presents the same geometry — e.g. a
palette switch or a coverage time-step change. Only the value
classification + pixel fill + PNG encode re-run; the projection pass is
skipped.

To benefit, keep the renderer instance alive across renders rather than
constructing a fresh one each time (`S102DatasetProcessor` and
`S104DatasetProcessor` hold the renderer in a field). The cache is a
single-slot, value-keyed entry published atomically, so it stays
correct if a renderer is ever reused for a different geometry (the key
mismatch forces a rebuild). It caches only the compact index array, not
the per-node Mercator coordinates, to bound memory (~4 MB per
megapixel grid).

## Dynamic feature sources

`EncDotNet.S100.Renderers.Mapsui.DynamicSources` hosts the Mapsui-bound side of the dynamic-feature-source abstraction defined in `EncDotNet.S100.Core` (see [`docs/design/dynamic-feature-source.md`](../../docs/design/dynamic-feature-source.md)). Renderers turn `DynamicFeature` snapshots into Mapsui `IFeature` + `IStyle` instances that the viewer's `DynamicSourceOverlayHost` attaches to a `MemoryLayer` through its focused map-layer collection capability.

- **`IDynamicFeatureRenderer`** — `CanRender` + `Render` contract. Implementations are stateless functions of one feature; the overlay host owns the layer-level state and UI-thread marshalling.
- **`DefaultDynamicFeatureRenderer`** — geometry-kind-dispatching fallback: coloured disc + optional speed-scaled heading line (six-minute predictor capped at 10 nm) for `Point`, stroked polyline for `Curve`, translucent fill + outline for `Surface`. Also the safety-net renderer when a source's `RendererKey` is `null` or unregistered.
- **`OwnShipRenderer`** — own-ship symbology under key `"ownship"`. Draws a true-scale 5-vertex hull polygon when the on-screen vessel length exceeds `MinVesselPixels` (22 px ≈ 6 mm @ 96 dpi), a coloured disc otherwise, plus a heading vector with filled-triangle arrowhead in both modes and a CCRP cross at the GPS antenna in outline mode. Uses `DynamicFeature.VesselGeometry` (CCRP offsets) to place the hull around the antenna and gates the outline / pictogram via mutually-exclusive `MinVisible` / `MaxVisible` styles so the renderer stays viewport-agnostic. Falls back to pictogram-only when no `VesselGeometry` is supplied (e.g. AIS targets with unknown dimensions). See [`docs/design/own-ship-symbology.md`](../../docs/design/own-ship-symbology.md).
- **`KindMatchingRenderer`** — dispatches by `DynamicFeature.Kind` via exact match or dot-namespaced prefix match (e.g. `"vessel"` matches `"vessel.cargo"`). Longest-key-first ordering keeps prefix matching deterministic.
- **`CompositeDynamicFeatureRenderer`** — first-`CanRender`-wins fallthrough over an ordered list. Conventional ordering: per-kind specialists first, `DefaultDynamicFeatureRenderer` last.
- **`DynamicFeatureRendererServiceCollectionExtensions`** — DI helpers that register renderers under the same string key a source advertises via `DynamicSourceMetadata.RendererKey`:

  ```csharp
  // Register a source and its renderer in one call:
  services.AddDynamicFeatureSource<MyAisFeed, MyVesselRenderer>("vessel");

  // Or just a renderer, for cross-source sharing:
  services.AddDynamicFeatureRenderer<MyVesselRenderer>("vessel");
  ```

  The viewer's overlay host resolves the renderer at registration time via `IServiceProvider.GetKeyedService<IDynamicFeatureRenderer>(source.Metadata.RendererKey)`.

## Performance instrumentation

The renderer ships with optional OpenTelemetry instrumentation that
attributes paint cost down to the style-renderer, layer, and geometry
vertex count. All instruments are sub-millisecond per paint when no
OTel listener is attached, so they are safe to leave in production
builds.

| Instrument | Unit | Tags | Purpose |
|---|---|---|---|
| `s100.map.paint.duration` | ms | — | Compositor-thread paint wall-time per frame |
| `s100.map.paint.interval` | ms | — | Time between paints (idle gaps > 500 ms dropped) |
| `s100.map.paint.style.calls` | count | `style`, `layer`, `points` | Style-renderer `Draw` calls per paint |
| `s100.map.paint.style.duration` | ms | `style`, `layer`, `points` | Cumulative `Draw` duration per paint |
| `s100.layer.get_features.duration` | ms | `layer` | Layer-level filter cost per `GetFeatures` call |
| `s100.layer.get_features.visible` / `total` | count | `layer` | Visible / total feature counts per call |
| `s100.layer.get_features.fps` | gauge | `layer` | Effective `GetFeatures` rate per layer |
| `s100.pattern_fill.draw.duration` | ms | — | `AnchoredPatternFillRenderer` per-call cost |

The `points` tag is bucketed (`1-9`, `10-99`, `100-999`, `1k-10k`,
`10k-100k`, `100k+`) to keep histogram cardinality bounded while
still revealing whether a layer's cost is driven by many cheap draws
or a few expensive ones.

To capture a measurement session, run the viewer with the OTel console
exporter enabled:

```sh
ENC_DOTNET_OTEL_CONSOLE=1 OTEL_METRIC_EXPORT_INTERVAL=2000 \
  dotnet run -c Release --project src/EncDotNet.S100.Viewer
```

Histograms are emitted every 2 s with cumulative counts and per-bucket
distributions. Aggregate by `(layer, points)` to identify which
geometries are dominating paint time — empirically, ~93% of paint cost
on real-world S-101 datasets is spent on geometries with ≥100 vertices,
with per-vertex cost ~1 µs. See
[`docs/design/mapsui-performance.md`](../../docs/design/mapsui-performance.md)
for the full investigation and optimization plan.

## Resolution-aware geometry simplification

The cached vector-style renderer (`CachedVectorStyleRenderer`, the
registered `VectorStyle` renderer) reduces the vertex count Skia
tessellates per frame by generalizing geometry **at `SKPath`-build time**,
keyed by the build resolution. Because the simplified path is cached per
`(feature, position, resolution)` and reused across every pan — and the
vector-snapshot record + off-thread prebuild draw through this same
renderer — the cost is paid once per `(feature, zoom)` and inherited by
all downstream consumers. Dropped detail is by construction sub-pixel *on
screen* at that zoom, so the result is visually indistinguishable at every
zoom level. On real S-101 datasets dense **line** geometry (contours,
coverage boundaries) typically simplifies by 5–10× at common pan zooms
with no visible regression at the default 0.6-pixel tolerance. Simplification
applies to **line** geometry only; polygon areas are always rendered
vertex-exact (see [Polygons](#polygons)).

### Lines

`LineString`/`MultiLineString` are simplified inline while building the
path: consecutive vertices that project to within the pixel tolerance of
the last emitted vertex are dropped (a radial-distance filter in the
anchored pixel frame). This collapses the dense sub-pixel vertex runs of
S-101 bathymetry contours so the Skia stroker rasterises far fewer
segments.

### Polygons

`Polygon`/`MultiPolygon` (land areas, depth areas, sea areas — the
highest-vertex S-101 features) are **fast-pathed and cached vertex-exact**:
each part's projected `SKPath` is built once per `(feature, position,
resolution)`, reused across pans under an affine draw matrix, and bounded
by the cache's coordinate budget. They are **not** geometrically
simplified.

Topology-preserving polygon simplification (NTS `TopologyPreservingSimplifier`
+ `IsValid`/`Buffer(0)` validation) was implemented and **measured, then
removed**: a live viewer A/B showed it provides **no paint benefit** on the
GPU path and is reproducibly *worse* under multi-dataset pressure. The
translation-invariant path cache already neutralizes vertex count on warm
paints (cache-served, ~0 ms), so dropping vertices cannot make warm paints
cheaper, while cold builds pay the simplifier cost; under cache pressure
that cost is re-paid on every rebuild, and GPU (Metal) fill is area-bound,
not vertex-bound. See `docs/design/mapsui-performance.md` for the data.

### Gating

A **Simplify dense geometry** setting
(`RenderingOptimizations.GeometrySimplificationEnabled`, default on) with a
pixel tolerance (`SimplificationTolerancePx`, default 0.6, seeded from
`S100_VECTOR_SIMPLIFY_PX`) governs **line** simplification — the proven,
default-on win — and is the only simplification knob. Polygons are always
vertex-exact and have no simplification toggle.

Simplification requires the path cache (`S100_VECTOR_PATH_CACHE`); changing
the effective tolerance clears the cache so rebuilt paths reflect the new
tolerance.

### Cache (coordinate-budget eviction)

The path cache evicts least-recently-used entries until under **both** an
entry cap (default 8192) and a coordinate budget (`MaxCachedCoordinates`,
default 5 M coords ≈ 80 MB). Bounding by coordinate count — not entry
count — keeps memory predictable now that dense polygon paths (tens of
thousands of vertices) share the cache with tiny features. Evicted
`SKPath`s are deliberately **not** disposed (a drawing thread may still
hold a reference outside the lock); they are reclaimed by GC finalization.

### Telemetry

| Instrument | Unit | Purpose |
|---|---|---|
| `s100.simplify.cache.hit.count` | count | Built path served from cache |
| `s100.simplify.cache.miss.count` | count | Path (re)built |
| `s100.simplify.cache.coords.tracked` | count | Live coords across all cached paths (drives budget eviction) |

### Known limits

- The miss path runs synchronously on the render (or prebuild) thread.
  After a zoom change the first paint at the new resolution may stall
  briefly while visible paths are rebuilt; subsequent frames at
  that zoom hit the cache, and sustained pan is served by the vector
  snapshot.
- Lines use a radial-distance filter rather than true Douglas-Peucker;
  the difference is visually negligible at sub-pixel tolerance. Unifying
  lines onto NTS DP is documented as a possible future micro-opt in
  `docs/design/mapsui-performance.md`.

### Precomputed line LOD (opt-in)

An **opt-in** precomputed line-LOD pyramid replaces the inline radial-distance
filter with a small pyramid of Douglas-Peucker levels
(`LineLodTolerances.HalfOctaveDefault = [256, 64, 16]` metres) built once
per feature on first paint and cached in-process. At paint time the
renderer picks the level whose tolerance is ≤ half a screen pixel at the
current ground resolution — coarser zooms pick coarser levels — so pans
inside a zoom band re-key against the same LOD bucket, absorbing float
noise from the caller's resolution and eliminating the cold rebuild after
a band change.

Enable it by setting `RenderingOptimizations.PrecomputedLineLodEnabled`
= `true` (or the `S100_VECTOR_LINE_LOD` env var). It is **off by default**;
when off, lines fall back to the inline radial-distance filter and the
raw-resolution cache key described above (no behaviour change vs.
earlier releases).

Measured on the dense S-101 trial cell `101GB00GB302045` (2.35 MB,
11 589 drawing instructions, viewer + Skia + Metal):

| metric (rolling window, palette-flip stress) | Off | On   | Delta   |
|---|---|---|---|
| vector paint **max** (spike)                 | 139 ms | 15 ms | **-89%** |
| whole-frame max                              | 394 ms | 68 ms | **-83%** |
| vector paint **mean**                        | ~2 ms  | ~1 ms | -50%   |
| vector paint **P95**                         | 7.6 ms | 3.7 ms | -51%   |

Screenshot A/B at each scale band (bbox / z13 / z14 / z15 / z16) is
**pixel-identical** to the flag-off output — the LOD tolerances are
sub-pixel per band by design.

Under multi-cell stress (six overlapping UKHO trial cells, palette
flips + cross-cell z14/z15 pans) the LOD path improves warm vecMean
(0.87 → 0.63 ms) and vecP95 (3.3 → 1.9 ms) but adds a +13 ms tax to the
first-paint vecMax while pyramids are built for every visible line
feature. That tax is the reason the flag ships default-off: on the
dense single-cell workload LOD dominates; under a shallow-detail
multi-cell workload the pyramid-build cost is paid once and *not
amortized* across the very cheap paints that dominate that regime.
A future S-101 reader hook that pre-builds pyramids at dataset open
would move the tax off the first paint.

Telemetry (in addition to the existing `s100.simplify.cache.*`
counters):

| Instrument | Unit | Purpose |
|---|---|---|
| `s100.geometry.lod.build.duration` | ms | Time to build a pyramid on cache miss |
| `s100.geometry.vertices.in` | count | Input vertices per pyramid build |
| `s100.geometry.vertices.out` | count | Output vertices per LOD level (tagged `s100.lod.bucket`) |
| `s100.geometry.lod.cache.hit.count` | count | LOD-level path served from `SKPath` cache |
| `s100.geometry.lod.cache.miss.count` | count | LOD-level path (re)built (tagged `s100.lod.bucket`) |

### Pattern-fill clip generalization

Independently of the resolution-aware line simplification above,
`MapsuiDisplayListRenderer` generalizes the polygon geometry used when
clipping tiled **pattern** fills against each other (display priority)
and against non-patterned solid fills such as land. S-101
quality/coverage areas (e.g. `M_QUAL`) can follow the coastline with
tens of thousands of vertices, the bulk of which are sub-pixel at chart
display scales. The NetTopologySuite `Difference`/`Union` overlay
operations these geometries feed are super-linear in vertex count, so a
single pathological area could dominate the whole frame (observed:
~10 s of an ~11 s frame on one 64k-vertex pattern zone in a real 2.35 MB
cell).

Before the overlay, each merged pattern geometry and the land exclusion
mask are passed through NTS `TopologyPreservingSimplifier` at a fixed
1 m (EPSG:3857) tolerance (`PatternClipSimplifyToleranceMetres`).
Topology-preserving simplification keeps the inputs valid for overlay;
the result is buffer(0)-repaired if it still validates as invalid, and
falls back to the original geometry on any failure. Because the clipped
boundary only bounds a tiled raster pattern fill, the generalization is
visually negligible (the S-101 visual-regression snapshot is unchanged).
An envelope-intersection test also short-circuits `Difference` when the
clip mask is disjoint from the entry. Together these cut the pattern
clip from ~11 s to well under 1 s on the affected cell, shaving ~6 s off
**every** S-101 frame (not just re-renders).

### Caching the pattern-fill clip across palette switches

Even after generalization, the priority clip is the dominant warm cost on
the densest cells (profiling on a ~64,000-vertex `M_QUAL` coverage area:
the clip is on the order of seconds, dominated by a single `Buffer(0)`
validity repair). The clip runs once per **layer build**
(`Render`) — not per frame — and re-fires on dataset load, palette
(Day/Dusk/Night) switch, and ECDIS display-setting changes. Crucially the
clipped boundary geometry is **palette-independent**: the renderer groups
pattern entries by the palette-independent area-fill reference, so only the
tile *colours* change per palette (applied after clipping).

`IPatternClipCache` lets a caller reuse the clip result across re-renders
whose clip inputs are unchanged — most importantly a palette switch.
Assign an `InMemoryPatternClipCache` (a single-slot cache that bounds
memory to one cell) and a key that fully identifies the clip inputs:

```csharp
private readonly InMemoryPatternClipCache _patternClipCache = new();

var renderer = new MapsuiDisplayListRenderer
{
    // … palette, providers, asset cache …
    PatternClipCache = _patternClipCache,
    PatternClipCacheKey = portrayalCacheKey, // mariner + ECDIS display state
};
```

When both `PatternClipCache` and `PatternClipCacheKey` are set, the
renderer obtains the clipped geometry via `GetOrCompute`; a palette switch
with the same key is a cache hit that skips the overlay entirely
(measured on the dense trial cell: a cold Day render ~6 s, the subsequent
Night palette switch ~0.2 s). When either is unset the clip is computed
inline, preserving behaviour for S-57/S-131/GML products and the line
renderer (which has no pattern fills).

Two implementations ship behind this contract:

- **`InMemoryPatternClipCache`** — a single-slot, per-processor cache that
  bounds memory to one cell. It only eliminates re-clip cost for re-renders
  of the *same already-open* dataset (palette/display switches) and is lost
  on close/restart.
- **`DiskPatternClipCache`** — a process-wide, disk-backed cache
  (`ctor(string cacheDirectory, long maxBytes)`). It persists each clip
  result as a WKB sidecar (filename = `SHA256(key)` hex + `.clip`) so the
  **cold first open of a previously-seen cell** skips the overlay, even
  after a restart. Writes are atomic (temp file + move) and a total-bytes
  LRU cap evicts least-recently-accessed entries; any IO/deserialization
  error or `FormatVersion` mismatch is treated as a miss (recompute) and
  never throws to the caller. Because the disk cache is process-global, the
  key must be **fully qualified** by the caller — the S-101 processor
  composes `{datasetScope}|{portrayalKey}`, where `datasetScope` encodes the
  dataset content hash, clip parameters
  (`PatternClipSimplifyToleranceMetres`, `MinPointsToSimplifyForClip`), CRS,
  and the `DiskPatternClipCache.FormatVersion` stamp, so persisted geometry
  auto-invalidates when content, parameters, or the serialization format
  change.

```csharp
// Per-processor in-memory (step 1):
private readonly InMemoryPatternClipCache _patternClipCache = new();

// Or one shared disk cache for the whole process (step 2):
var sharedClipCache = new DiskPatternClipCache(cacheDir, maxBytes: 256L * 1024 * 1024);

var renderer = new MapsuiDisplayListRenderer
{
    // … palette, providers, asset cache …
    PatternClipCache = sharedClipCache,
    PatternClipCacheKey = $"{datasetScope}|{portrayalCacheKey}",
};
```

## Translation-invariant vector path cache

`CachedVectorStyleRenderer` is a drop-in replacement for Mapsui's
`VectorStyleRenderer` (registered for `VectorStyle` by the viewer before
instrumentation wraps the renderer dictionary). It targets the dominant
pan/zoom cost on dense S-101 approach cells, where thousands of
`LineString` features (bathymetry contours) are re-projected and
re-stroked from scratch on **every** frame because Mapsui's own path
cache is keyed on the full viewport `extent`, which changes on every pan.

It addresses this in two ways:

1. **Translation-invariant path cache.** Polygons (solid fill / solid
   outline) and lines (solid `Line` pen, no casing `Outline`) have their
   projected `SKPath` built in an *anchor-relative pixel frame at the
   current resolution* and cached under `(featureId, position,
   resolutionBits)`. A pan changes only the viewport centre, so the
   cached path is re-used and the frame pays just a canvas translate plus
   the fill/stroke. A zoom changes the resolution (and the key), forcing
   a crisp rebuild — far rarer than pans. The transform reproduces
   Mapsui's `screen = (world − Center)/Res + Size/2` exactly, so output
   is pixel-identical outside simplification.

2. **Resolution-aware line simplification.** When building a line path,
   consecutive vertices that project to within `simplifyTolerancePx`
   (default `0.6`) of the last emitted vertex are dropped, with endpoints
   always preserved. Because this happens in the anchored pixel frame at
   the build resolution and the result is cached, the cost is paid once
   per (feature, zoom) and re-used across all pans. Dropped vertices are
   by construction sub-pixel *on screen* at that zoom, so the result is
   visually indistinguishable at every zoom level while removing the bulk
   of the Skia stroker's per-segment work — the real bottleneck on dense
   contours.

On the AU IC-ENC `444147` overview pure-pan (≈3,448 line features) this
cut the per-frame vector cost from ~479 ms (un-cached Mapsui) to ~71 ms
and the wall-clock frame from ~660–750 ms to ~200–225 ms — roughly a 3×
frame-time improvement — with a measured pixel diff of ≈1.5 % (anti-alias
fringes only) versus the un-simplified render.

Anything outside this scope — points, patterned/hatched fills,
dashed/casing-outlined lines, rotated viewports, and non-polygon/line
geometry — is delegated unchanged to the wrapped Mapsui renderer.

### Tuning

The four headline optimizations — the path cache, line simplification, the raster
snapshot, and the off-thread snapshot prebuild — are surfaced as user-facing knobs
in the viewer under **Settings → Map → Rendering optimizations**, backed by
[`RenderingOptimizations`](RenderingOptimizations.cs). All four default **on** (the
"best" set). The environment variables below seed those defaults and, when set
*explicitly*, pin the value so the perf A/B harness stays faithful — an explicit
env var always wins over the persisted viewer setting. The remaining variables
(margins, refresh fraction, diagnostics) are advanced and env-only.

The **render subsystem switch** (A/B), the TiledScene **scene mode**
(tiled vs single surface), and the **tiled optimization knobs** (gutter,
in-memory / disk / GPU budgets, prediction, disk cache) are likewise bound in
the viewer under **Settings → Render subsystem** (issue #331),
backed by the same [`RenderingOptimizations`](RenderingOptimizations.cs) store.
The env vars below seed and (when set explicitly) pin those too, disabling the
matching UI control. Some knobs are read each frame and apply live (subsystem,
scene mode, prediction, GPU residency); others are captured at init and apply on
the next dataset reload or restart (gutter, in-memory/disk/GPU budgets, disk
cache) — the Settings panel notes this.

| Environment variable | Default | Effect |
|---|---|---|
| `S100_VECTOR_PATH_CACHE` | on | `0`/`false` disables the renderer entirely (pure Mapsui), for A/B comparison. Also bound by *Settings → Map → Cache projected vector paths*. |
| `S100_VECTOR_SIMPLIFY_PX` | `0.6` | Line simplification tolerance in screen pixels; `0` disables simplification (vertex-exact paths). The on/off state is bound by *Settings → Map → Simplify dense line geometry*. |
| `S100_VECTOR_PICTURE_SNAPSHOT` | on | `0`/`false` disables the raster vector-layer snapshot fast path (see below); falls back to per-feature drawing every frame. Also bound by *Settings → Map → Raster snapshot on pan*. |
| `S100_VECTOR_SNAPSHOT_MARGIN` | `256` | Pixels of off-screen margin recorded around the viewport, so a pan can travel this far before the snapshot is re-recorded. |
| `S100_VECTOR_SNAPSHOT_PREBUILD` | on | `0`/`false` disables the off-thread pre-build (see below) and falls back to the single-image snapshot (synchronous re-record on zoom and on a pan past the margin). Also bound by *Settings → Map → Off-thread snapshot prebuild*. |
| `S100_VECTOR_SNAPSHOT_PAN_MARGIN` | `512` | Pixels of margin used for off-thread *pan* re-records (the sustained-pan look-ahead). Larger than `…_MARGIN` so one recentred-ahead background record covers roughly a full viewport of travel. Only used when the pre-build is on. |
| `S100_VECTOR_SNAPSHOT_PAN_REFRESH` | `0.5` | Fraction (0–1) of the active snapshot's margin at which the off-thread pan re-record is triggered (while the image still fully covers the view). Smaller = earlier/more frequent; larger = more deferred. Only used when the pre-build is on. |
| `S100_VECTOR_SNAPSHOT_DIAG` | off | `1`/`true` logs record / replay / stale / live-on-scale-band / prebuild-publish / pan-refresh decisions to stderr. |

### Raster vector snapshot

`S100VectorSnapshotRenderer` is a Mapsui *custom layer renderer* that
rasterizes a settled S-101 vector layer into a single device-resolution
`SKImage` once per (resolution, feature-set) and, on subsequent pans at the
same resolution, blits it under a translation instead of re-iterating and
re-stroking every feature. Because a raster blit is O(pixels) rather than
O(features), pure pans become independent of feature count — on the AU
IC-ENC harbour cell `101AU005PDB01` (~1,600 area/line features) pure-pan
frame time drops from ~90 ms to ~2 ms, and the vector-heavy cell `444147`
from ~270 ms to ~2 ms, with a pixel-faithful result (sub-pixel edge
anti-aliasing only).

The trade-off is the *record* frame: the first frame at each new resolution
(or after a pan past the recorded margin) re-rasterizes the whole layer at
device scale, costing more than a single live frame (~650 ms on PDB01). The
off-thread pre-build below hides that cost for both zoom and sustained pan.

**Image-source readiness at record time.** Mapsui resolves an `ImageStyle`'s
image (the SVG point symbols for buoys, beacons, lights) from its
`RenderService.ImageSourceCache`, which is normally populated by an
*asynchronous* fetch loop. The live per-frame path tolerates a cache miss
because it simply redraws the next frame once the fetch lands, but the
snapshot's one-shot record does not: a record taken before the fetch
completes would bake a symbol-less raster that the (still "valid") snapshot
never re-records, so the symbols would vanish until the next zoom. The
recorder therefore registers the layer's image sources **synchronously**
before drawing (`EnsureImageSourcesRegistered`), mirroring Mapsui's own
offscreen rasteriser (`RasterizingTileSource`, which awaits
`ImageSourceCache.FetchAllImageDataAsync` before `RenderToBitmapStream`). The
`svg-content://` / `base64-content://` sources used here resolve in-process,
so this adds no I/O wait.

**Off-thread pre-build (`S100_VECTOR_SNAPSHOT_PREBUILD`, default on).** When
enabled, the renderer keeps a small per-resolution LRU of recorded images
instead of a single image, and hides the record-frame stall in four ways:

1. **Speculative pre-build after settle** — once a frame replays cleanly, the
   predicted next zoom bucket(s) (inferred from the last two observed
   resolutions) are rasterized on a background thread, so a subsequent zoom
   lands on a ready, crisp image.
2. **Scaled-stale blit** — on a zoom whose image is not yet built, the nearest
   existing image is blitted *scaled* (one linear resample, slightly blurry)
   for a frame or two while the exact-resolution image is built off-thread.
   This also smooths continuous / pinch zoom. **A scaled-stale blit is only
   used when no scale-visibility boundary (`MinVisible`/`MaxVisible`, e.g. the
   S-101 out-of-band cap derived from `DataCoverage.minimumDisplayScale`) lies
   between the recorded image's resolution and the current one.** When a zoom
   crosses such a boundary the two resolutions have *different* visible feature
   sets — a buoy shown at one zoom is capped-hidden at the other — so reusing
   the wrong-resolution raster would briefly drop (or wrongly show) those
   features. In that case the layer is drawn **live** for that single frame
   (feature-correct, like the rotated-viewport fallback) while the
   exact-resolution image records off-thread. This eliminated an intermittent
   bug where point/text features (buoys, beacons, labels) flickered or vanished
   when zooming across the cell's display-scale cutoff.
3. **Sustained-pan look-ahead** — the original snapshot only buys
   `…_MARGIN` (256 px) of pan before a re-record, and that re-record used to
   run *synchronously* on the render thread (~250–650 ms), so a sustained drag
   went jittery once it passed ~1/3 of the viewport. Now, once a pan crosses
   `…_PAN_REFRESH` of the active image's margin (while it still fully covers
   the view), the renderer records a **recentred-ahead** image at the *same*
   resolution with the larger `…_PAN_MARGIN` (512 px) on a background thread,
   blitting the existing (translated) image until it publishes, then swapping
   in the crisp one. Leading the record into the direction of travel means one
   background record covers roughly a full viewport of continued pan, so a
   sustained drag stays smooth with no render-thread stall. A fast flick that
   briefly outruns the look-ahead blits the nearest same-resolution image
   translated (a transient uncovered leading strip over the basemap) rather
   than freezing.
4. **Async record + repaint** — every off-thread record uses a dedicated
   `RenderService` (CPU-backed raster, safe to blit on the render thread) and,
   on publish, requests a single repaint via
   `S100VectorSnapshotRenderer.RequestRedraw` (the viewer marshals a
   `RefreshGraphics()` onto the UI thread) so the crisp image replaces the
   stale/translated blit.

Pan re-records are at the *same* resolution (scale 1), so once a pan settles
the displayed image is an exact, in-margin, scale-1 blit — pixel-identical to a
live render. Disable with `S100_VECTOR_SNAPSHOT_PREBUILD=0` for A/B against the
single-image snapshot (one image, synchronous re-record on zoom and pan).
Rotated viewports fall back to live per-feature drawing.

**Measured sustained-pan A/B (PDB01, `101AU005PDB01`).** The viewer was
driven over its embedded MCP server (`set_viewport` pan sweep +
`await_render_idle` + `get_render_stats`) with `S100_VECTOR_SNAPSHOT_DIAG=1`,
panning 28 steps (~3–4 viewport widths) at five zoom levels in a 1400×1000
window (retina scale 2, warm portrayal-instruction cache so absolute records
sit well below the cold 250–650 ms — the periodic hitch pattern is the
point). Per-paint frame duration (ms), pre-build off vs default-on:

| zoom | res m/px | OFF — pan-time records | OFF p95 / max | ON — pan-time records | ON p95 / max |
|---|---|---|---|---|---|
| 11.14 | 69.4 | 8 synchronous RECORD | 35.8 / 36.5 | 0 (off-thread PAN-PUBLISH) | 9.1 / 9.6 |
| 12 | 38.2 | 8 synchronous RECORD | 104.4 / 105.6 | 0 (off-thread PAN-PUBLISH) | 10.5 / 11.9 |
| 13 | 19.1 | 8 synchronous RECORD | 8.1 / 10.7 | 0 (off-thread PAN-PUBLISH) | 11.6 / 12.0 |
| 14 | 9.55 | 8 synchronous RECORD | 12.0 / 264.4 | 0 (off-thread) | 9.7 / 11.5 |
| 15 | 4.78 | 8 synchronous RECORD | 219.5 / 225.8 | 0 (off-thread PAN-PUBLISH) | 11.1 / 12.4 |

Pre-build off fires a synchronous render-thread record on every margin
crossing (8 per sweep at every zoom) with worst-case frames of 105–264 ms at
the zoom levels users actually navigate. Default-on fires zero pan-time
records (only off-thread refresh/publish, plus one cold first-ever record)
and holds p95 ≤ 11.6 ms / max ≤ 12.4 ms across the whole zoom range and the
whole sweep, with settled output staying pixel-identical.

The tolerance is also a constructor parameter
(`new CachedVectorStyleRenderer(inner, capacity, simplifyTolerancePx)`),
and `CachedPathCount` exposes the number of distinct cached paths for
testing the build-once-per-(feature, zoom) behaviour.

### Async scene rasteriser (`S100VectorSceneRenderer`, render-subsystem "B")

`S100VectorSceneRenderer` is the **TiledScene** render subsystem's first arm
(see `docs/design/S100-Render-Subsystem-Design.md`, Appendix B). Like the
snapshot renderer it is a Mapsui *custom layer renderer*, but instead of
recording the live Mapsui features it rasterises the backend-agnostic
`VectorScene` IR directly with `SkiaDisplayListRenderer` on a **worker
thread**, then swap-and-blits the finished `SKImage` on the UI thread. The
whole viewport plus an over-render margin (`S100_VECTOR_SCENE_MARGIN`, default
256 DIP) is rendered at device scale; pans within that margin are a pure
translated re-blit (`ComputeTranslate`), so no rasterisation work touches the
UI/render thread during a gesture.

Activate it by selecting the subsystem (`S100_RENDER_SUBSYSTEM=tiledscene`, the
`TiledScene` value of `RenderingOptimizations.RenderSubsystem`, or
**Settings → Render subsystem → Subsystem** in the viewer).
`MapsuiDisplayListRenderer` then tags the vector layer with
`S100VectorSceneRenderer.RendererName` and binds a *pattern-complete* scene
(`BindScene`) — the Mapsui lowering omits patterns, so the B arm builds its own
scene with the `PatternResolver` set and renders fills from the IR. The worker
is latest-wins coalesced (a superseded request is dropped, never published) and
honours scale-visibility (`ScaleDenominatorFor` derives the S-100 denominator
from the EPSG:3857 resolution, the inverse of `DenominatorToResolution`) so the
same SCAMIN detail shows/hides as the live frame. Rotated viewports draw
nothing (north-up only in v1). On publish it calls `RequestRedraw` (the viewer
marshals `RefreshGraphics()` onto the UI thread). Two telemetry histograms,
`SceneRasterizeDuration` (worker) and `SceneCompositeDuration` (UI blit),
attribute the two halves.

**Measured (PDB01, 18-step gesture script).** On-screen `frameDurationMs`
worst case drops from ~409 ms (Mapsui arm) to ~5 ms (B arm) because the
display-list rasterisation moves off the UI paint thread — full numbers in
Appendix B of the design doc.

### Tiled base plane (`S100VectorTileRenderer`, render-subsystem "B", Phase 2)

`S100VectorTileRenderer` generalises the single-surface arm above into a
**pyramid of cached tiles** (design doc Appendix C). It is the **default** arm of
the TiledScene subsystem; `S100_VECTOR_SCENE_MODE=single` selects the
Phase-1 single-surface renderer instead. Instead of one viewport-sized image it
partitions the world into an origin-anchored EPSG:3857 power-of-two grid
(`TileGrid`, 256-DIP tiles, XYZ convention) and rasterises each visible tile
from the `VectorScene` IR on a worker. Because the grid is anchored to the world
origin (not the viewport), a constant-zoom pan re-uses every interior tile and
only the newly-exposed perimeter rasterises — pan cost scales with *perimeter,
not area*.

**Antimeridian / continuous-longitude datasets.** The grid is world-anchored at
`[-Extent, +Extent]` (±180°), but the tile enumeration keeps a **continuous** X
frame: `TileGrid.VisibleTileRange` / `PredictedTiles` clamp only the **Y**
(latitude) index at the poles and leave the **X** (longitude) index unclamped
(an absolute guard of 4096 columns prevents runaway allocation at pathological zoom-out, but the span is otherwise unclamped so every visible column is enumerated).
An antimeridian-spanning dataset kept in a continuous frame (e.g. the US NWS
S-411 sea-ice product, ~175°E → ~225°E) therefore tiles into columns at index
`>= perAxis`, whose `TileWorldBounds` map back to the correct world-X east of
+180°. Correspondingly, `RasterizeTile` sets `EnableSeamWrap = false` on its
`SkiaDisplayListRenderer` so the headless seam-wrap does not teleport the
off-tile vertices of large continuous polygons across the world (which
previously collapsed such datasets into a thin ±180° sliver).

Each frame the UI thread snaps the live resolution to the nearest band, blits
the **best available** tile for every visible slot, each hard-clipped to its
core over a rendered **gutter** (`S100_VECTOR_TILE_GUTTER`, default 64 DIP) so
strokes stay continuous across seams and no hole is ever shown. The exact target
band is drawn on top; a backdrop of cached fallback tiles is drawn underneath
**only while the target band is incomplete**, and then only from the **single
nearest** cached band (one scale, never stacked) so transitional zoom frames do
not ghost different-sized symbols. Finished tiles enter a
thread-safe LRU `TileCache` bounded by a hard **native-byte budget**
(`S100_VECTOR_TILE_BUDGET_MB`, default sized by the performance profile — see
below) — decoded `SKImage` pixels are
native memory; visible tiles are kept most-recently-used so they are never
evicted mid-frame. A tier-sized pool of coalescing workers per layer drains the
visible-miss set (replaced every frame), and all cache access is serialised
through the layer lock so a worker cannot dispose an image the compositor is
blitting. The pool size floor is `S100_VECTOR_TILE_WORKERS` (default sized by the
performance profile — one on low-end hosts, scaling with cores on high-end), so a
cold pan's visible misses rasterise in parallel instead of one at a time; a
process-wide cap (logical-core count) stops *N* layers × *N* workers from
oversubscribing the cores and starving the UI thread on a big exchange set. That
per-layer size is a **floor, not a ceiling**: a layer with a visible cold backlog
may borrow idle global capacity toward the process-wide cap (issue #432), but only
for *visible* work — speculative prewarm never borrows, and a borrowed worker sheds
itself the moment visible work drains (returning capacity within ~one tile raster)
rather than falling through to prediction. Before lending, each other layer that
also has visible work keeps its own floor reserved, so a dense bottom-of-z-order
layer cannot starve later-painting siblings; on a `LowEnd` (single-worker) host the
elastic ceiling collapses to the floor and the behaviour is unchanged.
Telemetry histograms `TileRasterizeDuration` (worker) and `TileCompositeDuration`
(UI composite pass) attribute the two halves, while `TileColdLatency` measures the
end-to-end queue-wait-plus-rasterise a cold tile takes to appear. A rotated
viewport (e.g. an incidental
trackpad-pinch spin) is composited north-up into an off-screen surface and then
that single image is rotated about the screen centre by an angle derived from
Mapsui's own `WorldToScreenXY` projection (so the sign matches without
hardcoding); tile selection grows to the rotated viewport's bounding box
(`TileGrid.RotatedCoverSize`) so corners stay covered. Compositing north-up first
(rather than rotating the live canvas and blitting each tile under it) keeps every
clip-to-core join and the cross-band backdrop/target boundary in the clean
axis-aligned space, so a non-north-up zoom transition no longer reveals
banding/seams between tiles and bands (issue #330). See design Appendix F.8.

**Measured (PDB01, 18-step gesture script).** On-screen `frameDurationMs` stayed
bounded — p50 ≈ 7.7 ms, p90 ≈ 34 ms, max ≈ 37 ms (the worst frames are zoom-out
backdrop blits) — versus the Mapsui arm's ~409 ms; pans held ~3–8 ms with no
visible tile seams. Full numbers in Appendix C of the design doc.

#### Performance profile (machine-aware budgets)

The tile-cache budgets that previously defaulted to fixed per-layer values now
scale to the host through `MachineProfile`. The hot, GPU, and disk budgets are
seeded from a `PerformanceProfile` tier; the default `Auto` resolves a tier from
logical-core count and available RAM (`LowEnd` <=4 cores or <=8 GB; `Balanced`
<=8 cores or <=16 GB; else `HighEnd`). This bounds total memory on a constrained
VM or low-end laptop, where the old fixed 256 MB x *N* cells thrashed the cache.
`S100_PERF_PROFILE` (`Auto`/`LowEnd`/`Balanced`/`HighEnd`) pins a tier; the
individual `*_TILE_*_MB` knobs still override per-budget. The same tier sizes the
per-layer tile-worker pool (`S100_VECTOR_TILE_WORKERS`): `LowEnd` stays at the
original single worker, `Balanced` uses two, `HighEnd` scales with cores (≈ one
per four, capped at 8). The viewer surfaces the profile, detected tier, and the
worker count in Settings.

#### Constant-size symbol/sounding overlay

Base tiles carry **only** area fills, contours, and lines. Point symbols and
point-anchored soundings are split out at bind time
(`S100VectorTileRenderer.PartitionScene` routes `PointPaintOp`/`TextPaintOp` to
an overlay scene, everything else to the base scene) and drawn **live every
frame** on top of the composited tiles via
`SkiaDisplayListRenderer.RenderOnto(canvas, scene, viewport)`. This is required
for correctness, not just polish: a tile is rasterised once per resolution band
and composited scaled by `ResolutionForBand(band)/resolution`, so anything baked
into a tile scales with the band fit — point symbols and soundings would grow
through a zoom gesture then shrink as you zoomed in, instead of holding the
constant on-screen size S-100 mandates. Drawing them against the live viewport
each frame keeps their px sizes (symbol scale, fallback-dot radius, font size —
all already in logical display px) constant regardless of zoom; under rotation
the overlay is rotated about the screen centre to match the tile composite.
Because old tiles had symbols baked in, `TileDiskCache.FormatVersion` was bumped
`1 → 2` so they are never reused (which would double-draw symbols). See design
Appendix F.11.

The partitioned base/overlay scenes a layer is rasterising can be read back for
fidelity verification via `S100VectorTileRenderer.TryGetPartitionedScene(layer,
out base, out overlay)` — a pixel-free diagnostics accessor that backs the
issue #347 multi-product parity guard (`MultiProductParityTests`), which asserts
at the paint-op level that point symbols never suppress labels.

Because the overlay redraws every symbol and sounding glyph **per frame**, three
costs are kept off the hot path. First, parsed symbol pictures are cached
process-wide in `SkiaDisplayListRenderer` keyed by the resolved SVG content, so
`SKSvg.CreateFromSvg` runs once per distinct symbol rather than once per op per
frame (the set of distinct symbol SVGs is small and bounded by the symbol
catalogue × palette). Second, `RenderOnto` culls point/text ops whose projected
anchor falls outside the viewport (inflated by `PointCullMarginPx`) before
parsing a symbol or measuring a label; `DrawOverlay` passes an explicit cull
rectangle expanded to the rotated viewport's bounding box so nothing visible is
dropped under rotation. Third, text drawing pools its `SKFont` (cached by pixel
size) and `SKPaint` for the duration of a render instead of allocating a native
font/paint per label, so a dense sounding overlay no longer churns thousands of
handles per frame. None of these change what is drawn — only the work done for
glyphs that cannot be seen or that share resources.

### Prediction / pre-warm (Phase 3)

To stop a pan or zoom from transiently exposing cold tiles, the tiled renderer
speculatively rasterises tiles **before** they scroll into view (design doc
Appendix D). Each frame it estimates the viewport-centre velocity as an EMA of
inter-frame deltas (`VelocityEstimator`, EPSG:3857 m/s) and builds a **warm
set** (`TileGrid.PredictedTiles`): a 1-ring halo around the visible range, a
directional fan aimed along the velocity vector whose depth scales with speed
(0.5 s look-ahead, capped at 4 tiles), and the z±1 centre tiles so a zoom step
finds the adjacent band warm.

The warm set is a **separate low-priority queue** (`PendingPredicted`); the
single worker drains on-screen misses (`PendingVisible`) first, so prediction
never delays a tile the user is looking at. The set is recomputed — and thereby
cancelled — every frame; hysteresis comes from the velocity EMA. Speculative
hits are counted via `s100.render.tile.prediction.hits` /
`.rasterized`, and cold exposure via the `s100.render.tile.cold.exposure`
histogram. Two further cold-path histograms isolate tiling stutter on the
initial cold gesture: `s100.render.tile.cold.latency` (ms) is the
**end-to-end** age of a visible tile — first frame it is seen cold to the
worker publishing it — so it captures queue wait, not just the per-tile
`s100.render.tile.rasterize.duration`; `s100.render.tile.visible.queue.depth`
is the cold-miss burst depth a gesture creates. Read together they separate a
slow tiling worker (high cold latency / deep queue, cheap Mapsui paints) from
slow Mapsui paints (low cold latency, high map-paint duration).

A published predicted tile must **not** request a repaint
(`ShouldRequestRedraw` returns `true` only for a published *visible* tile).
A pre-warm tile is off-screen, so repainting on its arrival changes nothing
visible — but it *would* trigger a frame that re-runs prediction and
re-publishes the next speculative tile, a self-sustaining repaint loop that
never lets the map settle. The loop only bites when frames are cheap (GPU
residency, where Mapsui does not coalesce the spurious invalidations); with
the visible-only gate the pre-warmed tile simply stays resident until the
viewport moves onto it (design doc Appendix F.7).

Prediction is on by default and is a first-class A/B knob:
`S100_VECTOR_TILE_PREDICT=0` reverts to the Phase-2 visible-only behaviour.
**Measured (PDB01, 20-step pan, OFF vs ON):** frames with cold-tile exposure
fell from **58 % → 16 %** (the residual is the cold start, not the pan), at a
~32 % prediction hit-rate; the steady-pan window itself was entirely zero-cold.

### Idle cross-band pre-warm (issue #428)

Same-band prediction warms only the two z±1 *centre* tiles, so a zoom that
crosses a band boundary still pays near-full cold latency at the new band. Idle
cross-band pre-warm closes that gap: when a layer is otherwise idle the renderer
warms the whole viewport footprint of both adjacent bands
(`TileGrid.CrossBandPrewarmTiles`), so a subsequent zoom-in or zoom-out starts
warm.

It runs as a **third, lowest-priority queue** (`PendingCrossBand`), drained
strictly behind `PendingVisible` and `PendingPredicted`, so warming an adjacent
band never delays visible or same-band work. It is enqueued only on a frame with
**no cold visible misses** and only while the hot cache is below 75 % of its byte
budget, so its speculative inserts never evict the current working set (visible
target-band tiles are additionally pinned via `TileCache.Protect`). The set is
centre-first and capped at 24 tiles per frame — the band+1 footprint alone is
~4× the visible count — so the cap keeps the most-central, most-likely-next-zoom
tiles. Like same-band prediction its tiles never request a repaint and are
rebuilt (cancelled) every frame; a later zoom that reveals one scores an ordinary
prediction hit (`TileKey` carries the band).

Cross-band pre-warm is on by default (off by default on the `LowEnd` performance
tier, though an explicit opt-in via the env var or settings toggle is still
honoured) and is a first-class A/B knob: `S100_VECTOR_TILE_XBAND=0`
(`CrossBandPrewarmEnabled`) disables it, leaving the same-band warm set intact.
Its tiles flow through the existing prediction telemetry, so a zoom-transition
A/B reads time-to-fill at the new band from `s100.render.tile.cold.latency` and
the `s100.render.tile.prediction.hits` counter.

### Metatile raster jobs (issue #427)

The tiled renderer can claim pending tiles from one aligned 2×2 block and one
priority tier, rasterise their union once, then slice the result back into
ordinary tile-granular cache and disk entries. SCAMIN visibility is evaluated
at every claimed row denominator; jobs split by row when visibility differs.
Per-tile cold latency, prediction accounting, eviction, and redraw behaviour
therefore remain unchanged.

Metatiling is experimental and off by default. Enable it with
`S100_VECTOR_TILE_METATILE=1`, the viewer's **Batch adjacent tiles** setting, or
`RenderingOptimizations.TileMetatileEnabled`. Measure it with
`s100.render.metatile.rasterize.duration`, `.slice.duration`, `.tiles`, `.jobs`,
and `.fallbacks`; the fallback counter is tagged
`reason=sparse|disk|scamin|dimension|scale`. The `scale` fallback preserves the
single-tile projection when fractional device scaling cannot represent both the
core and gutter as integer pixel spans. The feature should remain off unless
real-cell A/B runs reduce aggregate tile raster duration without regressing
cold latency, paint time, memory, or pixels.

### Persistent warm disk cache + `styleStateHash` (Phase 4)

Below the in-memory hot cache sits a **persistent, on-disk warm tier**
(`TileDiskCache`, design doc Appendix E): PNG-encoded tiles that survive a layer
rebuild (a palette flip-back re-uses them) and a process restart. A tile missing
from the hot cache is decoded from disk on the worker before any re-rasterise,
and each freshly rasterised tile is persisted for future reuse.

Correctness comes from the cache **namespace**,
`SHA-256(productLayerSet | styleStateHash)` — a per-style-state subdirectory.
The `styleStateHash` (computed in `MapsuiDisplayListRenderer`) folds the palette,
symbol/text scales, and a deterministic serialization of the drawing
instructions (which already encode display category, safety contour, and every
feature/portrayal selection). A change to any of those yields a different
namespace, so **a tile is never served from disk for a different mariner/palette
state** — old tiles are orphaned and reclaimed by the byte-budget LRU sweep. The
in-memory tier is already fresh per layer (a settings change rebuilds the layer),
so this extends the no-stale-portrayal guarantee to the persistent tier.

> **Palette fingerprint (design doc Appendix F.9).** The palette is folded via
> `DescribePalette` — its `Name` plus its ordered colour entries — **not**
> `ColorPalette.ToString()`. `ColorPalette` has no `ToString()` override, so the
> earlier code collapsed every palette to one type-name string; combined with the
> palette-independent S-101 instruction list, that made the namespace
> palette-insensitive and a Night render served the previously-persisted Day
> tiles. Folding the actual palette content keeps Day/Dusk/Night (and any palette
> content change) in distinct namespaces.

The cache mirrors `DiskPortrayalInstructionCache`: atomic temp+move writes,
mtime-LRU eviction to a soft byte budget, treat-any-error-as-a-miss; PNG codec
work runs outside the lock. Knobs: `S100_VECTOR_TILE_DISK` (default on),
`S100_VECTOR_TILE_DISK_DIR` (default an OS-temp subdirectory),
`S100_VECTOR_TILE_DISK_MB` (default 512). Telemetry counters
`s100.render.tile.disk.hits` / `.writes`. **Verified (PDB01):** a Day→Night→Day
palette flip produced two separate namespaces (no cross-style sharing); 163 tiles
persisted, 198 served warm from disk on the flip-back instead of re-rasterising.

### GPU texture residency (Phase 5)

The top tier keeps already-composited tiles **resident as GPU textures** so a
steady pan/zoom does not re-upload identical pixels to the GPU every frame. A
profile of the pre-residency steady pan attributed **98 % of render-thread native
self-time to `BlitTile → SKCanvas.DrawImage`** — i.e. a per-frame raster→GPU
re-upload of unchanged tiles (design doc Appendix F). Residency replaces that with
a one-time promotion: the first time a raster tile is composited it is promoted via
`SKImage.ToTextureImage(GRContext)` into a per-layer GPU-texture cache (a second
`TileCache` instance), and every subsequent frame blits the already-resident
texture. Telemetry counters `s100.render.tile.gpu.uploads` / `.hits` track the
reuse ratio.

This is **gated to GPU-backed surfaces**: the live `GRContext` is read from
`SKCanvas.Context` and is `null` on a software/CPU surface, in which case the
renderer transparently falls back to the raster blit path. The magnitude of the
win is therefore machine-dependent — on a Metal/Apple-silicon surface the steady
pan went from ~38 ms to ~3 ms per frame with a 96–99 % GPU hit ratio — but the
*direction* (stop re-uploading identical pixels) holds on any GPU surface and the
software path is unchanged.

**Thread-confinement (critical):** GPU-backed `SKImage`s must be created *and
freed* on the thread that owns the GPU context (the render thread); freeing one on
the GC finalizer thread crashes the native Skia GPU backend. All GPU-texture
mutation funnels through `ManageGpuResidency` / `BlitTile`, which run only on the
render thread under the layer lock. To make teardown safe — a closed dataset, a
palette re-portrayal that swaps in a fresh layer, or a silently GC'd layer all
abandon a `TileState` that will never render again — every GPU-texture cache is
held by a **process-wide registry** with a *strong* reference to the cache and a
*weak* reference to its owning layer. The strong reference keeps the textures off
the finalizer thread; when the layer is collected, the next render reconciles the
registry and disposes the orphaned cache on the render thread under the live
context. Knobs: `S100_VECTOR_TILE_GPU` (default on),
`S100_VECTOR_TILE_GPU_MB` (default 256). **Verified (PDB01):** four
close-all + reopen cycles (each warming and abandoning a GPU cache) with no native
crash, frames steady at 6–9 ms and a 96 % GPU hit ratio sustained across the
cycles.

**Deferred GPU disposal + bounded backdrop (zoom-out safety):** `SKCanvas.DrawImage`
is deferred — the texture is only dereferenced when Skia flushes *after* the render
method returns — so a GPU texture must outlive the frame that drew it. Two measures
keep that invariant. First, the per-frame compositor draws the fallback backdrop
only while the target band is incomplete, and then only from the single nearest
cached band (within `MaxFallbackBandDistance` (2) bands of the target); this both
removes the multi-scale "ghosting" of symbols stacked at different sizes during a
zoom and bounds the per-frame draw count, so a full zoom-out can no longer try to
composite the entire cache at once. Second, the GPU `TileCache` is built with `deferDisposal: true`:
evicted/replaced/cleared textures are not freed inline but on the *next* frame via
`DrainPendingDisposals()` (called at the top of `Composite`, before any draw is
recorded), by which point the frame that referenced them has already flushed. The
render-thread paint block and the rasterisation worker also reset their state from a
single guarded path, so a paint-time throw drops one frame (counter
`s100.render.tile.faults`) instead of stranding the pipeline into a blank chart.
**Verified (PDB01, GPU on and off):** zoom in → zoom out to the whole world → zoom
back in renders correctly with no crash and no blank frame.

**Rotated-viewport blanking (Appendix F.8):** the tiled compositor formerly bailed
on any non-zero `viewport.Rotation`, so an incidental trackpad-pinch spin (which
rarely returns to exactly 0) blanked the chart until a dataset reload. It now
composites north-up into an off-screen surface and rotates that single image about
the screen centre by an angle derived from Mapsui's `WorldToScreenXY` (matching its
convention without hardcoding), enlarging tile selection to the rotated bounding box
(`TileGrid.RotatedCoverSize`) so corners stay covered. Compositing north-up first
also keeps the per-tile clip-to-core joins and the cross-band backdrop/target
boundary seam-free under rotation, so a non-north-up zoom transition no longer
bands (issue #330). Set
`S100_VECTOR_TILE_DIAG=1` to emit a rate-limited (~1 Hz) per-frame compositor
summary to stderr (target-band completeness, fallback bands drawn, cache/GPU
residency) plus a one-line note whenever the layer draws nothing — the diagnostic
that root-caused both this and the ghosting issue. **Verified (GB Solent exchange
set):** trackpad pinch-zoom and pinch-rotate keep the chart visible and aligned,
corners filled, single tile scale with no ghosting.

**Rotation-composite teardown (off-thread finalization, issue #332).** The rotated
frame's off-screen composite — a GPU-backed `SKSurface` and its `SKImage` snapshot —
is GPU-resident just like the tiles, so it carries the same thread-confinement rule:
it must be freed on the render thread, never the GC finalizer thread. During steady
rendering the next `Composite` frees the previous frame's pair inline (deferred-draw
safe), but when the tiled ("B") layer is torn down with a rotated frame still set —
notably **switching the render subsystem from "B" tiled to "A" Mapsui**, which
re-portrays and swaps in fresh layers — that pair was reachable only from the
weakly-held `TileState` and was finalized off-thread, racing the now-active "A" render
thread inside the native Skia GPU backend and crashing the process. The fix mirrors
each GPU-backed rotation pair into its layer's `GpuRegistryEntry` (the same
strong-referenced, render-thread-disposed registry that already shields the GPU
texture cache), in lockstep with the `TileState`, so `ReconcileGpuCaches` frees it on
the render thread when the owning layer is collected. Only the small GPU pair is
pinned — the far larger CPU tile cache stays on the weakly-held `TileState` and
remains GC-collectible. (Software/CPU rotation surfaces are safe to finalize
off-thread and are not mirrored.)

**Graceful shutdown (Appendix F.10):** the rasterisation workers call into native
Skia, so the process must not begin tearing down `libSkiaSharp` (managed-runtime
exit → C++ `__cxa_finalize`) while a worker is mid-rasterise — that dereferences
freed Skia globals and dies with a native `SIGSEGV` (seen on
`--exit-after-screenshot`, latent on any quit). `S100VectorTileRenderer.ShutdownAndDrain(timeout)`
(backed by the one-way `WorkerDrainGate`) sets a permanent draining flag and
blocks until in-flight workers finish; every worker `TryRegister`s before starting
and a refused/late worker returns before any Skia call. The viewer calls it from
`IClassicDesktopStyleApplicationLifetime.ShutdownRequested`, which Avalonia raises
on every exit path. The gate's synchronisation is unit-covered
(`WorkerDrainGateTests`).

## Installation

```sh
dotnet add package EncDotNet.S100.Renderers.Mapsui
```
