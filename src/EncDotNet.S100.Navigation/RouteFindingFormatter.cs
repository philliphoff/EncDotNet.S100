using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Renders a <see cref="RouteFinding"/>'s <see cref="RouteFinding.Title"/>
/// and <see cref="RouteFinding.Meta"/> from its code and
/// <see cref="RouteFinding.Details"/>.
/// </summary>
/// <remarks>
/// <para>
/// The default wording is English, with numbers in <see cref="Culture"/>
/// (invariant unless given), depths in <see cref="DepthUnit"/> and times in
/// UTC (or <see cref="TimeZone"/>). A host with its own resources subclasses
/// it and overrides the wording; SoundCharts reads its <c>Strings.resx</c>.
/// Every phrase is a virtual member, and so are the pieces that format
/// depths, times and legs, and <see cref="Title"/> and <see cref="Meta"/>
/// themselves.
/// </para>
/// <para>
/// The checks render with the formatter in their options. To show findings
/// in another depth unit or time format, render them again with
/// <see cref="Render(RouteFinding)"/>; there is no need to check again.
/// </para>
/// <para>
/// Phrases that take a composite format are named <c>…Format</c>; their
/// arguments are listed on each. A depth argument is already formatted with
/// its unit (<see cref="FormatDepth"/>), and so is a time.
/// </para>
/// </remarks>
public class RouteFindingFormatter
{
    /// <summary>English, invariant culture, metres, UTC.</summary>
    public static RouteFindingFormatter Default { get; } = new();

    /// <summary>Creates a formatter.</summary>
    /// <param name="culture">The culture numbers and dates are formatted in; <c>null</c> for the invariant culture.</param>
    /// <param name="depthUnit">The unit depths are shown in.</param>
    /// <param name="timeZone">The zone times are shown in; <c>null</c> for UTC, marked "UTC".</param>
    public RouteFindingFormatter(CultureInfo? culture = null, DepthUnit depthUnit = DepthUnit.Metres, TimeZoneInfo? timeZone = null)
    {
        Culture = culture ?? CultureInfo.InvariantCulture;
        DepthUnit = depthUnit;
        TimeZone = timeZone;
    }

    /// <summary>The culture numbers and dates are formatted in.</summary>
    public CultureInfo Culture { get; }

    /// <summary>The unit depths are shown in.</summary>
    public DepthUnit DepthUnit { get; }

    /// <summary>The zone times are shown in, or <c>null</c> for UTC.</summary>
    public TimeZoneInfo? TimeZone { get; }

    /// <summary>A copy of a finding with its title and meta line rendered by this formatter.</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The copy.</returns>
    public RouteFinding Render(RouteFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.WithText(Title(finding), Meta(finding));
    }

    /// <summary>Copies of findings with their text rendered by this formatter, in the same order.</summary>
    /// <param name="findings">The findings.</param>
    /// <returns>The copies.</returns>
    public IReadOnlyList<RouteFinding> Render(IEnumerable<RouteFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings.Select(Render).ToList();
    }

    /// <summary>A finding's title, e.g. "Crosses drying ground".</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The title.</returns>
    public virtual string Title(RouteFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.Category switch
        {
            RouteFindingCategory.Area when finding.Details is AreaDetails area => AreaTitle(finding, area),
            RouteFindingCategory.Warning when finding.Details is WarningDetails warning => WarningTitle(finding, warning),
            RouteFindingCategory.Clearance or RouteFindingCategory.Confidence => ClearanceTitle(finding),
            RouteFindingCategory.Data when finding.Code != RouteFindingCodes.OutsideChartCoverage => ClearanceTitle(finding),
            _ => ChartTitle(finding),
        };
    }

    /// <summary>A finding's meta line: where and why, e.g. "Leg 2 → 3 · 0.10 NM · DepthArea".</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The meta line.</returns>
    public virtual string Meta(RouteFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return finding.Category switch
        {
            RouteFindingCategory.Area when finding.Details is AreaDetails area => AreaMeta(finding, area),
            RouteFindingCategory.Warning when finding.Details is WarningDetails warning => WarningMeta(warning),
            RouteFindingCategory.Clearance or RouteFindingCategory.Confidence => ClearanceMeta(finding),
            RouteFindingCategory.Data when finding.Code != RouteFindingCodes.OutsideChartCoverage => ClearanceMeta(finding),
            _ => ChartMeta(finding),
        };
    }

    // ---- Shared pieces ----

    /// <summary>Joins the parts of a meta line.</summary>
    protected virtual string Separator => " · ";

    /// <summary>Joins several restrictions or categories.</summary>
    protected virtual string ListSeparator => ", ";

