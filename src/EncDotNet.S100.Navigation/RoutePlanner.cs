using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>What a <see cref="RoutePlanner"/> plans.</summary>
/// <param name="Stops">The stops in order: the departure, any via points, and the arrival. At least two.</param>
/// <param name="MinimumDepthMeters">The least charted depth the route keeps to away from the stops: the larger of the draft (plus margin) and the safety contour.</param>
public sealed record RoutePlanRequest(IReadOnlyList<GeoPosition> Stops, double MinimumDepthMeters)
{
    /// <summary>The default berth radius: 300 m.</summary>
    public const double DefaultBerthRadiusMeters = 300;

    /// <summary>The default comfort distance: 200 m.</summary>
    public const double DefaultComfortMeters = 200;

    /// <summary>
    /// The least room the route keeps from anything that isn't navigable
    /// (land, shoal water, dangers, structures, uncharted water), in metres,
    /// away from the stops. 0 (the default) allows any navigable cell.
    /// </summary>
    public double ClearanceMeters { get; init; }

    /// <summary>
    /// Closer than this to anything that isn't navigable costs more, so the
    /// route keeps to open water where there's room; in metres.
    /// </summary>
    public double ComfortMeters { get; init; } = DefaultComfortMeters;

    /// <summary>Dangers at or shallower than this block their cells; <c>null</c> uses <see cref="MinimumDepthMeters"/>.</summary>
    public double? HazardDepthMeters { get; init; }

    /// <summary>
    /// Within this distance of a stop, shallower water down to
    /// <see cref="BerthDepthMeters"/> is allowed and the clearance isn't
    /// required, so a route can reach a berth or anchorage; in metres.
    /// </summary>
    public double BerthRadiusMeters { get; init; } = DefaultBerthRadiusMeters;

    /// <summary>The least charted depth allowed within the berth radius; <c>null</c> uses <see cref="MinimumDepthMeters"/>.</summary>
    public double? BerthDepthMeters { get; init; }

    /// <summary>How far a stop that isn't navigable may move to navigable water, in metres.</summary>
    public double SnapRadiusMeters { get; init; } = NavigableSnap.DefaultSearchRadiusMeters;

    /// <summary>The grid's cell size in metres; <c>null</c> picks one per leg (<see cref="RoutePlanner.DefaultCellMeters"/>, coarser when a leg's box would exceed <see cref="MaxCellsPerLeg"/>).</summary>
    public double? CellMeters { get; init; }

    /// <summary>The most cells one leg's grid may have.</summary>
    public int MaxCellsPerLeg { get; init; } = RoutePlanner.DefaultMaxCellsPerLeg;

    /// <summary>Areas the route prefers, such as charted fairways and dredged areas.</summary>
    public IReadOnlyList<SurfacePart> PreferredAreas { get; init; } = [];

    /// <summary>Lines the route prefers to follow, such as recommended tracks.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> PreferredLines { get; init; } = [];

    /// <summary>The cost of a preferred cell relative to an ordinary one, in (0, 1].</summary>
    public double PreferredCostFactor { get; init; } = 0.5;
}

/// <summary>A stop of a <see cref="RoutePlan"/>: where it was asked for and where the route goes.</summary>
/// <param name="Index">The stop's index in <see cref="RoutePlanRequest.Stops"/>.</param>
/// <param name="Requested">The position asked for.</param>
/// <param name="Position">The position the route uses: the one asked for, or the nearest navigable.</param>
/// <param name="Moved">Whether the stop moved.</param>
/// <param name="Reason">Why it moved (<see cref="NavigableSnapResult.Reason"/>); <c>null</c> when it didn't.</param>
/// <param name="DistanceMeters">How far it moved, in metres.</param>
/// <param name="BearingDegrees">The bearing it moved on, degrees true.</param>
/// <param name="DepthMeters">The least charted depth round <paramref name="Position"/>, in metres.</param>
public sealed record RoutePlanStop(
    int Index,
    GeoPosition Requested,
    GeoPosition Position,
    bool Moved,
    string? Reason,
    double DistanceMeters,
    double BearingDegrees,
    double? DepthMeters);

