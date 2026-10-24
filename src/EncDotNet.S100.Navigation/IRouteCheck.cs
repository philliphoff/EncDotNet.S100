namespace EncDotNet.S100.Navigation;

/// <summary>
/// One check a <see cref="RouteChecker"/> runs: the chart, area, warning and
/// clearance checks are built in, and a host can add its own.
/// </summary>
/// <remarks>
/// <para>
/// A check reads the plan, settings, sources and shared track from the
/// <see cref="RouteCheckContext"/>. It says when it can't run, with a reason,
/// rather than returning no findings (<see cref="RouteCheckOutcome.Skipped"/>).
/// </para>
/// <para>
/// One check object serves every run of its checker, possibly on several
/// threads at once, so any state it keeps between runs (a cache) must be
/// safe for that.
/// </para>
/// </remarks>
public interface IRouteCheck
{
    /// <summary>The check's name in the report, such as <c>"chart"</c> or <c>"clearance"</c>.</summary>
    string Name { get; }

    /// <summary>Runs the check.</summary>
    /// <param name="context">The plan, settings, sources and shared track.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The findings, or why the check was skipped.</returns>
    RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A check whose outcome carries its own typed result, such as
/// <see cref="ChartCheck"/> with its <see cref="ChartCheckResult"/>, so a
/// caller that runs it directly reads the result without a cast.
/// </summary>
/// <typeparam name="TDetail">The check's own result.</typeparam>
public interface IRouteCheck<TDetail> : IRouteCheck
    where TDetail : class
{
    /// <summary>Runs the check.</summary>
    /// <param name="context">The plan, settings, sources and shared track.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The findings and the check's own result, or why the check was skipped.</returns>
    new RouteCheckOutcome<TDetail> Run(RouteCheckContext context, CancellationToken cancellationToken);
}

/// <summary>Whether a check ran.</summary>
public enum RouteCheckState
{
    /// <summary>The check ran in full.</summary>
    Ran,

    /// <summary>
    /// The check ran, but part of it couldn't, such as the chart check's
    /// depth checks without a safety contour; <see cref="RouteCheckStatus.Reason"/> says why.
    /// </summary>
    Partial,

    /// <summary>The check didn't run; <see cref="RouteCheckStatus.Reason"/> says why.</summary>
    Skipped,
}

/// <summary>
/// Why a built-in check was skipped or ran only in part. A custom check may
/// give its own reasons.
/// </summary>
public static class RouteCheckReasons
{
    /// <summary>No safety contour and safety depth were set, so the depth and danger checks didn't run.</summary>
    public const string SafetySettingsMissing = "safety_settings_missing";

    /// <summary>A leg has no draft, so the clearance check didn't run.</summary>
    public const string DraftMissing = "draft_missing";

    /// <summary>The sources have no charts.</summary>
    public const string ChartsMissing = "charts_missing";

    /// <summary>The sources have no charted depth.</summary>
    public const string DepthMissing = "depth_missing";

    /// <summary>The sources have no water level.</summary>
    public const string WaterLevelMissing = "water_level_missing";

    /// <summary>The sources have no areas.</summary>
    public const string AreasMissing = "areas_missing";

    /// <summary>The sources have no navigational warnings.</summary>
    public const string WarningsMissing = "warnings_missing";
}

/// <summary>What one check found, or why it didn't run.</summary>
/// <remarks>
/// A check with a typed result returns a <see cref="RouteCheckOutcome{TDetail}"/>.
/// </remarks>
public class RouteCheckOutcome
{
    private protected RouteCheckOutcome(RouteCheckState state, string? reason, IReadOnlyList<RouteFinding> findings, object? detail)
    {
        State = state;
        Reason = reason;
        Findings = findings;
        Detail = detail;
    }

    /// <summary>Whether the check ran.</summary>
    public RouteCheckState State { get; }

    /// <summary>Why the check was skipped or ran only in part, such as <see cref="RouteCheckReasons.DraftMissing"/>; <c>null</c> when it ran in full.</summary>
    public string? Reason { get; }

