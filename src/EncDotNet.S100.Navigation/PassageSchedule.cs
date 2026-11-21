namespace EncDotNet.S100.Navigation;

/// <summary>The timing of one leg within a <see cref="PassageSchedule"/>.</summary>
/// <param name="LegIndex">Zero-based index of the leg.</param>
/// <param name="StartDistanceMeters">Distance from the start of the passage to the start of this leg, in metres.</param>
/// <param name="DistanceMeters">Length of the leg in metres.</param>
/// <param name="Duration">Time to run the leg, or <c>null</c> when the leg (or an earlier one) has no speed.</param>
/// <param name="Start">ETA at the start of the leg, or <c>null</c> when unknown.</param>
/// <param name="End">ETA at the end of the leg, or <c>null</c> when unknown.</param>
public sealed record LegSchedule(
    int LegIndex,
    double StartDistanceMeters,
    double DistanceMeters,
    TimeSpan? Duration,
    DateTimeOffset? Start,
    DateTimeOffset? End)
{
    /// <summary>Length of the leg in nautical miles.</summary>
    public double DistanceNauticalMiles => DistanceMeters / PassageGeodesy.MetersPerNauticalMile;
}

/// <summary>A stretch of a passage where currents were to be applied but none covered the ETA.</summary>
/// <param name="LegIndex">Zero-based index of the leg.</param>
/// <param name="StartAlongLegMeters">Where the stretch starts, from the start of the leg, in metres.</param>
/// <param name="EndAlongLegMeters">Where it ends, from the start of the leg, in metres.</param>
public sealed record PassageCurrentGap(int LegIndex, double StartAlongLegMeters, double EndAlongLegMeters);

/// <summary>
/// Distances, durations and ETAs for a passage. Build one with
/// <see cref="Compute(PassagePlan, RouteCheckSettings, RouteCheckSources)"/>.
/// </summary>
/// <remarks>
/// <para>
/// Without currents, ETAs are departure plus distance divided by the entered
/// speed, taken as over the ground. With a current source (speeds through
/// the water, #1033), each leg is cut into stretches of at most
/// <see cref="CurrentStretchMeters"/>, and each stretch's speed over ground
/// is the speed plus the current along the track at the stretch's ETA, as
/// the timing check works it out; where no current covers the ETA, the
/// speed stands (<see cref="CurrentGaps"/>).
/// </para>
/// <para>
/// A leg with no speed has no duration, and the ETAs from its start onward
/// are unknown.
/// </para>
/// </remarks>
public sealed class PassageSchedule
{
    /// <summary>The longest stretch one current reading stands for, in metres: 500 m, as the timing check reads it.</summary>
    public const double CurrentStretchMeters = 500;

    // A foul current never brings the boat below this over the ground, in knots.
    private const double MinSpeedOverGroundKnots = 0.5;

    private readonly double[][] _segmentStarts;

    // With currents, per leg: distances along it and the seconds to reach each.
    private readonly (double[] Along, double[] Seconds)?[] _profiles;

    private PassageSchedule(
        DateTimeOffset departure,
        IReadOnlyList<LegSchedule> legs,
        double[][] segmentStarts,
        IReadOnlyList<PassageLeg> source,
        (double[] Along, double[] Seconds)?[]? profiles = null,
        bool currentsApplied = false,
        IReadOnlyList<RouteCurrentSourceInfo>? currentSources = null,
        IReadOnlyList<PassageCurrentGap>? currentGaps = null)
    {
        Departure = departure;
        Legs = legs;
        _segmentStarts = segmentStarts;
        _profiles = profiles ?? new (double[], double[])?[legs.Count];
        Source = source;
        CurrentsApplied = currentsApplied;
        CurrentSources = currentSources ?? [];
        CurrentGaps = currentGaps ?? [];
        TotalDistanceMeters = legs.Count == 0 ? 0 : legs[^1].StartDistanceMeters + legs[^1].DistanceMeters;
        Arrival = legs.Count == 0 ? departure : legs[^1].End;
    }

    /// <summary>The departure time.</summary>
    public DateTimeOffset Departure { get; }

    /// <summary>The arrival time, or <c>null</c> when any leg has no speed.</summary>
    public DateTimeOffset? Arrival { get; }

    /// <summary>Total length of the passage in metres.</summary>
    public double TotalDistanceMeters { get; }

    /// <summary>Per-leg distances and times, in route order.</summary>
    public IReadOnlyList<LegSchedule> Legs { get; }

    /// <summary>Total time under way, or <c>null</c> when any leg has no speed.</summary>
    public TimeSpan? Duration => Arrival - Departure;

    internal IReadOnlyList<PassageLeg> Source { get; }

    /// <summary>Whether currents were applied to the ETAs: speeds through the water, with a current source.</summary>
    public bool CurrentsApplied { get; }

    /// <summary>The current datasets and stations read for the ETAs, in the order first read.</summary>
    public IReadOnlyList<RouteCurrentSourceInfo> CurrentSources { get; }

