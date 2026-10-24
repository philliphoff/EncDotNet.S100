using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// The area check, built into <see cref="RouteChecker"/>: checks the route against regulated areas (#956): S-101 restricted,
/// military practice, caution, anchorage and traffic areas, and S-122 marine
/// protected and restricted areas. It finds which the track enters or which
/// reach inside its cross-track limits, and whether each is in force while
/// the vessel is there.
/// </summary>
/// <remarks>
/// <para>
/// The corridor geometry is the S-124 check's (<see cref="WarningCheck"/>):
/// each leg is cut into short pieces and compared with the areas, holes
/// included, in a local plane. Where S-101 cells overlap, an area counts
/// only when its cell is in the finest band of the cells covering the place
/// where the route first meets it (#947), as for the chart checks, so a
/// coarse cell's copy of an area a finer cell also charts gives no second
/// finding.
/// </para>
/// <para>
/// Time: <c>fixedDateRange</c> and <c>periodicDateRange</c> are judged over
/// the ETAs where the vessel is in the area when the legs have speeds,
/// otherwise at the departure (<see cref="PassageSchedule.TimeAt"/>). A
/// season repeats each year by month and day and may run over the new
/// year. An area with both must be in both; a date that can't be read
/// counts as open, so an area is never dropped for want of a date. An area
/// not in force while the vessel is there stays in the list as INFO
/// <see cref="RouteFindingCodes.AreaNotInForce"/> rather than being dropped, as
/// S-124 warnings that aren't in force are (#957).
/// </para>
/// <para>
/// Severity: an S-101 area whose restriction is entry prohibited or entry
/// restricted is MUST REVIEW when the track enters it, which the verdict
/// ranks just after land and drying ground. Other areas are CHECK, or INFO
/// when their restrictions don't bear on a passage (fishing, anchoring,
/// dredging and the like) or for an inshore traffic zone. An area that only
/// reaches inside the cross-track limits is one group lighter. S-122 areas
/// are never MUST REVIEW: CHECK, or INFO by restriction as above.
/// </para>
/// <para>
/// Skipped without areas (<see cref="RouteCheckReasons.AreasMissing"/>). Its
/// <see cref="RouteCheckOutcome{TDetail}.Detail"/> is the <see cref="AreaCheckResult"/>.
/// </para>
/// <para>This is an aid for exploring the data. It isn't for navigation.</para>
/// </remarks>
public sealed class AreaCheck : IRouteCheck<AreaCheckResult>
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "areas";

    // Restrictions that bear on a vessel passing through, short of entry.
    private static readonly HashSet<int> PassageRestrictions = [13, 14, 24, 25, 27, 42];

    // Restricted-area categories worth checking whatever the restrictions say.
    private static readonly HashSet<int> HazardCategories = [1, 8, 9, 12, 14];

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="RouteAreaOptions.SegmentLengthMeters"/> isn't positive, or <see cref="RouteAreaOptions.MaxSegmentsPerLeg"/> is less than 1.</exception>
    public RouteCheckOutcome<AreaCheckResult> Run(RouteCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Areas is not { } source)
        {
            return RouteCheckOutcome<AreaCheckResult>.Skipped(RouteCheckReasons.AreasMissing);
        }

        var options = context.Options.Areas;
        if (!(options.SegmentLengthMeters > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(context), "SegmentLengthMeters must be positive.");
        }

        if (options.MaxSegmentsPerLeg < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "MaxSegmentsPerLeg must be at least 1.");
        }

        var track = context.Track.Densified(options.SegmentLengthMeters, options.MaxSegmentsPerLeg);
        var areas = new List<RouteArea>();
        var cells = new List<RouteChartCell>();
        var seenAreas = new HashSet<object>();
        var seenCells = new HashSet<RouteChartCell>(ReferenceEqualityComparer.Instance);
        foreach (var extent in track.LegExtents(track.MaxHalfWidth))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var area in source.AreasNear(extent))
            {
                if (seenAreas.Add((object?)area.Feature ?? area))
                {
                    areas.Add(area);
                }
            }

            foreach (var cell in source.CellsNear(extent))
            {
                if (seenCells.Add(cell))
                {
                    cells.Add(cell);
                }
            }
        }

        var (matches, findings) = Check(track, areas, cells, context.Schedule, context.Options.Formatter, cancellationToken);
        return RouteCheckOutcome<AreaCheckResult>.Ran(findings, new AreaCheckResult(matches));
    }

    /// <inheritdoc/>
    RouteCheckOutcome IRouteCheck.Run(RouteCheckContext context, CancellationToken cancellationToken) => Run(context, cancellationToken);

    /// <summary>
    /// Whether an area is in force over a time span: its fixed date range
    /// overlaps it, and a season (when it has any) includes a day of it.
    /// </summary>
    /// <param name="area">The area.</param>
    /// <param name="from">When the vessel reaches it.</param>
    /// <param name="until">When the vessel leaves it.</param>
    /// <returns><c>true</c> when it is in force at some time in the span.</returns>
    internal static bool InForceOver(RouteArea area, DateTimeOffset from, DateTimeOffset until)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (until < from)
        {
            (from, until) = (until, from);
        }

        if (area.FixedDateRange is { } range)
        {
            var start = S100DateTimes.Combine(range.DateStart, range.TimeOfDayStart);
            var end = EndOf(range.DateEnd, range.TimeOfDayEnd);
            if ((start is { } s && s > until) || (end is { } e && e <= from))
            {
                return false;
            }
        }

        if (area.PeriodicDateRanges.Count == 0)
        {
            return true;
        }

        var first = DateOnly.FromDateTime(from.UtcDateTime);
        var last = DateOnly.FromDateTime(until.UtcDateTime);
        foreach (var season in area.PeriodicDateRanges)
        {
            if (DayOfYear(season.DateStart) is not { } start || DayOfYear(season.DateEnd) is not { } end)
            {
                // A season that can't be read doesn't rule the area out.
                return true;
            }

            // A passage of a year or more meets every season.
            if (last.DayNumber - first.DayNumber >= 366)
            {
                return true;
            }

            for (var day = first; day <= last; day = day.AddDays(1))
            {
                var d = (day.Month, day.Day);
                var inside = start.CompareTo(end) <= 0
                    ? d.CompareTo(start) >= 0 && d.CompareTo(end) <= 0
                    : d.CompareTo(start) >= 0 || d.CompareTo(end) <= 0;
                if (inside)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The month and day of an <c>S100_TruncatedDate</c>, with or without its
    /// year: <c>--03-01</c>, <c>--0301</c>, <c>----0301</c>, <c>2026-03-01</c>
    /// or <c>20260301</c>; <c>null</c> when it names no month and day.
    /// </summary>
    /// <param name="text">The date text.</param>
    /// <returns>The month and day, or <c>null</c>.</returns>
    internal static (int Month, int Day)? DayOfYear(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var digits = text.Trim();
        if (digits.Length >= 10 && digits[4] == '-' && digits[7] == '-')
        {
            digits = digits[5..7] + digits[8..10];
        }
        else
        {
            digits = digits.Replace("-", string.Empty, StringComparison.Ordinal);
            if (digits.Length == 8)
            {
                digits = digits[4..];
            }
        }

        if (digits.Length != 4
            || !int.TryParse(digits[..2], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || !int.TryParse(digits[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || month is < 1 or > 12
            || day < 1
            || day > DateTime.DaysInMonth(2000, month))
        {
            return null;
        }

        return (month, day);
    }

    private static (IReadOnlyList<RouteAreaMatch> Matches, IReadOnlyList<RouteFinding> Findings) Check(
        RouteCorridor.Track track,
        IReadOnlyList<RouteArea> areas,
        IReadOnlyList<RouteChartCell> cells,
        PassageSchedule schedule,
        RouteFindingFormatter formatter,
        CancellationToken cancellationToken)
    {
        var shapes = areas
            .Select(a => new RouteCorridor.Shapes((a ?? throw new ArgumentException("An area is null.", nameof(areas))).Surfaces))
            .ToList();
        var accumulators = RouteCorridor.Measure(track, shapes, track.MaxHalfWidth, cancellationToken);

        var ranked = new List<RouteChartCell>(cells);
        var known = new HashSet<RouteChartCell>(cells, ReferenceEqualityComparer.Instance);
        foreach (var area in areas)
        {
            if (area.Cell is { } cell && known.Add(cell))
            {
                ranked.Add(cell);
            }
        }

        var matches = new List<RouteAreaMatch>(areas.Count);
        var findings = new List<RouteFinding>();
        for (var i = 0; i < areas.Count; i++)
        {
            var match = Match(areas[i], i, accumulators[i], track, ranked, schedule, formatter);
            matches.Add(match);
            if (match.Finding is { } finding)
            {
                findings.Add(finding);
            }
        }

        return (matches, RouteFinding.Sort(findings));
    }

    private static RouteAreaMatch Match(RouteArea area, int index, RouteCorridor.Accumulator acc, RouteCorridor.Track track, List<RouteChartCell> cells, PassageSchedule schedule, RouteFindingFormatter formatter)
    {
        RouteAreaRelation relation;
        double start, end;
        if (acc.CrossedArea && acc.CrossStart is { } crossStart && acc.CrossEnd is { } crossEnd)
        {
            (relation, start, end) = (RouteAreaRelation.Enters, crossStart, crossEnd);
        }
        else if (acc.CorridorStart is { } corridorStart && acc.CorridorEnd is { } corridorEnd)
        {
            (relation, start, end) = (RouteAreaRelation.InCorridor, corridorStart, corridorEnd);
        }
        else
        {
            return Off(area);
        }

        if (area.Cell is { } cell && !Finest(cell, track.PositionAt(relation == RouteAreaRelation.Enters ? start : acc.CorridorAlong ?? start), cells))
        {
            return Off(area);
        }

        var startLeg = track.LegAt(start);
        var endLeg = track.LegAt(end);
        var startEta = schedule.EtaAt(startLeg, start - track.Legs[startLeg].Start);
        var endEta = schedule.EtaAt(endLeg, end - track.Legs[endLeg].Start);
        var atEta = startEta is not null && endEta is not null;
        var from = atEta ? startEta!.Value : schedule.Departure;
        var until = atEta ? endEta!.Value : schedule.Departure;
        var inForce = InForceOver(area, from, until);

        RouteFindingCode code;
        RouteFindingGroup group;
        if (!inForce)
        {
            (code, group) = (RouteFindingCodes.AreaNotInForce, RouteFindingGroup.Info);
        }
        else
        {
            (code, group) = Classify(area);
            if (relation == RouteAreaRelation.InCorridor)
            {
                code = code == RouteFindingCodes.EntersProhibitedArea ? RouteFindingCodes.ProhibitedAreaInCorridor : code;
                group = group == RouteFindingGroup.MustReview ? RouteFindingGroup.Check : RouteFindingGroup.Info;
            }
        }

        var finding = formatter.Render(new RouteFinding(
            area.Feature is { } feature ? $"{code.Value}.{feature.DatasetId}/{feature.FeatureId}" : $"{code.Value}.{index}",
            code,
            group)
        {
            LegIndex = startLeg,
            Distance = Length.FromMetres(start),
            Location = RouteCorridor.Location(track, start, end),
            Feature = area.Feature,
            Details = new AreaDetails
            {
                Product = area.Product,
                Kind = area.Kind,
                FeatureType = area.FeatureType,
                Name = area.Name,
                Restrictions = area.Restrictions,
                Categories = area.Categories,
                CategoryOfMarineProtectedArea = area.CategoryOfMarineProtectedArea,
                FixedDateRange = area.FixedDateRange,
                PeriodicDateRanges = area.PeriodicDateRanges,
                Relation = relation,
                InForce = inForce,
                From = from,
                Until = until,
                AtEta = atEta,
            },
        });
        return new RouteAreaMatch(area, relation, startLeg, start, end, inForce, finding);
    }

    private static RouteAreaMatch Off(RouteArea area) =>
        new(area, RouteAreaRelation.Off, null, double.NaN, double.NaN, false, null);

    // The code and group of an area the track enters, while it is in force.
    private static (RouteFindingCode Code, RouteFindingGroup Group) Classify(RouteArea area)
    {
        var entry = area.Restrictions.Any(r => r is 7 or 8);
        var bears = area.Restrictions.Count == 0 || area.Restrictions.Any(PassageRestrictions.Contains) || entry;
        if (area.Product == RouteAreaProduct.S122)
        {
            return (RouteFindingCodes.ProtectedArea, bears ? RouteFindingGroup.Check : RouteFindingGroup.Info);
        }

        if (entry)
        {
            return (RouteFindingCodes.EntersProhibitedArea, RouteFindingGroup.MustReview);
        }

        return area.Kind switch
        {
            RouteAreaKind.RestrictedArea => (RouteFindingCodes.RestrictedArea,
                bears || area.Categories.Any(HazardCategories.Contains) ? RouteFindingGroup.Check : RouteFindingGroup.Info),
            RouteAreaKind.MilitaryPracticeArea => (RouteFindingCodes.RestrictedArea, RouteFindingGroup.Check),
            RouteAreaKind.CautionArea => (RouteFindingCodes.CautionArea, RouteFindingGroup.Check),
            RouteAreaKind.AnchorageArea => (RouteFindingCodes.AnchorageArea, RouteFindingGroup.Check),
            RouteAreaKind.InshoreTrafficZone => (RouteFindingCodes.TrafficArea, RouteFindingGroup.Info),
            RouteAreaKind.MarineProtectedArea => (RouteFindingCodes.ProtectedArea, bears ? RouteFindingGroup.Check : RouteFindingGroup.Info),
            _ => (RouteFindingCodes.TrafficArea, RouteFindingGroup.Check),
        };
    }

    // Whether the area's cell is in the finest band of the cells that cover a point; true when its cell doesn't cover it.
    private static bool Finest(RouteChartCell cell, GeoPosition position, List<RouteChartCell> cells)
    {
        var covering = cells.Where(c => c.Covers(position)).ToList();
        return !covering.Contains(cell, ReferenceEqualityComparer.Instance)
            || CellScaleRanking.FinestBand(covering, c => c.RankingScale).Contains(cell, ReferenceEqualityComparer.Instance);
    }

    // The end of a date range: its time of day, or the end of its day.
    private static DateTimeOffset? EndOf(string? date, string? timeOfDay)
    {
        if (!string.IsNullOrWhiteSpace(timeOfDay))
        {
            return S100DateTimes.Combine(date, timeOfDay);
        }

        var day = S100DateTimes.Combine(date, null);
        return day is { } d && !date!.Contains('T', StringComparison.Ordinal) ? d.AddDays(1) : day;
    }
}
