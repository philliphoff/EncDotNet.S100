using System.Globalization;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// The route checks against the chart (#954, #955): land, drying ground,
/// chart coverage, charted dangers and the safety contour, from
/// <see cref="RouteCheckEngine"/> over the loaded S-101 cells and S-102
/// surfaces. Runs with or without a draft.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="RouteCheckDataSources"/> come from a
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
    private readonly RouteCheckOptions? _options;

    /// <summary>Creates the producer.</summary>
    /// <param name="mariner">The mariner's settings for the depth checks, or <c>null</c> to skip them.</param>
    /// <param name="sources">The sources shared with the area check (#956), or <c>null</c> for its own over ProjNet.</param>
    /// <param name="options">Engine tuning, or <c>null</c> for the defaults with the viewer's wording.</param>
    public RouteCheckFindingProducer(IMarinerSettingsProvider? mariner = null, RouteCheckSourceCache? sources = null, RouteCheckOptions? options = null)
    {
        _mariner = mariner;
        _sources = sources ?? new RouteCheckSourceCache();
        _options = options;
    }

    public Task<IReadOnlyList<RouteFinding>> ProduceAsync(RouteFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = _options ?? RouteCheckOptions.Default with { Text = Text() };
        var settings = Settings(request);
        return Task.Run<IReadOnlyList<RouteFinding>>(
            () => _sources.Get(request.Datasets).CreateEngine(options).Check(request.Plan, settings, cancellationToken).Findings,
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

    /// <summary>The engine's wording, from the viewer's resources.</summary>
    internal static RouteCheckText Text() => new()
    {
        CrossesLand = Strings.RouteCheck_CrossesLand,
        LandInCorridor = Strings.RouteCheck_LandInCorridor,
        CrossesDrying = Strings.RouteCheck_CrossesDrying,
        DryingInCorridor = Strings.RouteCheck_DryingInCorridor,
        OutsideChartCoverage = Strings.RouteCheck_OutsideChartCoverage,
        DangerInCorridorFormat = Strings.RouteCheck_DangerInCorridorFormat,
        DangerNearRouteFormat = Strings.RouteCheck_DangerNearRouteFormat,
        Wreck = Strings.RouteCheck_Wreck,
        Obstruction = Strings.RouteCheck_Obstruction,
        Rock = Strings.RouteCheck_Rock,
        CrossesSafetyContour = Strings.RouteCheck_CrossesSafetyContour,
        SafetyContourInCorridor = Strings.RouteCheck_SafetyContourInCorridor,
        SoundingsBelowSafetyDepth = Strings.RouteCheck_SoundingsBelowSafetyDepth,
        CrossesS102Shoal = Strings.RouteCheck_CrossesS102Shoal,
        S102ShoalInCorridor = Strings.RouteCheck_S102ShoalInCorridor,
        ToPortFormat = Strings.RouteCheck_ToPortFormat,
        ToStarboardFormat = Strings.RouteCheck_ToStarboardFormat,
        OnTrack = Strings.RouteCheck_OnTrack,
        DepthFormat = Strings.RouteCheck_DepthFormat,
        LeastDepthFormat = Strings.RouteCheck_LeastDepthFormat,
        SoundingCountFormat = Strings.RouteCheck_SoundingCountFormat,
        S102CellWithUncertaintyFormat = Strings.RouteCheck_S102CellWithUncertaintyFormat,
        S102CellFormat = Strings.RouteCheck_S102CellFormat,
        DepthUnknown = Strings.RouteCheck_DepthUnknown,
        Awash = Strings.RouteCheck_Awash,
        CoversAndUncovers = Strings.RouteCheck_CoversAndUncovers,
        AboveWater = Strings.RouteCheck_AboveWater,
        BelowSafetyDepthFormat = Strings.RouteCheck_BelowSafetyDepthFormat,
        BelowKeelFormat = Strings.RouteCheck_BelowKeelFormat,
        BelowSafetyContourFormat = Strings.RouteCheck_BelowSafetyContourFormat,
        LegFormat = Strings.Check_LegFormat,
        LengthFormat = Strings.RouteCheck_LengthFormat,
        Separator = Strings.RouteCheck_Separator,
        Culture = CultureInfo.CurrentCulture,
    };
}

/// <summary>
/// The <see cref="RouteCheckDataSources"/> for the loaded data, kept per
/// dataset snapshot and shared by the chart and area producers, so the
/// cells' indexes are built once (on the first check after the data
/// changes) and a waypoint drag only re-sweeps the route.
/// </summary>
internal sealed class RouteCheckSourceCache
{
    private readonly ICrsTransformFactory _transforms;
    private readonly object _gate = new();
    private IReadOnlyList<LoadedDataset>? _snapshot;
    private RouteCheckDataSources? _sources;

    /// <param name="transforms">CRS transforms for projected S-102 grids, or <c>null</c> for ProjNet.</param>
    public RouteCheckSourceCache(ICrsTransformFactory? transforms = null)
    {
        _transforms = transforms ?? new ProjNetCrsTransformFactory();
    }

    /// <summary>The sources for a dataset snapshot, built on the first call after it changes.</summary>
    public RouteCheckDataSources Get(IReadOnlyList<LoadedDataset> datasets)
    {
        lock (_gate)
        {
            if (_sources is null || !ReferenceEquals(datasets, _snapshot))
            {
                _sources = RouteCheckDataSources.FromDatasets(datasets, _transforms);
                _snapshot = datasets;
            }

            return _sources;
        }
    }
}
