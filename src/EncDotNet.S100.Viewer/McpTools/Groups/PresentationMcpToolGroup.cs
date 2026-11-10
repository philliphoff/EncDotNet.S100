using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.McpCapabilities;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The shared, renderer-neutral mutating tools the viewer backs: presentation
/// (<c>set_palette</c>, <c>set_display_category</c>, <c>set_display_mode</c>), time
/// (<c>set_time_step</c>), <c>render_to_image</c>, and the dataset catalog
/// (<c>open_dataset</c>, <c>close_dataset</c>, <c>close_all_datasets</c>), bound
/// to the viewer's services through capability adapters.</summary>
internal sealed class PresentationMcpToolGroup : IViewerMcpToolGroup
{
    private readonly IRenderStateControllerAccessor _renderState;
    private readonly MapPresentationStateProjection _presentationProjection;
    private readonly GlobalTimeService _globalTime;
    private readonly ICapabilityAccessor<IImageRenderer> _imageRenderer;
    private readonly IDatasetLoadGateway _loadGateway;
    private readonly IDatasetCatalog _catalog;

    public PresentationMcpToolGroup(
        IRenderStateControllerAccessor renderState,
        MapPresentationStateProjection presentationProjection,
        GlobalTimeService globalTime,
        ICapabilityAccessor<IImageRenderer> imageRenderer,
        IDatasetLoadGateway loadGateway,
        IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(renderState);
        ArgumentNullException.ThrowIfNull(presentationProjection);
        ArgumentNullException.ThrowIfNull(globalTime);
        ArgumentNullException.ThrowIfNull(imageRenderer);
        ArgumentNullException.ThrowIfNull(loadGateway);
        ArgumentNullException.ThrowIfNull(catalog);

        _renderState = renderState;
        _presentationProjection = presentationProjection;
        _globalTime = globalTime;
        _imageRenderer = imageRenderer;
        _loadGateway = loadGateway;
        _catalog = catalog;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var presentation = new DelegatingCapabilityAccessor<IPresentationController>(() =>
            _renderState.Current is { } controller
                ? new ViewerPresentationController(controller, _presentationProjection.CreateSnapshot)
                : null);
        var time = new StaticCapabilityAccessor<ITimeController>(
            new ViewerTimeController(_globalTime, UiThread.InvokeAsync));

        // open_dataset / close_dataset / close_all_datasets: the viewer's
        // read-only catalog plus its UI-thread load gateway, presented as the
        // shared mutable-catalog seam. Readiness of the load path is handled
        // inside the adapter.
        var catalog = new ViewerMutableDatasetCatalog(_catalog, _loadGateway);

        return EncDotNet.S100.Mcp.MutableTools.S100MutableTools.Create(
            presentation: presentation, time: time, renderer: _imageRenderer, catalog: catalog);
    }
}
