using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>The product an area comes from.</summary>
public enum RouteAreaProduct
{
    /// <summary>An S-101 cell (or S-401, or a translated S-57 cell).</summary>
    S101,

    /// <summary>An S-122 marine protected area dataset.</summary>
    S122,
}

/// <summary>What an area is, as the area check sees it.</summary>
public enum RouteAreaKind
{
    /// <summary>A <c>RestrictedArea</c> (S-101 2.0.0, and the 1.x <c>RestrictedAreaNavigational</c> and <c>RestrictedAreaRegulatory</c>; S-122).</summary>
    RestrictedArea,

    /// <summary>An S-101 <c>MilitaryPracticeArea</c>.</summary>
    MilitaryPracticeArea,

    /// <summary>An S-101 <c>CautionArea</c>.</summary>
    CautionArea,

    /// <summary>An S-101 <c>AnchorageArea</c>.</summary>
    AnchorageArea,

    /// <summary>An S-101 <c>SeparationZoneOrLine</c> surface (the 1.x <c>TrafficSeparationZone</c>).</summary>
    SeparationZone,

    /// <summary>An S-101 <c>PrecautionaryArea</c>.</summary>
    PrecautionaryArea,

    /// <summary>An S-101 <c>InshoreTrafficZone</c>.</summary>
    InshoreTrafficZone,

    /// <summary>An S-101 <c>SubmarineTransitLane</c>.</summary>
    SubmarineTransitLane,

    /// <summary>An S-122 <c>MarineProtectedArea</c>.</summary>
    MarineProtectedArea,
}

/// <summary>
/// An S-100 date range (<c>fixedDateRange</c> or <c>periodicDateRange</c>)
/// as its text: <c>S100_TruncatedDate</c> dates (<c>2026-03-01</c>,
/// <c>20260301</c>, or without a year, <c>--03-01</c>, <c>----0301</c>)
/// and optional times of day.
/// </summary>
/// <param name="DateStart">The <c>dateStart</c> text, or <c>null</c> when open.</param>
/// <param name="DateEnd">The <c>dateEnd</c> text, or <c>null</c> when open.</param>
/// <param name="TimeOfDayStart">The <c>timeOfDayStart</c> text, or <c>null</c>.</param>
/// <param name="TimeOfDayEnd">The <c>timeOfDayEnd</c> text, or <c>null</c>.</param>
public sealed record RouteAreaDateRange(string? DateStart, string? DateEnd, string? TimeOfDayStart = null, string? TimeOfDayEnd = null);

/// <summary>
/// One regulated area to check a route against: an S-101 restricted,
/// caution, anchorage or traffic area, or an S-122 protected area.
/// </summary>
/// <param name="Product">The product it comes from.</param>
/// <param name="Kind">What it is.</param>
/// <param name="FeatureType">Its feature type, such as <c>"RestrictedArea"</c>.</param>
/// <param name="Surfaces">Its surfaces in WGS-84, with their holes.</param>
/// <param name="Feature">The feature behind it, which the finding refers to, or <c>null</c>.</param>
public sealed record RouteArea(
    RouteAreaProduct Product,
    RouteAreaKind Kind,
    string FeatureType,
    IReadOnlyList<SurfacePart> Surfaces,
    RouteFindingFeature? Feature = null)
{
    /// <summary>Its name (<c>featureName</c>), or <c>null</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Its <c>restriction</c> codes, in the order given.</summary>
    public IReadOnlyList<int> Restrictions { get; init; } = [];

    /// <summary>
    /// Its category codes: <c>categoryOfRestrictedArea</c> for a restricted
    /// or protected area, <c>categoryOfMilitaryPracticeArea</c>,
    /// <c>categoryOfAnchorage</c>.
    /// </summary>
    public IReadOnlyList<int> Categories { get; init; } = [];

    /// <summary>An S-122 area's <c>categoryOfMarineProtectedArea</c> (IUCN category), or <c>null</c>.</summary>
    public int? CategoryOfMarineProtectedArea { get; init; }

    /// <summary>When the area is valid (<c>fixedDateRange</c>), or <c>null</c> for always.</summary>
    public RouteAreaDateRange? FixedDateRange { get; init; }

    /// <summary>When the area is active each year (<c>periodicDateRange</c>); empty for all year.</summary>
    public IReadOnlyList<RouteAreaDateRange> PeriodicDateRanges { get; init; } = [];

    /// <summary>
    /// The S-101 cell that charts it, for the finest-cell rule, or
    /// <c>null</c> (S-122, or when not known).
    /// </summary>
    public RouteChartCell? Cell { get; init; }
}

