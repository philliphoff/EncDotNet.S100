# EncDotNet.S100.Renderers.Mapsui

Rendering of S-100 data into [Mapsui](https://mapsui.com/) map layers with CRS projection.

> **Note (#600 / #601):** the `VectorScene` IR is the only base-plane path,
> rasterised by `S100VectorSceneRenderer` / `S100VectorTileRenderer` (see
> [Base-plane scene rendering](#base-plane-scene-rendering)). The retired
> Mapsui feature/style path, its caches (`CachedVectorStyleRenderer`,
> `S100VectorSnapshotRenderer`), resolution-aware line simplification and the
> line-LOD pyramid were removed, along with their documentation; see git history
> before #600 for them.

## Overview

This library bridges the S-100 portrayal pipeline output to Mapsui map layers, including full CRS projection support (EPSG:3857 Web Mercator). Key types include:

- **`MapsuiCoverageRenderer`** — `ICoverageRenderer<ILayer>` implementation that renders coverage data as a georeferenced raster overlay (S-102 / S-104 / S-111).
- **`MapsuiCoverageArrowRenderer`** — renders current arrows (e.g. from S-111 data) as one vector `PointFeature` per selected grid cell, each carrying an SVG `ImageStyle`. Subsamples dense grids both by a grid cap (`MaxArrowsPerAxis`) and a viewport-aware screen-spacing floor (`MinArrowSpacingPixels`) so arrows stay legible and per-pan draw cost stays bounded.
- **`MapsuiDisplayListRenderer`** — product-agnostic vector renderer that consumes a list of `DrawingInstruction`s plus an `IFeatureGeometryProvider` and produces a `MemoryLayer` of styled point/line/area/text features. Used by every S-100 vector product (S-101, S-124, S-129, S-421); no per-spec subclass is required.
- **`MapsuiDatasetRenderer`** — the entry point that converts a dataset processor's renderer-neutral portrayal output into a Mapsui-owned `MapsuiDatasetResult` (layers + extent). It consumes the `IVectorPortrayalSource` / `ICoveragePortrayalSource` seam exposed by `EncDotNet.S100.Datasets.Pipelines` and owns everything Mapsui-specific: the NTS pattern-clip cache, feature-type tagging, out-of-scale-band cap application, S-101 area/line `ILayer` build, the S-111 arrow renderer, and the Mapsui-typed S-98 layer-stack. This is the seed of the future multi-layer renderer in issue #213 (which will adopt `IS100DatasetRenderer<IReadOnlyList<ILayer>>`); adopting that interface later is purely additive.
- **`MapsuiDatasetLayerSession`** — the reusable dataset-layer lifecycle
  component that `IS100MapSession` composes over. It acquires processors through `DatasetProcessorOwner` leases, renders and
  atomically replaces their layers, owns S-98 cross-product ordering and
  suppression, and applies independent active/visible state, opacity,
  persistent sub-layer state, cell scale windows, and overlapping-cell
  suppression. It also registers time-aware processors, aggregates their
  samples and coverage windows, applies S-104/S-111/S-411 snap and gating
  rules, serializes render work, and cancels/coalesces superseded time and
  presentation refreshes.

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
    SceneMode = VectorSceneMode.Tiled,
};
var renderer = new MapsuiDatasetRenderer(
    crsTransformFactory,
    patternClipCache,
    options);
```

The vector-scene mode is captured by this object. Other optimization settings
will move from `RenderingOptimizations` incrementally. Omitting `options`
preserves the existing live global behavior used by the Viewer and performance
harnesses.
When `patternClipCache` is omitted, the renderer retains an in-memory
single-entry cache for its lifetime. Hosts can inject `DiskPatternClipCache`
to share entries across renderers and process restarts.

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

`MapsuiDatasetLayerSession` owns the dataset band on top of this primitive:

```csharp
using var session = new MapsuiDatasetLayerSession(
    bands,
    processorOwner,
    renderer,
    interoperabilityAuthorityProvider);
session.SetDataset(dataset, minimumDisplayScale, maximumDisplayScale);
await session.RenderAsync(dataset.Id, presentation);
session.SetCurrentTime(clock);
await session.RefreshTimeAsync(presentation);
session.SetOrder(bottomToTopDatasetIds);
session.SetMarinerSettings(presentation.Mariner);
```

The processor must already be registered with `DatasetProcessorOwner`.
Rendering holds a safe lease and replacement is transactional: cancellation,
removal, a changed processor, or S-98 projection failure leaves the previous
layers installed. Concurrent renders are latest-started-wins, so an older
render cannot replace newer output or reinstall a removed dataset.

Time-aware registration is derived from `ITimeAwareDatasetProcessor` when
`SetDataset` is called. `GetTimeSnapshot` exposes the aggregate clock, sample
list, range, and merged coverage segments. `SetCurrentTime` updates the clock
immediately so host UI can track a drag; `RefreshTimeAsync` applies a 100 ms
trailing debounce and cancels the preceding time refresh. S-104 selects the
nearest sample, S-111 additionally hides files outside their forecast window
(with one sample interval of seam tolerance), and S-411 selects the latest
snapshot at or before the clock. `RefreshAsync` performs a latest-request-wins
full presentation refresh while preserving those gates. All render entry
points share one session gate; hosts must call and await them from the
map-owning synchronization context.

The session subscribes to `IInteroperabilityAuthorityProvider`, rebuilds the
neutral cross-product stack with `LayerStackBuilder`, applies the authority's
S-98 rules using the current mariner settings, and projects through
`LayerStackProjector` inside the same dataset-band update. It retains both the
complete ruled stack (`GetLayerStackEntries`, including inactive datasets for
inspection) and the active Mapsui band (`GetStackedLayers`). Visibility, scale
windows, and overlap clips are applied to the actual projected instances.
This follows S-98 Ed.2.0.0 Main §9.2.1 and Annex A §8.4.1.

The host supplies an immutable `MapPresentationState`; the session combines it
with each leased processor and its selected time so
`MapPresentationState.CreateRenderContext` owns product-context construction.
Notifications and zoom policy remain host responsibilities.

## `AddS100` extension

`Map.AddS100(options)` composes the pieces above in one call and returns a
disposable `IS100MapSession` that owns them — a host no longer wires layer bands,
processor ownership, the renderer, the session, and the navigator by hand or knows
the renderer registration order. All collaborators are supplied on
`S100MapsuiOptions`: the CRS transform factory, an optional pre-built renderer or
shared processor owner (for a DI host), the pipeline factory, and so on.

```csharp
using var s100 = map.AddS100(new S100MapsuiOptions
{
    CrsTransformFactory = new ProjNetCrsTransformFactory(),  // host supplies the CRS
    DatasetPipelineFactory = pipelineFactory,   // enables loading from a path
});

// Load from a path (detect spec, build, render) — returns the dataset id.
var id = await s100.Datasets.LoadAsync("cell.000");
// ...or add a pre-built processor instead:
// await s100.AddDatasetAsync(mapDataset, processor);

await s100.SetPresentationAsync(presentation);
await s100.SetTimeAsync(time);
s100.ZoomToDataset(id);
```

`AddS100` calls `S100MapsuiRendering.Register()` (idempotent) and builds a
`MapsuiLayerBands`, `DatasetProcessorOwner`, `MapsuiDatasetRenderer`,
`MapsuiDatasetLayerSession`, and `MapsuiMapNavigator` (borrowing any of the collaborators
a DI host supplies on the options — see below). Ownership lives only on the
returned instance — never in a static table or `Map.Tag`. `Dispose` always
disposes the session; it disposes the `DatasetProcessorOwner` (and, through it,
every processor the owner holds) **only when `AddS100` created the owner** — an
injected owner is borrowed and left to its caller's lifetime. Normal pan / zoom /
rotation stay with `Map.Navigator`; `ZoomToDataset` is an optional convenience.

A host attaches its own decoration layers through `s100.Layers` — an
`IS100MapLayerHost` exposing the basemap, overlay, and tool bands
(`SetBasemapLayer`, `AddOverlayLayer`/`RemoveOverlayLayer`,
`AddToolLayer`/`RemoveToolLayer`). These keep their z-order relative to the
dataset layers as datasets come and go. The **dataset** band is intentionally
not on this surface: the session owns and drives it through `AddDatasetAsync`,
`RemoveDataset`, and `SetOrder`.

Every collaborator is supplied on `S100MapsuiOptions`. The reusable assembly
ships no CRS implementation, so `CrsTransformFactory` is required (the coverage /
arrow renderers need it) unless a prebuilt `DatasetRenderer` — which already
carries one — is supplied instead; a host uses `ProjNetCrsTransformFactory` from
`EncDotNet.S100.Crs.ProjNet` or its own. The options also carry
render-subsystem/scene configuration, an optional S-98 authority provider and
pattern-clip cache, an optional shared `ProcessorOwner` (a DI host shares one
across services; the session disposes only an owner it created), and the
`DatasetPipelineFactory` used by `Datasets.LoadAsync`.

**Redraw.** The background cached / scene / tile renderers rasterise off-thread;
when a settled image publishes they request a repaint through a per-session
redraw sink the session stamps onto each dataset layer
(`InstrumentedMemoryLayer.RequestRedraw`) — replacing the former process-global
static hooks. The default sink invalidates the attached map
(`Map.RefreshGraphics()`, which every Mapsui control repaints from), so a
headless host needs nothing. A UI host whose control must be invalidated on its
dispatcher thread supplies `S100MapsuiOptions.RedrawMarshal` (an `Action<Action>`
posting to the UI thread); on Avalonia, `mapControl.AddS100(...)` in
`EncDotNet.S100.Renderers.Mapsui.Avalonia` wires that marshal (and attaches the
map adapter) for you.

`s100.Datasets.LoadAsync(path)` detects the product spec, builds a processor
with the host-supplied `DatasetPipelineFactory` (an ENC `.000` base cell also
picks up sibling `.001`/`.002` updates), constructs a renderer-neutral
`MapDataset`, and registers + renders it — returning the dataset id. It covers a
**single standalone file / cell**; exchange-set folder/ZIP loading is a later
addition. Hosts that only add pre-built processors via `AddDatasetAsync` need no
factory. Load *policy* (duplicate-cell suppression, per-product default
visibility, catalogue prompts, notifications) stays with the host — it is UX,
not part of the reusable load.

### Dependency injection (optional)

Using Microsoft DI is optional — the calls above compose a session by hand. For
DI hosts, `services.AddS100Mapsui()` registers an `IS100MapSessionFactory` that
builds a session per `Map` from container-resolved dependencies:

```csharp
services.AddSingleton<ICrsTransformFactory>(new ProjNetCrsTransformFactory());
services.AddS100Mapsui(_ => new S100MapsuiOptions { DatasetPipelineFactory = factory });
// later, once a Map exists:
using var s100 = provider.GetRequiredService<IS100MapSessionFactory>().Create(map);
```

`Create` resolves the (host-registered) `ICrsTransformFactory` and an optional
`S100MapsuiOptions` and calls `AddS100`. The reusable assembly ships no CRS
implementation, so the host must register one. The returned session is owned by
the caller — dispose it when the map/window goes away; the container does not
own it.

### Picking

`session.Query.PickAsync(...)` answers a geographic pick without a UI control:

```csharp
var picks = await session.Query.PickAsync(
    new GeographicPickQuery { Latitude = 50.75, Longitude = -1.45 });
// picks[0] is the topmost feature/coverage sample at that point
```

It hit-tests each currently-shown dataset (active, visible, and rendered/in-time)
via `IDatasetProcessor.HitTestFeatures`, resolves each hit to full `FeatureInfo`
(`GetFeatureInfoAt`), and — for coverage datasets with no vector hit — samples
`GetCoverageInfo` at the session's current time. Results are ranked **topmost
first by the S-98 paint stack**, then within a dataset by geometry specificity
(point → curve → area) and distance. The query is purely geographic:
screen→world conversion and pointer gestures stay in UI-framework interaction
adapters. `RadiusMeters` (default 50 m) sets the point/curve tolerance;
`MaxResults` caps the topmost picks.

A vector `S100Pick` also carries the feature's renderer-neutral `Geometry`
(`S100FeatureGeometry` — rings / curves / points in WGS-84) when the owning
processor exposes it via `GetFeatureGeometryAt`, so a host can outline or
highlight the hit without reaching into a product's feature model. It is `null`
for a coverage pick, or when the processor does not expose feature geometry.

#### Highlighting a pick

`S100PickHighlightLayer` is an optional, reusable Mapsui overlay that draws that
geometry — the drawing complement to `PickAsync`, independent of any view model,
catalogue, application palette, or Avalonia. Add its `Layer` to `Map.Layers`
once, then call `Show` as picks change:

```csharp
var highlight = new S100PickHighlightLayer();       // optional: S100PickHighlightStyle
map.Layers.Add(highlight.Layer);

var picks = await session.Query.PickAsync(query);
highlight.Show(picks.FirstOrDefault());             // outline the topmost hit
// highlight.Show(picks);                            // or outline every hit
// highlight.Clear();                                // remove the highlight
```

For each geometry it draws, in feature-space (so the outline scales with zoom and
stays anchored as the map pans): a faint fill plus accent outline for an area's
exterior ring and holes, an accent stroke per curve (split at the antimeridian),
and an accent ring per point. A `null` pick or a coverage pick (no geometry)
clears the layer. `S100PickHighlightStyle` tunes the accent colour and
stroke/fill weights; the default matches the Viewer's look. The overlay draws
only the feature outline — the *what*. A cursor-echo marker at the click point
and chart-palette dimming are host UX and stay in the application (the Viewer
keeps its own richer overlay for those).

Supply the optional `Resolution` (metres/pixel, the unit of Mapsui's
`Navigator.Viewport.Resolution`) to match what is actually painted at the current
zoom: a dataset whose whole-cell scale window has scaled it out — the same
catalogue-driven `ApplyCellScaleWindow` cutoff that drops a finer cell once you
zoom past its smallest-scale edge — is excluded. A UI adapter reads its map's
current resolution and passes it through; the query itself stays
viewport-agnostic. Omit it (the default) to skip scale filtering. Per-feature
scale limits *within* a still-drawn cell are not applied here.

The session reports its lifecycle through structured events rather than
notifications or localized strings, so a non-Viewer host can drive its own UI
(these are re-exposed on `IS100MapSession`):

- `DatasetRenderStarted` / `DatasetRenderCompleted` (`MapSessionDatasetRenderEventArgs`)
  mark each dataset render for both `RenderAsync` and every dataset of a
  coalesced refresh. The `Kind` (`MapSessionRenderKind`: `Render`, `TimeRefresh`,
  `PresentationRefresh`) says what triggered it. `Started` fires only once a
  processor lease is held (a dataset removed before that raises nothing).
  `Completed` fires only on a successful render that installs layers; a render
  that throws, is cancelled, or is superseded/removed *after* it begins raises
  `Started` without `Completed` (a swallowed refresh failure instead raises
  `DatasetRenderFailed`).
- `DatasetRenderFailed` (`MapSessionDatasetRenderFailedEventArgs`) reports a
  per-dataset failure the session **swallowed** during a coalesced refresh so the
  other datasets keep rendering. A single `RenderAsync` surfaces its error by
  **throwing** to the awaiting caller instead, so no failed event is raised there.
- `LayersChanged` / `TimeRangeChanged` (`EventHandler`) and `CurrentTimeChanged`
  (`MapSessionCurrentTimeEventArgs`) report projected-band and clock changes.

## Dataset extent indicators

`S100DatasetExtentIndicatorLayer` is an optional, reusable Mapsui overlay that
outlines the extents of loaded datasets which have zoomed out of scale — so a
mariner framing a wide-spread exchange set still sees where the member datasets
are and has a target to zoom toward. Like the pick-highlight layer it depends
only on Mapsui, not on the session, a catalogue, an application palette, a view
model, or Avalonia. Add its `Layer` once, then call `Show` as datasets,
visibility, or zoom-cutoffs change:

```csharp
var extents = new S100DatasetExtentIndicatorLayer();   // optional: S100DatasetExtentIndicatorStyle
map.Layers.Add(extents.Layer);

// One indicator per dataset that has both a captured mercator extent and a
// whole-cell zoom-out cutoff — a dataset that never drops out needs no hint.
// MapsuiMapDatasetSnapshot.Extent (EPSG:3857, Mapsui map units) and
// .ContentMaxVisibleResolution (metres/pixel) are both nullable, hence the
// filter. The cutoff becomes each border's MinVisible, so it appears exactly
// when the dataset's own content drops out; pass 0 to always show it (e.g. an
// unloaded catalogue footprint).
extents.Show(session.GetDatasets()
    .Where(d => d.Extent is not null && d.ContentMaxVisibleResolution is not null)
    .Select(d => new S100DatasetExtentIndicator(d.Extent!, d.ContentMaxVisibleResolution!.Value)));
// extents.Show(indicators, accent);   // re-theme without rebuilding the layer
// extents.Clear();                     // remove all indicators
```

Each indicator becomes one thin, dashed, unfilled accent rectangle whose border
style is gated by `MinVisible` = the dataset's content cutoff, so Mapsui reveals
it precisely when the viewport zooms out past the point where the dataset stops
drawing and hides it again on zoom-in — the overlay is viewport-agnostic and
needs no navigator subscription. `S100DatasetExtentIndicatorStyle` tunes the
accent, stroke weight, opacity, and dash; the default matches the Viewer's look.

The layer takes already-projected mercator rectangles: a host that captured
extents in mercator (Mapsui's native units) passes them straight through, while a
host holding geographic bounds projects them itself and splits any
antimeridian-crossing footprint into two non-wrapping boxes — which projection,
and how to treat wide footprints, is host policy. Deciding *which* datasets
qualify (loaded-and-visible, out-of-scale, catalogue footprints for deferred
cells) and the on/off toggle are likewise host policy; the Viewer keeps that in
its own controller and drives this layer.

## Overscale curtain

`S100OverscaleCurtainLayer` is an optional, reusable Mapsui overlay that paints
the S-52 / S-101 overscale "curtain" (`AP(OVERSC01)` Form A) — a subtle pattern
of evenly spaced vertical lines — over the regions of loaded cells being
displayed beyond their compilation scale. Like the pick-highlight and
dataset-extent-indicator layers it depends only on Mapsui, not on the session, a
catalogue, an application palette, a view model, or Avalonia. Add its `Layer`
once, then call `Show` with the regions computed for the current zoom:

```csharp
var curtain = new S100OverscaleCurtainLayer();   // optional: OverscaleCurtainStyle
map.Layers.Add(curtain.Layer);

// Region geometry depends only on the loaded cells and the viewport resolution
// (metres/pixel) — never on pan or rotation — so recompute it only when the zoom
// or the set of loaded cells changes.
var regions = OverscaleCurtain.ComputeRegions(overscaleCells, viewportResolution);
curtain.Show(regions);
// curtain.Clear();   // remove the curtain
```

`OverscaleCurtain.ComputeRegions` works in world (EPSG:3857) coordinates: it
returns one region per overscaled cell, that cell's coverage with every
strictly-finer overlapping cell subtracted so a finer cell's in-scale footprint
stays curtain-free (S-52: the curtain marks only genuinely overscaled area). The
layer fills each region with a shared `OverscaleCurtainStyle`, whose renderer
draws world-anchored vertical strokes clipped to the region per frame — so the
pattern stays crisp at any zoom and on HiDPI surfaces and moves with the chart
during panning without any per-frame rebuild here. `OverscaleCurtainStyle` tunes
the line spacing, width, and colour; the default matches the Viewer's look.

Deciding *which* cells qualify (loaded, drawing, scale-bearing) and honouring the
mariner's on/off toggle are host policy; the Viewer keeps that in its own
controller — caching the last resolution, recomputing only on a zoom or
dataset-set change — and drives this layer.

## Validation findings

`S100ValidationFindingLayer` is an optional, reusable Mapsui overlay that plots a
dataset's spatially-located validation findings: a severity-coloured marker for a
point finding and a translucent severity-coloured box for a bounding-box finding.
Like the other reusable overlays it depends only on Mapsui and the
renderer-neutral `ValidationSeverity` / `GeoPosition` / `BoundingBox` primitives —
not on the session, a catalogue, an application palette, a view model, or
Avalonia. Add its `Layer` once, then call `Show` with the findings to plot:

```csharp
var findings = new S100ValidationFindingLayer();   // optional: S100ValidationFindingStyle
map.Layers.Add(findings.Layer);

// One S100ValidationFinding per finding that carries a location. A finding may
// carry a Point, a BoundingBox, both (two features), or neither (skipped).
findings.Show(report.Findings
    .Where(f => f.Point is not null || f.BoundingBox is not null)
    .Select(f => new S100ValidationFinding(f.Severity, f.Point, f.BoundingBox)));
// findings.Clear();   // remove the overlay contents
```

Findings are projected from WGS-84 to EPSG:3857 (Mapsui's native map units)
internally, so a host passes geographic locations straight through. Each update
replaces the overlay wholesale — finding counts are small.
`S100ValidationFindingStyle` tunes the per-severity accent colours, the point
marker/halo, and the bounding-box outline and fill alpha; the default matches the
Viewer's validation-badge palette (red error, amber warning, blue info).

Deciding *which* findings to plot and *when* to rebuild is host policy; the Viewer
keeps that in a small selection-driven service that shows the findings of the
currently-selected dataset and drives this layer.

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

## Optional Avalonia adapter

Avalonia hosts can add
[`EncDotNet.S100.Renderers.Mapsui.Avalonia`](../EncDotNet.S100.Renderers.Mapsui.Avalonia/README.md)
without coupling this base package to a UI framework. Its
`AvaloniaMapsuiMapAdapter` attaches explicitly to a
`CaptureSynchronizedMapControl` and owns UI-thread redraw, control-state
coordinate conversion, current-view snapshots, and framework control capture.
Disposal detaches the adapter without disposing the borrowed control or map.

The optional package composes with `MapsuiLayerBands` and
`MapsuiMapNavigator`; it does not own processors, dataset layers, S-98
composition, presentation state, or automatic navigation policy.

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
- **Cell-wide zoom-out window from the exchange-set catalogue (`ApplyCellScaleWindow`).** Independent of the in-file per-feature cap above, `MapsuiDatasetRenderer.ApplyCellScaleWindow(layers, minimumDisplayScale)` clamps every layer's `MaxVisible` to `DenominatorToResolution(minimumDisplayScale, φ)` at the layer's extent-centre latitude, where `minimumDisplayScale` is the *coarsest permitted* denominator resolved from the cell's `CATALOG.XML` `DataCoverage` entries (max of the per-coverage `minimumDisplayScale` values). It only ever **tightens** an existing `MaxVisible`. Unlike the M_COVR-derived per-feature cap (which applies to the linework sub-layer only), this window suppresses the **whole cell — area fills included** — once you zoom out past the cell's smallest-scale edge, so a finer cell drops out entirely and the coarser cell nested beneath it shows through. This is *hole-safe*: as you zoom out, finer cells (smaller `minimumDisplayScale`) drop first, always leaving a coarser cell underneath (issue #438, Phase 1). `MapsuiDatasetLayerSession` gates the window on `IgnoreScaleMinimum`, prefers the host's catalogue scale, and falls back to `MapsuiDatasetResult.CellMinimumDisplayScale` for standalone cells (for S-57, the larger of CSCL and the cell's largest `SCAMIN`).
- **Cross-cell coverage clip / "larger-scale-in" overlap suppression (`OverlapSuppression` + `CoverageClip`, issue #438 Phase 2).** The zoom-*in* seam Phase 1 deferred: where a finer, overlapping in-band cell provides coverage, the coarser cell must stop contributing (no depth-area / fill bleed under the harbour cell). This is done as a **true geometry clip** in screen space, not a scale cap, and it is **zoom-aware** — a finer cell only suppresses a coarser cell while the finer cell is itself visible at the current resolution, so zooming out (which drops the finer cell via the Phase 1 window) never leaves a blank hole in the coarser cell. `MapsuiDatasetLayerSession` recomputes the cells after render, replacement, removal, ordinary reorder, visibility, opacity, active-state, and sub-layer changes. It applies clips after host projection so S-98-filtered or rebuilt layers receive the same ordinary overlap behavior. Hidden, transparent, inactive, or lazily unloaded cells never suppress coarser content. Cells are ranked by `MapsuiDatasetResult.CellCompilationScale` when set (S-57 CSCL), else by the whole-cell window; a finer cell's suppression cutoff always follows its whole-cell window (`OverlapSuppressionCell.CutoffScaleDenominator`). The same ranking value feeds `SubLayerStackItem.SourceScaleDenominator` for paint order.

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

`EncDotNet.S100.Renderers.Mapsui.DynamicSources` hosts the Mapsui-bound side of the dynamic-feature-source abstraction defined in `EncDotNet.S100.Core` (see [`docs/design/dynamic-feature-source.md`](../../docs/design/dynamic-feature-source.md)). Renderers turn `DynamicFeature` snapshots into Mapsui `IFeature` + `IStyle` instances that the reusable `S100DynamicSourceHost` attaches to a `MemoryLayer` on the overlay band.

- **`S100DynamicSourceHost`** — the reusable hosting lifecycle: it registers `IDynamicFeatureSource` instances as managed overlay layers, resolves each source's renderer, subscribes to `Changed`, coalesces high-frequency rebuilds, and offers geographic `HitTest`ing. It implements `IS100DynamicSourceRegistry` (registration set, per-source visibility, hit-testing) and depends only on Mapsui — not on Avalonia or a DI container:
  - **Overlay target** is an `IMapsuiOverlayLayerHost` (implemented by `MapsuiLayerBands`), so the host attaches layers without knowing the concrete map adapter.
  - **UI-thread marshalling** is an injectable `Action<Action>` (default: inline/synchronous). A UI host passes a dispatcher-backed marshal.
  - **Renderer resolution** is an injectable `Func<string?, IDynamicFeatureRenderer?>` (default: always the fallback renderer). A DI host passes a resolver over its keyed services.

  A reusable session exposes an owned instance via `IS100MapSession.DynamicSources`; a UI host can also construct one directly over its own layer-band adapter.
- **`IDynamicFeatureRenderer`** — `CanRender` + `Render` contract. Implementations are stateless functions of one feature; the host owns the layer-level state and UI-thread marshalling.
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

  When composed through `AddS100Mapsui`, the session's `DynamicFeatureRendererResolver` defaults to `IServiceProvider.GetKeyedService<IDynamicFeatureRenderer>(source.Metadata.RendererKey)`, so keyed registrations resolve automatically.

## Performance instrumentation

The renderer ships with optional OpenTelemetry instrumentation that
attributes paint cost down to the style-renderer, layer, source feature
class, and geometry vertex count. All instruments are sub-millisecond
per paint when no OTel listener is attached, so they are safe to leave
in production builds.

| Instrument | Unit | Tags | Purpose |
|---|---|---|---|
| `s100.map.paint.duration` | ms | — | Compositor-thread paint wall-time per frame |
| `s100.map.paint.interval` | ms | — | Time between paints (idle gaps > 500 ms dropped) |
| `s100.map.paint.style.calls` | count | `style`, `layer`, `points`, `featureClass` | Style-renderer `Draw` calls per paint |
| `s100.map.paint.style.duration` | ms | `style`, `layer`, `points`, `featureClass` | Cumulative `Draw` duration per paint |

The `points` tag is bucketed (`n/a`, `0`, `1-9`, `10-99`, `100-999`,
`1k-10k`, `10k-100k`, `100k+`) to keep histogram cardinality bounded
while still revealing whether a layer's cost is driven by many cheap draws
or a few expensive ones. `featureClass` is the source Feature Catalogue type
carried by S-101/S-57 features (for example, `DepthContour`); generated
features and products that do not attach a source type use `(unclassified)`.

To capture a measurement session, run the viewer with the OTel console
exporter enabled:

```sh
ENC_DOTNET_OTEL_CONSOLE=1 OTEL_METRIC_EXPORT_INTERVAL=2000 \
  dotnet run -c Release --project src/EncDotNet.S100.Viewer
```

Histograms are emitted every 2 s with cumulative counts and per-bucket
distributions. Aggregate by `(layer, featureClass, points)` to identify which
geometries are dominating paint time — empirically, ~93% of paint cost
on real-world S-101 datasets is spent on geometries with ≥100 vertices,
with per-vertex cost ~1 µs. See
[`docs/design/mapsui-performance.md`](../../docs/design/mapsui-performance.md)
for the full investigation and optimization plan.

## Pattern-fill clip generalization

When it builds the scene, `MapsuiDisplayListRenderer` generalizes the polygon
geometry used when
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
1 m (EPSG:3857) tolerance (`PatternPriorityClipper.SimplifyToleranceMetres`,
in `EncDotNet.S100.Rendering.Scene`).
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
  (`PatternPriorityClipper.SimplifyToleranceMetres`,
  `PatternPriorityClipper.MinPointsToSimplify`), CRS,
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

## Base-plane scene rendering

### Async scene rasteriser (`S100VectorSceneRenderer`, render-subsystem "B")

`S100VectorSceneRenderer` is the **TiledScene** render subsystem's first arm
(see `docs/design/S100-Render-Subsystem-Design.md`, Appendix B). Like the
retired raster snapshot it is a Mapsui *custom layer renderer*, but instead of
recording the live Mapsui features it rasterises the backend-agnostic
`VectorScene` IR directly with `SkiaDisplayListRenderer` on a **worker
thread**, then swap-and-blits the finished `SKImage` on the UI thread. The
whole viewport plus an over-render margin (`S100_VECTOR_SCENE_MARGIN`, default
256 DIP) is rendered at device scale; pans within that margin are a pure
translated re-blit (`ComputeTranslate`), so no rasterisation work touches the
UI/render thread during a gesture.

Since #600 the scene path is the only base-plane path. This single-surface arm
is selected with `S100_VECTOR_SCENE_MODE=single`,
`RenderingOptimizations.SceneMode = VectorSceneMode.Single`, or **Settings →
Base-plane rendering → Scene mode → Single surface** in the viewer; the tiled
base plane below is the default.
`MapsuiDisplayListRenderer` then tags the vector layer with
`S100VectorSceneRenderer.RendererName` and binds the scene (`BindScene`), built
with the `PatternResolver` set so fills render from the IR. The layer's Mapsui
features are lowered from the same scene and only carry pick identity; pattern
fills get a near-invisible geometry-only pick target (issue #604). The worker
is latest-wins coalesced (a superseded request is dropped, never published) and
honours scale-visibility (`ScaleDenominatorFor` derives the S-100 denominator
from the EPSG:3857 resolution, the inverse of `DenominatorToResolution`) so the
same SCAMIN detail shows/hides as the live frame. Rotated viewports draw
nothing (north-up only in v1). On publish it requests a repaint through the
layer's per-session redraw sink (which invalidates the attached map). Two
telemetry histograms,
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
and visible raster results are published immediately before being offered to a
bounded, process-wide write-behind queue. Prediction results are persisted only
after becoming visible. The dedicated low-priority writer snapshots, encodes,
and atomically stores accepted tiles; duplicate or overflow work is discarded
rather than blocking a render worker.

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
mtime-LRU eviction to a soft byte budget, treat-any-error-as-a-miss. Concurrent
reads do not serialize behind persistence; one background writer owns PNG
encoding, final-path mutation, and budget sweeps. Knobs:
`S100_VECTOR_TILE_DISK` (default on),
`S100_VECTOR_TILE_DISK_DIR` (default an OS-temp subdirectory),
`S100_VECTOR_TILE_DISK_MB` (default 512). Telemetry counters
`s100.render.tile.disk.hits` / `.writes`, plus
`s100.render.tile.disk.write_queue.depth` / `.discarded`. **Verified (PDB01):**
a Day→Night→Day palette flip produced two separate namespaces (no cross-style
sharing); 163 tiles persisted, 198 served warm from disk on the flip-back
instead of re-rasterising. The bounded queue drains during normal process exit;
an abrupt process termination may discard outstanding best-effort writes.

The write-behind path was also measured with a 360-step, 100 ms paced navigation
route over 16 overlapping UK S-101 cells. Moving persistence off render workers
reduced tile P95 from 781 ms to 49 ms, frame P95 from 23 ms to 12 ms, and
viewport-command P95 from 92 ms to 2.3 ms while still completing 735 background
writes. The same route with persistence disabled measured 30 ms, 8.8 ms, and
3.1 ms respectively; the remaining gap is therefore no longer dominated by
cache writes.

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
context. Knobs: `S100_VECTOR_TILE_GPU` (default off),
`S100_VECTOR_TILE_GPU_MB` (default 256). **Verified (PDB01):** four
close-all + reopen cycles (each warming and abandoning a GPU cache) with no native
crash, frames steady at 6–9 ms and a 96 % GPU hit ratio sustained across the
cycles.

GPU residency remains available as an opt-in for stable, repeatedly drawn views,
but is disabled by default. A paced pan/zoom route over 13 UK S-101 cells showed
that eager `ToTextureImage` promotion and texture churn can monopolize the
compositor thread: GPU residency produced a 3,155 ms maximum frame and 591 ms
P95, versus 123–160 ms maximum and 75–79 ms P95 without explicit residency.

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
freed Skia globals and dies with a native `SIGSEGV` (first seen on a fast
headless quit, latent on any quit). `S100VectorTileRenderer.ShutdownAndDrain(timeout)`
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
