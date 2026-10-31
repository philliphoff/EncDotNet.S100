namespace EncDotNet.S100.Navigation;

/// <summary>
/// The kind of a <see cref="RouteFinding"/>: its stable code, what it's
/// about, the group it's in unless the check says otherwise, and its rank
/// when a verdict names the worst finding.
/// </summary>
/// <remarks>
/// Every code is in <see cref="RouteFindingCodes"/>. Codes never change:
/// the MCP tools, <c>s100 route check</c> and their clients depend on them.
/// </remarks>
/// <param name="Value">The code, e.g. <c>route_check.crosses_land</c>.</param>
/// <param name="Category">What findings with this code are about.</param>
/// <param name="DefaultGroup">
/// The group a finding with this code is in. Some checks move a finding to a
/// lighter group, such as an area whose restrictions don't bear on a passage.
/// </param>
/// <param name="HeadlineRank">
/// The order in which Must review findings name a verdict, lowest first
/// (#959, decision 3): crosses land, crosses drying ground, a regulated area,
/// a danger in the corridor, below your UKC setting, a warning in force on
/// the route, then anything else.
/// </param>
public sealed record RouteFindingCode(
    string Value,
    RouteFindingCategory Category,
    RouteFindingGroup DefaultGroup,
    int HeadlineRank = RouteFindingCodes.LastRank)
{
    /// <inheritdoc/>
    public override string ToString() => Value;
}

/// <summary>Every code a route check gives its findings.</summary>
public static class RouteFindingCodes
{
    /// <summary>The <see cref="RouteFindingCode.HeadlineRank"/> of a code that has no place of its own in a verdict.</summary>
    public const int LastRank = 6;

    /// <summary>The track crosses a <c>LandArea</c> or a <c>Coastline</c>. MUST REVIEW.</summary>
    public static RouteFindingCode CrossesLand { get; } = new("route_check.crosses_land", RouteFindingCategory.Chart, RouteFindingGroup.MustReview, 0);

    /// <summary>Land reaches into the corridor, but the track doesn't cross it. CHECK.</summary>
    public static RouteFindingCode LandInCorridor { get; } = new("route_check.land_in_corridor", RouteFindingCategory.Chart, RouteFindingGroup.Check);

    /// <summary>The track crosses a drying <c>DepthArea</c>. MUST REVIEW.</summary>
    public static RouteFindingCode CrossesDrying { get; } = new("route_check.crosses_drying", RouteFindingCategory.Chart, RouteFindingGroup.MustReview, 1);

    /// <summary>Drying ground reaches into the corridor, but the track doesn't cross it. CHECK.</summary>
    public static RouteFindingCode DryingInCorridor { get; } = new("route_check.drying_in_corridor", RouteFindingCategory.Chart, RouteFindingGroup.Check);

    /// <summary>The track leaves the data coverage of every loaded chart cell. CHECK.</summary>
    public static RouteFindingCode OutsideChartCoverage { get; } = new("data.outside_chart_coverage", RouteFindingCategory.Data, RouteFindingGroup.Check);

    /// <summary>
    /// A charted danger (wreck, obstruction or rock) shallower than the
    /// threshold, or of unknown depth, inside the cross-track limits. MUST REVIEW.
    /// </summary>
    public static RouteFindingCode DangerInCorridor { get; } = new("route_check.danger_in_corridor", RouteFindingCategory.Depth, RouteFindingGroup.MustReview, 3);

    /// <summary>
    /// A charted danger shallower than the threshold within
    /// <see cref="ChartCheckOptions.DangerNearbyMeters"/> beyond the cross-track limits. CHECK.
    /// </summary>
    public static RouteFindingCode DangerNearRoute { get; } = new("route_check.danger_near_route", RouteFindingCategory.Depth, RouteFindingGroup.Check);

    /// <summary>
    /// An aid to navigation whose portrayal raises <c>NavHazard</c>, in the
    /// corridor (CHECK) or near the route (INFO) (#1127).
    /// </summary>
    public static RouteFindingCode AidToNavigation { get; } = new("route_check.aid_to_navigation", RouteFindingCategory.Chart, RouteFindingGroup.Check);

    /// <summary>A structure within the track clearance of the track line (#1127). MUST REVIEW.</summary>
    public static RouteFindingCode StructureOnTrack { get; } = new("route_check.structure_on_track", RouteFindingCategory.Chart, RouteFindingGroup.MustReview, 3);

    /// <summary>A structure elsewhere in the corridor (CHECK) or near the route (INFO) (#1127).</summary>
    public static RouteFindingCode StructureInCorridor { get; } = new("route_check.structure_in_corridor", RouteFindingCategory.Chart, RouteFindingGroup.Check);

    /// <summary>
    /// A bridge, span, overhead cable or pipeline or conveyor in the corridor
    /// (CHECK) or near the route (INFO) (#1127).
    /// </summary>
    public static RouteFindingCode OverheadOnRoute { get; } = new("route_check.overhead_on_route", RouteFindingCategory.Chart, RouteFindingGroup.Check);

