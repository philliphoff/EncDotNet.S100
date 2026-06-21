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
| `ResolvedSymbol`, `SymbolAsset` | Resolved point-symbol content + pivot (S-100 Part 9 §11.5). |
| `VectorSceneBuilder` | Lowers a `DrawingInstruction` display list into a `VectorScene`. Pattern tiles are supplied as PNG bytes through an injected `Func<string, byte[]?>` delegate, so the builder stays rasteriser-free. |
| `ColorResolver` | S-100 colour-token → `RgbaColor` resolution. |
| `ScaleVisibility` | S-100 Part 9 §11.1 scale-visibility semantics (SCAMIN inclusion). |
| `WebMercator` | Spherical EPSG:3857 forward projection (the `lat/lon → 3857` half of the S-100 Part 9 projection). |

## Who consumes it

- `EncDotNet.S100.Renderers.Skia` — headless rasteriser (`SkiaDisplayListRenderer`, `HeadlessVectorRenderer`).
- `EncDotNet.S100.Renderers.Mapsui` — Mapsui feature/style adapter.
- The tiled/async render subsystem (see `docs/design/S100-Render-Subsystem-Design.md`).

Because every backend consumes the same `VectorScene`, the IR is the A/B seam:
identical portrayal can be driven through different rendering backends for
apples-to-apples comparison.
