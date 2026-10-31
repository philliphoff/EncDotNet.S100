using System.ComponentModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Result of <see cref="CheckRouteTool"/>: a <see cref="RouteCheckReport"/> as JSON.</summary>
public sealed record CheckRouteResult(
    [property: Description("Id of the checked route, or null for a polyline.")] string? RouteId,
    [property: Description("Name of the checked route, or null.")] string? RouteName,
    [property: Description("Route length in nautical miles.")] double DistanceNm,
    [property: Description("The one answer: the worst thing the checks found, and the finding that names it.")] CheckRouteVerdict Verdict,
    [property: Description("Counts by group, and the summary line SoundCharts shows.")] CheckRouteSummary Summary,
    [property: Description("Whether each check ran, in the order they ran: chart, areas, warnings and clearance.")] IReadOnlyList<CheckRouteCheck> Checks,
    [property: Description("Every check's findings, by group (must_review, check, info) and then by distance along the route. Codes are the same as get_findings: route_check.crosses_land, route_check.crosses_drying, route_check.land_in_corridor, route_check.drying_in_corridor, route_check.danger_in_corridor, route_check.danger_near_route, route_check.aid_to_navigation, route_check.structure_on_track, route_check.structure_in_corridor, route_check.overhead_on_route, route_check.crosses_safety_contour, route_check.safety_contour_in_corridor, route_check.soundings_below_safety_contour, route_check.crosses_s102_shoal, route_check.s102_shoal_in_corridor, data.outside_chart_coverage; the area checks' route_check.enters_prohibited_area, route_check.prohibited_area_in_corridor, route_check.restricted_area, route_check.caution_area, route_check.traffic_area, route_check.anchorage_area, route_check.protected_area (S-122) and route_check.area_not_in_force; the S-124 warnings' s124.on_route, s124.near_route, s124.not_in_force and s124.cancelled; and the clearance check's clearance.below_setting, confidence.charted_depth_from_s101, confidence.datum_unconfirmed, data.water_level_ends, data.water_level_starts_late, data.no_water_level_coverage, data.water_level_no_data, data.datum_mismatch and data.no_charted_depth.")] IReadOnlyList<CheckRouteFinding> Findings,
    [property: Description("The corridor swept along each leg, in route order.")] IReadOnlyList<CheckRouteLeg> Legs,
    [property: Description("The under-keel clearance: verdict, minimum, sources and, with includeSamples or includeWindow, the detail. Null when the clearance check didn't run (see checks: no draft, no charted depth or no water level).")] CheckRouteClearance? Clearance,
    [property: Description("The datasets behind the result: S-101 (and S-401, and translated S-57) cells near the route, the S-122 and S-124 datasets behind area and warning findings, and the S-102, S-101 and S-104 datasets the clearance read.")] IReadOnlyList<string> Datasets,
    [property: Description("Disclaimer to show once with the findings.")] string Disclaimer)
{
    /// <summary>The disclaimer shown once with the findings, before the clearance's sources.</summary>
    public const string DisclaimerText = "Not for navigation · checked against the loaded S-101 charts, S-122 protected areas and S-124 warnings only";

    /// <summary>Projects a report.</summary>
    /// <param name="report">The report.</param>
    /// <param name="routeId">The route's id, or <c>null</c> for a polyline.</param>
    /// <param name="routeName">The route's name, or <c>null</c>.</param>
    /// <param name="includeSamples">Include the clearance's per-leg results and samples.</param>
    /// <param name="maxSamples">The most samples to include; extra samples are thinned, keeping the tightest of each run.</param>
    /// <returns>The result.</returns>
    public static CheckRouteResult From(RouteCheckReport report, string? routeId = null, string? routeName = null, bool includeSamples = false, int maxSamples = 500)
    {
        ArgumentNullException.ThrowIfNull(report);
        var counts = report.Counts;
        var clearance = report.Clearance is { } result ? CheckRouteClearance.From(report, result, includeSamples, maxSamples) : null;
        return new CheckRouteResult(
            routeId,
            routeName,
            Nm(report.Track.LengthMeters),
            CheckRouteVerdict.From(report),
            new CheckRouteSummary(report.Findings.Count, counts.MustReview, counts.Check, counts.Info, SummaryText(counts.MustReview, counts.Check)),
            report.Checks.Select(c => new CheckRouteCheck(
                c.Name,
                c.State switch
                {
                    RouteCheckState.Ran => "ran",
                    RouteCheckState.Partial => "partial",
                    _ => "skipped",
                },
                c.Reason,
                c.FindingCount)).ToList(),
            report.Findings.Select(CheckRouteFinding.From).ToList(),
            report.Track.Legs.Select(l => new CheckRouteLeg(
                l.Index,
                Nm(l.StartMeters),
                Nm(l.LengthMeters),
                l.PortMeters,
                l.StarboardMeters)).ToList(),
            clearance,
            report.DatasetsUsed,
            clearance is null ? DisclaimerText : DisclaimerText + " · clearance " + clearance.Disclaimer);
    }

    /// <summary>The summary line for a set of findings, as SoundCharts' no-draft verdict titles.</summary>
    /// <param name="mustReview">Number of must_review findings.</param>
    /// <param name="check">Number of check findings.</param>
    /// <returns>The line.</returns>
    public static string SummaryText(int mustReview, int check) =>
        mustReview > 0 ? $"{mustReview} to review"
        : check > 0 ? $"{check} to check"
        : "No findings for your settings";

    /// <summary>The JSON name of a finding category, e.g. <c>chart</c> or <c>warning</c>.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The name.</returns>
    public static string CategoryCode(RouteFindingCategory category) => category switch
    {
        RouteFindingCategory.Chart => "chart",
        RouteFindingCategory.Depth => "depth",
        RouteFindingCategory.Area => "area",
        RouteFindingCategory.Warning => "warning",
        RouteFindingCategory.Clearance => "clearance",
        RouteFindingCategory.Confidence => "confidence",
        _ => "data",
    };

    /// <summary>The JSON name of a verdict: <c>must_review</c>, <c>water_level_ends_before_arrival</c>, <c>check</c> or <c>clear</c>.</summary>
    /// <param name="kind">The verdict.</param>
    /// <returns>The name.</returns>
    public static string VerdictCode(RouteCheckVerdictKind kind) => kind switch
    {
        RouteCheckVerdictKind.MustReview => "must_review",
        RouteCheckVerdictKind.WaterLevelEndsBeforeArrival => "water_level_ends_before_arrival",
        RouteCheckVerdictKind.Check => "check",
        _ => "clear",
    };

    /// <summary>The wire code for a clearance state: <c>meets_settings</c>, <c>tight</c>, <c>below_setting</c> or <c>no_data</c>.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The code.</returns>
    public static string StateCode(UkcState state) => state switch
    {
        UkcState.MeetsSettings => "meets_settings",
        UkcState.Tight => "tight",
        UkcState.BelowSetting => "below_setting",
        _ => "no_data",
    };

    /// <summary>
    /// The wire codes for lower-confidence reasons, in a fixed order:
    /// <c>charted_depth_from_s101</c>, then <c>datum_unconfirmed</c>.
    /// </summary>
    /// <param name="reasons">The reasons.</param>
    /// <returns>The codes; empty at full confidence.</returns>
    public static IReadOnlyList<string> ConfidenceCodes(ConfidenceReasons reasons)
    {
        var codes = new List<string>(2);
        if (reasons.HasFlag(ConfidenceReasons.ChartedDepthFromS101))
        {
            codes.Add("charted_depth_from_s101");
        }

        if (reasons.HasFlag(ConfidenceReasons.DatumUnconfirmed))
        {
            codes.Add("datum_unconfirmed");
        }

        return codes;
    }

    /// <summary>The wire code for a clearance verdict, e.g. <c>below_setting</c>.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns>The code.</returns>
    public static string PassageVerdictCode(PassageVerdict verdict) => verdict switch
    {
        PassageVerdict.MeetsSettings => "meets_settings",
        PassageVerdict.MeetsSettingsTight => "meets_settings_tight",
        PassageVerdict.BelowSetting => "below_setting",
        PassageVerdict.IncompleteData => "incomplete_data",
        _ => "water_level_ends_before_arrival",
    };

    internal static double Nm(double meters) => Math.Round(meters / PassageGeodesy.MetersPerNauticalMile, 4);

    internal static double? Round(double? metres) => metres is { } m ? Math.Round(m, 2) : null;
}

