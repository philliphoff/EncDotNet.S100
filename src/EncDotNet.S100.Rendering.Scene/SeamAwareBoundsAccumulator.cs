namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// Accumulates the EPSG:3857 extent of a headless render while remaining aware
/// of the ±180° antimeridian seam, so the auto-fit viewport frames a dataset on
/// its true extent even when its geometry wraps the dateline.
/// </summary>
/// <remarks>
/// <para>A naive min/max over projected world-X collapses for antimeridian-
/// spanning data (e.g. the Alaska NWS S-411 product, ~175°E → ~225°E): one
/// cluster near +179° and another near −179° make the X-span ≈ the full world
/// width, so the fitted viewport becomes near-global and every feature draws
/// sub-pixel (issue #413).</para>
/// <para>To detect the seam robustly the accumulator maintains a coarse
/// longitude occupancy histogram (<see cref="BinCount"/> bins over
/// [−180°, 180°)), tracking the exact observed [minX, maxX] per bin. On
/// <see cref="TryResolve"/> it locates the <em>largest empty longitude arc</em>
/// (the emptiest gap): if that gap is an interior band — rather than the arc
/// that already wraps the seam — the data straddles ±180°, so the western
/// cluster is shifted by one <see cref="WebMercator.Circumference"/> to produce
/// a contiguous world-X window whose <c>maxX</c> may exceed +½ circumference.
/// The paired <see cref="WorldToScreen"/> wrap re-homes the shifted ops at draw
/// time.</para>
/// <para>The seam logic only engages when the naive longitude span exceeds
/// 180°; narrower extents (the overwhelming majority) take the fast path and
/// behave exactly as the previous naive min/max fit. Genuinely wide / global
/// datasets — whose emptiest gap is the seam-wrapping arc, not an interior band
/// — also keep the naive extent, so the heuristic does not false-positive.</para>
/// </remarks>
public sealed class SeamAwareBoundsAccumulator
{
    /// <summary>Number of longitude occupancy bins (0.5° each).</summary>
    public const int BinCount = 720;

    private const double DegToRad = Math.PI / 180.0;
    private const double RadToDeg = 180.0 / Math.PI;
    private const double BinWidthDeg = 360.0 / BinCount;

    private readonly bool[] _occupied = new bool[BinCount];
    private readonly double[] _binMinX = new double[BinCount];
    private readonly double[] _binMaxX = new double[BinCount];

    private double _minY = double.MaxValue;
    private double _maxY = double.MinValue;
    private bool _any;

    /// <summary>Creates an empty accumulator.</summary>
    public SeamAwareBoundsAccumulator()
    {
        Array.Fill(_binMinX, double.MaxValue);
        Array.Fill(_binMaxX, double.MinValue);
    }

    /// <summary>Whether any geometry has been accumulated.</summary>
    public bool HasGeometry => _any;

    /// <summary>
    /// Accumulates every vertex of a resolved <see cref="VectorScene"/> — point
    /// and text anchors, polyline vertices, and area shell + hole rings.
    /// </summary>
    /// <param name="scene">The lowered scene whose paint ops to bound.</param>
    public void AddScene(VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        foreach (var op in scene.Ops)
        {
            switch (op)
            {
                case PointPaintOp p:
                    Add(p.World.X, p.World.Y);
                    break;
                case TextPaintOp t:
                    Add(t.World.X, t.World.Y);
                    break;
                case LinePaintOp l:
                    foreach (var (x, y) in l.World) Add(x, y);
                    break;
                case AreaPaintOp a:
                    foreach (var (x, y) in a.WorldShell) Add(x, y);
                    foreach (var hole in a.WorldHoles)
                        foreach (var (x, y) in hole) Add(x, y);
                    break;
                case PatternAreaPaintOp pa:
                    foreach (var (x, y) in pa.WorldShell) Add(x, y);
                    foreach (var hole in pa.WorldHoles)
                        foreach (var (x, y) in hole) Add(x, y);
                    break;
            }
        }
    }

    /// <summary>
    /// Accumulates a single EPSG:3857 world coordinate (metres).
    /// </summary>
    /// <param name="x">Easting in metres.</param>
    /// <param name="y">Northing in metres.</param>
    public void Add(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return;

        _any = true;
        if (y < _minY) _minY = y;
        if (y > _maxY) _maxY = y;
        MarkX(x);
    }

    /// <summary>
    /// Accumulates a WGS-84 lon/lat bounding box (degrees), projecting its
    /// corners to EPSG:3857 and marking every longitude bin it covers as
    /// occupied so a wide extent (e.g. a coverage grid) does not introduce a
    /// spurious interior gap. Boxes whose <paramref name="west"/> exceeds
    /// <paramref name="east"/> are treated as crossing the ±180° seam.
    /// </summary>
    /// <param name="west">Western longitude edge, degrees.</param>
    /// <param name="east">Eastern longitude edge, degrees.</param>
    /// <param name="south">Southern latitude edge, degrees.</param>
    /// <param name="north">Northern latitude edge, degrees.</param>
    public void AddLonLatBox(double west, double east, double south, double north)
    {
        var (_, minY) = WebMercator.FromLonLat(0, Math.Min(south, north));
        var (_, maxY) = WebMercator.FromLonLat(0, Math.Max(south, north));

        _any = true;
        if (minY < _minY) _minY = minY;
        if (maxY > _maxY) _maxY = maxY;

        if (west <= east)
        {
            MarkLonRange(west, east);
        }
        else
        {
            // Crosses the seam: [west, +180) ∪ [−180, east].
            MarkLonRange(west, 180.0);
            MarkLonRange(-180.0, east);
        }
    }

