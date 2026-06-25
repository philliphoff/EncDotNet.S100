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
/// Point symbols always draw and are <b>never</b> suppressed. Matching the
/// Mapsui "A" arm (the fidelity baseline for issue&#160;#347), they do
/// <i>not</i> displace labels either: decluttering operates among labels only.
/// Labels (feature-name text and soundings, both <see cref="TextPaintOp"/>)
/// yield only to higher-priority labels. The op list is already ordered by
/// ascending S-100 Part 9 drawing priority (later = drawn on top = more
/// important), so labels are placed highest-priority first; a label whose
/// on-screen footprint overlaps an already-placed <i>label</i> footprint is
/// suppressed.
/// </para>
/// <para>
/// Letting a point symbol suppress an overlapping label would drop annotation
/// text the "A" arm always draws — e.g. S-421 route action-point / leg labels
/// anchored on (or beside) a co-located waypoint symbol, which the coarse
/// perceptual gate does not catch. Point symbols are therefore obstacles to
/// nothing; only label-vs-label overlap is resolved here.
/// </para>
/// <para>
/// All footprints are computed in <i>final</i> on-screen space — each anchor is
/// rotated about the screen centre by the same angle the overlay's point pass
/// uses — so collision is correct under a rotated viewport. The result is
/// deterministic (a stable, priority-ordered greedy placement); unlike the
/// Mapsui "A" arm's non-deterministic declutter, the same scene and viewport
/// always suppress the same labels.
/// </para>
/// <para>
/// <b>Threading.</b> The reusable per-frame buffers (the suppressed set, the
/// screen-rect index and its bucket lists, and the text-layout scratch) are
/// <i>render-thread-confined</i>: a single instance is reused across frames and
/// <see cref="Declutter"/> <see cref="ScreenRectIndex.Clear"/>s them at entry
/// rather than reallocating, eliminating the per-frame allocation that scaled
/// with op count (~180&#160;KB at 1k ops, ~6&#160;MB at 50k). This makes the
/// type <b>non-reentrant</b> — it must only be called from the one render
/// thread (the tiled subsystem's <c>DrawOverlay</c>), never concurrently. The
/// tile-raster path uses its own per-call renderer/scratch and is unaffected.
/// </para>
/// </remarks>
public sealed class LabelDeclutterer : IDisposable
{
    // Render-thread-confined reusable buffers. Cleared (not reallocated) each
    // Declutter call so a dense overlay no longer allocates per frame. See the
    // threading note in the class remarks.
    private readonly HashSet<TextPaintOp> _suppressed = new();
    private readonly ScreenRectIndex _index = new();
    private readonly SkiaDisplayListRenderer.TextDrawScratch _scratch = new();

    /// <summary>
    /// Returns the set of <see cref="TextPaintOp"/>s to <b>suppress</b> this
    /// frame (by reference identity) so the live overlay draws a decluttered
    /// label plane. Point symbols are never suppressed and never displace a
    /// label (parity with the Mapsui arm) — only label-vs-label overlap is
    /// resolved. Ops that are scale-culled or fall outside
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
    /// <returns>
    /// The text ops to skip; empty when nothing collides. <b>The returned set is
    /// a render-thread-confined reusable buffer</b> — valid only until the next
    /// <see cref="Declutter"/> call on this instance. Callers that must retain it
    /// across frames should copy it.
    /// </returns>
    public IReadOnlySet<TextPaintOp> Declutter(
        VectorScene scene, Viewport viewport, SKRect cullBounds, bool honorScaleVisibility,
        double anchorRotationDegrees, float centerX, float centerY)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(viewport);

        // Reuse the render-thread-confined buffers: clear, don't reallocate.
        _suppressed.Clear();
        _index.Clear();
        var suppressed = _suppressed;
        var transform = WorldToScreen.Create(viewport);
        double denom = viewport.ScaleDenominator;
        var index = _index;

        // Labels, highest drawing priority first (reverse op order). A label that
        // collides with an already-placed label footprint is suppressed; otherwise
        // it claims its footprint and becomes an obstacle for lower-priority
        // labels. Point symbols are intentionally not indexed: they always draw
        // and never displace a label, matching the Mapsui "A" arm (issue #347).
        var scratch = _scratch;
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
    /// Disposes the held render-thread text-layout scratch (native paint/fonts).
    /// The tiled subsystem's shared instance lives for the process lifetime; this
    /// exists so unit tests (and any future per-layer owner) can release the
    /// native handles deterministically.
    /// </summary>
    public void Dispose() => _scratch.Dispose();

    /// <summary>
    /// A uniform screen-space grid of occupied label rectangles giving near-O(1)
    /// overlap queries, so decluttering thousands of label ops per frame stays
    /// linear. Rectangles are bucketed by the fixed-size cells they touch.
    /// </summary>
    /// <remarks>
    /// Reusable across frames: <see cref="Clear"/> recycles the bucket lists into
    /// a free pool rather than discarding them, so a steady-state overlay walk
    /// allocates nothing here after warm-up.
    /// </remarks>
    private sealed class ScreenRectIndex
    {
        private const float CellSize = 64f;
        private readonly Dictionary<(int Cx, int Cy), List<SKRect>> _cells = new();
        private readonly Stack<List<SKRect>> _pool = new();

        /// <summary>
        /// Recycles all bucket lists into the free pool and empties the grid,
        /// leaving the index ready for reuse without reallocation.
        /// </summary>
        public void Clear()
        {
            foreach (var list in _cells.Values)
            {
                list.Clear();
                _pool.Push(list);
            }
            _cells.Clear();
        }

        public void Add(SKRect rect)
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
                        list = _pool.Count > 0 ? _pool.Pop() : new List<SKRect>(4);
                        _cells[key] = list;
                    }
                    list.Add(rect);
                }
            }
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
    }
}
