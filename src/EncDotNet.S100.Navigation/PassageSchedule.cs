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

/// <summary>
/// Distances, durations and ETAs for a passage at a constant speed over
/// ground on each leg. Build one with
/// <see cref="Compute(PassagePlan, RouteCheckSettings)"/>.
/// </summary>
/// <remarks>
/// ETAs are departure plus distance divided by the entered speed; S-111
/// currents are not applied (handoff decision 4). A leg with no speed has
/// no duration, and the ETAs from its start onward are unknown.
/// </remarks>
public sealed class PassageSchedule
{
    private readonly double[][] _segmentStarts;

    private PassageSchedule(
        DateTimeOffset departure,
        IReadOnlyList<LegSchedule> legs,
        double[][] segmentStarts,
        IReadOnlyList<PassageLeg> source)
    {
        Departure = departure;
        Legs = legs;
        _segmentStarts = segmentStarts;
        Source = source;
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

    /// <summary>Computes the schedule for <paramref name="legs"/> leaving at <paramref name="departure"/>.</summary>
    /// <param name="legs">The legs in route order.</param>
    /// <param name="departure">The departure time.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentException">A leg has fewer than two points, or a negative or non-finite speed.</exception>
    public static PassageSchedule Compute(IReadOnlyList<PassageLeg> legs, DateTimeOffset departure)
    {
        ArgumentNullException.ThrowIfNull(legs);

        var result = new List<LegSchedule>(legs.Count);
        var segmentStarts = new double[legs.Count][];
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
            var start = clock;
            clock = clock is { } c && duration is { } d ? c + d : null;
            result.Add(new LegSchedule(i, distance, legLength, duration, start, clock));
            distance += legLength;
        }

        return new PassageSchedule(departure, result, segmentStarts, legs);
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

        return start + TimeSpan.FromSeconds(Math.Clamp(distanceAlongLegMeters, 0, leg.DistanceMeters) / mps);
    }

    /// <summary>Distance in metres from the start of leg <paramref name="legIndex"/> to the start of its segment <paramref name="segmentIndex"/>.</summary>
    internal double SegmentStart(int legIndex, int segmentIndex) => _segmentStarts[legIndex][segmentIndex];

    internal static double? SpeedMetersPerSecond(PassageLeg leg) => leg.MetresPerSecond;
}