/// <summary>
/// Supplies the regulated areas a <see cref="RouteAreaCheck"/> checks a route
/// against. <c>EncDotNet.S100.Datasets.Pipelines</c> implements it over loaded
/// S-101 cells and S-122 datasets (<c>CatalogRouteCheckSources</c>).
/// </summary>
/// <remarks>Implementations must be safe to call from several threads at once.</remarks>
public interface IRouteAreaSource
{
    /// <summary>The areas whose extent overlaps a box, from every cell and dataset.</summary>
    /// <param name="extent">The box, in WGS-84.</param>
    /// <returns>The areas, in a stable order.</returns>
    IReadOnlyList<RouteArea> AreasNear(BoundingBox extent);

    /// <summary>The S-101 cells whose extent overlaps a box, for the finest-cell rule.</summary>
    /// <param name="extent">The box, in WGS-84.</param>
    /// <returns>The cells.</returns>
    IReadOnlyList<RouteChartCell> CellsNear(BoundingBox extent);
}

/// <summary>How an area relates to a route.</summary>
public enum RouteAreaRelation
{
    /// <summary>The track enters the area.</summary>
    Enters,

    /// <summary>The area reaches inside the cross-track limits, but the track doesn't enter it.</summary>
    InCorridor,

    /// <summary>Neither (or a finer cell charts the place, so the area doesn't count).</summary>
    Off,
}

/// <summary>One area against the route, found or not.</summary>
/// <param name="Area">The area.</param>
/// <param name="Relation">How it relates to the route.</param>
/// <param name="LegIndex">The leg where the route first meets it, or <c>null</c> when off.</param>
/// <param name="StartDistanceMeters">Distance from departure to where the route first meets it; <c>NaN</c> when off.</param>
/// <param name="EndDistanceMeters">Distance from departure to where the route leaves it; <c>NaN</c> when off.</param>
/// <param name="InForce">Whether it is in force while the vessel is there (<c>false</c> when off).</param>
/// <param name="Finding">The finding, or <c>null</c> when off.</param>
public sealed record RouteAreaMatch(
    RouteArea Area,
    RouteAreaRelation Relation,
    int? LegIndex,
    double StartDistanceMeters,
    double EndDistanceMeters,
    bool InForce,
    RouteFinding? Finding);

/// <summary>The result of <see cref="RouteAreaCheck.Check(PassagePlan, RouteCheckSettings, IReadOnlyList{RouteArea}, IReadOnlyList{RouteChartCell}, CancellationToken)"/>.</summary>
/// <param name="Matches">One entry per area, in input order.</param>
/// <param name="Findings">The findings, sorted by group (MUST REVIEW, CHECK, INFO) and then by distance from departure.</param>
public sealed record RouteAreaResult(
    IReadOnlyList<RouteAreaMatch> Matches,
    IReadOnlyList<RouteFinding> Findings);

/// <summary>Tuning for <see cref="RouteAreaCheck"/>.</summary>
public sealed record RouteAreaOptions
{
    /// <summary>The default options.</summary>
    public static RouteAreaOptions Default { get; } = new();

    /// <summary>
    /// The corridor's half-width on a side whose cross-track limit isn't set,
    /// in metres, as <see cref="RouteCheckOptions.DefaultHalfWidthMeters"/>. Default 50 m.
    /// </summary>
    public double DefaultHalfWidthMeters { get; init; } = 50;

    /// <summary>
    /// The length of the pieces a leg is cut into, in metres, as
    /// <see cref="RouteWarningOptions.SegmentLengthMeters"/>. Default 50 m.
    /// </summary>
    public double SegmentLengthMeters { get; init; } = 50;

    /// <summary>The most pieces per leg; a longer leg uses longer pieces. Default 20 000.</summary>
    public int MaxSegmentsPerLeg { get; init; } = 20_000;

