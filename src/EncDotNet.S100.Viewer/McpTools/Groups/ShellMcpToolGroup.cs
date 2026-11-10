using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The window's panels and its screenshot: <c>list_panels</c>, <c>set_panel</c> and <c>capture_app_screenshot</c>.</summary>
internal sealed class ShellMcpToolGroup : IViewerMcpToolGroup
{
    private readonly IViewerUiControllerAccessor _uiController;
    private readonly IAppScreenshotProvider _appScreenshot;
    private readonly EncDotNet.S100.Viewer.Services.DisplayCombinations.DisplayCombinationService _displayCombinations;

    public ShellMcpToolGroup(
        IViewerUiControllerAccessor uiController,
        IAppScreenshotProvider appScreenshot,
        EncDotNet.S100.Viewer.Services.DisplayCombinations.DisplayCombinationService displayCombinations)
    {
        ArgumentNullException.ThrowIfNull(uiController);
        ArgumentNullException.ThrowIfNull(appScreenshot);
        ArgumentNullException.ThrowIfNull(displayCombinations);

        _uiController = uiController;
        _appScreenshot = appScreenshot;
        _displayCombinations = displayCombinations;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        yield return ListPanelsMcpAdapter.Create(new ListPanelsTool(_uiController));
        yield return SetPanelMcpAdapter.Create(new SetPanelTool(_uiController));
        var combinations = _displayCombinations;
        yield return CaptureAppScreenshotMcpAdapter.Create(new CaptureAppScreenshotTool(
            _appScreenshot,
            _uiController,
            () => (combinations.Active?.Id, combinations.IsModified)));
    }
}