    /// <summary>The track crosses depth, dredged or drying areas shallower than the safety contour. MUST REVIEW.</summary>
    public static RouteFindingCode CrossesSafetyContour { get; } = new("route_check.crosses_safety_contour", RouteFindingCategory.Depth, RouteFindingGroup.MustReview);

    /// <summary>Areas shallower than the safety contour reach into the corridor, but the track doesn't cross them. CHECK.</summary>
    public static RouteFindingCode SafetyContourInCorridor { get; } = new("route_check.safety_contour_in_corridor", RouteFindingCategory.Depth, RouteFindingGroup.Check);

    /// <summary>
    /// Soundings no deeper than the safety contour inside the cross-track
    /// limits, outside water already shallower than it: the soundings the
    /// S-101 portrayal's <c>SOUNDG03</c> raises <c>NavHazard</c> for (#1127). CHECK.
    /// </summary>
    public static RouteFindingCode SoundingsBelowSafetyContour { get; } = new("route_check.soundings_below_safety_contour", RouteFindingCategory.Depth, RouteFindingGroup.Check);

    /// <summary>The track crosses S-102 cells shallower than the safety contour. MUST REVIEW.</summary>
    public static RouteFindingCode CrossesS102Shoal { get; } = new("route_check.crosses_s102_shoal", RouteFindingCategory.Depth, RouteFindingGroup.MustReview);

    /// <summary>S-102 cells shallower than the safety contour inside the cross-track limits, off the track. CHECK.</summary>
    public static RouteFindingCode S102ShoalInCorridor { get; } = new("route_check.s102_shoal_in_corridor", RouteFindingCategory.Depth, RouteFindingGroup.Check);

    /// <summary>
    /// The track enters an S-101 area whose <c>restriction</c> is entry
    /// prohibited (7) or entry restricted (8), in force while the vessel is
    /// there. MUST REVIEW.
    /// </summary>
    public static RouteFindingCode EntersProhibitedArea { get; } = new("route_check.enters_prohibited_area", RouteFindingCategory.Area, RouteFindingGroup.MustReview, 2);

    /// <summary>Such an area reaches inside the cross-track limits, but the track doesn't enter it. CHECK.</summary>
    public static RouteFindingCode ProhibitedAreaInCorridor { get; } = new("route_check.prohibited_area_in_corridor", RouteFindingCategory.Area, RouteFindingGroup.Check, 2);

    /// <summary>
    /// An S-101 <c>RestrictedArea</c> or <c>MilitaryPracticeArea</c> with
    /// other restrictions. CHECK, or INFO when its restrictions don't bear on
    /// a passage (such as fishing or anchoring prohibited) or it only
    /// reaches inside the cross-track limits.
    /// </summary>
    public static RouteFindingCode RestrictedArea { get; } = new("route_check.restricted_area", RouteFindingCategory.Area, RouteFindingGroup.Check, 2);

    /// <summary>An S-101 <c>CautionArea</c>. CHECK, or INFO when it only reaches inside the cross-track limits.</summary>
    public static RouteFindingCode CautionArea { get; } = new("route_check.caution_area", RouteFindingCategory.Area, RouteFindingGroup.Check, 2);

    /// <summary>
    /// An S-101 traffic area: a separation zone, precautionary area or
    /// submarine transit lane (CHECK), or an inshore traffic zone (INFO).
    /// INFO when it only reaches inside the cross-track limits.
    /// </summary>
    public static RouteFindingCode TrafficArea { get; } = new("route_check.traffic_area", RouteFindingCategory.Area, RouteFindingGroup.Check, 2);

    /// <summary>An S-101 <c>AnchorageArea</c>. CHECK, or INFO when it only reaches inside the cross-track limits.</summary>
    public static RouteFindingCode AnchorageArea { get; } = new("route_check.anchorage_area", RouteFindingCategory.Area, RouteFindingGroup.Check, 2);

    /// <summary>
    /// An S-122 <c>MarineProtectedArea</c> or <c>RestrictedArea</c>. CHECK,
    /// or INFO when its restrictions don't bear on a passage or it only
    /// reaches inside the cross-track limits.
    /// </summary>
    public static RouteFindingCode ProtectedArea { get; } = new("route_check.protected_area", RouteFindingCategory.Area, RouteFindingGroup.Check, 2);

    /// <summary>The track or corridor meets an area that isn't in force while the vessel is there. INFO.</summary>
    public static RouteFindingCode AreaNotInForce { get; } = new("route_check.area_not_in_force", RouteFindingCategory.Area, RouteFindingGroup.Info, 2);

    /// <summary>
    /// The route meets areas of a kind whose indication is off
    /// (<see cref="RouteCheckSettings.AreaKindsOff"/>, #1127). INFO.
    /// </summary>
    public static RouteFindingCode AreaKindOff { get; } = new("route_check.area_kind_off", RouteFindingCategory.Area, RouteFindingGroup.Info, 2);

