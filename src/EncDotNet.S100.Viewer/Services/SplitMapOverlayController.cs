using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// One pane's view of the other panes while split (#1733): the extent boxes
/// of much smaller panes (split map design note D15) and the cursor echo of
/// the pointer over another pane (D16). With one map it draws nothing.
/// </summary>
internal sealed class SplitMapOverlayController : IDisposable
{
    private readonly IMapLayerCollection _layers;
    private readonly MapViewId _self;
    private readonly IMapViewportSet _viewports;
    private readonly MapCursorEcho _echo;
    private readonly MapSplitViewModel _split;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly Action<Action> _marshal;
    private readonly Action _requestRedraw;
    private readonly MemoryLayer _extentLayer = SplitMapOverlayLayer.Create(SplitMapOverlayLayer.ExtentLayerName);
    private readonly MemoryLayer _echoLayer = SplitMapOverlayLayer.Create(SplitMapOverlayLayer.EchoLayerName);
    private bool _hasExtents;
    private bool _hasEcho;
    private bool _disposed;

    /// <param name="layers">This pane's map layers; the overlays go on its overlay tier.</param>
    /// <param name="self">This pane's map.</param>
    /// <param name="viewports">Every map's view.</param>
    /// <param name="echo">Where the pointer is, over which map.</param>
    /// <param name="split">The panes, their numbers and whether the window is split.</param>
    /// <param name="appearance">The accent for the extent boxes.</param>
    /// <param name="marshal">Runs work on the UI thread.</param>
    /// <param name="requestRedraw">Asks this pane's map to repaint.</param>
    public SplitMapOverlayController(
        IMapLayerCollection layers,
        MapViewId self,
        IMapViewportSet viewports,
        MapCursorEcho echo,
        MapSplitViewModel split,
        IMeasureOverlayAppearanceProvider appearance,
        Action<Action> marshal,
        Action requestRedraw)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(viewports);
        ArgumentNullException.ThrowIfNull(echo);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(marshal);
        ArgumentNullException.ThrowIfNull(requestRedraw);
        _layers = layers;
        _self = self;
        _viewports = viewports;
        _echo = echo;
        _split = split;
        _appearance = appearance;
        _marshal = marshal;
        _requestRedraw = requestRedraw;

        _viewports.ViewportsChanged += OnViewportsChanged;
        _echo.Changed += OnEchoChanged;
        _split.LayoutChanged += OnLayoutChanged;
        _appearance.Changed += OnLayoutChanged;

        _marshal(() =>
        {
            if (_disposed)
                return;
            _layers.AddOverlayLayer(_extentLayer);
            _layers.AddOverlayLayer(_echoLayer);
            RebuildAll();
        });
    }

    private void OnViewportsChanged(object? sender, IReadOnlyList<MapViewViewport> e) => _marshal(RebuildAll);

    private void OnLayoutChanged(object? sender, EventArgs e) => _marshal(RebuildAll);

    private void OnEchoChanged(object? sender, EventArgs e) => _marshal(() =>
    {
        if (!_disposed && RebuildEcho())
            _requestRedraw();
    });

    private void RebuildAll()
    {
        if (_disposed)
            return;
        var changed = RebuildExtents();
        changed |= RebuildEcho();
        if (changed)
            _requestRedraw();
    }

    /// <summary>This pane's view while split and unfolded, else <see langword="null"/>.</summary>
    private MapViewportSnapshot? OwnView()
    {
        if (!_split.IsSplit || _split.Panes.FirstOrDefault(p => p.Id == _self) is not { IsFolded: false })
            return null;
        foreach (var viewport in _viewports.Current)
        {
            if (viewport.View == _self)
                return viewport.Snapshot;
        }
        return null;
    }

    private bool RebuildExtents()
    {
        var boxes = OwnView() is { } own
            ? SplitMapOverlayLayer.ExtentBoxes(own, OtherPanes())
            : [];
        if (boxes.Count == 0 && !_hasExtents)
            return false;
        _hasExtents = boxes.Count > 0;
        SplitMapOverlayLayer.UpdateExtentBoxes(_extentLayer, boxes, _appearance.Current.Accent);
        return true;
    }

    private IEnumerable<PaneExtentBox> OtherPanes()
    {
        var current = _viewports.Current;
        foreach (var pane in _split.Panes)
        {
            if (pane.Id == _self || pane.IsFolded)
                continue;
            foreach (var viewport in current)
            {
                if (viewport.View == pane.Id)
                    yield return new PaneExtentBox(pane.Number, viewport.Snapshot);
            }
        }
    }

    private bool RebuildEcho()
    {
        // Every pane but the one under the pointer, where the position is in view (D16).
        DataModel.GeoPosition? at = null;
        var resolution = 0.0;
        if (_echo.Source is { } source && source != _self
            && _echo.Position is { } p
            && OwnView() is { } own && SplitMapOverlayLayer.Contains(own, p))
        {
            at = p;
            resolution = own.MercatorResolution;
        }

        if (at is null && !_hasEcho)
            return false;
        _hasEcho = at is not null;
        SplitMapOverlayLayer.UpdateCursorEcho(_echoLayer, at, resolution);
        return true;
    }

    /// <summary>Removes the overlays and unsubscribes. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _viewports.ViewportsChanged -= OnViewportsChanged;
        _echo.Changed -= OnEchoChanged;
        _split.LayoutChanged -= OnLayoutChanged;
        _appearance.Changed -= OnLayoutChanged;
        _marshal(() =>
        {
            _layers.RemoveOverlayLayer(_extentLayer);
            _layers.RemoveOverlayLayer(_echoLayer);
        });
    }
}