    /// <summary>The stretches where currents were applied elsewhere but none covered the ETA, so the speed was taken as over the ground.</summary>
    public IReadOnlyList<PassageCurrentGap> CurrentGaps { get; }

    /// <summary>Computes the schedule for <paramref name="legs"/> leaving at <paramref name="departure"/>, at the entered speeds over the ground.</summary>
    /// <param name="legs">The legs in route order.</param>
    /// <param name="departure">The departure time.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException">A leg has fewer than two points, or a negative or non-finite speed.</exception>
    public static PassageSchedule Compute(IReadOnlyList<PassageLeg> legs, DateTimeOffset departure) =>
        Compute(legs, departure, null);

    /// <summary>
    /// Computes the schedule for <paramref name="legs"/> leaving at
    /// <paramref name="departure"/>. With <paramref name="currents"/>, the
    /// speeds are through the water and the current along the track is added
    /// to them (#1033).
    /// </summary>
    /// <param name="legs">The legs in route order.</param>
    /// <param name="departure">The departure time.</param>
    /// <param name="currents">The currents to apply, or <c>null</c> for speeds over the ground.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException">A leg has fewer than two points, or a negative or non-finite speed.</exception>
    public static PassageSchedule Compute(IReadOnlyList<PassageLeg> legs, DateTimeOffset departure, IRouteCurrentSource? currents)
    {
        ArgumentNullException.ThrowIfNull(legs);

        var result = new List<LegSchedule>(legs.Count);
        var segmentStarts = new double[legs.Count][];
        var profiles = new (double[] Along, double[] Seconds)?[legs.Count];
        var sources = new List<RouteCurrentSourceInfo>();
        var gaps = new List<PassageCurrentGap>();
        double distance = 0;
        DateTimeOffset? clock = departure;
        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i] ?? throw new ArgumentException($"Leg {i} is null.", nameof(legs));
            if (leg.Points is null || leg.Points.Count < 2)
            {
                throw new ArgumentException($"Leg {i} needs at least two points.", nameof(legs));
            }

            if (leg.Speed is { } s && (!double.IsFinite(s.TotalMetresPerSecond) || s.TotalMetresPerSecond < 0))
            {
                throw new ArgumentException($"Leg {i} has an invalid speed ({s.TotalKnots} kn).", nameof(legs));
            }

            var starts = new double[leg.Points.Count];
            double legLength = 0;
            for (var p = 1; p < leg.Points.Count; p++)
            {
                starts[p - 1] = legLength;
                legLength += PassageGeodesy.DistanceMeters(leg.Points[p - 1], leg.Points[p], leg.Geometry);
            }

            starts[^1] = legLength;
            segmentStarts[i] = starts;

            TimeSpan? duration = SpeedMetersPerSecond(leg) is { } mps ? TimeSpan.FromSeconds(legLength / mps) : null;
            if (currents is not null && clock is { } at && SpeedMetersPerSecond(leg) is { } stw && stw > 0)
            {
                var profile = Profile(leg, starts, at, stw, currents, i, sources, gaps);
                profiles[i] = profile;
                duration = TimeSpan.FromSeconds(profile.Seconds[^1]);
            }

