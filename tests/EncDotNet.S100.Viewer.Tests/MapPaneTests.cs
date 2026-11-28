using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
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

    private static MapSplitViewModel TwoPanes() => TwoPanes(() => { });

    internal static MapPaneDatasetsViewModel NoDatasets(MapPaneViewModel pane) =>
        new(pane, new DatasetsViewModel(new FakeDatasetLoaderService()), _ => false, () => { }, action => action());

    private static MapSplitViewModel TwoPanes(Action requestSave)
    {
        var views = new MapViews(
            new MapCapabilityAccessor<IMapViewportController>(),
            new MapCapabilityAccessor<IMapCoordinateConverter>(),
            new MapCapabilityAccessor<IImageRenderer>(),
            new DemandPlannerSet());
        var split = new MapSplitViewModel(views, new Host(views), requestSave);
        split.Restore(saved: null, restoreViews: true);
        split.OpenSplit();
        return split;
    }

    private static (ViewHost Host, MapPane Left, MapPane Right) Show(MapSplitViewModel split)
    {
        var left = new MapPane();
        var right = new MapPane();
        left.Bind(split.Panes[0], NoDatasets(split.Panes[0]));
        right.Bind(split.Panes[1], NoDatasets(split.Panes[1]));
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
        pane.Bind(split.Panes[0], NoDatasets(split.Panes[0]));
        using var host = ViewHost.Show(pane, width: 600, height: 400);

        Assert.False(host.IsShown("MapPane.map-2.Header"));
        Assert.False(host.IsShown("MapPane.map-2.FoldStrip"));
    }

    [AvaloniaFact]
    public void The_header_chain_links_the_views_and_every_header_reads_Linked()
    {
        // D13: the chain in any header; each linked header reads "Linked".
        var split = TwoPanes();
        var (host, _, _) = Show(split);
        using var _h = host;

        var leftChain = host.Find<ToggleButton>("MapPane.map-1.Link");
        Assert.True(host.IsShown("MapPane.map-2.Link"));
        Assert.False(leftChain.IsChecked);
        Assert.Equal("Link views", Avalonia.Automation.AutomationProperties.GetName(leftChain));

        host.Click(leftChain);
        host.Window.UpdateLayout();

        Assert.True(split.IsLinked);
        foreach (var id in new[] { "MapPane.map-1.Link", "MapPane.map-2.Link" })
        {
            var chain = host.Find<ToggleButton>(id);
            Assert.True(chain.IsChecked);
            Assert.Equal("Unlink views", Avalonia.Automation.AutomationProperties.GetName(chain));
            Assert.Contains(
                chain.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == "Linked" && t.IsEffectivelyVisible);
        }

        host.Click(host.Find<ToggleButton>("MapPane.map-2.Link"));

        Assert.False(split.IsLinked);
        Assert.False(host.Find<ToggleButton>("MapPane.map-1.Link").IsChecked);
    }

    private static TextBox StartRenaming(ViewHost host, string id)
    {
        host.DoubleClick(host.Find<TextBlock>($"MapPane.{id}.Title"));
        var editor = host.Find<TextBox>($"MapPane.{id}.NameEditor");
        Assert.True(editor.IsEffectivelyVisible);
        Assert.False(host.IsShown($"MapPane.{id}.Title"));
        Assert.True(editor.IsFocused);
        return editor;
    }

    private static void ReplaceText(ViewHost host, TextBox editor, string text)
    {
        // The editor opens with the title selected, so typing replaces it.
        Assert.Equal(editor.Text?.Length ?? 0, editor.SelectionEnd - editor.SelectionStart);
        if (text.Length == 0)
            host.Press(PhysicalKey.Backspace);
        else
            host.Type(text);
    }

    [AvaloniaFact]
    public void Double_clicking_the_header_name_renames_the_pane_and_Enter_commits()
    {
        // D19: double-click the name, type, Enter.
        var saves = 0;
        var split = TwoPanes(() => saves++);
        var (host, _, _) = Show(split);
        using var _h = host;
        var before = saves;

        var editor = StartRenaming(host, "map-1");
        Assert.Equal("Map 1", editor.Text);
        Assert.Equal("Rename Map 1", Avalonia.Automation.AutomationProperties.GetName(editor));
        ReplaceText(host, editor, "S-57");
        host.Press(PhysicalKey.Enter);

        Assert.False(editor.IsEffectivelyVisible);
        Assert.True(host.IsShown("MapPane.map-1.Title"));
        Assert.Equal("S-57", host.Find<TextBlock>("MapPane.map-1.Title").Text);
        Assert.Equal("S-57", split.Panes[0].Name);
        Assert.True(saves > before);

        // Saved with the layout.
        var saved = new MapLayoutSettings();
        split.WriteTo(saved);
        Assert.Equal(["S-57", null], saved.Panes.Select(p => p.Name));
    }

    [AvaloniaFact]
    public void Esc_cancels_a_rename_and_keeps_the_name()
    {
        var split = TwoPanes();
        split.SetName(split.Panes[1].Id, "Low water");
        var (host, _, _) = Show(split);
        using var _h = host;
        // The main window binds Escape (leave pick mode); the editor still gets it.
        var windowEscapes = 0;
        host.Window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => windowEscapes++),
        });

        var editor = StartRenaming(host, "map-2");
        Assert.Equal("Low water", editor.Text);
        ReplaceText(host, editor, "Something else");
        host.Press(PhysicalKey.Escape);

        Assert.False(editor.IsEffectivelyVisible);
        Assert.Equal("Low water", host.Find<TextBlock>("MapPane.map-2.Title").Text);
        Assert.Equal("Low water", split.Panes[1].Name);
    }

    [AvaloniaFact]
    public void An_empty_name_restores_the_default()
    {
        var split = TwoPanes();
        split.SetName(split.Panes[1].Id, "Low water");
        var (host, _, _) = Show(split);
        using var _h = host;

        var editor = StartRenaming(host, "map-2");
        ReplaceText(host, editor, string.Empty);
        Assert.Equal(string.Empty, editor.Text);
        host.Press(PhysicalKey.Enter);

        Assert.Null(split.Panes[1].Name);
        Assert.Equal("Map 2", host.Find<TextBlock>("MapPane.map-2.Title").Text);
        var saved = new MapLayoutSettings();
        split.WriteTo(saved);
        Assert.All(saved.Panes, p => Assert.Null(p.Name));
    }

    [AvaloniaFact]
    public void The_split_menu_Rename_edits_the_active_pane()
    {
        // The split menu is a flyout, as in MainWindow: closing it hands
        // focus back to its button, which mustn't commit the editor at once.
        var split = TwoPanes();
        var left = new MapPane();
        var right = new MapPane();
        left.Bind(split.Panes[0], NoDatasets(split.Panes[0]));
        right.Bind(split.Panes[1], NoDatasets(split.Panes[1]));
        var rename = new MenuItem { Header = "Rename map…", Command = split.RenameActiveCommand };
        var menuButton = new Button { Content = "▾", Flyout = new MenuFlyout { Items = { rename } } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(left, 1);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        grid.Children.Add(menuButton);
        grid.Children.Add(left);
        grid.Children.Add(right);
        using var host = ViewHost.Show(grid, width: 900, height: 500);
        Assert.True(split.RenameActiveCommand.CanExecute(null));

        host.Click(menuButton);
        host.Click(rename);
        host.Settle();

        // map-2 is active after the split opens.
        Assert.True(host.IsShown("MapPane.map-2.NameEditor"));
        Assert.False(host.IsShown("MapPane.map-1.NameEditor"));
        Assert.True(host.Find<TextBox>("MapPane.map-2.NameEditor").IsFocused);
        host.Type("Tide");
        host.Press(PhysicalKey.Enter);
        Assert.Equal("Tide", split.Panes[1].Name);

        split.CloseSplit();
        Assert.False(split.RenameActiveCommand.CanExecute(null));
    }
}
