using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>The verdict of a route check, worst first.</summary>
public enum RouteCheckVerdictKind
{
    /// <summary>A MUST REVIEW finding from any check, or the clearance is below your UKC setting.</summary>
    MustReview,

    /// <summary>Arrival is after the water-level data ends, and nothing is MUST REVIEW.</summary>
    WaterLevelEndsBeforeArrival,

    /// <summary>CHECK findings, a tight or incomplete clearance, and nothing worse.</summary>
    Check,

    /// <summary>Nothing to review or check.</summary>
    Clear,
}

/// <summary>The one answer of a route check, and the finding that names it.</summary>
/// <param name="Kind">The worst thing the checks found.</param>
/// <param name="Headline">
/// The finding that names the verdict, or <c>null</c> when none does: for
/// <see cref="RouteCheckVerdictKind.MustReview"/>, the MUST REVIEW finding
/// with the lowest <see cref="RouteFindingCode.HeadlineRank"/> (the first in
/// list order among equals); for <see cref="RouteCheckVerdictKind.WaterLevelEndsBeforeArrival"/>,
/// the <see cref="RouteFindingCodes.WaterLevelEnds"/> finding; for
/// <see cref="RouteCheckVerdictKind.Check"/>, the first CHECK finding.
/// </param>
public sealed record RouteCheckVerdict(RouteCheckVerdictKind Kind, RouteFinding? Headline)
{
    /// <summary>Derives the verdict from sorted findings and the clearance result.</summary>
    /// <param name="findings">Every finding, sorted (<see cref="RouteFinding.Sort"/>).</param>
    /// <param name="clearance">The clearance result, or <c>null</c> when the clearance check didn't run.</param>
    /// <returns>The verdict.</returns>
    public static RouteCheckVerdict From(IReadOnlyList<RouteFinding> findings, PassageCheckResult? clearance)
    {
        ArgumentNullException.ThrowIfNull(findings);
        RouteFinding? headline = null;
        foreach (var finding in findings)
        {
            if (finding.Group == RouteFindingGroup.MustReview && (headline is null || finding.Code.HeadlineRank < headline.Code.HeadlineRank))
            {
                headline = finding;
            }
        }

        if (headline is not null || clearance is { Verdict: PassageVerdict.BelowSetting })
        {
            return new(RouteCheckVerdictKind.MustReview, headline);
        }

        if (clearance is { Verdict: PassageVerdict.WaterLevelEndsBeforeArrival })
        {
            return new(RouteCheckVerdictKind.WaterLevelEndsBeforeArrival, findings.FirstOrDefault(f => f.Code == RouteFindingCodes.WaterLevelEnds));
        }

        if (findings.FirstOrDefault(f => f.Group == RouteFindingGroup.Check) is { } check)
        {
            return new(RouteCheckVerdictKind.Check, check);
        }

        return clearance is { Verdict: not PassageVerdict.MeetsSettings }
            ? new(RouteCheckVerdictKind.Check, null)
            : new(RouteCheckVerdictKind.Clear, null);
    }
}

/// <summary>The number of findings in each group.</summary>
/// <param name="MustReview">MUST REVIEW findings.</param>
/// <param name="Check">CHECK findings.</param>
/// <param name="Info">INFO findings.</param>
public sealed record RouteFindingCounts(int MustReview, int Check, int Info)
{
    /// <summary>Counts findings by group.</summary>
    /// <param name="findings">The findings.</param>
    /// <returns>The counts.</returns>
    public static RouteFindingCounts Of(IEnumerable<RouteFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        int review = 0, check = 0, info = 0;
        foreach (var finding in findings)
        {
            switch (finding.Group)
            {
                case RouteFindingGroup.MustReview:
                    review++;
                    break;
                case RouteFindingGroup.Check:
                    check++;
                    break;
                default:
                    info++;
                    break;
            }
        }

        return new(review, check, info);
    }
}

/// <summary>
/// Everything one <see cref="RouteChecker.Check"/> run found: the findings of
/// every check in one sorted list, the verdict, whether each check ran, and
/// each built-in check's own result.
/// </summary>
/// <remarks>This is an aid for exploring the data. It isn't for navigation.</remarks>
public sealed class RouteCheckReport
{
    private readonly IReadOnlyDictionary<string, RouteCheckOutcome> _outcomes;

