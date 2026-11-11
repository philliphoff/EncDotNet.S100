namespace EncDotNet.S100.Testing.Scenarios.World;

/// <summary>What the seabed model says is at a point, as the chart's skin of the earth.</summary>
public enum Ground
{
    /// <summary>Covered at chart datum: the depth is zero or more.</summary>
    Water,

    /// <summary>Covers and uncovers: above chart datum but below high water.</summary>
    Drying,

    /// <summary>Above high water (<see cref="SyntheticWorld.HighWaterHeight"/>).</summary>
    Land,
}

/// <summary>A stretch of a route over one kind of <see cref="Ground"/>.</summary>
/// <param name="Kind">The ground.</param>
/// <param name="Leg">The index of the leg the stretch starts on.</param>
/// <param name="Start">Where the stretch starts, in local metres.</param>
/// <param name="End">Where the stretch ends, in local metres.</param>
public sealed record GroundStretch(Ground Kind, int Leg, (double X, double Y) Start, (double X, double Y) End);

/// <summary>
/// The test oracle for route checks against land and drying ground (#954):
/// where a route's track is over drying ground or land in the world model.
/// </summary>
/// <remarks>
/// The S-101 writer charts the same thresholds: <c>LandArea</c> where the
/// depth is less than minus high water, and a <c>DepthArea</c> whose
/// minimum depth is minus high water (so it dries) between there and chart
/// datum. A cell without generalisation charts these boundaries to within
/// its grid spacing.
/// </remarks>
public static class GroundTruth
{
    /// <summary>The ground at a local point.</summary>
    /// <param name="world">The world.</param>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <returns>The ground.</returns>
    public static Ground At(SyntheticWorld world, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(world);
        var depth = world.DepthAt(x, y);
        return depth < -world.HighWaterHeight ? Ground.Land
            : depth < 0 ? Ground.Drying
            : Ground.Water;
    }

    /// <summary>
    /// The stretches of drying ground and land along a route's track, in
    /// route order, found by stepping along each leg and bisecting each
    /// change of ground to a millimetre.
    /// </summary>
    /// <param name="world">The world.</param>
    /// <param name="waypoints">The route's waypoints, in local metres.</param>
    /// <param name="stepMeters">The step along each leg; changes closer together than this may be missed.</param>
    /// <returns>The stretches that aren't water. A stretch running over a waypoint is split at it.</returns>
    public static IReadOnlyList<GroundStretch> Along(SyntheticWorld world, IReadOnlyList<(double X, double Y)> waypoints, double stepMeters = 1)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(waypoints);
        var result = new List<GroundStretch>();
        for (var leg = 0; leg + 1 < waypoints.Count; leg++)
        {
            var a = waypoints[leg];
            var b = waypoints[leg + 1];
            var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            var steps = Math.Max(1, (int)Math.Ceiling(length / stepMeters));
            (double X, double Y) Point(double f) => (a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
            Ground GroundAt(double f)
            {
                var p = Point(f);
                return At(world, p.X, p.Y);
            }

            var kind = GroundAt(0);
            var start = 0.0;
            for (var i = 1; i <= steps; i++)
            {
                var f = (double)i / steps;
                var next = GroundAt(f);
                if (next == kind)
                {
                    continue;
                }

                // Bisect the change between the two steps.
                var lo = (double)(i - 1) / steps;
                var hi = f;
                while ((hi - lo) * length > 0.001)
                {
                    var mid = (lo + hi) / 2;
                    if (GroundAt(mid) == kind)
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                if (kind != Ground.Water)
                {
                    result.Add(new GroundStretch(kind, leg, Point(start), Point(hi)));
                }

                kind = next;
                start = hi;
            }

            if (kind != Ground.Water)
            {
                result.Add(new GroundStretch(kind, leg, Point(start), b));
            }
        }

        return result;
    }
}
