using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The tools for scripted testing, offered only when the viewer was started
/// with <c>--mcp-test-hooks</c>: <c>set_test_clock</c> and the <c>ui_*</c> tools,
/// which can click anything in the UI.</summary>
internal sealed class TestHooksMcpToolGroup : IViewerMcpToolGroup
{
    private readonly ViewerSettings _settings;
    private readonly AdjustableTimeProvider? _testClock;
    private readonly IViewerUiAutomation? _uiAutomation;

    public TestHooksMcpToolGroup(
        ViewerSettings settings,
        AdjustableTimeProvider? testClock,
        IViewerUiAutomation? uiAutomation)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _testClock = testClock;
        _uiAutomation = uiAutomation;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        if (!_settings.McpTestHooks)
            yield break;

        // set_test_clock needs the app's clock to be adjustable, which it is
        // only when started with --mcp-test-hooks.
        if (_testClock is not null)
            yield return ViewerStateMcpAdapters.Create(new SetTestClockTool(_testClock));

        if (_uiAutomation is not null)
        {
            yield return UiAutomationMcpAdapters.Create(new UiTreeTool(_uiAutomation));
            foreach (var action in Enum.GetValues<UiAction>())
                yield return UiAutomationMcpAdapters.Create(new UiActionTool(_uiAutomation, action));
        }
    }
}