    /// <summary>Renders the findings' titles and meta lines. Hosts with their own resources pass their own.</summary>
    public RouteFindingFormatter Formatter { get; init; } = RouteFindingFormatter.Default;
}

/// <summary>
/// The English labels of the codes an area finding names, from the S-101
/// Edition 2.0.0 Feature Catalogue (lower case, as they read in a meta line).
/// </summary>
public static class RouteAreaLabels
{
    /// <summary>The <c>restriction</c> codes (S-101 and S-122).</summary>
    public static IReadOnlyDictionary<int, string> Restrictions { get; } = new Dictionary<int, string>
    {
        [1] = "anchoring prohibited",
        [2] = "anchoring restricted",
        [3] = "fishing prohibited",
        [4] = "fishing restricted",
        [5] = "trawling prohibited",
        [6] = "trawling restricted",
        [7] = "entry prohibited",
        [8] = "entry restricted",
        [9] = "dredging prohibited",
        [10] = "dredging restricted",
        [11] = "diving prohibited",
        [12] = "diving restricted",
        [13] = "no wake",
        [14] = "area to be avoided",
        [15] = "construction prohibited",
        [16] = "discharging prohibited",
        [17] = "discharging restricted",
        [18] = "industrial or mineral exploration/development prohibited",
        [19] = "industrial or mineral exploration/development restricted",
        [20] = "drilling prohibited",
        [21] = "drilling restricted",
        [22] = "removal of historical artefacts prohibited",
        [23] = "cargo transhipment (lightening) prohibited",
        [24] = "dragging prohibited",
        [25] = "stopping prohibited",
        [26] = "landing prohibited",
        [27] = "speed restricted",
        [39] = "swimming prohibited",
        [42] = "power-driven vessels prohibited",
    };

    /// <summary>The <c>categoryOfRestrictedArea</c> codes.</summary>
    public static IReadOnlyDictionary<int, string> RestrictedAreaCategories { get; } = new Dictionary<int, string>
    {
        [1] = "offshore safety zone",
        [4] = "nature reserve",
        [5] = "bird sanctuary",
        [6] = "game reserve",
        [7] = "seal sanctuary",
        [8] = "degaussing range",
        [9] = "military area",
        [10] = "historic wreck area",
        [12] = "navigational aid safety zone",
        [14] = "minefield",
        [18] = "swimming area",
        [19] = "waiting area",
        [20] = "research area",
        [21] = "dredging area",
        [22] = "fish sanctuary",
        [23] = "ecological reserve",
        [24] = "no wake area",
        [25] = "swinging area",
        [27] = "environmentally sensitive sea area",
        [28] = "particularly sensitive sea area",
        [29] = "disengagement area",
        [30] = "port security area",
        [31] = "coral sanctuary",
        [32] = "recreation area",
    };

    /// <summary>The <c>categoryOfMilitaryPracticeArea</c> codes.</summary>
    public static IReadOnlyDictionary<int, string> MilitaryPracticeAreaCategories { get; } = new Dictionary<int, string>
    {
        [2] = "torpedo exercise area",
        [3] = "submarine exercise area",
        [4] = "firing danger area",
        [5] = "mine-laying practice area",
        [6] = "small arms firing range",
    };

    /// <summary>The <c>categoryOfAnchorage</c> codes.</summary>
    public static IReadOnlyDictionary<int, string> AnchorageCategories { get; } = new Dictionary<int, string>
    {
        [1] = "unrestricted anchorage",
        [2] = "deep water anchorage",
        [3] = "tanker anchorage",
        [5] = "quarantine anchorage",
        [6] = "seaplane anchorage",
        [7] = "small craft anchorage",
        [9] = "anchorage for periods up to 24 hours",
        [10] = "anchorage for a limited period of time",
        [14] = "waiting anchorage",
        [15] = "reported anchorage",
    };
}

