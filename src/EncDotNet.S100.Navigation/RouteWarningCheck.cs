using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>The codes of the findings <see cref="RouteWarningCheck"/> produces.</summary>
public static class RouteWarningCodes
{
    /// <summary>The warning's line or area crosses the corridor, and it is in force while the vessel is there. MUST REVIEW.</summary>
    public const string OnRoute = "s124.on_route";

    /// <summary>The warning is within the near distance of the track, and in force while the vessel is there. CHECK.</summary>
    public const string NearRoute = "s124.near_route";

    /// <summary>The warning is on or near the route, but not in force while the vessel is there. INFO.</summary>
    public const string NotInForce = "s124.not_in_force";

    /// <summary>The warning is on or near the route, but cancelled by the time the vessel is there. INFO.</summary>
    public const string Cancelled = "s124.cancelled";
}

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
    /// (see <see cref="RouteFindingWarning.Withdrawal"/> for which).
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

/// <summary>
/// The time a route's warnings are judged at: the ETA where the vessel
/// meets each warning when the legs have speeds, otherwise one time.
/// </summary>
/// <param name="Schedule">The route's ETAs, or <c>null</c> when it has none; a leg without a speed has no ETA.</param>
/// <param name="Fallback">The time used where there is no ETA, usually the view time.</param>
public sealed record RouteWarningTiming(PassageSchedule? Schedule, DateTimeOffset Fallback);

/// <summary>An S-124 finding's warning, and how it stands on the route.</summary>
/// <param name="Info">The warning part's message.</param>
/// <param name="Relation">How it relates to the route (never <see cref="RouteWarningRelation.Off"/>).</param>
/// <param name="Status">Whether it applies while the vessel is there.</param>
/// <param name="From">When the vessel reaches it (UTC): the ETA, or the fallback time when <paramref name="AtEta"/> is false.</param>
/// <param name="Until">When the vessel leaves it; equal to <paramref name="From"/> for a crossing point or a near warning.</param>
/// <param name="AtEta">Whether <paramref name="From"/> and <paramref name="Until"/> are ETAs rather than the fallback time.</param>
public sealed record RouteFindingWarning(
    NavigationalWarningInfo Info,
    RouteWarningRelation Relation,
    RouteWarningStatus Status,
    DateTimeOffset From,
    DateTimeOffset Until,
    bool AtEta)
{
    /// <summary>
    /// Why the warning is <see cref="RouteWarningStatus.Cancelled"/>: its
    /// cancellation date, the warning that cancels it, or the in-force
    /// bulletin that leaves it out (<see cref="NavigationalWarningSet.WithdrawnAt"/>).
    /// <c>null</c> for any other status.
    /// </summary>
    public NavigationalWarningState? Withdrawal { get; init; }

    /// <summary>
    /// The leg where the route leaves the warning, so a warning met on several
    /// legs reads "Crosses route on 2 legs"; <c>null</c> when it is the
    /// finding's <see cref="RouteFinding.LegIndex"/>.
    /// </summary>
    public int? EndLegIndex { get; init; }
}

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

    /// <summary>The wording of the findings. Hosts with their own resources pass their own.</summary>
    public RouteWarningText Text { get; init; } = RouteWarningText.Default;
}

/// <summary>The wording of the S-124 findings: titles and the parts of the meta line.</summary>
public sealed record RouteWarningText
{
    /// <summary>The English wording, formatted with the current culture, times in UTC.</summary>
    public static RouteWarningText Default { get; } = new();

    /// <summary>Title when the route goes through the warning's area ({0}: the warning's number).</summary>
    public string InsideArea { get; init; } = "Inside navigational warning {0}";

    /// <summary>Title when the route crosses the warning's line ({0}: the warning's number).</summary>
    public string CrossesLine { get; init; } = "Crosses navigational warning {0}";

    /// <summary>Title when the warning is near the route ({0}: the warning's number).</summary>
    public string Near { get; init; } = "Navigational warning {0} near the route";

    /// <summary>Title when the warning isn't in force while the vessel is there ({0}: the warning's number).</summary>
    public string NotInForce { get; init; } = "Navigational warning {0} not in force";

    /// <summary>Title when the warning is cancelled by then ({0}: the warning's number).</summary>
    public string Cancelled { get; init; } = "Navigational warning {0} cancelled";

    /// <summary>The meta line's status when in force ({0}: the time).</summary>
    public string InForceAt { get; init; } = "In force at {0}";

    /// <summary>The meta line's status when not in force ({0}: the time).</summary>
    public string NotInForceAt { get; init; } = "Not in force at {0}";

