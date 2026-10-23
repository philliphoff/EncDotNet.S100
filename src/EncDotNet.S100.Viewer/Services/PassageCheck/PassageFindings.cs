using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Tools;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The findings the clearance check produces for the findings list (#905,
/// handoff F4): each stretch below your UKC setting, each low-confidence
/// stretch, each water-level or depth gap, and expired S-111 runs.
/// </summary>
internal static class PassageFindings
{
    /// <summary>Codes for the findings this class produces.</summary>
    internal static class Codes
    {
        public const string BelowSetting = "clearance.below_setting";
        public const string ChartedDepthFromS101 = "confidence.charted_depth_from_s101";
        public const string DatumUnconfirmed = "confidence.datum_unconfirmed";
        public const string WaterLevelEnds = "data.water_level_ends";
        public const string WaterLevelStartsLate = "data.water_level_starts_late";
        public const string NoWaterLevelCoverage = "data.no_water_level_coverage";
        public const string WaterLevelNoData = "data.water_level_no_data";
        public const string DatumMismatch = "data.datum_mismatch";
        public const string NoChartedDepth = "data.no_charted_depth";
        public const string ForecastExpired = "data.s111_forecast_expired";
    }

    /// <summary>
    /// Builds the findings for a check, sorted by group (MUST REVIEW, CHECK,
    /// INFO) and then by distance along the route.
    /// </summary>
    /// <param name="snapshot">The check.</param>
    /// <param name="expiredForecasts">Names of the S-111 datasets near the route whose forecast has ended.</param>
    /// <param name="depthUnit">The depth unit for clearances.</param>
    /// <param name="timeFormat">The time format for ETAs.</param>
    public static IReadOnlyList<RouteFinding> Build(
        PassageCheckSnapshot snapshot,
        IReadOnlyList<string> expiredForecasts,
        DepthUnit depthUnit,
        TimeFormat timeFormat)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(expiredForecasts);
        var findings = new List<RouteFinding>();
        AddBelowSetting(findings, snapshot, depthUnit, timeFormat);

        var index = 0;
        foreach (var stretch in snapshot.Result.LowConfidence)
        {
            var (code, title, detail) = stretch.Kind == PassageStretchKind.ChartedDepthFromS101
                ? (Codes.ChartedDepthFromS101, Strings.Findings_S101Only,
                    string.Format(CultureInfo.CurrentCulture, Strings.Findings_OutsideS102Format, Nm(stretch.LengthMeters)))
                : (Codes.DatumUnconfirmed, Strings.Findings_DatumUnconfirmed,
                    string.Format(CultureInfo.CurrentCulture, Strings.Findings_DatumUnconfirmedMetaFormat, Nm(stretch.LengthMeters)));
            findings.Add(Span(snapshot, $"{code}.{index++}", code, RouteFindingGroup.Check, RouteFindingSource.Confidence,
                title, Join(LegLabel(stretch.FirstLeg, stretch.LastLeg), detail), stretch));
        }

        index = 0;
        foreach (var stretch in snapshot.Result.Gaps)
        {
            var nm = string.Format(CultureInfo.CurrentCulture, Strings.Check_LegDistanceFormat, Nm(stretch.LengthMeters));
            var (code, title, meta) = stretch.Kind switch
            {
                PassageStretchKind.WaterLevelAfterEnd => (Codes.WaterLevelEnds, Strings.Findings_WaterLevelEnds,
                    Join(LegLabel(stretch.FirstLeg, stretch.LastLeg),
                        string.Format(CultureInfo.CurrentCulture, Strings.Findings_FromFormat,
                            PassageFormatting.FormatEta(stretch.StartTime, snapshot.Result.Departure, timeFormat)),
                        string.Format(CultureInfo.CurrentCulture, Strings.Findings_TideNotAppliedForFormat, Nm(stretch.LengthMeters)))),
                PassageStretchKind.WaterLevelBeforeStart => (Codes.WaterLevelStartsLate, Strings.Findings_WaterLevelStartsLate,
                    Join(LegLabel(stretch.FirstLeg, stretch.LastLeg),
                        string.Format(CultureInfo.CurrentCulture, Strings.Findings_TideNotAppliedForFormat, Nm(stretch.LengthMeters)))),
                PassageStretchKind.NoWaterLevelCoverage => (Codes.NoWaterLevelCoverage, Strings.Findings_NoWaterLevelCoverage,
                    Join(LegLabel(stretch.FirstLeg, stretch.LastLeg), nm, Strings.Findings_TideNotApplied)),
                PassageStretchKind.WaterLevelNoData => (Codes.WaterLevelNoData, Strings.Findings_WaterLevelNoData,
                    Join(LegLabel(stretch.FirstLeg, stretch.LastLeg), nm, Strings.Findings_TideNotApplied)),
                PassageStretchKind.DatumMismatch => (Codes.DatumMismatch, Strings.Findings_DatumMismatch,
                    Join(LegLabel(stretch.FirstLeg, stretch.LastLeg), nm, Strings.Findings_TideNotApplied)),
                _ => (Codes.NoChartedDepth, Strings.Findings_NoChartedDepth,
                    Join(LegLabel(stretch.FirstLeg, stretch.LastLeg), nm, Strings.Findings_NoDepthSource)),
            };
            findings.Add(Span(snapshot, $"{code}.{index++}", code, RouteFindingGroup.Check, RouteFindingSource.Data, title, meta, stretch));
        }

        if (expiredForecasts.Count > 0)
        {
            var names = expiredForecasts.Count <= 2
                ? string.Join(", ", expiredForecasts)
                : string.Format(CultureInfo.CurrentCulture, Strings.Findings_AndMoreFormat, string.Join(", ", expiredForecasts.Take(2)), expiredForecasts.Count - 2);
            findings.Add(new RouteFinding(
                Codes.ForecastExpired,
                Codes.ForecastExpired,
                RouteFindingGroup.Info,
                RouteFindingSource.Data,
                expiredForecasts.Count == 1
                    ? Strings.Findings_ForecastExpired
                    : string.Format(CultureInfo.CurrentCulture, Strings.Findings_ForecastsExpiredFormat, expiredForecasts.Count),
                string.Format(CultureInfo.CurrentCulture, Strings.Findings_ForecastExpiredMetaFormat, names),
                LegIndex: null,
                DistanceMeters: null,
                Location: null));
        }

        return Sort(findings);
    }

    /// <summary>Sorts findings by group, then by distance along the route (unplaced findings last), keeping ties in order.</summary>
    internal static IReadOnlyList<RouteFinding> Sort(IEnumerable<RouteFinding> findings) =>
        findings
            .Select((f, i) => (f, i))
            .OrderBy(x => x.f.Group)
            .ThenBy(x => x.f.DistanceMeters ?? double.MaxValue)
            .ThenBy(x => x.i)
            .Select(x => x.f)
            .ToList();

    /// <summary>"Leg 6 → 7", or "Legs 5 → 7" for a span over more than one leg.</summary>
    internal static string LegLabel(int firstLeg, int lastLeg) => firstLeg == lastLeg
        ? string.Format(CultureInfo.CurrentCulture, Strings.Check_LegFormat, firstLeg + 1, firstLeg + 2)
        : string.Format(CultureInfo.CurrentCulture, Strings.Findings_LegsFormat, firstLeg + 1, lastLeg + 2);

    // One finding per run of consecutive samples below the setting, as the
    // engine counts places below.
    private static void AddBelowSetting(List<RouteFinding> findings, PassageCheckSnapshot snapshot, DepthUnit depthUnit, TimeFormat timeFormat)
    {
        var samples = snapshot.Result.Samples;
        var place = 0;
        var i = 0;
        while (i < samples.Count)
        {
            if (samples[i].State != UkcState.BelowSetting)
            {
                i++;
                continue;
            }

            var first = i;
            var minutes = 0.0;
            var hasEta = true;
            UkcSample? lowest = null;
            for (; i < samples.Count && samples[i].State == UkcState.BelowSetting; i++)
            {
                var s = samples[i];
                var knots = snapshot.Legs[s.LegIndex].Speed?.TotalKnots;
                if (knots is > 0)
                    minutes += (s.EndDistanceMeters - s.StartDistanceMeters) / (knots.Value * PassageGeodesy.MetersPerNauticalMile / 60.0);
                hasEta &= s.HasEta;
                if (lowest is null || (s.ClearanceMeters - s.RequiredUkcMeters) < (lowest.ClearanceMeters - lowest.RequiredUkcMeters))
                    lowest = s;
            }

            var a = samples[first];
            var b = samples[i - 1];
            var length = b.EndDistanceMeters - a.StartDistanceMeters;
            var title = hasEta
                ? string.Format(CultureInfo.CurrentCulture, Strings.Findings_BelowMinutesFormat, Math.Max(1, (int)Math.Round(minutes)))
                : string.Format(CultureInfo.CurrentCulture, Strings.Findings_BelowDistanceFormat, Nm(length));
            var min = lowest!;
            var clearance = min.ClearanceMeters is { } c ? PassageFormatting.Format(c, PassageQuantity.Depth, depthUnit) : "—";
            var minText = min.HasEta
                ? string.Format(CultureInfo.CurrentCulture, Strings.Findings_MinAtFormat, clearance,
                    PassageFormatting.FormatEta(min.Time, snapshot.Result.Departure, timeFormat))
                : string.Format(CultureInfo.CurrentCulture, Strings.Findings_MinFormat, clearance);
            findings.Add(new RouteFinding(
                $"{Codes.BelowSetting}.{place++}",
                Codes.BelowSetting,
                RouteFindingGroup.MustReview,
                RouteFindingSource.Clearance,
                title,
                Join(LegLabel(min.LegIndex, min.LegIndex), minText),
                min.LegIndex,
                min.DistanceMeters,
                Location(snapshot, a.StartDistanceMeters, b.EndDistanceMeters)));
        }
    }

    private static RouteFinding Span(
        PassageCheckSnapshot snapshot,
        string id,
        string code,
        RouteFindingGroup group,
        RouteFindingSource source,
        string title,
        string meta,
        PassageStretch stretch) =>
        new(id, code, group, source, title, meta,
            stretch.FirstLeg,
            (stretch.StartDistanceMeters + stretch.EndDistanceMeters) / 2,
            Location(snapshot, stretch.StartDistanceMeters, stretch.EndDistanceMeters));

    /// <summary>The route between two distances from departure, across legs.</summary>
    internal static RouteFindingLocation Location(PassageCheckSnapshot snapshot, double from, double to)
    {
        var path = new List<GeoPosition>();
        var legs = snapshot.Result.Legs;
        for (var leg = 0; leg < legs.Count; leg++)
        {
            var start = legs[leg].StartDistanceMeters;
            var end = start + legs[leg].DistanceMeters;
            if (end < from || start > to)
                continue;
            var part = RouteCheckOverlay.PathAlong(snapshot, leg, Math.Max(from, start), Math.Min(to, end));
            foreach (var p in part)
            {
                if (path.Count == 0 || path[^1] != p)
                    path.Add(p);
            }
        }

        if (path.Count == 0)
            return RouteFindingLocation.Point(PassageCheckService.CursorAt(snapshot, from).Position, from);
        return path.Count == 1 ? RouteFindingLocation.Point(path[0], from) : new RouteFindingLocation(path, from, to);
    }

    private static double Nm(double meters) => meters / PassageGeodesy.MetersPerNauticalMile;

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
