using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>What one cell of a <see cref="NavigabilityGrid"/> is.</summary>
public enum NavigabilityCellClass : byte
{
    /// <summary>No loaded chart covers the cell.</summary>
    Uncharted = 0,

    /// <summary>A loaded chart charts land at the cell's centre.</summary>
    Land = 1,

    /// <summary>Charted water at least the minimum depth, with no danger in the cell: the boat can go here.</summary>
    Navigable = 2,

    /// <summary>Charted water shallower than the minimum depth (drying ground included).</summary>
    Shoal = 3,

    /// <summary>Charted water with a danger (wreck, obstruction or rock) in the cell at or shallower than the hazard depth.</summary>
    Hazard = 4,

    /// <summary>Charted water with no charted depth.</summary>
    NoDepth = 5,
}

/// <summary>What a <see cref="NavigabilityGrid"/> covers and how it judges a cell.</summary>
/// <param name="Extent">The box, in WGS-84. An east longitude less than the west one crosses the antimeridian.</param>
/// <param name="CellMeters">The cell size in metres, along both axes (at the box's middle latitude for longitude).</param>
/// <param name="MinimumDepthMeters">
/// The least charted depth a cell needs to be <see cref="NavigabilityCellClass.Navigable"/>, in metres:
/// typically the draft plus the clearance wanted, or the safety contour.
/// </param>
/// <param name="HazardDepthMeters">
/// Dangers whose depth is at or shallower than this mark their cells
/// <see cref="NavigabilityCellClass.Hazard"/>, as do dangers with no known depth;
/// <c>null</c> uses <paramref name="MinimumDepthMeters"/>.
/// </param>
/// <param name="IncludeHazards">Whether to read the charted dangers at all.</param>
public sealed record NavigabilityGridRequest(
    BoundingBox Extent,
    double CellMeters,
    double MinimumDepthMeters,
    double? HazardDepthMeters = null,
    bool IncludeHazards = true)
{
    /// <summary>The most cells a grid may have.</summary>
    public const int MaxCells = 4_000_000;

    /// <summary>The number of rows and columns the request gives.</summary>
    /// <returns>The rows and columns.</returns>
    public (int Rows, int Columns) Size()
    {
        var (latStep, lonStep, span) = Steps();
        var rows = Math.Max(1, (int)Math.Ceiling((Extent.NorthLatitude - Extent.SouthLatitude) / latStep));
        var columns = Math.Max(1, (int)Math.Ceiling(span / lonStep));
        return (rows, columns);
    }

    /// <summary>The cell count the request gives, as a long so a huge box doesn't overflow.</summary>
    /// <returns>The cell count.</returns>
    public long CellCount()
    {
        var (latStep, lonStep, span) = Steps();
        var rows = Math.Max(1L, (long)Math.Ceiling((Extent.NorthLatitude - Extent.SouthLatitude) / latStep));
        var columns = Math.Max(1L, (long)Math.Ceiling(span / lonStep));
        return rows * columns;
    }

    /// <summary>The smallest cell size, in metres, that keeps the box within a cell count.</summary>
    /// <param name="maxCells">The cell count.</param>
    /// <returns>The cell size, rounded up to a whole metre.</returns>
    public double CellMetersFor(long maxCells)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCells, 1);
        var midLat = (Extent.SouthLatitude + Extent.NorthLatitude) / 2;
        var height = (Extent.NorthLatitude - Extent.SouthLatitude) * PassageGeodesy.MetersPerDegreeLatitude;
        var width = LongitudeSpan() * PassageGeodesy.MetersPerDegreeLatitude * Math.Max(Math.Cos(midLat * Math.PI / 180), 1e-6);
        var cell = Math.Sqrt(Math.Max(height, 1) * Math.Max(width, 1) / maxCells);
        while ((this with { CellMeters = Math.Ceiling(cell) }).CellCount() > maxCells)
        {
            cell *= 1.05;
        }

        return Math.Ceiling(cell);
    }

    internal (double LatStep, double LonStep, double LonSpan) Steps()
    {
        var midLat = (Extent.SouthLatitude + Extent.NorthLatitude) / 2;
        var latStep = CellMeters / PassageGeodesy.MetersPerDegreeLatitude;
        var lonStep = CellMeters / (PassageGeodesy.MetersPerDegreeLatitude * Math.Max(Math.Cos(midLat * Math.PI / 180), 1e-6));
        return (latStep, lonStep, LongitudeSpan());
    }

    private double LongitudeSpan()
    {
        var span = Extent.EastLongitude - Extent.WestLongitude;
        return span < 0 ? span + 360 : span;
    }
}

