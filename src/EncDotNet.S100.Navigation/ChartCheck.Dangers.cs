using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

// The charted-dangers check (#955).
//
// A danger is a Wreck, an Obstruction or an UnderwaterAwashRock. The rules
// follow the intent of the S-101 portrayal's isolated-danger procedures
// (UDWHAZ05, with OBSTRN07 and WRECKS05 deciding a danger's depth), written
// out here for the route check rather than taken from them:
//
// 1. Its depth is its valueOfSounding (VALSOU), else the defaultClearanceDepth
//    S-101 2.0 gives a danger without one (the source fills it in), unless
//    its waterLevelEffect says it reaches the surface. Without either, its
//    depth is unknown, and its waterLevelEffect says why: above water (1
//    partly submerged at high water, 2 always dry), covers and uncovers (4)
//    or awash (5) reach the surface; anything else is of unknown depth. A
//    danger whose depth is unknown is always a danger: nothing shows that
//    there's water over it.
// 2. A danger with a depth is a danger to the route when it's no deeper
//    than the safety contour, as UDWHAZ05 judges it (#1151), or, with a
//    draft, shallower than draft + squat + required UKC on the static
//    charted depth. Its details name the threshold that fired: the keel one
//    when both do, since it's the one the vessel would touch.
//    A danger counts wherever it lies, in deep water or inside water already
//    shallower than the safety contour: UDWHAZ05 raises its NavHazard alert
//    either way, and leaves the surrounding depth to choose the symbol.
// 3. Only the finest cell counts (#947): a danger is used when its cell is in
//    the finest band of the cells that cover it.
// 4. Distance is the true distance from the leg's centreline to the danger
//    (zero when the track runs over it), on its side. Inside that side's
//    cross-track limit it must be reviewed; within DangerNearbyMeters beyond
//    it, it's worth checking. A danger near the turn between two legs is
//    reported once, on the leg it's closest to.
public sealed partial class ChartCheck
{
    /// <summary>A danger found near a leg, kept until every leg is swept.</summary>
    private sealed record DangerCandidate(
        RouteChartFeature Feature,
        int LegIndex,
        double Distance,
        double Offset,
        GeoPosition Position,
        bool InCorridor,
        DangerDetails Details);

    /// <summary>The dangers found along the route: the closest leg's view of each.</summary>
    private sealed class DangerList
    {
        private readonly Dictionary<(string DatasetId, string FeatureId), DangerCandidate> _candidates = new();
        private readonly List<(string DatasetId, string FeatureId)> _order = new();

        public void Offer(DangerCandidate candidate)
        {
            var key = (candidate.Feature.Cell.DatasetId, candidate.Feature.FeatureId);
            if (!_candidates.TryGetValue(key, out var existing))
            {
                _order.Add(key);
                _candidates[key] = candidate;
            }
            else if (Math.Abs(candidate.Offset) < Math.Abs(existing.Offset))
            {
                _candidates[key] = candidate;
            }
        }

        public void AddTo(FindingList findings)
        {
            foreach (var key in _order)
            {
                var c = _candidates[key];
                findings.Add(
                    c.InCorridor ? RouteFindingCodes.DangerInCorridor : RouteFindingCodes.DangerNearRoute,
                    c.LegIndex,
                    c.Distance,
                    RouteFindingLocation.Point(c.Position, c.Distance),
                    c.Feature,
                    c.Details);
            }
        }
    }

    /// <summary>Where a point is from a leg: distance from departure to its foot on the track, and its signed offset (positive to port).</summary>
    private readonly record struct Projection(double Distance, double Offset);

