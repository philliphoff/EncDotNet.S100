using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// MCP wrapper over <see cref="IdentifyFeaturesService"/> — the ECDIS
/// cursor-pick that identifies vector features at a geographic point,
/// ranked most-specific first. The reusable query logic lives in
/// <see cref="IdentifyFeaturesService"/>
/// (<c>EncDotNet.S100.Datasets.Pipelines.Query</c>); this class exists only
/// to surface it as an MCP tool.
/// </summary>
public sealed class IdentifyFeaturesTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = IdentifyFeaturesService.Name;

    private readonly IdentifyFeaturesService _service;

    /// <summary>Creates a new <see cref="IdentifyFeaturesTool"/>.</summary>
    public IdentifyFeaturesTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _service = new IdentifyFeaturesService(catalog);
    }

    /// <summary>Executes the tool by delegating to the shared service.</summary>
    public Task<ToolResult<IdentifyFeaturesResult>> InvokeAsync(
        IdentifyFeaturesRequest request,
        CancellationToken cancellationToken = default) =>
        _service.InvokeAsync(request, cancellationToken);
}