/// <summary>A cell of a <see cref="NavigabilityGrid"/>.</summary>
/// <param name="Row">The row, from the south.</param>
/// <param name="Column">The column, from the west.</param>
public readonly record struct NavigabilityCell(int Row, int Column);

/// <summary>
/// Whether two positions are joined through navigable cells, and the
/// narrowest place on the widest way between them.
/// </summary>
/// <param name="Connected">Whether a way through navigable cells joins them.</param>
/// <param name="From">The cell of the first position, or <c>null</c> when it's off the grid.</param>
/// <param name="To">The cell of the second position, or <c>null</c> when it's off the grid.</param>
/// <param name="Reason">
/// Why they aren't connected: <c>from_off_grid</c>, <c>to_off_grid</c>,
/// <c>from_not_navigable</c>, <c>to_not_navigable</c> or
/// <c>no_navigable_way</c>; <c>null</c> when they are.
/// </param>
/// <param name="Narrowest">
/// The cell where the widest way between them is narrowest (by
/// <see cref="NavigabilityGrid.ClearanceMeters"/>), or <c>null</c> when they
/// aren't connected.
/// </param>
/// <param name="NarrowestClearanceMeters">
/// At <paramref name="Narrowest"/>, the distance from the cell's centre to
/// the nearest cell that isn't navigable, in metres.
/// </param>
public sealed record NavigabilityConnection(
    bool Connected,
    NavigabilityCell? From,
    NavigabilityCell? To,
    string? Reason,
    NavigabilityCell? Narrowest = null,
    double? NarrowestClearanceMeters = null);

/// <summary>
/// Water, land and charted depth over a box on a regular grid, with
/// overlapping cells resolved as the route check resolves them: for each
/// cell its class (<see cref="NavigabilityCellClass"/>), its least charted
/// depth, and whether a charted danger is in it. Built by
/// <see cref="Build(RouteCheckSources, NavigabilityGridRequest, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// A cell's class comes from the water mask at its centre
/// (<see cref="IWaterMask"/>: finest chart cell wins). Its least depth is the
/// shoalest charted depth (<see cref="IChartedDepthSource"/>: S-102, else
/// the finest S-101 cell's dredged or depth area) at its centre and its four
/// corners, so a depth contour through the cell counts its shoaler side.
/// Depths aren't reduced to a common vertical datum, as in the chart check.
/// </para>
/// <para>
/// The grid is the base the route planning tools search over: the nearest
/// navigable point (#1559) and route suggestions (#1560) read
/// <see cref="IsNavigable(int, int)"/> and <see cref="ClearanceMeters"/>.
/// </para>
/// <para>Safe to read from several threads at once once built.</para>
/// </remarks>
public sealed class NavigabilityGrid
{
    private readonly NavigabilityCellClass[] _classes;
    private readonly float[] _depths;
    private readonly bool[] _hazards;
    private readonly object _gate = new();
    private float[]? _clearance;
    private int[]? _components;
    private int[]? _componentSizes;

    private NavigabilityGrid(
        NavigabilityGridRequest request,
        WaterMaskGrid centres,
        NavigabilityCellClass[] classes,
        float[] depths,
        bool[] hazards,
        IReadOnlyList<string> dangerDatasets)
    {
        Request = request;
        Centres = centres;
        _classes = classes;
        _depths = depths;
        _hazards = hazards;
        HazardDatasets = dangerDatasets;
    }

    /// <summary>The request the grid was built for.</summary>
    public NavigabilityGridRequest Request { get; }

    /// <summary>The cell centres, as a grid of points: row 0 is the southernmost row, column 0 the westernmost.</summary>
    public WaterMaskGrid Centres { get; }

    /// <summary>The number of rows.</summary>
    public int Rows => Centres.Rows;

    /// <summary>The number of columns.</summary>
    public int Columns => Centres.Columns;

    /// <summary>The cell size in metres.</summary>
    public double CellMeters => Request.CellMeters;

