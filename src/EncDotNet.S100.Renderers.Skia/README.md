# EncDotNet.S100.Renderers.Skia

Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps.

## Overview

This library renders S-100 coverage and vector data to SkiaSharp bitmaps. It handles pure rasterization without a map control. Key types include:

- **`SkiaCoverageRenderer`** — `ICoverageRenderer<SKBitmap>` implementation that maps coverage grid cells to pixel colors.
- **`SkiaSvgRasterizer`** — rasterizes SVG portrayal symbols to tiled pattern bitmaps.
- **`SkiaColorExtensions`** — helpers for converting between `RgbaColor` and `SKColor`.

### Shared vector rendering core (now `EncDotNet.S100.Rendering.Scene`)

The backend-agnostic **S-100 Part 9 vector rendering core** — `VectorScene`,
`PaintOp`, `VectorSceneBuilder`, `ColorResolver`, `ScaleVisibility`, and
`WebMercator` — has been promoted to its own neutral assembly,
[`EncDotNet.S100.Rendering.Scene`](../EncDotNet.S100.Rendering.Scene/README.md),
so every rendering backend (this library, `EncDotNet.S100.Renderers.Mapsui`, and
the tiled/async render subsystem) depends on the IR without diamonding through
`Renderers.Skia`. It lowers a display list into a resolved, projected
intermediate representation (IR) so the portrayal-correctness logic lives in
exactly one place:

- **`VectorSceneBuilder`** (in `Rendering.Scene`) — lowers a `DrawingInstruction`
  list + `IFeatureGeometryProvider` into an ordered `VectorScene` of `PaintOp`s:
  applies S-100 Part 9 draw ordering, colour/symbol/line-style resolution,
  mm→px conversion (`1 px = 0.32 mm`), text-anchor selection, and the
  `lat/lon → EPSG:3857` projection half.
- **`PaintOp` / `VectorScene`** (in `Rendering.Scene`) — the IR. `PaintOp`
  coordinates are EPSG:3857 metres; all sizes are logical display pixels
  (resolution-independent). See the `PaintOp` XML docs for the full unit contract.
- **`SkiaDisplayListRenderer`** (in this library, `…Renderers.Skia.Scene`) —
  `VectorScene` + `Viewport` → `SKBitmap`. The
  vector analogue of `SkiaCoverageRenderer`, suitable for a tile-serving web API
  with no Mapsui/GUI dependency. It supplies the second projection half
  (`EPSG:3857 → screen`) via a `Viewport`-derived affine. Parsed symbol pictures
  are cached process-wide (keyed by the resolved SVG), point/text ops whose
  anchor falls outside the viewport (plus `PointCullMarginPx`) are culled before
  any per-op work, and text drawing pools its `SKFont`/`SKPaint` per render —
  all three matter for the tiled subsystem's per-frame overlay, which replays
  this renderer live every frame. `RenderOnto` takes an optional cull rectangle
  so a caller that rotates the canvas can expand it to the rotated viewport's
  bounding box.
- **`WebMercator`** (in `Rendering.Scene`) — EPSG:3857 forward projection (matches
  Mapsui's `SphericalMercator.FromLonLat`; a parity test asserts agreement).
- **`ScaleVisibility`** (in `Rendering.Scene`) — shared S-100 Part 9 §11.1 scale-visibility rule.
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

