using System.ComponentModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// The numbers behind a route finding (<see cref="RouteFinding.Details"/>):
/// one of its members is set, by the kind of finding.
/// </summary>
[Description("The numbers behind a finding, so they needn't be read from its text. One member is set, by the kind of finding.")]
public sealed record CheckRouteDetails(
    [property: Description("A charted danger in or near the corridor (route_check.danger_in_corridor, route_check.danger_near_route).")] CheckRouteDangerDetails? Danger = null,
    [property: Description("Water shallower than a safety setting: depth areas, soundings and S-102 shoals.")] CheckRouteShoalDetails? Shoal = null,
    [property: Description("A regulated area: what it is, what it restricts and when it's in force.")] CheckRouteAreaDetails? Area = null,
    [property: Description("A finding of the clearance check: a stretch below your UKC setting, of lower confidence or of missing data.")] CheckRouteClearanceDetails? Clearance = null)
{
    /// <summary>
    /// Projects a finding's details, or returns <c>null</c> when it has none
    /// or they are an S-124 warning's (which <c>warning</c> carries).
    /// </summary>
    /// <param name="details">The details.</param>
    /// <returns>The DTO, or <c>null</c>.</returns>
    public static CheckRouteDetails? From(RouteFindingDetails? details) => details switch
    {
        DangerDetails d => new(Danger: new CheckRouteDangerDetails(
            d.FeatureType,
            d.Depth?.TotalMetres,
            d.WaterLevelEffect,
            d.ThresholdKind is { } kind ? Threshold(kind) : null,
            d.Threshold?.TotalMetres,
            d.Side switch
            {
                RouteFindingSide.Port => "port",
                RouteFindingSide.Starboard => "starboard",
                _ => "on_track",
            },
            Math.Round(d.Offset.TotalMetres, 1))),
        ShoalDetails s => new(Shoal: new CheckRouteShoalDetails(
            s.LeastDepth.TotalMetres,
            Threshold(s.ThresholdKind),
            s.Threshold.TotalMetres,
            s.SoundingCount,
            s.Uncertainty?.TotalMetres,
            s.DatasetId,
            s.Cell)),
        AreaDetails a => new(Area: new CheckRouteAreaDetails(
            a.Product == RouteAreaProduct.S122 ? "S-122" : "S-101",
            Snake(a.Kind.ToString()),
            a.FeatureType,
            a.Name,
            a.Restrictions,
            a.Categories,
            a.CategoryOfMarineProtectedArea,
            a.FixedDateRange is { } range ? DateRange(range) : null,
            a.PeriodicDateRanges.Select(DateRange).ToList(),
            a.Relation == RouteAreaRelation.Enters ? "enters" : "in_corridor",
            a.InForce,
            a.From.ToUniversalTime(),
            a.Until.ToUniversalTime(),
            a.AtEta)),
        ClearanceDetails c => new(Clearance: new CheckRouteClearanceDetails(
            c.Stretch is { } stretch ? Snake(stretch.ToString()) : "below_setting",
            c.FirstLegIndex,
            c.LastLegIndex,
            Math.Round(c.Length.TotalNauticalMiles, 4),
            c.StartTime.ToUniversalTime(),
            c.MinimumClearance?.TotalMetres,
            c.RequiredUkc?.TotalMetres,
            c.Margin?.TotalMetres,
            c.MinimumTime?.ToUniversalTime(),
            c.MinutesBelow is { } minutes ? Math.Round(minutes, 1) : null)),
        _ => null,
    };

    private static string Threshold(RouteFindingThreshold kind) => kind switch
    {
        RouteFindingThreshold.Keel => "keel",
        RouteFindingThreshold.SafetyDepth => "safety_depth",
        _ => "safety_contour",
    };

    private static CheckRouteDateRange DateRange(RouteAreaDateRange range) =>
        new(range.DateStart, range.DateEnd, range.TimeOfDayStart, range.TimeOfDayEnd);

    // "MilitaryPracticeArea" → "military_practice_area".
    private static string Snake(string name) => string.Concat(name.Select((ch, i) =>
        char.IsUpper(ch) ? (i > 0 ? "_" : string.Empty) + char.ToLowerInvariant(ch) : ch.ToString()));
}

/// <summary>A charted danger behind a finding.</summary>
public sealed record CheckRouteDangerDetails(
    [property: Description("Its feature type: Wreck, Obstruction or UnderwaterAwashRock.")] string FeatureType,
    [property: Description("Its charted depth (valueOfSounding) in metres, or null when unknown.")] double? DepthMeters,
    [property: Description("Its waterLevelEffect code when its depth is unknown (1 or 2 above water, 4 covers and uncovers, 5 awash), or null.")] int? WaterLevelEffect,
    [property: Description("The threshold its depth is shallower than: safety_depth or keel (the leg's draft + squat + required UKC); null when its depth is unknown, which is always a danger.")] string? Threshold,
    [property: Description("The threshold in metres, or null.")] double? ThresholdMeters,
    [property: Description("Which side of the track it's on: on_track, port or starboard.")] string Side,
    [property: Description("How far it is from the track, metres.")] double OffsetMeters);

