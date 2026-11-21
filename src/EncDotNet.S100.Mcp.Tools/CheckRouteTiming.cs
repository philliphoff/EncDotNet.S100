using System.ComponentModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>The departure-window constraints of a <see cref="DepartureWindowArguments"/> (#1561).</summary>
/// <param name="ApplyCurrents">Whether the speeds are through the water, so the timing check applies currents to its ETAs.</param>
/// <param name="Gates">Tidal gates the passage must meet.</param>
/// <param name="LegLimits">Limits on the current against the track.</param>
/// <param name="ArriveBy">The latest arrival.</param>
/// <param name="ArriveNotBefore">The earliest arrival.</param>
/// <param name="ArrivalTide">The water level range at the arrival waypoint.</param>
public sealed record WindowConstraintsArguments(
    [property: Description("Speeds are through the water: the current along the track is added to them for the ETAs and running times, in this check and every other (the same as vessel.speedThroughWater). Needs S-111 or current predictions (s100 mcp serve --tide-predictions).")] bool ApplyCurrents = false,
    [property: Description("Tidal gates: [{name?, waypoint | latitude+longitude, station?, slackWithinMinutes?, maxCurrentKnots?, fairOnly?}]. waypoint is zero-based; a position is snapped to the track. The current comes from the named current station (id or name, as predict_current takes), else from the nearest station that reaches the gate. slackWithinMinutes needs a station.")] IReadOnlyList<GateArguments>? Gates = null,
    [property: Description("Limits on the current against the track: [{leg?, maxOpposingKnots}]; leg is zero-based, omitted for every leg.")] IReadOnlyList<LegLimitArguments>? LegLimits = null,
    [property: Description("ISO 8601 latest arrival, such as daylight or marina office hours.")] DateTimeOffset? ArriveBy = null,
    [property: Description("ISO 8601 earliest arrival.")] DateTimeOffset? ArriveNotBefore = null,
    [property: Description("Water level range at the arrival waypoint at the ETA, in metres on the water level's datum (S-104, else tide predictions): {minMeters?, maxMeters?}.")] ArrivalTideArguments? ArrivalTide = null);

/// <summary>A tidal gate in <see cref="WindowConstraintsArguments"/>.</summary>
/// <param name="Name">A name to report.</param>
/// <param name="Waypoint">The zero-based waypoint the gate is at.</param>
/// <param name="Latitude">With <paramref name="Longitude"/>, a position near the track.</param>
/// <param name="Longitude">With <paramref name="Latitude"/>, a position near the track.</param>
/// <param name="Station">A current station id or name.</param>
/// <param name="SlackWithinMinutes">The ETA must be within this many minutes of slack water.</param>
/// <param name="MaxCurrentKnots">The current at the ETA must be no stronger.</param>
/// <param name="FairOnly">The current at the ETA must be with the track, or slack.</param>
public sealed record GateArguments(
    [property: Description("A name to report.")] string? Name = null,
    [property: Description("The zero-based waypoint the gate is at.")] int? Waypoint = null,
    [property: Description("With longitude, a position near the track.")] double? Latitude = null,
    [property: Description("With latitude, a position near the track.")] double? Longitude = null,
    [property: Description("A current station id or name.")] string? Station = null,
    [property: Description("The ETA must be within this many minutes of slack water at the station.")] double? SlackWithinMinutes = null,
    [property: Description("The current at the ETA must be no stronger than this, in knots.")] double? MaxCurrentKnots = null,
    [property: Description("The current at the ETA must be with the track (or slack).")] bool FairOnly = false);

/// <summary>A limit on the current against the track in <see cref="WindowConstraintsArguments"/>.</summary>
/// <param name="Leg">The zero-based leg, or <c>null</c> for every leg.</param>
/// <param name="MaxOpposingKnots">The strongest current against the track allowed.</param>
public sealed record LegLimitArguments(
    [property: Description("The zero-based leg; omit for every leg.")] int? Leg = null,
    [property: Description("The strongest current against the track allowed, in knots.")] double MaxOpposingKnots = 0);

/// <summary>A water level range at the arrival in <see cref="WindowConstraintsArguments"/>.</summary>
/// <param name="MinMeters">The lowest height.</param>
/// <param name="MaxMeters">The highest height.</param>
public sealed record ArrivalTideArguments(
    [property: Description("The lowest height, in metres.")] double? MinMeters = null,
    [property: Description("The highest height, in metres.")] double? MaxMeters = null);

/// <summary>The timing check in a <see cref="CheckRouteResult"/> (#1561).</summary>
/// <param name="CurrentsApplied">Whether currents were applied to the ETAs.</param>
/// <param name="CurrentSources">The current stations read.</param>
/// <param name="Planned">The passage at the planned departure, in full.</param>
/// <param name="Windows">The runs of swept departures that meet every constraint.</param>
/// <param name="StepMinutes">Minutes between swept departures, or <c>null</c> without a sweep.</param>
/// <param name="Departures">Each swept departure, briefly, or <c>null</c> without a sweep.</param>
/// <param name="Caveat">What to quote with the times.</param>
public sealed record CheckRouteTiming(
    [property: Description("Whether currents were applied to the ETAs (constraints.applyCurrents with current data).")] bool CurrentsApplied,
    [property: Description("The current stations read: id, name and origin (prediction).")] IReadOnlyList<CheckRouteCurrentSource> CurrentSources,
    [property: Description("The passage at the planned departure: arrival, running time, each leg's running time and currents, each gate and each constraint.")] CheckRouteTimingDeparture Planned,
    [property: Description("Runs of consecutive swept departures that meet every constraint, each with its best departure: nearest slack at the slack gates when there are any, else the shortest running time (the earliest of equals). Empty when none does, or without a sweep.")] IReadOnlyList<CheckRouteTimingWindow> Windows,
    [property: Description("Minutes between swept departures; null without a sweep.")] double? StepMinutes,
    [property: Description("Each swept departure: its arrival, running time, whether it meets every constraint, and the codes of those that fail. Null without a sweep.")] IReadOnlyList<CheckRouteTimingRow>? Departures,
    [property: Description("Quote with the times.")] string Caveat)
{
    /// <summary>The caveat quoted with the times.</summary>
    public const string CaveatText = "Predicted astronomical tidal currents only: wind and river flow change real currents, and the strongest stream in a pass can be away from the station. Not for navigation.";

    /// <summary>Projects the timing check's result.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The projection.</returns>
    public static CheckRouteTiming From(PassageTimingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new CheckRouteTiming(
            result.CurrentsApplied,
            [.. result.CurrentSources.Select(s => new CheckRouteCurrentSource(s.Id, s.Name, s.Origin == RouteCurrentOrigin.S111 ? "s111" : "prediction"))],
            CheckRouteTimingDeparture.From(result.Planned),
            [.. result.Windows.Select(w => new CheckRouteTimingWindow(w.From, w.To, w.Best, Minutes(w.BestRunningTime)))],
            result.Step is { } step ? step.TotalMinutes : null,
            result.Departures is { } departures
                ? [.. departures.Select(d => new CheckRouteTimingRow(
                    d.Departure,
                    d.Arrival,
                    Minutes(d.RunningTime),
                    d.MeetsConstraints,
                    [.. d.Constraints.Where(c => !c.Holds).Select(c => c.Code).Distinct()]))]
                : null,
            CaveatText);
    }

    internal static double Minutes(TimeSpan span) => Math.Round(span.TotalMinutes, 1);
}

/// <summary>A current source the timing check read.</summary>
/// <param name="Id">The station or dataset id.</param>
/// <param name="Name">The name, or <c>null</c>.</param>
/// <param name="Origin"><c>prediction</c> or <c>s111</c>.</param>
public sealed record CheckRouteCurrentSource(
    [property: Description("The station or dataset id.")] string Id,
    [property: Description("The name, or null.")] string? Name,
    [property: Description("prediction (a current station's harmonic prediction) or s111.")] string Origin);

/// <summary>The passage at one departure, in full.</summary>
/// <param name="Departure">The departure.</param>
/// <param name="Arrival">The arrival.</param>
/// <param name="RunningMinutes">The running time in minutes.</param>
/// <param name="MeetsConstraints">Whether every constraint holds.</param>
/// <param name="Legs">Each leg's timing.</param>
/// <param name="Gates">Each gate as judged.</param>
/// <param name="Constraints">Each constraint as judged.</param>
public sealed record CheckRouteTimingDeparture(
    [property: Description("The departure.")] DateTimeOffset Departure,
    [property: Description("The ETA at the last waypoint.")] DateTimeOffset Arrival,
    [property: Description("The running time in minutes.")] double RunningMinutes,
    [property: Description("Whether every constraint holds.")] bool MeetsConstraints,
    [property: Description("Each leg: start and end ETA, running minutes with currents (where applied) and at the speed alone, the mean current along the track (positive fair) and the strongest against it, in knots, and how many 500 m stretches had no current value.")] IReadOnlyList<CheckRouteTimingLeg> Legs,
    [property: Description("Each gate: where it was judged, the ETA, the current (knots, set toward) and its part along the track, the station, and the nearest slack.")] IReadOnlyList<CheckRouteTimingGate> Gates,
    [property: Description("Each constraint: code (gate.slack, gate.max_current, gate.fair, leg.opposing_current, arrival.by, arrival.not_before, arrival.tide), holds, detail, value and limit (knots, minutes or metres), and the gate or leg.")] IReadOnlyList<CheckRouteTimingConstraint> Constraints)
{
    /// <summary>Projects one departure.</summary>
    /// <param name="d">The departure.</param>
    /// <returns>The projection.</returns>
    public static CheckRouteTimingDeparture From(TimingDeparture d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new CheckRouteTimingDeparture(
            d.Departure,
            d.Arrival,
            CheckRouteTiming.Minutes(d.RunningTime),
            d.MeetsConstraints,
            [.. d.Legs.Select(l => new CheckRouteTimingLeg(
                l.LegIndex, l.Start, l.End, CheckRouteTiming.Minutes(l.RunningTime), CheckRouteTiming.Minutes(l.RunningTimeAtSpeed),
                l.MeanAlongCurrentKnots, l.MaxOpposingCurrentKnots, l.StretchesWithoutCurrent))],
            [.. d.Gates.Select(g => new CheckRouteTimingGate(
                g.GateIndex, g.Name, g.Position.Latitude, g.Position.Longitude, g.Eta,
                g.Current is { } c ? Math.Round(c.SpeedKnots, 2) : null,
                g.Current is { } s ? Math.Round(s.SetDegrees, 0) : null,
                g.AlongKnots, g.Source?.Id, g.NearestSlack))],
            [.. d.Constraints.Select(c => new CheckRouteTimingConstraint(c.Code, c.Holds, c.Detail, c.Value, c.Limit, c.GateIndex, c.LegIndex))]);
    }
}

/// <summary>One leg's timing.</summary>
public sealed record CheckRouteTimingLeg(
    [property: Description("Zero-based leg.")] int Index,
    [property: Description("ETA at the leg's start.")] DateTimeOffset Start,
    [property: Description("ETA at the leg's end.")] DateTimeOffset End,
    [property: Description("Running time in minutes, with currents where applied.")] double RunningMinutes,
    [property: Description("Distance over speed, in minutes, without currents.")] double RunningMinutesAtSpeed,
    [property: Description("Mean current along the track in knots, positive fair; null without current data.")] double? MeanAlongCurrentKnots,
    [property: Description("Strongest current against the track in knots (0 when none is foul); null without current data.")] double? MaxOpposingCurrentKnots,
    [property: Description("500 m stretches with no current value.")] int StretchesWithoutCurrent);

/// <summary>A gate at one departure.</summary>
public sealed record CheckRouteTimingGate(
    [property: Description("Index in constraints.gates.")] int Index,
    [property: Description("The gate's name, or null.")] string? Name,
    [property: Description("Latitude on the track where it was judged.")] double Latitude,
    [property: Description("Longitude on the track where it was judged.")] double Longitude,
    [property: Description("The ETA there.")] DateTimeOffset Eta,
    [property: Description("Current speed at the ETA in knots, or null without data.")] double? CurrentKnots,
    [property: Description("Direction the current sets toward, degrees true, or null.")] double? SetDegrees,
    [property: Description("Current along the track in knots, positive fair, or null.")] double? AlongKnots,
    [property: Description("The current station, or null.")] string? StationId,
    [property: Description("The slack nearest the ETA, or null.")] DateTimeOffset? NearestSlack);

/// <summary>A constraint at one departure.</summary>
public sealed record CheckRouteTimingConstraint(
    [property: Description("gate.slack, gate.max_current, gate.fair, leg.opposing_current, arrival.by, arrival.not_before or arrival.tide.")] string Code,
    [property: Description("Whether it holds.")] bool Holds,
    [property: Description("What was found, in words.")] string Detail,
    [property: Description("The value judged (knots, minutes from slack or the limit time, or metres), or null when unknown.")] double? Value,
    [property: Description("The limit, or null.")] double? Limit,
    [property: Description("The gate's index, or null.")] int? Gate,
    [property: Description("The leg's index, or null.")] int? Leg);

/// <summary>A run of departures that meet every constraint.</summary>
public sealed record CheckRouteTimingWindow(
    [property: Description("The first departure of the run.")] DateTimeOffset From,
    [property: Description("The last departure of the run.")] DateTimeOffset To,
    [property: Description("The best departure: nearest slack at the slack gates, else the shortest running time.")] DateTimeOffset Best,
    [property: Description("The best departure's running time in minutes.")] double BestRunningMinutes);

/// <summary>One swept departure, briefly.</summary>
public sealed record CheckRouteTimingRow(
    [property: Description("The departure.")] DateTimeOffset Departure,
    [property: Description("The arrival.")] DateTimeOffset Arrival,
    [property: Description("Running time in minutes.")] double RunningMinutes,
    [property: Description("Whether every constraint holds.")] bool MeetsConstraints,
    [property: Description("Codes of the constraints that fail.")] IReadOnlyList<string> Failing);