    /// <summary>The least depth a cell needs to be navigable, in metres.</summary>
    public double MinimumDepthMeters => Request.MinimumDepthMeters;

    /// <summary>The datasets whose dangers marked a cell.</summary>
    public IReadOnlyList<string> HazardDatasets { get; }

    /// <summary>Every cell's class, row by row from the south-west.</summary>
    public ReadOnlySpan<NavigabilityCellClass> Classes => _classes;

    /// <summary>Every cell's least charted depth in metres (positive down), row by row from the south-west; NaN where there's none.</summary>
    public ReadOnlySpan<float> LeastDepths => _depths;

    /// <summary>Whether each cell has a danger in it, row by row from the south-west.</summary>
    public ReadOnlySpan<bool> Hazards => _hazards;

    /// <summary>The class of a cell.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>The class.</returns>
    public NavigabilityCellClass ClassAt(int row, int column) => _classes[Index(row, column)];

    /// <summary>The least charted depth of a cell in metres, or <c>null</c>.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>The depth, or <c>null</c>.</returns>
    public double? LeastDepthAt(int row, int column) =>
        _depths[Index(row, column)] is var d && !float.IsNaN(d) ? d : null;

    /// <summary>Whether a cell is navigable.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns><c>true</c> when it is.</returns>
    public bool IsNavigable(int row, int column) =>
        row >= 0 && column >= 0 && row < Rows && column < Columns
        && _classes[Index(row, column)] == NavigabilityCellClass.Navigable;

    /// <summary>The centre of a cell.</summary>
    /// <param name="cell">The cell.</param>
    /// <returns>The centre, with its longitude in −180…180°.</returns>
    public GeoPosition CenterOf(NavigabilityCell cell) => Centres.PositionAt(cell.Row, cell.Column);

    /// <summary>The cell a position falls in, or <c>null</c> when it's off the grid.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The cell, or <c>null</c>.</returns>
    public NavigabilityCell? CellAt(GeoPosition position)
    {
        var row = (int)Math.Floor(((position.Latitude - Centres.South) / Centres.LatitudeStep) + 0.5);
        var dLon = (((position.Longitude - Centres.West) + 540) % 360) - 180;
        if (dLon < -Centres.LongitudeStep)
        {
            dLon += 360;
        }

        var column = (int)Math.Floor((dLon / Centres.LongitudeStep) + 0.5);
        return row < 0 || column < 0 || row >= Rows || column >= Columns ? null : new NavigabilityCell(row, column);
    }

    /// <summary>
    /// The distance from a cell's centre to the centre of the nearest cell
    /// that isn't navigable, in metres; 0 for a cell that isn't navigable.
    /// The grid's edge isn't an obstacle, and a cell with no obstacle
    /// anywhere on the grid takes the grid's width plus height. Worked out
    /// once for the whole grid (a chamfer distance, within about 8 % of the
    /// straight one).
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>The clearance in metres.</returns>
    public double ClearanceMeters(int row, int column) => Clearance()[Index(row, column)] * CellMeters;

    /// <summary>
    /// The connected areas of navigable cells (cells joined through their
    /// four sides): each cell's area number, or −1 where it isn't navigable.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>The area number, or −1.</returns>
    public int ComponentAt(int row, int column) => Components().Labels[Index(row, column)];

    /// <summary>The number of cells in each connected navigable area, by area number.</summary>
    public IReadOnlyList<int> ComponentSizes => Components().Sizes;

    /// <summary>
    /// Whether two positions are joined through navigable cells, and if so
    /// the narrowest place on the widest way between them.
    /// </summary>
    /// <param name="from">The first position.</param>
    /// <param name="to">The second position.</param>
    /// <returns>The connection.</returns>
    public NavigabilityConnection Connect(GeoPosition from, GeoPosition to)
    {
        var a = CellAt(from);
        var b = CellAt(to);
        if (a is not { } start)
        {
            return new NavigabilityConnection(false, a, b, "from_off_grid");
        }

        if (b is not { } end)
        {
            return new NavigabilityConnection(false, a, b, "to_off_grid");
        }

        if (!IsNavigable(start.Row, start.Column))
        {
            return new NavigabilityConnection(false, a, b, "from_not_navigable");
        }

        if (!IsNavigable(end.Row, end.Column))
        {
            return new NavigabilityConnection(false, a, b, "to_not_navigable");
        }

        if (ComponentAt(start.Row, start.Column) != ComponentAt(end.Row, end.Column))
        {
            return new NavigabilityConnection(false, a, b, "no_navigable_way");
        }

        var (narrowest, clearance) = WidestWayBottleneck(start, end);
        return new NavigabilityConnection(true, a, b, null, narrowest, clearance);
    }

