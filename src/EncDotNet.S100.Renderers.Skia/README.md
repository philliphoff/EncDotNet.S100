# EncDotNet.S100.Renderers.Skia

Coverage and vector rendering to [SkiaSharp](https://github.com/mono/SkiaSharp) bitmaps.

## Overview

This library renders S-100 coverage and vector data to SkiaSharp bitmaps. It handles pure rasterization without a map control. Key types include:

- **`SkiaCoverageRenderer`** — `ICoverageRenderer<SKBitmap>` implementation that maps coverage grid cells to pixel colors.
- **`SkiaSvgRasterizer`** — rasterizes SVG portrayal symbols to tiled pattern bitmaps.

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
  bounding box, plus an `OverlayDrawOptions` overload that adds the live
  Label-plane behaviour: a suppressed-text set (from `LabelDeclutterer`), a
  screen-space text anchor-rotation that keeps glyphs **upright** under a rotated
  viewport while pinning the anchor to its feature, point/text draw filters, and
  per-run glyph fallback in `DrawText` (so codepoints the primary face lacks are
  drawn via `SKFontManager.MatchCharacter` instead of `.notdef` tofu boxes).
- **`LabelDeclutterer`** (`…Renderers.Skia.Scene`) — deterministic,
  priority-driven S-100 Part 9 overlap avoidance for the live label plane. Given
  a `VectorScene` it returns the set of `TextPaintOp`s to suppress: point symbols
  reserve their screen footprints first, then text is placed highest-priority-first
  against a uniform screen-bucket index, with labels yielding to symbols and to
  higher-priority labels. Pure and machine-independent.
- **`OverlayDrawOptions`** (`…Renderers.Skia.Scene`) — the options bag for the
  `RenderOnto` overlay pass (cull bounds, suppressed text, text anchor-rotation +
  screen centre, and point/text draw filters).
- **`WebMercator`** (in `Rendering.Scene`) — EPSG:3857 forward projection (matches
  Mapsui's `SphericalMercator.FromLonLat`; a parity test asserts agreement).
- **`ScaleVisibility`** (in `Rendering.Scene`) — shared S-100 Part 9 §11.1 scale-visibility rule.
- **`ColorResolver`** — S-100 colour-token resolution (palette + inline hex).

**Scope (spike):** the IR currently covers point, line, solid-area, and text
ops. Pattern fills and Web-Mercator pole limits are not yet represented in the
IR — pattern fills remain handled by the Mapsui renderer's dedicated pattern
collection / priority-clip / insert phase. Antimeridian (±180°) crossing **is**
handled for the headless auto-fit: `SeamAwareBoundsAccumulator` (in
`Rendering.Scene`) frames dateline-spanning datasets on their true extent and
`WorldToScreen` wraps ops into the shifted window at draw time (issue #413).
The seam-wrap is opt-out via `SkiaDisplayListRenderer.EnableSeamWrap` /
`WorldToScreen.Create(viewport, allowSeamWrap)`: the Mapsui **tiled** subsystem
disables it because it rasterises already-continuous geometry from narrow
per-tile viewports, where wrapping would teleport off-tile vertices of large
polygons back across the world (see the tiled renderer's `RasterizeTile`).

### Headless rendering & compositing (`…Renderers.Skia.Scene`)

Standalone, Mapsui-free entry points that rasterise a whole dataset (or several)
to an `SKBitmap`:

- **`HeadlessVectorRenderer`** — lowers a Part 9 display list to a `VectorScene`
  and rasterises it, auto-fitting the viewport to the scene extent (seam-aware
  across the ±180° antimeridian via `TryGetSeamAwareWorldBounds`). Its
  `BuildScene(...)` and `TryGetWorldBounds(...)` seams are reused by the
  compositor to lower a sub-layer and union its bounds against a *shared*
  viewport.
- **`CoverageHeadlessRenderer`** — rasterises a `StyledCoverageLayer` (S-102/104/111).
  `Render(...)` auto-fits; `DrawOnto(canvas, sharedViewport, layer, w,e,s,n)`
  projects the grid (and arrows) into a shared viewport's pixel space so coverage
  registers with vector layers in a composite.
- **`CompositeLayer`** — an ordered draw unit painted against one explicit
  `Viewport`: `VectorCompositeLayer` (draws a `VectorScene` via
  `SkiaDisplayListRenderer.RenderOnto`) and `CoverageCompositeLayer` (draws via
  `CoverageHeadlessRenderer.DrawOnto`), both on a transparent background so they
  layer.
- **`HeadlessCompositeRenderer`** — clears the background once, then paints an
  ordered `IReadOnlyList<CompositeLayer>` against the shared viewport. The
  cross-dataset ordering / suppression *decision* is made upstream by the S-98
  engine in `EncDotNet.S100.Datasets.Pipelines` (`HeadlessCompositor`); this
  renderer only paints the resolved stack.
- **`NaturalEarthBasemap`** — the bundled, offline, public-domain **Natural
  Earth 1:10m land** basemap (issues #295, #411) as a single, Mapsui-free source
  of land geometry. The embedded GeoJSON (`Assets/Basemap/ne_10m_land.geojson`)
  is parsed once and each ring projected `lon/lat → EPSG:3857` via `WebMercator`.
  `LandPolygons` exposes the world-metre rings; `LandScene` is a cached,
  viewport-independent `VectorScene` of parchment-filled (`238,232,220`)
  `AreaPaintOp`s. Both the headless render paths (`HeadlessVectorRenderer.Render`
  and `CoverageHeadlessRenderer.Render` take a `BasemapKind`; `HeadlessCompositor`
  prepends the land scene) and the interactive Avalonia viewer's offline basemap
  consume this same asset, so land is never duplicated.

## Installation

```sh
dotnet add package EncDotNet.S100.Renderers.Skia
```

This pulls in [`EncDotNet.S100.Rendering.Scene`](../EncDotNet.S100.Rendering.Scene/README.md)
(the scene IR) transitively. Together they let you **embed just the renderer +
IR** — build or lower a `VectorScene` and rasterise it headlessly — without the
batteries-included `EncDotNet.S100` facade or any Mapsui/GUI dependency. See the
[Embedding the renderer](https://github.com/philliphoff/EncDotNet.S100/blob/main/docs/embedding-the-renderer.md)
guide for the end-to-end path.

## Stability & versioning

The **stable, supported surface** of this package is the headless rendering
entry points: `SkiaDisplayListRenderer` (incl. its `RenderOnto` overloads),
`HeadlessVectorRenderer`, `CoverageHeadlessRenderer`, `HeadlessCompositeRenderer`,
the `CompositeLayer` family (`VectorCompositeLayer`, `CoverageCompositeLayer`),
`OverlayDrawOptions`, `LabelDeclutterer`, `SkiaCoverageRenderer`,
`SkiaCoverageArrowRenderer`, and `SkiaSvgRasterizer`. Types that are `internal`
or undocumented (e.g. colour/font helpers, diagnostics) are implementation
detail and may change at any time.

All `EncDotNet.S100.*` packages share **one version**, derived from the release
git tag (there is no per-package version). Versioning follows
[Semantic Versioning](https://semver.org/): once past `1.0.0`, a breaking change
to the stable surface above lands only in a **major** bump. While the version is
below `1.0.0`, the surface is still settling — breaking changes may occur in a
minor bump and will be called out in the release notes.

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

