using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The Navigational warnings panel's tools (#932).</summary>
internal sealed class NavigationalWarningsMcpToolGroup : IViewerMcpToolGroup
{
    private readonly EncDotNet.S100.Viewer.ViewModels.NavigationalWarningsViewModel _panel;
    private readonly NavigationalWarningsService _service;

    public NavigationalWarningsMcpToolGroup(
        EncDotNet.S100.Viewer.ViewModels.NavigationalWarningsViewModel panel,
        NavigationalWarningsService service)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(service);

        _panel = panel;
        _service = service;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var invoker = new DispatcherRouteEditInvoker();
        yield return NavigationalWarningsMcpAdapters.Create(new ListNavigationalWarningsTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new SelectNavigationalWarningTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new RefreshNavigationalWarningsTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new MarkNavigationalWarningsNewTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new GetWarningCardsTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new FollowWarningLinkTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new PreviewWarningCardTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new SetNavigationalWarningDisplayTool(_panel, _service, invoker));
        yield return NavigationalWarningsMcpAdapters.Create(new SetNavigationalWarningClassificationTool(_panel, _service, invoker));
    }
}
