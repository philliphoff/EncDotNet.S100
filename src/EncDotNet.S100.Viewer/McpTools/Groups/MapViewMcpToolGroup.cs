using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary><c>set_viewport</c> and <c>pick_features</c>: moving the map and picking features on it.</summary>
internal sealed class MapViewMcpToolGroup : IViewerMcpToolGroup
{
    private readonly ICapabilityAccessor<IMapViewportController> _viewport;
    private readonly ICapabilityAccessor<IMapCoordinateConverter> _coordinates;
    private readonly IDatasetCatalog _catalog;
    private readonly IMapDrawState _mapDrawState;
    private readonly IGeographicPickPresenter _pickPresenter;
    private readonly EncDotNet.S100.Viewer.Diagnostics.ViewSettleMonitor _settle;

    public MapViewMcpToolGroup(
        ICapabilityAccessor<IMapViewportController> viewport,
        ICapabilityAccessor<IMapCoordinateConverter> coordinates,
        IDatasetCatalog catalog,
        IMapDrawState mapDrawState,
        IGeographicPickPresenter pickPresenter,
        EncDotNet.S100.Viewer.Diagnostics.ViewSettleMonitor settle)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(mapDrawState);
        ArgumentNullException.ThrowIfNull(pickPresenter);
        ArgumentNullException.ThrowIfNull(settle);

        _viewport = viewport;
        _coordinates = coordinates;
        _catalog = catalog;
        _mapDrawState = mapDrawState;
        _pickPresenter = pickPresenter;
        _settle = settle;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        // set_viewport marks the settle trigger, so await_settled measures from it.
        yield return SetViewportMcpAdapter.Create(new SetViewportTool(_viewport, _settle.MarkTrigger));
        yield return PickFeaturesMcpAdapter.Create(
            new PickFeaturesTool(
                _coordinates,
                new EncDotNet.S100.Mcp.Tools.IdentifyFeaturesTool(_catalog, _mapDrawState),
                _pickPresenter));
    }
}