/// <summary>The verdict of a route check.</summary>
public sealed record CheckRouteVerdict(
    [property: Description("must_review (a must_review finding, or the clearance is below your UKC setting), water_level_ends_before_arrival (and nothing must_review), check (check findings, or a tight or incomplete clearance) or clear.")] string Kind,
    [property: Description("The verdict in the UI's words: the headline finding's title, the clearance verdict, or \"No findings for your settings\".")] string Title,
    [property: Description("Id of the finding that names the verdict (in findings), or null when none does.")] string? HeadlineId)
{
    /// <summary>Projects a report's verdict.</summary>
    /// <param name="report">The report.</param>
    /// <returns>The verdict.</returns>
    public static CheckRouteVerdict From(RouteCheckReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var verdict = report.Verdict;
        var title = verdict.Headline?.Title
            ?? (verdict.Kind != RouteCheckVerdictKind.Clear && report.Clearance is { } clearance ? CheckRouteClearance.TitleOf(clearance) : null)
            ?? CheckRouteResult.SummaryText(0, 0);
        return new CheckRouteVerdict(CheckRouteResult.VerdictCode(verdict.Kind), title, verdict.Headline?.Id);
    }
}

/// <summary>The counts and summary line of a route check.</summary>
public sealed record CheckRouteSummary(
    [property: Description("Number of findings.")] int Count,
    [property: Description("Number of must_review findings (the track crosses land, drying ground or the safety contour, enters an area where entry is prohibited or restricted, a danger is inside the limits, an S-124 warning in force reaches the corridor, or the clearance is below your UKC setting).")] int MustReview,
    [property: Description("Number of check findings (land, drying ground or shoal water inside the cross-track limits, dangers near the route, restricted, caution, traffic, anchorage or protected areas, S-124 warnings near the route, leaving chart coverage, lower-confidence or missing clearance data).")] int Check,
    [property: Description("Number of info findings.")] int Info,
    [property: Description("The summary line, e.g. \"2 to review\", \"1 to check\" or \"No findings for your settings\".")] string Text);