    /// <summary>The findings, in any order; empty when skipped.</summary>
    public IReadOnlyList<RouteFinding> Findings { get; }

    /// <summary>
    /// The check's own result, such as a <see cref="ChartCheckResult"/> for
    /// the chart check, or <c>null</c>. <see cref="RouteCheckOutcome{TDetail}.Detail"/>
    /// gives it typed.
    /// </summary>
    public object? Detail { get; }

    /// <summary>A check that ran in full.</summary>
    /// <param name="findings">The findings.</param>
    /// <param name="detail">The check's own result, or <c>null</c>.</param>
    /// <returns>The outcome.</returns>
    public static RouteCheckOutcome Ran(IReadOnlyList<RouteFinding> findings, object? detail = null)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return new(RouteCheckState.Ran, null, findings, detail);
    }

    /// <summary>A check that ran, but part of it couldn't.</summary>
    /// <param name="reason">Why part of it didn't run.</param>
    /// <param name="findings">The findings of the part that ran.</param>
    /// <param name="detail">The check's own result, or <c>null</c>.</param>
    /// <returns>The outcome.</returns>
    public static RouteCheckOutcome Partial(string reason, IReadOnlyList<RouteFinding> findings, object? detail = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        ArgumentNullException.ThrowIfNull(findings);
        return new(RouteCheckState.Partial, reason, findings, detail);
    }

    /// <summary>A check that didn't run.</summary>
    /// <param name="reason">Why, such as <see cref="RouteCheckReasons.DraftMissing"/>.</param>
    /// <returns>The outcome.</returns>
    public static RouteCheckOutcome Skipped(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new(RouteCheckState.Skipped, reason, [], null);
    }
}

/// <summary>What one check found, with its own typed result, or why it didn't run.</summary>
/// <typeparam name="TDetail">The check's own result, such as <see cref="ChartCheckResult"/>.</typeparam>
public sealed class RouteCheckOutcome<TDetail> : RouteCheckOutcome
    where TDetail : class
{
    private RouteCheckOutcome(RouteCheckState state, string? reason, IReadOnlyList<RouteFinding> findings, TDetail? detail)
        : base(state, reason, findings, detail)
    {
        Detail = detail;
    }

    /// <summary>The check's own result; <c>null</c> only when the check was skipped.</summary>
    public new TDetail? Detail { get; }

    /// <summary>A check that ran in full.</summary>
    /// <param name="findings">The findings.</param>
    /// <param name="detail">The check's own result.</param>
    /// <returns>The outcome.</returns>
    public static RouteCheckOutcome<TDetail> Ran(IReadOnlyList<RouteFinding> findings, TDetail detail)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(detail);
        return new(RouteCheckState.Ran, null, findings, detail);
    }

    /// <summary>A check that ran, but part of it couldn't.</summary>
    /// <param name="reason">Why part of it didn't run.</param>
    /// <param name="findings">The findings of the part that ran.</param>
    /// <param name="detail">The check's own result for the part that ran.</param>
    /// <returns>The outcome.</returns>
    public static RouteCheckOutcome<TDetail> Partial(string reason, IReadOnlyList<RouteFinding> findings, TDetail detail)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(detail);
        return new(RouteCheckState.Partial, reason, findings, detail);
    }

    /// <summary>A check that didn't run.</summary>
    /// <param name="reason">Why, such as <see cref="RouteCheckReasons.DraftMissing"/>.</param>
    /// <returns>The outcome.</returns>
    public static new RouteCheckOutcome<TDetail> Skipped(string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        return new(RouteCheckState.Skipped, reason, [], null);
    }
}

/// <summary>Whether one check of a <see cref="RouteCheckReport"/> ran, and why not.</summary>
/// <param name="Name">The check's <see cref="IRouteCheck.Name"/>.</param>
/// <param name="State">Whether it ran.</param>
/// <param name="Reason">Why it was skipped or ran only in part; <c>null</c> when it ran in full.</param>
/// <param name="FindingCount">The number of findings it gave.</param>
public sealed record RouteCheckStatus(string Name, RouteCheckState State, string? Reason, int FindingCount);
