using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Viewer;

// The split map (#1732; split map design note D1–D9, D17): the panes side by
// side, which one is active, and the window chrome placed against them.
public partial class MainWindow
{
    /// <inheritdoc />
    IMapViewCapabilities IMapPaneHost.OpenPane(MapViewId id, MapPaneViewport? viewport)
    {
        var pane = new MapPane();
        // The first map keeps the DI planner (the perf harness reads it);
        // every other map has its own (#1728).
        var planner = _diPlannerTaken
            ? new EncDotNet.S100.Rendering.Engine.Prediction.DemandPlanner()
            : App.Services.GetRequiredService<EncDotNet.S100.Rendering.Engine.Prediction.DemandPlanner>();
        _diPlannerTaken = true;
        var view = new MapView(id, pane, App.Services, planner);
        _paneViews[id] = view;
        MapPanesGrid.Children.Add(pane);
        AttachActivation(pane, view);
        pane.SizeChanged += (_, _) =>
        {
            if (ReferenceEquals(_mapViews.Active, view))
                TimeHud.MapWidth = pane.Bounds.Width;
        };

        // The loader catches the new map up to the datasets on Add (#1731).
        _mapViews.Add(view);
        view.Attach(
            _viewModel,
            _loader,
            _pickService,
            isActive: () => ReferenceEquals(_mapViews.Active, view),
            statusFocus: _statusFocus);
        view.SetRouteCheckLegendReserve(_routeCheckLegendReserve);
        // The first map became active before it was attached: hand it the
        // tools and the Legend source now.
        if (ReferenceEquals(_mapViews.Active, view))
            OnActiveMapChanged();
        if (viewport is { } start)
            ShowWhenLaidOut(view, start);
        return view;
    }

    /// <inheritdoc />
    void IMapPaneHost.ClosePane(IMapViewCapabilities view)
    {
        if (view is not MapView mapView || !_paneViews.Remove(mapView.Id))
            return;

        var pane = mapView.Pane;
        MapOverlayColumn.Children.Remove(pane.NavigationPanel);
        pane.HudSlot.Children.Remove(TimeHud);
        // Removing the map moves the active map first (D3), then the loader
        // releases its session (#1731).
        _mapViews.Remove(mapView);
        mapView.Dispose();
        MapPanesGrid.Children.Remove(pane);
    }

    /// <inheritdoc />
    MapPaneViewport? IMapPaneHost.GetViewport(IMapViewCapabilities view)
    {
        if (view is not MapView mapView || mapView.Pane.MapControl.Map?.Navigator is not { } navigator)
            return null;
        var viewport = navigator.Viewport;
        return viewport.Width > 0 && viewport.Resolution > 0
            ? new MapPaneViewport(viewport.CenterX, viewport.CenterY, viewport.Resolution)
            : null;
    }