    internal RouteCheckReport(RouteCheckContext context, IReadOnlyList<(IRouteCheck Check, RouteCheckOutcome Outcome)> outcomes)
    {
        Plan = context.Plan;
        Settings = context.Settings;
        Schedule = context.Schedule;
        Track = context.Track;
        Findings = RouteFinding.Sort(outcomes.SelectMany(o => o.Outcome.Findings));
        Counts = RouteFindingCounts.Of(Findings);
        Checks = outcomes.Select(o => new RouteCheckStatus(o.Check.Name, o.Outcome.State, o.Outcome.Reason, o.Outcome.Findings.Count)).ToArray();
        var byName = new Dictionary<string, RouteCheckOutcome>(StringComparer.Ordinal);
        foreach (var (check, outcome) in outcomes)
        {
            byName.TryAdd(check.Name, outcome);
        }

        _outcomes = byName;
        Chart = Detail<RouteCheckResult>(ChartCheck.CheckName);
        var clearance = Detail<ClearanceOutcome>(ClearanceCheck.CheckName);
        Clearance = clearance?.Result;
        Departures = clearance?.Departures;
        Areas = Detail<RouteAreaResult>(AreaCheck.CheckName)?.Matches ?? [];
        Warnings = Detail<RouteWarningResult>(WarningCheck.CheckName)?.Matches ?? [];
        Verdict = RouteCheckVerdict.From(Findings, Clearance);
        DatasetsUsed = Datasets(context.Sources);
    }

    /// <summary>The plan that was checked.</summary>
    public PassagePlan Plan { get; }

    /// <summary>The settings it was checked with.</summary>
    public RouteCheckSettings Settings { get; }

    /// <summary>The ETAs the checks judged times with.</summary>
    public PassageSchedule Schedule { get; }

    /// <summary>The plan's track.</summary>
    public RouteTrack Track { get; }

    /// <summary>Every check's findings, sorted by group (MUST REVIEW, CHECK, INFO) and then by distance from departure.</summary>
    public IReadOnlyList<RouteFinding> Findings { get; }

    /// <summary>The number of findings in each group.</summary>
    public RouteFindingCounts Counts { get; }

    /// <summary>The verdict and the finding that names it.</summary>
    public RouteCheckVerdict Verdict { get; }

    /// <summary>Whether each check ran, in the order they ran.</summary>
    public IReadOnlyList<RouteCheckStatus> Checks { get; }

    /// <summary>The chart check's result, with each leg's corridor, or <c>null</c> when it didn't run.</summary>
    public RouteCheckResult? Chart { get; }

    /// <summary>The clearance check's result, or <c>null</c> when it didn't run.</summary>
    public PassageCheckResult? Clearance { get; }

    /// <summary>The sweep of <see cref="RouteCheckSettings.DepartureWindow"/>, or <c>null</c>.</summary>
    public DepartureSweep? Departures { get; }

    /// <summary>Every area near the route, in force or not; empty when the area check didn't run.</summary>
    public IReadOnlyList<RouteAreaMatch> Areas { get; }

    /// <summary>Every warning near the route, in force or not; empty when the warning check didn't run.</summary>
    public IReadOnlyList<RouteWarningMatch> Warnings { get; }

    /// <summary>
    /// The ids of the datasets behind the report, sorted: the chart cells
    /// along the corridors, the datasets behind the findings, and the depth
    /// and water-level datasets the clearance check read.
    /// </summary>
    public IReadOnlyList<string> DatasetsUsed { get; }

    /// <summary>The status of the check named <paramref name="name"/>, or <c>null</c> when no check has that name.</summary>
    /// <param name="name">The check's <see cref="IRouteCheck.Name"/>.</param>
    /// <returns>The status.</returns>
    public RouteCheckStatus? Status(string name) => Checks.FirstOrDefault(c => c.Name == name);

    /// <summary>
    /// The <see cref="RouteCheckOutcome.Detail"/> of the check named
    /// <paramref name="name"/>, or <c>null</c> when it didn't run or its
    /// detail isn't a <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The detail's type.</typeparam>
    /// <param name="name">The check's <see cref="IRouteCheck.Name"/>.</param>
    /// <returns>The detail.</returns>
    public T? Detail<T>(string name)
        where T : class =>
        _outcomes.TryGetValue(name, out var outcome) ? outcome.Detail as T : null;

    private IReadOnlyList<string> Datasets(RouteCheckSources sources)
    {
        var datasets = new SortedSet<string>(StringComparer.Ordinal);
        if (sources.Charts is { } charts && Chart is { } chart)
        {
            foreach (var corridor in chart.Corridors)
            {
                foreach (var cell in charts.CellsNear(Extent(corridor.Ring)))
                {
                    datasets.Add(cell.DatasetId);
                }
            }
        }

        foreach (var finding in Findings)
        {
            if (finding.Feature is { } feature)
            {
                datasets.Add(feature.DatasetId);
            }
        }

        if (Clearance?.Sources is { } used)
        {
            datasets.UnionWith(used.BathymetryDatasets);
            datasets.UnionWith(used.FallbackDatasets);
            datasets.UnionWith(used.WaterLevelSources.Select(s => s.DatasetId));
        }

        return datasets.ToArray();
    }

    private static BoundingBox Extent(IReadOnlyList<GeoPosition> ring) => new(
        ring.Min(p => p.Latitude),
        ring.Min(p => p.Longitude),
        ring.Max(p => p.Latitude),
        ring.Max(p => p.Longitude));
}