    /// <summary>A leg, from its first waypoint number ({0}) to its last ({1}).</summary>
    protected virtual string LegFormat => "Leg {0} → {1}";

    /// <summary>Several legs, from the first waypoint number ({0}) to the last ({1}).</summary>
    protected virtual string LegsFormat => "Legs {0} → {1}";

    /// <summary>A length in nautical miles ({0}), as a chart finding's span.</summary>
    protected virtual string LengthFormat => "{0:0.00} NM";

    /// <summary>"Leg 6 → 7", or "Legs 5 → 7" for a span over more than one leg.</summary>
    /// <param name="firstLeg">The first leg's index.</param>
    /// <param name="lastLeg">The last leg's index.</param>
    /// <returns>The label.</returns>
    protected virtual string LegLabel(int firstLeg, int lastLeg) => firstLeg == lastLeg
        ? Format(LegFormat, firstLeg + 1, firstLeg + 2)
        : Format(LegsFormat, firstLeg + 1, lastLeg + 2);

    /// <summary>A depth with its unit, to a tenth: "7.6 m", "24.9 ft".</summary>
    /// <param name="depth">The depth.</param>
    /// <returns>The text.</returns>
    protected virtual string FormatDepth(Depth depth) => DepthText(depth, "0.0");

    /// <summary>A setting's depth with its unit, without a trailing zero: "10 m", "12.5 m".</summary>
    /// <param name="depth">The depth.</param>
    /// <returns>The text.</returns>
    protected virtual string FormatSetting(Depth depth) => DepthText(depth, "0.#");

    /// <summary>A time, such as a validity or an ETA: <c>yyyy-MM-dd HH:mm UTC</c>, or in <see cref="TimeZone"/>.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The text.</returns>
    protected virtual string FormatTime(DateTimeOffset time) => TimeZone is { } zone
        ? TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd HH:mm", Culture)
        : time.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>
    /// When the vessel is somewhere: an ETA, or the time a finding is judged
    /// at. <see cref="FormatTime"/> by default; a host can show it more
    /// compactly against the departure.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The text.</returns>
    protected virtual string FormatEta(DateTimeOffset time) => FormatTime(time);

    /// <summary>A day of a fixed date range: <c>d MMM yyyy</c>.</summary>
    /// <param name="date">The day.</param>
    /// <returns>The text.</returns>
    protected virtual string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", Culture);

    /// <summary>A day of a season: <c>d MMM</c>.</summary>
    /// <param name="month">The month.</param>
    /// <param name="day">The day.</param>
    /// <returns>The text.</returns>
    protected virtual string FormatDayOfYear(int month, int day) => new DateTime(2000, month, day).ToString("d MMM", Culture);

    /// <summary>Formats a phrase in <see cref="Culture"/>.</summary>
    /// <param name="format">The composite format.</param>
    /// <param name="args">Its arguments.</param>
    /// <returns>The text.</returns>
    protected string Format(string format, params object?[] args) => string.Format(Culture, format, args);

    // ---- Chart and depth findings ----

    /// <summary>The title of <see cref="RouteFindingCodes.CrossesLand"/>.</summary>
    protected virtual string CrossesLand => "Crosses land";

    /// <summary>The title of <see cref="RouteFindingCodes.LandInCorridor"/>.</summary>
    protected virtual string LandInCorridor => "Land inside the cross-track limits";

    /// <summary>The title of <see cref="RouteFindingCodes.CrossesDrying"/>.</summary>
    protected virtual string CrossesDrying => "Crosses drying ground";

    /// <summary>The title of <see cref="RouteFindingCodes.DryingInCorridor"/>.</summary>
    protected virtual string DryingInCorridor => "Drying ground inside the cross-track limits";

    /// <summary>The title of <see cref="RouteFindingCodes.OutsideChartCoverage"/>.</summary>
    protected virtual string OutsideChartCoverage => "Leaves chart coverage";

    /// <summary>The title of <see cref="RouteFindingCodes.DangerInCorridor"/>: the danger's kind ({0}, such as <see cref="Wreck"/>).</summary>
    protected virtual string DangerInCorridorFormat => "{0} in the corridor";

    /// <summary>The title of <see cref="RouteFindingCodes.DangerNearRoute"/>: the danger's kind ({0}).</summary>
    protected virtual string DangerNearRouteFormat => "{0} near the route";

    /// <summary>The kind of a <c>Wreck</c>, in a danger's title.</summary>
    protected virtual string Wreck => "Wreck";

    /// <summary>The kind of an <c>Obstruction</c>, in a danger's title.</summary>
    protected virtual string Obstruction => "Obstruction";

