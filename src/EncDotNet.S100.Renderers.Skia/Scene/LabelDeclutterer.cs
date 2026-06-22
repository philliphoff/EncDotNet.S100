using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Deterministic, priority-driven label declutter for the tiled subsystem's
/// live label plane. S-100 Part 9 makes overlap avoidance the portrayal
/// engine's responsibility (there is no per-label collision rule in a product's
/// Portrayal Catalogue), so the render subsystem resolves it here from the
/// drawing-priority and SCAMIN already carried on each <see cref="PaintOp"/>.
/// </summary>
/// <remarks>
/// <para>
/// Point symbols always draw and act as fixed obstacles; labels (feature-name
/// text and soundings, both <see cref="TextPaintOp"/>) yield to symbols and to
/// higher-priority labels. The op list is already ordered by ascending S-100
/// Part 9 drawing priority (later = drawn on top = more important), so labels
/// are placed highest-priority first; a label whose on-screen footprint
/// overlaps an already-placed footprint is suppressed.
/// </para>
/// <para>
/// All footprints are computed in <i>final</i> on-screen space — each anchor is
/// rotated about the screen centre by the same angle the overlay's point pass
/// uses — so collision is correct under a rotated viewport. The result is
/// deterministic (a stable, priority-ordered greedy placement); unlike the
/// Mapsui "A" arm's non-deterministic declutter, the same scene and viewport
/// always suppress the same labels.
/// </para>
/// </remarks>
public sealed class LabelDeclutterer
{
    /// <summary>
    /// Returns the set of <see cref="TextPaintOp"/>s to <b>suppress</b> this
    /// frame (by reference identity) so the live overlay draws a decluttered
    /// label plane. Point symbols are never suppressed; they reserve their
    /// footprint first as obstacles. Ops that are scale-culled or fall outside
    /// <paramref name="cullBounds"/> are ignored (they are not drawn, so they
    /// neither occupy space nor need suppressing).
    /// </summary>
    /// <param name="scene">The overlay scene (point + text ops).</param>
    /// <param name="viewport">The live viewport whose projection places anchors.</param>
    /// <param name="cullBounds">Screen-space rectangle outside which ops are not drawn.</param>
    /// <param name="honorScaleVisibility">Whether to apply S-100 Part 9 §11.1 SCAMIN culling.</param>
    /// <param name="anchorRotationDegrees">
    /// Degrees to rotate each anchor about (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) — matching the overlay's rotation — before
    /// building footprints. Zero for the north-up v1 case.
    /// </param>
    /// <param name="centerX">Screen-space X of the rotation centre.</param>
    /// <param name="centerY">Screen-space Y of the rotation centre.</param>
    /// <returns>The text ops to skip; empty when nothing collides.</returns>
    public IReadOnlySet<TextPaintOp> Declutter(
        VectorScene scene, Viewport viewport, SKRect cullBounds, bool honorScaleVisibility,
        double anchorRotationDegrees, float centerX, float centerY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(viewport);

        var suppressed = new HashSet<TextPaintOp>();
        var transform = WorldToScreen.Create(viewport);
        double denom = viewport.ScaleDenominator;
        var index = new ScreenRectIndex();

        // Pass 1 — point symbols reserve their footprints as obstacles.
        foreach (var op in scene.Ops)
        {
            if (op is not PointPaintOp point)
                continue;
            if (honorScaleVisibility && !ScaleVisibility.IsVisibleAtScale(point, denom))
                continue;

            var (px, py) = transform.Project(point.World);
            px += (float)point.OffsetXpx;
            py += (float)point.OffsetYpx;
            (px, py) = SkiaDisplayListRenderer.RotateAbout(px, py, centerX, centerY, anchorRotationDegrees);
            if (!cullBounds.Contains(px, py))
                continue;

            index.Add(SkiaDisplayListRenderer.PointScreenBounds(point, px, py));
        }

        // Pass 2 — labels, highest drawing priority first (reverse op order).
        // A label that collides with an already-placed footprint is suppressed;
        // otherwise it claims its footprint and becomes an obstacle for
        // lower-priority labels.
        using var scratch = new SkiaDisplayListRenderer.TextDrawScratch();
        for (int i = scene.Ops.Count - 1; i >= 0; i--)
        {
            if (scene.Ops[i] is not TextPaintOp text)
                continue;
            if (honorScaleVisibility && !ScaleVisibility.IsVisibleAtScale(text, denom))
                continue;

            var (ax, ay) = transform.Project(text.World);
            (ax, ay) = SkiaDisplayListRenderer.RotateAbout(ax, ay, centerX, centerY, anchorRotationDegrees);
            if (!cullBounds.Contains(ax + (float)text.OffsetXpx, ay + (float)text.OffsetYpx))
                continue;

            var font = scratch.FontFor((float)text.FontSizePx);
            var rect = SkiaDisplayListRenderer.LayoutText(text, ax, ay, font, scratch.Paint).Background;

            if (index.Intersects(rect))
                suppressed.Add(text);
            else
                index.Add(rect);
        }

        return suppressed;
    }

    /// <summary>
    /// A uniform screen-space grid of occupied rectangles giving near-O(1)
    /// overlap queries, so decluttering thousands of overlay ops per frame stays
    /// linear. Rectangles are bucketed by the fixed-size cells they touch.
    /// </summary>
    private sealed class ScreenRectIndex
    {
        private const float CellSize = 64f;
        private readonly Dictionary<(int Cx, int Cy), List<SKRect>> _cells = new();

        public void Add(SKRect rect)
        {
            ForEachCell(rect, (key, list) => list.Add(rect));
        }

        public bool Intersects(SKRect rect)
        {
            int minX = (int)Math.Floor(rect.Left / CellSize);
            int maxX = (int)Math.Floor(rect.Right / CellSize);
            int minY = (int)Math.Floor(rect.Top / CellSize);
            int maxY = (int)Math.Floor(rect.Bottom / CellSize);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    if (!_cells.TryGetValue((cx, cy), out var list))
                        continue;
                    foreach (var other in list)
                    {
                        if (rect.IntersectsWith(other))
                            return true;
                    }
                }
            }
            return false;
        }

        private void ForEachCell(SKRect rect, Action<(int, int), List<SKRect>> action)
        {
            int minX = (int)Math.Floor(rect.Left / CellSize);
            int maxX = (int)Math.Floor(rect.Right / CellSize);
            int minY = (int)Math.Floor(rect.Top / CellSize);
            int maxY = (int)Math.Floor(rect.Bottom / CellSize);

            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    var key = (cx, cy);
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<SKRect>(2);
                        _cells[key] = list;
                    }
                    action(key, list);
                }
            }
        }
    }
}