/// <summary>Whether one check ran.</summary>
public sealed record CheckRouteCheck(
    [property: Description("The check: chart, areas, warnings or clearance.")] string Name,
    [property: Description("ran, partial (part of it couldn't run; see reason) or skipped.")] string State,
    [property: Description("Why it was skipped or partial: safety_settings_missing, draft_missing, charts_missing, depth_missing, water_level_missing, areas_missing or warnings_missing; null when it ran in full.")] string? Reason,
    [property: Description("Number of findings it gave.")] int FindingCount);

/// <summary>One finding of a route check.</summary>
public sealed record CheckRouteFinding(
    [property: Description("Id, unique within this result, e.g. \"route_check.crosses_land.0\".")] string Id,
    [property: Description("Kind of finding: one of the codes listed under findings.")] string Code,
    [property: Description("What to do: must_review, check or info.")] string Group,
    [property: Description("What it's about: chart (land, drying ground), depth (safety contour, soundings, S-102 shoals, charted dangers), area (regulated and protected areas), warning (S-124 navigational warnings), clearance (below your UKC setting), confidence (lower-confidence clearance) or data (chart coverage, missing clearance data).")] string Category,
    [property: Description("One line, e.g. \"Crosses drying ground\".")] string Title,
    [property: Description("Where and what, e.g. \"Leg 2 → 3 · 0.10 NM · DepthArea\".")] string Meta,
    [property: Description("Zero-based leg the finding is on.")] int? LegIndex,
    [property: Description("Distance from departure in nautical miles to where it starts.")] double? DistanceNm,
    [property: Description("Where it is: a point, or a span of the route.")] CheckRouteLocation? Location,
    [property: Description("The feature behind the finding (open it with describe_feature), or null.")] CheckRouteFeature? Feature,
    [property: Description("For an s124 finding, the navigational warning and how it stands on the route at the ETA; null for other categories.")] CheckRouteWarning? Warning = null,
    [property: Description("The numbers behind the finding: a danger's depth, side and the threshold that fired, a shoal's least depth, an area's restrictions and dates, a clearance stretch's minimum and margin. Null for land, drying ground and coverage, and for s124 findings (see warning).")] CheckRouteDetails? Details = null)
{
    /// <summary>Projects a finding.</summary>
    /// <param name="f">The finding.</param>
    /// <returns>The DTO.</returns>
    public static CheckRouteFinding From(RouteFinding f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new(
            f.Id,
            f.Code.Value,
            f.Group switch
            {
                RouteFindingGroup.MustReview => "must_review",
                RouteFindingGroup.Check => "check",
                _ => "info",
            },
            CheckRouteResult.CategoryCode(f.Category),
            f.Title,
            f.Meta,
            f.LegIndex,
            f.Distance is { } d ? CheckRouteResult.Nm(d.TotalMetres) : null,
            f.Location is { } l
                ? new CheckRouteLocation(
                    l.IsPoint ? "point" : "span",
                    CheckRouteResult.Nm(l.StartDistanceMeters),
                    CheckRouteResult.Nm(l.EndDistanceMeters),
                    l.Path.Select(p => new[] { p.Latitude, p.Longitude }).ToList())
                : null,
            f.Feature is { } feature ? new CheckRouteFeature(feature.DatasetId, feature.FeatureId) : null,
            f.Details is WarningDetails warning ? CheckRouteWarning.Of(warning) : null,
            CheckRouteDetails.From(f.Details));
    }
}

