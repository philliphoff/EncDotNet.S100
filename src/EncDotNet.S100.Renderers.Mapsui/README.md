# EncDotNet.S100.Renderers.Mapsui

Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection.

## Overview

This library bridges the S-100 portrayal pipeline output to Mapsui map layers, including full CRS projection support (EPSG:3857 Web Mercator). Key types include:

- **`MapsuiCoverageRenderer`** — `ICoverageRenderer<ILayer>` implementation that renders coverage data as a georeferenced raster overlay (S-102 / S-104 / S-111).
- **`MapsuiCoverageArrowRenderer`** — renders current arrows (e.g. from S-111 data) as a georeferenced raster layer.
- **`MapsuiDisplayListRenderer`** — product-agnostic vector renderer that consumes a list of `DrawingInstruction`s plus an `IFeatureGeometryProvider` and produces a `MemoryLayer` of styled point/line/area/text features. Used by every S-100 vector product (S-101, S-124, S-129, S-421); no per-spec subclass is required.

> **CRS transforms** moved to the Mapsui-free **`EncDotNet.S100.Crs.ProjNet`**
> package (`ProjNetCrsTransformFactory`) so headless consumers can reproject
> coverage products without linking a map renderer.

`MapsuiDisplayListRenderer` lowers the display list through the **shared,
backend-agnostic vector rendering core** in
`EncDotNet.S100.Renderers.Skia.Scene` (`VectorSceneBuilder` → `VectorScene` of
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

`EncDotNet.S100.Renderers.Mapsui.DynamicSources` hosts the Mapsui-bound side of the dynamic-feature-source abstraction defined in `EncDotNet.S100.Core` (see [`docs/design/dynamic-feature-source.md`](../../docs/design/dynamic-feature-source.md)). Renderers turn `DynamicFeature` snapshots into Mapsui `IFeature` + `IStyle` instances that the viewer's `DynamicSourceOverlayHost` attaches to a `MemoryLayer` on the overlay tier of `IMapHost`.

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

Issue [#164](https://github.com/philliphoff/EncDotNet.S100/issues/164)
adds an opt-in resolution-aware Douglas-Peucker simplification path
that reduces the vertex count Skia tessellates per frame. Polylines
in the 1k–10k bucket on real S-101 datasets typically simplify by
5–10× at typical pan zooms with no visible quality regression at the
default 0.5-pixel tolerance.

### Pipeline placement

Simplification lives on `InstrumentedMemoryLayer.GetFeatures`,
because that is the only seam in the pipeline that has access to the
current zoom (`resolution`, m/px in EPSG:3857). When a layer has
simplification enabled, every visible feature is routed through a
per-layer `SimplificationCache`:

- Cache key: `(original-feature reference, half-octave bucket)`.
- Bucket: `round(log2(resolution) × 2)`. Tolerance for a bucket is
  `pixelTolerance × 2^(bucket / 2)` metres.
- Algorithm: NTS' `DouglasPeuckerSimplifier`, lines and multi-lines
  only in v1. Polygons, points, and other types pass through
  unchanged. (Polygon support is deferred until topology
  preservation + validation is wired in.)
- Eviction: on bucket transition, drop entries from buckets outside
  `[active − 1, active + 1]`. If the cache's tracked coordinate
  count still exceeds `MaxCachedCoordinates` (default 5 M ≈ 80 MB),
  the bucket farthest from the active one is dropped next, until
  under budget.
- Simplified clones share style instances by reference and copy all
  fields (including `S100.FeatureRef`); they also carry an
  `S100.OriginalFeature` back-reference. Use
  `Simplification.GetOriginal(feature)` to recover the unsimplified
  feature for picking / info-on-click.

### Wiring

```csharp
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Mapsui.Simplification;

if (layer is InstrumentedMemoryLayer iml)
{
    iml.EnableSimplification(
        DouglasPeuckerLineSimplifier.Instance,
        SimplificationOptions.Default);
}
```

In the desktop viewer this is driven by the
**Simplify line geometry (experimental)** setting, applied in
`DatasetLoaderService` before the optional rasterization wrap.

### Telemetry

| Instrument | Unit | Tags | Purpose |
|---|---|---|---|
| `s100.simplify.cache.hit.count` | count | `s100.product` | Simplified clone served from cache |
| `s100.simplify.cache.miss.count` | count | `s100.product` | DP invocation triggered |
| `s100.simplify.duration` | ms | `s100.product` | Per-feature DP cost (miss only) |
| `s100.simplify.coords.in` | count | `s100.product` | Original-geometry vertex count (miss) |
| `s100.simplify.coords.out` | count | `s100.product` | Simplified-geometry vertex count (miss) |
| `s100.simplify.cache.coords.tracked` | count | `s100.product` | Live coords in cache across all buckets |

The acceptance bar from issue #164 is steady-state hit rate ≥ 95%
and ≥ 50% reduction in `s100.map.paint.duration` mean on the
multi-S-101 workload from the perf review.

### Known limits

- v1 simplifies only line geometry; polygons (e.g. depth areas) and
  points are unaffected. The perf review shows lines dominate the
  paint cost in real datasets, so this still hits the projected
  budget.
- The miss path runs synchronously on the render thread. After a
  zoom-band transition, the first paint at the new bucket may stall
  briefly while the visible set is simplified; subsequent frames hit
  the cache. An async / pre-warm path is documented as future work
  in `docs/design/mapsui-performance.md`.
- The cache is sized by coordinate count, not entry count, so a
  handful of very dense polylines and many small features have
  comparable budget cost.

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

## Installation

```sh
dotnet add package EncDotNet.S100.Renderers.Mapsui
```