    /// <summary>
    /// Shows <paramref name="viewport"/> in a new map once its control has a
    /// size: before that, Mapsui's first sizing would frame the map's home.
    /// </summary>
    private static void ShowWhenLaidOut(MapView view, MapPaneViewport viewport)
    {
        void Apply() => view.Host.SetViewportToCenterAndResolution(
            new MPoint(viewport.CenterX, viewport.CenterY), viewport.Resolution);

        var control = view.Pane.MapControl;
        if (control.Bounds.Width > 0 && control.Map?.Navigator.Viewport.Width > 0)
        {
            Apply();
            return;
        }

        void OnSizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width <= 0)
                return;
            control.SizeChanged -= OnSizeChanged;
            // After Mapsui's own handler has sized the viewport.
            Dispatcher.UIThread.Post(Apply, DispatcherPriority.Background);
        }

        control.SizeChanged += OnSizeChanged;
    }

    /// <summary>
    /// D4: a pane becomes active on a press (any button, or a drag start), a
    /// scroll or pinch, keyboard focus moving into it, or a tool click (a
    /// press). Hover doesn't count. Tunnelled, so the map's own handlers and
    /// the tools see the press after the pane is active.
    /// </summary>
    private void AttachActivation(MapPane pane, MapView view)
    {
        void Activate()
        {
            if (!_closing && !ReferenceEquals(_mapViews.Active, view) && _paneViews.ContainsKey(view.Id))
                _split.Activate(view.Id);
        }

        foreach (var target in new Interactive[] { pane, pane.NavigationPanel })
        {
            target.AddHandler(PointerPressedEvent, (_, _) => Activate(), RoutingStrategies.Tunnel, handledEventsToo: true);
            target.AddHandler(PointerWheelChangedEvent, (_, _) => Activate(), RoutingStrategies.Tunnel, handledEventsToo: true);
            target.AddHandler(PointerTouchPadGestureMagnifyEvent, (_, _) => Activate(), RoutingStrategies.Tunnel, handledEventsToo: true);
            target.AddHandler(GotFocusEvent, (_, _) => Activate(), RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    /// <summary>
    /// Lays the panes out side by side (D17): a star column each, at least
    /// 320 px while split, a 28 px column for a folded pane, and a splitter
    /// between two unfolded panes. Then places the window chrome against them
    /// (D8): the rightmost pane's zoom column under the mode buttons, the
    /// Time HUD in the active pane, and the overlays below the headers.
    /// </summary>
    private void ApplyPaneLayout()
    {
        var panes = _split.Panes;
        var split = _split.IsSplit;
        var signature = string.Join('|', panes.Select(p => $"{p.Id}:{p.IsFolded}")) + $"|{split}";
        if (signature != _paneLayoutSignature)
        {
            _paneLayoutSignature = signature;
            RebuildPaneColumns(panes, split);
        }

        foreach (var pane in panes)
        {
            if (_paneViews.TryGetValue(pane.Id, out var view) && view.Pane.Header.DataContext != pane)
                view.Pane.Bind(pane);
        }

        // D8: the rightmost unfolded pane's zoom buttons and compass sit in the
        // window's overlay column, under the mode buttons; the others keep
        // theirs in the pane.
        var rightmost = panes.LastOrDefault(p => !p.IsFolded);
        foreach (var pane in panes)
        {
            if (!_paneViews.TryGetValue(pane.Id, out var view))
                continue;
            var navigation = view.Pane.NavigationPanel;
            var inColumn = ReferenceEquals(pane, rightmost);
            Panel target = inColumn ? MapOverlayColumn : view.Pane.NavigationSlot;
            if (!ReferenceEquals(navigation.Parent, target))
            {
                (navigation.Parent as Panel)?.Children.Remove(navigation);
                target.Children.Add(navigation);
            }
            view.Pane.NameNavigation(inColumn);
        }

        // The window chrome starts under the headers and leaves out folded strips.
        var firstUnfolded = panes.ToList().FindIndex(p => !p.IsFolded);
        var lastUnfolded = panes.ToList().FindLastIndex(p => !p.IsFolded);
        var left = firstUnfolded < 0 ? 0 : firstUnfolded * MapSplitViewModel.FoldedStripWidth;
        var right = lastUnfolded < 0 ? 0 : (panes.Count - 1 - lastUnfolded) * MapSplitViewModel.FoldedStripWidth;
        MapWindowOverlays.Margin = new Thickness(left, split ? MapSplitViewModel.HeaderHeight : 0, right, 0);

        PlaceTimeHud();
        UpdateStatusMapLabel();
        _viewModel.Layout.IsMapSplit = split;
        ApplyRightDockOverlay();
    }

    private void RebuildPaneColumns(IReadOnlyList<MapPaneViewModel> panes, bool split)
    {
        foreach (var splitter in MapPanesGrid.Children.OfType<GridSplitter>().ToArray())
            MapPanesGrid.Children.Remove(splitter);
        MapPanesGrid.ColumnDefinitions.Clear();

        for (var i = 0; i < panes.Count; i++)
        {
            var pane = panes[i];
            if (i > 0 && !pane.IsFolded && !panes[i - 1].IsFolded)
            {
                MapPanesGrid.ColumnDefinitions.Add(new ColumnDefinition(MapSplitViewModel.SplitterWidth, GridUnitType.Pixel));
                var splitter = new GridSplitter
                {
                    Width = MapSplitViewModel.SplitterWidth,
                    ResizeDirection = GridResizeDirection.Columns,
                    Background = Avalonia.Media.Brushes.Transparent,
                };
                splitter.Classes.Add("PaneSplitter");
                Behaviors.HoverDelay.SetDelayMilliseconds(splitter, 500);
                Avalonia.Automation.AutomationProperties.SetAutomationId(splitter, $"MapPane.{pane.Id.Value}.Splitter");
                splitter.DragCompleted += (_, _) => OnPaneSplitterDragged();
                var before = panes[i - 1];
                var after = pane;
                splitter.AddHandler(
                    PointerMovedEvent,
                    (_, e) => OnPaneSplitterMoved(splitter, before, after, e),
                    Avalonia.Interactivity.RoutingStrategies.Bubble,
                    handledEventsToo: true);
                Grid.SetColumn(splitter, MapPanesGrid.ColumnDefinitions.Count - 1);
                MapPanesGrid.Children.Add(splitter);
            }

            var column = pane.IsFolded
                ? new ColumnDefinition(MapSplitViewModel.FoldedStripWidth, GridUnitType.Pixel)
                : new ColumnDefinition(pane.Weight, GridUnitType.Star)
                {
                    MinWidth = split ? MapSplitViewModel.MinimumPaneWidth : 0,
                };
            MapPanesGrid.ColumnDefinitions.Add(column);
            if (_paneViews.TryGetValue(pane.Id, out var view))
                Grid.SetColumn(view.Pane, MapPanesGrid.ColumnDefinitions.Count - 1);
        }
    }

    /// <summary>
    /// A splitter between panes is being dragged: the panes stop at their
    /// minimum width, and dragging on, past half the minimum again, folds the
    /// pane being squeezed to its strip.
    /// </summary>
    private void OnPaneSplitterMoved(
        GridSplitter splitter, MapPaneViewModel before, MapPaneViewModel after, Avalonia.Input.PointerEventArgs e)
    {
        if (_paneFoldPending
            || !e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed
            || !_paneViews.TryGetValue(before.Id, out var left)
            || !_paneViews.TryGetValue(after.Id, out var right))
            return;

        var x = e.GetPosition(MapPanesGrid).X;
        var threshold = MapSplitViewModel.MinimumPaneWidth / 2;
        var fold = x - left.Pane.Bounds.Left < threshold ? before
            : right.Pane.Bounds.Right - x < threshold ? after
            : null;
        if (fold is null)
            return;

        // Folding removes this splitter: do it after the event.
        _paneFoldPending = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _paneFoldPending = false;
            _split.Fold(fold);
        });
    }

    /// <summary>A splitter between panes was dragged: keep each pane's share (saved, D2).</summary>
    private void OnPaneSplitterDragged()
    {
        var unfolded = _split.Panes.Where(p => !p.IsFolded && _paneViews.ContainsKey(p.Id)).ToArray();
        var total = unfolded.Sum(p => _paneViews[p.Id].Pane.Bounds.Width);
        if (total <= 0)
            return;
        foreach (var pane in unfolded)
            pane.Weight = _paneViews[pane.Id].Pane.Bounds.Width / total * unfolded.Length;
        _split.OnWeightsChanged();
    }

    /// <summary>D8: the one Time HUD sits at the bottom centre of the active pane.</summary>
    private void PlaceTimeHud()
    {
        if (ActiveView is not { } active)
            return;
        var slot = active.Pane.HudSlot;
        if (!ReferenceEquals(TimeHud.Parent, slot))
        {
            (TimeHud.Parent as Panel)?.Children.Remove(TimeHud);
            slot.Children.Add(TimeHud);
        }
        TimeHud.MapWidth = active.Pane.Bounds.Width > 0 ? active.Pane.Bounds.Width : double.PositiveInfinity;
    }

    /// <summary>
    /// The active map changed (D4–D6): the tools and the Legend's Library
    /// coverage section follow it, and the status bar too while the pointer
    /// is over no map.
    /// </summary>
    private void OnActiveMapChanged()
    {
        if (_closing || ActiveView is not { } active)
            return;

        active.BindTools();
        PlaceTimeHud();
        OnStatusMapChanged();

        // D27: one Library coverage entry in the Legend, the active map's.
        var legend = App.Services.GetRequiredService<LegendService>();
        if (!ReferenceEquals(_activeCoverageLegend, active.LibraryCoverageLegend))
        {
            if (_activeCoverageLegend is not null)
                legend.RemoveSource(_activeCoverageLegend);
            _activeCoverageLegend = active.LibraryCoverageLegend;
            if (_activeCoverageLegend is not null)
                legend.AddSource(_activeCoverageLegend);
        }
    }

    /// <summary>
    /// The status bar now describes another map (the one under the pointer,
    /// else the active one): its scale, overscale and depth basis, and the
    /// badge naming it while split.
    /// </summary>
    private void OnStatusMapChanged()
    {
        if (_closing)
            return;
        (_statusFocus.Current as MapView)?.RefreshStatusReadouts();
        UpdateStatusMapLabel();
    }

    private void UpdateStatusMapLabel() =>
        _viewModel.StatusBar.StatusMapLabel = _split.IsSplit
            ? _split.Panes.FirstOrDefault(p => ReferenceEquals(p.View, _statusFocus.Current))?.Label ?? string.Empty
            : string.Empty;
}
