using System.Diagnostics;
using System.Text.Json.Nodes;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.McpTools.Groups;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Fakes for the split-map MCP tests (#1735): maps that record what the map
/// tools do to them, a split over them, and an MCP server to call.
/// </summary>
internal static class SplitMapMcpTestKit
{
    /// <summary>An empty registry of maps.</summary>
    public static MapViews NewViews() => new(
        new MapCapabilityAccessor<IMapViewportController>(),
        new MapCapabilityAccessor<IMapCoordinateConverter>(),
        new MapCapabilityAccessor<IImageRenderer>(),
        new DemandPlannerSet());

    /// <summary>A viewport snapshot around a centre.</summary>
    public static MapViewportSnapshot Viewport(double latitude, double longitude, double halfDegrees = 0.5) => new()
    {
        MinLatitude = latitude - halfDegrees,
        MaxLatitude = latitude + halfDegrees,
        MinLongitude = longitude - halfDegrees,
        MaxLongitude = longitude + halfDegrees,
        MercatorResolution = 10,
    };

    /// <summary>The JSON a successful tool call returned.</summary>
    public static JsonNode Json(CallToolResult result)
    {
        var text = Assert.IsType<TextContentBlock>(result.Content.First(c => c is TextContentBlock)).Text;
        Assert.False(result.IsError == true, text);
        return JsonNode.Parse(text)!;
    }

    /// <summary>The error code a failed tool call returned.</summary>
    public static string ErrorCode(CallToolResult result)
    {
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content.First(c => c is TextContentBlock)).Text;
        return JsonNode.Parse(text)!["code"]!.GetValue<string>();
    }
}

/// <summary>A map that records what the map tools do to it.</summary>
internal sealed class TestMap : IMapViewCapabilities, IMapViewDiagnostics
{
    public TestMap(string id)
    {
        Id = new MapViewId(id);
    }

    public MapViewId Id { get; }

    public TestMapHost Host { get; } = new();

    public MapViewsTests.FakeNotifier Notifier { get; } = new();

    public FakeMapDatasetSession Session { get; } = new();

    public FakeRenderActivity Activity { get; } = new();

    /// <summary>What <see cref="EvaluateSettle"/> returns; unpainted by default.</summary>
    public Func<TileSettleState> Settle { get; set; } = () => new TileSettleState(1, 1, 0, 0, 0, 0);

    public int Redraws { get; private set; }

    public IMapViewportController ViewportController => Host;

    public IMapCoordinateConverter CoordinateConverter => Host;

    public IImageRenderer ImageRenderer => Host;

    public IMapViewportNotifier Viewport => Notifier;

    public DemandPlanner Planner { get; } = new();

    public IMapDatasetSession DatasetSession => Session;

    public IRenderActivityMonitor? RenderActivity => Activity;

    public TileSettleState EvaluateSettle() => Settle();

    public void RefreshGraphics() => Redraws++;

    /// <summary>A settled state painted now.</summary>
    public static TileSettleState SettledNow() => new(1, 0, 0, 0, 0, Stopwatch.GetTimestamp());
}

/// <summary>A map host that records the viewport calls and renders a stub image.</summary>
internal sealed class TestMapHost : IMapViewportController, IMapCoordinateConverter, IImageRenderer
{
    public List<string> Calls { get; } = [];

    public GeoPosition ScreenPoint { get; set; } = new(0, 0);

    public void ZoomToExtent(MRect extent) => Calls.Add("ZoomToExtent");

    public void SetViewportToExtent(MRect mercatorExtent) => Calls.Add("SetViewportToExtent");

