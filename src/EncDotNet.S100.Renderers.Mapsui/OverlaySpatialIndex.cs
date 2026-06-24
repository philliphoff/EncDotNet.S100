using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A uniform-grid spatial index over a live overlay scene's point/text anchors
/// (EPSG:3857 metres), built <b>once</b> when a scene is bound so the per-frame
/// <see cref="S100VectorTileRenderer.DrawOverlay"/> walk can be scoped to the
/// ops near the viewport instead of the whole cell (#332 lever b).
/// </summary>
/// <remarks>
/// <para>
/// Each overlay op (<see cref="PointPaintOp"/> / <see cref="TextPaintOp"/>) is
/// anchored at a single world point, so it lands in exactly one grid cell — a
/// query therefore returns each candidate at most once. <see cref="Query"/>
/// returns candidates in <b>original op order</b> (ascending op index), which is
/// S-100 Part 9 ascending drawing priority, so the scoped scene declutters and
/// draws with the identical z-order the whole-cell scene would.
/// </para>
/// <para>
/// The query is a deliberate <i>conservative superset</i>: the caller passes the
/// world AABB of the (rotated, margin-inflated) viewport footprint, and the
/// downstream declutter / draw passes still apply the exact per-op screen cull —
/// so scoping never drops a feature that would otherwise be drawn (fidelity
/// neutral), it only bounds the walk. The index holds no mutable per-frame state
/// and is safe to query repeatedly from the render thread.
/// </para>
/// </remarks>
internal sealed class OverlaySpatialIndex
{
    private readonly IReadOnlyList<PaintOp> _ops;
    private readonly double _minX;
    private readonly double _minY;
    private readonly double _invCellW;
    private readonly double _invCellH;
    private readonly int _cols;
    private readonly int _rows;
    private readonly List<int>[] _cells;

    /// <summary>The overlay ops, in original draw order.</summary>
    public IReadOnlyList<PaintOp> Ops => _ops;

    /// <summary>
    /// The largest absolute pixel offset (<c>OffsetXpx</c>/<c>OffsetYpx</c>) of
    /// any overlay op. A query must inflate its world bounds by this (times the
    /// resolution) because the per-op screen cull tests the anchor <i>plus</i> its
    /// offset, while the index keys on the anchor alone.
    /// </summary>
    public double MaxOffsetPx { get; }

    /// <summary>Builds the index over <paramref name="overlay"/>'s point/text ops.</summary>
    public OverlaySpatialIndex(VectorScene overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _ops = overlay.Ops;

        // World bounds of all anchors + the largest op offset.
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        double maxOffset = 0;
        int n = 0;
        foreach (var op in _ops)
        {
            if (!TryAnchor(op, out var x, out var y, out var offX, out var offY))
                continue;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            var off = Math.Max(Math.Abs(offX), Math.Abs(offY));
            if (off > maxOffset) maxOffset = off;
            n++;
        }
        MaxOffsetPx = maxOffset;

        if (n == 0)
        {
            // Degenerate: no anchored ops. A 1×1 grid that matches nothing.
            _cols = _rows = 1;
            _cells = new List<int>[1] { new() };
            _minX = _minY = 0;
            _invCellW = _invCellH = 0;
            return;
        }

        // Aim for ~1 op per cell on average, capped so a pathological cluster
        // can't build a huge grid. A zero-width/height extent collapses that axis
        // to a single column/row.
        int target = Math.Clamp((int)Math.Ceiling(Math.Sqrt(n)), 1, 256);
        _cols = (maxX > minX) ? target : 1;
        _rows = (maxY > minY) ? target : 1;
        _minX = minX;
        _minY = minY;
        double width = Math.Max(maxX - minX, double.Epsilon);
        double height = Math.Max(maxY - minY, double.Epsilon);
        // Nudge the divisor so the max coordinate maps to the last cell, not one past.
        _invCellW = _cols / (width * (1 + 1e-9));
        _invCellH = _rows / (height * (1 + 1e-9));

        _cells = new List<int>[_cols * _rows];
        for (int i = 0; i < _ops.Count; i++)
        {
            if (!TryAnchor(_ops[i], out var x, out var y, out _, out _))
                continue;
            int c = CellIndex(x, y);
            (_cells[c] ??= new List<int>()).Add(i);
        }
    }

    /// <summary>
    /// Fills <paramref name="results"/> (cleared first) with the ops whose anchor
    /// falls in the world AABB, in ascending op order. <paramref name="scratch"/>
    /// is a caller-owned reusable buffer for the gathered indices (cleared first)
    /// so a steady-state query allocates nothing.
    /// </summary>
    public void Query(
        double minX, double minY, double maxX, double maxY,
        List<int> scratch, List<PaintOp> results)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        ArgumentNullException.ThrowIfNull(results);
        scratch.Clear();
        results.Clear();

        if (maxX < minX || maxY < minY)
            return;

        int c0 = ColOf(minX), c1 = ColOf(maxX);
        int r0 = RowOf(minY), r1 = RowOf(maxY);

        for (int r = r0; r <= r1; r++)
        {
            int rowBase = r * _cols;
            for (int c = c0; c <= c1; c++)
            {
                var bucket = _cells[rowBase + c];
                if (bucket is null)
                    continue;
                // Bucket members may fall outside the AABB within their cell;
                // re-test the precise anchor so the candidate set is tight.
                foreach (var idx in bucket)
                {
                    if (!TryAnchor(_ops[idx], out var x, out var y, out _, out _))
                        continue;
                    if (x >= minX && x <= maxX && y >= minY && y <= maxY)
                        scratch.Add(idx);
                }
            }
        }

        // Restore original op order (each op is in one cell, so no duplicates).
        scratch.Sort();
        foreach (var idx in scratch)
            results.Add(_ops[idx]);
    }

    private int CellIndex(double x, double y) => RowOf(y) * _cols + ColOf(x);

    private int ColOf(double x)
    {
        int c = (int)((x - _minX) * _invCellW);
        return c < 0 ? 0 : (c >= _cols ? _cols - 1 : c);
    }

    private int RowOf(double y)
    {
        int r = (int)((y - _minY) * _invCellH);
        return r < 0 ? 0 : (r >= _rows ? _rows - 1 : r);
    }

    private static bool TryAnchor(PaintOp op, out double x, out double y, out double offX, out double offY)
    {
        switch (op)
        {
            case PointPaintOp p:
                x = p.World.X; y = p.World.Y; offX = p.OffsetXpx; offY = p.OffsetYpx;
                return true;
            case TextPaintOp t:
                x = t.World.X; y = t.World.Y; offX = t.OffsetXpx; offY = t.OffsetYpx;
                return true;
            default:
                x = y = offX = offY = 0;
                return false;
        }
    }
}
