# Embedding the renderer

The [`EncDotNet.S100`](../src/EncDotNet.S100/README.md) package opens a dataset
and renders it to an image in one call. If you already have a display list, or
want to build and draw a scene yourself, you can reference only the renderer
packages instead:

| Package | Contents |
|---|---|
| [`EncDotNet.S100.Rendering.Scene`](../src/EncDotNet.S100.Rendering.Scene/README.md) | The renderer-neutral scene model: `VectorScene`, the `PaintOp` types, `VectorSceneBuilder`, `ColorResolver`, `ScaleVisibility` and `WebMercator`. It depends only on `EncDotNet.S100.Core` and `EncDotNet.S100.Portrayals`. |
| [`EncDotNet.S100.Renderers.Skia`](../src/EncDotNet.S100.Renderers.Skia/README.md) | The headless SkiaSharp rasteriser: `SkiaDisplayListRenderer`, `HeadlessVectorRenderer`, `CoverageHeadlessRenderer` and `HeadlessCompositeRenderer`. |

Neither package references Mapsui, Avalonia or any other UI framework, so you
can use them in a tile server, a batch image job or another headless host. To
show S-100 data on an interactive Mapsui map instead, see
[Attach to a Mapsui map](#attach-to-a-mapsui-map).

## Install

```bash
dotnet add package EncDotNet.S100.Renderers.Skia
```

The Skia package brings in `EncDotNet.S100.Rendering.Scene`. Reference
`EncDotNet.S100.Rendering.Scene` on its own if you only build a `VectorScene`
and don't use Skia types.

## How rendering works

Rendering has two steps, so the portrayal logic and the rasteriser stay
independent:

1. **Lowering.** `VectorSceneBuilder` (in `Rendering.Scene`) turns a
   `DrawingInstruction` display list, the S-100 Part 9 portrayal output, into a
   `VectorScene`: an ordered list of `PaintOp`s in EPSG:3857 metres, with sizes
   in logical pixels and colours resolved to `RgbaColor`.
2. **Rasterising.** `SkiaDisplayListRenderer` (in `Renderers.Skia`) draws a
   `VectorScene` and a `Viewport` to an `SKBitmap`.

Every backend consumes the same scene, so you can draw one scene through
different backends and compare the results.

## Render a display list in one call

With a display list and the catalogue providers (symbol SVG, line style and
colour palette), `HeadlessVectorRenderer.Render` does both steps and fits the
viewport to the scene's extent:

```csharp
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

SKBitmap bitmap = HeadlessVectorRenderer.Render(
    instructions,          // IReadOnlyList<DrawingInstruction>
    geometryProvider,      // IFeatureGeometryProvider
    palette,               // ColorPalette
    symbolProvider,        // Func<string, string?>? (symbol name to SVG)
    lineStyleProvider,     // Func<string, LineStyle?>?
    symbolScale: 1.0,
    textScale: 1.0,
    widthPixels: 1024,
    heightPixels: 1024,
    background: RgbaColor.Transparent);

using var image = SKImage.FromBitmap(bitmap);
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes("out.png", data.ToArray());
```

Optional parameters add an area-fill provider, hidden instruction categories,
the offline basemap and an explicit `Viewport`.

## Render into an explicit viewport

A tile server, or any caller that owns the projection, can build the scene once
and draw it into its own viewport or canvas with `SkiaDisplayListRenderer`:

```csharp
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

VectorScene scene = new VectorSceneBuilder
{
    ResolveColor = ColorResolver.Create(palette), // required
    // SymbolResolver, LineStyleProvider and PatternResolver are optional.
}.Build(instructions, geometryProvider);

var renderer = new SkiaDisplayListRenderer
{
    Background = RgbaColor.Transparent,
    HonorScaleVisibility = true, // an explicit viewport has a real scale
};

// Allocate a bitmap and draw into it.
SKBitmap tile = renderer.Render(scene, viewport);

// Or draw onto an existing canvas, for example to composite an overlay.
renderer.RenderOnto(canvas, scene, viewport);
```

`HonorScaleVisibility` hides features outside their display-scale range. Set it
to `false` when the viewport is fitted to the data's extent, as in a "render
the whole dataset" call: a fitted scale isn't the dataset's compilation scale,
so scale visibility would hide detail it shouldn't. If your output is blank or
missing detail, check the viewport's extent and this setting first.

## Composite several datasets

To draw several vector and coverage datasets into one image with a shared
viewport, wrap each as a `CompositeLayer` (`VectorCompositeLayer` or
`CoverageCompositeLayer`) and draw the ordered stack with
`HeadlessCompositeRenderer`. The order and suppression between datasets (S-98
interoperability) are decided before this step; the renderer only draws the
stack it's given. See [S-98 interoperability](design/s98-interoperability.md).

## Render coverage products

Coverage products (S-102, S-104 and S-111) don't go through the vector path.
Use `CoverageHeadlessRenderer` to draw a whole layer fitted to its extent, or
`SkiaCoverageRenderer` (an `ICoverageRenderer<SKBitmap>` that maps cells to
colours).

## Describe map-wide presentation

A host that owns dataset processors can keep its map-wide portrayal choices in
one `MapPresentationState` (namespace `EncDotNet.S100.Datasets.Pipelines`). It's
an immutable snapshot of palette, symbol and text scale, ECDIS and mariner
settings, and display modes per product. It creates a product `RenderContext`
for a processor:

```csharp
using EncDotNet.S100.Datasets.Pipelines;

var presentation = new MapPresentationState(
    PaletteType.Day,
    symbolScale: 1.0,
    textScale: 1.0,
    ecdisSettings,
    marinerSettings);

DateTime? selectedTime = null;
RenderContext context = presentation.CreateRenderContext(processor, selectedTime);
```

`CreateRenderContext` picks the context type for the product and carries the
selected time for S-104, S-111 and S-411. If a request needs its own viewport,
basemap or instruction filter, build that context yourself and call
`presentation.ApplyTo(context, processor.PortrayalSpec)`.

A host that manages loaded datasets can implement
`IMapPresentationController.SetPresentationAsync(presentation, cancellationToken)`
(namespace `EncDotNet.S100.Maps`) to apply a snapshot. The state doesn't own
processors, layers, renderers or UI, and doesn't reference Mapsui, Avalonia or
SkiaSharp.

## Track loaded datasets

`MapDataset` is the matching snapshot for one loaded dataset:

```csharp
using EncDotNet.S100.Datasets.Pipelines;

var dataset = new MapDataset(
    new MapDatasetId("US5WA50M.000"),
    "US5WA50M.000",
    processor.Metadata,
    availableTimes: timeSteps,
    validation: processor.Validate(),
    versionAssessment: processor.VersionAssessment);
```

It holds the metadata and extent, visibility, active state, opacity, available
and current time, sub-layer state, validation report and version assessment. It
leaves out rendered layers, UI commands, framework events and localized
strings, so a map session can own it without depending on any UI.

## Attach to a Mapsui map

To show S-100 data on an interactive map, reference
`EncDotNet.S100.Renderers.Mapsui` and call `map.AddS100(options)` on a
`Mapsui.Map`. Set `CrsTransformFactory` on the `S100MapsuiOptions`, for example
to a `ProjNetCrsTransformFactory` from `EncDotNet.S100.Crs.ProjNet`.

`AddS100` builds the layer bands, processor owner, dataset renderer, dataset
session and navigator, and returns them as one disposable `IS100MapSession`:

- `AddDatasetAsync` adds a dataset from a processor you created.
- `session.Datasets.LoadAsync(path)` loads a single file or cell from a path,
  when you set `DatasetPipelineFactory` on the options. Loading an exchange-set
  folder or ZIP isn't supported yet.
- `AddS100Mapsui` registers an `IS100MapSessionFactory` for dependency
  injection. You register an `ICrsTransformFactory` yourself.

Layer order uses `MapsuiLayerBands` on the `Mapsui.Map`, and navigation uses
`MapsuiMapNavigator` on `Map.Navigator`. Both change the map directly and don't
need Avalonia. The
[Renderers.Mapsui README](../src/EncDotNet.S100.Renderers.Mapsui/README.md#add-s-100-data-to-a-map)
has a complete example, the options, and picking.

For an Avalonia app, reference `EncDotNet.S100.Renderers.Mapsui.Avalonia`. Its
`S100MapControl` attaches a session to itself. Or call `mapControl.AddS100(options)`
on a Mapsui `MapControl`, which returns the session and an
`AvaloniaMapsuiMapAdapter`. The adapter handles redraws on the UI thread,
coordinate conversion, PNG captures of the current view and Avalonia control
capture. Use it with a `CaptureSynchronizedMapControl`, which keeps an
offscreen capture from racing the live Skia paint over shared GPU images. The
adapter doesn't own datasets, processors, presentation, S-98 composition or host
UX. See the
[Renderers.Mapsui.Avalonia README](../src/EncDotNet.S100.Renderers.Mapsui.Avalonia/README.md).

## Publish for Linux arm64

When you publish a `linux-arm64` executable that uses the Skia renderer,
reference the self-contained SkiaSharp native library in your application
project. See
[Linux arm64 native dependency](../src/EncDotNet.S100.Renderers.Skia/README.md#linux-arm64-native-dependency)
in the Renderers.Skia README.

## Versions and supported surface

The supported surface of each package is the set of types its README
documents. `internal` and undocumented types can change at any time.

All `EncDotNet.S100.*` packages share one version, taken from the release tag,
so the renderer packages always match the facade. Versions follow
[Semantic Versioning](https://semver.org/). Below `1.0.0`, the API is still
settling: a minor version can include breaking changes, and the release notes
list them. From `1.0.0`, breaking changes to a documented surface come only in a
major version.

## See also

- [Top APIs](top-apis.md): the main entry points in each package.
- [Command-line rendering](cli.md): render datasets with `s100` instead of
  code.
- [S-98 interoperability](design/s98-interoperability.md): how composite layers
  are ordered.