/// <summary>
/// Checks a route against regulated areas (#956): S-101 restricted,
/// military practice, caution, anchorage and traffic areas, and S-122 marine
/// protected and restricted areas. It finds which the track enters or which
/// reach inside its cross-track limits, and whether each is in force while
/// the vessel is there.
/// </summary>
/// <remarks>
/// <para>
/// The corridor geometry is the S-124 check's (<see cref="RouteWarningCheck"/>):
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
/// <para>This is an aid for exploring the data. It isn't for navigation.</para>
/// </remarks>
public sealed class RouteAreaCheck
{
    // Restrictions that bear on a vessel passing through, short of entry.
    private static readonly HashSet<int> PassageRestrictions = [13, 14, 24, 25, 27, 42];

    // Restricted-area categories worth checking whatever the restrictions say.
    private static readonly HashSet<int> HazardCategories = [1, 8, 9, 12, 14];

    private readonly RouteAreaOptions _options;

    /// <summary>Creates the check.</summary>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteAreaOptions.Default"/>.</param>
    public RouteAreaCheck(RouteAreaOptions? options = null)
    {
        _options = options ?? RouteAreaOptions.Default;
        if (!(_options.SegmentLengthMeters > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SegmentLengthMeters must be positive.");
        }

        if (_options.MaxSegmentsPerLeg < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxSegmentsPerLeg must be at least 1.");
        }
    }

    /// <summary>Checks a plan against the areas a source has near it.</summary>
    /// <param name="plan">The plan: its legs with their cross-track limits and speeds, and its departure.</param>
    /// <param name="settings">The corridor override and the time to judge at where the plan gives none.</param>
    /// <param name="source">The areas, and the cells for the finest-cell rule.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>One match per area near the route, and the findings.</returns>
    public RouteAreaResult Check(PassagePlan plan, RouteCheckSettings settings, IRouteAreaSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(source);
        var track = BuildTrack(plan, settings);
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

        return Check(track, areas, cells, PassageSchedule.Compute(plan, settings), cancellationToken);
    }

    /// <summary>Checks a plan against areas.</summary>
    /// <param name="plan">The plan: its legs with their cross-track limits and speeds, and its departure.</param>
    /// <param name="settings">The corridor override and the time to judge at where the plan gives none.</param>
    /// <param name="areas">The areas.</param>
    /// <param name="cells">
    /// The S-101 cells near the route, for the finest-cell rule; the cells
    /// of <paramref name="areas"/> are used too. Empty to count every area.
    /// </param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>One match per area, and the findings.</returns>
    public RouteAreaResult Check(
        PassagePlan plan,
        RouteCheckSettings settings,
        IReadOnlyList<RouteArea> areas,
        IReadOnlyList<RouteChartCell> cells,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        return Check(BuildTrack(plan, settings), areas, cells, PassageSchedule.Compute(plan, settings), cancellationToken);
    }

    /// <summary>
    /// Whether an area is in force over a time span: its fixed date range
    /// overlaps it, and a season (when it has any) includes a day of it.
    /// </summary>
    /// <param name="area">The area.</param>
    /// <param name="from">When the vessel reaches it.</param>
    /// <param name="until">When the vessel leaves it.</param>
    /// <returns><c>true</c> when it is in force at some time in the span.</returns>
    public static bool InForceOver(RouteArea area, DateTimeOffset from, DateTimeOffset until)
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
    public static (int Month, int Day)? DayOfYear(string? text)
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

    private RouteCorridor.Track BuildTrack(PassagePlan plan, RouteCheckSettings settings) =>
        RouteCorridor.Track.Build(plan, settings, _options.DefaultHalfWidthMeters, _options.SegmentLengthMeters, _options.MaxSegmentsPerLeg);

    private RouteAreaResult Check(
        RouteCorridor.Track track,
        IReadOnlyList<RouteArea> areas,
        IReadOnlyList<RouteChartCell> cells,
        PassageSchedule schedule,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(cells);
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
            var match = Match(areas[i], i, accumulators[i], track, ranked, schedule);
            matches.Add(match);
            if (match.Finding is { } finding)
            {
                findings.Add(finding);
            }
        }

        return new RouteAreaResult(matches, RouteFinding.Sort(findings));
    }

    private RouteAreaMatch Match(RouteArea area, int index, RouteCorridor.Accumulator acc, RouteCorridor.Track track, List<RouteChartCell> cells, PassageSchedule schedule)
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

        var finding = _options.Formatter.Render(new RouteFinding(
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