/// <summary>Shoal water behind a finding.</summary>
public sealed record CheckRouteShoalDetails(
    [property: Description("The least depth in the span, metres.")] double LeastDepthMeters,
    [property: Description("The setting it's shallower than: safety_contour or safety_depth.")] string Threshold,
    [property: Description("The setting in metres.")] double ThresholdMeters,
    [property: Description("How many soundings the finding stands for, or null when it isn't about soundings.")] int? SoundingCount,
    [property: Description("For an S-102 shoal, the uncertainty of the least depth in metres, or null.")] double? UncertaintyMeters,
    [property: Description("For an S-102 shoal, the dataset of the least depth, or null.")] string? DatasetId,
    [property: Description("For an S-102 shoal, the grid cell of the least depth (\"row,col\"), or null.")] string? Cell);

/// <summary>A regulated area behind a finding.</summary>
public sealed record CheckRouteAreaDetails(
    [property: Description("The product: S-101 or S-122.")] string Product,
    [property: Description("What it is: restricted_area, military_practice_area, caution_area, anchorage_area, separation_zone, precautionary_area, inshore_traffic_zone, submarine_transit_lane or marine_protected_area.")] string Kind,
    [property: Description("Its feature type, e.g. RestrictedArea.")] string FeatureType,
    [property: Description("Its name, or null.")] string? Name,
    [property: Description("Its restriction codes (S-101/S-122 Feature Catalogue), e.g. 7 entry prohibited, 14 area to be avoided.")] IReadOnlyList<int> Restrictions,
    [property: Description("Its category codes: categoryOfRestrictedArea, categoryOfMilitaryPracticeArea or categoryOfAnchorage, by kind.")] IReadOnlyList<int> Categories,
    [property: Description("An S-122 area's categoryOfMarineProtectedArea (IUCN category code), or null.")] int? IucnCategory,
    [property: Description("When it's valid (fixedDateRange), or null for always.")] CheckRouteDateRange? FixedDateRange,
    [property: Description("When it's active each year (periodicDateRange); empty for all year.")] IReadOnlyList<CheckRouteDateRange> PeriodicDateRanges,
    [property: Description("How it relates to the route: enters (the track enters it) or in_corridor (it only reaches inside the cross-track limits).")] string Relation,
    [property: Description("Whether it's in force while the vessel is there.")] bool InForce,
    [property: Description("When the vessel reaches it, ISO 8601 UTC: the ETA, or the check time when atEta is false.")] DateTimeOffset From,
    [property: Description("When the vessel leaves it, ISO 8601 UTC.")] DateTimeOffset Until,
    [property: Description("True when from and until are ETAs.")] bool AtEta);

/// <summary>An S-100 date range, as the dataset writes it.</summary>
public sealed record CheckRouteDateRange(
    [property: Description("dateStart (an S100_TruncatedDate, e.g. 2026-03-01 or --03-01), or null when open.")] string? DateStart,
    [property: Description("dateEnd, or null when open.")] string? DateEnd,
    [property: Description("timeOfDayStart, or null.")] string? TimeOfDayStart,
    [property: Description("timeOfDayEnd, or null.")] string? TimeOfDayEnd);

/// <summary>A clearance-check stretch behind a finding.</summary>
public sealed record CheckRouteClearanceDetails(
    [property: Description("The stretch: below_setting, or for confidence and data findings charted_depth_from_s101, datum_unconfirmed, water_level_after_end, water_level_before_start, no_water_level_coverage, water_level_no_data, datum_mismatch or no_charted_depth.")] string Stretch,
    [property: Description("Zero-based first leg of the stretch.")] int FirstLegIndex,
    [property: Description("Zero-based last leg of the stretch.")] int LastLegIndex,
    [property: Description("The stretch's length, nautical miles.")] double LengthNm,
    [property: Description("When the vessel reaches the stretch, ISO 8601 UTC (the departure without ETAs).")] DateTimeOffset StartTime,
    [property: Description("Below the setting: the least clearance in metres, or null.")] double? MinimumClearanceMeters,
    [property: Description("Below the setting: the required UKC where the margin is least, metres.")] double? RequiredUkcMeters,
    [property: Description("Below the setting: the least clearance minus the required UKC, metres (negative).")] double? MarginMeters,
    [property: Description("Below the setting: when the margin is least, ISO 8601 UTC, or null without an ETA.")] DateTimeOffset? MinimumTime,
    [property: Description("Below the setting: minutes below it, or null when part of the stretch has no ETA.")] double? MinutesBelow);
