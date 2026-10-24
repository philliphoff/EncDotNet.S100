using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>One leg of a <see cref="RouteTrack"/>: where it runs and how far its corridor reaches.</summary>
public sealed class RouteTrackLeg
{
    private readonly double[] _vertexDistances;

    internal RouteTrackLeg(int index, PassageLeg leg, double startMeters, double portMeters, double starboardMeters)
    {
        Index = index;
        Points = leg.Points;
        Geometry = leg.Geometry;
        StartMeters = startMeters;
        PortMeters = portMeters;
        StarboardMeters = starboardMeters;
        _vertexDistances = new double[leg.Points.Count];
        for (var p = 1; p < leg.Points.Count; p++)
        {
            _vertexDistances[p] = _vertexDistances[p - 1] + PassageGeodesy.DistanceMeters(leg.Points[p - 1], leg.Points[p], leg.Geometry);
        }

        LengthMeters = _vertexDistances[^1];
    }

    /// <summary>The leg's index in the plan.</summary>
    public int Index { get; }

    /// <summary>The leg's positions in order, as the plan gives them.</summary>
    public IReadOnlyList<GeoPosition> Points { get; }

    /// <summary>Whether the leg is a rhumb line or a great circle.</summary>
    public LegGeometry Geometry { get; }

    /// <summary>Distance from departure to the leg's start, in metres.</summary>
    public double StartMeters { get; }

    /// <summary>The leg's length, in metres.</summary>
    public double LengthMeters { get; }

    /// <summary>Distance from departure to the leg's end, in metres.</summary>
    public double EndMeters => StartMeters + LengthMeters;

    /// <summary>
    /// How far the corridor reaches to port of the track, in metres: the
    /// settings' override, the leg's limit, or the default.
    /// </summary>
    public double PortMeters { get; }

    /// <summary>How far the corridor reaches to starboard of the track, in metres.</summary>
    public double StarboardMeters { get; }

    /// <summary>The position <paramref name="alongMeters"/> into the leg, clamped to it.</summary>
    internal GeoPosition PositionAlong(double alongMeters)
    {
        var d = Math.Clamp(alongMeters, 0, LengthMeters);
        var p = Segment(d);
        var length = _vertexDistances[p + 1] - _vertexDistances[p];
        var fraction = length > 0 ? (d - _vertexDistances[p]) / length : 0;
        return PassageGeodesy.Interpolate(Points[p], Points[p + 1], fraction, Geometry);
    }

    /// <summary>The positions along the leg between two distances into it, with each vertex between them.</summary>
    internal void AddPath(double fromMeters, double toMeters, List<GeoPosition> path)
    {
        var a = Math.Clamp(fromMeters, 0, LengthMeters);
        var b = Math.Clamp(toMeters, a, LengthMeters);
        Add(path, PositionAlong(a));
        for (var p = Segment(a); p < Points.Count - 1; p++)
        {
            var segmentStart = Math.Max(a, _vertexDistances[p]);
            var segmentEnd = Math.Min(b, _vertexDistances[p + 1]);
            if (segmentEnd < segmentStart)
            {
                break;
            }

            // A great circle is drawn in pieces so the path follows the curve.
            var length = _vertexDistances[p + 1] - _vertexDistances[p];
            var steps = Geometry == LegGeometry.GreatCircle && length > 0
                ? Math.Max(1, (int)Math.Ceiling((segmentEnd - segmentStart) / length * RouteTrack.GeodesicSegments))
                : 1;
            for (var k = 1; k <= steps; k++)
            {
                var d = segmentStart + ((segmentEnd - segmentStart) * k / steps);
                var fraction = length > 0 ? (d - _vertexDistances[p]) / length : 0;
                Add(path, PassageGeodesy.Interpolate(Points[p], Points[p + 1], fraction, Geometry));
            }
        }

        static void Add(List<GeoPosition> path, GeoPosition position)
        {
            if (path.Count == 0 || path[^1] != position)
            {
                path.Add(position);
            }
        }
    }

    // The segment a distance into the leg is on: the earlier one at a vertex.
    private int Segment(double alongMeters)
    {
        for (var p = 0; p < Points.Count - 2; p++)
        {
            if (alongMeters <= _vertexDistances[p + 1])
            {
                return p;
            }
        }

        return Points.Count - 2;
    }
}

/// <summary>
/// The track of a <see cref="PassagePlan"/>, built once per check and shared
/// by every check: each leg's distances and corridor half-widths, and the
/// position at a distance from departure.
/// </summary>
/// <remarks>
/// Distances run along each leg's own geometry (rhumb line or great circle),
/// as <see cref="PassageSchedule"/> measures them.
/// </remarks>
public sealed class RouteTrack
{
    /// <summary>Pieces a great-circle segment's path is drawn with, end to end.</summary>
    internal const int GeodesicSegments = 24;

    private readonly PassagePlan _plan;
    private readonly RouteCheckSettings _settings;
    private readonly double _defaultMeters;
    private readonly Dictionary<(double, int), RouteCorridor.Track> _densified = new();

