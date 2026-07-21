using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

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
    public SampleCoverageTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _service = new SampleCoverageService(catalog);
    }

    /// <summary>Executes the tool by delegating to the shared service.</summary>
    public Task<ToolResult<SampleCoverageResult>> InvokeAsync(
        SampleCoverageRequest request,
        CancellationToken cancellationToken = default) =>
        _service.InvokeAsync(request, cancellationToken);
}
