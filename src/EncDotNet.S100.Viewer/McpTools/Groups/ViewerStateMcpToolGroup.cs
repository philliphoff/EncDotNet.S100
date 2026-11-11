using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The viewer's own state: the Timeline (<c>get_timeline_state</c>,
/// <c>set_view_time</c>, <c>step_time</c>, <c>set_timeline_view</c>), dataset
/// state and selection, notifications, and own ship.</summary>
internal sealed class ViewerStateMcpToolGroup : IViewerMcpToolGroup
{
    private readonly IViewerTimelineController _timeline;
    private readonly IViewerDatasetStateController _datasetState;
    private readonly IViewerNotificationController _notifications;
    private readonly EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm _ownShipHelm;

    public ViewerStateMcpToolGroup(
        IViewerTimelineController timeline,
        IViewerDatasetStateController datasetState,
        IViewerNotificationController notifications,
        EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip.IOwnShipHelm ownShipHelm)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(datasetState);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(ownShipHelm);

        _timeline = timeline;
        _datasetState = datasetState;
        _notifications = notifications;
        _ownShipHelm = ownShipHelm;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        yield return ViewerStateMcpAdapters.Create(new GetTimelineStateTool(_timeline));
        yield return ViewerStateMcpAdapters.Create(new SetViewTimeTool(_timeline));
        yield return ViewerStateMcpAdapters.Create(new StepTimeTool(_timeline));
        yield return ViewerStateMcpAdapters.Create(new SetTimelineViewTool(_timeline));
        yield return ViewerStateMcpAdapters.Create(new SetDatasetStateTool(_datasetState));
        yield return ViewerStateMcpAdapters.Create(new SelectDatasetTool(_datasetState));
        yield return ViewerStateMcpAdapters.Create(new ListNotificationsTool(_notifications));
        yield return ViewerStateMcpAdapters.Create(new DismissNotificationTool(_notifications));
        yield return SetOwnShipMcpAdapter.Create(new SetOwnShipTool(_ownShipHelm));
    }
}
