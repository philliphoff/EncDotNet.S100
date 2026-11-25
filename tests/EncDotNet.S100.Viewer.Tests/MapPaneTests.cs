using Avalonia.Controls;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="MapPane"/>'s split chrome (#1732): the 26 px header while split
/// (D9) with the active accent (D5), the close button (D3), and the folded
/// strip (D17), named for UI automation after the map id.
/// </summary>
public sealed class MapPaneTests
{
    private sealed class Host(MapViews views) : IMapPaneHost
    {
        public IMapViewCapabilities OpenPane(MapViewId id, MapPaneViewport? viewport)
        {
            var view = new MapViewsTests.FakeMapView(id.Value);
            views.Add(view);
            return view;
        }

        public void ClosePane(IMapViewCapabilities view) => views.Remove(view);

        public MapPaneViewport? GetViewport(IMapViewCapabilities view) => null;
    }

    private static MapSplitViewModel TwoPanes()
    {
        var views = new MapViews(
            new MapCapabilityAccessor<IMapViewportController>(),
            new MapCapabilityAccessor<IMapCoordinateConverter>(),
            new MapCapabilityAccessor<IImageRenderer>(),
            new DemandPlannerSet());
        var split = new MapSplitViewModel(views, new Host(views), () => { });
        split.Restore(saved: null, restoreViews: true);
        split.OpenSplit();
        return split;
    }

    private static (ViewHost Host, MapPane Left, MapPane Right) Show(MapSplitViewModel split)
    {
        var left = new MapPane();
        var right = new MapPane();
        left.Bind(split.Panes[0]);
        right.Bind(split.Panes[1]);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return (ViewHost.Show(grid, width: 900, height: 500), left, right);
    }

    [AvaloniaFact]
    public void Each_pane_has_a_26_px_header_with_its_number_while_split()
    {
        var split = TwoPanes();
        var (host, left, right) = Show(split);
        using var _ = host;

        var header = host.Find<Border>("MapPane.map-1.Header");
        Assert.True(header.IsEffectivelyVisible);
        Assert.Equal(26, header.Bounds.Height);
        Assert.True(host.Find<Border>("MapPane.map-2.Header").IsEffectivelyVisible);
        // D5: the active pane's header is the one with the accent.
        Assert.Contains("active", host.Find<Border>("MapPane.map-2.Header").Classes);
        Assert.DoesNotContain("active", header.Classes);
        Assert.True(host.IsShown("MapPane.map-1.Close"));
        Assert.True(left.MapControl.IsEffectivelyVisible);
        Assert.True(right.MapControl.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Activating_a_pane_moves_the_header_accent()
    {
        var split = TwoPanes();
        var (host, _, _) = Show(split);
        using var _h = host;

        split.Activate(split.Panes[0].Id);
        host.Window.UpdateLayout();

        Assert.Contains("active", host.Find<Border>("MapPane.map-1.Header").Classes);
        Assert.DoesNotContain("active", host.Find<Border>("MapPane.map-2.Header").Classes);
    }

    [AvaloniaFact]
    public void The_header_close_button_closes_its_pane()
    {
        var split = TwoPanes();
        var (host, _, _) = Show(split);
        using var _h = host;

        host.Click(host.Find<Button>("MapPane.map-1.Close"));

        Assert.Equal(["map-2"], split.Panes.Select(p => p.Id.Value));
    }

    [AvaloniaFact]
    public void A_folded_pane_shows_only_its_strip_and_a_click_unfolds_it()
    {
        // D17: a 632 px map area folds the pane that isn't active.
        var split = TwoPanes();
        var (host, left, _) = Show(split);
        using var _h = host;

        split.MapAreaWidth = 632;
        host.Window.UpdateLayout();

        Assert.True(host.IsShown("MapPane.map-1.FoldStrip"));
        Assert.False(host.IsShown("MapPane.map-1.Header"));
        Assert.False(left.MapControl.IsEffectivelyVisible);

        host.Click(host.Find<Button>("MapPane.map-1.FoldStrip"));

        Assert.True(split.Panes[0].IsActive);
        Assert.False(split.Panes[0].IsFolded);
        Assert.True(split.Panes[1].IsFolded);
        Assert.True(left.MapControl.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void One_pane_has_no_header()
    {
        var split = TwoPanes();
        split.CloseSplit();
        var pane = new MapPane();
        pane.Bind(split.Panes[0]);
        using var host = ViewHost.Show(pane, width: 600, height: 400);

        Assert.False(host.IsShown("MapPane.map-2.Header"));
        Assert.False(host.IsShown("MapPane.map-2.FoldStrip"));
    }
}
