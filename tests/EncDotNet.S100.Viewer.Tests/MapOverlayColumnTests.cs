using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The map's overlay column (#1405, legends design note D14, D15, D20): the
/// <c>Map.Legend</c> button's pressed state over the right dock's Legend tab,
/// and the column wrap on maps too short for one column.
/// </summary>
public sealed class MapOverlayColumnTests : IDisposable
{
    // The overlay column's geometry in MainWindow.axaml: 32 px buttons, 2 px
    // apart; zoom has 16 px either side and the compass 4 px above it.
    private const double Button = 32;
    private const double ModeButtons = 6;
    private const double ModesHeight = (ModeButtons * Button) + ((ModeButtons - 1) * 2); // 202
    private const double NavigationHeight = 2 + 16 + (3 * Button) + 4 + 16 + 2 + 4 + Button; // 172

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Six_mode_buttons_make_a_374_px_column()
    {
        Assert.Equal(374, ModesHeight + NavigationHeight);
    }

    /// <summary>A stand-in for the column in MainWindow.axaml, on a map of the given height.</summary>
    private static (Grid Map, MapOverlayColumnPanel Column, Control Modes, Control Zoom, Control Compass) Column(double mapHeight)
    {
        var modes = new StackPanel { Spacing = 2 };
        for (var i = 0; i < ModeButtons; i++)
            modes.Children.Add(new Border { Width = Button, Height = Button });

        var zoom = new StackPanel { Spacing = 2, Margin = new Thickness(0, 16) };
        for (var i = 0; i < 3; i++)
            zoom.Children.Add(new Border { Width = Button, Height = Button });
        var navigation = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 0) };
        navigation.Children.Add(zoom);
        var compass = new Border { Width = Button, Height = Button, Margin = new Thickness(0, 4, 0, 0) };
        navigation.Children.Add(compass);

        var column = new MapOverlayColumnPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 12),
            ColumnGap = 8,
            WrappedSecondTop = 18,
            Children = { modes, navigation },
        };
        var map = new Grid { Width = 1000, Height = mapHeight, Children = { column } };
        return (map, column, modes, zoom, compass);
    }

    private static Rect InMap(Control control, Visual map) =>
        new(control.TranslatePoint(default, map)!.Value, control.Bounds.Size);

    [AvaloniaTheory]
    [InlineData(550)] // 1440 × 900, Timeline open
    [InlineData(634)] // 1280 × 720, Timeline closed
    [InlineData(398)] // exactly the column plus its margins
    public void A_map_tall_enough_keeps_one_column(double mapHeight)
    {
        var (map, column, modes, zoom, compass) = Column(mapHeight);
        using var host = ViewHost.Show(map, width: 1000, height: mapHeight);

        Assert.False(column.IsWrapped);
        Assert.Equal(new Rect(956, 12, 32, ModesHeight), InMap(modes, map));
        Assert.Equal(new Rect(956, 232, 32, 100), InMap(zoom, map));
        Assert.Equal(new Rect(956, 354, 32, 32), InMap(compass, map)); // the column ends at y 386
    }

    [AvaloniaTheory]
    [InlineData(397)] // a pixel short
    [InlineData(370)] // 1280 × 720, Timeline open
    [InlineData(300)]
    public void A_short_map_wraps_zoom_and_compass_into_a_second_column(double mapHeight)
    {
        var (map, column, modes, zoom, compass) = Column(mapHeight);
        using var host = ViewHost.Show(map, width: 1000, height: mapHeight);

        Assert.True(column.IsWrapped);
        // The mode buttons keep their place on the right edge (y 12–214) ...
        Assert.Equal(new Rect(956, 12, 32, ModesHeight), InMap(modes, map));
        // ... and zoom + compass sit 8 px to their left, from y 48 (below
        // the View pill) to y 202; the pair is 84 px wide with its margin.
        Assert.Equal(new Rect(916, 48, 32, 100), InMap(zoom, map));
        Assert.Equal(new Rect(916, 170, 32, 32), InMap(compass, map));
        Assert.Equal(84, 1000 - InMap(zoom, map).Left);
    }

    [AvaloniaFact]
    public void The_column_unwraps_when_the_map_grows_again()
    {
        var (map, column, _, zoom, _) = Column(370);
        using var host = ViewHost.Show(map, width: 1000, height: 700);
        Assert.True(column.IsWrapped);
        Assert.Contains(":wrapped", column.Classes);

        map.Height = 550;
        host.Settle();

        Assert.False(column.IsWrapped);
        Assert.Equal(new Point(956, 232), InMap(zoom, map).Position);
    }

    private sealed class Tab : IActivityTab
    {
        public required string Id { get; init; }
        public int Order { get; init; }
        public string Title { get; init; } = "T";
        public string Name => Title;
        public string Tooltip => "Tip";
        public object ViewModel { get; } = new object();
        public Control CreateView() => new ContentControl();
        public bool PersistAsLastSelected => false;
        public TabDock Dock => TabDock.Right;
        public bool AutoOpenOnContentSignal => true;
        public Control CreateIcon() => new Border();
        public bool IsVisible => true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    /// <summary>The binding source MainWindow gives the button: the main view model's <c>Layout</c>.</summary>
    private sealed class Shell(ShellLayoutViewModel layout)
    {
        public ShellLayoutViewModel Layout { get; } = layout;
    }

    private (ShellLayoutViewModel Layout, Tab Pick, MapToggleButton Button) LegendButton()
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsPath };
        var pick = new Tab { Id = ShellLayoutViewModel.PickReportTabId, Order = 10, Title = "Object information" };
        var legend = new Tab { Id = ShellLayoutViewModel.LegendTabId, Order = 20, Title = "Legend" };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        var layout = new ShellLayoutViewModel(settings, [legend, pick], timeline, action => action());

        // As MainWindow.axaml declares Map.Legend.
        var button = new MapToggleButton
        {
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            DataContext = new Shell(layout),
        };
        button.Classes.Add("Icon");
        button.Classes.Add("MapOverlay");
        AutomationProperties.SetAutomationId(button, "Map.Legend");
        button[!MapToggleButton.IsCheckedProperty] = new Binding("Layout.IsLegendTabShown");
        button[!Avalonia.Controls.Button.CommandProperty] = new Binding("Layout.ToggleLegendCommand");
        return (layout, pick, button);
    }

    private static ToggleState Toggle(Control button) =>
        ControlAutomationPeer.CreatePeerForElement(button).GetProvider<IToggleProvider>()!.ToggleState;

    [AvaloniaFact]
    public void The_Legend_button_is_not_pressed_and_the_tab_stays_closed_until_pressed()
    {
        var (layout, _, button) = LegendButton();
        using var host = ViewHost.Show(button, width: 200, height: 200);

        // D14: nothing opens the tab by itself.
        Assert.False(layout.IsRightDockOpen);
        Assert.False(button.IsChecked);
        Assert.DoesNotContain(":checked", button.Classes);
        Assert.Equal(ToggleState.Off, Toggle(button));
    }

    [AvaloniaFact]
    public void Pressing_the_Legend_button_shows_the_tab_and_pressing_again_closes_the_dock()
    {
        var (layout, _, button) = LegendButton();
        using var host = ViewHost.Show(button, width: 200, height: 200);

        host.Click(button);
        Assert.True(layout.IsLegendTabShown);
        Assert.True(button.IsChecked);
        Assert.Contains(":checked", button.Classes);
        Assert.Equal(ToggleState.On, Toggle(button));

        host.Click(button);
        Assert.False(layout.IsRightDockOpen);
        Assert.False(button.IsChecked);
        Assert.Equal(ToggleState.Off, Toggle(button));
    }

    [AvaloniaFact]
    public void Pressing_it_again_returns_to_Object_information_when_that_was_showing()
    {
        var (layout, pick, button) = LegendButton();
        layout.SelectTabCommand.Execute(pick);
        using var host = ViewHost.Show(button, width: 200, height: 200);
        Assert.False(button.IsChecked);

        host.Click(button);
        Assert.True(button.IsChecked);

        host.Click(button);
        Assert.True(layout.IsRightDockOpen);
        Assert.Same(pick, layout.SelectedRightTab);
        Assert.False(button.IsChecked);
    }

    [AvaloniaFact]
    public void The_toggle_pattern_presses_the_button()
    {
        var (layout, _, button) = LegendButton();
        using var host = ViewHost.Show(button, width: 200, height: 200);

        ControlAutomationPeer.CreatePeerForElement(button).GetProvider<IToggleProvider>()!.Toggle();
        host.Settle();

        Assert.True(layout.IsLegendTabShown);
        Assert.Equal(ToggleState.On, Toggle(button));
    }

    [AvaloniaFact]
    public void Showing_the_tab_another_way_presses_the_button()
    {
        var (layout, _, button) = LegendButton();
        using var host = ViewHost.Show(button, width: 200, height: 200);

        layout.ToggleLegendCommand.Execute(null); // View › Legend, Ctrl+Shift+L
        host.Settle();

        Assert.True(button.IsChecked);
    }
}
