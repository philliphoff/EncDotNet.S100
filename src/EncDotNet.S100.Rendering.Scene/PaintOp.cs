using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// A backend-agnostic, fully-resolved S-100 Part 9 paint operation — the
/// intermediate representation (IR) produced by <see cref="VectorSceneBuilder"/>
/// and consumed by both the headless <c>SkiaDisplayListRenderer</c> and
/// the Mapsui display-list renderer.
/// </summary>
/// <remarks>
/// <para><b>Unit contract.</b> All coordinate values
/// (<c>World…</c> members) are in <b>EPSG:3857 Web-Mercator metres</b> — the
/// first half of the S-100 Part 9 projection (<c>lat/lon → EPSG:3857</c>).
/// The second half (<c>EPSG:3857 → screen pixels</c>) is intentionally left to
/// each backend: Mapsui performs it per-frame via its <c>Navigator</c>; the
/// Skia backend applies a <see cref="Viewport"/>-derived affine.</para>
/// <para>All <i>size</i> values (stroke widths, dash lengths, offsets, font
/// size, symbol scale) are already expressed in <b>logical display pixels</b>
/// under the S-100 Part 9 §3.10.4 convention <c>1 px = 0.32 mm</c>. They are
/// resolution-independent device pixels — <i>not</i> world metres and
/// <i>not</i> physical output pixels. A backend that draws geometry through a
/// world→screen canvas transform must realise strokes, symbols, and text in
/// device pixels (i.e. reset/compensate the transform for size realisation),
/// otherwise sizes would scale with zoom.</para>
/// <para>All colours are already resolved to <see cref="RgbaColor"/> (palette
/// token lookup + transparency applied). Resources (symbols, pattern tiles)
/// are resolved to processed content rather than catalogue names.</para>
/// </remarks>
public abstract class PaintOp
{
    /// <summary>
    /// Identifier of the originating dataset feature (the S-100 feature
    /// reference), carried through so backends can tag rendered output for
    /// pick / hit-testing.
    /// </summary>
    public required string FeatureReference { get; init; }

    /// <summary>
    /// Minimum display scale denominator at which this op is visible (the most
    /// zoomed-out limit; S-100 Part 9 §11.1). Null means no lower bound.
    /// Carried as a denominator (not a backend resolution) so each backend
    /// applies the same inclusion semantics via
    /// <see cref="ScaleVisibility.IsVisibleAtScale"/>.
    /// </summary>
    public double? ScaleMinimum { get; init; }

    /// <summary>
    /// Maximum display scale denominator at which this op is visible (the most
    /// zoomed-in limit; S-100 Part 9 §11.1). Null means no upper bound.
    /// </summary>
    public double? ScaleMaximum { get; init; }
}

/// <summary>
/// Resolved intrinsic metrics for a point symbol, carried in the IR so the
/// Skia backend does not re-derive symbol geometry independently of the
/// Mapsui backend (S-100 Part 9 §11.5 pivot placement).
/// </summary>
/// <param name="ProcessedSvg">
/// SVG content with CSS classes already resolved to inline attributes (output
/// of <c>SvgProcessor.Process</c>), ready to hand to a rasteriser.
/// </param>
/// <param name="Scale">
/// Final symbol scale factor to apply to the rasterised SVG (already folds in
/// the per-instruction scale, the renderer-global scale, and the legacy 0.6
/// nominal factor).
/// </param>
/// <param name="PivotRelativeX">
/// Pivot offset as a fraction of symbol width, +X = pivot left of centre
/// (S-100 Part 9 §11.5). Backends shift the symbol by this fraction so the
/// pivot — not the bounding-box centre — lands on the anchor.
/// </param>
/// <param name="PivotRelativeY">
/// Pivot offset as a fraction of symbol height, screen-space +Y = down.
/// </param>
public readonly record struct ResolvedSymbol(
    string ProcessedSvg,
    double Scale,
    double PivotRelativeX,
    double PivotRelativeY);

