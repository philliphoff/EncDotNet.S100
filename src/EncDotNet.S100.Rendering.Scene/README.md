# EncDotNet.S100.Rendering.Scene

`EncDotNet.S100.Rendering.Scene` holds the vector scene, the backend-neutral
intermediate representation (IR) that sits between S-100 Part 9 portrayal and a
rendering backend. It lowers a portrayal display list into an ordered list of
fully resolved paint operations that any backend can draw. Reference it when
you write your own rendering backend, or when you build or inspect scenes
without depending on SkiaSharp, Mapsui or a UI framework. It depends only on
`EncDotNet.S100.Core` and `EncDotNet.S100.Portrayals`.

To rasterise a scene, pair it with
[`EncDotNet.S100.Renderers.Skia`](../EncDotNet.S100.Renderers.Skia/README.md).
For the end-to-end path without the `EncDotNet.S100` facade, see
[Embedding the renderer](../../docs/embedding-the-renderer.md).

## Install

```bash
dotnet add package EncDotNet.S100.Rendering.Scene
```

## Lower a display list to a scene

`VectorSceneBuilder` turns a `DrawingInstruction` display list and the matching
feature geometry into a `VectorScene`. `ResolveColor` is required; the symbol,
line-style and pattern resolvers are optional.

```csharp
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;

static VectorScene Lower(
    IReadOnlyList<DrawingInstruction> instructions,
    IFeatureGeometryProvider geometryProvider,
    ColorPalette palette)
{
    var builder = new VectorSceneBuilder
    {
        ResolveColor = ColorResolver.Create(palette),
    };

    return builder.Build(instructions, geometryProvider);
}
```

Each op in `scene.Ops` is in S-100 Part 9 draw order. Coordinates are EPSG:3857
metres, sizes are logical display pixels (1 px = 0.32 mm), and colours are
resolved to `RgbaColor`. The XML docs on `PaintOp` give the full unit contract.

## Main types

| Type | Role |
|---|---|
| `VectorScene`, `PaintOp` (`PointPaintOp`, `LinePaintOp`, `AreaPaintOp`, `PatternAreaPaintOp`, `TextPaintOp`) | The ordered list of resolved paint operations. Each op carries its own scale minimum (SCAMIN). |
| `VectorSceneBuilder` | Lowers a `DrawingInstruction` display list into a `VectorScene`. Pattern tiles come in as PNG bytes through the `PatternResolver` delegate (`Func<string, byte[]?>`), so the builder doesn't rasterise anything itself. Pattern fills are priority-clipped with `PatternPriorityClipper`. |
| `IVectorSceneRenderer<TSurface>` | The contract a rendering backend implements to draw a `VectorScene` onto its own surface type. |
| `WorldToScreen` | The EPSG:3857-to-pixel transform for a `Viewport`. When the viewport is shifted across the ±180° antimeridian, it wraps ops into that window. |
| `SeamAwareBoundsAccumulator` | Computes the extent to fit a dataset to. For geometry that straddles the antimeridian, it shifts the western part east so the dataset is framed on its true extent instead of the whole world. |
| `RotatedViewport` | Geometry for drawing a rotated viewport as a north-up cover that's then rotated onto the output. |
| `ResolvedSymbol`, `SymbolAsset` | A resolved point symbol and its pivot (S-100 Part 9 §11.5). |
| `PatternPriorityClipper` | Clips tiled pattern area fills by display priority (S-100 Part 9 §11.3). It subtracts higher-priority pattern areas and opaque solid fills, such as land, from each lower-priority pattern. |
| `PatternClipMemoizer` | An optional delegate you pass to `VectorSceneBuilder.PatternClipCache` to reuse the clip result. The clip doesn't depend on the palette, so a rebuild that only changes the palette can skip it. |
| `CoverageSymbolField` | The oriented symbols of a styled coverage grid, such as S-111 current arrows, flattened to EPSG:3857 arrays so every backend places, sizes and thins them the same way. |
| `ColorResolver` | Resolves S-100 colour tokens to `RgbaColor`. |
| `ScaleVisibility` | S-100 Part 9 §11.1 scale-visibility rules (SCAMIN inclusion). |
| `WebMercator` | The spherical EPSG:3857 forward projection (latitude and longitude to EPSG:3857). |
| `CoverageOverlap`, `CoverageOverlapCell`, `FinerCoverage` | Cross-cell overlap suppression: which finer, overlapping cells hide a coarser cell under their data coverage, and the zoom past which each stops hiding. `CoverageOverlap.ToWebMercator` turns a dataset's `CoverageArea`s into one EPSG:3857 footprint. SoundCharts and the headless compositor rank cells this way. |
| `HiddenCoverageCache`, `HiddenCoverage` | Whether a rectangle lies wholly inside one active finer coverage, so a backend can skip drawing what its coverage clip would erase. |

## Write a rendering backend

Every backend consumes the same `VectorScene`, so you can render S-100
portrayal through a backend other than Skia or Mapsui, such as a GPU, PDF or SVG
renderer. Implement `IVectorSceneRenderer<TSurface>` when your backend can draw
a scene to a surface on demand. Project op coordinates with `WorldToScreen`, and
apply sizes such as stroke width and font size directly in display pixels.

The two shipped backends show both approaches:

- `EncDotNet.S100.Renderers.Skia` implements the interface:
  `SkiaDisplayListRenderer` is an `IVectorSceneRenderer<SKCanvas>`.
- `EncDotNet.S100.Renderers.Mapsui` binds a scene to a map layer and draws it on
  its own per-frame schedule. It consumes the IR without implementing the
  interface.

Because both backends draw the same scene, you can also compare backends on
identical portrayal. For the guarantees the IR makes and a sample SVG backend,
see the [rendering-backend contract](../../docs/design/rendering-backend-contract.md)
design note.

## Stability and versioning

The supported surface of this package is `VectorScene`, the `PaintOp`
hierarchy, `VectorSceneBuilder`, `PatternPriorityClipper`, `PatternClipMemoizer`,
`ColorResolver`, `ScaleVisibility` and `WebMercator`. Internal and undocumented
types can change at any time.

All `EncDotNet.S100.*` packages share one version, taken from the release git
tag. Versioning follows [Semantic Versioning](https://semver.org/): from `1.0.0`
on, a breaking change to the supported surface comes only in a major version.
Below `1.0.0`, a minor version can include breaking changes, and the release
notes list them.
