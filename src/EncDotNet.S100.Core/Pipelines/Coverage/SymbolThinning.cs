namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Screen-space thinning of a field of scaled point symbols (e.g. S-111
/// surface-current arrows), shared by the Mapsui and headless Skia renderers
/// so both draw the same arrows at the same zoom.
/// </summary>
/// <remarks>
/// <para>
/// S-98 Ed 2.0.0 §13.1 requires thinning when gridded data is drawn as discrete
/// symbols, and leaves the algorithm to the implementer. Its recommended
/// algorithm (S-98 Appendix G-1.1, restated in S-111 Ed 2.0.0 §9.3.2 and Annex H
/// Rule 11) is implemented by <see cref="ThinGrid"/>; the point-by-point method
/// S-111 §9.3.2 describes for irregularly spaced data (and §9.3.3 allows for
/// ungeorectified grids) is implemented by <see cref="ThinPoints"/>.
/// </para>
/// <para>
/// Both methods use the same ratio <c>Rmax</c> between the largest symbol and the
/// displayed spacing, so a regular grid and a mesh with the same node density
/// thin to the same on-screen density. All coordinates and lengths passed to one
/// call must share a unit (typically EPSG:3857 metres, with symbol lengths
/// converted from pixels through the map resolution).
/// </para>
/// </remarks>
public static class SymbolThinning
{
    /// <summary>
    /// The default ratio <c>Rmax</c> of the largest symbol's length to the
    /// displayed symbol spacing: 0.5, the value S-111 Ed 2.0.0 §9.3.2 and S-98
    /// Appendix G-1.1 give. With 0.5 the drawn grid-cell diagonal is at least
    /// twice the largest arrow (and the axis spacing about 1.41 arrows), so
    /// arrows never overlap.
    /// </summary>
    public const double DefaultMaxSymbolToSpacingRatio = 0.5;

    /// <summary>
    /// Device-independent pixels per millimetre (96 / 25.4). The renderers
    /// rasterise millimetre-dimensioned S-100 SVG symbols at 96 DPI, so a symbol
    /// length given in millimetres is converted to on-screen pixels with this
    /// factor.
    /// </summary>
    public const double PixelsPerMillimetre = 96.0 / 25.4;

    /// <summary>
    /// The grid increment <c>n</c> of S-98 Appendix G-1.1 (S-111 Eqn 9.3, Annex H
    /// Eqn H.3): 1 when <c>Lsmax / D &lt; Rmax</c>, otherwise
    /// <c>1 + fix(Lsmax / (D · Rmax))</c>, so that every <c>n</c>th row and column
    /// is drawn.
    /// </summary>
    /// <param name="cellDiagonal">The on-screen grid cell diagonal <c>D</c>.</param>
    /// <param name="maxSymbolLength">
    /// The length <c>Lsmax</c> of the largest symbol in the displayed field, in the
    /// same unit as <paramref name="cellDiagonal"/>.
    /// </param>
    /// <param name="maxRatio">The ratio <c>Rmax</c>; must be positive.</param>
    /// <returns>The increment, at least 1.</returns>
    public static int GridIncrement(double cellDiagonal, double maxSymbolLength, double maxRatio)
    {
        if (!(cellDiagonal > 0) || !(maxSymbolLength > 0) || !(maxRatio > 0)
            || double.IsInfinity(cellDiagonal) || double.IsInfinity(maxSymbolLength))
        {
            return 1;
        }

        double ratio = maxSymbolLength / cellDiagonal;
        if (ratio < maxRatio)
            return 1;

        double n = 1 + Math.Floor(maxSymbolLength / (cellDiagonal * maxRatio));
        return n >= int.MaxValue ? int.MaxValue : (int)n;
    }

    /// <summary>
    /// Thins a regular grid of symbols with the S-98 Appendix G-1.1 algorithm:
    /// draws every <see cref="GridIncrement"/>th row and column, counted from a
    /// seed cell that holds the largest symbol in the displayed field.
    /// </summary>
    /// <param name="rows">Number of grid rows.</param>
    /// <param name="cols">Number of grid columns.</param>
    /// <param name="x">Per-cell symbol x position, row-major (<c>rows × cols</c>).</param>
    /// <param name="y">Per-cell symbol y position, row-major. y may increase up or down.</param>
    /// <param name="scale">
    /// Per-cell symbol scale, row-major. A cell with a NaN or non-positive scale
    /// has no symbol (no data, or out of every band).
    /// </param>
    /// <param name="priority">
    /// Per-cell priority, row-major (for S-111, the current speed). Among the
    /// cells with the largest symbol, the one with the highest priority seeds
    /// the lattice; remaining ties go to the first in row-major order.
    /// </param>
    /// <param name="lengthPerScale">
    /// Length of a symbol at scale 1, in the unit of <paramref name="x"/> /
    /// <paramref name="y"/>.
    /// </param>
    /// <param name="displayed">The displayed field; only cells inside it are considered.</param>
    /// <param name="maxRatio">The ratio <c>Rmax</c>.</param>
    /// <param name="selected">Receives the row-major indices of the cells to draw (cleared first).</param>
    /// <remarks>
    /// <para>
    /// <c>D</c> is the distance between the seed cell and its diagonal neighbour,
    /// so it reflects the local cell size where the largest symbol is (a
    /// geographic grid's cells shrink towards the poles in a Mercator display).
    /// </para>
    /// <para>
    /// Choice (S-98 G-1.1 lets implementers "adapt the determination of the seed
    /// point"): the seed is the fastest current among the largest symbols, not
    /// the first largest symbol in row-major order. S-111 arrows bottom out at
    /// the <c>Slow</c> size, so in a slow field every cell ties for largest and a
    /// row-major seed would move — and the drawn lattice would jump — whenever
    /// panning brought a new top-left cell into view. The fastest current is
    /// what S-111 §9.3.2 asks to keep ("the row and column with the maximum
    /// vector") and it only changes when a faster current enters or leaves the
    /// view.
    /// </para>
    /// </remarks>
    public static void ThinGrid(
        int rows,
        int cols,
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        ReadOnlySpan<float> scale,
        ReadOnlySpan<float> priority,
        double lengthPerScale,
        SymbolRect displayed,
        double maxRatio,
        List<int> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        selected.Clear();

        int count = rows * cols;
        if (rows <= 0 || cols <= 0 || x.Length < count || y.Length < count
            || scale.Length < count || priority.Length < count)
        {
            return;
        }

        // Pass 1: the largest symbol in the displayed field and the seed cell.
        int seed = -1;
        float maxScale = 0;
        float seedPriority = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            float s = scale[i];
            if (!(s > 0) || !displayed.Contains(x[i], y[i]))
                continue;

            float p = float.IsNaN(priority[i]) ? float.NegativeInfinity : priority[i];
            if (s > maxScale || (s == maxScale && p > seedPriority))
            {
                maxScale = s;
                seedPriority = p;
                seed = i;
            }
        }

        if (seed < 0)
            return;

        int seedRow = seed / cols;
        int seedCol = seed % cols;

        // D: the seed cell's diagonal. Use whichever diagonal neighbour exists.
        double diagonal = 0;
        if (rows > 1 && cols > 1)
        {
            int nRow = seedRow + 1 < rows ? seedRow + 1 : seedRow - 1;
            int nCol = seedCol + 1 < cols ? seedCol + 1 : seedCol - 1;
            int neighbour = nRow * cols + nCol;
            double dx = x[neighbour] - x[seed];
            double dy = y[neighbour] - y[seed];
            diagonal = Math.Sqrt(dx * dx + dy * dy);
        }

        int n = GridIncrement(diagonal, maxScale * lengthPerScale, maxRatio);

        // Pass 2: every nth row and column, counted from the seed.
        for (int i = 0; i < count; i++)
        {
            if (!(scale[i] > 0) || !displayed.Contains(x[i], y[i]))
                continue;

            int r = i / cols;
            int c = i - r * cols;
            if (Mod(r - seedRow, n) == 0 && Mod(c - seedCol, n) == 0)
                selected.Add(i);
        }
    }

    /// <summary>
    /// Thins irregularly spaced symbols (station series, ungeorectified meshes)
    /// point by point, as S-111 Ed 2.0.0 §9.3.2 describes: symbols are visited
    /// in descending priority, and a symbol is dropped when it lies closer than
    /// <c>L / (Rmax · √2)</c> to an already kept symbol, <c>L</c> being the longer
    /// of the two.
    /// </summary>
    /// <param name="x">Per-point symbol x position.</param>
    /// <param name="y">Per-point symbol y position.</param>
    /// <param name="length">
    /// Per-point symbol length, in the unit of <paramref name="x"/> /
    /// <paramref name="y"/>. A NaN or non-positive length marks a point with no
    /// symbol; it is never kept.
    /// </param>
    /// <param name="priority">
    /// Per-point priority (for S-111, the current speed); higher is kept first,
    /// ties go to the lower index.
    /// </param>
    /// <param name="maxRatio">The ratio <c>Rmax</c>.</param>
    /// <param name="kept">Receives the indices of the points to draw, in ascending order (cleared first).</param>
    /// <remarks>
    /// <para>
    /// Choice: S-111 §9.3.2 says overlapping symbols are eliminated but does not
    /// define overlap. We reuse the grid method's spacing pairwise. The grid
    /// method bounds the drawn cell <em>diagonal</em> by <c>Lsmax / Rmax</c>, so in
    /// a thinned square grid the nearest drawn neighbours, along an axis, are
    /// <c>Lsmax / (Rmax · √2)</c> apart. A kept point symbol of length <c>L</c>
    /// therefore clears a radius of <c>L / (Rmax · √2)</c>: a mesh and a grid of
    /// the same node density thin to the same nearest-neighbour spacing. With
    /// the default <c>Rmax</c> of 0.5 that is about 1.41 arrow lengths, more than
    /// the one length at which two arrows centred on their pivots could touch,
    /// so drawn arrows never overlap.
    /// </para>
    /// <para>
    /// Visiting in descending priority keeps the fastest currents, the
    /// point-set counterpart of the grid method's "row and column with the
    /// maximum vector". The result depends only on the input points and the
    /// lengths, not on a viewport, so callers that pass every point (rather than
    /// only the visible ones) get a selection that is stable while panning.
    /// </para>
    /// </remarks>
    public static void ThinPoints(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        ReadOnlySpan<double> length,
        ReadOnlySpan<double> priority,
        double maxRatio,
        List<int> kept)
    {
        ArgumentNullException.ThrowIfNull(kept);
        kept.Clear();

        int count = Math.Min(Math.Min(x.Length, y.Length), Math.Min(length.Length, priority.Length));
        if (count == 0 || !(maxRatio > 0))
            return;

        var order = new List<int>(count);
        double maxLength = 0;
        for (int i = 0; i < count; i++)
        {
            double l = length[i];
            if (!(l > 0) || double.IsInfinity(l) || !double.IsFinite(x[i]) || !double.IsFinite(y[i]))
                continue;
            order.Add(i);
            if (l > maxLength)
                maxLength = l;
        }

        if (order.Count == 0)
            return;

        var orderArray = order.ToArray();
        var keys = new double[count];
        foreach (int i in orderArray)
            keys[i] = double.IsNaN(priority[i]) ? double.NegativeInfinity : priority[i];

        // Descending priority; Array.Sort is unstable, so break ties on index.
        Array.Sort(orderArray, (a, b) =>
        {
            int byPriority = keys[b].CompareTo(keys[a]);
            return byPriority != 0 ? byPriority : a.CompareTo(b);
        });

        // Spatial hash with buckets as wide as the largest clearance radius, so a
        // conflict can only come from the 3 × 3 neighbouring buckets.
        double clearancePerLength = 1.0 / (maxRatio * Math.Sqrt(2));
        double bucket = maxLength * clearancePerLength;
        var buckets = new Dictionary<(long, long), List<int>>();
        foreach (int i in orderArray)
        {
            long bx = (long)Math.Floor(x[i] / bucket);
            long by = (long)Math.Floor(y[i] / bucket);
            bool clear = true;
            for (long gx = bx - 1; gx <= bx + 1 && clear; gx++)
            {
                for (long gy = by - 1; gy <= by + 1 && clear; gy++)
                {
                    if (!buckets.TryGetValue((gx, gy), out var occupants))
                        continue;
                    foreach (int j in occupants)
                    {
                        double radius = Math.Max(length[i], length[j]) * clearancePerLength;
                        double dx = x[i] - x[j];
                        double dy = y[i] - y[j];
                        if (dx * dx + dy * dy < radius * radius)
                        {
                            clear = false;
                            break;
                        }
                    }
                }
            }

            if (!clear)
                continue;

            if (!buckets.TryGetValue((bx, by), out var cell))
            {
                cell = new List<int>(2);
                buckets[(bx, by)] = cell;
            }
            cell.Add(i);
            kept.Add(i);
        }

        kept.Sort();
    }

    private static int Mod(int value, int n)
    {
        int m = value % n;
        return m < 0 ? m + n : m;
    }
}

/// <summary>
/// An axis-aligned rectangle in the coordinate space of a
/// <see cref="SymbolThinning"/> call (for example EPSG:3857 metres).
/// </summary>
/// <param name="MinX">Minimum x.</param>
/// <param name="MinY">Minimum y.</param>
/// <param name="MaxX">Maximum x.</param>
/// <param name="MaxY">Maximum y.</param>
public readonly record struct SymbolRect(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>A rectangle that contains every finite point.</summary>
    public static SymbolRect Everything { get; } =
        new(double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>Returns <see langword="true"/> when (<paramref name="x"/>, <paramref name="y"/>) lies inside or on the edge.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    public bool Contains(double x, double y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    /// <summary>Returns this rectangle grown by <paramref name="margin"/> on every side.</summary>
    /// <param name="margin">The margin to add.</param>
    public SymbolRect Inflate(double margin) => new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);
}
