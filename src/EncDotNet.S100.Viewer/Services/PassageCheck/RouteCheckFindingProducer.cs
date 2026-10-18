using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The route checks against the chart (#954): land, drying ground and chart
/// coverage, from <see cref="RouteCheckEngine"/> over the loaded S-101 cells.
/// Runs with or without a draft.
/// </summary>
/// <remarks>
/// The engine and its <see cref="RouteCheckDataSources"/> are kept per
/// dataset snapshot, so the cells' indexes are built once (on the first
/// check after the data changes) and a waypoint drag only re-sweeps the
/// route.
/// </remarks>
internal sealed class RouteCheckFindingProducer : IRouteFindingProducer
{
    private readonly RouteCheckOptions? _options;
    private readonly object _gate = new();
    private IReadOnlyList<LoadedDataset>? _snapshot;
    private RouteCheckEngine? _engine;

    /// <summary>Creates the producer.</summary>
    /// <param name="options">Engine tuning, or <c>null</c> for the defaults with the viewer's wording.</param>
    public RouteCheckFindingProducer(RouteCheckOptions? options = null)
    {
        _options = options;
    }

    public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run<IReadOnlyList<RouteFinding>>(() =>
        {
            var legs = Legs(request);
            return Engine(request.Datasets).Check(legs, cancellationToken).Findings;
        }, cancellationToken);
    }

    /// <summary>The route's legs with their cross-track limits.</summary>
    internal static IReadOnlyList<RouteCheckLeg> Legs(RouteFindingRequest request)
    {
        var legs = new RouteCheckLeg[request.Legs.Count];
        for (var i = 0; i < legs.Length; i++)
        {
            var leg = request.Legs[i];
            var (port, starboard) = i < request.CrossTrack.Count ? request.CrossTrack[i] : (null, null);
            legs[i] = new RouteCheckLeg(leg.Points, port, starboard, leg.Geometry);
        }

        return legs;
    }

    /// <summary>The engine's wording, from the viewer's resources.</summary>
    internal static RouteCheckText Text() => new()
    {
        CrossesLand = Strings.RouteCheck_CrossesLand,
        LandInCorridor = Strings.RouteCheck_LandInCorridor,
        CrossesDrying = Strings.RouteCheck_CrossesDrying,
        DryingInCorridor = Strings.RouteCheck_DryingInCorridor,
        OutsideChartCoverage = Strings.RouteCheck_OutsideChartCoverage,
        LegFormat = Strings.Check_LegFormat,
        LengthFormat = Strings.RouteCheck_LengthFormat,
        Culture = CultureInfo.CurrentCulture,
    };

    private RouteCheckEngine Engine(IReadOnlyList<LoadedDataset> datasets)
    {
        lock (_gate)
        {
            if (_engine is null || !ReferenceEquals(datasets, _snapshot))
            {
                var options = _options ?? RouteCheckOptions.Default with { Text = Text() };
                _engine = new RouteCheckEngine(RouteCheckDataSources.FromDatasets(datasets).Charts, options);
                _snapshot = datasets;
            }

            return _engine;
        }
    }
}
