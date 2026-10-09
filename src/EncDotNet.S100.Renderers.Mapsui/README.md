# EncDotNet.S100.Renderers.Mapsui

`EncDotNet.S100.Renderers.Mapsui` draws S-100 datasets as layers on a
[Mapsui](https://mapsui.com/) map and manages them as a map session: loading,
paint order, S-98 interoperability, time, presentation and picking. Reference it
when you show S-100 data on an interactive Mapsui map. The package doesn't
depend on a UI framework; for Avalonia, add
[`EncDotNet.S100.Renderers.Mapsui.Avalonia`](../EncDotNet.S100.Renderers.Mapsui.Avalonia/README.md).

The package targets `net8.0` and `net10.0`. It draws vector data through the
scene IR in [`EncDotNet.S100.Rendering.Scene`](../EncDotNet.S100.Rendering.Scene/README.md)
and [`EncDotNet.S100.Renderers.Skia`](../EncDotNet.S100.Renderers.Skia/README.md).
It doesn't include a CRS implementation: supply one, such as
`ProjNetCrsTransformFactory` from `EncDotNet.S100.Crs.ProjNet`.

## Install

```bash
dotnet add package EncDotNet.S100.Renderers.Mapsui
dotnet add package EncDotNet.S100.Crs.ProjNet
dotnet add package EncDotNet.S100
```

`EncDotNet.S100` supplies `BundledDatasetProcessorFactory`, which lets the
session load datasets from a path. If you build dataset processors yourself, you
don't need it.

## Add S-100 data to a map

`map.AddS100(options)` attaches an S-100 session to a `Mapsui.Map` and returns
it as an `IS100MapSession`. This example loads one cell, frames it, picks at its
centre and disposes the session. It's based on the
[MapHost sample's](../../samples/EncDotNet.S100.Samples.MapHost/README.md)
headless smoke test.

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Projections;

using var processorFactory = BundledDatasetProcessorFactory.Create();
var map = new Map { CRS = "EPSG:3857" };

await using var session = map.AddS100(new S100MapsuiOptions
{
    CrsTransformFactory = new ProjNetCrsTransformFactory(),
    DatasetPipelineFactory = processorFactory,
});

// A live map control sets the viewport size; a headless host sets it itself.
map.Navigator.SetSize(1000, 800);

var id = await session.Datasets.LoadAsync("path/to/cell.000");
session.ZoomToDataset(id);

var extent = session.GetDataset(id)!.Extent!;
var (longitude, latitude) = SphericalMercator.ToLonLat(extent.Centroid.X, extent.Centroid.Y);
var picks = await session.Query.PickAsync(new GeographicPickQuery
{
    Latitude = latitude,
    Longitude = longitude,
    Resolution = map.Navigator.Viewport.Resolution,
});
```

`AddS100` registers the S-100 Mapsui renderers and builds the layer bands,
processor owner, dataset renderer, dataset-layer session and navigator. The
returned session owns them; nothing is stored in a static table or `Map.Tag`.
Disposing the session disposes the processor owner, and every processor it
holds, only if `AddS100` created that owner. An owner you pass in is left to you.

Pan, zoom and rotation stay with `Map.Navigator`. `ZoomToDataset` is a
convenience.

### Options

`S100MapsuiOptions` carries every collaborator the session uses:

| Property | Purpose |
|---|---|
| `CrsTransformFactory` | Projects coverage grids to EPSG:3857. Required unless you supply `DatasetRenderer`. |
| `DatasetPipelineFactory` | Builds processors for `Datasets.LoadAsync`. Not needed if you only call `AddDatasetAsync`. |
| `DatasetRenderer` | A prebuilt `MapsuiDatasetRenderer`, which already carries a CRS factory. |
| `ProcessorOwner` | A shared `DatasetProcessorOwner`, for example one owned by a DI container. The session doesn't dispose it. |
| `InteroperabilityAuthorityProvider` | The S-98 interoperability authority. |
| `PatternClipCache` | A pattern-clip cache, such as a `DiskPatternClipCache`; see [Reuse pattern-fill clips](#reuse-pattern-fill-clips). |
| `SceneMode` | `VectorSceneMode.Tiled` (default) or `VectorSceneMode.Single`. |
| `RedrawMarshal` | Runs redraw requests on your UI thread. |
| `DynamicSourceMarshal`, `DynamicFeatureRendererResolver`, `DynamicSourceCoalesceWindow` | Configure [dynamic feature sources](#show-dynamic-features). |

### Redraw on the UI thread

The session renders off the UI thread. When a finished image is ready, each
dataset layer asks for a repaint through a redraw sink that belongs to the
session. By default the sink calls `Map.RefreshGraphics()`, which every Mapsui
control repaints from, so a headless host needs nothing. If your control must be
invalidated on its dispatcher thread, set `RedrawMarshal` to an
`Action<Action>` that posts to that thread. On Avalonia,
`mapControl.AddS100(...)` sets it for you.

### Load datasets

`session.Datasets.LoadAsync(path)` detects the product, builds a processor with
`DatasetPipelineFactory`, registers the dataset and renders it. It returns the
dataset's `MapDatasetId`. It loads one standalone dataset file. An ENC base cell
(`.000`) also picks up its sequential updates (`.001`, `.002`, …). Exchange-set
folders and archives aren't supported yet.

To add a processor you built yourself, call `session.AddDatasetAsync(dataset,
processor)` with a `MapDataset` that describes it.

Load policy, such as suppressing duplicate cells, choosing which products are
visible by default, and showing notifications, is up to your application.

### Use dependency injection

Dependency injection is optional. For a Microsoft DI host,
`services.AddS100Mapsui()` registers an `IS100MapSessionFactory` that creates a
session for each `Map`:

```csharp
services.AddSingleton<ICrsTransformFactory>(new ProjNetCrsTransformFactory());
services.AddS100Mapsui(_ => new S100MapsuiOptions { DatasetPipelineFactory = factory });

// Later, once a Map exists:
using var session = provider.GetRequiredService<IS100MapSessionFactory>().Create(map);
```

`Create` resolves the `ICrsTransformFactory` you registered and any
`S100MapsuiOptions`, then calls `AddS100`. Register a CRS implementation; the
package doesn't include one. The caller owns the returned session and disposes
it when the map goes away; the container doesn't.

## Main types

- **`IS100MapSession`**: the session `AddS100` returns. It exposes `Datasets`
  (loading), `Query` (picking), `Layers` (your own layers), `DynamicSources`,
  `Navigator` and the underlying `Session`, plus dataset, presentation and time
  methods.
- **`S100MapsuiOptions`**: session configuration; see [Options](#options).
- **`S100PickHighlightLayer`**, **`S100DatasetExtentIndicatorLayer`**,
  **`S100OverscaleCurtainLayer`**, **`S100ValidationFindingLayer`**: optional
  overlays; see [Add optional overlays](#add-optional-overlays).
- **`MapsuiMapNavigator`**: viewport framing and recentring; see
  [Navigate the viewport](#navigate-the-viewport).
- **`S100DynamicSourceHost`**: draws live features such as own ship and AIS
  targets; see [Show dynamic features](#show-dynamic-features).
- **`MapsuiCoverageRenderer`**: renders S-102, S-104 and S-111 coverage data as
  a georeferenced raster layer (`ICoverageRenderer<ILayer>`).
- **`MapsuiCoverageArrowRenderer`** and **`ThinnedSymbolLayer`**: draw current
  arrows, for example from S-111, as one SVG point symbol per grid cell. Each
  frame draws only the arrows that S-98 Appendix G grid thinning
  (`SymbolThinning`) keeps at that resolution, so arrow density stays readable
  at every zoom. Regular grids use lattice thinning; station series and meshes
  use point-by-point thinning.
- **`MapsuiDisplayListRenderer`**: turns a `DrawingInstruction` display list and
  an `IFeatureGeometryProvider` into a map layer, for any S-100 vector product
  (S-101, S-124, S-129, S-421 and others).
- **Lower-level components** for hosts that compose the session themselves:
  `S100MapsuiRendering`, `MapsuiDatasetRenderer`, `MapsuiLayerBands` and
  `MapsuiDatasetLayerSession`; see
  [Compose the session yourself](#compose-the-session-yourself).

## Present datasets

The session applies your presentation, order, visibility and time to every
dataset it holds:

```csharp
await session.SetPresentationAsync(presentation); // MapPresentationState
await session.SetTimeAsync(time);
session.SetOrder(bottomToTopDatasetIds);
session.SetVisible(id, isVisible: false);
session.SetOpacity(id, 0.5);
```

- **Presentation.** `MapPresentationState` is an immutable snapshot of palette,
  symbol and text scale, ECDIS and mariner settings. The session combines it
  with each dataset's processor and selected time to build that product's render
  context.
- **Visible and active.** `SetVisible` controls whether a dataset draws.
  `SetActive` controls whether it takes part in cross-product composition and
  picking.
- **Paint order and S-98.** The session follows the S-98 interoperability
  authority you supply. It rebuilds the cross-product layer stack, applies the
  S-98 rules with the current mariner settings, and updates the dataset layers
  in one step (S-98 Ed 2.0.0 Main §9.2.1 and Annex A §8.4.1).
- **Overlapping cells.** Where a finer cell covers part of a coarser one, the
  coarser cell stops drawing there. Each cell also drops out once you zoom out
  past its catalogue scale window. See
  [Portrayal conventions](#portrayal-conventions) for the rules.

### Time

`SetTime` moves the map clock without rendering, so a timeline can follow a
drag. `SetTimeAsync` moves it and re-renders the time-aware datasets.
`GetTimeSnapshot` returns the clock, the sample times, the overall range, the
covered segments, and each timed dataset with the span it covers (`Datasets`,
each with its own `Coverage` windows and `Covers(time)`).

The clock isn't clamped to the loaded data: it can sit in a gap or past the end,
where each dataset follows its own rule. It starts at the first sample and stays
put as datasets come and go.

A dataset never shows data from outside its tolerance as if it were current:

| Product | Shows |
|---|---|
| S-111 | The nearest sample within one step (the dataset's median sample interval). |
| S-104 | The latest sample at or before the clock, for up to one step. |
| S-411 | The latest snapshot at or before the clock, for up to 14 days. |

Outside its tolerance, including in gaps within a dataset, the dataset is
hidden, and its coverage segments split around those gaps.

### Session events

The session reports what it's doing through events, not notifications or
localized strings, so you can drive your own UI:

- `DatasetRenderStarted` and `DatasetRenderCompleted`
  (`MapSessionDatasetRenderEventArgs`) bracket each dataset render. `Kind`
  (`MapSessionRenderKind`: `Render`, `TimeRefresh` or `PresentationRefresh`)
  says what triggered it. `Started` fires once the session holds the
  processor; `Completed` fires only when the render installs layers. A render
  that throws, is cancelled, or is superseded or removed after it starts raises
  `Started` without `Completed`.
- `DatasetRenderFailed` (`MapSessionDatasetRenderFailedEventArgs`) reports a
  failure the session caught during a refresh of several datasets, so the
  others keep rendering. A single dataset render (`RenderAsync`) throws to the
  caller that awaits it instead, so it raises no failed event.
- `LayersChanged`, `TimeRangeChanged` and `CurrentTimeChanged`
  (`MapSessionCurrentTimeEventArgs`) report changes to the dataset layers, the
  time range and the clock.

## Pick features at a location

`session.Query.PickAsync` finds the features and coverage samples at a
geographic point, without a UI control:

```csharp
var picks = await session.Query.PickAsync(
    new GeographicPickQuery { Latitude = 50.75, Longitude = -1.45 });
// picks[0] is the topmost feature or coverage sample at that point.
```

It hit-tests each dataset that's currently shown (active, visible, and in time),
resolves each hit to a full `FeatureInfo`, and samples coverage datasets that
have no vector hit at the session's current time. Results are ordered topmost
first by the S-98 paint order, then within a dataset by geometry (point, then
curve, then area) and distance.

- `RadiusMeters` (default 50 m) sets the tolerance for points and curves.
- `MaxResults` limits the number of picks returned.
- `Resolution`, in metres per pixel like Mapsui's
  `Navigator.Viewport.Resolution`, leaves out datasets drawn out of scale at
  that zoom: a cell is dropped once you zoom out past its catalogue scale
  window. Per-feature scale limits within a cell that's still drawn aren't
  applied. Omit it to skip scale filtering.

The query only works in geographic coordinates. Converting a pointer position
to latitude and longitude is the UI adapter's job; on Avalonia, use
`PickAtScreenAsync` in the Avalonia package.

Each vector `S100Pick` carries the feature's `Geometry` (`S100FeatureGeometry`:
rings, curves and points in WGS-84) when the processor provides it, so you can
outline the hit. It's `null` for a coverage pick or when the processor doesn't
provide geometry.

### Highlight a pick

`S100PickHighlightLayer` is an optional overlay that outlines pick geometry. Add
its `Layer` to the map once, then call `Show` as the picks change:

```csharp
var highlight = new S100PickHighlightLayer(); // Optionally pass an S100PickHighlightStyle.
map.Layers.Add(highlight.Layer);

var picks = await session.Query.PickAsync(query);
highlight.Show(picks.FirstOrDefault()); // Outline the topmost hit.
// highlight.Show(picks);              // Or outline every hit.
// highlight.Clear();                  // Remove the highlight.
```

The overlay draws in map coordinates, so the outline scales and moves with the
map. Areas get a faint fill and an accent outline on the exterior ring and
holes; curves get an accent stroke, split at the antimeridian; points get an
accent ring. A `null` pick, or a coverage pick with no geometry, clears the
layer. `S100PickHighlightStyle` sets the accent colour and the stroke and fill
weights; the default matches SoundCharts.

The overlay draws only the feature outline. A marker at the click point, or
dimming the chart, is up to your application.

## Add your own layers

`session.Layers` (`IS100MapLayerHost`) holds your layers in bands that keep
their order as datasets come and go: basemap, then datasets, then overlays, then
tools.

```csharp
session.Layers.SetBasemapLayer(basemap);
session.Layers.AddOverlayLayer(validationLayer);
session.Layers.AddToolLayer(measureLayer);
```

`RemoveOverlayLayer` and `RemoveToolLayer` remove them. The dataset band isn't
on this interface; the session manages it through `AddDatasetAsync`,
`RemoveDataset` and `SetOrder`.

## Add optional overlays

These overlays depend only on Mapsui. They don't use the session, a catalogue,
an application palette, a view model or Avalonia. Deciding what to show, and
when, is up to your application.

### Dataset extent indicators

`S100DatasetExtentIndicatorLayer` outlines datasets that have dropped out of
scale as you zoom out, so you can still see where they are. Add its `Layer`
once, then call `Show` when datasets, visibility or zoom cutoffs change:

```csharp
var extents = new S100DatasetExtentIndicatorLayer(); // Optionally pass an S100DatasetExtentIndicatorStyle.
map.Layers.Add(extents.Layer);

// One indicator per dataset with a mercator extent and a zoom-out cutoff.
// Extent is EPSG:3857 (Mapsui map units) and ContentMaxVisibleResolution is in
// metres per pixel. Both are nullable, hence the filter.
extents.Show(session.GetDatasets()
    .Where(d => d.Extent is not null && d.ContentMaxVisibleResolution is not null)
    .Select(d => new S100DatasetExtentIndicator(d.Extent!, d.ContentMaxVisibleResolution!.Value)));
// extents.Show(indicators, accent); // Change the accent without rebuilding the layer.
// extents.Clear();                  // Remove all indicators.
```

Each indicator is a thin, dashed, unfilled rectangle. Its `MinVisible` is the
dataset's cutoff, so Mapsui shows it exactly when the dataset stops drawing and
hides it again when you zoom back in. Pass `0` to always show it, for example
for the footprint of a catalogue entry that isn't loaded.
`S100DatasetExtentIndicatorStyle` sets the accent, stroke weight, opacity and
dash.

The layer takes rectangles already projected to EPSG:3857. If you hold
geographic bounds, project them yourself and split any footprint that crosses
the antimeridian into two boxes.

### Overscale curtain

`S100OverscaleCurtainLayer` draws the S-52 and S-101 overscale pattern
(`AP(OVERSC01)` Form A), evenly spaced vertical lines, over cells shown beyond
their compilation scale:

```csharp
var curtain = new S100OverscaleCurtainLayer(); // Optionally pass an OverscaleCurtainStyle.
map.Layers.Add(curtain.Layer);

// The regions depend only on the loaded cells and the resolution (metres per
// pixel), not on pan or rotation, so recompute them only when those change.
var regions = OverscaleCurtain.ComputeRegions(overscaleCells, viewportResolution);
curtain.Show(regions);
// curtain.Clear(); // Remove the curtain.
```

`OverscaleCurtain.ComputeRegions` works in EPSG:3857. It returns one region per
overscaled cell: the cell's coverage minus every finer overlapping cell, so the
curtain marks only area that's really overscaled. The style draws lines anchored
to the map and clipped to each region every frame, so the pattern stays sharp at
any zoom and on HiDPI displays and moves with the chart. `OverscaleCurtainStyle`
sets the line spacing, width and colour.

### Validation findings

`S100ValidationFindingLayer` plots validation findings that have a location: a
marker for a point finding and a translucent box for a bounding-box finding,
coloured by severity:

```csharp
var findings = new S100ValidationFindingLayer(); // Optionally pass an S100ValidationFindingStyle.
map.Layers.Add(findings.Layer);

// A finding can have a Point, a BoundingBox, both (two features) or neither
// (skipped).
findings.Show(report.Findings
    .Where(f => f.Point is not null || f.BoundingBox is not null)
    .Select(f => new S100ValidationFinding(f.Severity, f.Point, f.BoundingBox)));
// findings.Clear(); // Remove the findings.
```

Pass WGS-84 locations; the layer projects them to EPSG:3857. Each `Show`
replaces the overlay's contents. `S100ValidationFindingStyle` sets the severity
colours, the point marker and halo, and the box outline and fill alpha; the
default uses red for errors, amber for warnings and blue for information.

## Navigate the viewport

`MapsuiMapNavigator` frames and recentres an existing `Mapsui.Map`:

```csharp
var navigation = new MapsuiMapNavigator(map);
navigation.ZoomToExtent(datasetExtent);
navigation.CenterOn(new GeoPosition(latitude, longitude));
```

It supports padded framing (`ZoomToExtent`), exact extent or centre and
resolution changes (`SetViewportToExtent`, `SetViewportToCenterAndResolution`),
rotation, recentring on a WGS-84 position, and reporting the viewport centre and
resolution. Exact changes take effect immediately; framing and recentring take
an optional animation duration. The navigator doesn't own the map, handle
gestures, switch threads, invalidate a control or zoom after a load.

## Show dynamic features

The `EncDotNet.S100.Renderers.Mapsui.DynamicSources` namespace draws live
features from an `IDynamicFeatureSource`, such as own ship or AIS targets, on
the overlay band. Renderers turn each `DynamicFeature` into Mapsui features and
styles. For the source model, see the
[dynamic feature source](../../docs/design/dynamic-feature-source.md) design
note.

- **`S100DynamicSourceHost`** registers sources as overlay layers, picks a
  renderer for each source, rebuilds layers when a source changes (coalescing
  frequent updates), and hit-tests features geographically. It implements
  `IS100DynamicSourceRegistry` (registration, per-source visibility and
  hit-testing) and depends only on Mapsui. It takes:
  - an `IMapsuiOverlayLayerHost` to add layers to (`MapsuiLayerBands`
    implements it);
  - an `Action<Action>` to run updates on the UI thread (default: run inline);
  - a `Func<string?, IDynamicFeatureRenderer?>` to resolve renderers (default:
    always the fallback renderer).

  The session exposes one as `IS100MapSession.DynamicSources`. You can also
  construct one over your own layer host.
- **`IDynamicFeatureRenderer`**: `CanRender` and `Render` for one feature.
  Implementations are stateless; the host owns layer state and threading.
- **`DefaultDynamicFeatureRenderer`**: the fallback renderer. It draws a
  coloured disc with an optional heading line scaled by speed (a six-minute
  predictor, capped at 10 nm) for a point, a line for a curve, and a
  translucent fill with an outline for a surface. It's also used when a
  source's `RendererKey` is `null` or not registered.
- **`OwnShipRenderer`**: own-ship symbols under the key `"ownship"`. When the
  vessel is longer than `MinVesselPixels` on screen (22 px, about 6 mm at
  96 dpi), it draws a true-scale hull outline from `DynamicFeature.VesselGeometry`
  with a cross at the GPS antenna; otherwise it draws a disc. Both modes draw a
  heading vector with an arrowhead. Without `VesselGeometry`, for example for an
  AIS target of unknown size, it always draws the disc. See the
  [own-ship symbology](../../docs/design/own-ship-symbology.md) design note.
- **`KindMatchingRenderer`**: chooses a renderer by `DynamicFeature.Kind`, by
  exact match or dotted prefix (`"vessel"` matches `"vessel.cargo"`). The
  longest key wins.
- **`CompositeDynamicFeatureRenderer`**: tries an ordered list of renderers and
  uses the first whose `CanRender` returns `true`. Put specialised renderers
  first and `DefaultDynamicFeatureRenderer` last.
- **`DynamicFeatureRendererServiceCollectionExtensions`**: registers renderers
  in DI under the key a source declares in `DynamicSourceMetadata.RendererKey`:

  ```csharp
  // Register a source and its renderer:
  services.AddDynamicFeatureSource<MyAisFeed, MyVesselRenderer>("vessel");

  // Or only a renderer, to share across sources:
  services.AddDynamicFeatureRenderer<MyVesselRenderer>("vessel");
  ```

  With `AddS100Mapsui`, the session resolves renderers with
  `GetKeyedService<IDynamicFeatureRenderer>(source.Metadata.RendererKey)` by
  default, so keyed registrations work without more setup.

## Compose the session yourself

`AddS100` builds the session from the components below. Use them directly only
when you need a different composition.

### Register the renderers

Call `S100MapsuiRendering.Register()` once at startup, before you install any
diagnostics that wrap Mapsui's renderer registry. It registers the S-100 style
and layer renderers in dependency order and is safe to call more than once.
Rendering doesn't register them for you; `AddS100` calls it.

### Render a dataset's layers

`MapsuiDatasetRenderer` turns a processor's portrayal output into a
`MapsuiDatasetResult` (layers and extent). It reads the processor through the
`IVectorPortrayalSource` and `ICoveragePortrayalSource` interfaces, which are in
`EncDotNet.S100.Core`, so dataset processors don't depend on Mapsui. The
renderer owns everything specific to Mapsui: the pattern-clip cache,
feature-type tagging, the out-of-scale-band cap, the S-101 area and line
layers, the S-111 arrows, and the S-98 layer stack.

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

`S100MapsuiOptions` takes its `SceneMode` default from `RenderingOptimizations`,
the process-wide settings that environment variables seed. Setting a value on the
options configures one renderer without changing the process-wide value. Other
settings are still only on `RenderingOptimizations`. If you omit `options`, the
renderer uses the process-wide settings.

If you omit `patternClipCache`, the renderer keeps a single-entry in-memory
cache for its lifetime. Pass a `DiskPatternClipCache` to share clips across
renderers and process restarts.

### Order layers in bands

`MapsuiLayerBands` keeps a map's layers in band order (basemap, datasets,
overlays, tools) without a UI control:

```csharp
var bands = new MapsuiLayerBands(map);
bands.SetBasemapLayer(basemap);
bands.AddDatasetLayer(datasetLayer);
bands.AddOverlayLayer(validationLayer);
bands.AddToolLayer(measureLayer);
```

`ReplaceDatasetLayers` replaces or reorders only the dataset band. Each remove
method removes only layers in its own band. Calls change `Map.Layers`
immediately; switching threads and invalidating the control are up to the host.

### Manage the dataset band

`MapsuiDatasetLayerSession` manages the dataset band on top of
`MapsuiLayerBands`. It's the component `IS100MapSession.Session` exposes.

```csharp
using var layerSession = new MapsuiDatasetLayerSession(
    bands,
    processorOwner,
    renderer,
    interoperabilityAuthorityProvider);
layerSession.SetDataset(dataset, minimumDisplayScale, maximumDisplayScale);
await layerSession.RenderAsync(dataset.Id, presentation);
layerSession.SetCurrentTime(clock);
await layerSession.RefreshTimeAsync(presentation);
layerSession.SetOrder(bottomToTopDatasetIds);
layerSession.SetMarinerSettings(presentation.Mariner);
```

- Register the processor with the `DatasetProcessorOwner` first. Rendering takes
  a lease on it.
- Replacing layers is transactional. Cancellation, removal, a changed processor
  or an S-98 projection failure leaves the previous layers in place. Concurrent
  renders are latest-started-wins, so an older render can't replace newer
  output or bring back a removed dataset.
- `SetDataset` registers the dataset as time-aware when its processor
  implements `ITimeAwareDatasetProcessor`. `SetCurrentTime` updates the clock
  immediately. `RefreshTimeAsync` waits 100 ms for further changes and cancels
  the previous time refresh. `RefreshAsync` re-renders everything for a new
  presentation; the latest request wins.
- All render methods share one lock. Call and await them from the
  synchronization context that owns the map.
- `GetLayerStackEntries` returns the full S-98 stack, including inactive
  datasets, and `GetStackedLayers` returns the layers in the dataset band.

### Reuse symbol work across renders

`MapsuiDisplayListRenderer` processes SVG symbols and rasterises pattern tiles
the first time it needs them. Processed SVGs depend on the palette, and pattern
rasterisation is relatively expensive. When you re-render the same dataset
repeatedly, for example to switch palettes, step through time or change mariner
settings, give every render the same `MapsuiRenderAssetCache`:

```csharp
private readonly MapsuiRenderAssetCache _renderAssetCache = new();

// For each render:
var renderer = new MapsuiDisplayListRenderer
{
    Palette = palette,
    AssetCache = _renderAssetCache,
    SymbolProvider = symbolProvider,
    AreaFillProvider = areaFillProvider,
};
```

The cache keeps entries for each palette (`Day`, `Dusk`, `Night`), so switching
back and forth keeps all of them warm. Without `AssetCache`, each renderer
instance uses its own cache. `MapsuiDatasetRenderer` keeps one cache per
processor.

### Reuse pattern-fill clips

Clipping pattern fills against each other and against land can take seconds on
dense cells. The clip runs once per layer build, not per frame, and the clipped
geometry doesn't depend on the palette. `IPatternClipCache` lets a render reuse
the clip when its inputs haven't changed, most importantly after a palette
switch. Set both `PatternClipCache` and a `PatternClipCacheKey` that fully
identifies the clip inputs:

```csharp
// One cache per processor:
private readonly InMemoryPatternClipCache _patternClipCache = new();

// Or one disk cache for the whole process:
var sharedClipCache = new DiskPatternClipCache(cacheDir, maxBytes: 256L * 1024 * 1024);

var renderer = new MapsuiDisplayListRenderer
{
    // Palette, providers and asset cache as above.
    PatternClipCache = sharedClipCache,
    PatternClipCacheKey = $"{datasetScope}|{portrayalCacheKey}",
};
```

With both set, a palette switch with the same key reuses the clip. On a dense
test cell, a first Day render took about 6 s and the following Night switch about
0.2 s. If either is unset, the clip is computed every time.

- **`InMemoryPatternClipCache`** holds one entry, so its memory is bounded to one
  cell. It only helps re-renders of a dataset that's already open, and it's lost
  when the dataset closes.
- **`DiskPatternClipCache`** (`cacheDirectory`, `maxBytes`) is shared by the
  process and stored on disk, so even the first open of a cell seen before skips
  the clip, after a restart too. It stores each clip as WKB in a file named after
  the SHA-256 of the key. Writes are atomic, and the least recently used entries
  are removed above `maxBytes`. Any read error or `FormatVersion` mismatch counts
  as a miss and never throws. Because the cache is shared, the key must be fully
  qualified. The S-101 processor uses `{datasetScope}|{portrayalKey}`, where
  `datasetScope` includes the dataset content hash, the clip parameters
  (`PatternPriorityClipper.SimplifyToleranceMetres` and
  `PatternPriorityClipper.MinPointsToSimplify`), the CRS and
  `DiskPatternClipCache.FormatVersion`, so stored clips become invalid when any
  of them changes.

### Reuse the coverage projection

`MapsuiCoverageRenderer` projects every grid node from the coverage's native CRS
to Web Mercator and maps nodes to output pixels. That mapping depends only on
the grid geometry (CRS, dimensions, origin and spacing), not on the palette,
display mode or values. The renderer caches it, with the output size and extent,
and reuses it when the next render has the same geometry, such as after a palette
switch or a new time step. Only classification, pixel fill and PNG encoding run
again.

The cache belongs to the renderer instance and holds one entry, so keep the
instance between renders to benefit. A different geometry rebuilds the entry. It
caches only the node-to-pixel index array, about 4 MB per megapixel grid.

## Performance instrumentation

SoundCharts records OpenTelemetry instruments that break paint time down by
style renderer, layer, source feature class and vertex count. They cost well
under a millisecond per paint when no listener is attached.

| Instrument | Unit | Tags | Purpose |
|---|---|---|---|
| `s100.map.paint.duration` | ms | none | Paint time per frame on the compositor thread. |
| `s100.map.paint.interval` | ms | none | Time between paints. Gaps over 500 ms are dropped. |
| `s100.map.paint.style.calls` | count | `style`, `layer`, `points`, `featureClass` | Style-renderer `Draw` calls per paint. |
| `s100.map.paint.style.duration` | ms | `style`, `layer`, `points`, `featureClass` | Total `Draw` time per paint. |

The `points` tag is bucketed (`n/a`, `0`, `1-9`, `10-99`, `100-999`, `1k-10k`,
`10k-100k`, `100k+`) to keep the number of series small while still showing
whether a layer's cost comes from many cheap draws or a few expensive ones.
`featureClass` is the source feature catalogue type of S-101 and S-57 features,
such as `DepthContour`; other features use `(unclassified)`.

To record a session, run SoundCharts with the OpenTelemetry console exporter:

```bash
ENC_DOTNET_OTEL_CONSOLE=1 OTEL_METRIC_EXPORT_INTERVAL=2000 \
  dotnet run -c Release --project src/EncDotNet.S100.Viewer
```

Histograms are written every 2 s. Group them by `layer`, `featureClass` and
`points` to see which geometry dominates paint time. On real S-101 datasets,
about 93% of paint time went to geometries with 100 or more vertices, at about
1 µs per vertex. For the investigation, see the
[Mapsui performance](../../docs/design/mapsui-performance.md) design note.

This package's own instruments, named `s100.render.*`, are described under
[Base-plane rendering](#base-plane-rendering).

## Internals

The rest of this page describes how the package renders, for contributors and
for anyone tuning performance. Environment variables named here seed
`RenderingOptimizations`; SoundCharts also exposes most of them under
**Settings** > **Advanced** > **Base-plane rendering**. The design reference is
the [S-100 render subsystem](../../docs/design/S100-Render-Subsystem-Design.md)
design note, cited below by appendix.

### Portrayal conventions

`MapsuiDisplayListRenderer` lowers the display list through
`VectorSceneBuilder` in `EncDotNet.S100.Rendering.Scene`, the same lowering the
headless `SkiaDisplayListRenderer` uses. Draw order, colour, symbol and
line-style resolution, millimetre-to-pixel conversion, text anchors, pattern
fills and the latitude/longitude-to-EPSG:3857 projection are all done there. The
tiled renderer draws that scene; the Mapsui features built from it only carry
pick identity, so every painted op, pattern fills included, can be picked.

It follows these S-100 Part 9 conventions:

- Pen widths and text and symbol offsets given in millimetres convert to pixels
  at 1 px = 0.32 mm (S-100 Part 9 §3.10.4).
- `<foreground>` and `<background>` colours accept a palette token or a literal
  `#RRGGBB` or `RRGGBBAA` value. The optional `transparency` attribute reduces
  alpha.
- Text alignment, millimetre offsets, and `textLine` start and end offsets
  (relative or absolute) follow S-100 Part 9 §11.4.
- `LineStyleProvider`, `SymbolProvider` and `AreaFillProvider` let the host plug
  in a portrayal catalogue without tying the renderer to a dataset library.
- **Scale limits are latitude-corrected.** S-100 Part 9 §11.1 scale
  denominators (per-feature `ScaleMinimum` and `ScaleMaximum`, and the
  cell-wide out-of-band cap from `DataCoverage.minimumDisplayScale`) are true
  scales, but a Mapsui resolution is metres per pixel at the equator. Web
  Mercator stretches ground distance by `1/cos φ`, so the resolution for a
  denominator is `denom × 0.00028 / cos φ`. Per-feature limits use the
  feature's extent-centre latitude; the cell-wide cap uses the layer's. This
  matches the headless Skia path. Without the correction, at φ ≈ 50.8° detail
  would disappear about two-thirds of a zoom level early.
- **Cell zoom-out window.** `MapsuiDatasetRenderer.ApplyCellScaleWindow(layers,
  minimumDisplayScale)` caps each layer's `MaxVisible` at the resolution for
  `minimumDisplayScale`, at the layer's extent-centre latitude. The scale is the
  coarsest `minimumDisplayScale` across the cell's `DataCoverage` entries in
  `CATALOG.XML`. It only ever tightens `MaxVisible`. Unlike the per-feature cap,
  which applies to line work only, this window hides the whole cell, area fills
  included, once you zoom out past it, so the coarser cell underneath shows
  through. Finer cells drop out first as you zoom out, so there's always a
  coarser cell beneath. `MapsuiDatasetLayerSession` skips the window when
  `IgnoreScaleMinimum` is set, prefers the host's catalogue scale, and falls
  back to `MapsuiDatasetResult.CellMinimumDisplayScale` for standalone cells.
  For S-57, that's the larger of CSCL and the cell's largest `SCAMIN`.
- **Overlapping cells.** `OverlapSuppression` and `CoverageClip` stop a coarser
  cell drawing where a finer, overlapping cell has coverage, so depth areas and
  fills don't bleed under the finer cell. This is a geometry clip in screen
  space, not a scale cap, and it depends on zoom: a finer cell suppresses a
  coarser one only while the finer cell is itself drawn, so zooming out never
  leaves a hole. `MapsuiDatasetLayerSession` recomputes the clips after render,
  replacement, removal, reorder, and changes to visibility, opacity, active
  state and sub-layers. It applies them after the host's S-98 projection.
  Hidden, transparent, inactive and unloaded cells never suppress other cells.
  Cells are ranked by `MapsuiDatasetResult.CellCompilationScale` when it's set
  (S-57 CSCL), otherwise by their zoom-out window. A finer cell's cutoff always
  follows its zoom-out window (`OverlapSuppressionCell.CutoffScaleDenominator`).
  The same ranking sets `SubLayerStackItem.SourceScaleDenominator` for paint
  order.

### Pattern-fill clip simplification

When it builds the scene, the renderer simplifies the polygons used to clip
pattern fills against each other and against solid fills such as land. S-101
quality and coverage areas (for example `M_QUAL`) can follow the coastline with
tens of thousands of vertices, most of them smaller than a pixel at chart
scales. The NetTopologySuite `Difference` and `Union` operations get much slower
as vertex counts grow, so one such area could dominate a frame: about 10 s of an
11 s frame on one 64,000-vertex pattern area in a 2.35 MB cell.

Before the overlay, each merged pattern geometry and the land mask go through
NTS `TopologyPreservingSimplifier` with a 1 m (EPSG:3857) tolerance
(`PatternPriorityClipper.SimplifyToleranceMetres`). The result stays valid for
overlay; if it doesn't validate, it's repaired with `buffer(0)`, and on any
failure the original geometry is used. The clipped boundary only bounds a tiled
pattern, so the change isn't visible; the S-101 visual-regression snapshot
didn't change. An envelope test also skips `Difference` when the mask doesn't
touch the entry. Together these took the clip on that cell from about 11 s to
well under 1 s. The clip's remaining cost is why the
[pattern-clip caches](#reuse-pattern-fill-clips) exist.

### Base-plane rendering

The base plane is the chart's area fills, contours and lines. The scene path is
the only base-plane path: the `VectorScene` is drawn by
`S100VectorTileRenderer` (default) or `S100VectorSceneRenderer`. The earlier
Mapsui feature and style path, its caches, line simplification and line
level-of-detail pyramid have been removed; see the repository history for them.

#### Single-surface renderer

`S100VectorSceneRenderer` is a Mapsui custom layer renderer that rasterises the
scene with `SkiaDisplayListRenderer` on a worker thread, then swaps in and blits
the finished `SKImage` on the UI thread (Appendix B). It renders the viewport
plus a margin (`S100_VECTOR_SCENE_MARGIN`, default 256 DIP) at device scale, so
a pan within that margin is only a translated blit.

Select it with `S100_VECTOR_SCENE_MODE=single`,
`RenderingOptimizations.SceneMode = VectorSceneMode.Single`, or in SoundCharts
**Settings** > **Advanced** > **Base-plane rendering** > **Scene mode** >
**Single surface**. `MapsuiDisplayListRenderer` then tags the layer with
`S100VectorSceneRenderer.RendererName` and binds the scene with `BindScene`.

The worker is latest-wins: a superseded request is dropped, never published. It
applies scale visibility, converting the EPSG:3857 resolution back to an S-100
denominator (`ScaleDenominatorFor`), so the same SCAMIN detail shows and hides
as on the live frame. Rotated viewports draw nothing; this renderer is north-up
only. On publish it asks for a repaint through the layer's redraw sink.
`SceneRasterizeDuration` (worker) and `SceneCompositeDuration` (UI blit)
measure the two halves. In an 18-step gesture script on the PDB01 cell, the
worst on-screen frame went from about 409 ms on the retired Mapsui path to about
5 ms (Appendix B).

#### Tiled base plane

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
its core over a rendered gutter (`S100_VECTOR_TILE_GUTTER`, default 64 DIP), so
strokes stay continuous across tile edges and no hole shows. The target band is
drawn on top. While it's incomplete, cached tiles from the single nearest other
band are drawn underneath, within `MaxFallbackBandDistance` (2) bands, so zoom
transitions don't show symbols at several sizes.

**Cache and workers.** Finished tiles go into a thread-safe LRU `TileCache`
bounded by a native-byte budget (`S100_VECTOR_TILE_BUDGET_MB`, default set by
the [performance profile](#performance-profile)). Visible tiles are kept most
recently used, so they're never evicted mid-frame. All cache access goes through
the layer lock, so a worker can't dispose an image the compositor is drawing.
Each layer has a pool of coalescing workers that drains the visible misses,
which are replaced every frame. `S100_VECTOR_TILE_WORKERS` sets the pool's
minimum size (default set by the performance profile). A process-wide cap, the
logical core count, stops many layers from oversubscribing the CPU and starving
the UI thread. A layer with visible work waiting can borrow idle capacity up to
that cap, but only for visible work: prediction never borrows, and a borrowed
worker stops when the visible work is done. Each other layer with visible work
keeps its own minimum, so a dense layer low in the stack can't starve the layers
above it. On a `LowEnd` host, with one worker, there's nothing to borrow.
`TileRasterizeDuration` (worker) and `TileCompositeDuration` (UI) measure the
two halves; `TileColdLatency` measures how long a cold tile takes to appear,
queue wait included.

**Rotation.** A rotated viewport, such as one left by a trackpad pinch, is
composited north-up to an off-screen surface, and that image is rotated about
the screen centre by an angle taken from Mapsui's `WorldToScreenXY`. Tile
selection grows to the rotated viewport's bounding box
(`TileGrid.RotatedCoverSize`) so the corners are covered. Compositing north-up
first keeps tile joins and the edge between fallback and target bands
axis-aligned, so zooming while rotated doesn't show seams (Appendix F.8).

**Device scale.** Tiles are rasterised at the frame's device scale
(`canvas.TotalMatrix.ScaleX`): a 256-DIP tile plus its two gutters is 384 px at
1x and 768 px at 2x. A cached tile's pixel size records its scale. A tile of
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

#### Performance profile

`MachineProfile` sizes the tile caches and worker pools for the host. A
`PerformanceProfile` tier seeds the hot, GPU and disk budgets. `Auto`, the
default, picks the tier from logical cores and available memory: `LowEnd` for
4 or fewer cores or 8 GB or less, `Balanced` for 8 or fewer cores or 16 GB or
less, otherwise `HighEnd`. Set `S100_PERF_PROFILE` (`Auto`, `LowEnd`,
`Balanced` or `HighEnd`) to choose a tier. The individual budget variables
still override each budget.

| Setting | `LowEnd` | `Balanced` | `HighEnd` |
|---|---|---|---|
| Hot tile cache per layer (`S100_VECTOR_TILE_BUDGET_MB`) | 192 MB | 256 MB | 256 MB |
| GPU tile cache per layer (`S100_VECTOR_TILE_GPU_MB`) | 192 MB | 256 MB | 256 MB |
| Disk tile cache (`S100_VECTOR_TILE_DISK_MB`) | 256 MB | 384 MB | 512 MB |
| Skia GPU resource cache (`S100_SKIA_GPU_RESOURCE_MB`) | 128 MB | 256 MB | 512 MB |
| Tile workers per layer (`S100_VECTOR_TILE_WORKERS`) | 1 | 2 | One per 4 cores, 3 to 8 |
| Cross-band pre-warm (`S100_VECTOR_TILE_XBAND`) | Off | On | On |

SoundCharts shows the profile, the detected tier and the worker count under
**Settings** > **Advanced**.

Skia keeps an uploaded texture for every raster tile it blits in the host GPU
context's resource cache (`RenderingOptimizations.SkiaGpuResourceMb`). That
cache must hold the visible tiles of every layer, or every tile is uploaded
again every frame. Avalonia's default, about 28 MB, holds only a couple of dozen
Retina tiles, so pass this budget to `SkiaOptions.MaxGpuResourceSizeBytes`.
SoundCharts does this at startup.

#### Symbol and sounding overlay

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

#### Prediction

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

#### Cross-band pre-warm

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

#### Metatile raster jobs

The tiled renderer can take pending tiles from one aligned 2×2 block in one
priority tier, rasterise them together, and split the result back into ordinary
tile cache and disk entries. SCAMIN visibility is checked for each row; a job
splits by row when visibility differs. Per-tile cold latency, prediction
accounting, eviction and repaint behaviour don't change.

Metatiling is experimental and off by default. Turn it on with
`S100_VECTOR_TILE_METATILE=1`, `RenderingOptimizations.TileMetatileEnabled`, or
SoundCharts' **Batch adjacent tiles** setting. Measure it with
`s100.render.metatile.rasterize.duration`, `.slice.duration`, `.tiles`, `.jobs`
and `.fallbacks`. The fallback counter has a `reason` tag: `sparse`, `disk`,
`scamin`, `dimension` or `scale`. The `scale` fallback keeps single-tile
rendering when a fractional device scale can't give the core and gutter whole
pixel sizes. Leave it off unless A/B runs on real cells show lower total raster
time without worse cold latency, paint time, memory or pixels.

#### Disk tile cache

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

#### GPU texture residency

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

#### GPU resource lifetime and shutdown

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
