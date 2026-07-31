# EncDotNet.S100.Rendering.Scene

The backend-agnostic **vector scene intermediate representation (IR)** for the
S-100 portrayal pipeline. This assembly holds the neutral seam where resolved
S-100 Part 9 portrayal output is handed to a rendering backend — it depends only
on `EncDotNet.S100.Core` and `EncDotNet.S100.Portrayals`, never on SkiaSharp,
Mapsui, or any GUI framework.

## What lives here

| Type | Role |
|---|---|
| `VectorScene` / `PaintOp` (+ `PointPaintOp`, `LinePaintOp`, `AreaPaintOp`, `PatternAreaPaintOp`, `TextPaintOp`) | Ordered list of fully-resolved paint operations — world coords in EPSG:3857 m, sizes in logical display px, colours resolved to `RgbaColor`, SCAMIN carried per-op. |
| `IVectorSceneRenderer<TSurface>` | The named **rendering-backend contract**: implement it to draw a `VectorScene` onto a backend-specific surface. `SkiaDisplayListRenderer` implements `IVectorSceneRenderer<SKCanvas>`; the Mapsui renderer is a conforming consumer of the IR rather than an implementer. |
| `WorldToScreen` | Backend-neutral EPSG:3857 world → pixel affine derived from a `Viewport` (the `EPSG:3857 → pixels` half of the Part 9 projection). Lets a Skia-free backend project the IR's world coordinates. Wraps ops across the ±180° antimeridian when the viewport is a seam-shifted frame (issue #413). |
| `SeamAwareBoundsAccumulator` | Computes a seam-aware EPSG:3857 auto-fit extent: detects geometry straddling the ±180° antimeridian (via a longitude-occupancy histogram + largest-empty-arc heuristic) and shifts the western cluster into a contiguous world-X window so dateline-spanning datasets are framed on their true extent (issue #413). |
| `ResolvedSymbol`, `SymbolAsset` | Resolved point-symbol content + pivot (S-100 Part 9 §11.5). |
| `VectorSceneBuilder` | Lowers a `DrawingInstruction` display list into a `VectorScene`. Pattern tiles are supplied as PNG bytes through an injected `Func<string, byte[]?>` delegate, so the builder stays rasteriser-free. When it lowers pattern fills it priority-clips them via `PatternPriorityClipper`. |
| `PatternPriorityClipper` | Backend-neutral NetTopologySuite priority-clipping of tiled pattern area fills (S-100 Part 9 §11.3): subtracts higher-priority pattern areas and opaque non-patterned solid fills (e.g. land) from each lower-priority pattern. Shared by both render paths (the Mapsui feature renderer delegates to it) so headless Skia, the Mapsui TiledScene subsystem, and the Mapsui feature path clip identically. |
| `PatternClipMemoizer` | Optional delegate a caller injects into `VectorSceneBuilder` to memoize the (palette-independent) pattern-clip result, so a re-build that only changes the palette reuses the geometry instead of repeating the overlay. The Mapsui renderer wires it to its pattern-clip cache for the default TiledScene subsystem. |
| `ColorResolver` | S-100 colour-token → `RgbaColor` resolution. |
| `ScaleVisibility` | S-100 Part 9 §11.1 scale-visibility semantics (SCAMIN inclusion). |
| `WebMercator` | Spherical EPSG:3857 forward projection (the `lat/lon → 3857` half of the S-100 Part 9 projection). |

## The rendering-backend seam

Because every backend consumes the same `VectorScene`, the IR is the blessed
**pluggable rendering-backend contract**: an embedder can implement
`IVectorSceneRenderer<TSurface>` (or consume the IR directly, as Mapsui does) to
render S-100 portrayal through a non-Skia/non-Mapsui backend — GPU, server-side
raster, PDF, SVG. See [`docs/design/rendering-backend-contract.md`](../../docs/design/rendering-backend-contract.md)
for the end-to-end seam, the IR guarantees, the two shipped backends as worked
references, and an illustrative SVG backend.

## Who consumes it

- `EncDotNet.S100.Renderers.Skia` — headless rasteriser (`SkiaDisplayListRenderer`, `HeadlessVectorRenderer`).
- `EncDotNet.S100.Renderers.Mapsui` — Mapsui feature/style adapter.
- The tiled/async render subsystem (see `docs/design/S100-Render-Subsystem-Design.md`).

Because every backend consumes the same `VectorScene`, the IR is the A/B seam:
identical portrayal can be driven through different rendering backends for
apples-to-apples comparison.

## Installation

```sh
dotnet add package EncDotNet.S100.Rendering.Scene
```

This package is Mapsui-free and GUI-free. Pair it with
[`EncDotNet.S100.Renderers.Skia`](../EncDotNet.S100.Renderers.Skia/README.md) to
rasterise a scene headlessly **without** taking the batteries-included
`EncDotNet.S100` facade. See the
[Embedding the renderer](https://github.com/philliphoff/EncDotNet.S100/blob/main/docs/embedding-the-renderer.md)
guide for the end-to-end path.

## Stability & versioning

The **stable, supported surface** of this package is the set of types documented
in [What lives here](#what-lives-here): `VectorScene`, the `PaintOp` hierarchy
(`PointPaintOp`, `LinePaintOp`, `AreaPaintOp`, `PatternAreaPaintOp`,
`TextPaintOp`), `VectorSceneBuilder`, `PatternPriorityClipper`,
`PatternClipMemoizer`, `ColorResolver`, `ScaleVisibility`, and `WebMercator`.
Types that are `internal` or undocumented are implementation detail and may
change at any time.

All `EncDotNet.S100.*` packages share **one version**, derived from the release
git tag (there is no per-package version). Versioning follows
[Semantic Versioning](https://semver.org/): once past `1.0.0`, a breaking change
to the stable surface above lands only in a **major** bump. While the version is
below `1.0.0`, the surface is still settling — breaking changes may occur in a
minor bump and will be called out in the release notes.