    /// <summary>The meta line's status when cancelled.</summary>
    public string CancelledStatus { get; init; } = "Cancelled";

    /// <summary>
    /// Title when the latest in-force bulletin leaves the warning out
    /// ({0}: the warning's number, {1}: the bulletin's series and number).
    /// </summary>
    public string NotInBulletin { get; init; } = "Navigational warning {0} not in in-force bulletin {1}";

    /// <summary>The meta line's status when the latest in-force bulletin leaves it out ({0}: the bulletin).</summary>
    public string NotInBulletinStatus { get; init; } = "Cancelled — not in in-force bulletin {0}";

    /// <summary>A validity range with both ends ({0} start, {1} end).</summary>
    public string ValidityRange { get; init; } = "{0}–{1}";

    /// <summary>A validity range with no end ({0} start).</summary>
    public string ValidityFrom { get; init; } = "from {0}";

    /// <summary>A validity range with no start ({0} end).</summary>
    public string ValidityUntil { get; init; } = "until {0}";

    /// <summary>The validity when the part gives no range.</summary>
    public string ValidityOpen { get; init; } = "until cancelled";

    /// <summary>Joins the parts of a meta line.</summary>
    public string Separator { get; init; } = " · ";

    /// <summary>Formats a time; <c>null</c> writes it in UTC as <c>yyyy-MM-dd HH:mm UTC</c>.</summary>
    public Func<DateTimeOffset, string>? FormatTime { get; init; }

    /// <summary>
    /// Formats the time the status is judged at (an ETA, or the fallback);
    /// <c>null</c> uses <see cref="FormatTime"/>.
    /// </summary>
    public Func<DateTimeOffset, string>? FormatAt { get; init; }

    /// <summary>The culture text is formatted in, or <c>null</c> for the current culture.</summary>
    public CultureInfo? Culture { get; init; }

    internal string Time(DateTimeOffset time) =>
        FormatTime?.Invoke(time) ?? time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    internal string At(DateTimeOffset time) => FormatAt?.Invoke(time) ?? Time(time);

    internal string Format(string format, params object?[] args) =>
        string.Format(Culture ?? CultureInfo.CurrentCulture, format, args);
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
/// when the legs have speeds, otherwise <see cref="RouteWarningTiming.Fallback"/>.
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

    /// <summary>Checks a route against warnings, resolving cancellations and in-force bulletins among them.</summary>
    /// <param name="legs">The route's legs in order, with their cross-track limits; each starts where the previous one ends.</param>
    /// <param name="warnings">The warning parts.</param>
    /// <param name="timing">The ETAs, or the time to judge at without them.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>One match per warning, and the findings.</returns>
    public RouteWarningResult Check(
        IReadOnlyList<RouteCheckLeg> legs,
        IReadOnlyList<RouteWarning> warnings,
        RouteWarningTiming timing,
        CancellationToken cancellationToken = default) =>
        Check(legs, warnings, timing, null, cancellationToken);