    /// <summary>The kind of an <c>UnderwaterAwashRock</c>, in a danger's title.</summary>
    protected virtual string Rock => "Rock";

    /// <summary>The title of <see cref="RouteFindingCodes.CrossesSafetyContour"/>.</summary>
    protected virtual string CrossesSafetyContour => "Crosses the safety contour";

    /// <summary>The title of <see cref="RouteFindingCodes.SafetyContourInCorridor"/>.</summary>
    protected virtual string SafetyContourInCorridor => "Shallower than the safety contour inside the cross-track limits";

    /// <summary>The title of <see cref="RouteFindingCodes.SoundingsBelowSafetyDepth"/>.</summary>
    protected virtual string SoundingsBelowSafetyDepth => "Soundings shallower than the safety depth";

    /// <summary>The title of <see cref="RouteFindingCodes.CrossesS102Shoal"/>.</summary>
    protected virtual string CrossesS102Shoal => "Crosses S-102 depths shallower than the safety contour";

    /// <summary>The title of <see cref="RouteFindingCodes.S102ShoalInCorridor"/>.</summary>
    protected virtual string S102ShoalInCorridor => "S-102 depths shallower than the safety contour inside the cross-track limits";

    /// <summary>How far a danger is to port of the track, in metres ({0}).</summary>
    protected virtual string ToPortFormat => "{0:0} m to port";

    /// <summary>How far a danger is to starboard of the track, in metres ({0}).</summary>
    protected virtual string ToStarboardFormat => "{0:0} m to starboard";

    /// <summary>A danger the track passes over.</summary>
    protected virtual string OnTrack => "on the track";

    /// <summary>The least of several charted depths ({0}).</summary>
    protected virtual string LeastDepthFormat => "least {0}";

    /// <summary>A sounding count ({0}).</summary>
    protected virtual string SoundingCountFormat => "{0} soundings";

    /// <summary>An S-102 depth ({0}) with its uncertainty ({1}), and its grid cell ({2}, as <c>row,col</c>) in a dataset ({3}).</summary>
    protected virtual string S102CellWithUncertaintyFormat => "least {0} ± {1} at cell {2} of {3}";

    /// <summary>An S-102 depth ({0}) without an uncertainty, and its grid cell ({2}) in a dataset ({3}).</summary>
    protected virtual string S102CellFormat => "least {0} at cell {2} of {3}";

    /// <summary>A danger of unknown depth.</summary>
    protected virtual string DepthUnknown => "depth unknown";

    /// <summary>A danger that is awash (<c>waterLevelEffect</c> 5).</summary>
    protected virtual string Awash => "awash";

    /// <summary>A danger that covers and uncovers (<c>waterLevelEffect</c> 4).</summary>
    protected virtual string CoversAndUncovers => "covers and uncovers";

    /// <summary>A danger that shows above water (<c>waterLevelEffect</c> 1 or 2).</summary>
    protected virtual string AboveWater => "above water";

    /// <summary>The safety-depth threshold fired ({0}).</summary>
    protected virtual string BelowSafetyDepthFormat => "shallower than safety depth {0}";

    /// <summary>The draft + squat + required UKC threshold fired ({0}).</summary>
    protected virtual string BelowKeelFormat => "shallower than draft + squat + UKC ({0})";

    /// <summary>The safety-contour threshold fired ({0}).</summary>
    protected virtual string BelowSafetyContourFormat => "shallower than safety contour {0}";

    /// <summary>The title of a chart or depth finding.</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The title.</returns>
    protected virtual string ChartTitle(RouteFinding finding)
    {
        var code = finding.Code;
        if (code == RouteFindingCodes.DangerInCorridor || code == RouteFindingCodes.DangerNearRoute)
        {
            var type = (finding.Details as DangerDetails)?.FeatureType ?? finding.Feature?.FeatureType ?? string.Empty;
            return Format(code == RouteFindingCodes.DangerInCorridor ? DangerInCorridorFormat : DangerNearRouteFormat, DangerKind(type));
        }

        return code.Value switch
        {
            "route_check.crosses_land" => CrossesLand,
            "route_check.land_in_corridor" => LandInCorridor,
            "route_check.crosses_drying" => CrossesDrying,
            "route_check.drying_in_corridor" => DryingInCorridor,
            "data.outside_chart_coverage" => OutsideChartCoverage,
            "route_check.crosses_safety_contour" => CrossesSafetyContour,
            "route_check.safety_contour_in_corridor" => SafetyContourInCorridor,
            "route_check.soundings_below_safety_depth" => SoundingsBelowSafetyDepth,
            "route_check.crosses_s102_shoal" => CrossesS102Shoal,
            "route_check.s102_shoal_in_corridor" => S102ShoalInCorridor,
            _ => code.Value,
        };
    }

