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
    /// seed cell that holds the largest symbol in the grid, with the increment
    /// set by the largest symbol in the displayed field.
    /// </summary>
    /// <param name="rows">Number of grid rows.</param>
    /// <param name="cols">Number of grid columns.</param>
    /// <param name="x">
    /// Per-cell symbol x position, row-major (<c>rows × cols</c>). A cell with a
    /// non-finite position (beyond the Web Mercator latitude limit, say) is never drawn.
    /// </param>
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
    /// <param name="selected">Receives the row-major indices of the cells to draw, in ascending order (cleared first).</param>
    /// <remarks>
    /// <para>
    /// Choice (S-98 G-1.1 lets implementers "adapt the determination of the seed
    /// point"): the seed is the largest symbol in the whole grid, fastest first,
    /// not the largest in the displayed field. A seed taken from the view moves
    /// whenever panning brings a faster current or wind into it, and with it the
    /// whole drawn lattice, so the symbols jitter over an area that hasn't
    /// changed. The grid's seed is still "the row and column with the maximum
    /// vector" S-111 §9.3.2 asks to keep, and it never moves while panning. Only
    /// the increment depends on the view, through the largest displayed symbol,
    /// so the lattice changes with zoom (and when a larger symbol enters view)
    /// but not with panning.
    /// </para>
    /// <para>
    /// Choice: the increment is worked out per axis and per cell rather than once
    /// from the seed cell's diagonal. A geographic grid's cells are not square in
    /// a Mercator display, and they grow taller towards the poles (by
    /// <c>1/cos(latitude)</c>) while their width stays put. One diagonal increment
    /// for the whole view draws rows near the poles far apart and the symbols
    /// along each of them nearly touching. Instead, rows are walked out from the
    /// seed row along the seed column, and each row's columns out from the seed
    /// column along that row, keeping the next cell whose distance from the last
    /// kept one <c>d</c> satisfies Eqn 9.2 with <c>D = √2 · d</c>. On a uniform
    /// square grid that is exactly every <see cref="GridIncrement"/>th row and
    /// column; on a geographic grid it draws fewer rows near the equator than
    /// near the poles, so the drawn spacing stays about the same on screen.
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

        // Pass 1: the largest symbol in the displayed field, which sets the
        // spacing, and the seed cell, the largest symbol in the grid.
        int seed = -1;
        float maxScale = 0;
        float seedScale = 0;
        float seedPriority = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            float s = scale[i];
            if (!(s > 0) || !double.IsFinite(x[i]) || !double.IsFinite(y[i]))
                continue;

            if (s > maxScale && displayed.Contains(x[i], y[i]))
                maxScale = s;

            float p = float.IsNaN(priority[i]) ? float.NegativeInfinity : priority[i];
            if (s > seedScale || (s == seedScale && p > seedPriority))
            {
                seedScale = s;
                seedPriority = p;
                seed = i;
            }
        }

        if (!(maxScale > 0))
            return;

        int seedRow = seed / cols;
        int seedCol = seed % cols;
        double maxLength = maxScale * lengthPerScale;

        // Pass 2: the rows, along the seed column, then each kept row's
        // columns, along the row.
        var keptRows = new bool[rows];
        MarkLattice(x, y, seedCol, cols, rows, seedRow, maxLength, maxRatio, keptRows);

        var keptCols = new bool[cols];
        for (int r = 0; r < rows; r++)
        {
            if (!keptRows[r])
                continue;

            int rowStart = r * cols;
            MarkLattice(x, y, rowStart, 1, cols, seedCol, maxLength, maxRatio, keptCols);
            for (int c = 0; c < cols; c++)
            {
                int i = rowStart + c;
                if (keptCols[c] && scale[i] > 0 && displayed.Contains(x[i], y[i]))
                    selected.Add(i);
            }
        }
    }

    /// <summary>
    /// Marks the cells of one grid line (a column, or a row) to draw: the
    /// anchor, then walking out from it both ways, each next cell far enough
    /// from the last kept one to satisfy Eqn 9.2 with <c>D = √2 · d</c>.
    /// </summary>
    /// <param name="x">Per-cell x, row-major.</param>
    /// <param name="y">Per-cell y, row-major.</param>
    /// <param name="start">Index of the line's first cell.</param>
    /// <param name="stride">Index step between the line's cells: the column count for a column, 1 for a row.</param>
    /// <param name="length">Number of cells on the line.</param>
    /// <param name="anchor">Position on the line of the cell the lattice is counted from.</param>
    /// <param name="maxSymbolLength">The largest displayed symbol's length.</param>
    /// <param name="maxRatio">The ratio <c>Rmax</c>.</param>
    /// <param name="kept">Receives, per position on the line, whether its cell is drawn (cleared first).</param>
    private static void MarkLattice(
        ReadOnlySpan<double> x,
        ReadOnlySpan<double> y,
        int start,
        int stride,
        int length,
        int anchor,
        double maxSymbolLength,
        double maxRatio,
        Span<bool> kept)
    {
        kept.Clear();
        int anchorIndex = start + (anchor * stride);
        if (!double.IsFinite(x[anchorIndex]) || !double.IsFinite(y[anchorIndex]))
            return;

        kept[anchor] = true;
        for (int step = -1; step <= 1; step += 2)
        {
            int last = anchorIndex;
            for (int k = anchor + step; k >= 0 && k < length; k += step)
            {
                int i = start + (k * stride);
                if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]))
                    continue;

                double dx = x[i] - x[last];
                double dy = y[i] - y[last];
                double diagonal = Math.Sqrt(2 * ((dx * dx) + (dy * dy)));
                if (!(maxSymbolLength > 0) || !(maxRatio > 0) || maxSymbolLength / diagonal < maxRatio)
                {
                    kept[k] = true;
                    last = i;
                }
            }
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