    /// <summary>Checks a route against warnings, judged with every loaded warning.</summary>
    /// <param name="legs">The route's legs in order, with their cross-track limits; each starts where the previous one ends.</param>
    /// <param name="warnings">The warning parts to check.</param>
    /// <param name="timing">The ETAs, or the time to judge at without them.</param>
    /// <param name="set">
    /// Every loaded warning, including cancellations and in-force bulletins
    /// with no parts, which may end the checked ones; <c>null</c> builds it
    /// from <paramref name="warnings"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>One match per warning, and the findings.</returns>
    public RouteWarningResult Check(
        IReadOnlyList<RouteCheckLeg> legs,
        IReadOnlyList<RouteWarning> warnings,
        RouteWarningTiming timing,
        NavigationalWarningSet? set,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(legs);
        ArgumentNullException.ThrowIfNull(warnings);
        ArgumentNullException.ThrowIfNull(timing);
        var track = RouteCorridor.Track.Build(legs, _options.DefaultHalfWidthMeters, _options.SegmentLengthMeters, _options.MaxSegmentsPerLeg);
        var shapes = warnings
            .Select(w => new RouteCorridor.Shapes((w ?? throw new ArgumentException("A warning is null.", nameof(warnings))).Shapes))
            .ToList();
        var reach = Math.Max(_options.NearDistanceMeters, track.MaxHalfWidth);
        var accumulators = RouteCorridor.Measure(track, shapes, reach, cancellationToken);
        set ??= new NavigationalWarningSet(warnings.Select(w => w.Info));

        var matches = new List<RouteWarningMatch>(warnings.Count);
        var findings = new List<RouteFinding>();
        for (var w = 0; w < warnings.Count; w++)
        {
            var match = Match(warnings, w, shapes[w], accumulators[w], track, timing, set);
            matches.Add(match);
            if (match.Finding is { } finding)
            {
                findings.Add(finding);
            }
        }

        var sorted = findings
            .OrderBy(f => f.Group)
            .ThenBy(f => f.DistanceMeters ?? double.MaxValue)
            .ToList();
        return new RouteWarningResult(matches, sorted);
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

    private RouteWarningMatch Match(IReadOnlyList<RouteWarning> warnings, int index, RouteCorridor.Shapes shapes, RouteCorridor.Accumulator acc, RouteCorridor.Track track, RouteWarningTiming timing, NavigationalWarningSet set)
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
        var startEta = timing.Schedule?.EtaAt(startLeg, start - track.Legs[startLeg].Start);
        var endEta = timing.Schedule?.EtaAt(endLeg, end - track.Legs[endLeg].Start);
        var atEta = startEta is not null && endEta is not null;
        var from = atEta ? startEta!.Value : timing.Fallback;
        var until = atEta ? endEta!.Value : timing.Fallback;
        var (status, withdrawal) = StatusOver(warning.Info, set, from, until);

        var text = _options.Text;
        var number = warning.Info.Number ?? warning.Info.Title ?? string.Empty;
        var bulletin = withdrawal is { Reason: NavigationalWarningStatusReason.NotInBulletin, By: { } by }
            ? by.Label ?? by.Title ?? string.Empty
            : null;
        var (code, group, title) = (status, relation) switch
        {
            (RouteWarningStatus.InForce, RouteWarningRelation.Crosses) => (RouteWarningCodes.OnRoute, RouteFindingGroup.MustReview,
                acc.CrossedArea || (acc.CrossStart is null && shapes.Areas.Count > 0) ? text.InsideArea : text.CrossesLine),
            (RouteWarningStatus.InForce, _) => (RouteWarningCodes.NearRoute, RouteFindingGroup.Check, text.Near),
            (RouteWarningStatus.Cancelled, _) => (RouteWarningCodes.Cancelled, RouteFindingGroup.Info, bulletin is null ? text.Cancelled : text.NotInBulletin),
            _ => (RouteWarningCodes.NotInForce, RouteFindingGroup.Info, text.NotInForce),
        };

        var finding = new RouteFinding(
            warning.Feature is { } feature ? $"s124.{feature.DatasetId}/{feature.FeatureId}" : $"s124.{index}",
            code,
            group,
            RouteFindingSource.S124,
            text.Format(title, number, bulletin).Trim(),
            Meta(warning.Info, status, from, text, bulletin),
            startLeg,
            start,
            RouteCorridor.Location(track, start, end),
            warning.Feature)
        {
            Warning = new RouteFindingWarning(warning.Info, relation, status, from, until, atEta) { Withdrawal = withdrawal, EndLegIndex = endLeg },
        };
        return new RouteWarningMatch(warning, relation, startLeg, start, end, acc.Closest, finding);
    }

    // "NAVAREA I 012/26 · Dover Strait · 1 Mar 05:00–08:00 · In force at 06:08"
    private static string Meta(NavigationalWarningInfo info, RouteWarningStatus status, DateTimeOffset at, RouteWarningText text, string? bulletin)
    {
        var parts = new List<string>();
        if (info.Label is { } label)
        {
            parts.Add(label);
        }

        if ((info.Locality ?? info.GeneralArea) is { } locality)
        {
            parts.Add(locality);
        }

        parts.Add(info.Validity.Count == 0
            ? text.ValidityOpen
            : string.Join(", ", info.Validity.Select(r => (r.Start, r.End) switch
            {
                ({ } s, { } e) => text.Format(text.ValidityRange, text.Time(s), text.Time(e)),
                ({ } s, null) => text.Format(text.ValidityFrom, text.Time(s)),
                (null, { } e) => text.Format(text.ValidityUntil, text.Time(e)),
                _ => text.ValidityOpen,
            })));
        parts.Add(status switch
        {
            RouteWarningStatus.InForce => text.Format(text.InForceAt, text.At(at)),
            RouteWarningStatus.NotInForce => text.Format(text.NotInForceAt, text.At(at)),
            _ => bulletin is null ? text.CancelledStatus : text.Format(text.NotInBulletinStatus, bulletin),
        });
        return string.Join(text.Separator, parts);
    }
}