/// <summary>The navigational warning behind an S-124 finding, and how it stands on the route.</summary>
public sealed record CheckRouteWarning(
    [property: Description("The series and number, e.g. \"NAVAREA I 012/26\", or null when the preamble gives no number.")] string? Number,
    [property: Description("The warning's title, or null.")] string? Title,
    [property: Description("The locality (else the general area), or null.")] string? Locality,
    [property: Description("How it relates to the route: crosses (a line or area reaches the corridor between the cross-track limits) or near (within 0.5 NM of the track, or a point inside the corridor).")] string Relation,
    [property: Description("Whether it applies while the vessel is there: in_force, not_in_force or cancelled.")] string Status,
    [property: Description("When the vessel reaches it, ISO 8601 UTC: the ETA, or the check time when the legs have no speed (see atEta).")] DateTimeOffset From,
    [property: Description("When the vessel leaves it, ISO 8601 UTC; equal to from for a crossing point or a near warning.")] DateTimeOffset Until,
    [property: Description("True when from and until are ETAs; false when the route has no speed and the check time is used.")] bool AtEta,
    [property: Description("When the warning applies: its validity ranges in document order; empty when it gives none (in force until cancelled).")] IReadOnlyList<CheckRouteValidity> Validity,
    [property: Description("When the warning cancels itself, ISO 8601 UTC, or null.")] DateTimeOffset? CancellationDate,
    [property: Description("The full message text, in document order.")] IReadOnlyList<string> Text)
{
    internal static CheckRouteWarning Of(WarningDetails w) => new(
        w.Info.Label,
        w.Info.Title,
        w.Info.Locality ?? w.Info.GeneralArea,
        w.Relation == RouteWarningRelation.Crosses ? "crosses" : "near",
        w.Status switch
        {
            RouteWarningStatus.InForce => "in_force",
            RouteWarningStatus.NotInForce => "not_in_force",
            _ => "cancelled",
        },
        w.From.ToUniversalTime(),
        w.Until.ToUniversalTime(),
        w.AtEta,
        w.Info.Validity.Select(v => new CheckRouteValidity(v.Start?.ToUniversalTime(), v.End?.ToUniversalTime())).ToList(),
        w.Info.CancellationDate?.ToUniversalTime(),
        w.Info.Texts.Select(t => t.Text).ToList());
}

/// <summary>A period when a navigational warning applies.</summary>
public sealed record CheckRouteValidity(
    [property: Description("Start, ISO 8601 UTC, or null when open.")] DateTimeOffset? Start,
    [property: Description("End, ISO 8601 UTC, or null when open.")] DateTimeOffset? End);

/// <summary>Where a finding is.</summary>
public sealed record CheckRouteLocation(
    [property: Description("\"point\" or \"span\".")] string Kind,
    [property: Description("Distance from departure to the start, nautical miles.")] double StartNm,
    [property: Description("Distance from departure to the end, nautical miles.")] double EndNm,
    [property: Description("The point, or the positions along the span in route order, as [latitude, longitude] pairs.")] IReadOnlyList<double[]> Path);

/// <summary>A feature behind a finding.</summary>
public sealed record CheckRouteFeature(
    [property: Description("Catalog id of the dataset that holds the feature (as list_datasets).")] string DatasetId,
    [property: Description("The feature's id within the dataset.")] string FeatureId);

/// <summary>The corridor swept along one leg.</summary>
public sealed record CheckRouteLeg(
    [property: Description("Zero-based leg index.")] int Index,
    [property: Description("Distance from departure to the start of the leg, nautical miles.")] double StartNm,
    [property: Description("Leg length, nautical miles.")] double DistanceNm,
    [property: Description("How far the corridor reaches to port of the track, metres (a half-width).")] double PortMeters,
    [property: Description("How far the corridor reaches to starboard of the track, metres (a half-width).")] double StarboardMeters);

