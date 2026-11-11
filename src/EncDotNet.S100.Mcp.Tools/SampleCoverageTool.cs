using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// MCP wrapper over <see cref="SampleCoverageService"/> — samples a
/// gridded coverage (S-102 / S-104 / S-111) at a geographic point. The
/// reusable query logic lives in <see cref="SampleCoverageService"/>
/// (<c>EncDotNet.S100.Datasets.Pipelines.Query</c>); this class exists only
/// to surface it as an MCP tool.
/// </summary>
public sealed class SampleCoverageTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = SampleCoverageService.Name;

    private readonly SampleCoverageService _service;

    /// <summary>Creates a new <see cref="SampleCoverageTool"/>.</summary>
    /// <param name="catalog">The catalog of loaded datasets to sample from.</param>
    /// <param name="transforms">
    /// Factory used to reproject the WGS-84 request point into a coverage's
    /// native CRS before grid indexing — required for correct sampling of
    /// projected S-102 tiles (e.g. UTM zone 31N).
    /// </param>
    public SampleCoverageTool(IDatasetCatalog catalog, ICrsTransformFactory transforms)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(transforms);
        _service = new SampleCoverageService(catalog, transforms);
    }

    /// <summary>Executes the tool by delegating to the shared service.</summary>
    public Task<ToolResult<SampleCoverageResult>> InvokeAsync(
        SampleCoverageRequest request,
        CancellationToken cancellationToken = default) =>
        _service.InvokeAsync(request, cancellationToken);
}
