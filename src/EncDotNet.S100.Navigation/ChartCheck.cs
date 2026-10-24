using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The chart check, built into <see cref="RouteChecker"/>: where the route's
/// track crosses land or drying ground, where either reaches into the
/// corridor between its cross-track limits, and where it leaves the loaded
/// charts (#888, #954), over <see cref="RouteCheckSources.Charts"/>. When
/// <see cref="RouteCheckSettings.ChecksDepths"/> it also checks charted
/// dangers and the safety contour and depth (#955), judging each leg's
/// dangers against that leg's keel when it has a draft.
/// </summary>
/// <remarks>
/// <para>
/// Skipped without charts (<see cref="RouteCheckReasons.ChartsMissing"/>).
/// Without a safety contour and safety depth it runs only in part
/// (<see cref="RouteCheckReasons.SafetySettingsMissing"/>): land, drying
/// ground and coverage are checked; depths and dangers aren't.
/// </para>
/// <para>
/// Each leg is swept by cross-sections every
/// <see cref="ChartCheckOptions.StationSpacingMeters"/>, each reaching from
/// the port limit to the starboard limit. Where the track itself crosses an
/// area or a coastline, the crossing is found exactly, between cross-sections;
/// an area that only reaches into the corridor is found at the cross-sections
/// it touches.
/// </para>
/// <para>
/// Where cells overlap, only the finest cell counts (#947): a feature is used
/// at a place only when its cell is in the finest band (by
/// <see cref="CellScaleRanking"/>) of the cells whose coverage contains that
/// place. So a coarse cell's generalised land doesn't fire where a finer cell
/// charts water, matching what the display shows. The S-102 safety-contour
/// check reads only the S-102 surfaces of <see cref="RouteCheckSources.Depth"/>
/// (<see cref="IChartedDepthSource.SampleBathymetry"/>).
/// </para>
/// <para>
/// Findings are <see cref="RouteFinding"/>s in the
/// <see cref="RouteFindingCategory.Chart"/> and <see cref="RouteFindingCategory.Depth"/>
/// categories (and <see cref="RouteFindingCategory.Data"/> for coverage),
/// with their text rendered by <see cref="RouteCheckOptions.Formatter"/>. Consecutive crossings
/// of the same kind are merged into one finding, so adjacent depth areas that
/// both dry make a single "Crosses drying ground"; its feature is the one at
/// the start of the span.
/// </para>
/// <para>
/// Its findings don't depend on time, so it keeps the last result and reuses
/// it while the legs, the depth settings, the corridor override, the options
/// and the sources are unchanged: changing only the departure doesn't sweep
/// the charts again. Its <see cref="RouteCheckOutcome{TDetail}.Detail"/> is
/// the <see cref="ChartCheckResult"/>.
/// </para>
/// <para>
/// This is an aid for exploring the data. It isn't for navigation.
/// </para>
/// </remarks>
public sealed partial class ChartCheck : IRouteCheck<ChartCheckResult>
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "chart";

    // How many cross-sections share one edge bucket.
    private const int ChunkSize = 16;

    // How many points each side of the corridor ring has at most.
    private const int MaxRingPointsPerSide = 32;

    // A side narrower than this is swept this wide, so the corridor keeps an area.
    private const double MinimumRingHalfWidthMeters = 0.5;

    // Pieces closer than this are one span.
    private const double MergeGapMeters = 1.0;

    // Crossings shorter than this aren't reported.
    private const double MinimumCrossingMeters = 1.0;

    private readonly object _gate = new();
    private (RouteCheckSources Sources, ChartRun Run, IReadOnlyList<PassageLeg> Legs, RouteCheckSettings Settings, IReadOnlyList<RouteFinding> Findings, ChartCheckResult Result)? _last;

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ChartCheckOptions.StationSpacingMeters"/> isn't positive, or <see cref="ChartCheckOptions.MaxStationsPerLeg"/> is less than 2.</exception>
    public RouteCheckOutcome<ChartCheckResult> Run(RouteCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Charts is not { } charts)
        {
            return RouteCheckOutcome<ChartCheckResult>.Skipped(RouteCheckReasons.ChartsMissing);
        }

        var run = new ChartRun(charts, context.Options.Chart, context.Sources.Depth, context.Options.DefaultLimit.TotalMetres, context.Options.Formatter);
        var settings = context.Settings;
        (IReadOnlyList<RouteFinding> Findings, ChartCheckResult Result)? reuse = null;
        lock (_gate)
        {
            if (_last is { } last
                && ReferenceEquals(last.Sources, context.Sources)
                && last.Run == run
                && last.Settings.SafetyContour == settings.SafetyContour
                && last.Settings.SafetyDepth == settings.SafetyDepth
                && last.Settings.CorridorOverride == settings.CorridorOverride
                && PassageLegs.Same(last.Legs, context.Plan.Legs))
            {
                reuse = (last.Findings, last.Result);
            }
        }

        if (reuse is not { } swept)
        {
            swept = Sweep(run, context.Plan, settings, cancellationToken);
            lock (_gate)
            {
                _last = (context.Sources, run, context.Plan.Legs.ToArray(), settings, swept.Findings, swept.Result);
            }
        }

        return settings.ChecksDepths
            ? RouteCheckOutcome<ChartCheckResult>.Ran(swept.Findings, swept.Result)
            : RouteCheckOutcome<ChartCheckResult>.Partial(RouteCheckReasons.SafetySettingsMissing, swept.Findings, swept.Result);
    }

    /// <inheritdoc/>
    RouteCheckOutcome IRouteCheck.Run(RouteCheckContext context, CancellationToken cancellationToken) => Run(context, cancellationToken);

    private static (IReadOnlyList<RouteFinding> Findings, ChartCheckResult Result) Sweep(ChartRun run, PassagePlan plan, RouteCheckSettings settings, CancellationToken cancellationToken)
    {
        var options = run.Options;
        if (!(options.StationSpacingMeters > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(run), "StationSpacingMeters must be positive.");
        }

        if (options.MaxStationsPerLeg < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(run), "MaxStationsPerLeg must be at least 2.");
        }

        var legs = plan.Legs;
        var findings = new FindingList(run.Formatter);
        var corridors = new List<RouteCheckCorridor>(legs.Count);
        var dangers = new DangerList();
        var offset = 0.0;
        for (var i = 0; i < legs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leg = legs[i] ?? throw new ArgumentException($"Leg {i} is null.", nameof(plan));
            if (leg.Points is null || leg.Points.Count < 2)
            {
                throw new ArgumentException($"Leg {i} needs at least two points.", nameof(plan));
            }

            var depths = settings is { SafetyContour: { } contour, SafetyDepth: { } safety }
                ? new LegDepths(contour.TotalMetres, safety.TotalMetres, leg.KeelMetres)
                : null;
            var sweep = new LegSweep(run, i, leg, settings, depths, offset, findings, dangers, cancellationToken);
            sweep.Run();
            corridors.Add(sweep.Corridor);
            offset = sweep.EndDistance;
        }

        dangers.AddTo(findings);

        return (findings.Sorted(), new ChartCheckResult(corridors));
    }

    /// <summary>What one run sweeps against: the charts, the tuning and the S-102 surfaces.</summary>
    private sealed record ChartRun(
        IRouteChartSource Charts,
        ChartCheckOptions Options,
        IChartedDepthSource? Bathymetry,
        double DefaultHalfWidthMeters,
        RouteFindingFormatter Formatter);

    /// <summary>One cross-section: a point on the track and the corridor's limits either side of it.</summary>
    private readonly record struct Station(GeoPosition Centre, double Distance, GeoPosition Port, GeoPosition Starboard);

    /// <summary>
    /// A stretch of the track inside a feature of one kind, and the shoalest
    /// feature in it once pieces are merged.
    /// </summary>
    private readonly record struct Piece(double Start, double End, RouteChartFeature Feature, RouteChartFeature? Shoalest = null);

    /// <summary>The sweep of one leg.</summary>
    private sealed partial class LegSweep
    {
        private readonly ChartRun _run;
        private readonly int _legIndex;
        private readonly PassageLeg _leg;
        private readonly LegDepths? _depths;
        private readonly FindingList _findings;
        private readonly DangerList _dangers;
        private readonly CancellationToken _cancellationToken;
        private readonly double _port;
        private readonly double _starboard;
        private readonly double _spacing;
        private readonly List<Station> _stations;
        private readonly BoundingBox _extent;
        private readonly IReadOnlyList<GeoPosition> _ring;
        private BoundingBox[] _chunks = [];
        private List<RouteChartCell> _cells = [];
        private Dictionary<RouteChartCell, int> _cellIndex = new(ReferenceEqualityComparer.Instance);
        private IReadOnlyList<int>[] _centreBands = [];
        private IReadOnlyList<int>?[] _portBands = [];
        private IReadOnlyList<int>?[] _starboardBands = [];

        public LegSweep(ChartRun run, int legIndex, PassageLeg leg, RouteCheckSettings settings, LegDepths? depths, double offset, FindingList findings, DangerList dangers, CancellationToken cancellationToken)
        {
            _run = run;
            _legIndex = legIndex;
            _leg = leg;
            _depths = depths;
            _findings = findings;
            _dangers = dangers;
            _cancellationToken = cancellationToken;
            _port = settings.PortMeters(leg, run.DefaultHalfWidthMeters);
            _starboard = settings.StarboardMeters(leg, run.DefaultHalfWidthMeters);
            (_stations, _spacing) = BuildStations(leg, offset, run.Options);
            _extent = Extent(_stations);
            _ring = Ring(_stations);
            Corridor = new RouteCheckCorridor(legIndex, offset, EndDistance, _port, _starboard, _ring);
        }

        public double EndDistance => _stations[^1].Distance;

        public RouteCheckCorridor Corridor { get; }

        public void Run()
        {
            // The depth checks look for dangers beyond the limits too, so one
            // wider query serves every check; the corridor checks test
            // geometry against the corridor itself, so the extra features
            // change nothing for them.
            var depths = _depths;
            var (ring, extent) = depths is null ? (_ring, _extent) : WideRing(Math.Max(0, _run.Options.DangerNearbyMeters));
            var features = _run.Charts.FeaturesInCorridor(ring, extent);
            _cells = new List<RouteChartCell>(_run.Charts.CellsNear(extent));
            foreach (var cell in _cells)
            {
                _cellIndex.TryAdd(cell, _cellIndex.Count);
            }

            foreach (var feature in features)
            {
                if (_cellIndex.TryAdd(feature.Cell, _cellIndex.Count))
                {
                    _cells.Add(feature.Cell);
                }
            }

            _chunks = Chunks(_stations);
            _centreBands = new IReadOnlyList<int>[_stations.Count];
            _portBands = new IReadOnlyList<int>?[_stations.Count];
            _starboardBands = new IReadOnlyList<int>?[_stations.Count];
            for (var k = 0; k < _stations.Count; k++)
            {
                _centreBands[k] = Band(_stations[k].Centre);
            }

            CheckCoverage();

            var land = CheckAreas(features, RouteChartFeatureKind.Land, RouteFindingCodes.CrossesLand, RouteFindingCodes.LandInCorridor);
            CheckCoastlines(features, land);
            CheckAreas(features, RouteChartFeatureKind.DryingArea, RouteFindingCodes.CrossesDrying, RouteFindingCodes.DryingInCorridor);
            if (depths is not null)
            {
                CheckDepths(features, depths);
            }
        }

        private void CheckCoverage()
        {
            var k = 0;
            while (k < _stations.Count)
            {
                if (_centreBands[k].Count > 0)
                {
                    k++;
                    continue;
                }

                var first = k;
                while (k < _stations.Count && _centreBands[k].Count == 0)
                {
                    k++;
                }

                // From halfway to the covered station before to halfway to the one after.
                var start = first == 0 ? _stations[0].Distance : (_stations[first - 1].Distance + _stations[first].Distance) / 2;
                var end = k == _stations.Count ? _stations[^1].Distance : (_stations[k - 1].Distance + _stations[k].Distance) / 2;
                _findings.Add(RouteFindingCodes.OutsideChartCoverage, _legIndex, start, Location(start, end), feature: null);
            }
        }

        // Finds the track's crossings of one kind of area, then the
        // cross-sections that touch it where the track doesn't; returns the
        // merged crossings. Details, when given, are a finding's details
        // from the shoalest feature in it.
        private List<Piece> CheckAreas(
            IReadOnlyList<RouteChartFeature> features,
            RouteChartFeatureKind kind,
            RouteFindingCode crossesCode,
            RouteFindingCode inCorridorCode,
            Func<RouteChartFeature, RouteFindingDetails>? details = null)
        {
            var pieces = new List<Piece>();
            var touched = new RouteChartFeature?[_stations.Count];
            var shoalest = new RouteChartFeature?[_stations.Count];
            foreach (var feature in features)
            {
                if (feature.Kind != kind || feature.Surfaces.Count == 0)
                {
                    continue;
                }

                _cancellationToken.ThrowIfCancellationRequested();
                var cell = _cellIndex[feature.Cell];
                var edges = new EdgeBuckets(this, AreaEdges(feature));
                var inside = new bool[_stations.Count];
                var state = PlanarGeometry.Contains(feature.Surfaces, _stations[0].Centre);
                var crossings = new List<double>();
                for (var k = 0; k < _stations.Count; k++)
                {
                    inside[k] = state;
                    if (k == _stations.Count - 1)
                    {
                        break;
                    }

                    var a = _stations[k];
                    var b = _stations[k + 1];
                    crossings.Clear();
                    edges.Crossings(k, a.Centre, b.Centre, crossings);
                    var counts = InBand(cell, _centreBands[k]) || InBand(cell, _centreBands[k + 1]);
                    if (crossings.Count == 0)
                    {
                        if (state && counts)
                        {
                            pieces.Add(new Piece(a.Distance, b.Distance, feature));
                        }

                        continue;
                    }

                    // Between crossings the track is wholly in or out, so
                    // each stretch is tested at its middle. That stays right
                    // where the track runs through a vertex or a station
                    // sits on the boundary, which a running parity doesn't.
                    crossings.Add(0);
                    crossings.Add(1);
                    crossings.Sort();
                    for (var c = 1; c < crossings.Count; c++)
                    {
                        var from = crossings[c - 1];
                        var to = crossings[c];
                        if (to - from < 1e-9)
                        {
                            continue;
                        }

                        var middle = Lerp(a.Centre, b.Centre, (from + to) / 2);
                        state = PlanarGeometry.Contains(feature.Surfaces, middle);
                        if (state && counts)
                        {
                            pieces.Add(new Piece(Lerp(a.Distance, b.Distance, from), Lerp(a.Distance, b.Distance, to), feature));
                        }
                    }
                }

                for (var k = 0; k < _stations.Count; k++)
                {
                    // A touched cross-section is looked at again only for a shoaler feature.
                    if (touched[k] is not null && !(feature.DepthMeters < shoalest[k]!.DepthMeters))
                    {
                        continue;
                    }

                    var s = _stations[k];
                    if ((inside[k] || edges.CrossesSection(k, s.Port, s.Starboard)) && SectionInBand(cell, k))
                    {
                        touched[k] ??= feature;
                        shoalest[k] = Shoaler(feature, shoalest[k]);
                    }
                }
            }

            var merged = Merge(pieces);
            foreach (var piece in merged)
            {
                // A clip of a corner shorter than this is a rounding artefact,
                // not a stretch to review; it still absorbs its corridor fringe.
                if (piece.End - piece.Start < MinimumCrossingMeters)
                {
                    continue;
                }

                _findings.Add(crossesCode, _legIndex, piece.Start, Location(piece.Start, piece.End), piece.Feature,
                    details?.Invoke(piece.Shoalest ?? piece.Feature));
            }

            // A run of touched cross-sections that reaches a crossing is the
            // same ground seen from the limits, so the crossing stands for it.
            var k2 = 0;
            while (k2 < _stations.Count)
            {
                if (touched[k2] is null)
                {
                    k2++;
                    continue;
                }

                var first = k2;
                var nearCrossing = false;
                RouteChartFeature? runShoalest = null;
                while (k2 < _stations.Count && touched[k2] is not null)
                {
                    nearCrossing |= NearCrossing(merged, _stations[k2].Distance);
                    runShoalest = Shoaler(shoalest[k2], runShoalest);
                    k2++;
                }

                if (nearCrossing)
                {
                    continue;
                }

                var start = _stations[first].Distance;
                var end = _stations[k2 - 1].Distance;
                var feature = touched[first]!;
                _findings.Add(inCorridorCode, _legIndex, start, Location(start, end), feature,
                    details?.Invoke(runShoalest ?? feature));
            }

            return merged;
        }

        // A coastline the track crosses away from any charted land crossing
        // (a cell whose land isn't encoded as LandArea) is a land crossing too.
        private void CheckCoastlines(IReadOnlyList<RouteChartFeature> features, List<Piece> land)
        {
            var found = new List<double>();
            var crossings = new List<double>();
            foreach (var feature in features)
            {
                if (feature.Kind != RouteChartFeatureKind.Coastline || feature.Curves.Count == 0)
                {
                    continue;
                }

                _cancellationToken.ThrowIfCancellationRequested();
                var cell = _cellIndex[feature.Cell];
                var edges = new EdgeBuckets(this, CurveEdges(feature));
                for (var k = 0; k < _stations.Count - 1; k++)
                {
                    crossings.Clear();
                    edges.Crossings(k, _stations[k].Centre, _stations[k + 1].Centre, crossings);
                    if (crossings.Count == 0 || !(InBand(cell, _centreBands[k]) || InBand(cell, _centreBands[k + 1])))
                    {
                        continue;
                    }

                    foreach (var u in crossings)
                    {
                        var d = Lerp(_stations[k].Distance, _stations[k + 1].Distance, u);
                        if (NearCrossing(land, d) || found.Any(f => Math.Abs(f - d) <= _spacing))
                        {
                            continue;
                        }

                        found.Add(d);
                        _findings.Add(RouteFindingCodes.CrossesLand, _legIndex, d, Location(d, d), feature);
                    }
                }
            }
        }

        private bool NearCrossing(List<Piece> merged, double distance)
        {
            foreach (var piece in merged)
            {
                if (distance >= piece.Start - _spacing && distance <= piece.End + _spacing)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Piece> Merge(List<Piece> pieces)
        {
            pieces.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<Piece>();
            foreach (var piece in pieces)
            {
                if (merged.Count > 0 && piece.Start <= merged[^1].End + MergeGapMeters)
                {
                    var last = merged[^1];
                    merged[^1] = last with
                    {
                        End = Math.Max(last.End, piece.End),
                        Shoalest = Shoaler(last.Shoalest ?? last.Feature, piece.Feature),
                    };
                }
                else
                {
                    merged.Add(piece);
                }
            }

            return merged;
        }

        // The feature that charts the lesser depth; the first when neither or only one has a depth.
        private static RouteChartFeature? Shoaler(RouteChartFeature? a, RouteChartFeature? b) =>
            a is null ? b
            : b is null ? a
            : b.DepthMeters is { } db && (a.DepthMeters is not { } da || db < da) ? b : a;

        private static bool InBand(int cell, IReadOnlyList<int> band)
        {
            foreach (var c in band)
            {
                if (c == cell)
                {
                    return true;
                }
            }

            return false;
        }

        // A cross-section counts a feature when its cell is the finest at the
        // track or at either limit.
        private bool SectionInBand(int cell, int k)
        {
            if (InBand(cell, _centreBands[k]))
            {
                return true;
            }

            var port = _portBands[k] ??= Band(_stations[k].Port);
            if (InBand(cell, port))
            {
                return true;
            }

            var starboard = _starboardBands[k] ??= Band(_stations[k].Starboard);
            return InBand(cell, starboard);
        }

        // The finest band of the cells that cover a point, as cell indices.
        private IReadOnlyList<int> Band(GeoPosition position)
        {
            List<int>? covering = null;
            for (var i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].Covers(position))
                {
                    (covering ??= new List<int>(2)).Add(i);
                }
            }

            return covering is null ? [] : CellScaleRanking.FinestBand(covering, i => _cells[i].RankingScale);
        }

        private List<(GeoPosition A, GeoPosition B)> AreaEdges(RouteChartFeature feature)
        {
            var edges = new List<(GeoPosition, GeoPosition)>();
            foreach (var surface in feature.Surfaces)
            {
                AddEdges(surface.ExteriorRing, closed: true, edges);
                foreach (var hole in surface.InteriorRings)
                {
                    AddEdges(hole, closed: true, edges);
                }
            }

            return edges;
        }

        private List<(GeoPosition A, GeoPosition B)> CurveEdges(RouteChartFeature feature)
        {
            var edges = new List<(GeoPosition, GeoPosition)>();
            foreach (var curve in feature.Curves)
            {
                AddEdges(curve, closed: false, edges);
            }

            return edges;
        }

        // Keeps only the edges near the corridor: the rest can't cross a
        // cross-section or the track.
        private void AddEdges(IReadOnlyList<GeoPosition> line, bool closed, List<(GeoPosition, GeoPosition)> edges)
        {
            var count = line.Count;
            if (count < 2)
            {
                return;
            }

            var last = closed && line[0] != line[count - 1] ? count : count - 1;
            for (var i = 0; i < last; i++)
            {
                var a = line[i];
                var b = line[(i + 1) % count];
                if (Overlaps(_extent, a, b))
                {
                    edges.Add((a, b));
                }
            }
        }

        private RouteFindingLocation Location(double start, double end)
        {
            if (end - start <= 0)
            {
                return RouteFindingLocation.Point(PositionAt(start), start);
            }

            var path = new List<GeoPosition> { PositionAt(start) };
            foreach (var s in _stations)
            {
                if (s.Distance > start && s.Distance < end)
                {
                    path.Add(s.Centre);
                }
            }

            path.Add(PositionAt(end));
            return new RouteFindingLocation(path, start, end);
        }

        private GeoPosition PositionAt(double distance)
        {
            var lo = 0;
            var hi = _stations.Count - 1;
            if (distance <= _stations[lo].Distance)
            {
                return _stations[lo].Centre;
            }

            if (distance >= _stations[hi].Distance)
            {
                return _stations[hi].Centre;
            }

            while (hi - lo > 1)
            {
                var mid = (lo + hi) / 2;
                if (_stations[mid].Distance <= distance)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            var a = _stations[lo];
            var b = _stations[hi];
            var u = (distance - a.Distance) / (b.Distance - a.Distance);
            return new GeoPosition(
                a.Centre.Latitude + (b.Centre.Latitude - a.Centre.Latitude) * u,
                a.Centre.Longitude + (b.Centre.Longitude - a.Centre.Longitude) * u);
        }

        private (List<Station> Stations, double Spacing) BuildStations(PassageLeg leg, double offset, ChartCheckOptions options)
        {
            var points = leg.Points;
            var lengths = new double[points.Count - 1];
            var total = 0.0;
            for (var i = 0; i < lengths.Length; i++)
            {
                lengths[i] = PassageGeodesy.DistanceMeters(points[i], points[i + 1], leg.Geometry);
                total += lengths[i];
            }

            var spacing = Math.Max(options.StationSpacingMeters, total / (options.MaxStationsPerLeg - 1));
            var centres = new List<(GeoPosition Position, double Distance)> { (points[0], offset) };
            var along = offset;
            for (var i = 0; i < lengths.Length; i++)
            {
                var n = Math.Max(1, (int)Math.Ceiling(lengths[i] / spacing));
                for (var t = 1; t <= n; t++)
                {
                    var f = (double)t / n;
                    centres.Add((PassageGeodesy.Interpolate(points[i], points[i + 1], f, leg.Geometry), along + lengths[i] * f));
                }

                along += lengths[i];
            }

            var stations = new List<Station>(centres.Count);
            for (var k = 0; k < centres.Count; k++)
            {
                var (east, north) = Direction(centres[Math.Max(0, k - 1)].Position, centres[Math.Min(centres.Count - 1, k + 1)].Position);
                var centre = centres[k].Position;
                stations.Add(new Station(
                    centre,
                    centres[k].Distance,
                    Offset(centre, -north, east, Math.Max(_port, MinimumRingHalfWidthMeters)),
                    Offset(centre, north, -east, Math.Max(_starboard, MinimumRingHalfWidthMeters))));
            }

            return (stations, spacing);
        }

        // The corridor's ring: port side forward, then starboard side back.
        private IReadOnlyList<GeoPosition> Ring(List<Station> stations)
        {
            var stride = Math.Max(1, (int)Math.Ceiling((stations.Count - 1) / (double)(MaxRingPointsPerSide - 1)));
            var indices = new List<int>();
            for (var k = 0; k < stations.Count - 1; k += stride)
            {
                indices.Add(k);
            }

            indices.Add(stations.Count - 1);
            var ring = new List<GeoPosition>(indices.Count * 2 + 1);
            foreach (var k in indices)
            {
                ring.Add(stations[k].Port);
            }

            for (var i = indices.Count - 1; i >= 0; i--)
            {
                var k = indices[i];
                ring.Add(stations[k].Starboard);
            }

            ring.Add(ring[0]);
            return ring;
        }

        private BoundingBox[] Chunks(List<Station> stations)
        {
            var count = (stations.Count + ChunkSize - 1) / ChunkSize;
            var chunks = new BoundingBox[count];
            for (var c = 0; c < count; c++)
            {
                var first = c * ChunkSize;
                var last = Math.Min(stations.Count - 1, first + ChunkSize);
                chunks[c] = Extent(stations, first, last);
            }

            return chunks;
        }

        private static BoundingBox Extent(List<Station> stations, int first = 0, int last = -1)
        {
            if (last < 0)
            {
                last = stations.Count - 1;
            }

            double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
            for (var k = first; k <= last; k++)
            {
                var s = stations[k];
                foreach (var p in (ReadOnlySpan<GeoPosition>)[s.Centre, s.Port, s.Starboard])
                {
                    south = Math.Min(south, p.Latitude);
                    north = Math.Max(north, p.Latitude);
                    west = Math.Min(west, p.Longitude);
                    east = Math.Max(east, p.Longitude);
                }
            }

            // A little slack so an edge exactly on the limit isn't lost to rounding.
            const double pad = 1e-9;
            return new BoundingBox(south - pad, west - pad, north + pad, east + pad);
        }

        private static bool Overlaps(BoundingBox box, GeoPosition a, GeoPosition b) =>
            Math.Max(a.Latitude, b.Latitude) >= box.SouthLatitude
            && Math.Min(a.Latitude, b.Latitude) <= box.NorthLatitude
            && Math.Max(a.Longitude, b.Longitude) >= box.WestLongitude
            && Math.Min(a.Longitude, b.Longitude) <= box.EastLongitude;

        // The unit direction from a to b, in local east/north metres.
        private static (double East, double North) Direction(GeoPosition a, GeoPosition b)
        {
            var cosLat = Math.Cos((a.Latitude + b.Latitude) / 2 * Math.PI / 180);
            var east = (b.Longitude - a.Longitude) * cosLat;
            var north = b.Latitude - a.Latitude;
            var length = Math.Sqrt(east * east + north * north);
            return length > 0 ? (east / length, north / length) : (0, 0);
        }

        private static GeoPosition Offset(GeoPosition centre, double east, double north, double meters)
        {
            var cosLat = Math.Cos(centre.Latitude * Math.PI / 180);
            return new GeoPosition(
                centre.Latitude + north * meters / PassageGeodesy.MetersPerDegreeLatitude,
                centre.Longitude + east * meters / (PassageGeodesy.MetersPerDegreeLatitude * Math.Max(cosLat, 1e-6)));
        }

        private static double Lerp(double a, double b, double u) => a + (b - a) * u;

        private static GeoPosition Lerp(GeoPosition a, GeoPosition b, double u) =>
            new(Lerp(a.Latitude, b.Latitude, u), Lerp(a.Longitude, b.Longitude, u));

        /// <summary>A feature's edges near the leg, bucketed by chunk of cross-sections.</summary>
        private sealed class EdgeBuckets
        {
            private readonly List<(GeoPosition A, GeoPosition B)>?[] _buckets;

            public EdgeBuckets(LegSweep sweep, List<(GeoPosition A, GeoPosition B)> edges)
            {
                _buckets = new List<(GeoPosition, GeoPosition)>?[sweep._chunks.Length];
                foreach (var edge in edges)
                {
                    for (var c = 0; c < sweep._chunks.Length; c++)
                    {
                        if (Overlaps(sweep._chunks[c], edge.A, edge.B))
                        {
                            (_buckets[c] ??= new List<(GeoPosition, GeoPosition)>()).Add(edge);
                        }
                    }
                }
            }

            // Where the track between station k and k+1 crosses an edge, as fractions along it.
            public void Crossings(int k, GeoPosition a, GeoPosition b, List<double> result)
            {
                if (_buckets[k / ChunkSize] is not { } bucket)
                {
                    return;
                }

                foreach (var (p, q) in bucket)
                {
                    if (PlanarGeometry.Intersect(a, b, p, q) is { } u)
                    {
                        result.Add(u);
                    }
                }
            }

            // Whether station k's cross-section crosses an edge. Its chunk's box holds the whole section.
            public bool CrossesSection(int k, GeoPosition port, GeoPosition starboard)
            {
                if (_buckets[k / ChunkSize] is not { } bucket)
                {
                    return false;
                }

                foreach (var (p, q) in bucket)
                {
                    if (PlanarGeometry.Intersect(port, starboard, p, q) is not null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <summary>Collects findings with ids that are stable within one result, rendered by a formatter.</summary>
    private sealed class FindingList(RouteFindingFormatter formatter)
    {
        private readonly List<RouteFinding> _findings = new();
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        // Each code is in its own group: crossings and dangers in the
        // corridor are MUST REVIEW, the rest CHECK.
        public void Add(
            RouteFindingCode code,
            int legIndex,
            double distance,
            RouteFindingLocation location,
            RouteChartFeature? feature,
            RouteFindingDetails? details = null)
        {
            _counts.TryGetValue(code.Value, out var n);
            _counts[code.Value] = n + 1;
            _findings.Add(formatter.Render(new RouteFinding($"{code.Value}.{n}", code)
            {
                LegIndex = legIndex,
                Distance = Length.FromMetres(distance),
                Location = location,
                Feature = feature is null ? null : new RouteFindingFeature(feature.Cell.DatasetId, feature.FeatureId) { FeatureType = feature.FeatureType },
                Details = details,
            }));
        }

        public IReadOnlyList<RouteFinding> Sorted() => RouteFinding.Sort(_findings);
    }
}