/// <summary>A resolved point-symbol placement.</summary>
public sealed class PointPaintOp : PaintOp
{
    /// <summary>Symbol anchor in EPSG:3857 metres.</summary>
    public required (double X, double Y) World { get; init; }

    /// <summary>
    /// The resolved SVG symbol, or <see langword="null"/> when no symbol could
    /// be resolved (the backend falls back to a coloured dot of
    /// <see cref="FallbackColor"/>).
    /// </summary>
    public ResolvedSymbol? Symbol { get; init; }

    /// <summary>
    /// Fallback dot colour used when <see cref="Symbol"/> is
    /// <see langword="null"/> (resolved from the symbol name heuristic).
    /// </summary>
    public RgbaColor FallbackColor { get; init; }

    /// <summary>Fallback-dot scale factor (used only when <see cref="Symbol"/> is null).</summary>
    public double FallbackScale { get; init; }

    /// <summary>Symbol rotation in degrees clockwise from north, or null for upright.</summary>
    public double? Rotation { get; init; }

    /// <summary>Local horizontal offset from the anchor, in display pixels.</summary>
    public double OffsetXpx { get; init; }

    /// <summary>Local vertical offset from the anchor, in display pixels (screen-space +Y = down).</summary>
    public double OffsetYpx { get; init; }
}

/// <summary>A resolved polyline stroke.</summary>
public sealed class LinePaintOp : PaintOp
{
    /// <summary>Projected polyline vertices in EPSG:3857 metres.</summary>
    public required IReadOnlyList<(double X, double Y)> World { get; init; }

    /// <summary>Resolved stroke colour.</summary>
    public RgbaColor Color { get; init; }

    /// <summary>Stroke width in display pixels (already clamped to a 1 px minimum).</summary>
    public double WidthPx { get; init; }

    /// <summary>
    /// Dash pattern as an alternating [on, off, …] array in display pixels, or
    /// <see langword="null"/> for a solid stroke (or when
    /// <see cref="DefaultDash"/> applies).
    /// </summary>
    public IReadOnlyList<float>? DashArrayPx { get; init; }

    /// <summary>
    /// When true and <see cref="DashArrayPx"/> is null, the stroke is dashed but
    /// no explicit S-100 dash array was available (it came from an external
    /// line-style's dash flag). Backends apply their built-in default dash:
    /// the Mapsui adapter sets <c>PenStyle.Dash</c>; the Skia backend uses a
    /// width-derived default.
    /// </summary>
    public bool DefaultDash { get; init; }
}

/// <summary>A resolved solid-colour area fill (with optional outline).</summary>
/// <remarks>
/// Tiled-symbol pattern fills are represented separately as
/// <see cref="PatternAreaPaintOp"/>. The Mapsui renderer still drives its own
/// pattern collection / priority-clip / insert phase (so it ignores
/// <see cref="PatternAreaPaintOp"/> in the IR) — only the headless Skia
/// backend lowers patterns through this IR today.
/// </remarks>
public sealed class AreaPaintOp : PaintOp
{
    /// <summary>Exterior ring in EPSG:3857 metres (closed or auto-closed by the backend).</summary>
    public required IReadOnlyList<(double X, double Y)> WorldShell { get; init; }

    /// <summary>Interior (hole) rings in EPSG:3857 metres.</summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> WorldHoles { get; init; } = [];

    /// <summary>Resolved fill colour (transparency already folded into the alpha channel).</summary>
    public RgbaColor Fill { get; init; }

    /// <summary>Resolved outline colour.</summary>
    public RgbaColor OutlineColor { get; init; }

    /// <summary>Outline width in display pixels.</summary>
    public double OutlineWidthPx { get; init; }
}

