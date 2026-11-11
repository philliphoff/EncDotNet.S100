namespace EncDotNet.S100.Navigation;

/// <summary>What <see cref="ClearanceCheck"/> found besides its findings: the clearance and the departure sweep.</summary>
/// <param name="Clearance">The clearance at the plan's departure.</param>
/// <param name="Departures">The sweep of <see cref="RouteCheckSettings.DepartureWindow"/>, or <c>null</c> without one or without water-level data.</param>
public sealed record ClearanceCheckResult(PassageCheckResult Clearance, DepartureSweep? Departures);

/// <summary>
/// The clearance check, built into <see cref="RouteChecker"/>: under-keel
/// clearance over <see cref="RouteCheckSources.Depth"/> and
/// <see cref="RouteCheckSources.WaterLevel"/>, with its findings from
/// <see cref="ClearanceFindings"/>.
/// </summary>
/// <remarks>
/// <para>
/// At each sample's ETA, <c>UKC = charted depth + water level − draft − squat</c>,
/// compared with the required UKC (0 m when unset). Charted depth comes from
/// S-102 where it covers the route, otherwise from S-101 (marked low
/// confidence). Water level comes from S-104 at the ETA; outside the S-104
/// extent or time range the sample is "no data" ("water level not applied"), never
/// a charted-depth-only pass. The depth and water level must share a
/// vertical datum (<see cref="VerticalDatumCheck"/>). With
/// <see cref="RouteCheckSettings.DepartureWindow"/> it also sweeps departures
/// (<see cref="ClearanceCheckResult.Departures"/>).
/// </para>
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
/// departure alone doesn't change. Its <see cref="RouteCheckOutcome{TDetail}.Detail"/>
/// is a <see cref="ClearanceCheckResult"/>.
/// </para>
/// <para>This is an aid for exploring the data. It isn't for navigation.</para>
/// </remarks>
public sealed partial class ClearanceCheck : IRouteCheck<ClearanceCheckResult>
{
    /// <summary>The check's <see cref="Name"/>.</summary>
    public const string CheckName = "clearance";

    private readonly object _gate = new();
    private (RouteCheckSources Sources, PassageCheckOptions Options, PassageDepthProfile Profile)? _profile;
    private (PassageDepthProfile Profile, IReadOnlyList<PassageLeg> Legs, DepartureRange Range, DepartureSweep? Sweep)? _sweep;

    /// <inheritdoc/>
    public string Name => CheckName;

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">The <see cref="PassageCheckOptions"/> have a step, spacing or factor that isn't positive.</exception>
    public RouteCheckOutcome<ClearanceCheckResult> Run(RouteCheckContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Sources.Depth is not { } depth)
        {
            return RouteCheckOutcome<ClearanceCheckResult>.Skipped(RouteCheckReasons.DepthMissing);
        }

        if (context.Sources.WaterLevel is not { } waterLevel)
        {
            return RouteCheckOutcome<ClearanceCheckResult>.Skipped(RouteCheckReasons.WaterLevelMissing);
        }

        var legs = context.Plan.Legs;
        if (legs.Any(l => l.Draft is null))
        {
            return RouteCheckOutcome<ClearanceCheckResult>.Skipped(RouteCheckReasons.DraftMissing);
        }

        var options = context.Options.Clearance;
        var model = new UkcModel(depth, waterLevel, options);
        PassageDepthProfile? reuse = null;
        lock (_gate)
        {
            if (_profile is { } last && ReferenceEquals(last.Sources, context.Sources) && last.Options == options)
            {
                reuse = last.Profile;
            }
        }

        var profile = reuse is not null && reuse.Matches(legs) ? reuse : model.SampleDepths(legs, reuse, cancellationToken);
        lock (_gate)
        {
            _profile = (context.Sources, options, profile);
        }

        var result = model.Evaluate(profile, context.Plan, context.Settings, cancellationToken);
        var departures = context.Settings.DepartureWindow is { } range ? Sweep(model, profile, legs, range, cancellationToken) : null;
        var findings = ClearanceFindings.Build(context.Plan, result, context.Options.Formatter);
        return RouteCheckOutcome<ClearanceCheckResult>.Ran(findings, new ClearanceCheckResult(result, departures));
    }

    /// <inheritdoc/>
    RouteCheckOutcome IRouteCheck.Run(RouteCheckContext context, CancellationToken cancellationToken) => Run(context, cancellationToken);

    private DepartureSweep? Sweep(UkcModel model, PassageDepthProfile profile, IReadOnlyList<PassageLeg> legs, DepartureRange range, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_sweep is { } last && ReferenceEquals(last.Profile, profile) && last.Range == range && PassageLegs.Same(last.Legs, legs))
            {
                return last.Sweep;
            }
        }

        var sweep = range is { From: { } from, To: { } to }
            ? model.SweepDepartures(profile, legs, from, to, range.Step, cancellationToken)
            : model.SweepWaterLevelRange(profile, legs, range.Step, range.MaxDepartures, cancellationToken);
        lock (_gate)
        {
            _sweep = (profile, legs.ToArray(), range, sweep);
        }

        return sweep;
    }
}