/// <summary>The under-keel clearance of a route check.</summary>
public sealed record CheckRouteClearance(
    [property: Description("Departure time, UTC.")] DateTimeOffset Departure,
    [property: Description("Arrival time, UTC; null when a leg has no speed.")] DateTimeOffset? Arrival,
    [property: Description("Clearance verdict code: water_level_ends_before_arrival, below_setting, incomplete_data, meets_settings_tight or meets_settings (in that priority).")] string Verdict,
    [property: Description("The clearance verdict in the UI's words, e.g. \"Below your UKC setting in 2 places\".")] string VerdictTitle,
    [property: Description("Lower-confidence reasons anywhere on the passage: charted_depth_from_s101 and/or datum_unconfirmed. The verdict follows the clearance band; these qualify it and never replace it.")] IReadOnlyList<string> Confidence,
    [property: Description("True when some leg has no required UKC, so it was checked against 0 m.")] bool NoMarginSet,
    [property: Description("The smallest margin over the required UKC, with where and when; null when no sample has a clearance.")] CheckRouteMinimum? Minimum,
    [property: Description("Minutes spent below the UKC setting (0 without ETAs).")] double MinutesBelow,
    [property: Description("Number of separate stretches below the UKC setting.")] int PlacesBelow,
    [property: Description("Number of samples along the passage.")] int SampleCount,
    [property: Description("Stretches with no data: no charted depth, no S-104 coverage, before/after the S-104 time range, masked S-104 values, or a datum mismatch.")] IReadOnlyList<CheckRouteStretch> Gaps,
    [property: Description("Lower-confidence stretches: charted depth from S-101, or datums that can't be confirmed to agree. The samples in them keep their own state (meets_settings, tight or below_setting).")] IReadOnlyList<CheckRouteStretch> LowConfidence,
    [property: Description("Datasets, stations and datums behind the clearance.")] CheckRouteClearanceSources Sources,
    [property: Description("Per-leg results in route order; present only when includeSamples is set.")] IReadOnlyList<CheckRouteClearanceLeg>? Legs,
    [property: Description("Per-sample values; present only when includeSamples is set.")] IReadOnlyList<CheckRouteSample>? Samples,
    [property: Description("True when samples were thinned to maxSamples; present only when includeSamples is set.")] bool? SamplesThinned,
    [property: Description("The departure sweep; present only when includeWindow is set (and, without windowFrom and windowTo, some S-104 covers the passage).")] CheckRouteWindow? Window,
    [property: Description("The data the clearance was computed from, e.g. \"computed from S-102, S-104 for your settings\".")] string Disclaimer)
{
    /// <summary>Projects a report's clearance result.</summary>
    /// <param name="report">The report.</param>
    /// <param name="result">The report's <see cref="RouteCheckReport.Clearance"/>.</param>
    /// <param name="includeSamples">Include the per-leg results and samples.</param>
    /// <param name="maxSamples">The most samples to include.</param>
    /// <returns>The clearance.</returns>
    public static CheckRouteClearance From(RouteCheckReport report, PassageCheckResult result, bool includeSamples = false, int maxSamples = 500)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(result);
        var samples = result.Samples;
        IReadOnlyList<CheckRouteSample>? sampleList = null;
        IReadOnlyList<CheckRouteClearanceLeg>? legList = null;
        bool? thinned = null;
        if (includeSamples)
        {
            var kept = Thin(samples, maxSamples);
            thinned = kept.Count < samples.Count;
            sampleList = kept.Select(Sample).ToList();
            var legs = report.Plan.Legs;
            legList = result.Legs.Select(l => new CheckRouteClearanceLeg(
                l.LegIndex,
                l.Name,
                CheckRouteResult.Nm(l.StartDistanceMeters),
                CheckRouteResult.Nm(l.DistanceMeters),
                l.Start,
                l.End,
                legs[l.LegIndex].Draft!.Value.TotalMetres,
                legs[l.LegIndex].Speed?.TotalKnots,
                legs[l.LegIndex].Squat.TotalMetres,
                legs[l.LegIndex].RequiredUkc?.TotalMetres,
                MinimumOf(l.Minimum),
                CheckRouteResult.StateCode(l.WorstState),
                Math.Round(l.MinutesBelow, 1),
                l.PlacesBelow,
                l.HasLowConfidence,
                CheckRouteResult.ConfidenceCodes(l.Confidence),
                l.HasNoWaterLevel)).ToList();
        }

        var window = report.Departures is { } sweep && (sweep.Options.Count > 0 || report.Settings.DepartureWindow is { FollowsWaterLevelData: false })
            ? WindowOf(sweep, report.Plan.EffectiveDeparture(report.Settings))
            : null;

        return new CheckRouteClearance(
            result.Departure,
            result.Arrival,
            CheckRouteResult.PassageVerdictCode(result.Verdict),
            TitleOf(result),
            CheckRouteResult.ConfidenceCodes(result.Confidence),
            result.NoMarginSet,
            MinimumOf(result.Minimum),
            Math.Round(result.MinutesBelow, 1),
            result.PlacesBelow,
            samples.Count,
            result.Gaps.Select(Stretch).ToList(),
            result.LowConfidence.Select(Stretch).ToList(),
            SourcesOf(result.Sources),
            legList,
            sampleList,
            thinned,
            window,
            DisclaimerOf(result.Sources));
    }

    /// <summary>
    /// The handoff's verdict title (C3): data ends before arrival → below →
    /// (incomplete) → tight → meets.
    /// </summary>
    internal static string TitleOf(PassageCheckResult result) => result.Verdict switch
    {
        PassageVerdict.WaterLevelEndsBeforeArrival => "Arrival is after the water-level data ends",
        PassageVerdict.BelowSetting => result.PlacesBelow == 1
            ? "Below your UKC setting in 1 place"
            : $"Below your UKC setting in {result.PlacesBelow} places",
        PassageVerdict.IncompleteData => "No data for part of the passage",
        PassageVerdict.MeetsSettingsTight => "Meets your settings · tight",
        _ => "Meets your settings",
    } + (result.NoMarginSet ? " · no margin set" : string.Empty);

    private static string DisclaimerOf(PassageSources sources)
    {
        var products = new List<string>();
        if (sources.BathymetryDatasets.Count > 0) products.Add("S-102");
        if (sources.FallbackDatasets.Count > 0) products.Add("S-101");
        if (sources.WaterLevelSources.Count > 0) products.Add("S-104");
        return "computed from " + (products.Count == 0 ? "no data" : string.Join(", ", products)) + " for your settings";
    }

    private static List<UkcSample> Thin(IReadOnlyList<UkcSample> samples, int max)
    {
        if (samples.Count <= max)
        {
            return [.. samples];
        }

        // Keep the tightest sample of each run, so the minimum and every
        // below-the-setting run survive the thinning.
        var kept = new List<UkcSample>(max);
        var per = (double)samples.Count / max;
        for (var b = 0; b < max; b++)
        {
            var start = (int)(b * per);
            var end = Math.Min(samples.Count, (int)((b + 1) * per));
            UkcSample? best = null;
            for (var i = start; i < end; i++)
            {
                var s = samples[i];
                if (best is null || Margin(s) < Margin(best))
                {
                    best = s;
                }
            }

            if (best is not null)
            {
                kept.Add(best);
            }
        }

        return kept;

        static double Margin(UkcSample s) => s.ClearanceMeters is { } c ? c - s.RequiredUkcMeters : double.PositiveInfinity;
    }

    private static CheckRouteSample Sample(UkcSample s) => new(
        s.Index,
        s.LegIndex,
        s.Position.Latitude,
        s.Position.Longitude,
        CheckRouteResult.Nm(s.DistanceMeters),
        s.Time,
        CheckRouteResult.Round(s.Depth?.DepthMeters),
        s.Depth?.Kind switch
        {
            ChartedDepthKind.Bathymetry => "s102",
            ChartedDepthKind.DredgedArea => "s101_dredged_area",
            ChartedDepthKind.DepthArea => "s101_depth_area",
            ChartedDepthKind.Sounding => "s101_sounding",
            _ => null,
        },
        s.Depth?.DatasetId,
        s.Depth?.Reference,
        CheckRouteResult.Round(s.Depth?.UncertaintyMeters),
        CheckRouteResult.Round(s.WaterLevel.HeightMeters),
        Snake(s.WaterLevel.Status.ToString()),
        s.WaterLevel.Source?.DatasetId,
        s.WaterLevel.Source?.StationId,
        s.WaterLevel.StepTime,
        s.WaterLevel.Datum is { } d ? Snake(d.ToString()) : null,
        CheckRouteResult.Round(s.ClearanceMeters),
        s.RequiredUkcMeters,
        CheckRouteResult.StateCode(s.State),
        CheckRouteResult.ConfidenceCodes(s.Confidence));

    private static CheckRouteMinimum? MinimumOf(UkcMinimum? m) => m is null
        ? null
        : new CheckRouteMinimum(
            m.LegIndex,
            m.SampleIndex,
            m.Position.Latitude,
            m.Position.Longitude,
            CheckRouteResult.Nm(m.DistanceMeters),
            m.Time,
            m.HasEta,
            CheckRouteResult.Round(m.ClearanceMeters)!.Value,
            m.RequiredUkcMeters,
            CheckRouteResult.StateCode(m.State),
            CheckRouteResult.ConfidenceCodes(m.Confidence));

    private static CheckRouteStretch Stretch(PassageStretch s) => new(
        Snake(s.Kind.ToString()),
        s.FirstSample,
        s.LastSample,
        s.FirstLeg,
        s.LastLeg,
        CheckRouteResult.Nm(s.StartDistanceMeters),
        CheckRouteResult.Nm(s.EndDistanceMeters),
        s.StartTime,
        s.EndTime);

    private static CheckRouteClearanceSources SourcesOf(PassageSources s) => new(
        s.BathymetryDatasets,
        s.FallbackDatasets,
        s.WaterLevelSources.Select(w => new CheckRouteWaterLevelSource(
            w.DatasetId, w.Kind == WaterLevelSourceKind.Grid ? "grid" : "station", w.StationId)).ToList(),
        s.DepthDatums.Select(Datum).ToList(),
        s.WaterLevelDatums.Select(Datum).ToList(),
        CheckRouteResult.Round(s.MaxUncertaintyMeters));

    private static CheckRouteDatum Datum(int code) => new(code, VerticalDatums.GetLabel(code));

    private static CheckRouteWindow WindowOf(DepartureSweep sweep, DateTimeOffset departure) => new(
        sweep.Options.Count > 0 ? sweep.Options[0].Departure : departure,
        sweep.Options.Count > 0 ? sweep.Options[^1].Departure : departure,
        (int)Math.Round(sweep.Step.TotalMinutes),
        sweep.Windows().Select(w => new CheckRouteWindowRange(w.First, w.Last)).ToList(),
        CheckRouteResult.ConfidenceCodes(sweep.MeetingConfidence()),
        sweep.NearestMeeting(departure)?.Departure,
        sweep.ArrivalAfterDataEnds() is { } hatched ? new CheckRouteWindowRange(hatched.First, hatched.Last) : null,
        sweep.Options.Select(o => new CheckRouteDeparture(
            o.Departure,
            o.Arrival,
            CheckRouteResult.PassageVerdictCode(o.Verdict),
            CheckRouteResult.Round(o.MinimumClearanceMeters),
            Math.Round(o.MinutesBelow, 1),
            CheckRouteResult.ConfidenceCodes(o.Confidence))).ToList());

    // PascalCase enum name → snake_case wire code; digits stay attached ("FromS101" → "from_s101").
    private static string Snake(string pascal)
    {
        var builder = new System.Text.StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(pascal[i]));
        }

        return builder.ToString();
    }
}

