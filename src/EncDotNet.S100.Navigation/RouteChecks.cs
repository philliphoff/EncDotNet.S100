namespace EncDotNet.S100.Navigation;

/// <summary>
/// The chart check, built into <see cref="RouteChecker"/>: land, drying
/// ground, chart coverage, the safety contour, soundings, S-102 shoals and
/// charted dangers, from <see cref="RouteCheckEngine"/> over
/// <see cref="RouteCheckSources.Charts"/>.
/// </summary>
/// <remarks>
/// <para>
/// Skipped without charts (<see cref="RouteCheckReasons.ChartsMissing"/>).
/// Without a safety contour and safety depth it runs only in part
/// (<see cref="RouteCheckReasons.SafetySettingsMissing"/>): land, drying
/// ground and coverage are checked; depths and dangers aren't.
/// </para>
/// <para>
/// Its findings don't depend on time, so it keeps the last result and reuses
/// it while the legs, the depth settings, the corridor override and the
/// sources are unchanged: changing only the departure doesn't sweep the
/// charts again. Its <see cref="RouteCheckOutcome.Detail"/> is the
/// <see cref="RouteCheckResult"/>.
/// </para>
/// </remarks>
public sealed class ChartCheck : IRouteCheck
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "chart";

    private readonly object _gate = new();
    private (RouteCheckSources Sources, ChartCheckOptions Options, IReadOnlyList<PassageLeg> Legs, RouteCheckSettings Settings, RouteCheckResult Result)? _last;

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Charts is not { } charts)
        {
            return RouteCheckOutcome.Skipped(RouteCheckReasons.ChartsMissing);
        }

        var options = context.Options.EffectiveChart;
        var settings = context.Settings;
        RouteCheckResult? result = null;
        lock (_gate)
        {
            if (_last is { } last
                && ReferenceEquals(last.Sources, context.Sources)
                && last.Options == options
                && last.Settings.SafetyContour == settings.SafetyContour
                && last.Settings.SafetyDepth == settings.SafetyDepth
                && last.Settings.CorridorOverride == settings.CorridorOverride
                && PassageLegs.Same(last.Legs, context.Plan.Legs))
            {
                result = last.Result;
            }
        }

        if (result is null)
        {
            result = new RouteCheckEngine(charts, options, context.Sources.Depth).Check(context.Plan, settings, cancellationToken);
            lock (_gate)
            {
                _last = (context.Sources, options, context.Plan.Legs.ToArray(), settings, result);
            }
        }

        return settings.ChecksDepths
            ? RouteCheckOutcome.Ran(result.Findings, result)
            : RouteCheckOutcome.Partial(RouteCheckReasons.SafetySettingsMissing, result.Findings, result);
    }
}

/// <summary>
/// The area check, built into <see cref="RouteChecker"/>: S-101 regulated
/// areas and S-122 protected areas, from <see cref="RouteAreaCheck"/> over
/// <see cref="RouteCheckSources.Areas"/>, on the shared track.
/// </summary>
/// <remarks>
/// Skipped without areas (<see cref="RouteCheckReasons.AreasMissing"/>). Its
/// <see cref="RouteCheckOutcome.Detail"/> is the <see cref="RouteAreaResult"/>.
/// </remarks>
public sealed class AreaCheck : IRouteCheck
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "areas";

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Areas is not { } areas)
        {
            return RouteCheckOutcome.Skipped(RouteCheckReasons.AreasMissing);
        }

        var result = new RouteAreaCheck(context.Options.EffectiveAreas).Check(context.Track, context.Schedule, areas, cancellationToken);
        return RouteCheckOutcome.Ran(result.Findings, result);
    }
}

/// <summary>
/// The warning check, built into <see cref="RouteChecker"/>: S-124
/// navigational warnings, from <see cref="RouteWarningCheck"/> over
/// <see cref="RouteCheckSources.Warnings"/>, on the shared track.
/// </summary>
/// <remarks>
/// Skipped without warnings (<see cref="RouteCheckReasons.WarningsMissing"/>).
/// Its <see cref="RouteCheckOutcome.Detail"/> is the <see cref="RouteWarningResult"/>.
/// </remarks>
public sealed class WarningCheck : IRouteCheck
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "warnings";

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Warnings is not { } warnings)
        {
            return RouteCheckOutcome.Skipped(RouteCheckReasons.WarningsMissing);
        }

        var result = new RouteWarningCheck(context.Options.EffectiveWarnings).Check(context.Track, context.Schedule, warnings, cancellationToken);
        return RouteCheckOutcome.Ran(result.Findings, result);
    }
}

