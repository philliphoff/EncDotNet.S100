using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.Services.Tides;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The tide prediction display tools, get_tide_display and set_tide_display (#1453, design D17).</summary>
internal sealed class TideDisplayMcpToolGroup : IViewerMcpToolGroup
{
    private readonly TideStationsOverlay _overlay;
    private readonly ViewerSettingsService _settings;
    private readonly TidePredictionSourceService _predictions;
    private readonly TideStationListService _list;

    public TideDisplayMcpToolGroup(
        TideStationsOverlay overlay,
        ViewerSettingsService settings,
        TidePredictionSourceService predictions,
        TideStationListService list)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(predictions);
        ArgumentNullException.ThrowIfNull(list);
        _overlay = overlay;
        _settings = settings;
        _predictions = predictions;
        _list = list;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var tools = new TideDisplayTools(_overlay, _settings, _predictions, _list, new DispatcherRouteEditInvoker());
        yield return TideDisplayMcpAdapters.CreateGet(tools);
        yield return TideDisplayMcpAdapters.CreateSet(tools);
    }
}
