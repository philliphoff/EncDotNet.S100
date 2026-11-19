using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.McpTools.Groups;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>SoundCharts' MCP toolsets (#1608): each tool group's toolset, the setting and the restart.</summary>
public class McpToolsetsViewerTests
{
    /// <summary>
    /// Each viewer tool group's toolset. A new group must be added here. Tools
    /// that declare another toolset themselves: get_route_weather_settings and
    /// set_route_weather_settings (display group) and get_clearance_state and
    /// set_clearance_view (route group) are in passage; the presentation group's
    /// open_dataset and close tools are in datasets.
    /// </summary>
    private static readonly Dictionary<Type, string> Pinned = new()
    {
        [typeof(DiagnosticsMcpToolGroup)] = McpToolsets.Diagnostics,
        [typeof(DisplaySettingsMcpToolGroup)] = McpToolsets.Display,
        [typeof(LegendMcpToolGroup)] = McpToolsets.Display,
        [typeof(LibraryMcpToolGroup)] = McpToolsets.Library,
        [typeof(MapViewMcpToolGroup)] = McpToolsets.Map,
        [typeof(MeasuringMcpToolGroup)] = McpToolsets.Map,
        [typeof(NavigationalWarningsMcpToolGroup)] = McpToolsets.Warnings,
        [typeof(PassageCheckMcpToolGroup)] = McpToolsets.Passage,
        [typeof(PresentationMcpToolGroup)] = McpToolsets.Presentation,
        [typeof(RouteMcpToolGroup)] = McpToolsets.Routes,
        [typeof(ShellMcpToolGroup)] = McpToolsets.Ui,
        [typeof(TestHooksMcpToolGroup)] = McpToolsets.Ui,
        [typeof(TideDisplayMcpToolGroup)] = McpToolsets.Display,
        [typeof(TidesMcpToolGroup)] = McpToolsets.Tides,
        [typeof(ViewerStateMcpToolGroup)] = McpToolsets.Map,
    };

    [Fact]
    public void Every_tool_group_declares_its_pinned_toolset()
    {
        var groups = typeof(IViewerMcpToolGroup).Assembly.GetTypes()
            .Where(t => typeof(IViewerMcpToolGroup).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
            .ToList();

        Assert.Equal(Pinned.Keys.OrderBy(t => t.Name), groups.OrderBy(t => t.Name));
        foreach (var group in groups)
        {
            // Toolset is a constant: no constructor arguments needed to read it.
            var instance = (IViewerMcpToolGroup)RuntimeHelpers.GetUninitializedObject(group);
            Assert.Equal(Pinned[group], instance.Toolset);
        }
    }

    [Fact]
    public void Resolve_uses_the_override_then_the_persisted_choice_without_unknown_names()
    {
        var settings = new ViewerSettings();
        Assert.Null(settings.ResolveMcpToolsets());

        settings.McpToolsets = ["routes", "gone", "query"];
        Assert.Equal(["query", "routes"], settings.ResolveMcpToolsets());

        settings.McpToolsetsOverride = ["tides"];
        Assert.Equal(["tides"], settings.ResolveMcpToolsets());
    }

    [Fact]
    public async Task Changing_the_toolsets_restarts_the_server_with_the_selection()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EncDotNet.S100.Viewer.Tests", $"mcp-toolsets-{Guid.NewGuid():N}");
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(dir, "settings.json"),
            McpEnabled = true,
            McpPort = 0,
            McpConfiguredFromCommandLine = true,
        };
        await using var host = TestMcpServerHost.Create(new EmptyCatalog(), settings);
        var ct = TestContext.Current.CancellationToken;

        await host.Apply(ct);
        var first = host.Server;
        Assert.Contains("list_datasets", await ToolNamesAsync(host, ct));

        await host.Apply(ct);
        Assert.Same(first, host.Server);

        settings.McpToolsets = [McpToolsets.Routes];
        await host.Apply(ct);

        Assert.NotSame(first, host.Server);
        Assert.Equal(["check_route"], await ToolNamesAsync(host, ct));
    }

    private static async Task<IReadOnlyList<string>> ToolNamesAsync(McpServerHost host, CancellationToken ct)
    {
        await using var client = await ModelContextProtocol.Client.McpClient.CreateAsync(
            new ModelContextProtocol.Client.HttpClientTransport(new ModelContextProtocol.Client.HttpClientTransportOptions
            {
                Endpoint = host.Server!.Endpoint!,
                TransportMode = ModelContextProtocol.Client.HttpTransportMode.StreamableHttp,
            }),
            cancellationToken: ct);
        var tools = await client.ListToolsAsync(cancellationToken: ct);
        return [.. tools.Select(t => t.Name)];
    }

    private sealed class EmptyCatalog : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets => [];
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }
}