    /// <summary>Builds the grid from the loaded data.</summary>
    /// <param name="sources">The route-check sources: the water mask, charted depth and, for dangers, the charts.</param>
    /// <param name="request">The box, cell size and depths.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <returns>The grid.</returns>
    /// <exception cref="ArgumentException">The request is out of range: no cell size, no box, or more than <see cref="NavigabilityGridRequest.MaxCells"/> cells.</exception>
    public static NavigabilityGrid Build(RouteCheckSources sources, NavigabilityGridRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(request);
        if (!(request.CellMeters > 0) || !double.IsFinite(request.CellMeters))
        {
            throw new ArgumentException("The cell size must be a positive number of metres.", nameof(request));
        }

        if (!(request.Extent.NorthLatitude > request.Extent.SouthLatitude))
        {
            throw new ArgumentException("The box's north latitude must be greater than its south latitude.", nameof(request));
        }

        if (request.CellCount() > NavigabilityGridRequest.MaxCells)
        {
            throw new ArgumentException($"The grid would have more than {NavigabilityGridRequest.MaxCells} cells.", nameof(request));
        }

        var (rows, columns) = request.Size();
        var (latStep, lonStep, _) = request.Steps();
        var count = rows * columns;
        var south = request.Extent.SouthLatitude;
        var west = request.Extent.WestLongitude;
        var centres = new WaterMaskGrid(south + (latStep / 2), west + (lonStep / 2), latStep, lonStep, rows, columns);
        var corners = new WaterMaskGrid(south, west, latStep, lonStep, rows + 1, columns + 1);

        // Land and water at the centres.
        var water = new WaterMaskClass[count];
        if (sources.WaterMask is { } mask)
        {
            mask.Classify(centres, water);
        }
        else
        {
            Array.Fill(water, WaterMaskClass.Uncharted);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Least depth: the shoalest of the centre and the four corners.
        var depths = new float[count];
        Array.Fill(depths, float.NaN);
        if (sources.Depth is { } depthSource)
        {
            depthSource.Sample(centres, depths);
            cancellationToken.ThrowIfCancellationRequested();
            var cornerDepths = new float[corners.Count];
            depthSource.Sample(corners, cornerDepths);
            var stride = columns + 1;
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < columns; col++)
                {
                    var i = (row * columns) + col;
                    var c = (row * stride) + col;
                    var least = depths[i];
                    least = Shoaler(least, cornerDepths[c]);
                    least = Shoaler(least, cornerDepths[c + 1]);
                    least = Shoaler(least, cornerDepths[c + stride]);
                    least = Shoaler(least, cornerDepths[c + stride + 1]);
                    depths[i] = least;
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var hazards = new bool[count];
        var hazardDatasets = new SortedSet<string>(StringComparer.Ordinal);
        if (request.IncludeHazards && sources.Charts is { } charts)
        {
            MarkDangers(charts, request, centres, hazards, hazardDatasets, cancellationToken);
        }

        var minimum = request.MinimumDepthMeters;
        var classes = new NavigabilityCellClass[count];
        for (var i = 0; i < count; i++)
        {
            classes[i] = water[i] switch
            {
                WaterMaskClass.Uncharted => NavigabilityCellClass.Uncharted,
                WaterMaskClass.Land => NavigabilityCellClass.Land,
                _ when hazards[i] => NavigabilityCellClass.Hazard,
                _ when float.IsNaN(depths[i]) => NavigabilityCellClass.NoDepth,
                _ when depths[i] < minimum => NavigabilityCellClass.Shoal,
                _ => NavigabilityCellClass.Navigable,
            };
        }

        return new NavigabilityGrid(request, centres, classes, depths, hazards, [.. hazardDatasets]);
    }

    private static float Shoaler(float a, float b) => float.IsNaN(a) ? b : float.IsNaN(b) ? a : Math.Min(a, b);

    // Marks the cells a danger shallower than the hazard depth (or of unknown
    // depth) touches. Only dangers from the finest cells charting their
    // position count, as in the chart check.
    private static void MarkDangers(
        IRouteChartSource charts,
        NavigabilityGridRequest request,
        WaterMaskGrid centres,
        bool[] hazards,
        SortedSet<string> datasets,
        CancellationToken cancellationToken)
    {
        var extent = request.Extent;
        var east = extent.EastLongitude < extent.WestLongitude ? extent.EastLongitude + 360 : extent.EastLongitude;
        var ring = new[]
        {
            new GeoPosition(extent.SouthLatitude, extent.WestLongitude),
            new GeoPosition(extent.SouthLatitude, Wrap(east)),
            new GeoPosition(extent.NorthLatitude, Wrap(east)),
            new GeoPosition(extent.NorthLatitude, extent.WestLongitude),
            new GeoPosition(extent.SouthLatitude, extent.WestLongitude),
        };
        var threshold = request.HazardDepthMeters ?? request.MinimumDepthMeters;
        var cells = charts.CellsNear(extent);
        var features = charts.FeaturesInCorridor(ring, extent, RouteChartQuery.Default);
        foreach (var feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (feature.Kind != RouteChartFeatureKind.Danger
                || (feature.DepthMeters is { } depth && depth > threshold))
            {
                continue;
            }

            var marked = false;
            foreach (var position in Positions(feature, centres))
            {
                if (!IsFinestAt(feature.Cell, cells, position))
                {
                    continue;
                }

                var row = (int)Math.Floor(((position.Latitude - centres.South) / centres.LatitudeStep) + 0.5);
                var dLon = (((position.Longitude - centres.West) + 540) % 360) - 180;
                var col = (int)Math.Floor((dLon / centres.LongitudeStep) + 0.5);
                if (row >= 0 && col >= 0 && row < centres.Rows && col < centres.Columns)
                {
                    hazards[(row * centres.Columns) + col] = true;
                    marked = true;
                }
            }

            foreach (var surface in feature.Surfaces)
            {
                MarkSurface(feature, surface, cells, centres, hazards, ref marked);
            }

            if (marked)
            {
                datasets.Add(feature.Cell.DatasetId);
            }
        }
    }

    // A danger's points, and its curves' and surfaces' edges walked at a
    // third of a cell, so a long obstruction marks every cell it crosses.
    private static IEnumerable<GeoPosition> Positions(RouteChartFeature feature, WaterMaskGrid centres)
    {
        foreach (var p in feature.Points)
        {
            yield return p;
        }

        var step = Math.Min(centres.LatitudeStep, centres.LongitudeStep) / 3;
        foreach (var curve in feature.Curves)
        {
            foreach (var p in Walk(curve, step))
            {
                yield return p;
            }
        }

        foreach (var surface in feature.Surfaces)
        {
            foreach (var p in Walk(surface.ExteriorRing, step))
            {
                yield return p;
            }
        }
    }

    private static IEnumerable<GeoPosition> Walk(IReadOnlyList<GeoPosition> line, double step)
    {
        for (var k = 0; k < line.Count; k++)
        {
            yield return line[k];
            if (k + 1 >= line.Count)
            {
                break;
            }

            var a = line[k];
            var b = line[k + 1];
            var dLon = (((b.Longitude - a.Longitude) + 540) % 360) - 180;
            var n = (int)Math.Ceiling(Math.Max(Math.Abs(b.Latitude - a.Latitude), Math.Abs(dLon)) / step);
            for (var i = 1; i < n; i++)
            {
                var t = (double)i / n;
                yield return new GeoPosition(a.Latitude + ((b.Latitude - a.Latitude) * t), Wrap(a.Longitude + (dLon * t)));
            }
        }
    }

    // A danger area also marks the cells whose centres it contains.
    private static void MarkSurface(
        RouteChartFeature feature,
        SurfacePart surface,
        IReadOnlyList<RouteChartCell> cells,
        WaterMaskGrid centres,
        bool[] hazards,
        ref bool marked)
    {
        var ring = surface.ExteriorRing;
        if (ring.Count < 3)
        {
            return;
        }

        double south = double.MaxValue, north = double.MinValue, west = double.MaxValue, east = double.MinValue;
        foreach (var p in ring)
        {
            south = Math.Min(south, p.Latitude);
            north = Math.Max(north, p.Latitude);
            var lon = centres.West + ((((p.Longitude - centres.West) + 540) % 360) - 180);
            west = Math.Min(west, lon);
            east = Math.Max(east, lon);
        }

        var r0 = Math.Max(0, (int)Math.Ceiling((south - centres.South) / centres.LatitudeStep));
        var r1 = Math.Min(centres.Rows - 1, (int)Math.Floor((north - centres.South) / centres.LatitudeStep));
        var c0 = Math.Max(0, (int)Math.Ceiling((west - centres.West) / centres.LongitudeStep));
        var c1 = Math.Min(centres.Columns - 1, (int)Math.Floor((east - centres.West) / centres.LongitudeStep));
        for (var row = r0; row <= r1; row++)
        {
            for (var col = c0; col <= c1; col++)
            {
                var centre = centres.PositionAt(row, col);
                if (PlanarGeometry.Contains([surface], centre) && IsFinestAt(feature.Cell, cells, centre))
                {
                    hazards[(row * centres.Columns) + col] = true;
                    marked = true;
                }
            }
        }
    }

    private static bool IsFinestAt(RouteChartCell cell, IReadOnlyList<RouteChartCell> cells, GeoPosition position)
    {
        foreach (var other in cells)
        {
            if (!ReferenceEquals(other, cell)
                && CellScaleRanking.IsFiner(other.Rank, cell.Rank)
                && other.Covers(position))
            {
                return false;
            }
        }

        return true;
    }

    private static double Wrap(double lon) => ((lon + 540) % 360) - 180;

    private int Index(int row, int column)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, Columns);
        return (row * Columns) + column;
    }