/// <summary>A leg of a <see cref="RoutePlan"/>: the way from one stop to the next.</summary>
/// <param name="From">The index of the stop it starts at.</param>
/// <param name="To">The index of the stop it ends at.</param>
/// <param name="Points">The waypoints, from the one stop to the other.</param>
/// <param name="DistanceMeters">Its length along rhumb lines, in metres.</param>
/// <param name="LeastClearanceMeters">The least room along it, away from the stops, in metres.</param>
/// <param name="LeastDepthMeters">The least charted depth along it, away from the stops, in metres.</param>
/// <param name="CellMeters">The grid's cell size it was planned on, in metres.</param>
public sealed record RoutePlanLeg(
    int From,
    int To,
    IReadOnlyList<GeoPosition> Points,
    double DistanceMeters,
    double? LeastClearanceMeters,
    double? LeastDepthMeters,
    double CellMeters);

/// <summary>A planned route through a list of stops.</summary>
/// <param name="Stops">The stops, with where the route uses them.</param>
/// <param name="Legs">The legs between consecutive stops.</param>
/// <param name="Waypoints">The whole route's waypoints, the legs joined at the stops.</param>
/// <param name="DistanceMeters">The route's length, in metres.</param>
public sealed record RoutePlan(
    IReadOnlyList<RoutePlanStop> Stops,
    IReadOnlyList<RoutePlanLeg> Legs,
    IReadOnlyList<GeoPosition> Waypoints,
    double DistanceMeters);

/// <summary>Why a <see cref="RoutePlanner"/> couldn't plan.</summary>
/// <param name="Reason">
/// <c>stop_not_navigable</c> (a stop has no navigable water within the snap
/// radius) or <c>no_navigable_way</c> (no way through navigable water joins
/// two stops).
/// </param>
/// <param name="StopIndex">The stop, for <c>stop_not_navigable</c>.</param>
/// <param name="LegIndex">The leg, for <c>no_navigable_way</c>.</param>
/// <param name="Message">What to try.</param>
public sealed record RoutePlanFailure(string Reason, int? StopIndex, int? LegIndex, string Message);

/// <summary>A <see cref="RoutePlan"/>, or why there's none.</summary>
/// <param name="Plan">The plan, or <c>null</c>.</param>
/// <param name="Failure">Why there's no plan, or <c>null</c>.</param>
public sealed record RoutePlanResult(RoutePlan? Plan, RoutePlanFailure? Failure);

/// <summary>
/// Suggests a route through a list of stops (#1560): a least-cost way through
/// the navigable cells of a <see cref="NavigabilityGrid"/>, costing distance
/// and closeness to anything that isn't navigable, then cut to few waypoints
/// by line of sight.
/// </summary>
/// <remarks>
/// <para>
/// Each stop is first moved to navigable water as <see cref="NavigableSnap"/>
/// moves a point, at the berth depth: a stop in a marina stays in it. Each
/// leg is then searched on its own grid over the two stops' box, padded so
/// the way can go round an island, and padded further when it can't.
/// Within <see cref="RoutePlanRequest.BerthRadiusMeters"/> of a stop, water
/// down to the berth depth is passable and the clearance isn't required.
/// </para>
/// <para>
/// The search (A*, eight neighbours, no cutting a blocked corner) costs each
/// step its length, more within <see cref="RoutePlanRequest.ComfortMeters"/>
/// of the nearest cell that isn't navigable, and less in a preferred area.
/// The waypoints keep only the turns a straight line can't skip: a shortcut
/// must stay in passable cells and keep at least the room of the way it
/// replaces.
/// </para>
/// <para>
/// The grid is the chart check's reading of the charts, so a planned route
/// should check clean; it isn't checked here. Not for navigation.
/// </para>
/// </remarks>
public static class RoutePlanner
{
    /// <summary>The default cell size: 20 m.</summary>
    public const double DefaultCellMeters = 20;

    /// <summary>The default most cells per leg's grid: 3,000,000.</summary>
    public const int DefaultMaxCellsPerLeg = 3_000_000;

    // How much more a step right beside an obstacle costs than one in open water.
    private const double ClosePenalty = 3;

    // How many shortcuts in a row may fail before the simplifier stops looking further along.
    private const int LookAhead = 256;

