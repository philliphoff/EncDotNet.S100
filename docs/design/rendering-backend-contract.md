# Rendering-backend contract (the portrayal seam)

This document blesses and documents the **pluggable rendering-backend seam** in
EncDotNet.S100: the point at which a fully-resolved, renderer-neutral portrayal
scene is handed to a rendering backend. An embedder can implement a backend that
targets neither SkiaSharp nor Mapsui — a GPU rasteriser, a server-side raster
library, PDF, or SVG — by consuming exactly this contract.

The seam is **vector-only** today. The coverage side is discussed under
[Coverage: partially neutral](#coverage-partially-neutral-out-of-scope).

## The seam end-to-end

```
IDatasetProcessor
  └─ IVectorPortrayalSource.BuildVectorPortrayalAsync(context)
        → VectorPortrayalResult            (Mapsui-free portrayal snapshot)
             │                              sub-layers + palette + geometry/
             │                              symbol/line/fill resolver delegates
             ▼
        VectorSceneBuilder  (EncDotNet.S100.Rendering.Scene)
             │  lowers a Part 9 DrawingInstruction display list:
             │  • sorts into Part 9 draw order
             │  • resolves colours / symbols / line-styles / pattern tiles
             │  • converts sizes mm → display px (1 px = 0.32 mm)
             │  • projects geometry lat/lon → EPSG:3857
             ▼
        VectorScene { IReadOnlyList<PaintOp> }   ◄── the renderer-neutral IR
             │      PointPaintOp / LinePaintOp / AreaPaintOp /
             │      PatternAreaPaintOp / TextPaintOp
             ▼
        IVectorSceneRenderer<TSurface>.Render(surface, scene, viewport)
             ├─ Skia   : SkiaDisplayListRenderer : IVectorSceneRenderer<SKCanvas>
             └─ (yours): SvgSceneRenderer, PdfSceneRenderer, …
```

Everything above `VectorScene` is S-100 portrayal correctness; everything below
is backend-specific drawing. The IR is the contract between the two.

### The three types you target

| Type | Assembly | Role |
|---|---|---|
| `VectorScene` / `PaintOp` | `EncDotNet.S100.Rendering.Scene` | The IR: an ordered list of fully-resolved paint operations. |
| `IVectorSceneRenderer<TSurface>` | `EncDotNet.S100.Rendering.Scene` | The named backend contract — implement this to draw a scene onto your surface. |
| `WorldToScreen` | `EncDotNet.S100.Rendering.Scene` | The EPSG:3857 world → pixel affine derived from a `Viewport`. Backend-neutral (no Skia / Mapsui dependency). |

`EncDotNet.S100.Rendering.Scene` depends only on `EncDotNet.S100.Core` and
`EncDotNet.S100.Portrayals` — never on SkiaSharp, Mapsui, or any GUI framework —
so a backend that references it inherits no rendering-stack dependency.

## IR guarantees for alternate backends

These are the invariants a backend may rely on. They are enforced by
`VectorSceneBuilder`; see the XML docs on `PaintOp` for the authoritative
wording.

1. **World units are EPSG:3857 metres.** Every `World…` member on a `PaintOp`
   is already projected (the `lat/lon → EPSG:3857` half of the S-100 Part 9
   projection). Your backend supplies the second half — `EPSG:3857 → pixels` —
   via `WorldToScreen.Create(viewport).Project(world)` or an equivalent affine.

2. **Sizes are logical display pixels.** Stroke widths, dash lengths, offsets,
   font size, and symbol scale are already in display pixels under the Part 9
   §3.10.4 convention `1 px = 0.32 mm`. They are resolution-independent device
   pixels — **not** world metres and **not** physical output pixels. A backend
   that draws geometry through a world→screen transform **must not** pass size
   values through that transform; realise them in device pixels directly
   (reset/compensate the transform for stroke, symbol, and text realisation),
   otherwise sizes scale with zoom.

3. **Colours are resolved.** All colours are `RgbaColor` (`byte R, G, B, A`) —
   palette token lookup and transparency are already applied. Symbols are
   resolved to processed SVG (CSS classes inlined); pattern tiles are supplied
   as PNG bytes.

4. **Draw order is applied.** `VectorScene.Ops` is already in Part 9 draw order
   (areas → lines → points → text within ascending drawing priority, under-radar
   before over-radar). Draw the ops in list order.

5. **SCAMIN is carried per op.** Each `PaintOp` carries `ScaleMinimum` /
   `ScaleMaximum` as **scale denominators** (not backend resolutions). Apply the
   same inclusion semantics every backend uses with
   `ScaleVisibility.IsVisibleAtScale(op, viewport.ScaleDenominator)`; the Skia
   backend gates on this when `HonorScaleVisibility` is set.

## The two shipped backends as worked references

The two in-tree backends consume the same IR but illustrate two integration
shapes:

- **Skia — pull / synchronous** (`EncDotNet.S100.Renderers.Skia`).
  `SkiaDisplayListRenderer` implements `IVectorSceneRenderer<SKCanvas>`: given a
  scene and a viewport it draws every op onto the canvas on demand, projecting
  with `WorldToScreen` and realising sizes in device pixels. This is the
  reference shape for a new backend — it maps one-to-one onto the interface.

- **Mapsui — push / per-frame** (`EncDotNet.S100.Renderers.Mapsui`).
  `S100VectorSceneRenderer` binds a `VectorScene` to a Mapsui layer
  (`BindScene`) and renders on Mapsui's own per-frame schedule, letting the
  navigator perform the `EPSG:3857 → screen` projection. It is a **conforming
  consumer** of the IR rather than an implementer of
  `IVectorSceneRenderer<TSurface>`: its asynchronous, navigator-driven model
  does not fit a synchronous pull-render signature, and forcing it to would be a
  leaky fit. Implement the interface when your backend can draw a scene to a
  surface on demand; consume the IR directly (as Mapsui does) when it cannot.

## Backend-specific carry-over limits

- **North-up only.** `WorldToScreen` applies no rotation. Rotated-viewport
  support (keeping labels upright, etc.) is a backend concern layered on top and
  is not part of the IR.
- **Pattern-fill clipping (#192).** `PatternAreaPaintOp` carries a pre-rasterised
  PNG tile, but the IR does not carry the NetTopologySuite priority-clipping the
  Mapsui pattern phase performs. A backend that lowers patterns from the IR
  (as the headless Skia path does) may see lower-priority patterns bleed across
  opaque overlay areas. Acceptance per #192 is "as closely as practical".
- **Coverage is out of scope** (see next section).

## Coverage: partially neutral (out of scope)

The coverage side (`ICoveragePortrayalSource` → `CoveragePortrayalResult`) is
only **partially** neutralised: it carries `StyledCoverageLayer`, `Viewport`,
and pre-projected `PointGlyph`s rather than a `PaintOp`-style neutral op list.
There is no coverage IR equivalent to `VectorScene` yet, so coverage products
(S-102/S-104/S-111) are **not** part of the blessed backend contract in this
revision. A coverage IR is possible future work; it is intentionally not
introduced here.

## Writing your own backend

Implement `IVectorSceneRenderer<TSurface>` for your surface type, project with
`WorldToScreen`, and lower each `PaintOp` subtype. The sketch below is an
illustrative **SVG** backend that writes to a `TextWriter` and depends only on
`EncDotNet.S100.Rendering.Scene` — no SkiaSharp, no Mapsui. It is a
documentation snippet (not a shipped project); it shows the shape of the
lowering rather than every production edge case.

```csharp
using System.Globalization;
using EncDotNet.S100.Pipelines;             // Viewport, RgbaColor
using EncDotNet.S100.Rendering.Scene;       // VectorScene, PaintOp, WorldToScreen, ScaleVisibility

/// <summary>
/// An illustrative SVG rendering backend: lowers a renderer-neutral
/// <see cref="VectorScene"/> to an SVG document. Depends only on
/// EncDotNet.S100.Rendering.Scene — proof that the seam is backend-agnostic.
/// </summary>
public sealed class SvgSceneRenderer : IVectorSceneRenderer<TextWriter>
{
    public void Render(TextWriter svg, VectorScene scene, Viewport viewport)
    {
        var t = WorldToScreen.Create(viewport);
        double denom = viewport.ScaleDenominator;

        svg.Write($"<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        svg.Write($"width=\"{viewport.WidthPixels}\" height=\"{viewport.HeightPixels}\">");

        foreach (var op in scene.Ops)
        {
            // Guarantee 5: apply the same SCAMIN inclusion every backend uses.
            if (!ScaleVisibility.IsVisibleAtScale(op, denom))
                continue;

            switch (op)
            {
                case AreaPaintOp area:
                    // Geometry projects through the world→screen transform …
                    svg.Write($"<path d=\"{RingPath(area.WorldShell, t)}");
                    foreach (var hole in area.WorldHoles)
                        svg.Write($" {RingPath(hole, t)}");
                    // … but the outline WIDTH is a display-pixel size (guarantee 2):
                    // it is emitted as-is, NOT scaled by the transform.
                    svg.Write($"\" fill=\"{Rgb(area.Fill)}\" fill-rule=\"evenodd\" ");
                    svg.Write($"stroke=\"{Rgb(area.OutlineColor)}\" ");
                    svg.Write($"stroke-width=\"{Px(area.OutlineWidthPx)}\"/>");
                    break;

                case LinePaintOp line:
                    svg.Write($"<polyline points=\"{Points(line.World, t)}\" fill=\"none\" ");
                    svg.Write($"stroke=\"{Rgb(line.Color)}\" stroke-width=\"{Px(line.WidthPx)}\"");
                    if (line.DashArrayPx is { Count: > 0 } dash)
                        svg.Write($" stroke-dasharray=\"{string.Join(' ', dash)}\"");
                    svg.Write("/>");
                    break;

                case PointPaintOp point:
                    var (px, py) = t.Project(point.World);
                    // point.Symbol.ProcessedSvg is ready-to-embed SVG content;
                    // Symbol.Scale / pivot fractions place it. Fall back to a dot.
                    if (point.Symbol is { } sym)
                        EmbedSymbol(svg, sym, px, py, point.Rotation);
                    else
                        svg.Write($"<circle cx=\"{Px(px)}\" cy=\"{Px(py)}\" r=\"3\" " +
                                  $"fill=\"{Rgb(point.FallbackColor)}\"/>");
                    break;

                case TextPaintOp text:
                    var (tx, ty) = t.Project(text.World);
                    svg.Write($"<text x=\"{Px(tx + text.OffsetXpx)}\" y=\"{Px(ty + text.OffsetYpx)}\" ");
                    svg.Write($"font-size=\"{Px(text.FontSizePx)}\" fill=\"{Rgb(text.ForeColor)}\">");
                    svg.Write(System.Security.SecurityElement.Escape(text.Text));
                    svg.Write("</text>");
                    break;

                case PatternAreaPaintOp pattern:
                    // pattern.TilePng is renderer-neutral PNG bytes → a <pattern>
                    // referencing a data-URI <image>. (No priority-clipping — #192.)
                    EmitPatternFill(svg, pattern, t);
                    break;
            }
        }

        svg.Write("</svg>");
    }

    private static string Rgb(RgbaColor c) =>
        $"rgba({c.R},{c.G},{c.B},{(c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)})";

    private static string Px(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Points(IReadOnlyList<(double X, double Y)> ring, WorldToScreen t)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var w in ring)
        {
            var (x, y) = t.Project(w);
            sb.Append(Px(x)).Append(',').Append(Px(y)).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    private static string RingPath(IReadOnlyList<(double X, double Y)> ring, WorldToScreen t)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ring.Count; i++)
        {
            var (x, y) = t.Project(ring[i]);
            sb.Append(i == 0 ? 'M' : 'L').Append(Px(x)).Append(' ').Append(Px(y)).Append(' ');
        }
        return sb.Append('Z').ToString();
    }

    // EmbedSymbol / EmitPatternFill omitted for brevity: embed
    // sym.ProcessedSvg at the projected anchor (shifted by the pivot fractions
    // and scaled by sym.Scale in display px), and emit a <pattern> whose tile is
    // a base64 data-URI built from pattern.TilePng.
}
```

The parity that makes this possible is that `WorldToScreen` is now a
backend-neutral helper in `EncDotNet.S100.Rendering.Scene` (it was previously
private to the Skia renderer). See
`tests/EncDotNet.S100.Rendering.Scene.Tests/WorldToScreenTests.cs` for the
projection parity tests.