    /// <summary>An S-124 warning's line or area crosses the corridor, and it is in force while the vessel is there. MUST REVIEW.</summary>
    public static RouteFindingCode WarningOnRoute { get; } = new("s124.on_route", RouteFindingCategory.Warning, RouteFindingGroup.MustReview, 5);

    /// <summary>An S-124 warning is within the near distance of the track, and in force while the vessel is there. CHECK.</summary>
    public static RouteFindingCode WarningNearRoute { get; } = new("s124.near_route", RouteFindingCategory.Warning, RouteFindingGroup.Check);

    /// <summary>An S-124 warning is on or near the route, but not in force while the vessel is there. INFO.</summary>
    public static RouteFindingCode WarningNotInForce { get; } = new("s124.not_in_force", RouteFindingCategory.Warning, RouteFindingGroup.Info);

    /// <summary>An S-124 warning is on or near the route, but cancelled by the time the vessel is there. INFO.</summary>
    public static RouteFindingCode WarningCancelled { get; } = new("s124.cancelled", RouteFindingCategory.Warning, RouteFindingGroup.Info);

    /// <summary>A stretch where the clearance is below your UKC setting. MUST REVIEW.</summary>
    public static RouteFindingCode BelowSetting { get; } = new("clearance.below_setting", RouteFindingCategory.Clearance, RouteFindingGroup.MustReview, 4);

    /// <summary>A stretch where the charted depth comes from S-101 because no S-102 covers it. CHECK.</summary>
    public static RouteFindingCode ChartedDepthFromS101 { get; } = new("confidence.charted_depth_from_s101", RouteFindingCategory.Confidence, RouteFindingGroup.Check);

    /// <summary>A stretch where a depth or water-level datum isn't declared, so the two can't be confirmed to match. CHECK.</summary>
    public static RouteFindingCode DatumUnconfirmed { get; } = new("confidence.datum_unconfirmed", RouteFindingCategory.Confidence, RouteFindingGroup.Check);

    /// <summary>The water-level data ends before the vessel reaches a stretch, so the tide isn't applied there. CHECK.</summary>
    public static RouteFindingCode WaterLevelEnds { get; } = new("data.water_level_ends", RouteFindingCategory.Data, RouteFindingGroup.Check);

    /// <summary>The water-level data starts after the vessel passes a stretch. CHECK.</summary>
    public static RouteFindingCode WaterLevelStartsLate { get; } = new("data.water_level_starts_late", RouteFindingCategory.Data, RouteFindingGroup.Check);

    /// <summary>A stretch outside every water-level dataset's coverage. CHECK.</summary>
    public static RouteFindingCode NoWaterLevelCoverage { get; } = new("data.no_water_level_coverage", RouteFindingCategory.Data, RouteFindingGroup.Check);

    /// <summary>A stretch inside water-level coverage where the data has no value. CHECK.</summary>
    public static RouteFindingCode WaterLevelNoData { get; } = new("data.water_level_no_data", RouteFindingCategory.Data, RouteFindingGroup.Check);

    /// <summary>A stretch where the water level's datum differs from the charted depth's, so it isn't applied. CHECK.</summary>
    public static RouteFindingCode DatumMismatch { get; } = new("data.datum_mismatch", RouteFindingCategory.Data, RouteFindingGroup.Check);

    /// <summary>A stretch with no charted depth from S-102 or S-101. CHECK.</summary>
    public static RouteFindingCode NoChartedDepth { get; } = new("data.no_charted_depth", RouteFindingCategory.Data, RouteFindingGroup.Check);

    /// <summary>Every code, by category and then as listed here.</summary>
    public static IReadOnlyList<RouteFindingCode> All { get; } =
    [
        CrossesLand, LandInCorridor, CrossesDrying, DryingInCorridor,
        DangerInCorridor, DangerNearRoute, AidToNavigation, StructureOnTrack, StructureInCorridor, OverheadOnRoute,
        CrossesSafetyContour, SafetyContourInCorridor,
        SoundingsBelowSafetyContour, CrossesS102Shoal, S102ShoalInCorridor,
        EntersProhibitedArea, ProhibitedAreaInCorridor, RestrictedArea, CautionArea,
        TrafficArea, AnchorageArea, ProtectedArea, AreaNotInForce, AreaKindOff,
        WarningOnRoute, WarningNearRoute, WarningNotInForce, WarningCancelled,
        BelowSetting,
        ChartedDepthFromS101, DatumUnconfirmed,
        OutsideChartCoverage, WaterLevelEnds, WaterLevelStartsLate, NoWaterLevelCoverage,
        WaterLevelNoData, DatumMismatch, NoChartedDepth,
    ];

    private static readonly Dictionary<string, RouteFindingCode> ByValue = All.ToDictionary(c => c.Value, StringComparer.Ordinal);

    /// <summary>Finds a code by its value.</summary>
    /// <param name="value">The code, e.g. <c>route_check.crosses_land</c>.</param>
    /// <param name="code">The code, when found.</param>
    /// <returns>Whether the catalogue has it.</returns>
    public static bool TryGet(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RouteFindingCode? code)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ByValue.TryGetValue(value, out code);
    }
}
