using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Passage timing for a <see cref="TimingCheck"/> (#1561): whether the legs'
/// speeds are through the water, and the constraints each departure is
/// judged against.
/// </summary>
public sealed record PassageTimingSettings
{
    /// <summary>
    /// Whether the legs' speeds are speeds through the water: each stretch's
    /// speed over ground is then the speed plus the current along the track
    /// (#1033). Otherwise the speeds are over the ground, and currents are
    /// read only for gates and leg limits.
    /// </summary>
    public bool ApplyCurrents { get; init; }

    /// <summary>Tidal gates the passage must meet.</summary>
    public IReadOnlyList<TimingGate> Gates { get; init; } = [];

    /// <summary>Limits on the current against the track, per leg or for every leg.</summary>
    public IReadOnlyList<LegCurrentLimit> LegLimits { get; init; } = [];

    /// <summary>The latest arrival, or <c>null</c>.</summary>
    public DateTimeOffset? ArriveBy { get; init; }

    /// <summary>The earliest arrival, or <c>null</c>.</summary>
    public DateTimeOffset? ArriveNotBefore { get; init; }

    /// <summary>The water level range at the arrival waypoint at the ETA, or <c>null</c>.</summary>
    public ArrivalTideRange? ArrivalTide { get; init; }

    /// <summary>Whether any constraint is set.</summary>
    public bool HasConstraints =>
        Gates.Count > 0 || LegLimits.Count > 0 || ArriveBy is not null || ArriveNotBefore is not null || ArrivalTide is not null;
}

/// <summary>
/// A tidal gate: a place on the route where the current must be slack, weak
/// or fair at the ETA. Name a waypoint or a position; the current comes from
/// the named station, else from what covers the place.
/// </summary>
public sealed record TimingGate
{
    /// <summary>A name to report, or <c>null</c>.</summary>
    public string? Name { get; init; }

    /// <summary>The zero-based waypoint the gate is at, or <c>null</c> with <see cref="Position"/>.</summary>
    public int? WaypointIndex { get; init; }

    /// <summary>A position near the track the gate is at, or <c>null</c> with <see cref="WaypointIndex"/>.</summary>
    public GeoPosition? Position { get; init; }

    /// <summary>A current station id or name, or <c>null</c> for the current that covers the gate.</summary>
    public string? Station { get; init; }

    /// <summary>With a value, the ETA must be within this of a slack water at the station.</summary>
    public TimeSpan? SlackWithin { get; init; }

    /// <summary>With a value, the current at the ETA must be no stronger than this.</summary>
    public Speed? MaxCurrent { get; init; }

    /// <summary>Whether the current at the ETA must be fair: with the track, or slack.</summary>
    public bool FairOnly { get; init; }
}

/// <summary>A limit on the current against the track.</summary>
public sealed record LegCurrentLimit
{
    /// <summary>The zero-based leg, or <c>null</c> for every leg.</summary>
    public int? LegIndex { get; init; }

    /// <summary>The strongest current against the track allowed anywhere on the leg.</summary>
    public required Speed MaxOpposing { get; init; }
}

/// <summary>The water level range at the arrival waypoint, on the water level source's datum.</summary>
public sealed record ArrivalTideRange
{
    /// <summary>The lowest height, in metres, or <c>null</c>.</summary>
    public double? MinMeters { get; init; }

    /// <summary>The highest height, in metres, or <c>null</c>.</summary>
    public double? MaxMeters { get; init; }
}

/// <summary>Codes of the timing constraints in a <see cref="TimingConstraintResult"/>.</summary>
public static class TimingConstraintCodes
{
    /// <summary>A gate's ETA is (or isn't) within the slack window.</summary>
    public const string GateSlack = "gate.slack";

    /// <summary>A gate's current is (or isn't) within its limit.</summary>
    public const string GateMaxCurrent = "gate.max_current";

    /// <summary>A gate's current is (or isn't) fair.</summary>
    public const string GateFair = "gate.fair";

    /// <summary>A leg's current against the track is (or isn't) within its limit.</summary>
    public const string LegOpposingCurrent = "leg.opposing_current";

    /// <summary>The arrival is (or isn't) by the latest time.</summary>
    public const string ArriveBy = "arrival.by";

    /// <summary>The arrival is (or isn't) after the earliest time.</summary>
    public const string ArriveNotBefore = "arrival.not_before";

    /// <summary>The water level at the arrival is (or isn't) within the range.</summary>
    public const string ArrivalTide = "arrival.tide";
}

/// <summary>Why the timing check didn't run in full.</summary>
public static class TimingReasons
{
    /// <summary>No timing settings were given.</summary>
    public const string NotRequested = "timing_not_requested";

    /// <summary>A leg has no speed, so there are no ETAs to judge.</summary>
    public const string SpeedMissing = "speed_missing";

    /// <summary>Currents were needed (through-water speeds, a gate or a leg limit), but the sources have none.</summary>
    public const string CurrentsMissing = "currents_missing";
}

/// <summary>One constraint judged at one departure.</summary>
/// <param name="Code">The constraint (<see cref="TimingConstraintCodes"/>).</param>
/// <param name="Holds">Whether it holds.</param>
/// <param name="Detail">What was found, in words: why it fails, or the margin.</param>
/// <param name="Value">The value judged (knots, minutes or metres), or <c>null</c> when unknown.</param>
/// <param name="Limit">The limit it was judged against, or <c>null</c>.</param>
/// <param name="GateIndex">The gate, or <c>null</c>.</param>
/// <param name="LegIndex">The leg, or <c>null</c>.</param>
public sealed record TimingConstraintResult(string Code, bool Holds, string Detail, double? Value, double? Limit, int? GateIndex = null, int? LegIndex = null);

/// <summary>One leg's timing at one departure.</summary>
/// <param name="LegIndex">The zero-based leg.</param>
/// <param name="Start">The ETA at the leg's start.</param>
/// <param name="End">The ETA at the leg's end.</param>
/// <param name="RunningTime">The time to run the leg, with currents where applied.</param>
/// <param name="RunningTimeAtSpeed">The leg's distance over its speed, without currents.</param>
/// <param name="MeanAlongCurrentKnots">The mean current along the track (positive fair), or <c>null</c> without current data.</param>
/// <param name="MaxOpposingCurrentKnots">The strongest current against the track (0 when none is foul), or <c>null</c> without current data.</param>
/// <param name="StretchesWithoutCurrent">How many stretches had no current value.</param>
public sealed record TimingLeg(
    int LegIndex,
    DateTimeOffset Start,
    DateTimeOffset End,
    TimeSpan RunningTime,
    TimeSpan RunningTimeAtSpeed,
    double? MeanAlongCurrentKnots,
    double? MaxOpposingCurrentKnots,
    int StretchesWithoutCurrent);

/// <summary>A gate at one departure.</summary>
/// <param name="GateIndex">The gate's index in <see cref="PassageTimingSettings.Gates"/>.</param>
/// <param name="Name">Its name, or <c>null</c>.</param>
/// <param name="Position">Where on the track it was judged.</param>
/// <param name="Eta">The ETA there.</param>
/// <param name="Current">The current at the ETA, or <c>null</c> without data.</param>
/// <param name="AlongKnots">The current along the track (positive fair), or <c>null</c>.</param>
/// <param name="Source">What the current came from, or <c>null</c>.</param>
/// <param name="NearestSlack">The slack nearest the ETA, or <c>null</c>.</param>
public sealed record TimingGateResult(
    int GateIndex,
    string? Name,
    GeoPosition Position,
    DateTimeOffset Eta,
    RouteCurrent? Current,
    double? AlongKnots,
    RouteCurrentSourceInfo? Source,
    DateTimeOffset? NearestSlack);

/// <summary>The passage at one departure.</summary>
/// <param name="Departure">The departure.</param>
/// <param name="Arrival">The ETA at the last waypoint.</param>
/// <param name="RunningTime">The time under way.</param>
/// <param name="MeetsConstraints">Whether every constraint holds.</param>
/// <param name="Constraints">Each constraint as judged.</param>
/// <param name="Legs">Each leg's timing.</param>
/// <param name="Gates">Each gate as judged.</param>
public sealed record TimingDeparture(
    DateTimeOffset Departure,
    DateTimeOffset Arrival,
    TimeSpan RunningTime,
    bool MeetsConstraints,
    IReadOnlyList<TimingConstraintResult> Constraints,
    IReadOnlyList<TimingLeg> Legs,
    IReadOnlyList<TimingGateResult> Gates);

/// <summary>A run of consecutive departures that meet every constraint.</summary>
/// <param name="From">The first departure of the run.</param>
/// <param name="To">The last departure of the run.</param>
/// <param name="Best">The best departure in the run: nearest slack at the slack gates when there are any, else the shortest running time (the earliest of equals).</param>
/// <param name="BestRunningTime">Its running time.</param>
public sealed record TimingWindow(DateTimeOffset From, DateTimeOffset To, DateTimeOffset Best, TimeSpan BestRunningTime);

/// <summary>What the <see cref="TimingCheck"/> found.</summary>
/// <param name="Planned">The passage at the plan's departure.</param>
/// <param name="Departures">Each swept departure, or <c>null</c> without a sweep.</param>
/// <param name="Windows">The runs of departures that meet every constraint, in time order.</param>
/// <param name="Step">The time between swept departures, or <c>null</c> without a sweep.</param>
/// <param name="CurrentsApplied">Whether currents were applied to the ETAs.</param>
/// <param name="CurrentSources">The current datasets and stations read.</param>
public sealed record PassageTimingResult(
    TimingDeparture Planned,
    IReadOnlyList<TimingDeparture>? Departures,
    IReadOnlyList<TimingWindow> Windows,
    TimeSpan? Step,
    bool CurrentsApplied,
    IReadOnlyList<RouteCurrentSourceInfo> CurrentSources);

/// <summary>
/// The timing check (#1561): running times with currents applied where the
/// speeds are through the water (#1033), tidal gates, limits on the current
/// against each leg, and arrival constraints, at the plan's departure and,
/// with <see cref="RouteCheckSettings.DepartureWindow"/>, at every swept
/// departure, with the windows that meet them all.
/// </summary>
/// <remarks>
/// <para>
/// It runs only with <see cref="RouteCheckSettings.Timing"/>, and is skipped
/// otherwise. It needs a speed on every leg. Its ETAs are its own: the other
/// checks keep the speeds as over the ground.
/// </para>
/// <para>
/// A window that follows the water-level data (no fixed bounds) sweeps the
/// 24 hours from the plan's departure.
/// </para>
/// <para>It reports a result and no findings. It isn't for navigation.</para>
/// </remarks>
public sealed class TimingCheck : IRouteCheck<PassageTimingResult>
{
    /// <summary>The check's name in the report.</summary>
    public const string CheckName = "timing";

    /// <summary>The longest stretch one current reading stands for, in metres.</summary>
    public const double StretchMeters = 500;

    private const int MaxDepartures = 2000;
    private const double MinSpeedOverGroundKnots = 0.5;
    private const double KnotsToMetresPerSecond = 1852.0 / 3600.0;
    private static readonly TimeSpan SlackSearch = TimeSpan.FromHours(13);

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    RouteCheckOutcome IRouteCheck.Run(RouteCheckContext context, CancellationToken cancellationToken) => Run(context, cancellationToken);

    /// <inheritdoc/>
    public RouteCheckOutcome<PassageTimingResult> Run(RouteCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Settings.Timing is not { } timing)
        {
            return RouteCheckOutcome<PassageTimingResult>.Skipped(TimingReasons.NotRequested);
        }

        var legs = context.Plan.Legs;
        if (legs.Count == 0 || legs.Any(l => l.Speed is not { } s || s.TotalKnots <= 0))
        {
            return RouteCheckOutcome<PassageTimingResult>.Skipped(TimingReasons.SpeedMissing);
        }

        var currents = context.Sources.Currents;
        var needsCurrents = timing.ApplyCurrents || timing.LegLimits.Count > 0
            || timing.Gates.Any(g => g.SlackWithin is not null || g.MaxCurrent is not null || g.FairOnly);
        var stretches = Stretches(legs, currents);
        var gates = timing.Gates.Select((g, i) => Locate(g, i, stretches, legs, currents)).ToList();

        var departure = context.Schedule.Departure;
        var planned = Evaluate(departure, legs, stretches, gates, timing, currents is not null && timing.ApplyCurrents, context.Sources);
        List<TimingDeparture>? swept = null;
        TimeSpan? step = null;
        if (context.Settings.DepartureWindow is { } range)
        {
            var from = range.From ?? departure;
            var to = range.To ?? departure.AddHours(24);
            step = range.Step;
            while ((to - from).Ticks / step.Value.Ticks > MaxDepartures)
            {
                step = step.Value * 2;
            }

            swept = [];
            for (var t = from; t <= to; t += step.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                swept.Add(Evaluate(t, legs, stretches, gates, timing, currents is not null && timing.ApplyCurrents, context.Sources));
            }
        }

        var sources = stretches.Select(s => s.Series?.Source)
            .Concat(gates.Select(g => g.Series?.Source))
            .OfType<RouteCurrentSourceInfo>()
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Select(g => g.First() with { DistanceMeters = null })
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        var result = new PassageTimingResult(
            planned,
            swept,
            swept is null ? [] : Windows(swept),
            step,
            currents is not null && timing.ApplyCurrents,
            sources);
        return needsCurrents && currents is null
            ? RouteCheckOutcome<PassageTimingResult>.Partial(TimingReasons.CurrentsMissing, [], result)
            : RouteCheckOutcome<PassageTimingResult>.Ran([], result);
    }

    // One stretch of the track: where one current reading stands.
    private sealed record Stretch(int LegIndex, double StartAlongLeg, double Length, GeoPosition Middle, double CourseDegrees, IRouteCurrentSeries? Series);

    private sealed record LocatedGate(int Index, TimingGate Gate, int StretchIndex, GeoPosition Position, IRouteCurrentSeries? Series);

    private static List<Stretch> Stretches(IReadOnlyList<PassageLeg> legs, IRouteCurrentSource? currents)
    {
        var result = new List<Stretch>();
        for (var i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            double along = 0;
            for (var p = 1; p < leg.Points.Count; p++)
            {
                var a = leg.Points[p - 1];
                var b = leg.Points[p];
                var length = PassageGeodesy.DistanceMeters(a, b, leg.Geometry);
                var pieces = Math.Max(1, (int)Math.Ceiling(length / StretchMeters));
                for (var k = 0; k < pieces; k++)
                {
                    var start = PassageGeodesy.Interpolate(a, b, (double)k / pieces, leg.Geometry);
                    var end = PassageGeodesy.Interpolate(a, b, (double)(k + 1) / pieces, leg.Geometry);
                    var middle = PassageGeodesy.Interpolate(a, b, (k + 0.5) / pieces, leg.Geometry);
                    result.Add(new Stretch(i, along + (length * k / pieces), length / pieces, middle, Course(start, end), currents?.Locate(middle)));
                }

                along += length;
            }
        }

        return result;
    }

    private static LocatedGate Locate(TimingGate gate, int index, List<Stretch> stretches, IReadOnlyList<PassageLeg> legs, IRouteCurrentSource? currents)
    {
        int stretch;
        GeoPosition position;
        if (gate.WaypointIndex is { } waypoint)
        {
            // Waypoint k is the start of leg k, or the end of the last leg.
            if (waypoint >= legs.Count)
            {
                stretch = stretches.Count - 1;
                position = legs[^1].Points[^1];
            }
            else
            {
                stretch = stretches.FindIndex(s => s.LegIndex == waypoint);
                position = legs[waypoint].Points[0];
            }
        }
        else
        {
            var at = gate.Position ?? throw new ArgumentException("A gate needs a waypoint or a position.", nameof(gate));
            stretch = 0;
            var best = double.MaxValue;
            for (var i = 0; i < stretches.Count; i++)
            {
                var d = PlanarMeters(stretches[i].Middle, at);
                if (d < best)
                {
                    best = d;
                    stretch = i;
                }
            }

            position = stretches[stretch].Middle;
        }

        IRouteCurrentSeries? series = gate.Station is { } name
            ? currents?.FindStation(name)
            : currents?.Locate(gate.Position ?? position);
        return new LocatedGate(index, gate, stretch, position, series);
    }

    private static TimingDeparture Evaluate(
        DateTimeOffset departure,
        IReadOnlyList<PassageLeg> legs,
        List<Stretch> stretches,
        List<LocatedGate> gates,
        PassageTimingSettings timing,
        bool applyCurrents,
        RouteCheckSources sources)
    {
        // The ETA at the start of each stretch, then at the end of the last.
        var starts = new DateTimeOffset[stretches.Count + 1];
        var t = departure;
        var legStats = new (double AlongSum, double Weight, double? MaxOpposing, int Missing, double Distance)[legs.Count];
        for (var i = 0; i < stretches.Count; i++)
        {
            var s = stretches[i];
            starts[i] = t;
            var speed = legs[s.LegIndex].Speed!.Value.TotalKnots;
            var current = s.Series?.At(t);
            ref var stat = ref legStats[s.LegIndex];
            stat.Distance += s.Length;
            double sog = speed;
            if (current is { } c)
            {
                var along = c.AlongKnots(s.CourseDegrees);
                stat.AlongSum += along * s.Length;
                stat.Weight += s.Length;
                stat.MaxOpposing = Math.Max(stat.MaxOpposing ?? 0, -along);
                if (applyCurrents)
                {
                    sog = Math.Max(MinSpeedOverGroundKnots, speed + along);
                }
            }
            else
            {
                stat.Missing++;
            }

            t += TimeSpan.FromSeconds(s.Length / (sog * KnotsToMetresPerSecond));
        }

        starts[^1] = t;
        var timedLegs = new List<TimingLeg>(legs.Count);
        for (var l = 0; l < legs.Count; l++)
        {
            var first = stretches.FindIndex(s => s.LegIndex == l);
            var last = stretches.FindLastIndex(s => s.LegIndex == l);
            var stat = legStats[l];
            timedLegs.Add(new TimingLeg(
                l,
                starts[first],
                starts[last + 1],
                starts[last + 1] - starts[first],
                TimeSpan.FromSeconds(stat.Distance / (legs[l].Speed!.Value.TotalKnots * KnotsToMetresPerSecond)),
                stat.Weight > 0 ? Math.Round(stat.AlongSum / stat.Weight, 2) : null,
                stat.Weight > 0 ? Math.Round(stat.MaxOpposing ?? 0, 2) : null,
                stat.Missing));
        }

        var constraints = new List<TimingConstraintResult>();
        var gateResults = new List<TimingGateResult>(gates.Count);
        foreach (var gate in gates)
        {
            gateResults.Add(JudgeGate(gate, starts, stretches, constraints));
        }

        foreach (var limit in timing.LegLimits)
        {
            foreach (var leg in timedLegs.Where(l => limit.LegIndex is null || l.LegIndex == limit.LegIndex))
            {
                var max = limit.MaxOpposing.TotalKnots;
                constraints.Add(leg.MaxOpposingCurrentKnots is { } opposing
                    ? new TimingConstraintResult(
                        TimingConstraintCodes.LegOpposingCurrent,
                        opposing <= max,
                        opposing <= max
                            ? Invariant($"Strongest current against the track {opposing:0.0} kn, within {max:0.0} kn.")
                            : Invariant($"Current against the track reaches {opposing:0.0} kn, over {max:0.0} kn."),
                        opposing,
                        max,
                        LegIndex: leg.LegIndex)
                    // Unknown isn't a failure here: a limit for every leg would
                    // otherwise fail wherever no station reaches.
                    : new TimingConstraintResult(TimingConstraintCodes.LegOpposingCurrent, true, "No current data on the leg: not judged.", null, max, LegIndex: leg.LegIndex));
            }
        }

        var arrival = starts[^1];
        if (timing.ArriveBy is { } by)
        {
            var minutes = (by - arrival).TotalMinutes;
            constraints.Add(new TimingConstraintResult(
                TimingConstraintCodes.ArriveBy,
                minutes >= 0,
                minutes >= 0 ? Invariant($"Arrives {minutes:0} min before the latest arrival.") : Invariant($"Arrives {-minutes:0} min after the latest arrival."),
                Math.Round(minutes, 1),
                0));
        }

        if (timing.ArriveNotBefore is { } notBefore)
        {
            var minutes = (arrival - notBefore).TotalMinutes;
            constraints.Add(new TimingConstraintResult(
                TimingConstraintCodes.ArriveNotBefore,
                minutes >= 0,
                minutes >= 0 ? Invariant($"Arrives {minutes:0} min after the earliest arrival.") : Invariant($"Arrives {-minutes:0} min before the earliest arrival."),
                Math.Round(minutes, 1),
                0));
        }

        if (timing.ArrivalTide is { } tide)
        {
            constraints.Add(JudgeTide(tide, legs[^1].Points[^1], arrival, sources.WaterLevel));
        }

        return new TimingDeparture(departure, arrival, arrival - departure, constraints.All(c => c.Holds), constraints, timedLegs, gateResults);
    }

    private static TimingGateResult JudgeGate(LocatedGate located, DateTimeOffset[] starts, List<Stretch> stretches, List<TimingConstraintResult> constraints)
    {
        var gate = located.Gate;
        var stretch = stretches[located.StretchIndex];

        // A waypoint at the end of the route is judged at the arrival.
        var eta = gate.WaypointIndex is { } w && w > stretch.LegIndex
            ? starts[^1]
            : gate.WaypointIndex is not null
                ? starts[located.StretchIndex]
                : starts[located.StretchIndex] + ((starts[located.StretchIndex + 1] - starts[located.StretchIndex]) / 2);
        var current = located.Series?.At(eta);
        double? along = current?.AlongKnots(stretch.CourseDegrees);
        DateTimeOffset? nearestSlack = null;

        if (gate.SlackWithin is { } within)
        {
            if (located.Series is IRouteCurrentStation station)
            {
                var slacks = station.SlacksBetween(eta - SlackSearch, eta + SlackSearch);
                if (slacks.Count > 0)
                {
                    nearestSlack = slacks.MinBy(s => Math.Abs((s - eta).Ticks));
                    var minutes = (eta - nearestSlack.Value).TotalMinutes;
                    var holds = Math.Abs(minutes) <= within.TotalMinutes;
                    constraints.Add(new TimingConstraintResult(
                        TimingConstraintCodes.GateSlack,
                        holds,
                        minutes >= 0
                            ? Invariant($"ETA {minutes:0} min after slack at {Label(station.Source)}.")
                            : Invariant($"ETA {-minutes:0} min before slack at {Label(station.Source)}."),
                        Math.Round(minutes, 1),
                        within.TotalMinutes,
                        GateIndex: located.Index));
                }
                else
                {
                    constraints.Add(new TimingConstraintResult(TimingConstraintCodes.GateSlack, false, Invariant($"No slack water at {Label(station.Source)} near the ETA."), null, within.TotalMinutes, GateIndex: located.Index));
                }
            }
            else
            {
                constraints.Add(new TimingConstraintResult(TimingConstraintCodes.GateSlack, false, "No current station for the gate, so slack water is unknown.", null, within.TotalMinutes, GateIndex: located.Index));
            }
        }

        if (gate.MaxCurrent is { } max)
        {
            constraints.Add(current is { } c
                ? new TimingConstraintResult(
                    TimingConstraintCodes.GateMaxCurrent,
                    c.SpeedKnots <= max.TotalKnots,
                    Invariant($"Current {c.SpeedKnots:0.0} kn at the ETA, limit {max.TotalKnots:0.0} kn."),
                    Math.Round(c.SpeedKnots, 2),
                    max.TotalKnots,
                    GateIndex: located.Index)
                : new TimingConstraintResult(TimingConstraintCodes.GateMaxCurrent, false, "No current data at the gate.", null, max.TotalKnots, GateIndex: located.Index));
        }

        if (gate.FairOnly)
        {
            // Within a tenth of a knot of slack counts as fair.
            constraints.Add(along is { } a
                ? new TimingConstraintResult(
                    TimingConstraintCodes.GateFair,
                    a >= -0.1,
                    a >= -0.1 ? Invariant($"Current {a:0.0} kn with the track at the ETA.") : Invariant($"Current {-a:0.0} kn against the track at the ETA."),
                    Math.Round(a, 2),
                    0,
                    GateIndex: located.Index)
                : new TimingConstraintResult(TimingConstraintCodes.GateFair, false, "No current data at the gate.", null, 0, GateIndex: located.Index));
        }

        return new TimingGateResult(
            located.Index,
            gate.Name,
            located.Position,
            eta,
            current,
            along is { } x ? Math.Round(x, 2) : null,
            located.Series?.Source,
            nearestSlack);
    }

    private static TimingConstraintResult JudgeTide(ArrivalTideRange range, GeoPosition position, DateTimeOffset arrival, IWaterLevelSource? waterLevel)
    {
        var utc = arrival.UtcDateTime;
        WaterLevelReading? reading = null;
        foreach (var series in waterLevel?.Locate(position) ?? [])
        {
            if (series.ValueAt(utc) is { } r)
            {
                reading = r;
                break;
            }
        }

        var limit = range.MinMeters ?? range.MaxMeters;
        if (reading is not { } value)
        {
            return new TimingConstraintResult(TimingConstraintCodes.ArrivalTide, false, "No water level at the arrival waypoint at the ETA.", null, limit);
        }

        var h = value.HeightMeters;
        var low = range.MinMeters is { } min && h < min;
        var high = range.MaxMeters is { } maxH && h > maxH;
        return new TimingConstraintResult(
            TimingConstraintCodes.ArrivalTide,
            !low && !high,
            low ? Invariant($"Water level {h:0.00} m at arrival, below {range.MinMeters:0.00} m.")
                : high ? Invariant($"Water level {h:0.00} m at arrival, above {range.MaxMeters:0.00} m.")
                : Invariant($"Water level {h:0.00} m at arrival, within the range."),
            Math.Round(h, 2),
            low ? range.MinMeters : high ? range.MaxMeters : limit);
    }

    private static List<TimingWindow> Windows(List<TimingDeparture> departures)
    {
        var result = new List<TimingWindow>();
        var i = 0;
        while (i < departures.Count)
        {
            if (!departures[i].MeetsConstraints)
            {
                i++;
                continue;
            }

            var start = i;
            var best = departures[i];
            while (i < departures.Count && departures[i].MeetsConstraints)
            {
                if (Score(departures[i]) < Score(best))
                {
                    best = departures[i];
                }

                i++;
            }

            result.Add(new TimingWindow(departures[start].Departure, departures[i - 1].Departure, best.Departure, best.RunningTime));
        }

        return result;
    }

    // With slack gates, the departure nearest slack at them is best; else
    // the shortest running time. Earlier wins a tie (strictly less above).
    private static double Score(TimingDeparture departure)
    {
        var slack = departure.Constraints.Where(c => c.Code == TimingConstraintCodes.GateSlack && c.Value is not null).ToList();
        return slack.Count > 0 ? slack.Sum(c => Math.Abs(c.Value!.Value)) : departure.RunningTime.TotalMinutes;
    }

    private static string Label(RouteCurrentSourceInfo source) =>
        source.Name is { Length: > 0 } name ? $"{name} ({source.Id})" : source.Id;

    // Short stretches: a plane scaled by the latitude is close enough.
    private static double Course(GeoPosition from, GeoPosition to)
    {
        var north = to.Latitude - from.Latitude;
        var east = (to.Longitude - from.Longitude) * Math.Cos(from.Latitude * Math.PI / 180);
        var degrees = Math.Atan2(east, north) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double PlanarMeters(GeoPosition a, GeoPosition b)
    {
        var north = (b.Latitude - a.Latitude) * PassageGeodesy.MetersPerDegreeLatitude;
        var east = (b.Longitude - a.Longitude) * PassageGeodesy.MetersPerDegreeLatitude * Math.Cos(a.Latitude * Math.PI / 180);
        return Math.Sqrt((north * north) + (east * east));
    }

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
