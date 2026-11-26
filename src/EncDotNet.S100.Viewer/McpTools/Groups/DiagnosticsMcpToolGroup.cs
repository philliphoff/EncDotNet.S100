using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>Rendering and performance diagnostics: <c>await_render_idle</c>,
/// <c>get_render_stats</c>, the perf harness's tools (<c>await_settled</c>,
/// <c>get_process_metrics</c>, <c>fling</c>, <c>portrayal_prewarm</c>) and
/// <c>get_recent_logs</c>.</summary>
internal sealed class DiagnosticsMcpToolGroup : IViewerMcpToolGroup
{
    private readonly IRenderActivityMonitor _renderActivity;
    private readonly PerfHarnessServices _perfHarness;
    private readonly ViewerSettings _settings;
    private readonly ICapabilityAccessor<IMapViewportController> _viewport;
    private readonly EncDotNet.S100.Viewer.Diagnostics.LogBuffer _logBuffer;
    private readonly MapViews _views;

    public DiagnosticsMcpToolGroup(
        IRenderActivityMonitor renderActivity,
        PerfHarnessServices perfHarness,
        ViewerSettings settings,
        ICapabilityAccessor<IMapViewportController> viewport,
        EncDotNet.S100.Viewer.Diagnostics.LogBuffer logBuffer,
        MapViews views)
    {
        ArgumentNullException.ThrowIfNull(renderActivity);
        ArgumentNullException.ThrowIfNull(perfHarness);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(logBuffer);
        ArgumentNullException.ThrowIfNull(views);

        _renderActivity = renderActivity;
        _perfHarness = perfHarness;
        _settings = settings;
        _viewport = viewport;
        _logBuffer = logBuffer;
        _views = views;
    }

    /// <inheritdoc />
    public string Toolset => McpToolsets.Diagnostics;

    /// <inheritdoc />
    public IEnumerable<McpServerTool> CreateTools() => McpToolsets.AssignDefault(Toolset, CreateGroupTools());

    private IEnumerable<McpServerTool> CreateGroupTools()
    {
        yield return AwaitRenderIdleMcpAdapter.Create(new AwaitRenderIdleTool(_renderActivity, _views));
        yield return GetRenderStatsMcpAdapter.Create(new GetRenderStatsTool(_renderActivity, _views));

        var harness = _perfHarness;
        var settings = _settings;
        yield return PerfHarnessMcpAdapters.Create(new AwaitSettledTool(harness.Settle, _views));
        yield return PerfHarnessMcpAdapters.Create(new GetProcessMetricsTool(
            harness.Metrics,
            _renderActivity,
            () => GetProcessMetricsTool.DescribeEnvironment(settings, harness.RenderCacheDirectory),
            EncDotNet.S100.Rendering.Engine.S100VectorTileRenderer.GetTileCacheMemory,
            () => EncDotNet.S100.Core.Scheduling.WorkScheduler.Shared.GetStats(),
            harness.Planner is { } planner ? planner.Stats.Snapshot : null));
        yield return PerfHarnessMcpAdapters.Create(new FlingTool(_viewport, harness.Settle.MarkTrigger));
        yield return PerfHarnessMcpAdapters.Create(new PortrayalPrewarmTool(
            harness.PrewarmStatus,
            harness.PrewarmOverride,
            () => settings.PortrayalPrewarmEnabled,
            harness.PrewarmConcurrency));

        yield return GetRecentLogsMcpAdapter.Create(new GetRecentLogsTool(_logBuffer));
    }
}