/// <summary>The smallest margin over the required UKC.</summary>
public sealed record CheckRouteMinimum(
    [property: Description("Zero-based leg index.")] int Leg,
    [property: Description("Sample index.")] int Sample,
    [property: Description("Latitude, WGS-84 decimal degrees.")] double Latitude,
    [property: Description("Longitude, WGS-84 decimal degrees.")] double Longitude,
    [property: Description("Distance from departure, nautical miles.")] double DistanceNm,
    [property: Description("When it occurs (ETA, UTC; the departure time without ETAs).")] DateTimeOffset Time,
    [property: Description("True when time is a real ETA.")] bool HasEta,
    [property: Description("Under-keel clearance there, metres.")] double UkcMeters,
    [property: Description("Required UKC there, metres.")] double RequiredUkcMeters,
    [property: Description("State there (see samples[].state).")] string State,
    [property: Description("Lower-confidence reasons there (see samples[].confidence).")] IReadOnlyList<string> Confidence);

/// <summary>One leg's clearance.</summary>
public sealed record CheckRouteClearanceLeg(
    [property: Description("Zero-based leg index.")] int Index,
    [property: Description("Leg name, when known.")] string? Name,
    [property: Description("Distance from departure to the start of the leg, nautical miles.")] double StartNm,
    [property: Description("Leg length, nautical miles.")] double DistanceNm,
    [property: Description("ETA at the start of the leg, UTC; null when unknown.")] DateTimeOffset? Start,
    [property: Description("ETA at the end of the leg, UTC; null when unknown.")] DateTimeOffset? End,
    [property: Description("Draft used on the leg, metres.")] double DraftMeters,
    [property: Description("Speed used on the leg, knots; null when unknown.")] double? SpeedKnots,
    [property: Description("Squat allowance used on the leg, metres.")] double SquatMeters,
    [property: Description("Required UKC on the leg, metres; null when unset (checked against 0 m).")] double? RequiredUkcMeters,
    [property: Description("Smallest UKC margin on the leg; null when no sample has a clearance.")] CheckRouteMinimum? Minimum,
    [property: Description("Worst state on the leg (see samples[].state); tints the leg chip, whatever the confidence.")] string WorstState,
    [property: Description("Minutes below the UKC setting on the leg.")] double MinutesBelow,
    [property: Description("Separate stretches below the UKC setting on the leg.")] int PlacesBelow,
    [property: Description("True when part of the leg is lower confidence (dashed chip border and dashed route line in the band's colour).")] bool LowConfidence,
    [property: Description("Lower-confidence reasons on the leg (see samples[].confidence).")] IReadOnlyList<string> Confidence,
    [property: Description("True when part of the leg has charted depth but no applied water level (\"no water level\").")] bool NoWaterLevel);