    // Chamfer (3-4) distance in cells from each navigable cell to the nearest
    // one that isn't. The grid's edge isn't an obstacle (the water goes on
    // past it); a cell with no obstacle on the grid takes the grid's size.
    private float[] Clearance()
    {
        if (_clearance is { } built)
        {
            return built;
        }

        lock (_gate)
        {
            if (_clearance is { } again)
            {
                return again;
            }

            const int straight = 3, diagonal = 4;
            int rows = Rows, cols = Columns;
            var d = new int[rows * cols];
            for (var i = 0; i < d.Length; i++)
            {
                d[i] = _classes[i] == NavigabilityCellClass.Navigable ? int.MaxValue / 2 : 0;
            }

            const int far = int.MaxValue / 2;
            int At(int r, int c) => r < 0 || c < 0 || r >= rows || c >= cols ? far : d[(r * cols) + c];

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var i = (r * cols) + c;
                    if (d[i] == 0)
                    {
                        continue;
                    }

                    d[i] = Math.Min(d[i], Math.Min(
                        Math.Min(At(r, c - 1) + straight, At(r - 1, c) + straight),
                        Math.Min(At(r - 1, c - 1) + diagonal, At(r - 1, c + 1) + diagonal)));
                }
            }

            for (var r = rows - 1; r >= 0; r--)
            {
                for (var c = cols - 1; c >= 0; c--)
                {
                    var i = (r * cols) + c;
                    if (d[i] == 0)
                    {
                        continue;
                    }

                    d[i] = Math.Min(d[i], Math.Min(
                        Math.Min(At(r, c + 1) + straight, At(r + 1, c) + straight),
                        Math.Min(At(r + 1, c + 1) + diagonal, At(r + 1, c - 1) + diagonal)));
                }
            }