    public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) =>
        Calls.Add("SetViewportToCenterAndResolution");

    public void SetRotation(double degrees) => Calls.Add($"SetRotation({degrees})");

    public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300) => Calls.Add("CenterOn");

    public GeoPosition? TryGetViewportCenterWgs84() => null;

    public (double CenterX, double CenterY, double Resolution)? Fling(double velocityX, double velocityY)
    {
        Calls.Add($"Fling({velocityX},{velocityY})");
        return (0, 0, 10);
    }

    public (double Width, double Height)? TryGetViewportSizePx() => (800, 600);

    public GeoPosition? TryScreenToWgs84(double xPx, double yPx) => ScreenPoint;

    public GeoPosition? TryImagePixelToWgs84(double xPx, double yPx, int imageWidthPx, int imageHeightPx) => ScreenPoint;

    public Task<byte[]?> RenderToPngAsync(int widthPx, int heightPx, double pixelDensity, CancellationToken cancellationToken = default)
    {
        Calls.Add($"Render({widthPx}x{heightPx})");
        return Task.FromResult<byte[]?>([0x89, 0x50, 0x4E, 0x47]);
    }
}

/// <summary>A render-activity monitor that records the waits and reports set stats.</summary>
internal sealed class FakeRenderActivity : IRenderActivityMonitor
{
    public int Waits { get; private set; }

    public long PaintCount { get; set; }

    public RenderStatsSnapshot? LatestStats { get; set; }

    public Func<bool>? BusyProbe { get; set; }

    public Task<RenderIdleResult> WaitForIdleAsync(TimeSpan quietPeriod, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Waits++;
        return Task.FromResult(new RenderIdleResult(true, false, 1, PaintCount, quietPeriod.TotalMilliseconds));
    }

    public RenderWindowStats GetWindowStats() => RenderWindowStats.Empty;

    public void ResetWindow()
    {
    }
}

/// <summary>Lays panes out over <see cref="TestMap"/>s.</summary>
internal sealed class TestPaneHost(MapViews views) : IMapPaneHost
{
    public Dictionary<string, TestMap> Maps { get; } = [];

    public IMapViewCapabilities OpenPane(MapViewId id, MapPaneViewport? viewport)
    {
        var map = new TestMap(id.Value);
        Maps[id.Value] = map;
        views.Add(map);
        return map;
    }

    public void ClosePane(IMapViewCapabilities view)
    {
        Maps.Remove(view.Id.Value);
        views.Remove(view);
    }

    public MapPaneViewport? GetViewport(IMapViewCapabilities view) => null;
}

/// <summary>A tool group serving the tools it's given.</summary>
internal sealed class TestToolGroup(params McpServerTool[] tools) : IViewerMcpToolGroup
{
    public string Toolset => EncDotNet.S100.Mcp.McpToolsets.Map;

    public IEnumerable<McpServerTool> CreateTools() => EncDotNet.S100.Mcp.McpToolsets.AssignDefault(Toolset, tools);
}

/// <summary>
/// A window's split over <see cref="TestMap"/>s with its MCP tools served by
/// a real <see cref="McpServerHost"/>, and a client connected to it.
/// </summary>
internal sealed class SplitMapMcpFixture : IAsyncDisposable
{
    private readonly McpServerHost _host;
    private readonly string _dir;
    private McpClient? _client;

