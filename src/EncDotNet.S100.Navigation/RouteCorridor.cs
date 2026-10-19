using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The corridor geometry <see cref="RouteWarningCheck"/> and
/// <see cref="RouteAreaCheck"/> share: the route cut into short pieces, and
/// how a set of points, lines and areas meets each piece and the corridor
/// between its cross-track limits.
/// </summary>
/// <remarks>
/// Within a piece the track, the shapes and the corridor are compared in a
/// local plane, so crossings are exact to the piece's straightness. A line
/// or area crosses when it touches the track or comes within the leg's
/// cross-track limit on its side.
/// </remarks>
internal static class RouteCorridor
{
    // The most positions in a span's path.
    private const int MaxPathPoints = 64;

    /// <summary>Measures every piece of the track against every set of shapes it may reach.</summary>
    /// <param name="track">The track.</param>
    /// <param name="shapes">The shapes, one set per item checked.</param>
    /// <param name="reach">How far from the track a shape is still measured, in metres.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    /// <returns>One accumulator per set of shapes.</returns>
    public static Accumulator[] Measure(Track track, IReadOnlyList<Shapes> shapes, double reach, CancellationToken cancellationToken)
    {
        var accumulators = shapes.Select(_ => new Accumulator()).ToArray();
        for (var k = 0; k + 1 < track.Samples.Count; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var a = track.Samples[k];
            var b = track.Samples[k + 1];
            if (a.Leg != b.Leg || b.Distance <= a.Distance)
            {
                continue;
            }

            for (var w = 0; w < shapes.Count; w++)
            {
                if (shapes[w].Near(a.Position, b.Position, reach))
                {
                    Measure(shapes[w], a, b, track.Legs[a.Leg], accumulators[w]);
                }
            }
        }

        return accumulators;
    }

    /// <summary>A point, or the span of the track between two distances, as a finding's location.</summary>
    public static RouteFindingLocation Location(Track track, double start, double end)
    {
        if (end - start < 1e-6)
        {
            return RouteFindingLocation.Point(track.PositionAt(start), start);
        }

        var path = new List<GeoPosition> { track.PositionAt(start) };
        var inner = track.Samples.Where(s => s.Distance > start && s.Distance < end).ToList();
        var stride = Math.Max(1, (int)Math.Ceiling(inner.Count / (double)(MaxPathPoints - 2)));
        for (var i = 0; i < inner.Count; i += stride)
        {
            path.Add(inner[i].Position);
        }

        path.Add(track.PositionAt(end));
        return new RouteFindingLocation(path, start, end);
    }

    // Compares one piece of track with one set of shapes, in a plane centred on the piece's start.
    private static void Measure(Shapes shapes, Sample a, Sample b, LegInfo leg, Accumulator acc)
    {
        var frame = new LocalFrame(a.Position);
        var bLocal = frame.ToLocal(b.Position);
        var origin = (X: 0.0, Y: 0.0);
        var length = b.Distance - a.Distance;

        void Pair((double X, double Y) onTrack, double t, (double X, double Y) onShape, bool isPoint)
        {
            var dx = onShape.X - onTrack.X;
            var dy = onShape.Y - onTrack.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var along = a.Distance + t * length;
            if (distance < acc.Closest)
            {
                acc.Closest = distance;
                acc.ClosestAlong = along;
            }

            // Left of the direction of travel is port.
            var side = bLocal.X * dy - bLocal.Y * dx;
            var limit = side >= 0 ? leg.Port : leg.Starboard;
            if (distance <= limit)
            {
                if (isPoint)
                {
                    acc.PointInCorridor = true;
                }
                else
                {
                    if (distance < acc.CorridorClosest)
                    {
                        acc.CorridorClosest = distance;
                        acc.CorridorAlong = along;
                    }

                    acc.CorridorStart = acc.CorridorStart is { } s ? Math.Min(s, along) : along;
                    acc.CorridorEnd = acc.CorridorEnd is { } e ? Math.Max(e, along) : along;
                }
            }
        }

        void Vertex((double X, double Y) q, bool isPoint)
        {
            var (t, onTrack) = Closest(origin, bLocal, q);
            Pair(onTrack, t, q, isPoint);
        }

        void Edge((double X, double Y) p, (double X, double Y) q)
        {
            Pair(origin, 0, Closest(p, q, origin).Point, isPoint: false);
            Pair(bLocal, 1, Closest(p, q, bLocal).Point, isPoint: false);
        }

        foreach (var point in shapes.Points)
        {
            Vertex(frame.ToLocal(point), isPoint: true);
        }

        foreach (var line in shapes.Lines)
        {
            var local = line.Select(frame.ToLocal).ToArray();
            for (var i = 0; i < local.Length; i++)
            {
                Vertex(local[i], isPoint: false);
                if (i == 0)
                {
                    continue;
                }

                Edge(local[i - 1], local[i]);
                if (Intersect(origin, bLocal, local[i - 1], local[i]) is { } t)
                {
                    acc.AddCrossing(a.Distance + t * length, area: false);
                }
            }
        }

        foreach (var area in shapes.Areas)
        {
            var rings = area.Select(ring => ring.Select(frame.ToLocal).ToArray()).ToArray();
            var crossings = new List<double> { 0, 1 };
            foreach (var ring in rings)
            {
                for (var i = 0; i < ring.Length; i++)
                {
                    var p = ring[i];
                    var q = ring[(i + 1) % ring.Length];
                    Vertex(p, isPoint: false);
                    Edge(p, q);
                    if (Intersect(origin, bLocal, p, q) is { } t)
                    {
                        crossings.Add(t);
                    }
                }
            }

            // Between crossings the piece is wholly in or out; test each stretch at its middle.
            crossings.Sort();
            for (var c = 1; c < crossings.Count; c++)
            {
                var from = crossings[c - 1];
                var to = crossings[c];
                var middle = (from + to) / 2;
                if (to - from < 1e-12 || !Inside(rings, (bLocal.X * middle, bLocal.Y * middle)))
                {
                    continue;
                }

                acc.AddCrossing(a.Distance + from * length, area: true);
                acc.AddCrossing(a.Distance + to * length, area: true);
            }
        }
    }

