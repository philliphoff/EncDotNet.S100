# Render images and map tiles

This guide renders S-100 data to images without an interactive map: map tiles
for a web map or another mapping library, images for reports, or frames for a
batch job. To show S-100 data on a Mapsui map instead, see
[Add S-100 data to a Mapsui app](mapsui-app.md).

There are two levels to work at:

| Start from | Use | Package |
|---|---|---|
| Dataset files | `PngS100DatasetRenderer`: open, portray and render in one call | [`EncDotNet.S100`](../src/EncDotNet.S100/README.md) |
| A portrayal display list you already have | `SkiaDisplayListRenderer` and the scene model | [`EncDotNet.S100.Renderers.Skia`](../src/EncDotNet.S100.Renderers.Skia/README.md) |

Most hosts should start from dataset files.

## Render a map tile

`PngS100DatasetRenderer` draws one or more datasets into any viewport you give
it. To serve tiles in the usual XYZ (slippy map) scheme, convert each tile
address to a `Viewport` and render with a transparent background so tiles
overlay your basemap.

```bash
dotnet add package EncDotNet.S100
```

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Pipelines;

using var chart = S100Dataset.Open("path/to/cell.000");
using var renderer = new PngS100DatasetRenderer();

byte[] png = await RenderTileAsync(chart, z: 12, x: 655, y: 1430);
File.WriteAllBytes("12-655-1430.png", png);

Task<byte[]> RenderTileAsync(S100Dataset dataset, int z, int x, int y) =>
    renderer.RenderAsync(
        new[] { new S100Layer { Dataset = dataset } },
        new S100CompositeOptions
        {
            Viewport = TileViewport(z, x, y),
            Background = RgbaColor.Transparent,
        });

static Viewport TileViewport(int z, int x, int y, int size = 256)
{
    double n = Math.Pow(2, z);
    double Lon(int tx) => tx / n * 360.0 - 180.0;
    double Lat(int ty) => Math.Atan(Math.Sinh(Math.PI * (1 - 2 * ty / n))) * 180.0 / Math.PI;

    double north = Lat(y), south = Lat(y + 1);
    double midLatitude = (north + south) / 2 * Math.PI / 180.0;

    // Ground metres per pixel at the tile's centre, over the S-100 standard
    // pixel size of 0.28 mm, gives the display scale.
    double metresPerPixel = 2 * Math.PI * 6378137 / (size * n) * Math.Cos(midLatitude);

    return new Viewport
    {
        MinLongitude = Lon(x),
        MaxLongitude = Lon(x + 1),
        MinLatitude = south,
        MaxLatitude = north,
        WidthPixels = size,
        HeightPixels = size,
        ScaleDenominator = metresPerPixel / 0.00028,
    };
}
```

- Open each `S100Dataset` once and reuse it, and the renderer, for every tile.
  The renderer keeps the bundled catalogues loaded between calls. Portrayal
  still runs for each tile, so cache the PNGs you serve.
- Each tile is drawn on its own, so a label or symbol that crosses a tile edge
  is cut off. To avoid that, render a larger area, such as 512 × 512 pixels
  covering 2 × 2 tiles, and split it into tiles.
- To layer several products in one tile, such as an S-101 chart under S-102
  bathymetry, pass several `S100Layer`s. They're ordered and combined with the
  same S-98 interoperability rules as the viewer.
- `S100CompositeOptions` also sets the palette (`Palette`), symbol and text
  scale, the time step for S-104 and S-111, and the mariner settings, such as
  the safety contour.
- A tile outside the dataset's extent renders as a transparent image.

### Render one image of a whole dataset

Without a `Viewport`, the renderer fits the image to the data:

```csharp
byte[] png = await renderer.RenderAsync(chart, new S100RendererOptions
{
    Width = 2048,
    Height = 1536,
});
```

For all the options, see the
[`EncDotNet.S100` README](../src/EncDotNet.S100/README.md#render-options).
The [`s100 render`](cli.md#render) command does the same from a terminal,
including `--bbox` for an explicit area.

## Render a display list

If you run portrayal yourself, or produce drawing instructions some other way,
render them with the Skia renderer directly:

```bash
dotnet add package EncDotNet.S100.Renderers.Skia
```

Rendering has two steps. `VectorSceneBuilder` turns a `DrawingInstruction`
display list (the S-100 Part 9 portrayal output) into a `VectorScene`: paint
operations in EPSG:3857 metres with colours resolved. `SkiaDisplayListRenderer`
then draws the scene into a `Viewport`:

```csharp
using EncDotNet.S100.Pipelines;
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
    HonorScaleVisibility = true,
};

// Allocate a bitmap for the viewport...
using SKBitmap tile = renderer.Render(scene, viewport);

// ...or draw onto a canvas you own, for example to composite layers.
renderer.RenderOnto(canvas, scene, viewport);
```

Build the scene once and draw it into as many viewports as you need.

`HonorScaleVisibility` hides features outside their display-scale range, using
the viewport's `ScaleDenominator`. Leave it on for tiles. Turn it off when you
fit the viewport to the data's extent: that scale isn't a real display scale,
so features would be hidden that shouldn't be. If an image comes out blank or
missing detail, check the viewport and this setting first.

To render a display list fitted to its own extent in one call, use
`HeadlessVectorRenderer.Render`, which builds the scene and the viewport for
you.

### Coverage and composite images

- **Coverage products** (S-102, S-104 and S-111) don't produce a display list.
  Draw them with `CoverageHeadlessRenderer`, or `SkiaCoverageRenderer` for a
  cell-by-cell `ICoverageRenderer<SKBitmap>`.
- **Several layers in one image:** wrap each scene or coverage as a
  `VectorCompositeLayer` or `CoverageCompositeLayer` and draw the ordered stack
  with `HeadlessCompositeRenderer`. It draws the stack it's given; deciding the
  order between products is up to you. See
  [S-98 interoperability](design/s98-interoperability.md).

## Publish for Linux arm64

A `linux-arm64` app that uses the Skia renderer, including through
`EncDotNet.S100`, needs a different SkiaSharp native library. See
[Linux arm64 native dependency](../src/EncDotNet.S100.Renderers.Skia/README.md#linux-arm64-native-dependency).

## See also

- [Add S-100 data to a Mapsui app](mapsui-app.md): interactive maps.
- [Rendering.Scene README](../src/EncDotNet.S100.Rendering.Scene/README.md):
  the scene model, for writing your own rendering backend.
- [Command-line rendering](cli.md): render with `s100` instead of code.