    public SplitMapMcpFixture(EncDotNet.S100.Datasets.Pipelines.Catalog.IDatasetCatalog? catalog = null)
    {
        ViewportAccessor = new MapCapabilityAccessor<IMapViewportController>();
        ConverterAccessor = new MapCapabilityAccessor<IMapCoordinateConverter>();
        RendererAccessor = new MapCapabilityAccessor<IImageRenderer>();
        Views = new MapViews(ViewportAccessor, ConverterAccessor, RendererAccessor, new DemandPlannerSet());
        Panes = new TestPaneHost(Views);
        Split = new MapSplitViewModel(Views, Panes, () => { });
        Split.Restore(saved: null, restoreViews: true);
        ControllerAccessor = new MapSplitControllerAccessor
        {
            Current = new MapSplitController(Split, Views, action =>
            {
                action();
                return Task.CompletedTask;
            }),
        };
        WindowActivity = new FakeRenderActivity();
        Settle = new EncDotNet.S100.Viewer.Diagnostics.ViewSettleMonitor(_ => { });
        var drawStates = new SharedMapCapabilityAccessor<IMapDrawState>(Views, new TestMcpServerHost.NothingHidden());
        Catalog = catalog ?? new EmptyCatalog();

        var mapGroup = new MapViewMcpToolGroup(
            ViewportAccessor,
            ConverterAccessor,
            Catalog,
            drawStates,
            Presenter,
            Settle,
            ControllerAccessor);
        var diagnostics = new TestToolGroup(
            McpTools.AwaitRenderIdleMcpAdapter.Create(new McpTools.AwaitRenderIdleTool(WindowActivity, Views)),
            McpTools.GetRenderStatsMcpAdapter.Create(new McpTools.GetRenderStatsTool(WindowActivity, Views)),
            McpTools.PerfHarnessMcpAdapters.Create(new McpTools.AwaitSettledTool(Settle, Views)),
            McpTools.PerfHarnessMcpAdapters.Create(new McpTools.FlingTool(ViewportAccessor, Settle.MarkTrigger)));
        var presentation = new TestToolGroup(
            [.. EncDotNet.S100.Mcp.MutableTools.S100MutableTools.Create(renderer: RendererAccessor)]);

        _dir = Path.Combine(Path.GetTempPath(), "EncDotNet.S100.Viewer.Tests", $"split-mcp-{Guid.NewGuid():N}");
        var settings = new ViewerSettings
        {
            SettingsFilePath = Path.Combine(_dir, "settings.json"),
            McpEnabled = true,
            McpPort = 0,
            McpConfiguredFromCommandLine = true,
        };
        _host = TestMcpServerHost.Create(Catalog, settings, drawStates, [mapGroup, diagnostics, presentation]);
    }

    public MapCapabilityAccessor<IMapViewportController> ViewportAccessor { get; }

    /// <summary>What pick_features with select asked the viewer to show.</summary>
    public RecordingPresenter Presenter { get; } = new();

    public MapCapabilityAccessor<IMapCoordinateConverter> ConverterAccessor { get; }

    public MapCapabilityAccessor<IImageRenderer> RendererAccessor { get; }

    public MapViews Views { get; }

    public TestPaneHost Panes { get; }

    public MapSplitViewModel Split { get; }

    public MapSplitControllerAccessor ControllerAccessor { get; }

    public FakeRenderActivity WindowActivity { get; }

    public EncDotNet.S100.Viewer.Diagnostics.ViewSettleMonitor Settle { get; }

    public EncDotNet.S100.Datasets.Pipelines.Catalog.IDatasetCatalog Catalog { get; }

    public TestMap Map(string id) => Panes.Maps[id];

    /// <summary>Starts the server and connects a client.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        await _host.Apply(ct);
        _client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = _host.Server!.Endpoint!,
                TransportMode = HttpTransportMode.StreamableHttp,
            }),
            cancellationToken: ct);
    }

    /// <summary>Calls a tool.</summary>
    public Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?>? arguments, CancellationToken ct) =>
        _client!.CallToolAsync(tool, arguments ?? [], cancellationToken: ct).AsTask();

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        await _host.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class EmptyCatalog : EncDotNet.S100.Datasets.Pipelines.Catalog.IDatasetCatalog
    {
        public IReadOnlyList<EncDotNet.S100.Datasets.Pipelines.Catalog.LoadedDataset> Datasets => [];

        public event EventHandler<EncDotNet.S100.Datasets.Pipelines.Catalog.DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    /// <summary>Records the picks pick_features shows in the viewer.</summary>
    internal sealed class RecordingPresenter : IGeographicPickPresenter
    {
        public List<(IReadOnlyList<GeographicPickFeature> Features, MapViewId? Map)> Presented { get; } = [];

        public IReadOnlyList<PickHit> Present(double latitude, double longitude, IReadOnlyList<GeographicPickFeature> features, MapViewId? map)
        {
            Presented.Add((features, map));
            return [];
        }
    }
}
