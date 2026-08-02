# Embedding the renderer

The batteries-included [`EncDotNet.S100`](getting-started.md) facade is the
easiest way to open a dataset and render it to an image. But if you already have
a display list (or want to build a scene yourself) and only need the
**renderer**, you can depend on two small, headless, Mapsui-free packages
instead of the whole facade:

| Package | Role |
|---|---|
| [`EncDotNet.S100.Rendering.Scene`](https://github.com/philliphoff/EncDotNet.S100/blob/main/src/EncDotNet.S100.Rendering.Scene/README.md) | The backend-neutral **scene IR** — `VectorScene`, the `PaintOp` hierarchy, `VectorSceneBuilder`, `ColorResolver`, `ScaleVisibility`, `WebMercator`. Depends only on `EncDotNet.S100.Core` and `EncDotNet.S100.Portrayals`. |
| [`EncDotNet.S100.Renderers.Skia`](https://github.com/philliphoff/EncDotNet.S100/blob/main/src/EncDotNet.S100.Renderers.Skia/README.md) | The headless **SkiaSharp rasteriser** — `SkiaDisplayListRenderer`, `HeadlessVectorRenderer`, `CoverageHeadlessRenderer`, `HeadlessCompositeRenderer`. |

Neither package references Mapsui, Avalonia, or any GUI framework, so this is the
seam to embed into a tile-serving web API, a batch image job, or the library half
of a future web/WASM target.

Hosts that own dataset processors can also capture map-wide portrayal choices in
`MapPresentationState` from `EncDotNet.S100.Datasets.Pipelines`. The immutable
snapshot carries palette, symbol/text scale, ECDIS and mariner settings, including
per-product display modes, and projects them onto a product `RenderContext`:

```csharp
var presentation = new MapPresentationState(
    PaletteType.Day,
    symbolScale: 1.0,
    textScale: 1.0,
    ecdisSettings,
    marinerSettings);

RenderContext context = presentation.ApplyTo(
    new S101RenderContext(),
    processor.PortrayalSpec);
```

This presentation layer is renderer-neutral: it does not reference Mapsui,
Avalonia, or SkiaSharp. Per-dataset time and viewport choices remain on the
specific render context. Hosts that manage loaded datasets can expose
`IMapPresentationController.SetPresentationAsync(presentation, cancellationToken)`
to apply the snapshot explicitly. The boundary does not own the supplied state
or prescribe processor, layer, renderer, or UI lifecycles.

Loaded dataset state has the same renderer-neutral seam:

```csharp
var dataset = new MapDataset(
    new MapDatasetId("US5WA50M.000"),
    "US5WA50M.000",
    processor.Metadata,
    availableTimes: timeSteps,
    validation: processor.Validate(),
    versionAssessment: processor.VersionAssessment);
```

`MapDataset` snapshots metadata and extent, independent visibility and active
state, opacity, available/current time, sub-layer state, validation, and version
assessment. It intentionally excludes rendered layers, UI commands, framework
events, and localized strings so a future map session can own the state without
depending on Viewer, Mapsui, or Avalonia.

The Viewer now follows that boundary throughout its loaded-dataset lifecycle:
its dataset and sub-layer view-models project `MapDataset` /
`MapDatasetSubLayer` snapshots while retaining only UI commands, localized
labels, selection, and registration metadata. Map-wide Viewer inputs are
similarly projected into one current `MapPresentationState` before render
contexts are created, then applied through `IMapPresentationController` rather
than a presentation-specific refresh event. The Viewer therefore no longer acts
as the rendering identity or active-state store. Processor and layer ownership
remain in the Viewer loader until the later reusable session extraction.

The Viewer's live map adapter is also segregated by responsibility. Its
`MapsuiMapHost` implements separate internal capabilities for layer-band
collection, viewport/navigation, coordinate conversion, snapshot rendering, and
redraw invalidation. Dataset loading receives only the layer and viewport
capabilities; overlays receive only layer collection; MCP and feedback services
use typed late-bound accessors for only the viewport, conversion, or snapshot
capability they need. There is no aggregate `IMapHost` facade.

This is an application-boundary cleanup rather than the final reusable session
API. Layer ownership continues to use the reusable `MapsuiLayerBands` component
against `Mapsui.Map`, while Avalonia dispatch, live-control invalidation, and
capture remain in the Viewer adapter. A future reusable navigator adapter and
separate Avalonia capture package can therefore be extracted without consumers
depending on the current control-bound implementation.

## Why it matters

This is the smallest seam for teams that already own portrayal outputs or want
direct control over rendering without the full facade package.

## Quick win

```sh
dotnet add package EncDotNet.S100.Renderers.Skia
```

Expected result: headless rasterization support in your app with no Mapsui/Avalonia dependency.

```sh
dotnet add package EncDotNet.S100.Renderers.Skia
```

Adding the renderer transitively brings in the scene IR. Add
`EncDotNet.S100.Rendering.Scene` explicitly if you build a `VectorScene` without
touching Skia types.

## The two layers

The renderer is split into two seams so the portrayal-correctness logic and the
rasteriser stay independent:

1. **Lowering** — a `DrawingInstruction` display list (S-100 Part 9 portrayal
   output) is lowered into a `VectorScene`: an ordered list of fully-resolved
   `PaintOp`s in EPSG:3857 metres with sizes in logical pixels and colours
   resolved to `RgbaColor`. This is `VectorSceneBuilder` (in `Rendering.Scene`).
2. **Rasterising** — a `VectorScene` + `Viewport` is drawn to an `SKBitmap` by
   `SkiaDisplayListRenderer` (in `Renderers.Skia`). Because every backend
   consumes the same IR, the same scene can be driven through a different
   backend for apples-to-apples comparison.

## Rendering a display list in one call

If you have a display list plus the catalogue providers (symbol SVG, line style,
colour palette), `HeadlessVectorRenderer.Render` does both steps and auto-fits
the viewport to the scene extent:

```csharp
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

SKBitmap bitmap = HeadlessVectorRenderer.Render(
    instructions,          // IReadOnlyList<DrawingInstruction>
    geometryProvider,      // IFeatureGeometryProvider
    palette,               // ColorPalette
    symbolProvider,        // Func<string, string?>?  (name -> SVG)
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

## Rendering into an explicit viewport

For a tile server (or any caller that owns the projection), lower the scene once
and draw it onto your own canvas / viewport with `SkiaDisplayListRenderer`:

```csharp
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

VectorScene scene = new VectorSceneBuilder
{
    ResolveColor = ColorResolver.Create(palette), // Func<string?, RgbaColor>, required
    // SymbolResolver / LineStyleProvider / PatternResolver are optional
}.Build(instructions, geometryProvider);

var renderer = new SkiaDisplayListRenderer
{
    Background = RgbaColor.Transparent,
    HonorScaleVisibility = true, // an explicit viewport carries a real scale
};

// Render == allocate a bitmap and draw:
SKBitmap tile = renderer.Render(scene, viewport);

// …or draw onto an existing canvas (compositing an overlay, etc.):
renderer.RenderOnto(canvas, scene, viewport);
```

Set `HonorScaleVisibility = false` when the viewport is synthesised from a fitted
extent (an auto-fit / "render the whole dataset" call), because a fitted scale
denominator is not the dataset's compilation scale and would wrongly cull
scale-ranged detail.

## Compositing multiple datasets

To paint several vector and coverage datasets into one image with a shared
viewport, wrap each as a `CompositeLayer` (`VectorCompositeLayer` /
`CoverageCompositeLayer`) and paint the ordered stack with
`HeadlessCompositeRenderer`. The cross-dataset ordering / suppression *decision*
(S-98 interoperability) is made upstream; this renderer only paints the resolved
stack.

## Coverage products

Coverage products (S-102 / S-104 / S-111) rasterise through
`CoverageHeadlessRenderer` (whole-layer, auto-fit) or `SkiaCoverageRenderer`
(`ICoverageRenderer<SKBitmap>`, cell → colour) rather than the vector path.

## Stability & versioning

The **stable, supported surface** is the documented type set of each package
(see their READMEs). `internal` and undocumented types are implementation detail
and may change at any time.

All `EncDotNet.S100.*` packages share **one version**, derived from the release
git tag — there is no per-package version, and these two packages move in lockstep
with the facade. Versioning follows [Semantic Versioning](https://semver.org/):
once past `1.0.0`, a breaking change to a documented surface lands only in a
**major** bump. While the version is below `1.0.0`, the surface is still settling
— breaking changes may occur in a minor bump and will be called out in the
release notes.

## Linux arm64 note

When you publish a `linux-arm64` executable that uses the Skia renderer, reference
the self-contained SkiaSharp native in your **application** project — see the
[`Renderers.Skia` README](https://github.com/philliphoff/EncDotNet.S100/blob/main/src/EncDotNet.S100.Renderers.Skia/README.md#linux-arm64-native-dependency)
([issue #23](https://github.com/philliphoff/EncDotNet.S100/issues/23)).

## Troubleshooting

> [!IMPORTANT]
> If your output is blank or clipped, verify your viewport extent and scale-visibility handling (`HonorScaleVisibility`).

## Next step

- [Top APIs](top-apis.md)
- [Command-line rendering](cli.md)
- [S-98 interoperability design note](design/s98-interoperability.md)