/// <summary>
/// A resolved tiled-symbol pattern area fill (S-100 Part 9 §11.3 area fills with
/// a referenced <c>AreaFill</c>). The op carries the pre-rasterised pattern
/// tile as PNG bytes and the pattern reference so backends can cache the
/// decoded image across ops sharing the same pattern.
/// </summary>
/// <remarks>
/// <para>The tile bytes are encoded as PNG — a renderer-neutral raster format
/// chosen to mirror the Mapsui pattern phase's existing
/// <c>SkiaSvgRasterizer.RasterizePatternTile</c> output. A future
/// non-Skia backend can decode the same bytes.</para>
/// <para>The headless Skia backend draws the tile via a repeat-tiled
/// <c>SKShader</c> anchored at world (0,0) projected to screen space, matching
/// the Mapsui <c>AnchoredPatternFillStyle</c> contract so the pattern is
/// seamless across overlapping polygons that share a global tile grid.</para>
/// <para><b>Priority clipping.</b> When <see cref="VectorSceneBuilder"/> lowers
/// pattern fills it priority-clips them via the shared
/// <see cref="PatternPriorityClipper"/> (S-100 Part 9 §11.3): a lower-priority
/// pattern op is clipped where a higher-priority pattern op — or an opaque
/// non-patterned solid colour fill (e.g. land) — covers it, so the geometry
/// carried here is already the visible portion. This is the identical clip the
/// Mapsui feature path applies, so the headless Skia backend and the Mapsui
/// TiledScene subsystem no longer bleed patterns across opaque overlay areas
/// (resolves the issue #192 "as closely as practical" caveat). A clipped group
/// can be a multi-polygon, so it is emitted as several ops (one shell + holes
/// each).</para>
/// </remarks>
public sealed class PatternAreaPaintOp : PaintOp
{
    /// <summary>The portrayal-catalogue area-fill name (e.g. <c>DIAMOND1</c>).</summary>
    public required string PatternReference { get; init; }

    /// <summary>Exterior ring in EPSG:3857 metres (closed or auto-closed by the backend).</summary>
    public required IReadOnlyList<(double X, double Y)> WorldShell { get; init; }

    /// <summary>Interior (hole) rings in EPSG:3857 metres.</summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> WorldHoles { get; init; } = [];

    /// <summary>The pre-rasterised pattern tile as PNG bytes.</summary>
    public required byte[] TilePng { get; init; }
}

/// <summary>A resolved text label. The anchor is already selected (Part 9 §11.4/§11.5).</summary>
public sealed class TextPaintOp : PaintOp
{
    /// <summary>Anchor position in EPSG:3857 metres.</summary>
    public required (double X, double Y) World { get; init; }

    /// <summary>The literal text to render.</summary>
    public required string Text { get; init; }

    /// <summary>Font size in display pixels (already folds in the renderer-global text scale).</summary>
    public double FontSizePx { get; init; }

    /// <summary>Resolved foreground colour (transparency already applied).</summary>
    public RgbaColor ForeColor { get; init; }

    /// <summary>Resolved background colour, or null when the label has no background box.</summary>
    public RgbaColor? BackColor { get; init; }

    /// <summary>Horizontal alignment relative to the anchor.</summary>
    public TextHorizontalAlignment HorizontalAlignment { get; init; } = TextHorizontalAlignment.Center;

    /// <summary>Vertical alignment relative to the anchor.</summary>
    public TextVerticalAlignment VerticalAlignment { get; init; } = TextVerticalAlignment.Center;

    /// <summary>Horizontal offset from the anchor, in display pixels.</summary>
    public double OffsetXpx { get; init; }

    /// <summary>Vertical offset from the anchor, in display pixels (screen-space +Y = down).</summary>
    public double OffsetYpx { get; init; }
}

/// <summary>
/// An ordered, backend-agnostic list of resolved <see cref="PaintOp"/>s — the
/// output of <see cref="VectorSceneBuilder"/>. Ops are already in S-100 Part 9
/// draw order (areas → lines → points → text within ascending drawing
/// priority, under-radar before over-radar).
/// </summary>
public sealed class VectorScene
{
    /// <summary>Creates a scene from an ordered op list.</summary>
    public VectorScene(IReadOnlyList<PaintOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);
        Ops = ops;
    }

    /// <summary>The resolved paint operations, in draw order.</summary>
    public IReadOnlyList<PaintOp> Ops { get; }
}
