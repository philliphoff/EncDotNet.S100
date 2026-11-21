using System.Collections.Concurrent;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>What a <see cref="IWaterMask"/> says is at a point.</summary>
public enum WaterMaskClass
{
    /// <summary>A loaded chart covers the point, and it isn't land there.</summary>
    Water,

    /// <summary>A loaded chart charts land at the point.</summary>
    Land,

    /// <summary>No loaded chart covers the point.</summary>
    Uncharted,
}

/// <summary>
/// Where the loaded charts have water and land: the mask a station's reach is
/// measured through (<see cref="WaterReach"/>), and that the adjusted-area
/// edge is clipped to.
/// </summary>
/// <remarks>
/// <para>
/// <c>EncDotNet.S100.Datasets.Pipelines</c> implements it over loaded S-101
/// cells (and S-401, and translated S-57): a point is land when the finest
/// cell covering it charts a <c>LandArea</c> there
/// (<see cref="SkinOfTheEarth.LandAreaTypes"/>).
/// </para>
/// <para>Implementations must be safe to call from several threads at once.</para>
/// </remarks>
public interface IWaterMask
{
    /// <summary>Classifies a point.</summary>
    /// <param name="position">The point, in WGS-84.</param>
    /// <returns>Water, land, or uncharted.</returns>
    WaterMaskClass Classify(GeoPosition position);

    /// <summary>
    /// Classifies every point of a regular latitude and longitude grid, as
    /// <see cref="Classify(GeoPosition)"/> would one at a time. An
    /// implementation can do it much faster at once, by filling polygons row
    /// by row.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <param name="result">Receives the classes, row by row from the south-west; at least <see cref="WaterMaskGrid.Count"/> long.</param>
    void Classify(WaterMaskGrid grid, Span<WaterMaskClass> result)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(result.Length, grid.Count, nameof(result));
        for (var row = 0; row < grid.Rows; row++)
        {
            for (var col = 0; col < grid.Columns; col++)
            {
                result[(row * grid.Columns) + col] = Classify(grid.PositionAt(row, col));
            }
        }
    }
}

/// <summary>A regular grid of points in latitude and longitude, for <see cref="IWaterMask.Classify(WaterMaskGrid, Span{WaterMaskClass})"/>.</summary>
/// <param name="South">The latitude of row 0, in degrees.</param>
/// <param name="West">The longitude of column 0, in degrees.</param>
/// <param name="LatitudeStep">Degrees of latitude between rows; positive.</param>
/// <param name="LongitudeStep">Degrees of longitude between columns; positive.</param>
/// <param name="Rows">The number of rows.</param>
/// <param name="Columns">The number of columns.</param>
public readonly record struct WaterMaskGrid(double South, double West, double LatitudeStep, double LongitudeStep, int Rows, int Columns)
{
    /// <summary>The number of points.</summary>
    public int Count => Rows * Columns;

    /// <summary>The latitude of the last row.</summary>
    public double North => South + ((Rows - 1) * LatitudeStep);

    /// <summary>The longitude of the last column, which may be past 180° when the grid crosses the antimeridian.</summary>
    public double East => West + ((Columns - 1) * LongitudeStep);

    /// <summary>The point at a row and column, with its longitude in −180…180°.</summary>
    /// <param name="row">The row, from the south.</param>
    /// <param name="column">The column, from the west.</param>
    /// <returns>The point.</returns>
    public GeoPosition PositionAt(int row, int column)
    {
        var lon = West + (column * LongitudeStep);
        lon = ((lon + 540) % 360) - 180;
        return new GeoPosition(South + (row * LatitudeStep), lon);
    }
}

/// <summary>
/// How far a station's water level reaches, measured through charted water:
/// the distance by water from a station to a point, or <c>null</c> beyond
/// the reach.
/// </summary>
/// <remarks>
/// <para>
/// A station's value applies only within its reach (10 km by default). Land
/// on the loaded charts blocks it, so a station doesn't reach across a
/// headland into the next basin; where no chart covers the gap, the distance
/// is a straight line, as it is everywhere when there's no mask (tide
/// predictions design D19). S-104 stations and predicted stations follow the
/// same rule.
/// </para>
/// <para>
/// Each station's distances are worked out once, on a grid of
/// <see cref="CellSizeMeters"/> cells around it, and kept. A cell the station
/// sees in a straight line over water gets the geodesic distance; a cell in
/// the lee of land gets the shortest path round it through water cells
/// (16 directions, so the path is within about 3 % of the true one). Land
/// within <see cref="StationLandToleranceMeters"/> of the station is passable,
/// since a gauge on a pier can sit on a land area of a small-scale chart. A
/// point on a land cell (a sample on the shoreline, or a track over a spit)
/// takes the distance of the nearest water cell within about 1 km of it,
/// plus the way to it over land.
/// </para>
/// <para>Safe to call from several threads at once.</para>
/// </remarks>
public sealed class WaterReach
{
    /// <summary>The default reach: 10 km, as S-104 stations use.</summary>
    public const double DefaultReachMeters = 10_000;