    /// <summary>
    /// The meta line of a chart or depth finding: the leg, the span's length,
    /// the feature type, and what fired.
    /// </summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The meta line.</returns>
    protected virtual string ChartMeta(RouteFinding finding)
    {
        var parts = new List<string>(5);
        if (finding.LegIndex is { } leg)
        {
            parts.Add(LegLabel(leg, leg));
        }

        if (finding.Location is { } location && location.EndDistanceMeters - location.StartDistanceMeters is > 0 and var length)
        {
            parts.Add(Format(LengthFormat, length / PassageGeodesy.MetersPerNauticalMile));
        }

        if (finding.Feature?.FeatureType is { } featureType)
        {
            parts.Add(featureType);
        }

        switch (finding.Details)
        {
            case DangerDetails danger:
                parts.Add(danger.Side == RouteFindingSide.OnTrack
                    ? OnTrack
                    : Format(danger.Side == RouteFindingSide.Port ? ToPortFormat : ToStarboardFormat, danger.Offset.TotalMetres));
                if (danger.Depth is not { } depth)
                {
                    parts.Add(danger.WaterLevelEffect switch
                    {
                        1 or 2 => AboveWater,
                        4 => CoversAndUncovers,
                        5 => Awash,
                        _ => DepthUnknown,
                    });
                }
                else
                {
                    parts.Add(FormatDepth(depth));
                    if (danger is { ThresholdKind: { } kind, Threshold: { } threshold })
                    {
                        parts.Add(Below(kind, threshold));
                    }
                }

                break;
            case ShoalDetails shoal when finding.Code == RouteFindingCodes.CrossesS102Shoal || finding.Code == RouteFindingCodes.S102ShoalInCorridor:
                parts.Add(Format(
                    shoal.Uncertainty is null ? S102CellFormat : S102CellWithUncertaintyFormat,
                    FormatDepth(shoal.LeastDepth),
                    shoal.Uncertainty is { } uncertainty ? FormatDepth(uncertainty) : null,
                    shoal.Cell,
                    shoal.DatasetId));
                parts.Add(Below(shoal.ThresholdKind, shoal.Threshold));
                break;
            case ShoalDetails shoal:
                if (shoal.SoundingCount is > 1 and var count)
                {
                    parts.Add(Format(SoundingCountFormat, count));
                }

                parts.Add(Format(LeastDepthFormat, FormatDepth(shoal.LeastDepth)));
                parts.Add(Below(shoal.ThresholdKind, shoal.Threshold));
                break;
        }

        return string.Join(Separator, parts);
    }

    /// <summary>A danger's kind in its title, from its feature type.</summary>
    /// <param name="featureType">The feature type.</param>
    /// <returns>The kind.</returns>
    protected virtual string DangerKind(string featureType) => featureType switch
    {
        "Wreck" => Wreck,
        "Obstruction" => Obstruction,
        "UnderwaterAwashRock" => Rock,
        _ => featureType,
    };

    private string Below(RouteFindingThreshold kind, Depth threshold) => Format(
        kind switch
        {
            RouteFindingThreshold.Keel => BelowKeelFormat,
            RouteFindingThreshold.SafetyDepth => BelowSafetyDepthFormat,
            _ => BelowSafetyContourFormat,
        },
        FormatSetting(threshold));

    // ---- Area findings ----

    /// <summary>Title when the track enters an area where entry is prohibited or restricted ({0}: the area's name or kind).</summary>
    protected virtual string AreaEntersFormat => "Enters {0}";

    /// <summary>Title when the track crosses any other area ({0}: the area's name or kind).</summary>
    protected virtual string AreaCrossesFormat => "Crosses {0}";

    /// <summary>Title when the area only reaches inside the cross-track limits ({0}: the area's name or kind).</summary>
    protected virtual string AreaInCorridorFormat => "{0} inside the cross-track limits";

    /// <summary>Title when the area isn't in force while the vessel is there, judged at ETAs ({0}: the area's name or kind).</summary>
    protected virtual string AreaNotInForceFormat => "{0} not in force during the passage";

    /// <summary>Title when the area isn't in force at the fallback time ({0}: the area's name or kind; {1}: the time).</summary>
    protected virtual string AreaNotInForceAtFormat => "{0} not in force at {1}";

    /// <summary>An IUCN category of a marine protected area ({0}: the category, such as <c>IV</c>).</summary>
    protected virtual string IucnCategoryFormat => "IUCN {0}";

