using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>How a warning's geometry relates to a route.</summary>
public enum RouteWarningRelation
{
    /// <summary>A line or area of the warning reaches the corridor between the cross-track limits.</summary>
    Crosses,

    /// <summary>
    /// The warning is within <see cref="RouteWarningOptions.NearDistanceMeters"/>
    /// of the track, or is a point inside the corridor (a position can't be crossed).
    /// </summary>
    Near,

    /// <summary>Neither.</summary>
    Off,
}

/// <summary>Whether a warning applies while the vessel is at it.</summary>
public enum RouteWarningStatus
{
    /// <summary>A validity range overlaps the vessel's time there, and the warning isn't cancelled by then.</summary>
    InForce,

    /// <summary>No validity range overlaps the vessel's time there.</summary>
    NotInForce,

    /// <summary>
    /// Its cancellation date, a warning that cancels it, or an in-force
    /// bulletin that leaves it out comes before the vessel gets there
    /// (see <see cref="WarningDetails.Withdrawal"/> for which).
    /// </summary>
    Cancelled,
}

/// <summary>
/// One navigational warning part to check a route against.
/// </summary>
/// <param name="Info">The part's message: identification, validity, cancellation and text.</param>
/// <param name="Shapes">
/// The part's geometry and the areas it affects (<see cref="NavigationalWarningInfo.AffectedAreas"/>),
/// in WGS-84. Points, curves and surfaces are each used; a part with none is never on the route.
/// </param>
/// <param name="Feature">The feature behind it, which the finding refers to, or <c>null</c>.</param>
public sealed record RouteWarning(
    NavigationalWarningInfo Info,
    IReadOnlyList<S100FeatureGeometry> Shapes,
    RouteFindingFeature? Feature = null);

/// <summary>One warning against the route, found or not.</summary>
/// <param name="Warning">The warning.</param>
/// <param name="Relation">How it relates to the route.</param>
/// <param name="LegIndex">The leg where the route first meets it, or <c>null</c> when off the route.</param>
/// <param name="StartDistanceMeters">Distance from departure to where the route first meets it (or passes closest); <c>NaN</c> when off.</param>
/// <param name="EndDistanceMeters">Distance from departure to where the route leaves it; <c>NaN</c> when off.</param>
/// <param name="ClosestMeters">The least distance between the track and the warning, 0 when the track crosses it.</param>
/// <param name="Finding">The finding, or <c>null</c> when off the route.</param>
public sealed record RouteWarningMatch(
    RouteWarning Warning,
    RouteWarningRelation Relation,
    int? LegIndex,
    double StartDistanceMeters,
    double EndDistanceMeters,
    double ClosestMeters,
    RouteFinding? Finding);

/// <summary>The result of a <see cref="RouteWarningCheck"/> check.</summary>
/// <param name="Matches">One entry per warning, in input order.</param>
/// <param name="Findings">The findings, sorted by group (MUST REVIEW, CHECK, INFO) and then by distance from departure.</param>
public sealed record RouteWarningResult(
    IReadOnlyList<RouteWarningMatch> Matches,
    IReadOnlyList<RouteFinding> Findings);

/// <summary>Tuning for <see cref="RouteWarningCheck"/>.</summary>
public sealed record RouteWarningOptions
{
    /// <summary>The default options.</summary>
    public static RouteWarningOptions Default { get; } = new();

    /// <summary>
    /// How close to the track a warning counts as near the route, in metres.
    /// Default 0,5 NM (926 m).
    /// </summary>
    public double NearDistanceMeters { get; init; } = 0.5 * PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// The corridor's half-width on a side whose cross-track limit isn't set,
    /// in metres, as <see cref="RouteCheckOptions.DefaultHalfWidthMeters"/>. Default 50 m.
    /// </summary>
    public double DefaultHalfWidthMeters { get; init; } = 50;

