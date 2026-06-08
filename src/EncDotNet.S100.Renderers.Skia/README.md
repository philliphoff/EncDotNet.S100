# EncDotNet.S100.Renderers.Skia

Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps.

## Overview

This library renders S-100 coverage and vector data to SkiaSharp bitmaps. It handles pure rasterization without a map control. Key types include:

- **`SkiaCoverageRenderer`** — `ICoverageRenderer<SKBitmap>` implementation that maps coverage grid cells to pixel colors.
- **`SkiaSvgRasterizer`** — rasterizes SVG portrayal symbols to tiled pattern bitmaps.
- **`SkiaColorExtensions`** — helpers for converting between `RgbaColor` and `SKColor`.

### Shared vector rendering core (`Scene` namespace)

The `EncDotNet.S100.Renderers.Skia.Scene` namespace hosts a **backend-agnostic
S-100 Part 9 vector rendering core** consumed by both this library's headless
renderer and `EncDotNet.S100.Renderers.Mapsui`. It lowers a display list into a
resolved, projected intermediate representation (IR) so the portrayal-correctness
logic lives in exactly one place:

- **`VectorSceneBuilder`** — lowers a `DrawingInstruction` list +
  `IFeatureGeometryProvider` into an ordered `VectorScene` of `PaintOp`s:
  applies S-100 Part 9 draw ordering, colour/symbol/line-style resolution,
  mm→px conversion (`1 px = 0.32 mm`), text-anchor selection, and the
  `lat/lon → EPSG:3857` projection half.
- **`PaintOp` / `VectorScene`** — the IR. `PaintOp` coordinates are EPSG:3857
  metres; all sizes are logical display pixels (resolution-independent). See the
  `PaintOp` XML docs for the full unit contract.
- **`SkiaDisplayListRenderer`** — `VectorScene` + `Viewport` → `SKBitmap`. The
  vector analogue of `SkiaCoverageRenderer`, suitable for a tile-serving web API
  with no Mapsui/GUI dependency. It supplies the second projection half
  (`EPSG:3857 → screen`) via a `Viewport`-derived affine.
- **`WebMercator`** — EPSG:3857 forward projection (matches Mapsui's
  `SphericalMercator.FromLonLat`; a parity test asserts agreement).
- **`ScaleVisibility`** — shared S-100 Part 9 §11.1 scale-visibility rule.
- **`ColorResolver`** — S-100 colour-token resolution (palette + inline hex).

**Scope (spike):** the IR currently covers point, line, solid-area, and text
ops. Pattern fills, antimeridian crossing, and Web-Mercator pole limits are not
yet represented in the IR — pattern fills remain handled by the Mapsui renderer's
dedicated pattern collection / priority-clip / insert phase.

## Installation

```sh
dotnet add package EncDotNet.S100.Renderers.Skia
```

## Linux arm64 native dependency

When you publish a **`linux-arm64`** executable that uses this renderer, reference
the self-contained SkiaSharp native in **your application project**:

```xml
<!-- In your app's .csproj -->
<ItemGroup>
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" ExcludeAssets="all" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" />
</ItemGroup>
```

The regular `SkiaSharp.NativeAssets.Linux` arm64 `libSkiaSharp.so` declares
undefined `uuid_*` / `FT_Get_BDF_Property` symbols that abort once
`fontconfig`/`freetype` load on a normal arm64 host, so any render path crashes
with `undefined symbol: …`. The `…NoDependencies` build is self-contained and
renders on both x64 and arm64. Native RID asset selection belongs to the final
executable, so this library does **not** apply the swap to your build. See
[issue #23](https://github.com/philliphoff/EncDotNet.S100/issues/23).

Text labels render without any system font infrastructure: this package embeds an
Open Sans face (Apache-2.0) used as a fallback when the host exposes no usable
system font (e.g. the `NoDependencies` native on a box without `fontconfig`).