            var result = new float[d.Length];
            var cap = (float)(rows + cols);
            for (var i = 0; i < d.Length; i++)
            {
                result[i] = Math.Min(d[i] / (float)straight, cap);
            }

            return _clearance = result;
        }
    }

    private (int[] Labels, int[] Sizes) Components()
    {
        if (_components is { } labels && _componentSizes is { } sizes)
        {
            return (labels, sizes);
        }

        lock (_gate)
        {
            if (_components is { } l2 && _componentSizes is { } s2)
            {
                return (l2, s2);
            }

            int rows = Rows, cols = Columns;
            var result = new int[rows * cols];
            Array.Fill(result, -1);
            var counts = new List<int>();
            var stack = new Stack<int>();
            for (var seed = 0; seed < result.Length; seed++)
            {
                if (result[seed] != -1 || _classes[seed] != NavigabilityCellClass.Navigable)
                {
                    continue;
                }

                var label = counts.Count;
                var size = 0;
                result[seed] = label;
                stack.Push(seed);
                while (stack.TryPop(out var i))
                {
                    size++;
                    int r = i / cols, c = i % cols;
                    Visit(r - 1, c);
                    Visit(r + 1, c);
                    Visit(r, c - 1);
                    Visit(r, c + 1);
                }

                counts.Add(size);

                void Visit(int r, int c)
                {
                    if (r < 0 || c < 0 || r >= rows || c >= cols)
                    {
                        return;
                    }

                    var j = (r * cols) + c;
                    if (result[j] == -1 && _classes[j] == NavigabilityCellClass.Navigable)
                    {
                        result[j] = label;
                        stack.Push(j);
                    }
                }
            }

            _componentSizes = [.. counts];
            return (_components = result, _componentSizes);
        }
    }

    // The way between two cells of one area that keeps the most clearance:
    // a maximin search, cells taken widest first. Returns where that way is
    // narrowest.
    private (NavigabilityCell Cell, double ClearanceMeters) WidestWayBottleneck(NavigabilityCell start, NavigabilityCell end)
    {
        var clearance = Clearance();
        int cols = Columns, rows = Rows;
        var best = new float[clearance.Length];
        var via = new int[clearance.Length];
        Array.Fill(best, -1);
        Array.Fill(via, -1);
        var s = (start.Row * cols) + start.Column;
        var e = (end.Row * cols) + end.Column;
        var queue = new PriorityQueue<int, float>();
        best[s] = clearance[s];
        queue.Enqueue(s, -best[s]);
        while (queue.TryDequeue(out var i, out var negative))
        {
            if (-negative < best[i])
            {
                continue;
            }

            if (i == e)
            {
                break;
            }

            int r = i / cols, c = i % cols;
            ReadOnlySpan<(int Dr, int Dc)> moves = [(1, 0), (-1, 0), (0, 1), (0, -1)];
            foreach (var (dr, dc) in moves)
            {
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nc < 0 || nr >= rows || nc >= cols)
                {
                    continue;
                }

                var j = (nr * cols) + nc;
                if (_classes[j] != NavigabilityCellClass.Navigable)
                {
                    continue;
                }

                var width = Math.Min(best[i], clearance[j]);
                if (width > best[j])
                {
                    best[j] = width;
                    via[j] = i;
                    queue.Enqueue(j, -width);
                }
            }
        }

        // Many ways share the widest bottleneck, and one that hugs a shore is
        // as narrow there as the bottleneck itself. Of the ways at least that
        // wide, take the one that keeps clear of shores (each step costs more
        // the closer it is to one), so its narrowest cell is the bottleneck a
        // boat would pass, not a shore it needn't go near.
        var bottleneck = best[e];
        var from = new int[clearance.Length];
        var cost = new double[clearance.Length];
        Array.Fill(from, -1);
        Array.Fill(cost, double.PositiveInfinity);
        var frontier = new PriorityQueue<int, double>();
        from[s] = s;
        cost[s] = 0;
        frontier.Enqueue(s, 0);
        while (frontier.TryDequeue(out var i, out var spent))
        {
            if (spent > cost[i])
            {
                continue;
            }

            if (i == e)
            {
                break;
            }

            int r = i / cols, c = i % cols;
            ReadOnlySpan<(int Dr, int Dc)> moves = [(1, 0), (-1, 0), (0, 1), (0, -1)];
            foreach (var (dr, dc) in moves)
            {
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nc < 0 || nr >= rows || nc >= cols)
                {
                    continue;
                }

                var j = (nr * cols) + nc;
                if (_classes[j] != NavigabilityCellClass.Navigable || clearance[j] < bottleneck)
                {
                    continue;
                }

                var next = spent + 1 + (4 / Math.Max(clearance[j], 0.25));
                if (next < cost[j])
                {
                    cost[j] = next;
                    from[j] = i;
                    frontier.Enqueue(j, next);
                }
            }
        }

        var narrowest = e;
        for (var k = e; ; k = from[k])
        {
            if (clearance[k] < clearance[narrowest])
            {
                narrowest = k;
            }

            if (k == s || from[k] == -1)
            {
                break;
            }
        }

        return (new NavigabilityCell(narrowest / cols, narrowest % cols), clearance[narrowest] * CellMeters);
    }
}
