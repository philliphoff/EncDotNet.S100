namespace EncDotNet.S100.Navigation;

/// <summary>
/// Checks a <see cref="PassagePlan"/> against one set of loaded data with
/// every check, and returns one <see cref="RouteCheckReport"/>: the findings,
/// the verdict and whether each check ran.
/// </summary>
/// <remarks>
/// <para>
/// The built-in checks are <see cref="ChartCheck"/>, <see cref="AreaCheck"/>,
/// <see cref="WarningCheck"/> and <see cref="ClearanceCheck"/>, run in that
/// order. Each one says when it can't run, with a reason, rather than
/// finding nothing.
/// </para>
/// <para>
/// Create one checker per <see cref="RouteCheckSources"/> and keep it while
/// the data is loaded: its checks keep what the last run computed and reuse
/// it. Moving a waypoint resamples only the legs that changed; changing the
/// departure re-evaluates the clearance and re-judges the times of areas and
/// warnings without sweeping the charts or sampling the depths again.
/// </para>
/// <para>
/// <see cref="Check"/> may be called from several threads at once.
/// </para>
/// <para>This is an aid for exploring the data. It isn't for navigation.</para>
/// </remarks>
public sealed class RouteChecker
{
    private readonly RouteCheckSources _sources;
    private readonly RouteCheckOptions _options;

    /// <summary>Creates a checker with the built-in checks and any of the host's own.</summary>
    /// <param name="sources">The loaded data.</param>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteCheckOptions.Default"/>.</param>
    /// <param name="additionalChecks">Checks to run after the built-in ones, or <c>null</c>.</param>
    public RouteChecker(RouteCheckSources sources, RouteCheckOptions? options = null, IEnumerable<IRouteCheck>? additionalChecks = null)
        : this(sources, [new ChartCheck(), new AreaCheck(), new WarningCheck(), new ClearanceCheck(), .. additionalChecks ?? []], options)
    {
    }

    /// <summary>Creates a checker that runs exactly <paramref name="checks"/>, in order.</summary>
    /// <param name="sources">The loaded data.</param>
    /// <param name="checks">The checks, such as only a <see cref="ClearanceCheck"/>.</param>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteCheckOptions.Default"/>.</param>
    /// <exception cref="ArgumentException">A check is null, or two have the same name.</exception>
    public RouteChecker(RouteCheckSources sources, IEnumerable<IRouteCheck> checks, RouteCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(checks);
        _sources = sources;
        _options = options ?? RouteCheckOptions.Default;
        Checks = checks.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in Checks)
        {
            if (check is null)
            {
                throw new ArgumentException("A check is null.", nameof(checks));
            }

            if (!names.Add(check.Name))
            {
                throw new ArgumentException($"Two checks are named '{check.Name}'.", nameof(checks));
            }
        }
    }

    /// <summary>The checks, in the order they run.</summary>
    public IReadOnlyList<IRouteCheck> Checks { get; }

    /// <summary>Checks a plan. Synchronous and CPU-bound; the token cancels cooperatively.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="settings">The settings for this run.</param>
    /// <param name="cancellationToken">Cancels the check, for example when the route changes again.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentException">A leg has fewer than two points, or a negative or non-finite speed.</exception>
    public RouteCheckReport Check(PassagePlan plan, RouteCheckSettings settings, CancellationToken cancellationToken = default)
    {
        var context = new RouteCheckContext(plan, settings, _sources, _options);
        var outcomes = new List<(IRouteCheck, RouteCheckOutcome)>(Checks.Count);
        foreach (var check in Checks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = check.Run(context, cancellationToken)
                ?? throw new InvalidOperationException($"The check '{check.Name}' returned no outcome.");
            outcomes.Add((check, outcome));
        }

        return new RouteCheckReport(context, outcomes);
    }
}