    private sealed partial class LegSweep
    {
        // The features reach DangerNearbyMeters beyond the corridor, and so
        // do the sweep's cells.
        private void CheckDangers(IReadOnlyList<RouteChartFeature> features, LegDepths depths)
        {
            var nearby = _nearby;
            foreach (var feature in features)
            {
                if (feature.Kind != RouteChartFeatureKind.Danger)
                {
                    continue;
                }

                _cancellationToken.ThrowIfCancellationRequested();
                if (Judge(feature, depths) is not { } threshold)
                {
                    continue;
                }

                var (projection, position) = Nearest(feature);
                var limit = projection.Offset > 0 ? _port : _starboard;
                var offset = Math.Abs(projection.Offset);
                if (offset > limit + nearby || !InBand(_cellIndex[feature.Cell], Band(position)))
                {
                    continue;
                }

                var details = new DangerDetails
                {
                    FeatureType = feature.FeatureType,
                    Depth = feature.DepthMeters is { } depth ? Quantities.Depth.FromMetres(depth) : null,
                    WaterLevelEffect = feature.WaterLevelEffect,
                    ThresholdKind = threshold.Kind,
                    Threshold = threshold.Value is { } value ? Quantities.Depth.FromMetres(value) : null,
                    Side = offset < 0.5 ? RouteFindingSide.OnTrack : projection.Offset > 0 ? RouteFindingSide.Port : RouteFindingSide.Starboard,
                    Offset = Quantities.Length.FromMetres(offset),
                };
                _dangers.Offer(new DangerCandidate(feature, _legIndex, projection.Distance, projection.Offset, position, offset <= limit, details));
            }
        }

        // Rules 1 and 2: whether the danger is one for these depths, and the
        // threshold that fired (none when its depth is unknown), or null when
        // it isn't.
        private static (RouteFindingThreshold? Kind, double? Value)? Judge(RouteChartFeature danger, LegDepths depths)
        {
            if (danger.DepthMeters is not { } depth)
            {
                return (null, null);
            }

            if (depths.KeelMeters is { } keel && depth < keel)
            {
                return (RouteFindingThreshold.Keel, keel);
            }

            return depth <= depths.SafetyContourMeters
                ? (RouteFindingThreshold.SafetyContour, depths.SafetyContourMeters)
                : null;
        }

        // Rule 4: the danger's point nearest the track, and where it is from
        // the track. A line or area the track passes through is on the track.
        private (Projection Projection, GeoPosition Position) Nearest(RouteChartFeature danger)
        {
            if (danger.Points.Count > 0)
            {
                return Closest(danger.Points);
            }

            foreach (var s in _stations)
            {
                if (danger.Surfaces.Count > 0 && PlanarGeometry.Contains(danger.Surfaces, s.Centre))
                {
                    return (new Projection(s.Distance, 0), s.Centre);
                }
            }

            var lines = new List<IReadOnlyList<GeoPosition>>(danger.Curves);
            foreach (var surface in danger.Surfaces)
            {
                lines.Add(surface.ExteriorRing);
            }

            // Where the track crosses the danger's outline.
            var vertices = new List<GeoPosition>();
            foreach (var line in lines)
            {
                vertices.AddRange(line);
                for (var i = 0; i + 1 < line.Count; i++)
                {
                    if (!Overlaps(_extent, line[i], line[i + 1]))
                    {
                        continue;
                    }

                    for (var k = 0; k + 1 < _stations.Count; k++)
                    {
                        if (PlanarGeometry.Intersect(_stations[k].Centre, _stations[k + 1].Centre, line[i], line[i + 1]) is { } u)
                        {
                            var d = Lerp(_stations[k].Distance, _stations[k + 1].Distance, u);
                            return (new Projection(d, 0), Lerp(_stations[k].Centre, _stations[k + 1].Centre, u));
                        }
                    }
                }
            }

            return vertices.Count > 0 ? Closest(vertices) : (new Projection(_stations[0].Distance, double.MaxValue), _stations[0].Centre);
        }

        private (Projection Projection, GeoPosition Position) Closest(IReadOnlyList<GeoPosition> points)
        {
            var best = (Projection: new Projection(0, double.MaxValue), Position: points[0]);
            foreach (var p in points)
            {
                var projection = Project(p);
                if (Math.Abs(projection.Offset) < Math.Abs(best.Projection.Offset))
                {
                    best = (projection, p);
                }
            }

            return best;
        }