    /// <summary>A code with no label ({0}: the code).</summary>
    protected virtual string UnknownCodeFormat => "code {0}";

    /// <summary>A season, date range or span of ETAs with both ends ({0} start, {1} end).</summary>
    protected virtual string RangeFormat => "{0}–{1}";

    /// <summary>A date range with no end ({0} start).</summary>
    protected virtual string FromFormat => "from {0}";

    /// <summary>A date range with no start ({0} end).</summary>
    protected virtual string UntilFormat => "until {0}";

    /// <summary>What an area is, in a title or meta line: "restricted area", "caution area", ….</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The label.</returns>
    protected virtual string AreaKindLabel(RouteAreaKind kind) => kind switch
    {
        RouteAreaKind.RestrictedArea => "restricted area",
        RouteAreaKind.MilitaryPracticeArea => "military practice area",
        RouteAreaKind.CautionArea => "caution area",
        RouteAreaKind.AnchorageArea => "anchorage area",
        RouteAreaKind.SeparationZone => "traffic separation zone",
        RouteAreaKind.PrecautionaryArea => "precautionary area",
        RouteAreaKind.InshoreTrafficZone => "inshore traffic zone",
        RouteAreaKind.SubmarineTransitLane => "submarine transit lane",
        _ => "marine protected area",
    };

    /// <summary>A <c>restriction</c> code's label (from the S-101 Edition 2.0.0 Feature Catalogue).</summary>
    /// <param name="code">The code.</param>
    /// <returns>The label.</returns>
    protected virtual string RestrictionLabel(int code) => Label(RouteAreaLabels.Restrictions, code);

    /// <summary>
    /// A category code's label: <c>categoryOfMilitaryPracticeArea</c>,
    /// <c>categoryOfAnchorage</c> or <c>categoryOfRestrictedArea</c>, by the
    /// area's kind (from the S-101 Edition 2.0.0 Feature Catalogue).
    /// </summary>
    /// <param name="kind">The area's kind.</param>
    /// <param name="code">The code.</param>
    /// <returns>The label.</returns>
    protected virtual string CategoryLabel(RouteAreaKind kind, int code) => Label(
        kind switch
        {
            RouteAreaKind.MilitaryPracticeArea => RouteAreaLabels.MilitaryPracticeAreaCategories,
            RouteAreaKind.AnchorageArea => RouteAreaLabels.AnchorageCategories,
            _ => RouteAreaLabels.RestrictedAreaCategories,
        },
        code);

    /// <summary>The title of an area finding.</summary>
    /// <param name="finding">The finding.</param>
    /// <param name="area">Its details.</param>
    /// <returns>The title.</returns>
    protected virtual string AreaTitle(RouteFinding finding, AreaDetails area)
    {
        var subject = string.IsNullOrWhiteSpace(area.Name) ? AreaKindLabel(area.Kind) : area.Name.Trim();
        var title = !area.InForce
            ? area.AtEta ? Format(AreaNotInForceFormat, subject) : Format(AreaNotInForceAtFormat, subject, FormatEta(area.From))
            : area.Relation == RouteAreaRelation.InCorridor ? Format(AreaInCorridorFormat, subject)
            : Format(finding.Code == RouteFindingCodes.EntersProhibitedArea ? AreaEntersFormat : AreaCrossesFormat, subject);
        title = title.Trim();
        return title.Length == 0 || char.IsUpper(title[0]) ? title : char.ToUpper(title[0], Culture) + title[1..];
    }

    /// <summary>
    /// The meta line of an area finding: "Leg 1 → 2 · S-122 marine protected
    /// area · speed restricted · seal sanctuary · IUCN IV · 1 Mar–31 May · 06:11–06:14".
    /// </summary>
    /// <param name="finding">The finding.</param>
    /// <param name="area">Its details.</param>
    /// <returns>The meta line.</returns>
    protected virtual string AreaMeta(RouteFinding finding, AreaDetails area)
    {
        var parts = new List<string>();
        if (finding.LegIndex is { } leg)
        {
            parts.Add(LegLabel(leg, leg));
        }

        var product = area.Product == RouteAreaProduct.S122 ? "S-122" : "S-101";
        parts.Add(string.IsNullOrWhiteSpace(area.Name) ? product : product + " " + AreaKindLabel(area.Kind));
        if (area.Restrictions.Count > 0)
        {
            parts.Add(string.Join(ListSeparator, area.Restrictions.Select(RestrictionLabel)));
        }

        var categories = area.Categories.Select(c => CategoryLabel(area.Kind, c)).ToList();
        if (area.CategoryOfMarineProtectedArea is { } iucn)
        {
            categories.Add(Format(IucnCategoryFormat, Iucn(iucn) ?? iucn.ToString(CultureInfo.InvariantCulture)));
        }

        if (categories.Count > 0)
        {
            parts.Add(string.Join(ListSeparator, categories));
        }

        if (area.FixedDateRange is { } range && DateRange(range, yearly: false) is { } fixedText)
        {
            parts.Add(fixedText);
        }

        var seasons = area.PeriodicDateRanges.Select(r => DateRange(r, yearly: true)).OfType<string>().ToList();
        if (seasons.Count > 0)
        {
            parts.Add(string.Join(ListSeparator, seasons));
        }

        if (area.AtEta)
        {
            parts.Add(area.Until - area.From < TimeSpan.FromSeconds(30)
                ? FormatEta(area.From)
                : Format(RangeFormat, FormatEta(area.From), FormatEta(area.Until)));
        }

        return string.Join(Separator, parts);
    }