/// <summary>A run of samples sharing a gap or low-confidence reason.</summary>
public sealed record CheckRouteStretch(
    [property: Description("Kind: no_charted_depth, no_water_level_coverage, water_level_before_start, water_level_after_end, water_level_no_data, datum_mismatch, charted_depth_from_s101 or datum_unconfirmed.")] string Kind,
    [property: Description("First sample index.")] int FirstSample,
    [property: Description("Last sample index.")] int LastSample,
    [property: Description("Leg the stretch starts on.")] int FirstLeg,
    [property: Description("Leg the stretch ends on.")] int LastLeg,
    [property: Description("Start, nautical miles from departure.")] double StartNm,
    [property: Description("End, nautical miles from departure.")] double EndNm,
    [property: Description("Time at the first sample, UTC.")] DateTimeOffset From,
    [property: Description("Time at the last sample, UTC.")] DateTimeOffset To);

/// <summary>Where the clearance's values came from.</summary>
public sealed record CheckRouteClearanceSources(
    [property: Description("S-102 datasets that supplied charted depth.")] IReadOnlyList<string> BathymetryDatasets,
    [property: Description("S-101 cells that supplied charted depth where S-102 didn't (low confidence).")] IReadOnlyList<string> FallbackDatasets,
    [property: Description("S-104 datasets (and stations) that supplied water level.")] IReadOnlyList<CheckRouteWaterLevelSource> WaterLevels,
    [property: Description("Vertical datums of the charted depths.")] IReadOnlyList<CheckRouteDatum> DepthDatums,
    [property: Description("Vertical datums of the water levels.")] IReadOnlyList<CheckRouteDatum> WaterLevelDatums,
    [property: Description("Largest S-102 depth uncertainty met, metres; null when none.")] double? MaxUncertaintyMeters);