    /// <summary>Plans a route through the stops.</summary>
    /// <param name="sources">The route-check sources: water mask, charted depth and charts.</param>
    /// <param name="request">The stops, depths and preferences.</param>
    /// <param name="cancellationToken">Cancels the planning.</param>
    /// <returns>The plan, or why there's none.</returns>
    /// <exception cref="ArgumentException">Fewer than two stops, or a value out of range.</exception>
    public static RoutePlanResult Plan(RouteCheckSources sources, RoutePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var berthDepth = Math.Min(request.BerthDepthMeters ?? request.MinimumDepthMeters, request.MinimumDepthMeters);
        var hazardDepth = request.HazardDepthMeters ?? request.MinimumDepthMeters;

        var stops = new List<RoutePlanStop>(request.Stops.Count);
        for (var i = 0; i < request.Stops.Count; i++)
        {
            var requested = request.Stops[i];
            var snap = NavigableSnap.Snap(
                sources,
                requested,
                new NavigableSnapRequest(berthDepth, 0, request.SnapRadiusMeters, hazardDepth),
                cancellationToken);
            if (snap.Position is not { } used)
            {
                return Fail(new RoutePlanFailure(
                    "stop_not_navigable",
                    i,
                    null,
                    FormattableString.Invariant($"Stop {i} ({requested.Latitude:0.#####}, {requested.Longitude:0.#####}) is {snap.Reason}, and nothing within {request.SnapRadiusMeters:0} m is charted water at least {berthDepth:0.##} m deep. Move it, or widen the snap radius.")));
            }

            stops.Add(new RoutePlanStop(
                i,
                requested,
                used,
                snap.Status == NavigableSnapStatus.Moved,
                snap.Reason,
                snap.DistanceMeters,
                snap.BearingDegrees,
                snap.DepthMeters));
        }

        var legs = new List<RoutePlanLeg>(stops.Count - 1);
        for (var i = 0; i + 1 < stops.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PlanLeg(sources, request, stops[i], stops[i + 1], berthDepth, hazardDepth, cancellationToken) is not { } leg)
            {
                return Fail(new RoutePlanFailure(
                    "no_navigable_way",
                    null,
                    i,
                    FormattableString.Invariant($"No way through water at least {request.MinimumDepthMeters:0.##} m deep{(request.ClearanceMeters > 0 ? FormattableString.Invariant($" with {request.ClearanceMeters:0} m of room") : string.Empty)} joins stop {i} and stop {i + 1} on the loaded charts. Check the charts cover the way between them, add a via point, or lower the depth or clearance.")));
            }

            legs.Add(leg);
        }

        var waypoints = new List<GeoPosition>();
        foreach (var leg in legs)
        {
            waypoints.AddRange(waypoints.Count == 0 ? leg.Points : leg.Points.Skip(1));
        }

        return new RoutePlanResult(new RoutePlan(stops, legs, waypoints, legs.Sum(l => l.DistanceMeters)), null);