    /// <summary>The default grid cell: 100 m.</summary>
    public const double DefaultCellSizeMeters = 100;

    /// <summary>Land closer than this to a station doesn't block it.</summary>
    public const double StationLandToleranceMeters = 300;

    // How far, in cells, a point on land looks for the water nearest it.
    private const int LandSearchCells = 10;

    private const double EarthRadiusMeters = 6_371_008.8;

    private readonly IWaterMask? _mask;
    private readonly ConcurrentDictionary<(double Latitude, double Longitude), Lazy<Field>> _fields = new();

    /// <summary>Creates the reach.</summary>
    /// <param name="mask">The water mask, or <c>null</c> to measure in straight lines.</param>
    /// <param name="reachMeters">How far a station reaches, in metres.</param>
    /// <param name="cellSizeMeters">The grid cell the distances are worked out on, in metres.</param>
    /// <exception cref="ArgumentOutOfRangeException">A distance isn't positive.</exception>
    public WaterReach(IWaterMask? mask, double reachMeters = DefaultReachMeters, double cellSizeMeters = DefaultCellSizeMeters)
    {
        if (!(reachMeters > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(reachMeters), reachMeters, "The reach must be positive.");
        }

        if (!(cellSizeMeters > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(cellSizeMeters), cellSizeMeters, "The cell size must be positive.");
        }

        _mask = mask;
        ReachMeters = reachMeters;
        CellSizeMeters = cellSizeMeters;
    }

    /// <summary>How far a station reaches, in metres.</summary>
    public double ReachMeters { get; }

    /// <summary>The grid cell the distances are worked out on, in metres.</summary>
    public double CellSizeMeters { get; }

    /// <summary>Whether distances go round charted land (a mask was given).</summary>
    public bool ByWater => _mask is not null;

