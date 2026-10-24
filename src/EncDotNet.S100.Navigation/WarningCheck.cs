using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The warning check, built into <see cref="RouteChecker"/>: checks the route against navigational warnings (S-124 Editions 1.0.0 and
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
/// <para>
/// Skipped without warnings (<see cref="RouteCheckReasons.WarningsMissing"/>).
/// Its <see cref="RouteCheckOutcome{TDetail}.Detail"/> is the <see cref="WarningCheckResult"/>.
/// </para>
/// <para>This is an aid for exploring the data. It isn't for navigation.</para>
/// </remarks>
public sealed class WarningCheck : IRouteCheck<WarningCheckResult>
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "warnings";

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="RouteWarningOptions.SegmentLengthMeters"/> isn't positive,
    /// <see cref="RouteWarningOptions.MaxSegmentsPerLeg"/> is less than 1, or
    /// <see cref="RouteWarningOptions.NearDistanceMeters"/> is negative.
    /// </exception>
    public RouteCheckOutcome<WarningCheckResult> Run(RouteCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Warnings is not { } source)
        {
            return RouteCheckOutcome<WarningCheckResult>.Skipped(RouteCheckReasons.WarningsMissing);
        }

        var options = context.Options.Warnings;
        if (!(options.SegmentLengthMeters > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(context), "SegmentLengthMeters must be positive.");
        }

        if (options.MaxSegmentsPerLeg < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "MaxSegmentsPerLeg must be at least 1.");
        }

        if (!(options.NearDistanceMeters >= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(context), "NearDistanceMeters must not be negative.");
        }

        var track = context.Track.Densified(options.SegmentLengthMeters, options.MaxSegmentsPerLeg);
        var warnings = new List<RouteWarning>();
        var seen = new HashSet<object>();
        foreach (var extent in track.LegExtents(Math.Max(options.NearDistanceMeters, track.MaxHalfWidth)))
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

        var (matches, findings) = Check(track, context.Schedule, warnings, source.Set, options.NearDistanceMeters, context.Options.Formatter, cancellationToken);
        return RouteCheckOutcome<WarningCheckResult>.Ran(findings, new WarningCheckResult(matches));
    }

    /// <inheritdoc/>
    RouteCheckOutcome IRouteCheck.Run(RouteCheckContext context, CancellationToken cancellationToken) => Run(context, cancellationToken);

    private static (IReadOnlyList<RouteWarningMatch> Matches, IReadOnlyList<RouteFinding> Findings) Check(
        RouteCorridor.Track track,
        PassageSchedule schedule,
        IReadOnlyList<RouteWarning> warnings,
        NavigationalWarningSet set,
        double nearDistance,
        RouteFindingFormatter formatter,
        CancellationToken cancellationToken)
    {
        var shapes = warnings
            .Select(w => new RouteCorridor.Shapes((w ?? throw new ArgumentException("A warning is null.", nameof(warnings))).Shapes))
            .ToList();
        var accumulators = RouteCorridor.Measure(track, shapes, Math.Max(nearDistance, track.MaxHalfWidth), cancellationToken);

        var matches = new List<RouteWarningMatch>(warnings.Count);
        var findings = new List<RouteFinding>();
        for (var w = 0; w < warnings.Count; w++)
        {
            var match = Match(warnings, w, shapes[w], accumulators[w], track, schedule, set, nearDistance, formatter);
            matches.Add(match);
            if (match.Finding is { } finding)
            {
                findings.Add(finding);
            }
        }

        return (matches, RouteFinding.Sort(findings));
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
    internal static (RouteWarningStatus Status, NavigationalWarningState? Withdrawal) StatusOver(
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

    private static RouteWarningMatch Match(IReadOnlyList<RouteWarning> warnings, int index, RouteCorridor.Shapes shapes, RouteCorridor.Accumulator acc, RouteCorridor.Track track, PassageSchedule schedule, NavigationalWarningSet set, double nearDistance, RouteFindingFormatter formatter)
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
        else if (acc.PointInCorridor || acc.Closest <= nearDistance)
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

        var finding = formatter.Render(new RouteFinding(
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
