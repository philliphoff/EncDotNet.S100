# EncDotNet.S100.Renderers.Skia

`EncDotNet.S100.Renderers.Skia` rasterises S-100 vector scenes and coverage
grids to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps, with no map
control or UI framework. Reference it when you render S-100 data to images
without the `EncDotNet.S100` facade: in a tile server, a batch image job, or your
own compositor. It draws the vector scene IR from
[`EncDotNet.S100.Rendering.Scene`](../EncDotNet.S100.Rendering.Scene/README.md),
which it brings in as a dependency.

For the end-to-end path, from display list to PNG, see
[Embedding the renderer](../../docs/embedding-the-renderer.md).

## Install

```bash
dotnet add package EncDotNet.S100.Renderers.Skia
```

If you publish for `linux-arm64`, also follow
[Linux arm64 native dependency](#linux-arm64-native-dependency).

## Render a display list to PNG

`HeadlessVectorRenderer.Render` lowers a display list to a `VectorScene`,
fits the viewport to the scene's extent and rasterises it:

```csharp
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

static void RenderToPng(
    IReadOnlyList<DrawingInstruction> instructions,
    IFeatureGeometryProvider geometryProvider,
    ColorPalette palette,
    Func<string, string?>? symbolProvider,
    Func<string, LineStyle?>? lineStyleProvider,
    string outputPath)
{
    using SKBitmap bitmap = HeadlessVectorRenderer.Render(
        instructions,
        geometryProvider,
        palette,
        symbolProvider,
        lineStyleProvider,
        symbolScale: 1.0,
        textScale: 1.0,
        widthPixels: 1024,
        heightPixels: 1024,
        background: RgbaColor.Transparent);

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(outputPath, data.ToArray());
}
```

Optional parameters add an area-fill provider for pattern fills, hide
instruction categories, draw the offline basemap underneath (`BasemapKind`), or
render an explicit `Viewport` instead of fitting one. With an explicit viewport,
S-100 Part 9 scale-visibility culling applies.

## Main entry points

### Vector scenes

These types are in the `EncDotNet.S100.Renderers.Skia.Scene` namespace.

- **`HeadlessVectorRenderer`**: renders a display list in one call, as above.
  `BuildScene` and `TryGetWorldBounds` expose the lowering and bounds steps so
  a compositor can lower a dataset and fit it into a shared viewport.
  `TryGetSeamAwareWorldBounds` frames datasets that cross the ±180°
  antimeridian on their true extent.
- **`SkiaDisplayListRenderer`**: draws a `VectorScene` for a given `Viewport`.
  It implements `IVectorSceneRenderer<SKCanvas>`. `Render` returns a new
  bitmap; `RenderOnto` draws onto a canvas you own.
  - Set `HonorScaleVisibility` to `false` when the viewport is fitted to the
    data rather than chosen by the user. A fitted scale isn't the dataset's
    compilation scale and would hide scale-limited detail.
  - Set `EnableSeamWrap` to `false` when you draw geometry that's already
    continuous across the antimeridian into a small viewport, such as a map
    tile. Otherwise, off-tile vertices of large polygons wrap to the other side
    of the world.
  - The `RenderOnto` overload that takes `OverlayDrawOptions` draws a label
    overlay: it can suppress text, keep labels upright under a rotated
    viewport, and filter which point and text ops it draws.
- **`LabelDeclutterer`**: given a `VectorScene`, returns the text ops to hide
  so labels don't overlap symbols or higher-priority labels (S-100 Part 9). Point
  symbols reserve their screen space first, then labels are placed in priority
  order. The result is deterministic.
- **`OverlayDrawOptions`**: the options for an overlay `RenderOnto` pass: cull
  bounds, suppressed text, text rotation and screen centre, and point and text
  draw filters.

### Coverage grids

- **`SkiaCoverageRenderer`**: an `ICoverageRenderer<SKBitmap>` that maps
  coverage grid cells (S-102, S-104, S-111) to pixel colours.
- **`SkiaCoverageArrowRenderer`**: draws oriented coverage symbols, such as
  S-111 current arrows.
- **`CoverageHeadlessRenderer`**: rasterises a `StyledCoverageLayer`. `Render`
  fits the viewport to the grid; `DrawOnto` draws the grid and arrows into a
  shared viewport so they line up with vector layers in a composite.

### Composite images

- **`CompositeLayer`**: one layer of a composite, drawn against a shared
  `Viewport` on a transparent background. Use `VectorCompositeLayer` for a
  `VectorScene` and `CoverageCompositeLayer` for a coverage layer.
- **`HeadlessCompositeRenderer`**: clears the background once, then draws an
  ordered list of `CompositeLayer`s against a shared viewport. It doesn't decide
  the order or which layers to suppress. The S-98 interoperability engine
  (`HeadlessCompositor` in `EncDotNet.S100.Datasets.Pipelines`) makes that
  decision; this renderer draws the result.

### Basemap and symbols

- **`NaturalEarthBasemap`**: the bundled, offline Natural Earth 1:10m land
  layer (public domain). The data is already projected to EPSG:3857, cut into
  tiles, and stored at several levels of detail. `SelectLevel` picks the
  coarsest level that stays within a pixel at a given resolution,
  `GetLandPolygons` returns that level's land in a rectangle, and
  `GetLandScene` returns the land in a `Viewport` as `AreaPaintOp`s filled with
  `LandFill` (RGB 238, 232, 220). The headless renderers, `HeadlessCompositor`
  and SoundCharts all draw land from this one asset.
- **`SkiaSvgRasterizer`**: rasterises SVG portrayal symbols into tiled pattern
  bitmaps.

## Linux arm64 native dependency

When you publish a `linux-arm64` executable that uses this renderer, reference
the self-contained SkiaSharp native library in your application project:

```xml
<!-- In your app's .csproj -->
<ItemGroup>
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" ExcludeAssets="all" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" />
</ItemGroup>
```

The arm64 `libSkiaSharp.so` in `SkiaSharp.NativeAssets.Linux` declares
undefined `uuid_*` and `FT_Get_BDF_Property` symbols. Once `fontconfig` and
`freetype` load on an ordinary arm64 host, any render crashes with
`undefined symbol: …`. The `NoDependencies` build is self-contained and renders
on both x64 and arm64. The executable chooses native assets for its runtime, so
this library can't make the swap for you. See
[issue #23](https://github.com/philliphoff/EncDotNet.S100/issues/23).

Without `fontconfig`, the host may have no usable system font. The package
embeds an Open Sans face (Apache-2.0) and uses it for labels in that case; see
[Embedded render fonts](Assets/Fonts/README.md).

## Stability and versioning

The supported surface of this package is the headless rendering entry points:
`SkiaDisplayListRenderer` (including its `RenderOnto` overloads),
`HeadlessVectorRenderer`, `CoverageHeadlessRenderer`,
`HeadlessCompositeRenderer`, the `CompositeLayer` family
(`VectorCompositeLayer`, `CoverageCompositeLayer`), `OverlayDrawOptions`,
`LabelDeclutterer`, `SkiaCoverageRenderer`, `SkiaCoverageArrowRenderer` and
`SkiaSvgRasterizer`. Internal and undocumented types, such as colour and font
helpers and diagnostics, can change at any time.

All `EncDotNet.S100.*` packages share one version, taken from the release git
tag. Versioning follows [Semantic Versioning](https://semver.org/): from `1.0.0`
on, a breaking change to the supported surface comes only in a major version.
Below `1.0.0`, a minor version can include breaking changes, and the release
notes list them.

## How `SkiaDisplayListRenderer` draws a scene

This section is for contributors and for hosts that redraw a scene every frame,
as the Mapsui tiled renderer does for its symbol overlay.

- **Lowering.** `VectorSceneBuilder` (in `EncDotNet.S100.Rendering.Scene`)
  applies S-100 Part 9 draw order; colour, symbol and line-style resolution;
  millimetre-to-pixel conversion (1 px = 0.32 mm); text-anchor selection; and
  the latitude/longitude-to-EPSG:3857 half of the projection. This renderer
  applies the other half, EPSG:3857 to screen pixels, from the `Viewport`.
- **Pattern fills.** When a pattern resolver is supplied, as both the headless
  path and the Mapsui tiled renderer do, pattern fills are lowered into the
  scene as `PatternAreaPaintOp`s. `PatternPriorityClipper` clips them so a
  lower-priority pattern doesn't show through a higher-priority pattern or an
  opaque solid fill.
- **Antimeridian.** For a fitted viewport, `SeamAwareBoundsAccumulator` frames
  datasets that cross ±180° on their true extent, and `WorldToScreen` wraps ops
  into the shifted window at draw time. Turn this off with `EnableSeamWrap` or
  `WorldToScreen.Create(viewport, allowSeamWrap)`.
- **Per-frame cost.** Parsed symbol pictures are cached for the process, keyed
  by the resolved SVG. Point and text ops whose anchor falls outside the
  viewport plus `PointCullMarginPx` are skipped before any other work. Line ops
  whose padded bounding box misses the cull rectangle are skipped before the
  native draw. Text drawing reuses `SKFont` and `SKPaint` objects for the
  length of a render; line drawing reuses one `SKPath`, one `SKPaint`, a point
  buffer and a dash-effect cache.
- **Rotated viewports.** `RenderOnto` takes an optional cull rectangle so a
  caller that rotates the canvas can widen it to the rotated viewport's
  bounding box.
- **Missing glyphs.** `DrawText` falls back per run of text: characters the
  primary typeface lacks are drawn with a face from
  `SKFontManager.MatchCharacter` instead of `.notdef` boxes.