    private string? DateRange(RouteAreaDateRange range, bool yearly)
    {
        string? Day(string? value)
        {
            if (yearly)
            {
                return AreaCheck.DayOfYear(value) is { } d ? FormatDayOfYear(d.Month, d.Day) : value;
            }

            return S100DateTimes.Combine(value, null) is { } moment ? FormatDate(DateOnly.FromDateTime(moment.UtcDateTime)) : value;
        }

        return (Day(range.DateStart), Day(range.DateEnd)) switch
        {
            ({ } s, { } e) => Format(RangeFormat, s, e),
            ({ } s, null) => Format(FromFormat, s),
            (null, { } e) => Format(UntilFormat, e),
            _ => null,
        };
    }

    private string Label(IReadOnlyDictionary<int, string> labels, int code) =>
        labels.TryGetValue(code, out var label) ? label : Format(UnknownCodeFormat, code);

    // The IUCN category of a categoryOfMarineProtectedArea code (1 Ia … 7 VI).
    private static string? Iucn(int code) => code switch
    {
        1 => "Ia",
        2 => "Ib",
        3 => "II",
        4 => "III",
        5 => "IV",
        6 => "V",
        7 => "VI",
        _ => null,
    };

    // ---- Warning findings ----

    /// <summary>Title when the route goes through the warning's area ({0}: the warning's number).</summary>
    protected virtual string WarningInsideAreaFormat => "Inside navigational warning {0}";

    /// <summary>Title when the route crosses the warning's line ({0}: the warning's number).</summary>
    protected virtual string WarningCrossesLineFormat => "Crosses navigational warning {0}";

    /// <summary>Title when the warning is near the route ({0}: the warning's number).</summary>
    protected virtual string WarningNearFormat => "Navigational warning {0} near the route";

    /// <summary>Title when the warning isn't in force while the vessel is there ({0}: the warning's number).</summary>
    protected virtual string WarningNotInForceFormat => "Navigational warning {0} not in force";

    /// <summary>Title when the warning is cancelled by then ({0}: the warning's number).</summary>
    protected virtual string WarningCancelledFormat => "Navigational warning {0} cancelled";

    /// <summary>
    /// Title when the latest in-force bulletin leaves the warning out
    /// ({0}: the warning's number, {1}: the bulletin's series and number).
    /// </summary>
    protected virtual string WarningNotInBulletinFormat => "Navigational warning {0} not in in-force bulletin {1}";

    /// <summary>The meta line's status when in force ({0}: the time).</summary>
    protected virtual string WarningInForceAtFormat => "In force at {0}";

    /// <summary>The meta line's status when not in force ({0}: the time).</summary>
    protected virtual string WarningNotInForceAtFormat => "Not in force at {0}";

    /// <summary>The meta line's status when cancelled.</summary>
    protected virtual string WarningCancelledStatus => "Cancelled";

    /// <summary>The meta line's status when the latest in-force bulletin leaves it out ({0}: the bulletin).</summary>
    protected virtual string WarningNotInBulletinStatusFormat => "Cancelled — not in in-force bulletin {0}";

    /// <summary>A validity range with both ends ({0} start, {1} end).</summary>
    protected virtual string ValidityRangeFormat => "{0}–{1}";

    /// <summary>A validity range with no end ({0} start).</summary>
    protected virtual string ValidityFromFormat => "from {0}";

    /// <summary>A validity range with no start ({0} end).</summary>
    protected virtual string ValidityUntilFormat => "until {0}";

    /// <summary>The validity when the part gives no range.</summary>
    protected virtual string ValidityOpen => "until cancelled";

