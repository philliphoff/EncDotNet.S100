using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Spec;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// MCP wrapper over <see cref="DescribeFeatureService"/> — describes a
/// single feature in a loaded dataset. The reusable query logic lives in
/// <see cref="DescribeFeatureService"/>
/// (<c>EncDotNet.S100.Datasets.Pipelines.Query</c>); this class exists only
/// to surface it as an MCP tool.
/// </summary>
public sealed class DescribeFeatureTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = DescribeFeatureService.Name;

    private readonly DescribeFeatureService _service;

    /// <summary>Creates a new <see cref="DescribeFeatureTool"/> with the default registry.</summary>
    public DescribeFeatureTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _service = new DescribeFeatureService(catalog);
    }

    /// <summary>Creates a new <see cref="DescribeFeatureTool"/> with a custom registry.</summary>
    public DescribeFeatureTool(IDatasetCatalog catalog, FeatureDescriberRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(registry);
        _service = new DescribeFeatureService(catalog, registry);
    }

    /// <summary>Executes the tool by delegating to the shared service.</summary>
    public Task<ToolResult<DescribeFeatureResult>> InvokeAsync(
        DescribeFeatureRequest request,
        CancellationToken cancellationToken = default) =>
        _service.InvokeAsync(request, cancellationToken);
}