    /// <summary>
    /// Resolves the accumulated geometry to a seam-aware EPSG:3857 window.
    /// </summary>
    /// <param name="minX">Resolved minimum easting, metres.</param>
    /// <param name="minY">Resolved minimum northing, metres.</param>
    /// <param name="maxX">
    /// Resolved maximum easting, metres. May exceed +½
    /// <see cref="WebMercator.Circumference"/> when the extent was shifted
    /// across the antimeridian; the paired <see cref="WorldToScreen"/> wrap
    /// re-homes ops into this window at draw time.
    /// </param>
    /// <param name="maxY">Resolved maximum northing, metres.</param>
    /// <returns>
    /// <see langword="true"/> when geometry was accumulated; otherwise
    /// <see langword="false"/> (all outputs zero).
    /// </returns>
    public bool TryResolve(out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = minY = maxX = maxY = 0;
        if (!_any)
            return false;

        minY = _minY;
        maxY = _maxY;

        // Naive world-X extent over all occupied bins.
        double naiveMinX = double.MaxValue, naiveMaxX = double.MinValue;
        for (int i = 0; i < BinCount; i++)
        {
            if (!_occupied[i]) continue;
            if (_binMinX[i] < naiveMinX) naiveMinX = _binMinX[i];
            if (_binMaxX[i] > naiveMaxX) naiveMaxX = _binMaxX[i];
        }

        double naiveSpanLon = (naiveMaxX - naiveMinX) * RadToDeg / WebMercator.EarthRadius;
        if (naiveSpanLon <= 180.0)
        {
            // Fast path: extent cannot be pathologically wide — no seam handling.
            minX = naiveMinX;
            maxX = naiveMaxX;
            return true;
        }

        if (TryFindLargestInteriorGap(out int gapAfterBin))
        {
            // The emptiest longitude arc is an interior band, so the data wraps
            // the ±180° seam. Bins at and below the gap are the western cluster;
            // shift them by +one circumference to sit contiguously to the east
            // of the higher-longitude cluster.
            double westMaxXShifted = double.MinValue; // western cluster (shifted)
            double eastMinX = double.MaxValue;        // eastern cluster (unshifted)
            for (int i = 0; i < BinCount; i++)
            {
                if (!_occupied[i]) continue;
                if (i <= gapAfterBin)
                {
                    double shifted = _binMaxX[i] + WebMercator.Circumference;
                    if (shifted > westMaxXShifted) westMaxXShifted = shifted;
                }
                else
                {
                    if (_binMinX[i] < eastMinX) eastMinX = _binMinX[i];
                }
            }

            minX = eastMinX;
            maxX = westMaxXShifted;
            return true;
        }

        // Largest gap is the seam-wrapping arc (genuinely wide / global data):
        // the naive extent is already the tightest enclosure.
        minX = naiveMinX;
        maxX = naiveMaxX;
        return true;
    }

    /// <summary>
    /// Finds the largest run of empty bins. Returns <see langword="true"/> and
    /// the index of the last occupied bin <em>before</em> that gap when the gap
    /// is interior (i.e. it does not wrap the ±180° seam), signalling that the
    /// data straddles the antimeridian. Returns <see langword="false"/> when the
    /// largest gap is the seam-wrapping arc (no shift needed).
    /// </summary>
    private bool TryFindLargestInteriorGap(out int gapAfterBin)
    {
        gapAfterBin = -1;

        int first = -1, last = -1;
        for (int i = 0; i < BinCount; i++)
        {
            if (!_occupied[i]) continue;
            if (first < 0) first = i;
            last = i;
        }

        if (first < 0)
            return false;

        // The seam-wrapping gap: empty bins from after the last occupied bin,
        // around through ±180°, to before the first occupied bin.
        int wrapGap = (first + BinCount) - last - 1;

        int bestGap = -1;
        int bestGapAfterBin = -1;
        int prev = -1;
        for (int i = 0; i < BinCount; i++)
        {
            if (!_occupied[i]) continue;
            if (prev >= 0)
            {
                int gap = i - prev - 1; // empty bins strictly between prev and i
                if (gap > bestGap)
                {
                    bestGap = gap;
                    bestGapAfterBin = prev;
                }
            }
            prev = i;
        }

        if (bestGap <= 0 || bestGap <= wrapGap)
            return false; // no interior gap dominates the seam-wrapping arc

        gapAfterBin = bestGapAfterBin;
        return true;
    }

    private void MarkX(double x)
    {
        double lon = x * RadToDeg / WebMercator.EarthRadius;
        int bin = BinOf(lon);
        _occupied[bin] = true;
        if (x < _binMinX[bin]) _binMinX[bin] = x;
        if (x > _binMaxX[bin]) _binMaxX[bin] = x;
    }

    private void MarkLonRange(double westLon, double eastLon)
    {
        int startBin = BinOf(westLon);
        int endBin = BinOf(Math.Min(eastLon, 180.0 - 1e-9));
        for (int i = startBin; i <= endBin; i++)
        {
            double loLon = -180.0 + i * BinWidthDeg;
            double hiLon = loLon + BinWidthDeg;
            double loX = loLon * DegToRad * WebMercator.EarthRadius;
            double hiX = hiLon * DegToRad * WebMercator.EarthRadius;
            _occupied[i] = true;
            if (loX < _binMinX[i]) _binMinX[i] = loX;
            if (hiX > _binMaxX[i]) _binMaxX[i] = hiX;
        }
    }

    private static int BinOf(double lon)
    {
        double clamped = Math.Clamp(lon, -180.0, 180.0 - 1e-9);
        int bin = (int)Math.Floor((clamped + 180.0) / BinWidthDeg);
        if (bin < 0) bin = 0;
        else if (bin >= BinCount) bin = BinCount - 1;
        return bin;
    }
}