/// <summary>An S-104 source.</summary>
public sealed record CheckRouteWaterLevelSource(
    [property: Description("S-104 dataset id.")] string Dataset,
    [property: Description("\"grid\" or \"station\".")] string Kind,
    [property: Description("Station identifier, for a station series.")] string? Station);

/// <summary>A vertical datum code and its name.</summary>
public sealed record CheckRouteDatum(
    [property: Description("S-100 vertical datum code as declared (verticalDatum).")] int Code,
    [property: Description("Datum name, or \"Unknown (code N)\" for a code outside the S-100 list.")] string Label);

/// <summary>One clearance sample.</summary>
public sealed record CheckRouteSample(
    [property: Description("Sample index.")] int Index,
    [property: Description("Zero-based leg index.")] int Leg,
    [property: Description("Latitude of the shoalest point in the sample's stretch.")] double Latitude,
    [property: Description("Longitude of the shoalest point in the sample's stretch.")] double Longitude,
    [property: Description("Distance from departure, nautical miles.")] double DistanceNm,
    [property: Description("ETA at the sample, UTC (the departure time without ETAs).")] DateTimeOffset Time,
    [property: Description("Charted depth, metres below its datum; null when none.")] double? ChartedDepthMeters,
    [property: Description("Charted depth source: s102, s101_dredged_area, s101_depth_area or s101_sounding.")] string? DepthSource,
    [property: Description("Dataset (cell) of the charted depth.")] string? DepthDataset,
    [property: Description("Grid cell (row,col) for S-102, or feature record id for S-101.")] string? DepthReference,
    [property: Description("S-102 uncertainty, metres.")] double? UncertaintyMeters,
    [property: Description("Water level above its datum, metres (also reported on a datum mismatch, where it isn't applied).")] double? WaterLevelMeters,
    [property: Description("applied, no_coverage, before_start, after_end, no_data or datum_mismatch.")] string WaterLevelStatus,
    [property: Description("S-104 dataset of the water level.")] string? WaterLevelDataset,
    [property: Description("S-104 station, for a station series.")] string? Station,
    [property: Description("S-104 time step nearest the ETA, UTC.")] DateTime? TimeStep,
    [property: Description("same, different or unknown: how the depth and water-level datums compare.")] string? Datum,
    [property: Description("Under-keel clearance = charted depth + water level − draft − squat, metres; null without both.")] double? UkcMeters,
    [property: Description("Required UKC, metres.")] double RequiredUkcMeters,
    [property: Description("Clearance state: meets_settings, tight (within 0.5 m of required), below_setting or no_data. Confidence is reported separately.")] string State,
    [property: Description("Lower-confidence reasons, separate from the state: charted_depth_from_s101 (S-101 stands in for S-102) and datum_unconfirmed (the depth or water-level datum isn't declared, so they can't be confirmed to share a reference). Empty at full confidence.")] IReadOnlyList<string> Confidence);

/// <summary>The departure sweep.</summary>
public sealed record CheckRouteWindow(
    [property: Description("First swept departure, UTC.")] DateTimeOffset From,
    [property: Description("Last swept departure, UTC.")] DateTimeOffset To,
    [property: Description("Minutes between departures.")] int StepMinutes,
    [property: Description("Runs of departures that meet your settings, including those that meet them with lower confidence.")] IReadOnlyList<CheckRouteWindowRange> Windows,
    [property: Description("Lower-confidence reasons among the departures that meet your settings: the caveat on the window. Empty when none.")] IReadOnlyList<string> WindowConfidence,
    [property: Description("The meeting departure nearest the requested departure; null when none meets.")] DateTimeOffset? NearestMeetingDeparture,
    [property: Description("The trailing run of departures whose arrival is after the S-104 data ends (verdict water_level_ends_before_arrival; never meeting); null when the last departure arrives within the data.")] CheckRouteWindowRange? ArrivalAfterDataEnds,
    [property: Description("One entry per swept departure.")] IReadOnlyList<CheckRouteDeparture> Departures);

/// <summary>A run of departures that meet your settings.</summary>
public sealed record CheckRouteWindowRange(
    [property: Description("First meeting departure, UTC.")] DateTimeOffset First,
    [property: Description("Last meeting departure, UTC.")] DateTimeOffset Last);

/// <summary>One swept departure.</summary>
public sealed record CheckRouteDeparture(
    [property: Description("Departure, UTC.")] DateTimeOffset Departure,
    [property: Description("Arrival, UTC; null without ETAs.")] DateTimeOffset? Arrival,
    [property: Description("Clearance verdict code for this departure.")] string Verdict,
    [property: Description("UKC at the smallest margin, metres; null when no sample has a clearance.")] double? MinUkcMeters,
    [property: Description("Minutes below the UKC setting.")] double MinutesBelow,
    [property: Description("Lower-confidence reasons along the passage at this departure.")] IReadOnlyList<string> Confidence);