    /// <summary>The distance from a station to a point, by water.</summary>
    /// <param name="station">The station's position.</param>
    /// <param name="point">The point.</param>
    /// <returns>
    /// The distance in metres, at least the geodesic one, or <c>null</c> when
    /// the point is beyond <see cref="ReachMeters"/> by water (or land cuts it
    /// off from the station).
    /// </returns>
    public double? DistanceMeters(GeoPosition station, GeoPosition point)
    {
        var straight = PassageGeodesy.GreatCircleDistanceMeters(station, point);
        if (straight > ReachMeters)
        {
            return null;
        }

        if (_mask is null)
        {
            return straight;
        }

        var field = _fields.GetOrAdd(
            (station.Latitude, station.Longitude),
            key => new Lazy<Field>(() => new Field(_mask, new GeoPosition(key.Latitude, key.Longitude), ReachMeters, CellSizeMeters), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return field.DistanceMeters(point, straight) is { } d && d <= ReachMeters ? d : null;
    }

    private sealed class Field
    {
        // Moves to the 16 neighbours: the 8 next to a cell and the 8 a knight's move away.
        private static readonly (int Dx, int Dy)[] Moves =
        [
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1),
            (2, 1), (2, -1), (-2, 1), (-2, -1),
            (1, 2), (1, -2), (-1, 2), (-1, -2),
        ];

        private readonly GeoPosition _station;
        private readonly double _cell;
        private readonly double _metersPerDegreeLon;
        private readonly int _half;
        private readonly int _size;
        private readonly bool[] _blocked;
        private readonly bool[] _inSight;
        private readonly double[] _distance;

        public Field(IWaterMask mask, GeoPosition station, double reach, double cell)
        {
            _station = station;
            _cell = cell;
            _metersPerDegreeLon = EarthRadiusMeters * Math.PI / 180 * Math.Max(Math.Cos(station.Latitude * Math.PI / 180), 1e-6);
            _half = (int)Math.Ceiling(reach / cell) + 1;
            _size = (2 * _half) + 1;
            var count = _size * _size;
            _blocked = new bool[count];
            _inSight = new bool[count];
            _distance = new double[count];
            Array.Fill(_distance, double.PositiveInfinity);

            var degreesPerMeterLat = 180 / (EarthRadiusMeters * Math.PI);
            var southWest = ToGeo(-_half * cell, -_half * cell);
            var grid = new WaterMaskGrid(southWest.Latitude, southWest.Longitude, cell * degreesPerMeterLat, cell / _metersPerDegreeLon, _size, _size);
            var classes = new WaterMaskClass[count];
            mask.Classify(grid, classes);
            var tolerance = StationLandToleranceMeters / cell;
            for (var row = 0; row < _size; row++)
            {
                for (var col = 0; col < _size; col++)
                {
                    int dx = col - _half, dy = row - _half;
                    var i = Index(row, col);
                    _blocked[i] = classes[i] == WaterMaskClass.Land && Math.Sqrt((dx * dx) + (dy * dy)) > tolerance;
                }
            }

            // Cells the station sees over water take the straight distance,
            // which is final; the rest are reached round the land from them.
            var queue = new PriorityQueue<int, double>();
            for (var row = 0; row < _size; row++)
            {
                for (var col = 0; col < _size; col++)
                {
                    var i = Index(row, col);
                    if (_blocked[i] || !Clear(_half, _half, row, col))
                    {
                        continue;
                    }

                    int dx = col - _half, dy = row - _half;
                    _inSight[i] = true;
                    _distance[i] = Math.Sqrt((dx * dx) + (dy * dy)) * cell;
                    queue.Enqueue(i, _distance[i]);
                }
            }

            while (queue.TryDequeue(out var i, out var d))
            {
                if (d > _distance[i] || d > reach + cell)
                {
                    continue;
                }

                int row = i / _size, col = i % _size;
                foreach (var (mx, my) in Moves)
                {
                    int r = row + my, c = col + mx;
                    if (r < 0 || c < 0 || r >= _size || c >= _size)
                    {
                        continue;
                    }

                    var j = Index(r, c);
                    if (_blocked[j] || _inSight[j])
                    {
                        continue;
                    }

                    var next = d + (Math.Sqrt((mx * mx) + (my * my)) * cell);
                    if (next < _distance[j] && Clear(row, col, r, c))
                    {
                        _distance[j] = next;
                        queue.Enqueue(j, next);
                    }
                }
            }
        }

        public double? DistanceMeters(GeoPosition point, double straight)
        {
            var (x, y) = ToLocal(point);
            var col = (int)Math.Round(x / _cell) + _half;
            var row = (int)Math.Round(y / _cell) + _half;
            if (row < 0 || col < 0 || row >= _size || col >= _size)
            {
                return null;
            }

            var i = Index(row, col);
            if (!_blocked[i])
            {
                return _inSight[i] ? straight : Finite(Math.Max(_distance[i], straight));
            }

            // A point on a land cell: the shoreline, or a track over a spit.
            // It takes the reach of the water nearest it, plus the way over
            // land to it, looking out ring by ring up to LandSearchCells.
            for (var ring = 1; ring <= LandSearchCells; ring++)
            {
                var best = double.PositiveInfinity;
                for (var dr = -ring; dr <= ring; dr++)
                {
                    for (var dc = -ring; dc <= ring; dc++)
                    {
                        int r = row + dr, c = col + dc;
                        if ((Math.Abs(dr) != ring && Math.Abs(dc) != ring) || r < 0 || c < 0 || r >= _size || c >= _size)
                        {
                            continue;
                        }

                        var j = Index(r, c);
                        if (_blocked[j] || !double.IsFinite(_distance[j]))
                        {
                            continue;
                        }

                        var over = ring > 1 ? Math.Sqrt((dr * dr) + (dc * dc)) * _cell : 0;
                        best = Math.Min(best, Math.Max(_distance[j] + over, straight));
                    }
                }

                if (double.IsFinite(best))
                {
                    return best;
                }
            }

            return null;
        }

        private static double? Finite(double d) => double.IsFinite(d) ? d : null;

        private int Index(int row, int col) => (row * _size) + col;

        // Whether the straight line between two cell centres crosses no
        // blocked cell, stepping a third of a cell at a time.
        private bool Clear(int r0, int c0, int r1, int c1)
        {
            int dr = r1 - r0, dc = c1 - c0;
            var steps = 3 * Math.Max(Math.Abs(dr), Math.Abs(dc));
            for (var k = 1; k < steps; k++)
            {
                var r = (int)Math.Round(r0 + ((double)dr * k / steps));
                var c = (int)Math.Round(c0 + ((double)dc * k / steps));
                if (_blocked[Index(r, c)])
                {
                    return false;
                }
            }

            return true;
        }

        private (double X, double Y) ToLocal(GeoPosition p)
        {
            var dLon = p.Longitude - _station.Longitude;
            dLon = ((dLon + 540) % 360) - 180;
            return (dLon * _metersPerDegreeLon, (p.Latitude - _station.Latitude) * EarthRadiusMeters * Math.PI / 180);
        }

        private GeoPosition ToGeo(double x, double y)
        {
            var lon = _station.Longitude + (x / _metersPerDegreeLon);
            lon = ((lon + 540) % 360) - 180;
            return new GeoPosition(_station.Latitude + (y / (EarthRadiusMeters * Math.PI / 180)), lon);
        }
    }
}
