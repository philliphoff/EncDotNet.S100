using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// What a findings producer checks (#953): one route, the data loaded when
/// the check was scheduled, and the formats for titles and meta lines.
/// </summary>
/// <param name="RouteId">The route.</param>
/// <param name="Legs">The route's effective legs, named "1 → 2". Without a draft, <see cref="PassageLeg.DraftMeters"/> is 0.</param>
/// <param name="Departure">The route's departure, or the view time when it has none.</param>
/// <param name="HasDraft">Whether every leg has a draft. Checks that need none (land, warnings, areas) run either way.</param>
/// <param name="Datasets">The loaded datasets.</param>
/// <param name="Clearance">The live clearance check of this route, or <c>null</c> (no draft, or a one-off check). Only producers with <see cref="IRouteFindingProducer.UsesClearance"/> read it.</param>
/// <param name="DepthUnit">The depth unit for depths and clearances.</param>
/// <param name="TimeFormat">The time format for ETAs.</param>
/// <param name="Now">The current time, for expired data.</param>
/// <param name="CheckLegs">The legs with their cross-track limits, one per <paramref name="Legs"/> entry, for the corridor checks.</param>
/// <param name="ViewTime">The view time, which time-dependent checks use where the legs give no ETA.</param>
internal sealed record RouteFindingRequest(
    string RouteId,
    IReadOnlyList<PassageLeg> Legs,
    DateTimeOffset Departure,
    bool HasDraft,
    IReadOnlyList<LoadedDataset> Datasets,
    PassageCheckSnapshot? Clearance,
    DepthUnit DepthUnit,
    TimeFormat TimeFormat,
    DateTimeOffset Now,
    IReadOnlyList<RouteCheckLeg> CheckLegs,
    DateTimeOffset ViewTime);

/// <summary>
/// A findings producer whose inputs go beyond the request, such as the loaded
/// navigational warnings: <see cref="RouteFindingsService"/> re-runs it alone
/// when <see cref="InputsChanged"/> fires (on the UI thread).
/// </summary>
internal interface IRouteFindingInputs
{
    /// <summary>Raised when what the producer would find may have changed.</summary>
    event EventHandler? InputsChanged;
}

/// <summary>
/// One source of findings for a route's findings list (#953). The
/// <see cref="RouteFindingsService"/> runs every producer for the checked
/// route and merges what they find, sorted by group and then by distance
/// (<see cref="PassageFindings.Sort"/>).
/// </summary>
/// <remarks>
/// <para>
/// The service calls <see cref="ProduceAsync"/> on the thread pool, after
/// the same debounce as <see cref="PassageCheckService"/>, whenever the
/// route, its departure, the loaded data or a format changes. A newer
/// request cancels the token of the one before; until the new findings land,
/// the producer's previous findings stay in the list (marked stale), and
/// neither a cancellation nor a failure touches other producers' findings.
/// </para>
/// <para>
/// Finding ids need only be unique within a producer's result, so prefix
/// them with the finding code (e.g. <c>route_check.land.0</c>).
/// </para>
/// </remarks>
internal interface IRouteFindingProducer
{
    /// <summary>
    /// Whether the producer works from <see cref="RouteFindingRequest.Clearance"/>.
    /// Such a producer runs when a clearance check lands rather than after the
    /// debounce, inline on the UI thread when it returns a completed task.
    /// </summary>
    bool UsesClearance => false;

    /// <summary>Finds what this producer checks on the route.</summary>
    /// <param name="request">The route and data to check.</param>
    /// <param name="cancellationToken">Cancelled when a newer request supersedes this one, or Check passage turns off.</param>
    /// <returns>The findings, in any order.</returns>
    Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The clearance findings (#905) as the first producer: built from the live
/// clearance check, or from a one-off check of the route when there is none.
/// Without a draft there is no clearance check, so it finds nothing.
/// </summary>
internal sealed class ClearanceFindingProducer : IRouteFindingProducer
{
    private readonly ICrsTransformFactory _transforms;
    private readonly object _engineGate = new();
    private IReadOnlyList<LoadedDataset>? _engineSnapshot;
    private PassageCheckEngine? _engine;

    public ClearanceFindingProducer(ICrsTransformFactory? transforms = null)
    {
        _transforms = transforms ?? new ProjNetCrsTransformFactory();
    }

    public bool UsesClearance => true;

    public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasDraft)
            return Task.FromResult<IReadOnlyList<RouteFinding>>([]);
        if (request.Clearance is { } snapshot)
            return Task.FromResult(Build(snapshot, request));

        // A one-off check: sample and evaluate the route on the thread pool.
        return Task.Run(() =>
        {
            var plan = new PassagePlan(request.Legs, request.Departure);
            PassageCheckResult result;
            lock (_engineGate)
            {
                if (!ReferenceEquals(request.Datasets, _engineSnapshot) || _engine is null)
                {
                    var sources = PassageDataSources.FromDatasets(request.Datasets, _transforms);
                    _engine = new PassageCheckEngine(sources.Depth, sources.WaterLevel);
                    _engineSnapshot = request.Datasets;
                }

                var profile = _engine.SampleDepths(plan.Legs, null, cancellationToken);
                result = _engine.Evaluate(profile, plan, cancellationToken);
            }

            var oneOff = new PassageCheckSnapshot(request.RouteId, request.Legs, result, new string?[request.Legs.Count], null);
            return Build(oneOff, request);
        }, cancellationToken);
    }

    private static IReadOnlyList<RouteFinding> Build(PassageCheckSnapshot snapshot, RouteFindingRequest request) =>
        PassageFindings.Build(
            snapshot,
            RouteFindingsService.ExpiredForecasts(request.Datasets, snapshot.Legs, request.Now),
            request.DepthUnit,
            request.TimeFormat);
}