    /// <summary>
    /// The length of the pieces a leg is cut into, in metres. Crossings are
    /// found exactly within a piece; it bounds how far a planar piece departs
    /// from the leg's rhumb line or great circle. Default 50 m.
    /// </summary>
    public double SegmentLengthMeters { get; init; } = 50;

    /// <summary>The most pieces per leg; a longer leg uses longer pieces. Default 20 000.</summary>
    public int MaxSegmentsPerLeg { get; init; } = 20_000;

    /// <summary>Renders the findings' titles and meta lines. Hosts with their own resources pass their own.</summary>
    public RouteFindingFormatter Formatter { get; init; } = RouteFindingFormatter.Default;
}

/// <summary>
/// Checks a route against navigational warnings (S-124 Editions 1.0.0 and
/// 2.0.0, #957): which cross its corridor or come near it, and whether each
/// is in force while the vessel is there.
/// </summary>
/// <remarks>
/// <para>
/// Each leg is cut into short pieces; within a piece the track, the
/// warning's lines and areas and the corridor are compared in a local plane,
/// so crossings are exact to the piece's straightness. A line or area
/// <see cref="RouteWarningRelation.Crosses"/> when it touches the track or
/// comes within the leg's cross-track limit on its side. A warning is
/// <see cref="RouteWarningRelation.Near"/> when it comes within
/// <see cref="RouteWarningOptions.NearDistanceMeters"/> of the track; a
/// point inside the corridor is near too, since a position can't be crossed.
/// </para>
/// <para>
/// The time is the ETA over the stretch where the vessel meets the warning
/// when the legs have speeds, otherwise the departure (<see cref="PassageSchedule.TimeAt"/>).
/// A warning is in force when a validity range overlaps that time (no range:
/// in force until cancelled). It is cancelled when
/// <see cref="NavigationalWarningSet.WithdrawnAt"/> withdraws it by the time
/// the vessel gets there: its cancellation date (Edition 2.0.0), another
/// warning that cancels it and was published by then (one with no
/// publication time, as in Edition 1.0.0, cancels at once), or the latest
/// in-force bulletin for its series leaving it out (§9.2.1). The Warning
/// card and the Navigational warnings panel judge with the same set.
/// </para>
/// <para>
/// Severity (#958 handoff, decision 10): crosses and in force → MUST REVIEW;
/// near and in force → CHECK; on or near the route but not in force, or
/// cancelled → INFO with the reason. Warnings off the route give no finding.
/// </para>
/// <para>This is an aid for exploring the data. It isn't for navigation.</para>
/// </remarks>
public sealed class RouteWarningCheck
{
    private readonly RouteWarningOptions _options;

