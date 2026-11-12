using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Legend tab (#1404, legends design note D2, D4, D5, D8, D10, D11, D14,
/// D16): its toolbar, sections and rows over <see cref="LegendService"/>, and
/// the right dock's tab strip, View › Legend toggle and pick switch.
/// </summary>
public sealed class LegendTabTests : IDisposable
{
    private static readonly SwatchLegendEntry[] WaveLegend =
    [
        new("#F3EEFF", "0–0.5 m"),
        new("#E5DFFC", "0.5–1 m"),
        new("#48198A", "14 m and higher"),
    ];

    private static readonly SwatchLegendEntry[] CurrentLegend =
    [
        new("#D0E8FF", "0–1 kn"),
        new("#1F4E9A", "1 kn and more"),
    ];

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    private sealed class FixedFormat(TimeFormat format) : ITimeFormatProvider
    {
        public TimeFormat Current { get; set; } = format;

        public event Action<TimeFormat>? TimeFormatChanged;

        public void Change(TimeFormat format)
        {
            Current = format;
            TimeFormatChanged?.Invoke(format);
        }
    }

    /// <summary>An S-111 tile in view and a wave grid out of it, under a view of the S-111 tile.</summary>
    private static (LegendServiceTests.Fixture Fixture, LegendService Service) TwoProducts(PaletteType palette = PaletteType.Day)
    {
        var f = new LegendServiceTests.Fixture();
        f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        f.Add("gfswave.grib2", "GRIB2", 10, 10, 11, 11);
        f.Loader.SetEntries(
            LayerStackLegendTests.FillEntry("gfswave.grib2", WaveLegend),
            LegendServiceTests.VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend));
        f.Viewport.Publish(39, -76, 42, -73);
        return (f, f.Create(palette));
    }

    private static LegendViewModel Tab(LegendService service, FixedFormat? format = null) =>
        new(service, new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))), format ?? new FixedFormat(TimeFormat.Utc), find: null);

    [Fact]
    public void In_view_shows_only_what_the_view_draws_with_a_line_for_the_rest()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);

        var section = Assert.Single(tab.Sections);
        Assert.Equal("S-111", section.Id);
        Assert.Equal("Surface currents", section.Title);
        Assert.Equal("111US00_a.h5", section.Detail);
        Assert.Equal("S-111 Surface currents, 2 entries", section.AccessibleName);
        Assert.Equal("Legend.Section.S-111", section.AutomationId);
        Assert.Equal(["0–1 kn", "1 kn and more"], section.Rows.Select(r => r.Label));
        Assert.Equal(1, tab.NotInViewCount);
        Assert.True(tab.HasNotInView);
        Assert.Equal("1 more loaded, not in view", tab.NotInViewText);

        tab.ShowAllLoadedCommand.Execute(null);

        Assert.True(tab.IsAllLoaded);
        Assert.Equal(["S-111", "weather.wave.height"], tab.Sections.Select(s => s.Id));
        Assert.False(tab.HasNotInView);
    }

    [Fact]
    public void Sections_are_kept_across_a_pan_with_their_expanded_state()
    {
        var (f, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        tab.ScopeIndex = LegendViewModel.AllLoadedScope;
        var currents = tab.Sections[0];
        var rows = currents.Rows.ToArray();

        currents.IsExpanded = false;
        Assert.False(Assert.Single(service.Sections, s => s.Id == "S-111").IsExpanded);

        // Pan to the wave grid: S-111 leaves the view but keeps its view model,
        // rows and collapsed state.
        f.Viewport.Publish(9, 9, 12, 12);
        f.Clock.Advance(LegendService.PanSettleDelay * 2);

        Assert.Same(currents, tab.Sections[0]);
        Assert.False(currents.IsInView);
        Assert.False(currents.IsExpanded);
        Assert.Equal(rows, currents.Rows);

        tab.ScopeIndex = LegendViewModel.InViewScope;
        Assert.Equal(["weather.wave.height"], tab.Sections.Select(s => s.Id));
    }

    [Fact]
    public void The_filter_keeps_matching_rows_or_whole_sections_by_name()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        tab.IsAllLoaded = true;

        tab.Filter = "kn";
        var currents = Assert.Single(tab.Sections);
        Assert.Equal(["0–1 kn", "1 kn and more"], currents.VisibleGroups.SelectMany(g => g.Rows).Select(r => r.Label));

        tab.Filter = "higher";
        var wave = Assert.Single(tab.Sections);
        Assert.Equal(["14 m and higher"], wave.VisibleGroups.SelectMany(g => g.Rows).Select(r => r.Label));
        Assert.Equal(3, wave.RowCount);

        tab.Filter = "WAVE";
        Assert.Equal(3, Assert.Single(tab.Sections).VisibleGroups.Single().Rows.Count);

        tab.Filter = "nothing like it";
        Assert.True(tab.IsEmpty);
        Assert.Equal("No matches", tab.EmptyTitle);

        tab.ClearFilterCommand.Execute(null);
        Assert.Equal(2, tab.Sections.Count);
    }

    [Fact]
    public void Collapse_all_collapses_every_section_shown()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        tab.IsAllLoaded = true;
        Assert.True(tab.CollapseAllCommand.CanExecute(null));

        tab.CollapseAllCommand.Execute(null);

        Assert.All(tab.Sections, s => Assert.False(s.IsExpanded));
        Assert.All(service.Sections, s => Assert.False(s.IsExpanded));
        Assert.False(tab.CollapseAllCommand.CanExecute(null));
    }

    [Fact]
    public void A_product_without_data_at_the_view_time_says_so_in_its_header()
    {
        var f = new LegendServiceTests.Fixture();
        var a = f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        f.Loader.SetEntries(LegendServiceTests.VectorEntry("111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend));
        a.SetLayerTime(new LayerTime("08:00Z · T+20 h", LayerTimeState.Exact, null), null);
        using var service = f.Create();
        using var tab = Tab(service);

        Assert.Equal("111US00_a.h5 · 08:00Z · T+20 h", tab.Sections[0].Detail);
        Assert.False(tab.Sections[0].HasNoData);

        a.SetLayerTime(new LayerTime("no data · last 18:00Z", LayerTimeState.NoData, null), null);

        Assert.Equal("No data at 14:20 UTC", tab.Sections[0].Detail);
        Assert.True(tab.Sections[0].HasNoData);
    }

    [Fact]
    public void S111_arrows_make_a_section_with_their_caption()
    {
        // Slice 4 (#1406) puts rows and a caption on every coverage kind, not
        // only grids: the S-111 arrows' speed bands.
        var grid = (GridCoverageSubLayer)((CoverageStackPayload)LayerStackLegendTests.FillEntry("111US00.h5", CurrentLegend).Item.Payload).SubLayer;
        var arrows = new ArrowCoverageSubLayer
        {
            LayerKey = "s111.arrows",
            LayerName = "Surface currents: 111US00.h5",
            Plane = S98DisplayPlane.DynamicArrows,
            Coverage = grid.Coverage,
            Viewport = grid.Viewport,
            SymbolProvider = _ => null,
            FallbackExtent = new GeographicBounds(0, 0, 2, 2),
            Legend = CurrentLegend,
            LegendCaption = "Speed, kn · longer arrow = faster",
        };
        var result = new CoveragePortrayalResult
        {
            SubLayers = [arrows],
            Spec = new EncDotNet.S100.Core.SpecRef("S-111", default),
            SourceDatasetId = "111US00.h5",
            Info = "test",
        };
        var f = new LegendServiceTests.Fixture();
        f.Loader.SetEntries(new LayerStackEntry(
            new Mapsui.Layers.MemoryLayer("111US00.h5"),
            new SubLayerStackItem(new CoverageStackPayload(result, arrows), arrows.Plane, 0, "111US00.h5", "SurfaceCurrent")));
        using var service = f.Create();
        using var tab = Tab(service);

        var section = Assert.Single(tab.Sections);
        Assert.Equal("S-111", section.Id);
        Assert.Equal("Speed, kn · longer arrow = faster", section.Caption);
        Assert.True(section.HasCaption);
        Assert.Equal(["0–1 kn", "1 kn and more"], section.Rows.Select(r => r.Label));
    }

    [Fact]
    public void Swatches_sit_on_the_palettes_chart_background()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        var day = Assert.IsAssignableFrom<ISolidColorBrush>(tab.Sections[0].Rows.First().Tile);
        Assert.Equal(Color.FromRgb(201, 237, 255), day.Color);

        service.SetPalette(PaletteType.Night);

        var night = Assert.IsAssignableFrom<ISolidColorBrush>(tab.Sections[0].Rows.First().Tile);
        Assert.Equal(Colors.Black, night.Color);
    }

    [Fact]
    public void The_tab_never_opens_by_itself_but_Show_opens_it_at_a_section_and_rings_a_row()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        var opened = 0;
        ((IActivityTabContentSignal)tab).ContentBecameAvailable += (_, _) => opened++;
        LegendRevealEventArgs? reveal = null;
        tab.RevealRequested += (_, e) => reveal = e;
        tab.Filter = "kn";
        tab.CollapseAllCommand.Execute(null);

        // Loading, panning and palette changes don't open it (D14).
        service.SetPalette(PaletteType.Dusk);
        service.Refresh();
        Assert.Equal(0, opened);

        // The wave grid isn't in view and the filter hides it: Show switches to
        // All loaded, clears the filter, expands it and rings the row.
        service.Show("weather.wave.height", "0.5–1 m");

        Assert.Equal(1, opened);
        Assert.True(tab.IsAllLoaded);
        Assert.Equal(string.Empty, tab.Filter);
        var wave = tab.Sections.Single(s => s.Id == "weather.wave.height");
        Assert.True(wave.IsExpanded);
        Assert.NotNull(reveal);
        Assert.Same(wave, reveal.Section);
        Assert.Equal("0.5–1 m", reveal.Row?.Label);
        Assert.True(reveal.Row!.IsHighlighted);
        Assert.Single(tab.Sections.SelectMany(s => s.Rows), r => r.IsHighlighted);

        // Hiding the tab drops the ring.
        tab.IsTabShown = true;
        tab.IsTabShown = false;
        Assert.DoesNotContain(tab.Sections.SelectMany(s => s.Rows), r => r.IsHighlighted);
    }

    [Fact]
    public void The_tab_tells_the_service_whether_it_is_shown()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);

        tab.IsTabShown = true;
        Assert.True(service.IsTabShown);
        tab.IsTabShown = false;
        Assert.False(service.IsTabShown);
    }

    [Fact]
    public void Sections_from_a_source_go_on_top_and_remember_their_expanded_state()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        var source = new StateSource();
        service.AddSource(source);
        using var tab = Tab(service);
        Assert.Equal(["S-111"], tab.Sections.Select(s => s.Id));

        source.Set(new LegendSection
        {
            Id = "RouteCheck",
            Spec = "Route",
            Title = "Route check",
            Source = "Solent passage",
            DatasetIds = [],
            IsInView = true,
            IsExpanded = true,
            Plane = S98DisplayPlane.MarinerOverlay,
            Groups = [new LegendGroup("route.check.ukc", "UKC at ETA", [new SwatchLegendEntry("#00A000", "Meets")])],
        });

        Assert.Equal(["RouteCheck", "S-111"], tab.Sections.Select(s => s.Id));
        tab.Sections[0].IsExpanded = false;
        source.Set(source.Sections[0] with { Source = "Solent passage, 2" });
        Assert.False(tab.Sections[0].IsExpanded);
        Assert.Equal("Solent passage, 2", tab.Sections[0].Detail);

        service.RemoveSource(source);
        Assert.Equal(["S-111"], tab.Sections.Select(s => s.Id));
    }

    private sealed class StateSource : ILegendSectionSource
    {
        public IReadOnlyList<LegendSection> Sections { get; private set; } = [];

        public event EventHandler? Changed;

        public void Set(LegendSection section)
        {
            Sections = [section];
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    [AvaloniaFact]
    public void The_view_names_sections_and_rows_and_draws_tiles()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        tab.IsAllLoaded = true;
        using var host = ViewHost.Show(new LegendView { DataContext = tab }, width: 360, height: 700);

        var currents = host.Find<Expander>("Legend.Section.S-111");
        Assert.Equal("S-111 Surface currents, 2 entries", AutomationProperties.GetName(currents));
        Assert.Equal("GRIB2 Wave height, 3 entries", AutomationProperties.GetName(host.Find<Expander>("Legend.Section.weather.wave.height")));

        var rows = host.FindAll<Border>("Legend.Row");
        Assert.Equal(["0–1 kn", "1 kn and more", .. WaveLegend.Select(e => e.Label)], rows.Select(AutomationProperties.GetName));
        Assert.All(rows, r => Assert.Equal(AutomationControlType.ListItem, AutomationProperties.GetControlTypeOverride(r)));
        var tiles = host.FindAll<Border>("Legend.Row.Tile");
        Assert.All(tiles, t => Assert.Equal(Color.FromRgb(201, 237, 255), Assert.IsAssignableFrom<ISolidColorBrush>(t.Background).Color));

        Assert.True(host.IsShown("Legend.Toolbar.InView"));
        Assert.True(host.IsShown("Legend.Filter"));
        Assert.True(host.IsShown("Legend.CollapseAll"));
        Assert.False(host.IsShown("Legend.NotInView"));

        // Collapsing from the header hides the rows and is remembered.
        currents.IsExpanded = false;
        host.Settle();
        Assert.False(Assert.Single(service.Sections, s => s.Id == "S-111").IsExpanded);
    }

    [AvaloniaFact]
    public void The_view_switches_scope_and_filters()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        using var host = ViewHost.Show(new LegendView { DataContext = tab }, width: 360, height: 700);

        Assert.Single(host.All<Expander>(), e => e.IsVisible);
        Assert.True(host.IsShown("Legend.NotInView"));
        host.Click(host.Find<TabItem>("Legend.Toolbar.ScopeAllLoaded"));
        host.Settle();
        Assert.True(tab.IsAllLoaded);
        Assert.Equal(2, host.All<Expander>().Count(e => e.IsVisible));

        host.Click(host.Find<TextBox>("Legend.Filter"));
        host.Type("higher");
        host.Settle();
        Assert.Equal("higher", tab.Filter);
        Assert.Equal(["14 m and higher"], host.FindAll<Border>("Legend.Row").Where(r => r.IsEffectivelyVisible).Select(AutomationProperties.GetName));
    }

    [AvaloniaFact]
    public void A_narrow_tab_puts_the_filter_on_its_own_row()
    {
        var (_, service) = TwoProducts();
        using var _service = service;
        using var tab = Tab(service);
        using var host = ViewHost.Show(new LegendView { DataContext = tab }, width: 360, height: 400);
        var filter = host.Find<TextBox>("Legend.Filter");
        Assert.Equal(0, Grid.GetRow(filter));

        host.Window.Width = 250;
        host.Settle();

        Assert.Equal(1, Grid.GetRow(filter));
        host.Settle();
        Assert.InRange(filter.Bounds.Width, 200, 250);
    }

    [AvaloniaFact]
    public void The_empty_tab_explains_itself()
    {
        var f = new LegendServiceTests.Fixture();
        using var service = f.Create();
        using var tab = Tab(service);
        using var host = ViewHost.Show(new LegendView { DataContext = tab }, width: 360, height: 400);

        Assert.True(tab.IsEmpty);
        Assert.Equal("No legends yet", tab.EmptyTitle);
        Assert.Empty(host.All<Expander>());
    }

    // ── Right dock: tab strip, View › Legend, pick switch ────────────────

    private sealed class Tab2 : IActivityTab
    {
        public required string Id { get; init; }
        public int Order { get; init; }
        public string Title { get; init; } = "T";
        public string Name => Title;
        public string Tooltip { get; init; } = "Tip";
        public object ViewModel { get; init; } = new object();
        public Control CreateView() => new ContentControl();
        public bool PersistAsLastSelected => false;
        public TabDock Dock => TabDock.Right;
        public bool AutoOpenOnContentSignal => true;
        public Control CreateIcon() => new Border { Width = 22, Height = 22 };
        public bool IsVisible => true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private sealed class SignalingVm : IActivityTabContentSignal
    {
        public event EventHandler? ContentBecameAvailable;

        public void Raise() => ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
    }

    private (ShellLayoutViewModel Layout, Tab2 Pick, Tab2 Legend, LegendViewModel LegendVm, LegendService Service, PickReportViewModel PickVm, ViewerSettings Settings) RightDock(ViewerSettings? settings = null)
    {
        settings ??= new ViewerSettings { SettingsFilePath = _settingsPath };
        var (_, service) = TwoProducts();
        var legendVm = Tab(service);
        var pickVm = TestPickReport.Create();
        var pick = new Tab2 { Id = ShellLayoutViewModel.PickReportTabId, Order = 10, Title = "Object information", ViewModel = pickVm };
        var legend = new Tab2 { Id = ShellLayoutViewModel.LegendTabId, Order = 20, Title = "Legend", ViewModel = legendVm };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        var layout = new ShellLayoutViewModel(settings, [legend, pick], timeline, action => action());
        return (layout, pick, legend, legendVm, service, pickVm, settings);
    }

    [Fact]
    public void The_right_dock_shows_a_strip_of_Object_information_then_Legend()
    {
        var d = RightDock();

        Assert.True(d.Layout.HasRightDockTabs);
        Assert.Equal([ShellLayoutViewModel.PickReportTabId, ShellLayoutViewModel.LegendTabId], d.Layout.VisibleRightTabs.Select(t => t.Id));
        Assert.Same(d.Pick, d.Layout.SelectedRightTab);
        Assert.False(d.Layout.IsRightDockOpen); // nothing opens by itself
    }

    [Fact]
    public void View_Legend_opens_the_tab_and_puts_back_what_the_dock_showed()
    {
        var d = RightDock();

        // From closed: opens on Legend, and closes again.
        d.Layout.ToggleLegendCommand.Execute(null);
        Assert.True(d.Layout.IsLegendTabShown);
        Assert.True(d.Service.IsTabShown);
        Assert.Equal(ShellLayoutViewModel.LegendTabId, d.Settings.LastSelectedRightTab);
        d.Layout.ToggleLegendCommand.Execute(null);
        Assert.False(d.Layout.IsRightDockOpen);
        Assert.False(d.Service.IsTabShown);

        // From Object information: back to Object information.
        d.Layout.SelectTabCommand.Execute(d.Pick);
        d.Layout.ToggleLegendCommand.Execute(null);
        Assert.True(d.Layout.IsLegendTabShown);
        d.Layout.ToggleLegendCommand.Execute(null);
        Assert.True(d.Layout.IsRightDockOpen);
        Assert.Same(d.Pick, d.Layout.SelectedRightTab);
        Assert.False(d.Service.IsTabShown);
    }

    [Fact]
    public void View_Legend_unfolds_a_folded_dock()
    {
        var d = RightDock();
        d.Layout.SelectTabCommand.Execute(d.Pick);
        d.Layout.IsRightDockFolded = true;
        Assert.Equal("Show Object information", d.Layout.RightDockEdgeTooltip);

        d.Layout.ToggleLegendCommand.Execute(null);

        Assert.False(d.Layout.IsRightDockFolded);
        Assert.True(d.Layout.IsLegendTabShown);
    }

    [Fact]
    public void A_legend_link_opens_the_tab_and_a_pick_switches_back_to_Object_information()
    {
        var d = RightDock();
        PickReportAutoOpenTests.Pick(d.PickVm);
        Assert.Same(d.Pick, d.Layout.SelectedRightTab);
        Assert.True(d.Layout.IsRightDockOpen);

        d.Service.Show("S-111");
        Assert.True(d.Layout.IsLegendTabShown);
        Assert.True(d.Service.IsTabShown);

        // A second pick, while the first is still shown, switches back (D2).
        PickReportAutoOpenTests.Pick(d.PickVm);
        Assert.Same(d.Pick, d.Layout.SelectedRightTab);
        Assert.False(d.Service.IsTabShown);
    }

    [Fact]
    public void The_selected_right_tab_is_restored()
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsPath, LastSelectedRightTab = ShellLayoutViewModel.LegendTabId, IsRightDockOpen = true };

        var d = RightDock(settings);

        Assert.True(d.Layout.IsLegendTabShown);
        Assert.True(d.Service.IsTabShown);
    }

    [AvaloniaFact]
    public void The_strip_shows_labels_and_falls_back_to_icons_when_narrow()
    {
        var d = RightDock();
        var strip = new DockTabStrip
        {
            Tabs = d.Layout.VisibleRightTabs,
            SelectedTabId = d.Layout.SelectedRightTabId,
            SelectCommand = d.Layout.SelectTabCommand,
        };
        var holder = new Border { Width = 318, Child = strip, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        using var host = ViewHost.Show(holder, width: 400, height: 60);

        var panel = host.All<DockTabStripPanel>().Single();
        Assert.False(panel.IsCompact);
        var legendTab = host.Find<ToggleButton>("Dock.Tab.Legend");
        Assert.Equal("Legend", AutomationProperties.GetName(legendTab));
        Assert.True(host.Find<ToggleButton>("Dock.Tab.PickReport").IsChecked);
        Assert.Null(ToolTip.GetTip(legendTab));

        // Too narrow for the labels: icons only, the title as tooltip and name.
        holder.Width = 60;
        host.Settle();
        Assert.True(panel.IsCompact);
        Assert.Equal("Legend", ToolTip.GetTip(legendTab));
        Assert.Equal("Legend", AutomationProperties.GetName(legendTab));
        Assert.True(panel.Bounds.Width <= 60);

        // Wide again: the labels come back.
        holder.Width = 318;
        host.Settle();
        Assert.False(panel.IsCompact);

        host.Click(legendTab);
        Assert.True(d.Layout.IsLegendTabShown);
    }
}