    /// <summary>The title of an S-124 finding.</summary>
    /// <param name="finding">The finding.</param>
    /// <param name="warning">Its details.</param>
    /// <returns>The title.</returns>
    protected virtual string WarningTitle(RouteFinding finding, WarningDetails warning)
    {
        var number = warning.Info.Number ?? warning.Info.Title ?? string.Empty;
        var bulletin = Bulletin(warning);
        var code = finding.Code;
        var format = code == RouteFindingCodes.WarningOnRoute ? (warning.InsideArea ? WarningInsideAreaFormat : WarningCrossesLineFormat)
            : code == RouteFindingCodes.WarningNearRoute ? WarningNearFormat
            : code == RouteFindingCodes.WarningCancelled ? (bulletin is null ? WarningCancelledFormat : WarningNotInBulletinFormat)
            : WarningNotInForceFormat;
        return Format(format, number, bulletin).Trim();
    }

    /// <summary>The meta line of an S-124 finding: "NAVAREA I 012/26 · Dover Strait · 1 Mar 05:00–08:00 · In force at 06:08".</summary>
    /// <param name="warning">Its details.</param>
    /// <returns>The meta line.</returns>
    protected virtual string WarningMeta(WarningDetails warning)
    {
        var info = warning.Info;
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
            ? ValidityOpen
            : string.Join(", ", info.Validity.Select(r => (r.Start, r.End) switch
            {
                ({ } s, { } e) => Format(ValidityRangeFormat, FormatTime(s), FormatTime(e)),
                ({ } s, null) => Format(ValidityFromFormat, FormatTime(s)),
                (null, { } e) => Format(ValidityUntilFormat, FormatTime(e)),
                _ => ValidityOpen,
            })));
        var bulletin = Bulletin(warning);
        parts.Add(warning.Status switch
        {
            RouteWarningStatus.InForce => Format(WarningInForceAtFormat, FormatEta(warning.From)),
            RouteWarningStatus.NotInForce => Format(WarningNotInForceAtFormat, FormatEta(warning.From)),
            _ => bulletin is null ? WarningCancelledStatus : Format(WarningNotInBulletinStatusFormat, bulletin),
        });
        return string.Join(Separator, parts);
    }

    private static string? Bulletin(WarningDetails warning) =>
        warning.Withdrawal is { Reason: NavigationalWarningStatusReason.NotInBulletin, By: { } by }
            ? by.Label ?? by.Title ?? string.Empty
            : null;

    // ---- Clearance, confidence and data findings ----

    /// <summary>Title below your UKC setting, with ETAs ({0}: whole minutes).</summary>
    protected virtual string BelowMinutesFormat => "Below your UKC setting for {0} min";

    /// <summary>Title below your UKC setting, without ETAs ({0}: nautical miles).</summary>
    protected virtual string BelowDistanceFormat => "Below your UKC setting for {0:0.0} NM";

    /// <summary>The least clearance ({0}) and when ({1}).</summary>
    protected virtual string MinAtFormat => "min {0} at {1}";

    /// <summary>The least clearance ({0}) without an ETA.</summary>
    protected virtual string MinFormat => "min {0}";

    /// <summary>A clearance that isn't known.</summary>
    protected virtual string NoValue => "—";

    /// <summary>The title of <see cref="RouteFindingCodes.ChartedDepthFromS101"/>.</summary>
    protected virtual string ChartedDepthFromS101 => "Charted depth from S-101 only";

    /// <summary>The meta of <see cref="RouteFindingCodes.ChartedDepthFromS101"/> ({0}: nautical miles).</summary>
    protected virtual string OutsideS102Format => "{0:0.0} NM outside S-102 coverage";

    /// <summary>The title of <see cref="RouteFindingCodes.DatumUnconfirmed"/>.</summary>
    protected virtual string DatumUnconfirmed => "Vertical datums unconfirmed";

    /// <summary>The meta of <see cref="RouteFindingCodes.DatumUnconfirmed"/> ({0}: nautical miles).</summary>
    protected virtual string DatumUnconfirmedMetaFormat => "{0:0.0} NM · a depth or water-level datum isn't declared";

    /// <summary>The title of <see cref="RouteFindingCodes.WaterLevelEnds"/>.</summary>
    protected virtual string WaterLevelEnds => "Water-level data ends before arrival";

    /// <summary>The title of <see cref="RouteFindingCodes.WaterLevelStartsLate"/>.</summary>
    protected virtual string WaterLevelStartsLate => "Water-level data starts after departure";

    /// <summary>The title of <see cref="RouteFindingCodes.NoWaterLevelCoverage"/>.</summary>
    protected virtual string NoWaterLevelCoverage => "Outside water-level coverage";

    /// <summary>The title of <see cref="RouteFindingCodes.WaterLevelNoData"/>.</summary>
    protected virtual string WaterLevelNoData => "No water-level value";

    /// <summary>The title of <see cref="RouteFindingCodes.DatumMismatch"/>.</summary>
    protected virtual string DatumMismatch => "Water-level datum differs from the chart datum";

    /// <summary>The title of <see cref="RouteFindingCodes.NoChartedDepth"/>.</summary>
    protected virtual string NoChartedDepth => "No charted depth";

    /// <summary>When a gap starts ({0}: the time).</summary>
    protected virtual string StretchFromFormat => "from {0}";

    /// <summary>A stretch's length ({0}: nautical miles).</summary>
    protected virtual string StretchLengthFormat => "{0:0.0} NM";

    /// <summary>The tide isn't applied for a length ({0}: nautical miles).</summary>
    protected virtual string TideNotAppliedForFormat => "tide not applied for {0:0.0} NM";

    /// <summary>The tide isn't applied.</summary>
    protected virtual string TideNotApplied => "tide not applied";

    /// <summary>Why there's no charted depth.</summary>
    protected virtual string NoDepthSource => "no S-102 or S-101 coverage";

    /// <summary>The title of a clearance, confidence or data finding of the clearance check.</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The title.</returns>
    protected virtual string ClearanceTitle(RouteFinding finding)
    {
        var details = finding.Details as ClearanceDetails;
        return finding.Code.Value switch
        {
            "clearance.below_setting" => details?.MinutesBelow is { } minutes
                ? Format(BelowMinutesFormat, Math.Max(1, (int)Math.Round(minutes)))
                : Format(BelowDistanceFormat, Nm(details?.Length ?? Length.Zero)),
            "confidence.charted_depth_from_s101" => ChartedDepthFromS101,
            "confidence.datum_unconfirmed" => DatumUnconfirmed,
            "data.water_level_ends" => WaterLevelEnds,
            "data.water_level_starts_late" => WaterLevelStartsLate,
            "data.no_water_level_coverage" => NoWaterLevelCoverage,
            "data.water_level_no_data" => WaterLevelNoData,
            "data.datum_mismatch" => DatumMismatch,
            "data.no_charted_depth" => NoChartedDepth,
            _ => finding.Code.Value,
        };
    }

    /// <summary>The meta line of a clearance, confidence or data finding of the clearance check.</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The meta line.</returns>
    protected virtual string ClearanceMeta(RouteFinding finding)
    {
        if (finding.Details is not ClearanceDetails details)
        {
            return finding.LegIndex is { } only ? LegLabel(only, only) : string.Empty;
        }

        if (finding.Code == RouteFindingCodes.BelowSetting)
        {
            var leg = finding.LegIndex ?? details.FirstLegIndex;
            var clearance = details.MinimumClearance is { } c ? FormatDepth(c) : NoValue;
            var min = details.MinimumTime is { } at ? Format(MinAtFormat, clearance, FormatEta(at)) : Format(MinFormat, clearance);
            return Join(LegLabel(leg, leg), min);
        }

        var legs = LegLabel(details.FirstLegIndex, details.LastLegIndex);
        var nm = Nm(details.Length);
        var length = Format(StretchLengthFormat, nm);
        return finding.Code.Value switch
        {
            "confidence.charted_depth_from_s101" => Join(legs, Format(OutsideS102Format, nm)),
            "confidence.datum_unconfirmed" => Join(legs, Format(DatumUnconfirmedMetaFormat, nm)),
            "data.water_level_ends" => Join(legs, Format(StretchFromFormat, FormatEta(details.StartTime)), Format(TideNotAppliedForFormat, nm)),
            "data.water_level_starts_late" => Join(legs, Format(TideNotAppliedForFormat, nm)),
            "data.no_charted_depth" => Join(legs, length, NoDepthSource),
            _ => Join(legs, length, TideNotApplied),
        };
    }

    private string Join(params string?[] parts) =>
        string.Join(Separator, parts.Where(p => !string.IsNullOrEmpty(p)));

    private static double Nm(Length length) => length.TotalMetres / PassageGeodesy.MetersPerNauticalMile;

    private string DepthText(Depth depth, string format)
    {
        if (DepthUnit == DepthUnit.FathomsFeet)
        {
            return DepthFormatting.Format(depth.TotalMetres, DepthUnit);
        }

        return DepthFormatting.ToDisplay(depth.TotalMetres, DepthUnit).ToString(format, Culture) + " " + DepthFormatting.UnitAbbreviation(DepthUnit);
    }
}