        // The point's foot on the track (the stations' polyline) and its
        // signed offset, in local metres around the point.
        private Projection Project(GeoPosition point)
        {
            var scale = Wgs84Geodesy.MetresPerDegree(point.Latitude);
            (double X, double Y) Local(GeoPosition p) => ((p.Longitude - point.Longitude) * scale.East, (p.Latitude - point.Latitude) * scale.North);

            var best = new Projection(_stations[0].Distance, double.MaxValue);
            var bestSquared = double.MaxValue;
            for (var k = 0; k + 1 < _stations.Count; k++)
            {
                var (ax, ay) = Local(_stations[k].Centre);
                var (bx, by) = Local(_stations[k + 1].Centre);
                var dx = bx - ax;
                var dy = by - ay;
                var lengthSquared = dx * dx + dy * dy;
                var u = lengthSquared > 0 ? Math.Clamp(-(ax * dx + ay * dy) / lengthSquared, 0, 1) : 0;
                var fx = ax + dx * u;
                var fy = ay + dy * u;
                var squared = fx * fx + fy * fy;
                if (squared < bestSquared)
                {
                    bestSquared = squared;

                    // The point is at the origin: left of a→b (to port) when a→b × a→point is positive.
                    var side = dx * -ay - dy * -ax;
                    var offset = Math.Sqrt(squared);
                    best = new Projection(Lerp(_stations[k].Distance, _stations[k + 1].Distance, u), side >= 0 ? offset : -offset);
                }
            }

            return best;
        }

        private bool InsideLimits(double offset) => offset > 0 ? offset <= _port : -offset <= _starboard;

        // The corridor widened by extra metres each side, and its box.
        private (IReadOnlyList<GeoPosition> Ring, BoundingBox Extent) WideRing(double extra)
        {
            if (extra <= 0)
            {
                return (_ring, _extent);
            }

            var stride = Math.Max(1, (int)Math.Ceiling((_stations.Count - 1) / (double)(MaxRingPointsPerSide - 1)));
            var indices = new List<int>();
            for (var k = 0; k < _stations.Count - 1; k += stride)
            {
                indices.Add(k);
            }

            indices.Add(_stations.Count - 1);
            var port = new List<GeoPosition>(indices.Count);
            var starboard = new List<GeoPosition>(indices.Count);
            foreach (var k in indices)
            {
                var s = _stations[k];
                port.Add(Widen(s.Centre, s.Port, Math.Max(_port, MinimumRingHalfWidthMeters) + extra));
                starboard.Add(Widen(s.Centre, s.Starboard, Math.Max(_starboard, MinimumRingHalfWidthMeters) + extra));
            }

            // The ends reach forward and back too, so a danger just past a
            // leg's end is measured from that end.
            var first = _stations[0];
            var last = _stations[^1];
            var ring = new List<GeoPosition>(port.Count + starboard.Count + 5);
            ring.Add(Ahead(first.Centre, last.Centre, port[0], -extra));
            ring.AddRange(port);
            ring.Add(Ahead(last.Centre, first.Centre, port[^1], -extra));
            ring.Add(Ahead(last.Centre, first.Centre, starboard[^1], -extra));
            for (var i = starboard.Count - 1; i >= 0; i--)
            {
                ring.Add(starboard[i]);
            }

            ring.Add(Ahead(first.Centre, last.Centre, starboard[0], -extra));
            ring.Add(ring[0]);

            double south = double.MaxValue, west = double.MaxValue, north = double.MinValue, east = double.MinValue;
            foreach (var p in ring)
            {
                south = Math.Min(south, p.Latitude);
                north = Math.Max(north, p.Latitude);
                west = Math.Min(west, p.Longitude);
                east = Math.Max(east, p.Longitude);
            }

            return (ring, new BoundingBox(south, west, north, east));
        }

        // The point at meters from centre through limit.
        private static GeoPosition Widen(GeoPosition centre, GeoPosition limit, double meters)
        {
            var (east, north) = Direction(centre, limit);
            return Offset(centre, east, north, meters);
        }

        // A point moved meters along the direction from 'from' to 'to'.
        private static GeoPosition Ahead(GeoPosition from, GeoPosition to, GeoPosition point, double meters)
        {
            var (east, north) = Direction(from, to);
            return Offset(point, east, north, meters);
        }
    }
}
