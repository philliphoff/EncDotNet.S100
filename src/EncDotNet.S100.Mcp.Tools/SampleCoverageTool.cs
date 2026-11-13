using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// MCP wrapper over <see cref="SampleCoverageService"/> — samples a
/// gridded coverage (S-102 / S-104 / S-111) at a geographic point. The
/// reusable query logic lives in <see cref="SampleCoverageService"/>
/// (<c>EncDotNet.S100.Datasets.Pipelines.Query</c>); this class surfaces it
/// as an MCP tool and falls back to predictions where no dataset covers the
/// point and time: tide predictions for S-104, tidal current predictions for
/// S-111.
/// </summary>
public sealed class SampleCoverageTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = SampleCoverageService.Name;

    private readonly SampleCoverageService _service;
    private readonly PredictedWaterLevelSampler? _predictions;
    private readonly PredictedCurrentSampler? _currentPredictions;

    /// <summary>Creates a new <see cref="SampleCoverageTool"/> with no tide prediction fallback.</summary>
    /// <param name="catalog">The catalog of loaded datasets to sample from.</param>
    /// <param name="transforms">
    /// Factory used to reproject the WGS-84 request point into a coverage's
    /// native CRS before grid indexing — required for correct sampling of
    /// projected S-102 tiles (e.g. UTM zone 31N).
    /// </param>
    public SampleCoverageTool(IDatasetCatalog catalog, ICrsTransformFactory transforms)
        : this(catalog, transforms, null, TimeProvider.System)
    {
    }

    /// <summary>Creates a new <see cref="SampleCoverageTool"/>.</summary>
    /// <param name="catalog">The catalog of loaded datasets to sample from.</param>
    /// <param name="transforms">Reprojects the request point into a coverage's native CRS.</param>
    /// <param name="predictions">
    /// The host's route-check sources, whose
    /// <see cref="RouteCheckSourceCache.TidePredictions"/> are used for S-104
    /// (tide stations) and S-111 (current stations) where no dataset covers
    /// the point and time; <c>null</c> for none.
    /// </param>
    /// <param name="time">The clock, for a predicted sample with no time.</param>
    public SampleCoverageTool(IDatasetCatalog catalog, ICrsTransformFactory transforms, RouteCheckSourceCache? predictions, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(time);
        _service = new SampleCoverageService(catalog, transforms);
        _predictions = predictions is null ? null : new PredictedWaterLevelSampler(catalog, predictions, time);
        _currentPredictions = predictions is null ? null : new PredictedCurrentSampler(catalog, predictions, time);
    }

    /// <summary>
    /// Executes the tool: the shared service, then for S-104 a tide
    /// prediction, or for S-111 a tidal current prediction, when no dataset
    /// covers the point and time.
    /// </summary>
    public async Task<ToolResult<SampleCoverageResult>> InvokeAsync(
        SampleCoverageRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.TryGetError(out var error)
            || error is not (NoDatasetCoversPoint or OutOfBounds or NoDataAtPoint or TimeOutOfRange))
        {
            return result;
        }

        return request.Spec.Name switch
        {
            "S-104" when _predictions is not null => _predictions.Sample(request) ?? result,
            "S-111" when _currentPredictions is not null => _currentPredictions.Sample(request) ?? result,
            _ => result,
        };
    }
}