        static RoutePlanResult Fail(RoutePlanFailure failure) => new(null, failure);
    }

    private static void Validate(RoutePlanRequest request)
    {
        if (request.Stops is null || request.Stops.Count < 2)
        {
            throw new ArgumentException("A route needs at least two stops.", nameof(request));
        }

        if (!double.IsFinite(request.MinimumDepthMeters))
        {
            throw new ArgumentException("The minimum depth must be a number.", nameof(request));
        }

        if (!(request.ClearanceMeters >= 0) || !(request.ComfortMeters >= 0) || !(request.BerthRadiusMeters >= 0))
        {
            throw new ArgumentException("The clearance, comfort and berth radius must be zero or more.", nameof(request));
        }

        if (!(request.SnapRadiusMeters > 0) || request.SnapRadiusMeters > NavigableSnap.MaxSearchRadiusMeters)
        {
            throw new ArgumentException($"The snap radius must be more than 0 and at most {NavigableSnap.MaxSearchRadiusMeters} m.", nameof(request));
        }

        if (request.CellMeters is { } cell && !(cell >= 1))
        {
            throw new ArgumentException("The cell size must be at least 1 m.", nameof(request));
        }

        if (!(request.PreferredCostFactor > 0) || request.PreferredCostFactor > 1)
        {
            throw new ArgumentException("The preferred cost factor must be in (0, 1].", nameof(request));
        }
    }


    // The cell size round a stop, where berths and marina entrances are.
    private const double ApproachCellMeters = 5;

    // How far beyond the berth radius the fine grid round a stop reaches.
    private const double ApproachMarginMeters = 700;

    // The most cells a leg searched on one fine grid may have.
    private const int SingleGridCells = 1_500_000;

    // One leg: on one fine grid when it's short, else a fine grid round each
    // stop joined across a coarse grid that widens until a way is found.
    private static RoutePlanLeg? PlanLeg(
        RouteCheckSources sources,
        RoutePlanRequest request,
        RoutePlanStop from,
        RoutePlanStop to,
        double berthDepth,
        double hazardDepth,
        CancellationToken cancellationToken)
    {
        GeoPosition a = from.Position, b = to.Position;
        GeoPosition[] ends = [a, b];
        var length = PassageGeodesy.GreatCircleDistanceMeters(a, b);
        var fineCell = Math.Min(request.CellMeters ?? ApproachCellMeters, ApproachCellMeters);
        var approach = request.BerthRadiusMeters + ApproachMarginMeters;

        var single = new NavigabilityGridRequest(Pad(a, b, approach), fineCell, request.MinimumDepthMeters, hazardDepth);
        if (single.CellCount() <= SingleGridCells)
        {
            var field = new Field(NavigabilityGrid.Build(sources, single, cancellationToken), request, ends, berthDepth);
            if (field.CellOf(a) is { } s && field.CellOf(b) is { } t
                && field.Search([(s, 0)], i => i == t ? 0 : double.PositiveInfinity, t, cancellationToken) is { } path)
            {
                return Leg(from, to, field.Points(field.Simplify(path), a, b), [(field, path)]);
            }

            if (length < approach)
            {
                return null;
            }
        }

        var fineA = BuildApproach(sources, request, a, ends, berthDepth, hazardDepth, fineCell, approach, cancellationToken);
        var fineB = BuildApproach(sources, request, b, ends, berthDepth, hazardDepth, fineCell, approach, cancellationToken);
        if (fineA is null || fineB is null)
        {
            return null;
        }

        // First the usual cells over a box with room to round an island; then,
        // as a narrow channel can close on those, a tighter box on the finest
        // cells the budget allows; then wider boxes.
        var pad = Math.Max(3_000, length * 0.25);
        (double Pad, double? Cell)[] attempts =
        [
            (pad, request.CellMeters ?? DefaultCellMeters),
            (Math.Max(1_000, length * 0.1), request.CellMeters is null ? ApproachCellMeters : null),
            (pad * 2.5, request.CellMeters ?? DefaultCellMeters),
            (pad * 6, request.CellMeters ?? DefaultCellMeters),
        ];
        var tried = new HashSet<(double, double)>();
        foreach (var (attemptPad, wanted) in attempts)
        {
            if (wanted is not { } wantedCell)
            {
                continue;
            }

            var box = Pad(a, b, attemptPad);
            var gridRequest = new NavigabilityGridRequest(box, 1, request.MinimumDepthMeters, hazardDepth);
            var cell = Math.Max(wantedCell, Math.Ceiling(gridRequest.CellMetersFor(Math.Min(request.MaxCellsPerLeg, NavigabilityGridRequest.MaxCells))));
            if (!tried.Add((attemptPad, cell)))
            {
                continue;
            }

            var coarse = new Field(NavigabilityGrid.Build(sources, gridRequest with { CellMeters = cell }, cancellationToken), request, ends, berthDepth);

            // The coarse cells the fine grids reach from (and to) the stops, at the fine cost.
            var seeds = fineA.Reached(coarse);
            var goals = fineB.Reached(coarse).ToDictionary(g => g.Cell, g => g.Cost);
            if (seeds.Count == 0 || goals.Count == 0 || coarse.CellOf(b) is not { } target)
            {
                continue;
            }

            var middle = coarse.Search(seeds, i => goals.TryGetValue(i, out var c) ? c : double.PositiveInfinity, target, cancellationToken);
            if (middle is null)
            {
                continue;
            }

            var outbound = fineA.PathTo(coarse.Centre(middle[0]));
            var inbound = fineB.PathTo(coarse.Centre(middle[^1]));
            inbound.Reverse();

            var points = new List<GeoPosition>();
            points.AddRange(fineA.Field.Points(fineA.Field.Simplify(outbound), a, coarse.Centre(middle[0])));
            points.AddRange(coarse.Points(coarse.Simplify(middle), coarse.Centre(middle[0]), coarse.Centre(middle[^1])).Skip(1));
            points.AddRange(fineB.Field.Points(fineB.Field.Simplify(inbound), coarse.Centre(middle[^1]), b).Skip(1));
            var fields = new[] { (fineA.Field, outbound), (coarse, middle), (fineB.Field, inbound) };
            return Leg(from, to, Smooth(points, fields), fields);
        }

        return null;
    }

    // The fine grid round a stop, flooded from it.
    private static Approach? BuildApproach(
        RouteCheckSources sources,
        RoutePlanRequest request,
        GeoPosition stop,
        GeoPosition[] ends,
        double berthDepth,
        double hazardDepth,
        double cell,
        double half,
        CancellationToken cancellationToken)
    {
        var grid = NavigabilityGrid.Build(
            sources,
            new NavigabilityGridRequest(NavigableSnap.Box(stop, half), cell, request.MinimumDepthMeters, hazardDepth),
            cancellationToken);
        var field = new Field(grid, request, ends, berthDepth);
        if (field.CellOf(stop) is not { } start)
        {
            return null;
        }

        var (cost, parent) = field.Flood(start, cancellationToken);
        return new Approach(field, cost, parent, stop, half);
    }

    // Drops a joining point the straight line past it doesn't need, on every grid that holds the line.
    private static List<GeoPosition> Smooth(List<GeoPosition> points, (Field Field, List<int> Path)[] fields)
    {
        var result = new List<GeoPosition>(points);
        for (var k = 1; k < result.Count - 1;)
        {
            var clear = true;
            var held = false;
            foreach (var (field, path) in fields)
            {
                if (field.CellOf(result[k - 1]) is { } p && field.CellOf(result[k + 1]) is { } q)
                {
                    held = true;
                    clear &= field.Visible(p, q, field.LeastRoom(path));
                }
            }

            if (held && clear)
            {
                result.RemoveAt(k);
            }
            else
            {
                k++;
            }
        }

        return result;
    }

    private static RoutePlanLeg Leg(RoutePlanStop from, RoutePlanStop to, List<GeoPosition> points, (Field Field, List<int> Path)[] fields)
    {
        double? leastClearance = null;
        double? leastDepth = null;
        foreach (var (field, path) in fields)
        {
            foreach (var i in path)
            {
                if (field.IsBerth(i))
                {
                    continue;
                }

                leastClearance = Math.Min(leastClearance ?? double.MaxValue, field.Clearance(i));
                if (field.Depth(i) is { } d)
                {
                    leastDepth = Math.Min(leastDepth ?? double.MaxValue, d);
                }
            }
        }

        var distance = 0.0;
        for (var k = 1; k < points.Count; k++)
        {
            distance += PassageGeodesy.RhumbDistanceMeters(points[k - 1], points[k]);
        }

        return new RoutePlanLeg(
            from.Index,
            to.Index,
            points,
            Math.Round(distance, 1),
            leastClearance is { } lc ? Math.Round(lc, 1) : null,
            leastDepth is { } ld ? Math.Round(ld, 2) : null,
            fields.Max(f => f.Field.CellMeters));
    }

    private static BoundingBox Pad(GeoPosition a, GeoPosition b, double padMeters)
    {
        var dLat = padMeters / PassageGeodesy.MetersPerDegreeLatitude;
        var midLat = (a.Latitude + b.Latitude) / 2;
        var dLon = dLat / Math.Max(0.01, Math.Cos(midLat * Math.PI / 180));
        var dLonAb = (((b.Longitude - a.Longitude) + 540) % 360) - 180;
        var west = dLonAb >= 0 ? a.Longitude : a.Longitude + dLonAb;
        var east = west + Math.Abs(dLonAb);
        return new BoundingBox(
            Math.Max(-90, Math.Min(a.Latitude, b.Latitude) - dLat),
            Wrap(west - dLon),
            Math.Min(90, Math.Max(a.Latitude, b.Latitude) + dLat),
            Wrap(east + dLon));
    }

    private static double Wrap(double longitude) => ((longitude + 540) % 360) - 180;

    // A fine grid round a stop, with every cell's cost from the stop.
    private sealed class Approach(Field field, double[] cost, int[] parent, GeoPosition stop, double half)
    {
        public Field Field { get; } = field;

        // The coarse cells inside this grid (less a margin) that the stop reaches, with the cost in coarse cells.
        public List<(int Cell, double Cost)> Reached(Field coarse)
        {
            var result = new List<(int, double)>();
            var inner = half - (3 * coarse.CellMeters);
            if (inner <= 0 || coarse.CellOf(stop) is not { } centre)
            {
                return result;
            }

            var span = (int)Math.Ceiling(inner / coarse.CellMeters);
            var scale = Field.CellMeters / coarse.CellMeters;
            foreach (var i in coarse.Around(centre, span))
            {
                if (!coarse.Passable(i)
                    || PassageGeodesy.GreatCircleDistanceMeters(stop, coarse.Centre(i)) > inner
                    || Field.CellOf(coarse.Centre(i)) is not { } f
                    || !double.IsFinite(cost[f]))
                {
                    continue;
                }

                result.Add((i, cost[f] * scale));
            }

            return result;
        }

        // The way from the stop to a position, cell by cell.
        public List<int> PathTo(GeoPosition position)
        {
            var path = new List<int>();
            for (var k = Field.CellOf(position) ?? -1; k != -1; k = parent[k])
            {
                path.Add(k);
            }

            path.Reverse();
            return path;
        }
    }

    // One grid, with what the planner reads from each cell.
    private sealed class Field
    {
        private readonly NavigabilityGrid _grid;
        private readonly RoutePlanRequest _request;
        private readonly int _rows;
        private readonly int _cols;
        private readonly bool[] _passable;
        private readonly bool[] _berth;
        private readonly bool[] _preferred;
        private readonly float[] _clearance;
        private readonly double _comfort;
        private readonly bool _anyPreferred;

        public Field(NavigabilityGrid grid, RoutePlanRequest request, IReadOnlyList<GeoPosition> stops, double berthDepth)
        {
            _grid = grid;
            _request = request;
            _rows = grid.Rows;
            _cols = grid.Columns;
            var count = _rows * _cols;
            _passable = new bool[count];
            _berth = new bool[count];
            _clearance = new float[count];
            _preferred = new bool[count];
            _comfort = Math.Max(request.ComfortMeters, grid.CellMeters);

            var berthCells = request.BerthRadiusMeters / grid.CellMeters;
            var berth2 = berthCells * berthCells;
            var stopCells = stops.Select(s => grid.CellAt(s)).OfType<NavigabilityCell>().ToList();
            var classes = grid.Classes;
            var depths = grid.LeastDepths;
            for (var r = 0; r < _rows; r++)
            {
                for (var c = 0; c < _cols; c++)
                {
                    var i = (r * _cols) + c;
                    var clearance = grid.IsNavigable(r, c) ? Math.Max(0, grid.ClearanceMeters(r, c) - (grid.CellMeters / 2)) : 0;
                    _clearance[i] = (float)clearance;
                    var nearStop = false;
                    foreach (var s in stopCells)
                    {
                        if (Sq(r - s.Row) + Sq(c - s.Column) <= berth2)
                        {
                            nearStop = true;
                            break;
                        }
                    }

                    _berth[i] = nearStop;
                    _passable[i] = nearStop
                        ? classes[i] is NavigabilityCellClass.Navigable or NavigabilityCellClass.Shoal && depths[i] >= berthDepth
                        : classes[i] == NavigabilityCellClass.Navigable && clearance >= request.ClearanceMeters;
                }
            }

            // A stop's own cell is always passable: the stop was moved to water at the berth depth on a finer grid.
            foreach (var s in stopCells)
            {
                _passable[(s.Row * _cols) + s.Column] = true;
            }

            _anyPreferred = MarkPreferred();
        }

        public double CellMeters => _grid.CellMeters;

        public int? CellOf(GeoPosition position) => _grid.CellAt(position) is { } cell ? (cell.Row * _cols) + cell.Column : null;

        public GeoPosition Centre(int i) => _grid.CenterOf(new NavigabilityCell(i / _cols, i % _cols));

        public bool Passable(int i) => _passable[i];

        public bool IsBerth(int i) => _berth[i];

        public double Clearance(int i) => _clearance[i];

        public double? Depth(int i) => _grid.LeastDepthAt(i / _cols, i % _cols);

        public IEnumerable<int> Around(int centre, int span)
        {
            int r0 = centre / _cols, c0 = centre % _cols;
            for (var r = Math.Max(0, r0 - span); r <= Math.Min(_rows - 1, r0 + span); r++)
            {
                for (var c = Math.Max(0, c0 - span); c <= Math.Min(_cols - 1, c0 + span); c++)
                {
                    yield return (r * _cols) + c;
                }
            }
        }

        // Every cell's least cost from a cell (Dijkstra), and the way back.
        public (double[] Cost, int[] From) Flood(int start, CancellationToken cancellationToken)
        {
            var cost = new double[_rows * _cols];
            Array.Fill(cost, double.PositiveInfinity);
            var from = new int[cost.Length];
            Array.Fill(from, -1);
            Run([(start, 0)], cost, from, _ => double.PositiveInfinity, -1, cancellationToken);
            return (cost, from);
        }

        // A* from the seeds to the cheapest goal (a goal's cost is added on arrival).
        public List<int>? Search(IReadOnlyList<(int Cell, double Cost)> seeds, Func<int, double> goal, int target, CancellationToken cancellationToken)
        {
            var cost = new double[_rows * _cols];
            Array.Fill(cost, double.PositiveInfinity);
            var from = new int[cost.Length];
            Array.Fill(from, -1);
            var end = Run(seeds, cost, from, goal, target, cancellationToken);
            if (end < 0)
            {
                return null;
            }

            var path = new List<int>();
            for (var k = end; k != -1; k = from[k])
            {
                path.Add(k);
            }

            path.Reverse();
            return path;
        }

        private int Run(IReadOnlyList<(int Cell, double Cost)> seeds, double[] cost, int[] from, Func<int, double> goal, int target, CancellationToken cancellationToken)
        {
            var frontier = new PriorityQueue<int, double>();
            var hScale = target < 0 ? 0 : _anyPreferred ? _request.PreferredCostFactor : 1;
            int tr = target < 0 ? 0 : target / _cols, tc = target < 0 ? 0 : target % _cols;
            double H(int i) => hScale == 0 ? 0 : hScale * Math.Sqrt(Sq((i / _cols) - tr) + Sq((i % _cols) - tc));

            foreach (var (cell, c) in seeds)
            {
                if (c < cost[cell])
                {
                    cost[cell] = c;
                    from[cell] = -1;
                    frontier.Enqueue(cell, c + H(cell));
                }
            }

            var best = -1;
            var bestCost = double.PositiveInfinity;
            var steps = 0;
            ReadOnlySpan<(int Dr, int Dc, double W)> moves =
            [
                (1, 0, 1), (-1, 0, 1), (0, 1, 1), (0, -1, 1),
                (1, 1, Math.Sqrt(2)), (1, -1, Math.Sqrt(2)), (-1, 1, Math.Sqrt(2)), (-1, -1, Math.Sqrt(2)),
            ];
            while (frontier.TryDequeue(out var i, out var f))
            {
                if (f >= bestCost)
                {
                    break;
                }

                if (f - H(i) > cost[i] + 1e-9)
                {
                    continue;
                }

                if (goal(i) is var exit && double.IsFinite(exit) && cost[i] + exit < bestCost)
                {
                    bestCost = cost[i] + exit;
                    best = i;
                }

                if ((++steps & 0xFFFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                int r = i / _cols, c = i % _cols;
                foreach (var (dr, dc, w) in moves)
                {
                    int nr = r + dr, nc = c + dc;
                    if (nr < 0 || nc < 0 || nr >= _rows || nc >= _cols)
                    {
                        continue;
                    }

                    var j = (nr * _cols) + nc;
                    if (!_passable[j] || (dr != 0 && dc != 0 && (!_passable[(nr * _cols) + c] || !_passable[(r * _cols) + nc])))
                    {
                        continue;
                    }

                    var close = _berth[j] ? 0 : Math.Max(0, 1 - (_clearance[j] / _comfort));
                    var next = cost[i] + (w * (1 + (ClosePenalty * close)) * (_preferred[j] ? _request.PreferredCostFactor : 1));
                    if (next < cost[j])
                    {
                        cost[j] = next;
                        from[j] = i;
                        frontier.Enqueue(j, next + H(j));
                    }
                }
            }

            return best;
        }

        // Keeps the turns a straight line can't skip.
        public List<int> Simplify(List<int> path)
        {
            if (path.Count <= 2)
            {
                return path;
            }

            var kept = new List<int> { path[0] };
            var i = 0;
            while (i < path.Count - 1)
            {
                var best = i + 1;
                var least = Room(path[i]);
                var fails = 0;
                for (var j = i + 1; j < path.Count && fails < LookAhead; j++)
                {
                    least = Math.Min(least, Room(path[j]));
                    if (Visible(path[i], path[j], least))
                    {
                        best = j;
                        fails = 0;
                    }
                    else
                    {
                        fails++;
                    }
                }

                kept.Add(path[best]);
                i = best;
            }

            return kept;
        }

        // The positions of kept cells, with the given ends in place of the first and last cells' centres.
        public List<GeoPosition> Points(List<int> kept, GeoPosition first, GeoPosition last)
        {
            var points = new List<GeoPosition>(Math.Max(2, kept.Count)) { first };
            for (var k = 1; k < kept.Count - 1; k++)
            {
                var p = Centre(kept[k]);
                points.Add(new GeoPosition(Math.Round(p.Latitude, 6), Math.Round(p.Longitude, 6)));
            }

            points.Add(last);
            return points;
        }

        // The least room along a path, as the shortcut test counts it.
        public float LeastRoom(List<int> path)
        {
            var least = float.MaxValue;
            foreach (var i in path)
            {
                least = Math.Min(least, Room(i));
            }

            return least;
        }

        // A cell's room as the shortcut test counts it: berth cells don't
        // count, and a shortcut may come within half the comfort distance.
        private float Room(int i) => _berth[i] ? float.MaxValue : Math.Min(_clearance[i], (float)(_comfort / 2));

        // Whether a straight line between two cells stays in passable cells
        // with at least the given room (berth cells excepted), and doesn't
        // leave preferred water it starts or ends in.
        public bool Visible(int a, int b, float room)
        {
            int ar = a / _cols, ac = a % _cols, br = b / _cols, bc = b % _cols;
            var n = (int)Math.Ceiling(Math.Max(Math.Abs(br - ar), Math.Abs(bc - ac)) * 3.0);
            var keepPreferred = _anyPreferred && (_preferred[a] || _preferred[b]);
            for (var k = 0; k <= n; k++)
            {
                var t = n == 0 ? 0 : (double)k / n;
                var r = (int)Math.Round(ar + ((br - ar) * t));
                var c = (int)Math.Round(ac + ((bc - ac) * t));
                var i = (r * _cols) + c;
                if (!_passable[i] || (!_berth[i] && _clearance[i] < room) || (keepPreferred && !_preferred[i] && !_berth[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool MarkPreferred()
        {
            var any = false;
            foreach (var surface in _request.PreferredAreas)
            {
                any |= MarkSurface(surface);
            }

            var step = Math.Min(_grid.Centres.LatitudeStep, _grid.Centres.LongitudeStep) / 3;
            foreach (var line in _request.PreferredLines)
            {
                for (var k = 0; k + 1 < line.Count; k++)
                {
                    var p = line[k];
                    var q = line[k + 1];
                    var n = (int)Math.Ceiling(Math.Max(Math.Abs(q.Latitude - p.Latitude), Math.Abs(q.Longitude - p.Longitude)) / step);
                    for (var s = 0; s <= n; s++)
                    {
                        var t = n == 0 ? 0 : (double)s / n;
                        var at = new GeoPosition(p.Latitude + ((q.Latitude - p.Latitude) * t), p.Longitude + ((q.Longitude - p.Longitude) * t));
                        if (_grid.CellAt(at) is not { } cell)
                        {
                            continue;
                        }

                        // A track is a line: a band a cell either side of it.
                        for (var dr = -1; dr <= 1; dr++)
                        {
                            for (var dc = -1; dc <= 1; dc++)
                            {
                                int r = cell.Row + dr, c = cell.Column + dc;
                                if (r >= 0 && c >= 0 && r < _rows && c < _cols)
                                {
                                    _preferred[(r * _cols) + c] = true;
                                    any = true;
                                }
                            }
                        }
                    }
                }
            }

            return any;
        }

        private bool MarkSurface(SurfacePart surface)
        {
            var ring = surface.ExteriorRing;
            if (ring.Count < 3)
            {
                return false;
            }

            double south = double.MaxValue, north = double.MinValue, west = double.MaxValue, east = double.MinValue;
            foreach (var p in ring)
            {
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
            }

            var centres = _grid.Centres;
            var r0 = Math.Max(0, (int)Math.Ceiling((south - centres.South) / centres.LatitudeStep));
            var r1 = Math.Min(_rows - 1, (int)Math.Floor((north - centres.South) / centres.LatitudeStep));
            var c0 = Math.Max(0, (int)Math.Ceiling((west - centres.West) / centres.LongitudeStep));
            var c1 = Math.Min(_cols - 1, (int)Math.Floor((east - centres.West) / centres.LongitudeStep));
            var any = false;
            for (var r = r0; r <= r1; r++)
            {
                for (var c = c0; c <= c1; c++)
                {
                    if (PlanarGeometry.Contains([surface], centres.PositionAt(r, c)))
                    {
                        _preferred[(r * _cols) + c] = true;
                        any = true;
                    }
                }
            }

            return any;
        }

        private static double Sq(double x) => x * x;
    }
}
