using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Points the render-activity and settle monitors at the window's maps
/// (split map, #1732): the busy probe asks every map, so the MCP
/// <c>await_render_idle</c> tool waits for all of them; the settle probe
/// (<c>await_settled</c>) and its redraw follow the active map, which the MCP
/// map tools act on until they gain a <c>map</c> argument (#1735).
/// </summary>
internal sealed class MapViewProbes : IDisposable
{
    private readonly MapViews _views;
    private readonly RenderActivityMonitor _activity;
    private readonly ViewSettleMonitor _settle;

    /// <summary>Starts following <paramref name="views"/>.</summary>
    /// <param name="views">The window's maps.</param>
    /// <param name="activity">The render-activity monitor.</param>
    /// <param name="settle">The settle monitor.</param>
    public MapViewProbes(MapViews views, RenderActivityMonitor activity, ViewSettleMonitor settle)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(settle);
        _views = views;
        _activity = activity;
        _settle = settle;
        _views.ActiveChanged += OnActiveChanged;
        OnActiveChanged(this, EventArgs.Empty);
    }

    /// <summary>Stops following the maps and clears the probes.</summary>
    public void Dispose()
    {
        _views.ActiveChanged -= OnActiveChanged;
        Clear();
    }

    private void OnActiveChanged(object? sender, EventArgs e)
    {
        if (_views.Active is not MapView active)
        {
            Clear();
            return;
        }

        _activity.BusyProbe = () => _views.All.OfType<MapView>().Any(view => view.IsBusy());
        _settle.TileProbe = active.EvaluateSettle;
        _settle.RequestRedraw = active.RefreshGraphics;
    }

    private void Clear()
    {
        // No map: the static hub mustn't outlive the maps, and a torn-down
        // map mustn't be probed.
        RenderActivityHub.Sink = null;
        _activity.BusyProbe = null;
        _settle.TileProbe = null;
        _settle.RequestRedraw = null;
    }
}
