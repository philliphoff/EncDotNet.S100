namespace EncDotNet.S100.Navigation;

/// <summary>
/// What every check of one <see cref="RouteChecker.Check"/> run reads: the
/// plan, settings, sources and options, and the track and schedule built
/// once for all of them.
/// </summary>
public sealed class RouteCheckContext
{
    /// <summary>Builds the context for one run, with its track and schedule.</summary>
    /// <param name="plan">The plan to check.</param>
    /// <param name="settings">The settings for this run.</param>
    /// <param name="sources">The loaded data.</param>
    /// <param name="options">Tuning, or <c>null</c> for <see cref="RouteCheckOptions.Default"/>.</param>
    /// <exception cref="ArgumentException">A leg has fewer than two points, or a negative or non-finite speed.</exception>
    public RouteCheckContext(PassagePlan plan, RouteCheckSettings settings, RouteCheckSources sources, RouteCheckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sources);
        Plan = plan;
        Settings = settings;
        Sources = sources;
        Options = options ?? RouteCheckOptions.Default;
        Track = RouteTrack.Build(plan, settings, Options.DefaultLimit);
        Schedule = PassageSchedule.Compute(plan, settings);
    }

    /// <summary>The plan to check.</summary>
    public PassagePlan Plan { get; }

    /// <summary>The settings for this run.</summary>
    public RouteCheckSettings Settings { get; }

    /// <summary>The loaded data.</summary>
    public RouteCheckSources Sources { get; }

    /// <summary>The tuning.</summary>
    public RouteCheckOptions Options { get; }

    /// <summary>The plan's track, shared by every check.</summary>
    public RouteTrack Track { get; }

    /// <summary>
    /// The plan's ETAs. Where a leg has none, the time is the plan's
    /// departure, or <see cref="RouteCheckSettings.At"/> without one.
    /// </summary>
    public PassageSchedule Schedule { get; }
}
