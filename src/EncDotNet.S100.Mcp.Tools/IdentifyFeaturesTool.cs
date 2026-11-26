using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// MCP wrapper over <see cref="IdentifyFeaturesService"/> — the ECDIS
/// cursor-pick that identifies vector features at a geographic point,
/// ranked by drawing priority, then most-specific first. The reusable query logic lives in
/// <see cref="IdentifyFeaturesService"/>
/// (<c>EncDotNet.S100.Datasets.Pipelines.Query</c>); this class exists only
/// to surface it as an MCP tool.
/// </summary>
public sealed class IdentifyFeaturesTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = IdentifyFeaturesService.Name;

    private readonly IdentifyFeaturesService _service;

    /// <summary>
    /// Creates a new <see cref="IdentifyFeaturesTool"/> for a host with no
    /// map: a request with no time identifies every drawable feature.
    /// </summary>
    public IdentifyFeaturesTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _service = new IdentifyFeaturesService(catalog);
    }

    /// <summary>
    /// Creates a new <see cref="IdentifyFeaturesTool"/> for a host whose map
    /// draws as <paramref name="drawState"/> says: a request with no time
    /// identifies what the map draws.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="drawState">What the host's map draws.</param>
    public IdentifyFeaturesTool(IDatasetCatalog catalog, IMapDrawState drawState)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(drawState);
        _service = new IdentifyFeaturesService(catalog, drawState);
    }

    /// <summary>
    /// Creates the tool for a host whose maps draw as <paramref name="drawStates"/>
    /// says; a request may name a map when it's an
    /// <see cref="EncDotNet.S100.Hosting.IMapCapabilityAccessor{TCapability}"/>.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="drawStates">What the host's maps draw.</param>
    public IdentifyFeaturesTool(IDatasetCatalog catalog, EncDotNet.S100.Hosting.ICapabilityAccessor<IMapDrawState> drawStates)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(drawStates);
        _service = new IdentifyFeaturesService(catalog, drawStates);
    }

    /// <summary>Executes the tool by delegating to the shared service.</summary>
    public Task<ToolResult<IdentifyFeaturesResult>> InvokeAsync(
        IdentifyFeaturesRequest request,
        CancellationToken cancellationToken = default) =>
        _service.InvokeAsync(request, cancellationToken);
}