/// <summary>The clearance check's own result: the <see cref="PassageCheckResult"/> and the departure sweep.</summary>
/// <param name="Result">The clearance at the plan's departure.</param>
/// <param name="Departures">The sweep of <see cref="RouteCheckSettings.DepartureWindow"/>, or <c>null</c> without one or without water-level data.</param>
public sealed record ClearanceOutcome(PassageCheckResult Result, DepartureSweep? Departures);

/// <summary>
/// The clearance check, built into <see cref="RouteChecker"/>: under-keel
/// clearance from <see cref="PassageCheckEngine"/> over
/// <see cref="RouteCheckSources.Depth"/> and <see cref="RouteCheckSources.WaterLevel"/>,
/// with its findings from <see cref="ClearanceFindings"/>.
/// </summary>
/// <remarks>
/// <para>
/// Skipped without charted depth or water level
/// (<see cref="RouteCheckReasons.DepthMissing"/>, <see cref="RouteCheckReasons.WaterLevelMissing"/>),
/// or when any leg has no draft (<see cref="RouteCheckReasons.DraftMissing"/>).
/// </para>
/// <para>
/// It keeps the last depth profile and resamples only the legs whose points
/// or geometry changed, so moving a waypoint resamples the two legs that
/// meet there and changing the departure or the vessel values resamples
/// nothing. It also keeps the last departure sweep, which a changed
/// departure alone doesn't change. Its <see cref="RouteCheckOutcome.Detail"/>
/// is a <see cref="ClearanceOutcome"/>.
/// </para>
/// </remarks>
public sealed class ClearanceCheck : IRouteCheck
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "clearance";

    private readonly object _gate = new();
    private (RouteCheckSources Sources, PassageCheckOptions Options, PassageDepthProfile Profile)? _profile;
    private (PassageDepthProfile Profile, IReadOnlyList<PassageLeg> Legs, DepartureRange Range, DepartureSweep? Sweep)? _sweep;

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    public RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Depth is not { } depth)
        {
            return RouteCheckOutcome.Skipped(RouteCheckReasons.DepthMissing);
        }

        if (context.Sources.WaterLevel is not { } waterLevel)
        {
            return RouteCheckOutcome.Skipped(RouteCheckReasons.WaterLevelMissing);
        }

        var legs = context.Plan.Legs;
        if (legs.Any(l => l.Draft is null))
        {
            return RouteCheckOutcome.Skipped(RouteCheckReasons.DraftMissing);
        }

        var options = context.Options.Clearance;
        var engine = new PassageCheckEngine(depth, waterLevel, options);
        PassageDepthProfile? reuse = null;
        lock (_gate)
        {
            if (_profile is { } last && ReferenceEquals(last.Sources, context.Sources) && last.Options == options)
            {
                reuse = last.Profile;
            }
        }

        var profile = reuse is not null && reuse.Matches(legs) ? reuse : engine.SampleDepths(legs, reuse, cancellationToken);
        lock (_gate)
        {
            _profile = (context.Sources, options, profile);
        }

        var result = engine.Evaluate(profile, context.Plan, context.Settings, cancellationToken);
        var departures = context.Settings.DepartureWindow is { } range ? Sweep(engine, profile, legs, range, cancellationToken) : null;
        var findings = ClearanceFindings.Build(context.Plan, result, context.Options.Formatter);
        return RouteCheckOutcome.Ran(findings, new ClearanceOutcome(result, departures));
    }

    private DepartureSweep? Sweep(PassageCheckEngine engine, PassageDepthProfile profile, IReadOnlyList<PassageLeg> legs, DepartureRange range, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_sweep is { } last && ReferenceEquals(last.Profile, profile) && last.Range == range && PassageLegs.Same(last.Legs, legs))
            {
                return last.Sweep;
            }
        }

        var sweep = range is { From: { } from, To: { } to }
            ? engine.SweepDepartures(profile, legs, from, to, range.Step, cancellationToken)
            : engine.SweepWaterLevelRange(profile, legs, range.Step, range.MaxDepartures, cancellationToken);
        lock (_gate)
        {
            _sweep = (profile, legs.ToArray(), range, sweep);
        }

        return sweep;
    }
}

/// <summary>Comparing legs for the checks' caches.</summary>
internal static class PassageLegs
{
    /// <summary>Whether two lists of legs have the same values, leg by leg.</summary>
    public static bool Same(IReadOnlyList<PassageLeg> a, IReadOnlyList<PassageLeg> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Same(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(PassageLeg a, PassageLeg b) =>
        ReferenceEquals(a, b)
        || (a.Geometry == b.Geometry
            && a.PortLimit == b.PortLimit
            && a.StarboardLimit == b.StarboardLimit
            && a.Speed == b.Speed
            && a.Draft == b.Draft
            && a.Squat == b.Squat
            && a.RequiredUkc == b.RequiredUkc
            && a.Name == b.Name
            && a.Points.SequenceEqual(b.Points));
}
