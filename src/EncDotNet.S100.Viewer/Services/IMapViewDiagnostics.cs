namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// One map's render diagnostics (split map, #1735): what the MCP
/// <c>await_render_idle</c>, <c>get_render_stats</c> and <c>await_settled</c>
/// tools read when their <c>map</c> argument names it.
/// </summary>
internal interface IMapViewDiagnostics
{
    /// <summary>This map's own render-activity monitor, or <see langword="null"/> before it's attached.</summary>
    IRenderActivityMonitor? RenderActivity { get; }

    /// <summary>The tile half of this map's settled signal.</summary>
    /// <returns>The map's tile settle state.</returns>
    EncDotNet.S100.Rendering.Engine.TileSettleState EvaluateSettle();

    /// <summary>Asks this map to repaint.</summary>
    void RefreshGraphics();
}