    // The fraction along a–b of the point closest to q, and that point.
    private static (double T, (double X, double Y) Point) Closest((double X, double Y) a, (double X, double Y) b, (double X, double Y) q)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((q.X - a.X) * dx + (q.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return (t, (a.X + t * dx, a.Y + t * dy));
    }

    // Where a–b crosses p–q, as a fraction along a–b, or null.
    private static double? Intersect((double X, double Y) a, (double X, double Y) b, (double X, double Y) p, (double X, double Y) q)
    {
        var rx = b.X - a.X;
        var ry = b.Y - a.Y;
        var sx = q.X - p.X;
        var sy = q.Y - p.Y;
        var denominator = rx * sy - ry * sx;
        if (Math.Abs(denominator) < 1e-12)
        {
            return null;
        }

        var qx = p.X - a.X;
        var qy = p.Y - a.Y;
        var t = (qx * sy - qy * sx) / denominator;
        var u = (qx * ry - qy * rx) / denominator;
        const double epsilon = 1e-9;
        return t is >= -epsilon and <= 1 + epsilon && u is >= -epsilon and <= 1 + epsilon ? Math.Clamp(t, 0, 1) : null;
    }

    // Even-odd over every ring, so holes are outside.
    private static bool Inside((double X, double Y)[][] rings, (double X, double Y) point)
    {
        var inside = false;
        foreach (var ring in rings)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Y > point.Y) != (b.Y > point.Y)
                    && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <summary>A point on the track.</summary>
    internal readonly record struct Sample(GeoPosition Position, double Distance, int Leg);

    /// <summary>A leg's distance from departure and its corridor's half-widths.</summary>
    internal readonly record struct LegInfo(double Start, double Port, double Starboard);

    /// <summary>The route cut into pieces.</summary>
    internal sealed class Track
    {
        private Track(List<Sample> samples, List<LegInfo> legs)
        {
            Samples = samples;
            Legs = legs;
            MaxHalfWidth = legs.Count == 0 ? 0 : legs.Max(l => Math.Max(l.Port, l.Starboard));
        }

        public List<Sample> Samples { get; }

        public List<LegInfo> Legs { get; }

        public double MaxHalfWidth { get; }

        /// <summary>The route's length in metres.</summary>
        public double Length => Samples.Count == 0 ? 0 : Samples[^1].Distance;

        /// <summary>Cuts the legs into pieces.</summary>
        /// <param name="legs">The legs.</param>
        /// <param name="defaultHalfWidthMeters">The half-width of a side whose cross-track limit isn't set.</param>
        /// <param name="segmentLengthMeters">The length of the pieces.</param>
        /// <param name="maxSegmentsPerLeg">The most pieces per leg.</param>
        /// <returns>The track.</returns>
        public static Track Build(IReadOnlyList<RouteCheckLeg> legs, double defaultHalfWidthMeters, double segmentLengthMeters, int maxSegmentsPerLeg)
        {
            var samples = new List<Sample>();
            var infos = new List<LegInfo>(legs.Count);
            var offset = 0.0;
            for (var i = 0; i < legs.Count; i++)
            {
                var leg = legs[i] ?? throw new ArgumentException($"Leg {i} is null.", nameof(legs));
                if (leg.Points is null || leg.Points.Count < 2)
                {
                    throw new ArgumentException($"Leg {i} needs at least two points.", nameof(legs));
                }

                infos.Add(new LegInfo(
                    offset,
                    Math.Max(0, leg.PortCrossTrackMeters ?? defaultHalfWidthMeters),
                    Math.Max(0, leg.StarboardCrossTrackMeters ?? defaultHalfWidthMeters)));
                var points = leg.Points;
                var lengths = new double[points.Count - 1];
                for (var p = 0; p < lengths.Length; p++)
                {
                    lengths[p] = PassageGeodesy.DistanceMeters(points[p], points[p + 1], leg.Geometry);
                }

                var spacing = Math.Max(segmentLengthMeters, lengths.Sum() / maxSegmentsPerLeg);
                samples.Add(new Sample(points[0], offset, i));
                for (var p = 0; p < lengths.Length; p++)
                {
                    var n = Math.Max(1, (int)Math.Ceiling(lengths[p] / spacing));
                    for (var s = 1; s <= n; s++)
                    {
                        var f = (double)s / n;
                        samples.Add(new Sample(PassageGeodesy.Interpolate(points[p], points[p + 1], f, leg.Geometry), offset + lengths[p] * f, i));
                    }

                    offset += lengths[p];
                }
            }

            return new Track(samples, infos);
        }

        /// <summary>The leg a distance from departure is on: the earlier leg at a waypoint.</summary>
        public int LegAt(double distance)
        {
            for (var i = Legs.Count - 1; i > 0; i--)
            {
                if (distance > Legs[i].Start)
                {
                    return i;
                }
            }

            return 0;
        }

        public GeoPosition PositionAt(double distance)
        {
            var leg = LegAt(distance);
            Sample? previous = null;
            foreach (var sample in Samples)
            {
                if (sample.Leg != leg)
                {
                    continue;
                }

                if (sample.Distance >= distance)
                {
                    if (previous is not { } p || sample.Distance <= p.Distance)
                    {
                        return sample.Position;
                    }

                    var f = (distance - p.Distance) / (sample.Distance - p.Distance);
                    return new GeoPosition(
                        p.Position.Latitude + f * (sample.Position.Latitude - p.Position.Latitude),
                        p.Position.Longitude + f * LocalFrame.Wrap(sample.Position.Longitude - p.Position.Longitude));
                }

                previous = sample;
            }

            return previous?.Position ?? Samples[^1].Position;
        }
    }

    /// <summary>A set of points, lines and areas, split by kind, with its extent.</summary>
    internal sealed class Shapes
    {
        private double _south = double.MaxValue;
        private double _north = double.MinValue;
        private readonly double _centreLongitude;
        private readonly double _halfLongitudeSpan;
        private readonly bool _wide;

        /// <summary>The shapes of feature geometries: their points, curves and surfaces.</summary>
        public Shapes(IEnumerable<S100FeatureGeometry> geometries)
            : this(Split(geometries))
        {
        }

        /// <summary>The shapes of surfaces only, with their holes.</summary>
        public Shapes(IEnumerable<SurfacePart> surfaces)
            : this(([], [], surfaces.Select(s => (s.ExteriorRing, s.InteriorRings)).ToList()))
        {
        }

        private Shapes((List<GeoPosition> Points, List<IReadOnlyList<GeoPosition>> Curves, List<(IReadOnlyList<GeoPosition> Exterior, IReadOnlyList<IReadOnlyList<GeoPosition>> Holes)> Surfaces) parts)
        {
            var longitudes = new List<double>();
            void Add(IEnumerable<GeoPosition> positions)
            {
                foreach (var p in positions)
                {
                    _south = Math.Min(_south, p.Latitude);
                    _north = Math.Max(_north, p.Latitude);
                    longitudes.Add(p.Longitude);
                }
            }

            Points.AddRange(parts.Points);
            Add(parts.Points);
            foreach (var curve in parts.Curves)
            {
                if (curve.Count >= 2)
                {
                    Lines.Add(curve);
                }
                else
                {
                    Points.AddRange(curve);
                }

                Add(curve);
            }

            foreach (var (exterior, holes) in parts.Surfaces)
            {
                if (exterior.Count < 3)
                {
                    continue;
                }

                var rings = new List<IReadOnlyList<GeoPosition>> { Open(exterior) };
                rings.AddRange(holes.Where(r => r.Count >= 3).Select(Open));
                Areas.Add(rings);
                Add(exterior);
            }

            if (longitudes.Count == 0)
            {
                return;
            }

            double west = longitudes.Min(), east = longitudes.Max();
            _wide = east - west > 180;
            _centreLongitude = (west + east) / 2;
            _halfLongitudeSpan = (east - west) / 2;
        }

        public List<GeoPosition> Points { get; } = [];

        public List<IReadOnlyList<GeoPosition>> Lines { get; } = [];

        public List<List<IReadOnlyList<GeoPosition>>> Areas { get; } = [];

        /// <summary>Whether the piece a–b may come within <paramref name="reach"/> metres of the shapes.</summary>
        public bool Near(GeoPosition a, GeoPosition b, double reach)
        {
            if (_south > _north)
            {
                return false;
            }

            var latitudeReach = reach / PassageGeodesy.MetersPerDegreeLatitude;
            if (Math.Max(a.Latitude, b.Latitude) < _south - latitudeReach || Math.Min(a.Latitude, b.Latitude) > _north + latitudeReach)
            {
                return false;
            }

            if (_wide)
            {
                return true;
            }

            var cos = Math.Cos(Math.Max(Math.Abs(a.Latitude), Math.Abs(b.Latitude)) * Math.PI / 180);
            var longitudeReach = cos < 1e-6 ? 360 : latitudeReach / cos;
            var da = Math.Abs(LocalFrame.Wrap(a.Longitude - _centreLongitude));
            var db = Math.Abs(LocalFrame.Wrap(b.Longitude - _centreLongitude));
            var segmentHalf = Math.Abs(LocalFrame.Wrap(b.Longitude - a.Longitude)) / 2;
            return Math.Min(da, db) - segmentHalf <= _halfLongitudeSpan + longitudeReach;
        }

        private static (List<GeoPosition>, List<IReadOnlyList<GeoPosition>>, List<(IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>)>) Split(IEnumerable<S100FeatureGeometry> geometries)
        {
            var points = new List<GeoPosition>();
            var curves = new List<IReadOnlyList<GeoPosition>>();
            var surfaces = new List<(IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>)>();
            foreach (var shape in geometries)
            {
                points.AddRange(shape.Points);
                curves.AddRange(shape.Curves);
                if (shape.ExteriorRing.Count > 0)
                {
                    surfaces.Add((shape.ExteriorRing, shape.InteriorRings));
                }
            }

            return (points, curves, surfaces);
        }

        // Drops a closed ring's repeated last point.
        private static IReadOnlyList<GeoPosition> Open(IReadOnlyList<GeoPosition> ring) =>
            ring.Count > 1 && ring[0].Equals(ring[^1]) ? ring.Take(ring.Count - 1).ToArray() : ring;
    }

    /// <summary>What a set of shapes does along the route.</summary>
    internal sealed class Accumulator
    {
        /// <summary>Where the track first meets a line or enters an area.</summary>
        public double? CrossStart { get; private set; }

        /// <summary>Where the track last meets a line or leaves an area.</summary>
        public double? CrossEnd { get; private set; }

        public bool CrossedArea { get; private set; }

        public double Closest { get; set; } = double.PositiveInfinity;

        public double ClosestAlong { get; set; } = double.PositiveInfinity;

        public double CorridorClosest { get; set; } = double.PositiveInfinity;

        /// <summary>Where on the track a line or area comes closest within the cross-track limits.</summary>
        public double? CorridorAlong { get; set; }

        /// <summary>The first place on the track a line or area comes within the cross-track limits.</summary>
        public double? CorridorStart { get; set; }

        /// <summary>The last place on the track a line or area comes within the cross-track limits.</summary>
        public double? CorridorEnd { get; set; }

        public bool PointInCorridor { get; set; }

        public void AddCrossing(double along, bool area)
        {
            if (Closest > 0)
            {
                Closest = 0;
                ClosestAlong = along;
            }

            CrossStart = CrossStart is { } s ? Math.Min(s, along) : along;
            CrossEnd = CrossEnd is { } e ? Math.Max(e, along) : along;
            CrossedArea |= area;
        }
    }

    /// <summary>An equirectangular plane in metres, centred on one position.</summary>
    internal readonly struct LocalFrame(GeoPosition origin)
    {
        private readonly double _cos = Math.Cos(origin.Latitude * Math.PI / 180);

        public (double X, double Y) ToLocal(GeoPosition p) => (
            Wrap(p.Longitude - origin.Longitude) * _cos * PassageGeodesy.MetersPerDegreeLatitude,
            (p.Latitude - origin.Latitude) * PassageGeodesy.MetersPerDegreeLatitude);

        public static double Wrap(double degrees) =>
            degrees > 180 ? degrees - 360 : degrees < -180 ? degrees + 360 : degrees;
    }
}