            var start = clock;
            clock = clock is { } c && duration is { } d ? c + d : null;
            result.Add(new LegSchedule(i, distance, legLength, duration, start, clock));
            distance += legLength;
        }

        return new PassageSchedule(departure, result, segmentStarts, legs, profiles, currents is not null, sources, Merge(gaps));
    }

    // A leg's ETAs with the current along the track: the seconds to reach
    // the end of each stretch, the stretch's current read at its start.
    private static (double[] Along, double[] Seconds) Profile(
        PassageLeg leg,
        double[] starts,
        DateTimeOffset start,
        double stwMetersPerSecond,
        IRouteCurrentSource currents,
        int legIndex,
        List<RouteCurrentSourceInfo> sources,
        List<PassageCurrentGap> gaps)
    {
        var along = new List<double> { 0 };
        var seconds = new List<double> { 0 };
        var stwKnots = stwMetersPerSecond * 3600 / PassageGeodesy.MetersPerNauticalMile;
        for (var p = 1; p < leg.Points.Count; p++)
        {
            var a = leg.Points[p - 1];
            var b = leg.Points[p];
            var length = starts[p] - starts[p - 1];
            var pieces = Math.Max(1, (int)Math.Ceiling(length / CurrentStretchMeters));
            for (var k = 0; k < pieces; k++)
            {
                var from = PassageGeodesy.Interpolate(a, b, (double)k / pieces, leg.Geometry);
                var to = PassageGeodesy.Interpolate(a, b, (double)(k + 1) / pieces, leg.Geometry);
                var middle = PassageGeodesy.Interpolate(a, b, (k + 0.5) / pieces, leg.Geometry);
                var piece = length / pieces;
                var eta = start + TimeSpan.FromSeconds(seconds[^1]);
                var sog = stwKnots;
                if (currents.Locate(middle)?.Read(eta) is { } reading)
                {
                    sog = Math.Max(MinSpeedOverGroundKnots, stwKnots + reading.Current.AlongKnots(NavigableSnap.InitialBearingDegrees(from, to)));
                    if (!sources.Contains(reading.Source))
                    {
                        sources.Add(reading.Source);
                    }
                }
                else
                {
                    var startAlong = starts[p - 1] + (length * k / pieces);
                    gaps.Add(new PassageCurrentGap(legIndex, startAlong, startAlong + piece));
                }

                along.Add(starts[p - 1] + (length * (k + 1) / pieces));
                seconds.Add(seconds[^1] + (piece / (sog * PassageGeodesy.MetersPerNauticalMile / 3600)));
            }
        }

        return ([.. along], [.. seconds]);
    }

    // Joins gaps that touch on the same leg.
    private static List<PassageCurrentGap> Merge(List<PassageCurrentGap> gaps)
    {
        var merged = new List<PassageCurrentGap>();
        foreach (var gap in gaps)
        {
            if (merged.Count > 0 && merged[^1] is var last && last.LegIndex == gap.LegIndex && Math.Abs(last.EndAlongLegMeters - gap.StartAlongLegMeters) < 1e-6)
            {
                merged[^1] = last with { EndAlongLegMeters = gap.EndAlongLegMeters };
            }
            else
            {
                merged.Add(gap);
            }
        }

        return merged;
    }

    /// <summary>
    /// Computes the schedule the checks use for a plan: it leaves at the
    /// plan's departure, or at <see cref="RouteCheckSettings.At"/> when the
    /// plan has none.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="settings">The check settings.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException">A leg has fewer than two points, or a negative or non-finite speed.</exception>
    public static PassageSchedule Compute(PassagePlan plan, RouteCheckSettings settings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Compute(plan.Legs, plan.EffectiveDeparture(settings));
    }

    /// <summary>
    /// Computes the schedule the checks use for a plan, as
    /// <see cref="Compute(PassagePlan, RouteCheckSettings)"/>, with the
    /// current along the track added to the speeds when the settings say
    /// they're through the water (<see cref="RouteCheckSettings.SpeedsThroughWater"/>,
    /// or the timing check's <see cref="PassageTimingSettings.ApplyCurrents"/>)
    /// and the sources have currents.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="settings">The check settings.</param>
    /// <param name="sources">The loaded data, for <see cref="RouteCheckSources.Currents"/>.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException">A leg has fewer than two points, or a negative or non-finite speed.</exception>
    public static PassageSchedule Compute(PassagePlan plan, RouteCheckSettings settings, RouteCheckSources sources)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sources);
        return Compute(plan.Legs, plan.EffectiveDeparture(settings), settings.AppliesCurrents ? sources.Currents : null);
    }

    /// <summary>
    /// The time a check judges a point at: its ETA, or the departure where
    /// it has none (a leg without a speed, or after one).
    /// </summary>
    /// <param name="legIndex">Zero-based leg index.</param>
    /// <param name="distanceAlongLegMeters">Distance from the start of the leg, in metres.</param>
    /// <returns>The time.</returns>
    public DateTimeOffset TimeAt(int legIndex, double distanceAlongLegMeters) =>
        EtaAt(legIndex, distanceAlongLegMeters) ?? Departure;

    /// <summary>
    /// The ETA at a point <paramref name="distanceAlongLegMeters"/> into leg
    /// <paramref name="legIndex"/>, or <c>null</c> when it is unknown.
    /// </summary>
    /// <param name="legIndex">Zero-based leg index.</param>
    /// <param name="distanceAlongLegMeters">Distance from the start of the leg, in metres.</param>
    /// <returns>The ETA, or <c>null</c>.</returns>
    public DateTimeOffset? EtaAt(int legIndex, double distanceAlongLegMeters)
    {
        var leg = Legs[legIndex];
        if (leg.Start is not { } start || SpeedMetersPerSecond(Source[legIndex]) is not { } mps)
        {
            return null;
        }

        var along = Math.Clamp(distanceAlongLegMeters, 0, leg.DistanceMeters);
        if (_profiles[legIndex] is not var (distances, seconds))
        {
            return start + TimeSpan.FromSeconds(along / mps);
        }

        // Between the ends of two stretches, at that stretch's speed.
        var k = Array.BinarySearch(distances, along);
        if (k >= 0)
        {
            return start + TimeSpan.FromSeconds(seconds[k]);
        }

        var next = Math.Min(~k, distances.Length - 1);
        var prev = Math.Max(0, next - 1);
        var span = distances[next] - distances[prev];
        var t = span > 0 ? (along - distances[prev]) / span : 0;
        return start + TimeSpan.FromSeconds(seconds[prev] + ((seconds[next] - seconds[prev]) * t));
    }

    /// <summary>Distance in metres from the start of leg <paramref name="legIndex"/> to the start of its segment <paramref name="segmentIndex"/>.</summary>
    internal double SegmentStart(int legIndex, int segmentIndex) => _segmentStarts[legIndex][segmentIndex];

    internal static double? SpeedMetersPerSecond(PassageLeg leg) => leg.MetresPerSecond;
}
