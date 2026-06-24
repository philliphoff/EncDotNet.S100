using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A uniform-grid spatial index over a bound base scene's area/line/pattern ops
/// (EPSG:3857 metres), built <b>once</b> when a scene is bound so the off-thread
/// <see cref="S100VectorTileRenderer.RasterizeTile"/> walk can be scoped to the
/// ops intersecting a tile (+ gutter) instead of the whole cell. This implements
/// the long-specified but never-built base-plane index from the render-subsystem
/// design §3.3 ("query VectorScene ops intersecting tile+gutter — spatial index
/// over the scene") — the overlay plane already got its own index in #351
/// (<see cref="OverlaySpatialIndex"/>); this is the base-plane counterpart
/// (#332 cold tile-gen perf line under #347).
/// </summary>
/// <remarks>
/// <para>
/// Unlike the overlay index (each op is anchored at a single world point), base
/// ops are <i>extents</i> — polylines and polygons — so each op occupies a world
/// AABB and is inserted into <b>every</b> grid cell its bounds cover.
/// <see cref="Query"/> therefore de-duplicates and returns each matching op once,
/// in <b>ascending original op index</b> (S-100 Part 9 ascending drawing
/// priority), so a scoped tile rasterises with the identical z-order the
/// whole-cell scene would.
/// </para>
/// <para>
/// The query is a deliberate <i>conservative superset</i>: it intersects op world
/// bounding boxes against the tile AABB (no precise polygon test), and the
/// downstream <c>SkiaDisplayListRenderer</c> still applies the exact per-op
/// <c>ScaleVisibility</c> cull and pixel clip — so scoping never drops an op that
/// would otherwise be drawn (pixel-identical / fidelity neutral); it only bounds
/// the per-tile walk. Any base op whose geometry this index does not recognise is
/// treated as an always-candidate so it can never be missed.
/// </para>
/// <para>
/// The index holds no mutable state after construction, so it is safe to
/// <see cref="Query"/> concurrently from the multiple worker threads that
/// rasterise tiles. It touches no Skia or GPU objects — it is a pure-CPU
/// transform over the immutable paint-op IR — so it is independent of the render
/// thread's GPU-context lifetime rules (design §3.7 / Appendix F).
/// </para>
/// </remarks>
internal sealed class BaseSpatialIndex
{
    private readonly IReadOnlyList<PaintOp> _ops;
    private readonly double[] _minXs;
    private readonly double[] _minYs;
    private readonly double[] _maxXs;
    private readonly double[] _maxYs;

    // Ops whose geometry was not recognised (no world bbox): always candidates.
    private readonly List<int> _alwaysCandidates;

    private readonly double _minX;
    private readonly double _minY;
    private readonly double _invCellW;
    private readonly double _invCellH;
    private readonly int _cols;
    private readonly int _rows;
    private readonly List<int>[] _cells;

    /// <summary>The base ops, in original draw order.</summary>
    public IReadOnlyList<PaintOp> Ops => _ops;