    /// <summary>Creates the check.</summary>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteWarningOptions.Default"/>.</param>
    public RouteWarningCheck(RouteWarningOptions? options = null)
    {
        _options = options ?? RouteWarningOptions.Default;
        if (!(_options.SegmentLengthMeters > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SegmentLengthMeters must be positive.");
        }

        if (_options.MaxSegmentsPerLeg < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSegmentsPerLeg must be at least 1.");
        }

        if (!(_options.NearDistanceMeters >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "NearDistanceMeters must not be negative.");
        }
    }

    /// <summary>Checks a plan against the warnings a source has near it, judged with its <see cref="IRouteWarningSource.Set"/>.</summary>
    /// <param name="plan">The plan: its legs with their cross-track limits and speeds, and its departure.</param>
    /// <param name="settings">The corridor override and the time to judge at where the plan gives none.</param>
    /// <param name="source">The warnings.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>One match per warning near the route, and the findings.</returns>
    public RouteWarningResult Check(PassagePlan plan, RouteCheckSettings settings, IRouteWarningSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(source);
        var track = BuildTrack(plan, settings);
        var warnings = new List<RouteWarning>();
        var seen = new HashSet<object>();
        foreach (var extent in track.LegExtents(Reach(track)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var warning in source.WarningsNear(extent))
            {
                if (seen.Add((object?)warning.Feature ?? warning))
                {
                    warnings.Add(warning);
                }
            }
        }

        return Check(track, PassageSchedule.Compute(plan, settings), warnings, source.Set, cancellationToken);
    }

    /// <summary>Checks a plan against warnings, resolving cancellations and in-force bulletins among them.</summary>
    /// <param name="plan">The plan: its legs with their cross-track limits and speeds, and its departure.</param>
    /// <param name="settings">The corridor override and the time to judge at where the plan gives none.</param>
    /// <param name="warnings">The warning parts.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>One match per warning, and the findings.</returns>
    public RouteWarningResult Check(
        PassagePlan plan,
        RouteCheckSettings settings,
        IReadOnlyList<RouteWarning> warnings,
        CancellationToken cancellationToken = default) =>
        Check(plan, settings, warnings, null, cancellationToken);

    /// <summary>Checks a plan against warnings, judged with every loaded warning.</summary>
    /// <param name="plan">The plan: its legs with their cross-track limits and speeds, and its departure.</param>
    /// <param name="settings">The corridor override and the time to judge at where the plan gives none.</param>
    /// <param name="warnings">The warning parts to check.</param>
    /// <param name="set">
    /// Every loaded warning, including cancellations and in-force bulletins
    /// with no parts, which may end the checked ones; <c>null</c> builds it
    /// from <paramref name="warnings"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>One match per warning, and the findings.</returns>
    public RouteWarningResult Check(
        PassagePlan plan,
        RouteCheckSettings settings,
        IReadOnlyList<RouteWarning> warnings,
        NavigationalWarningSet? set,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(warnings);
        return Check(BuildTrack(plan, settings), PassageSchedule.Compute(plan, settings), warnings, set, cancellationToken);
    }

    private RouteCorridor.Track BuildTrack(PassagePlan plan, RouteCheckSettings settings) =>
        RouteCorridor.Track.Build(plan, settings, _options.DefaultHalfWidthMeters, _options.SegmentLengthMeters, _options.MaxSegmentsPerLeg);

    // How far from the track a warning can be and still be on or near the route.
    private double Reach(RouteCorridor.Track track) => Math.Max(_options.NearDistanceMeters, track.MaxHalfWidth);

    private RouteWarningResult Check(
        RouteCorridor.Track track,
        PassageSchedule schedule,
        IReadOnlyList<RouteWarning> warnings,
        NavigationalWarningSet? set,
        CancellationToken cancellationToken)
    {
        var shapes = warnings
            .Select(w => new RouteCorridor.Shapes((w ?? throw new ArgumentException("A warning is null.", nameof(warnings))).Shapes))
            .ToList();
        var accumulators = RouteCorridor.Measure(track, shapes, Reach(track), cancellationToken);
        set ??= new NavigationalWarningSet(warnings.Select(w => w.Info));

        var matches = new List<RouteWarningMatch>(warnings.Count);
        var findings = new List<RouteFinding>();
        for (var w = 0; w < warnings.Count; w++)
        {
            var match = Match(warnings, w, shapes[w], accumulators[w], track, schedule, set);
            matches.Add(match);
            if (match.Finding is { } finding)
            {
                findings.Add(finding);
            }
        }

        return new RouteWarningResult(matches, RouteFinding.Sort(findings));
    }

    /// <summary>
    /// Whether a warning is in force over a time span, given every loaded
    /// warning, which may cancel it or leave it out of an in-force bulletin.
    /// </summary>
    /// <param name="warning">The warning part.</param>
    /// <param name="set">Every loaded warning.</param>
    /// <param name="from">When the vessel reaches it; withdrawals are judged then.</param>
    /// <param name="until">When the vessel leaves it.</param>
    /// <returns>The status, and the withdrawal when it is <see cref="RouteWarningStatus.Cancelled"/>.</returns>
    public static (RouteWarningStatus Status, NavigationalWarningState? Withdrawal) StatusOver(
        NavigationalWarningInfo warning, NavigationalWarningSet set, DateTimeOffset from, DateTimeOffset until)
    {
        ArgumentNullException.ThrowIfNull(warning);
        ArgumentNullException.ThrowIfNull(set);
        if (set.WithdrawnAt(warning, from) is { } withdrawn)
        {
            return (RouteWarningStatus.Cancelled, withdrawn);
        }

        if (warning.Validity.Count == 0)
        {
            return (RouteWarningStatus.InForce, null);
        }

        return warning.Validity.Any(r => (r.Start is null || r.Start <= until) && (r.End is null || r.End >= from))
            ? (RouteWarningStatus.InForce, null)
            : (RouteWarningStatus.NotInForce, null);
    }

    private RouteWarningMatch Match(IReadOnlyList<RouteWarning> warnings, int index, RouteCorridor.Shapes shapes, RouteCorridor.Accumulator acc, RouteCorridor.Track track, PassageSchedule schedule, NavigationalWarningSet set)
    {
        var warning = warnings[index];
        RouteWarningRelation relation;
        double start, end;
        if (acc.CrossStart is { } crossStart && acc.CrossEnd is { } crossEnd)
        {
            (relation, start, end) = (RouteWarningRelation.Crosses, crossStart, crossEnd);
        }
        else if (acc.CorridorAlong is { } corridor)
        {
            (relation, start, end) = (RouteWarningRelation.Crosses, corridor, corridor);
        }
        else if (acc.PointInCorridor || acc.Closest <= _options.NearDistanceMeters)
        {
            (relation, start, end) = (RouteWarningRelation.Near, acc.ClosestAlong, acc.ClosestAlong);
        }
        else
        {
            return new RouteWarningMatch(warning, RouteWarningRelation.Off, null, double.NaN, double.NaN,
                double.IsFinite(acc.Closest) ? acc.Closest : double.PositiveInfinity, null);
        }

        var startLeg = track.LegAt(start);
        var endLeg = track.LegAt(end);
        var startEta = schedule.EtaAt(startLeg, start - track.Legs[startLeg].Start);
        var endEta = schedule.EtaAt(endLeg, end - track.Legs[endLeg].Start);
        var atEta = startEta is not null && endEta is not null;
        var from = atEta ? startEta!.Value : schedule.Departure;
        var until = atEta ? endEta!.Value : schedule.Departure;
        var (status, withdrawal) = StatusOver(warning.Info, set, from, until);

        var code = (status, relation) switch
        {
            (RouteWarningStatus.InForce, RouteWarningRelation.Crosses) => RouteFindingCodes.WarningOnRoute,
            (RouteWarningStatus.InForce, _) => RouteFindingCodes.WarningNearRoute,
            (RouteWarningStatus.Cancelled, _) => RouteFindingCodes.WarningCancelled,
            _ => RouteFindingCodes.WarningNotInForce,
        };

        var finding = _options.Formatter.Render(new RouteFinding(
            warning.Feature is { } feature ? $"s124.{feature.DatasetId}/{feature.FeatureId}" : $"s124.{index}",
            code)
        {
            LegIndex = startLeg,
            Distance = Length.FromMetres(start),
            Location = RouteCorridor.Location(track, start, end),
            Feature = warning.Feature,
            Details = new WarningDetails
            {
                Info = warning.Info,
                Relation = relation,
                Status = status,
                From = from,
                Until = until,
                AtEta = atEta,
                InsideArea = acc.CrossedArea || (acc.CrossStart is null && shapes.Areas.Count > 0),
                Withdrawal = withdrawal,
                EndLegIndex = endLeg,
            },
        });
        return new RouteWarningMatch(warning, relation, startLeg, start, end, acc.Closest, finding);
    }

}
