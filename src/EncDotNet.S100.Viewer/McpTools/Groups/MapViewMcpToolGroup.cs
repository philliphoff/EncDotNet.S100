using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary><c>set_viewport</c> and <c>pick_features</c>: moving the map and picking features on it;
/// and the split-map tools (<c>list_maps</c>, <c>set_active_map</c>, <c>set_split_map</c>,
/// <c>set_map_link</c>, <c>set_map_name</c>; D28, #1735).</summary>
internal sealed class MapViewMcpToolGroup : IViewerMcpToolGroup
{
    private readonly ICapabilityAccessor<IMapViewportController> _viewport;
    private readonly ICapabilityAccessor<IMapCoordinateConverter> _coordinates;
    private readonly IDatasetCatalog _catalog;
    private readonly ICapabilityAccessor<IMapDrawState> _mapDrawState;
    private readonly IGeographicPickPresenter _pickPresenter;
    private readonly EncDotNet.S100.Viewer.Diagnostics.ViewSettleMonitor _settle;
    private readonly ICapabilityAccessor<MapSplitController> _split;

    public MapViewMcpToolGroup(
        ICapabilityAccessor<IMapViewportController> viewport,
        ICapabilityAccessor<IMapCoordinateConverter> coordinates,
        IDatasetCatalog catalog,
        ICapabilityAccessor<IMapDrawState> mapDrawState,
        IGeographicPickPresenter pickPresenter,
        EncDotNet.S100.Viewer.Diagnostics.ViewSettleMonitor settle,
        ICapabilityAccessor<MapSplitController> split)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(mapDrawState);
        ArgumentNullException.ThrowIfNull(pickPresenter);
        ArgumentNullException.ThrowIfNull(settle);
        ArgumentNullException.ThrowIfNull(split);

        _viewport = viewport;
        _coordinates = coordinates;
        _catalog = catalog;
        _mapDrawState = mapDrawState;
        _pickPresenter = pickPresenter;
        _settle = settle;
        _split = split;
    }

    /// <inheritdoc />
    public string Toolset => McpToolsets.Map;

    /// <inheritdoc />
    public IEnumerable<McpServerTool> CreateTools() => McpToolsets.AssignDefault(Toolset, CreateGroupTools());

    private IEnumerable<McpServerTool> CreateGroupTools()
    {
        // set_viewport marks the settle trigger, so await_settled measures from it.
        yield return SetViewportMcpAdapter.Create(new SetViewportTool(_viewport, _settle.MarkTrigger));
        yield return PickFeaturesMcpAdapter.Create(
            new PickFeaturesTool(
                _coordinates,
                new EncDotNet.S100.Mcp.Tools.IdentifyFeaturesTool(_catalog, _mapDrawState),
                _pickPresenter,
                _split));
        foreach (var tool in SplitMapMcpAdapters.Create(new SplitMapTools(_split)))
            yield return tool;
    }
}
