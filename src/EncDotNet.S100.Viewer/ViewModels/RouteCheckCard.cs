using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services.PassageCheck;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>Which findings the list shows (#959, handoff D1); the summary rows set it too (B3).</summary>
internal enum FindingsFilter
{
    /// <summary>Every finding.</summary>
    All,

    /// <summary>The clearance check's findings, with its confidence and data findings.</summary>
    Clearance,

    /// <summary>The route checks against the chart.</summary>
    Route,

    /// <summary>S-124 navigational warnings.</summary>
    Warnings,
}

/// <summary>
/// One summary row of the verdict card (B3): Clearance, Route or Warnings,
/// a value and an optional chip. Clicking it filters the findings list.
/// </summary>
/// <param name="Filter">The findings filter the row sets.</param>
/// <param name="Label">"Clearance", "Route" or "Warnings".</param>
/// <param name="Text">The value, e.g. "min 0,4 m · required 1,0 m" or "Not checked".</param>
/// <param name="Chip">The chip text, e.g. "2 below", or <c>null</c> for none.</param>
/// <param name="ChipTone">The chip's tone.</param>
/// <param name="AddDraft">Whether the row ends with the "+ Add draft" link (no draft).</param>
internal sealed record RouteCheckSummaryRow(
    FindingsFilter Filter,
    string Label,
    string Text,
    string? Chip,
    CheckTone ChipTone,
    bool AddDraft = false);

/// <summary>
/// The Check route verdict card (#959, handoff B1–B5): the report's verdict
/// (<see cref="RouteCheckReport.Verdict"/>) for the clearance check and every
/// route check, worded for the card, with the summary rows and the one "Not
/// for navigation" footer.
/// </summary>
/// <param name="Kind">The report's verdict.</param>
/// <param name="Tone">The card's tone: destructive only for Must review.</param>
/// <param name="Title">The title, naming the worst item, e.g. "Crosses drying ground" or "2 to review".</param>
/// <param name="Subtitle">The line under the title (B2), or <c>null</c>.</param>
/// <param name="Rows">The summary rows, Clearance, Route and (when warnings were checked) Warnings.</param>
/// <param name="Footer">"Not for navigation · computed from {sources} for your settings".</param>
/// <param name="HasRouteFindings">Whether any route-check finding exists (the window line then says they don't change with departure, B4).</param>
internal sealed record RouteCheckCard(
    RouteCheckVerdictKind Kind,
    CheckTone Tone,
    string Title,
    string? Subtitle,
    IReadOnlyList<RouteCheckSummaryRow> Rows,
    string Footer,
    bool HasRouteFindings)
{
    /// <summary>The products the footer can name, in its order.</summary>
    private static readonly string[] FooterProducts = ["S-101", "S-102", "S-104", "S-122", "S-124"];

    /// <summary>
    /// The card for what Check route shows now, or <c>null</c> when it's off,
    /// the route has no legs, or the checked route's report hasn't landed yet.
    /// </summary>
    /// <param name="check">The route check.</param>
    /// <param name="findings">The findings list in the viewer's wording, or <c>null</c> for the report's own.</param>
    /// <param name="depthUnit">The depth unit for the Clearance row.</param>
    /// <param name="timeFormat">Local or UTC, for the where line.</param>
    public static RouteCheckCard? For(PassageCheckService check, RouteFindingsService? findings, DepthUnit depthUnit, TimeFormat timeFormat)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (check.Input is not { } input || check.Report is not { } report || check.ReportRouteId != input.RouteId)
            return null;
        var snapshot = input.HasDraft && check.Current is { } current && current.RouteId == input.RouteId ? current : null;

        var datasets = findings?.Datasets ?? [];
        var near = RouteFindingsService.Overlapping(datasets, input.Legs);
        var list = findings?.Findings ?? report.Findings;
        var warningsChecked = near.Any(d => IsSpec(d, "S-124")) || list.Any(f => f.Category == RouteFindingCategory.Warning);
        return Build(report.Verdict.Kind, input.HasDraft, snapshot, list, warningsChecked, Products(snapshot?.Result.Sources, near), depthUnit, timeFormat);
    }

    /// <summary>Builds the card from its parts (for tests).</summary>
    /// <param name="kind">The report's verdict (<see cref="RouteCheckVerdict.From"/>).</param>
    /// <param name="hasDraft">Whether the route has a draft, so clearance was checked.</param>
    /// <param name="snapshot">The clearance result; <c>null</c> without a draft.</param>
    /// <param name="findings">Every finding, in list order.</param>
    /// <param name="warningsChecked">Whether S-124 warnings were checked (the Warnings row shows).</param>
    /// <param name="products">The products the check used, e.g. "S-101", "S-124".</param>
    /// <param name="depthUnit">The depth unit.</param>
    /// <param name="timeFormat">Local or UTC.</param>
    internal static RouteCheckCard Build(
        RouteCheckVerdictKind kind,
        bool hasDraft,
        PassageCheckSnapshot? snapshot,
        IReadOnlyList<RouteFinding> findings,
        bool warningsChecked,
        IReadOnlyList<string> products,
        DepthUnit depthUnit,
        TimeFormat timeFormat)
    {
        var result = hasDraft ? snapshot?.Result : null;
        var review = findings.Where(f => f.Group == RouteFindingGroup.MustReview).ToList();
        var toCheck = findings.Where(f => f.Group == RouteFindingGroup.Check).ToList();

        CheckTone tone;
        string title;
        string? subtitle;
        // The report judged the kind; the card names it in the viewer's words.
        if (kind == RouteCheckVerdictKind.MustReview)
        {
            tone = CheckTone.Destructive;
            if (hasDraft)
            {
                var worst = review.OrderBy(f => f.Code.HeadlineRank).FirstOrDefault();
                var isBelow = (worst is null || worst.Code == RouteFindingCodes.BelowSetting) && result is not null;
                title = isBelow ? PassageCheckViewModel.VerdictTitle(result!) : worst!.Title;
                var where = isBelow ? Where(snapshot!, timeFormat) : worst!.Meta;
                subtitle = review.Count > 1
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Check_AndMoreToReviewFormat, where, review.Count - 1)
                    : where;
            }
            else
            {
                title = Count(Strings.Check_ToReviewFormat, review.Count);
                var named = review.OrderBy(f => f.Code.HeadlineRank).Select(f => f.Title).ToList();
                var parts = named.Take(2).ToList();
                if (named.Count > 2)
                    parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_MoreFormat, named.Count - 2));
                if (toCheck.Count > 0)
                    parts.Add(Count(Strings.Check_ToCheckFormat, toCheck.Count));
                subtitle = string.Join(Strings.RouteCheck_Separator, parts);
            }
        }
        else if (kind == RouteCheckVerdictKind.WaterLevelEndsBeforeArrival && result is not null)
        {
            tone = CheckTone.Neutral;
            title = PassageCheckViewModel.VerdictTitle(result);
            subtitle = Where(snapshot!, timeFormat);
        }
        else if (kind == RouteCheckVerdictKind.Check && toCheck.Count > 0)
        {
            tone = CheckTone.Warning;
            title = !hasDraft ? Count(Strings.Check_ToCheckFormat, toCheck.Count)
                : result is { Verdict: PassageVerdict.IncompleteData } ? PassageCheckViewModel.VerdictTitle(result)
                : string.Format(CultureInfo.CurrentCulture, Strings.Check_VerdictCheckFormat, toCheck.Count);
            subtitle = toCheck[0].Title;
        }
        else if (result is not null)
        {
            tone = PassageCheckViewModel.ToneOf(result.Verdict);
            title = PassageCheckViewModel.VerdictTitle(result);
            subtitle = Where(snapshot!, timeFormat);
        }
        else
        {
            tone = CheckTone.Success;
            title = Strings.Check_NoDraftVerdictNone;
            subtitle = Strings.Check_ClearNoDraft;
        }

        var rows = new List<RouteCheckSummaryRow>
        {
            ClearanceRow(hasDraft, result, depthUnit),
            RouteRow(findings.Where(f => FilterOf(f) == FindingsFilter.Route).ToList()),
        };
        if (warningsChecked)
            rows.Add(WarningsRow(findings.Where(f => FilterOf(f) == FindingsFilter.Warnings).ToList()));

        var footer = products.Count == 0
            ? Strings.Check_FooterNoData
            : string.Format(CultureInfo.CurrentCulture, Strings.Check_FooterFormat, string.Join(", ", products));
        return new RouteCheckCard(kind, tone, title, subtitle, rows, footer,
            findings.Any(f => f.Category is RouteFindingCategory.Chart or RouteFindingCategory.Depth or RouteFindingCategory.Area));
    }

    /// <summary>
    /// The no-draft title for a findings list (and get_findings' summary):
    /// "N to review", "N to check" or "No findings for your settings".
    /// </summary>
    internal static string NoDraftTitle(IReadOnlyList<RouteFinding> findings)
    {
        var review = findings.Count(f => f.Group == RouteFindingGroup.MustReview);
        if (review > 0)
            return Count(Strings.Check_ToReviewFormat, review);
        var check = findings.Count(f => f.Group == RouteFindingGroup.Check);
        return check > 0 ? Count(Strings.Check_ToCheckFormat, check) : Strings.Check_NoDraftVerdictNone;
    }

    /// <summary>Which filter chip a finding belongs to (D1): leaving chart coverage is the route's, the clearance check's data gaps are clearance.</summary>
    internal static FindingsFilter FilterOf(RouteFinding finding) => finding.Category switch
    {
        RouteFindingCategory.Warning => FindingsFilter.Warnings,
        RouteFindingCategory.Chart or RouteFindingCategory.Depth or RouteFindingCategory.Area => FindingsFilter.Route,
        _ when finding.Code == RouteFindingCodes.OutsideChartCoverage => FindingsFilter.Route,
        _ => FindingsFilter.Clearance,
    };

    /// <summary>
    /// The products the check used, in footer order: the clearance check's
    /// own sources, and the S-101, S-102, S-122 and S-124 datasets over the
    /// route that the route checks read.
    /// </summary>
    internal static IReadOnlyList<string> Products(PassageSources? sources, IReadOnlyList<LoadedDataset> near)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sources is not null)
        {
            if (sources.BathymetryDatasets.Count > 0) used.Add("S-102");
            if (sources.FallbackDatasets.Count > 0) used.Add("S-101");
            if (sources.WaterLevelSources.Count > 0) used.Add("S-104");
        }

        foreach (var dataset in near)
        {
            foreach (var name in (string[])["S-101", "S-102", "S-122", "S-124"])
            {
                if (IsSpec(dataset, name))
                    used.Add(name);
            }
        }

        return FooterProducts.Where(used.Contains).ToList();
    }

    private static bool IsSpec(LoadedDataset dataset, string name) =>
        string.Equals(dataset.Spec.Name, name, StringComparison.OrdinalIgnoreCase);

    private static RouteCheckSummaryRow ClearanceRow(bool hasDraft, PassageCheckResult? result, DepthUnit depthUnit)
    {
        if (!hasDraft || result is null)
            return new RouteCheckSummaryRow(FindingsFilter.Clearance, Strings.Check_SummaryClearance, Strings.Check_SummaryNotChecked, null, CheckTone.Neutral, AddDraft: true);

        if (result.Minimum is not { } min)
            return new RouteCheckSummaryRow(FindingsFilter.Clearance, Strings.Check_SummaryClearance, Strings.Check_NoClearance, null, CheckTone.Neutral);

        var minText = PassageFormatting.Format(min.ClearanceMeters, PassageQuantity.Depth, depthUnit);
        var text = result.NoMarginSet
            ? string.Format(CultureInfo.CurrentCulture, Strings.Check_SummaryClearanceNoMarginFormat, minText)
            : string.Format(CultureInfo.CurrentCulture, Strings.Check_SummaryClearanceFormat, minText,
                PassageFormatting.Format(min.RequiredUkcMeters, PassageQuantity.Depth, depthUnit));
        var (chip, tone) = result.Verdict switch
        {
            PassageVerdict.BelowSetting => (string.Format(CultureInfo.CurrentCulture, Strings.Check_ChipBelowFormat, result.PlacesBelow), CheckTone.Destructive),
            PassageVerdict.MeetsSettingsTight => (Strings.Check_ChipTight, CheckTone.Warning),
            _ when result.Sources.WaterLevelSources.Count == 0 => (Strings.Check_ChipNoTide, CheckTone.Neutral),
            PassageVerdict.MeetsSettings => (Strings.Check_ChipMeets, CheckTone.Success),
            _ => ((string?)null, CheckTone.Neutral),
        };
        return new RouteCheckSummaryRow(FindingsFilter.Clearance, Strings.Check_SummaryClearance, text, chip, tone);
    }

    private static RouteCheckSummaryRow RouteRow(IReadOnlyList<RouteFinding> route)
    {
        var review = route.Count(f => f.Group == RouteFindingGroup.MustReview);
        var check = route.Count(f => f.Group == RouteFindingGroup.Check);
        if (review + check == 0)
            return new RouteCheckSummaryRow(FindingsFilter.Route, Strings.Check_SummaryRoute, Strings.Check_SummaryNothingFound, "0", CheckTone.Success);

        var parts = new List<string>();
        if (review > 0) parts.Add(Count(Strings.Check_ToReviewFormat, review));
        if (check > 0) parts.Add(Count(Strings.Check_ToCheckFormat, check));
        return new RouteCheckSummaryRow(
            FindingsFilter.Route,
            Strings.Check_SummaryRoute,
            string.Join(Strings.RouteCheck_Separator, parts),
            (review + check).ToString(CultureInfo.CurrentCulture),
            review > 0 ? CheckTone.Destructive : CheckTone.Warning);
    }

    private static RouteCheckSummaryRow WarningsRow(IReadOnlyList<RouteFinding> warnings)
    {
        var onRoute = warnings.Count(f => f.Group == RouteFindingGroup.MustReview);
        var near = warnings.Count(f => f.Group == RouteFindingGroup.Check);
        if (onRoute + near == 0)
            return new RouteCheckSummaryRow(FindingsFilter.Warnings, Strings.Check_SummaryWarnings, Strings.Check_SummaryNothingOnRoute, null, CheckTone.Neutral);

        var text = onRoute == 0
            ? Count(Strings.Check_SummaryNearRouteFormat, near)
            : near == 0
                ? Count(Strings.Check_SummaryInForceFormat, onRoute)
                : Count(Strings.Check_SummaryInForceFormat, onRoute) + Strings.RouteCheck_Separator + Count(Strings.Check_SummaryNearFormat, near);
        return new RouteCheckSummaryRow(
            FindingsFilter.Warnings,
            Strings.Check_SummaryWarnings,
            text,
            (onRoute + near).ToString(CultureInfo.CurrentCulture),
            onRoute > 0 ? CheckTone.Destructive : CheckTone.Warning);
    }

    /// <summary>Where and when the minimum UKC is: "Leg 6 → 7 · Thorn Channel · 07:52 · 12 min below".</summary>
    internal static string? Where(PassageCheckSnapshot current, TimeFormat timeFormat)
    {
        if (current.Result.Minimum is not { } min)
            return null;
        var parts = new List<string>
        {
            string.Format(CultureInfo.CurrentCulture, Strings.Check_LegFormat, min.LegIndex + 1, min.LegIndex + 2),
        };
        if (current.MinimumPlaceName is { } place)
            parts.Add(place);
        if (min.HasEta)
            parts.Add(PassageFormatting.FormatEta(min.Time, current.Result.Departure, timeFormat));
        var minutes = (int)Math.Round(current.Result.MinutesBelow);
        if (minutes > 0)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Check_MinutesBelowFormat, minutes));
        return string.Join(" · ", parts);
    }

    private static string Count(string format, int n) => string.Format(CultureInfo.CurrentCulture, format, n);
}