    /// <summary>Builds the index over <paramref name="baseScene"/>'s area/line ops.</summary>
    public BaseSpatialIndex(VectorScene baseScene)
    {
        ArgumentNullException.ThrowIfNull(baseScene);
        _ops = baseScene.Ops;
        int count = _ops.Count;

        _minXs = new double[count];
        _minYs = new double[count];
        _maxXs = new double[count];
        _maxYs = new double[count];
        _alwaysCandidates = new List<int>();

        // World bounds across all recognised op bboxes.
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        int bounded = 0;
        for (int i = 0; i < count; i++)
        {
            if (TryBounds(_ops[i], out var x0, out var y0, out var x1, out var y1))
            {
                _minXs[i] = x0; _minYs[i] = y0; _maxXs[i] = x1; _maxYs[i] = y1;
                if (x0 < minX) minX = x0;
                if (y0 < minY) minY = y0;
                if (x1 > maxX) maxX = x1;
                if (y1 > maxY) maxY = y1;
                bounded++;
            }
            else
            {
                // Sentinel that never intersects any finite query; the op is
                // served from _alwaysCandidates instead so it is never dropped.
                _minXs[i] = double.NaN;
                _alwaysCandidates.Add(i);
            }
        }

        if (bounded == 0)
        {
            // No bounded ops: a 1×1 grid that matches nothing. Always-candidates
            // (if any) are still served by Query.
            _cols = _rows = 1;
            _cells = new List<int>[1] { new() };
            _minX = _minY = 0;
            _invCellW = _invCellH = 0;
            return;
        }

        // Aim for ~1 op per cell on average, capped so a pathological cluster
        // can't build a huge grid. A zero-width/height extent collapses that axis.
        int target = Math.Clamp((int)Math.Ceiling(Math.Sqrt(bounded)), 1, 256);
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
        for (int i = 0; i < count; i++)
        {
            if (double.IsNaN(_minXs[i]))
                continue;
            int c0 = ColOf(_minXs[i]), c1 = ColOf(_maxXs[i]);
            int r0 = RowOf(_minYs[i]), r1 = RowOf(_maxYs[i]);
            for (int r = r0; r <= r1; r++)
            {
                int rowBase = r * _cols;
                for (int c = c0; c <= c1; c++)
                    (_cells[rowBase + c] ??= new List<int>()).Add(i);
            }
        }
    }

    /// <summary>
    /// Returns the base ops whose world bounding box intersects the world AABB
    /// (<paramref name="minX"/>..<paramref name="maxY"/>), de-duplicated and in
    /// ascending original op order. A fresh list is allocated per call so the
    /// query is safe to run concurrently from worker threads.
    /// </summary>
    public List<PaintOp> Query(double minX, double minY, double maxX, double maxY)
    {
        var results = new List<PaintOp>();
        if (maxX < minX || maxY < minY)
            return results;

        var seen = new HashSet<int>();
        foreach (var idx in _alwaysCandidates)
            seen.Add(idx);

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
                // An op straddles multiple cells, so re-test its precise bbox to
                // keep the candidate set tight (and naturally de-duplicate via the set).
                foreach (var idx in bucket)
                {
                    if (_maxXs[idx] < minX || _minXs[idx] > maxX ||
                        _maxYs[idx] < minY || _minYs[idx] > maxY)
                        continue;
                    seen.Add(idx);
                }
            }
        }

        if (seen.Count == 0)
            return results;

        var ordered = new List<int>(seen);
        ordered.Sort();
        foreach (var idx in ordered)
            results.Add(_ops[idx]);
        return results;
    }

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

    /// <summary>
    /// Computes the world AABB of a base op's geometry. Area/pattern ops use the
    /// exterior ring (holes are inside the shell); lines use the polyline.
    /// Returns <see langword="false"/> for op types this index does not model
    /// (those become always-candidates so they are never dropped).
    /// </summary>
    private static bool TryBounds(PaintOp op, out double minX, out double minY, out double maxX, out double maxY)
    {
        switch (op)
        {
            case AreaPaintOp a:
                return TryRingBounds(a.WorldShell, out minX, out minY, out maxX, out maxY);
            case PatternAreaPaintOp p:
                return TryRingBounds(p.WorldShell, out minX, out minY, out maxX, out maxY);
            case LinePaintOp l:
                return TryRingBounds(l.World, out minX, out minY, out maxX, out maxY);
            default:
                minX = minY = maxX = maxY = 0;
                return false;
        }
    }

    private static bool TryRingBounds(
        IReadOnlyList<(double X, double Y)> ring,
        out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = double.MaxValue;
        maxX = maxY = double.MinValue;
        if (ring is null || ring.Count == 0)
            return false;
        foreach (var (x, y) in ring)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        return true;
    }
}
