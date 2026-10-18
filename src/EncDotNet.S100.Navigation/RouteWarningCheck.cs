using System.Globalization;
using EncDotNet.S100.DataModel;
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
    // The most positions in a span's path.
    private const int MaxPathPoints = 64;

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
        var track = Track.Build(legs, _options);
        var shapes = warnings.Select(w => new Shapes(w ?? throw new ArgumentException("A warning is null.", nameof(warnings)))).ToList();
        var accumulators = shapes.Select(_ => new Accumulator()).ToArray();
        var reach = Math.Max(_options.NearDistanceMeters, track.MaxHalfWidth);
        set ??= new NavigationalWarningSet(warnings.Select(w => w.Info));

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

    // Compares one piece of track with one warning's shapes, in a plane centred on the piece's start.
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
                else if (distance < acc.CorridorClosest)
                {
                    acc.CorridorClosest = distance;
                    acc.CorridorAlong = along;
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

    private RouteWarningMatch Match(IReadOnlyList<RouteWarning> warnings, int index, Shapes shapes, Accumulator acc, Track track, RouteWarningTiming timing, NavigationalWarningSet set)
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
            Location(track, start, end),
            warning.Feature)
        {
            Warning = new RouteFindingWarning(warning.Info, relation, status, from, until, atEta) { Withdrawal = withdrawal },
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

    private static RouteFindingLocation Location(Track track, double start, double end)
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
    private readonly record struct Sample(GeoPosition Position, double Distance, int Leg);

    /// <summary>A leg's distance from departure and its corridor's half-widths.</summary>
    private readonly record struct LegInfo(double Start, double Port, double Starboard);

    /// <summary>The route cut into pieces.</summary>
    private sealed class Track
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

        public static Track Build(IReadOnlyList<RouteCheckLeg> legs, RouteWarningOptions options)
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
                    Math.Max(0, leg.PortCrossTrackMeters ?? options.DefaultHalfWidthMeters),
                    Math.Max(0, leg.StarboardCrossTrackMeters ?? options.DefaultHalfWidthMeters)));
                var points = leg.Points;
                var lengths = new double[points.Count - 1];
                for (var p = 0; p < lengths.Length; p++)
                {
                    lengths[p] = PassageGeodesy.DistanceMeters(points[p], points[p + 1], leg.Geometry);
                }

                var spacing = Math.Max(options.SegmentLengthMeters, lengths.Sum() / options.MaxSegmentsPerLeg);
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

    /// <summary>A warning's geometry, split by kind, with its extent.</summary>
    private sealed class Shapes
    {
        private double _south = double.MaxValue;
        private double _north = double.MinValue;
        private readonly double _centreLongitude;
        private readonly double _halfLongitudeSpan;
        private readonly bool _wide;

        public Shapes(RouteWarning warning)
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

            foreach (var shape in warning.Shapes)
            {
                if (shape.Points.Count > 0)
                {
                    Points.AddRange(shape.Points);
                    Add(shape.Points);
                }

                foreach (var curve in shape.Curves)
                {
                    if (curve.Count >= 2)
                    {
                        Lines.Add(curve);
                        Add(curve);
                    }
                    else
                    {
                        Points.AddRange(curve);
                        Add(curve);
                    }
                }

                if (shape.ExteriorRing.Count >= 3)
                {
                    var rings = new List<IReadOnlyList<GeoPosition>> { Open(shape.ExteriorRing) };
                    rings.AddRange(shape.InteriorRings.Where(r => r.Count >= 3).Select(Open));
                    Areas.Add(rings);
                    Add(shape.ExteriorRing);
                }
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

        // Drops a closed ring's repeated last point.
        private static IReadOnlyList<GeoPosition> Open(IReadOnlyList<GeoPosition> ring) =>
            ring.Count > 1 && ring[0].Equals(ring[^1]) ? ring.Take(ring.Count - 1).ToArray() : ring;
    }

    /// <summary>What a warning's shapes do along the route.</summary>
    private sealed class Accumulator
    {
        public double? CrossStart { get; private set; }

        public double? CrossEnd { get; private set; }

        public bool CrossedArea { get; private set; }

        public double Closest { get; set; } = double.PositiveInfinity;

        public double ClosestAlong { get; set; } = double.PositiveInfinity;

        public double CorridorClosest { get; set; } = double.PositiveInfinity;

        public double? CorridorAlong { get; set; }

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
    private readonly struct LocalFrame(GeoPosition origin)
    {
        private readonly double _cos = Math.Cos(origin.Latitude * Math.PI / 180);

        public (double X, double Y) ToLocal(GeoPosition p) => (
            Wrap(p.Longitude - origin.Longitude) * _cos * PassageGeodesy.MetersPerDegreeLatitude,
            (p.Latitude - origin.Latitude) * PassageGeodesy.MetersPerDegreeLatitude);

        public static double Wrap(double degrees) =>
            degrees > 180 ? degrees - 360 : degrees < -180 ? degrees + 360 : degrees;
    }
}