    private RouteTrack(PassagePlan plan, RouteCheckSettings settings, double defaultMeters, IReadOnlyList<RouteTrackLeg> legs)
    {
        _plan = plan;
        _settings = settings;
        _defaultMeters = defaultMeters;
        Legs = legs;
    }

    /// <summary>The legs in route order.</summary>
    public IReadOnlyList<RouteTrackLeg> Legs { get; }

    /// <summary>The route's length, in metres.</summary>
    public double LengthMeters => Legs.Count == 0 ? 0 : Legs[^1].EndMeters;

    /// <summary>Builds the track of a plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="settings">The corridor override.</param>
    /// <param name="defaultLimit">The half-width of a side whose cross-track limit isn't set.</param>
    /// <returns>The track.</returns>
    /// <exception cref="ArgumentException">A leg is null or has fewer than two points.</exception>
    public static RouteTrack Build(PassagePlan plan, RouteCheckSettings settings, Length defaultLimit)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        var defaultMeters = defaultLimit.TotalMetres;
        var legs = new RouteTrackLeg[plan.Legs.Count];
        var offset = 0.0;
        for (var i = 0; i < legs.Length; i++)
        {
            var leg = plan.Legs[i] ?? throw new ArgumentException($"Leg {i} is null.", nameof(plan));
            if (leg.Points is null || leg.Points.Count < 2)
            {
                throw new ArgumentException($"Leg {i} needs at least two points.", nameof(plan));
            }

            legs[i] = new RouteTrackLeg(i, leg, offset, settings.PortMeters(leg, defaultMeters), settings.StarboardMeters(leg, defaultMeters));
            offset = legs[i].EndMeters;
        }

        return new RouteTrack(plan, settings, defaultMeters, legs);
    }

    /// <summary>The leg a distance from departure is on: the earlier leg at a waypoint.</summary>
    /// <param name="distanceMeters">Distance from departure, in metres.</param>
    /// <returns>The leg's index; 0 for an empty track.</returns>
    public int LegAt(double distanceMeters)
    {
        for (var i = Legs.Count - 1; i > 0; i--)
        {
            if (distanceMeters > Legs[i].StartMeters)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>The position at a distance from departure, clamped to the route.</summary>
    /// <param name="distanceMeters">Distance from departure, in metres.</param>
    /// <returns>The position.</returns>
    /// <exception cref="InvalidOperationException">The track has no legs.</exception>
    public GeoPosition PositionAt(double distanceMeters)
    {
        if (Legs.Count == 0)
        {
            throw new InvalidOperationException("The track has no legs.");
        }

        var leg = Legs[LegAt(distanceMeters)];
        return leg.PositionAlong(distanceMeters - leg.StartMeters);
    }

    /// <summary>
    /// The positions along the route between two distances from departure,
    /// across legs and through every waypoint between them.
    /// </summary>
    /// <param name="fromMeters">Distance from departure to the start.</param>
    /// <param name="toMeters">Distance from departure to the end.</param>
    /// <returns>The path; one position when the span is empty.</returns>
    /// <exception cref="InvalidOperationException">The track has no legs.</exception>
    public IReadOnlyList<GeoPosition> PathBetween(double fromMeters, double toMeters)
    {
        if (Legs.Count == 0)
        {
            throw new InvalidOperationException("The track has no legs.");
        }

        var from = Math.Clamp(fromMeters, 0, LengthMeters);
        var to = Math.Clamp(toMeters, from, LengthMeters);
        var path = new List<GeoPosition>();
        foreach (var leg in Legs)
        {
            if (leg.EndMeters < from || leg.StartMeters > to || (path.Count > 0 && leg.StartMeters == to))
            {
                continue;
            }

            leg.AddPath(from - leg.StartMeters, to - leg.StartMeters, path);
        }

        return path;
    }

    /// <summary>The location of a span of the route: a point when it is empty.</summary>
    /// <param name="fromMeters">Distance from departure to the start.</param>
    /// <param name="toMeters">Distance from departure to the end.</param>
    /// <returns>The location.</returns>
    public RouteFindingLocation Location(double fromMeters, double toMeters)
    {
        var path = PathBetween(fromMeters, toMeters);
        return path.Count == 1 ? RouteFindingLocation.Point(path[0], fromMeters) : new RouteFindingLocation(path, fromMeters, toMeters);
    }

    /// <summary>
    /// The track cut into pieces of at most <paramref name="segmentLengthMeters"/>,
    /// for the area and warning checks. Built once per spacing and shared.
    /// </summary>
    internal RouteCorridor.Track Densified(double segmentLengthMeters, int maxSegmentsPerLeg)
    {
        lock (_densified)
        {
            if (!_densified.TryGetValue((segmentLengthMeters, maxSegmentsPerLeg), out var track))
            {
                track = RouteCorridor.Track.Build(_plan, _settings, _defaultMeters, segmentLengthMeters, maxSegmentsPerLeg);
                _densified[(segmentLengthMeters, maxSegmentsPerLeg)] = track;
            }

            return track;
        }
    }
}
