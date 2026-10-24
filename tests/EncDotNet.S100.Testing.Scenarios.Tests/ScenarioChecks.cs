using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>Runs one route check on a scenario's loaded data, the way a <see cref="RouteChecker"/> does.</summary>
internal static class ScenarioChecks
{
    /// <summary>Runs a check once on a fresh context.</summary>
    public static RouteCheckOutcome<T> Run<T>(IRouteCheck<T> check, RouteCheckSources sources, PassagePlan plan, RouteCheckSettings settings, RouteCheckOptions? options = null)
        where T : class =>
        check.Run(new RouteCheckContext(plan, settings, sources, options), TestContext.Current.CancellationToken);

    /// <summary>Runs a fresh <see cref="ChartCheck"/>.</summary>
    public static RouteCheckOutcome<ChartCheckResult> Chart(RouteCheckSources sources, PassagePlan plan, RouteCheckSettings settings, RouteCheckOptions? options = null) =>
        Run(new ChartCheck(), sources, plan, settings, options);

    /// <summary>Runs a fresh <see cref="AreaCheck"/>.</summary>
    public static RouteCheckOutcome<AreaCheckResult> Areas(RouteCheckSources sources, PassagePlan plan, RouteCheckSettings settings) =>
        Run(new AreaCheck(), sources, plan, settings);

    /// <summary>Runs a fresh <see cref="WarningCheck"/>.</summary>
    public static RouteCheckOutcome<WarningCheckResult> Warnings(RouteCheckSources sources, PassagePlan plan, RouteCheckSettings settings, RouteCheckOptions? options = null) =>
        Run(new WarningCheck(), sources, plan, settings, options);

    /// <summary>Runs a fresh <see cref="ClearanceCheck"/> and returns the clearance; every leg needs a draft.</summary>
    public static PassageCheckResult Clearance(RouteCheckSources sources, PassagePlan plan, RouteCheckSettings settings)
    {
        var outcome = Run(new ClearanceCheck(), sources, plan, settings);
        return outcome.Detail?.Clearance ?? throw new InvalidOperationException($"The clearance check was skipped: {outcome.Reason}.");
    }
}

/// <summary>
/// Every warning part, whatever the extent, so each gets a match (off the
/// route or not), judged with a set built from them.
/// </summary>
internal sealed class EveryWarning(IReadOnlyList<RouteWarning> warnings) : IRouteWarningSource
{
    public NavigationalWarningSet Set { get; } = new(warnings.Select(w => w.Info));

    public IReadOnlyList<RouteWarning> WarningsNear(BoundingBox extent) => warnings;
}
