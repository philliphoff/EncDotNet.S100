using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

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

    /// <summary>An S-101 <c>OffshoreProductionArea</c> (#1127).</summary>
    OffshoreProductionArea,

    /// <summary>An S-101 <c>SeaplaneLandingArea</c> (#1127).</summary>
    SeaplaneLandingArea,

    /// <summary>An S-101 <c>MarineFarmCulture</c> (#1127).</summary>
    MarineFarm,

    /// <summary>Another S-101 feature whose portrayal raises the <c>ProhAre</c> alert (#1127).</summary>
    Other,

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

    /// <summary>
    /// Its kind of prohibited area or area with special conditions in the
    /// S-101 alert catalogue: the viewing group of its <c>ProhAre</c>
    /// highlight (53012–53024, <see cref="RouteAreaKinds"/>), or <c>null</c>
    /// when it raises no <c>ProhAre</c> alert, as an S-122 area or an S-101
    /// precautionary area (#1127).
    /// </summary>
    public int? AreaKind { get; init; }
}

/// <summary>
/// Supplies the regulated areas a <see cref="AreaCheck"/> checks a route
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

/// <summary>What <see cref="AreaCheck"/> found besides its findings: every area near the route.</summary>
/// <param name="Matches">One entry per area near the route, in force or not, in the order the source gave them.</param>
public sealed record AreaCheckResult(IReadOnlyList<RouteAreaMatch> Matches);

/// <summary>Tuning for <see cref="AreaCheck"/>.</summary>
public sealed record RouteAreaOptions
{
    /// <summary>The default options.</summary>
    public static RouteAreaOptions Default { get; } = new();

    /// <summary>
    /// The length of the pieces a leg is cut into, in metres, as
    /// <see cref="RouteWarningOptions.SegmentLengthMeters"/>. Default 50 m.
    /// </summary>
    public double SegmentLengthMeters { get; init; } = 50;

    /// <summary>The most pieces per leg; a longer leg uses longer pieces. Default 20 000.</summary>
    public int MaxSegmentsPerLeg { get; init; } = 20_000;
}

/// <summary>
/// The English labels of the codes an area finding names, from the S-101
/// Edition 2.0.0 Feature Catalogue (lower case, as they read in a meta line).
/// </summary>
internal static class RouteAreaLabels
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
