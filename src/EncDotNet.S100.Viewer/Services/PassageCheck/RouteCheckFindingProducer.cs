using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The route checks against the chart (#954, #955): land, drying ground,
/// chart coverage, charted dangers and the safety contour, from
/// <see cref="RouteCheckEngine"/> over the loaded S-101 cells and S-102
/// surfaces. Runs with or without a draft.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="RouteCheckSources"/> come from a
/// <see cref="RouteCheckSourceCache"/> it shares with the area producer.
/// </para>
/// <para>
/// The depth checks use the mariner's safety contour and safety depth
/// (<see cref="RouteCheckSettings"/>) and, on each leg with a draft, that
/// leg's draft, squat and required UKC. The findings service re-runs
/// producers when the mariner's settings change.
/// </para>
/// </remarks>
internal sealed class RouteCheckFindingProducer : IRouteFindingProducer
{
    private readonly IMarinerSettingsProvider? _mariner;
    private readonly RouteCheckSourceCache _sources;
    private readonly ChartCheckOptions? _options;

    /// <summary>Creates the producer.</summary>
    /// <param name="mariner">The mariner's settings for the depth checks, or <c>null</c> to skip them.</param>
    /// <param name="sources">The sources shared with the area check (#956), or <c>null</c> for its own over ProjNet.</param>
    /// <param name="options">Engine tuning, or <c>null</c> for the defaults with the viewer's wording.</param>
    public RouteCheckFindingProducer(IMarinerSettingsProvider? mariner = null, RouteCheckSourceCache? sources = null, ChartCheckOptions? options = null)
    {
        _mariner = mariner;
        _sources = sources ?? new RouteCheckSourceCache(new ProjNetCrsTransformFactory());
        _options = options;
    }

    public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = _options ?? ChartCheckOptions.Default with { Formatter = new ViewerFindingFormatter(request) };
        var settings = Settings(request);
        return Task.Run<IReadOnlyList<RouteFinding>>(
            () =>
            {
                var sources = _sources.Get(request.Datasets);
                return new RouteCheckEngine(sources.Charts!, options, sources.Depth).Check(request.Plan, settings, cancellationToken).Findings;
            },
            cancellationToken);
    }

    /// <summary>
    /// The check settings for a request: the mariner's safety contour and
    /// depth, or none (no depth checks) without mariner settings. Each leg's
    /// dangers are judged against that leg's own keel.
    /// </summary>
    internal RouteCheckSettings Settings(RouteFindingRequest request) =>
        _mariner?.Current is { } mariner
            ? new RouteCheckSettings
            {
                At = request.Settings.At,
                CorridorOverride = request.Settings.CorridorOverride,
                SafetyContour = mariner.SafetyContour,
                SafetyDepth = mariner.SafetyDepth,
            }
            : request.Settings;
}
