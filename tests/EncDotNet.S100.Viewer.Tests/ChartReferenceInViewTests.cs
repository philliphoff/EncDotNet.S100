using Avalonia.Controls;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Chart reference slice 5 (#1638, symbol dictionary §4.1, §4.5, R5): In view
/// agrees with the Legend tab's catalogue key, hover rings what the Legend row
/// rings, and the Legend tab's Show all opens the activity at the product's
/// symbols with In view on.
/// </summary>
public sealed class ChartReferenceInViewTests : IDisposable
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle class=\"sCHBLK\" cx=\"5\" cy=\"5\" r=\"4\"/></svg>";

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"chart-reference-in-view-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    // ── Fixtures ─────────────────────────────────────────────────────────

    /// <summary>One drawn feature: its class, the symbol it draws (or null for a line), where, and its SCAMIN.</summary>
    private sealed record Drawn(string Type, string? Symbol, double Lat, double Lon, double? ScaleMinimum = null);

    private sealed class Features(SpecRef spec, IReadOnlyList<(string Ref, string Type)> features) : IDatasetProcessor
    {
        public SpecRef Spec { get; } = spec;

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public IEnumerable<FeatureSummary> EnumerateFeatures() =>
            features.Select(f => new FeatureSummary { FeatureRef = f.Ref, FeatureType = f.Type });
    }

    private sealed class Geometry(Dictionary<string, FeatureGeometry> features) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => features.GetValueOrDefault(featureReference);
    }

    /// <summary>An S-101 cell drawing <paramref name="drawn"/>, one feature each, with its processor's feature list.</summary>
    private static (LayerStackEntry Entry, IDatasetProcessor Processor) Cell(string datasetId, IReadOnlyList<Drawn> drawn, IReadOnlySet<string>? suppressed = null)
    {
        var refs = drawn.Select((d, i) => (Ref: $"{datasetId}:{i}", Drawn: d)).ToList();
        var geometry = refs.ToDictionary(
            r => r.Ref,
            r => r.Drawn.Symbol is null
                ? new FeatureGeometry { Type = GeometryType.Curve, Coordinates = [new(r.Drawn.Lat, r.Drawn.Lon), new(r.Drawn.Lat + 0.05, r.Drawn.Lon + 0.05)] }
                : new FeatureGeometry { Type = GeometryType.Point, Coordinates = [new(r.Drawn.Lat, r.Drawn.Lon)] });
        var subLayer = new VectorSubLayer
        {
            LayerKey = "s101.linework",
            LayerName = "Linework: " + datasetId,
            Instructions =
            [
                .. refs.Select(r => r.Drawn.Symbol is { } symbol
                    ? (DrawingInstruction)new PointInstruction { FeatureReference = r.Ref, SymbolReference = symbol, ScaleMinimum = r.Drawn.ScaleMinimum }
                    : new LineInstruction { FeatureReference = r.Ref, LineColor = "#000000", LineWidth = 1, ScaleMinimum = r.Drawn.ScaleMinimum }),
            ],
            Plane = S98DisplayPlane.BaseChartOver,
            SourceFeatureType = "s101.linework",
        };
        var result = new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day),
            GeometryProvider = new Geometry(geometry),
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
            SymbolProvider = _ => Svg,
            SymbolTitleProvider = _ => null, // rows are labelled by symbol name
        };
        var entry = new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 0, datasetId, subLayer.SourceFeatureType))
        {
            SuppressedFeatures = suppressed,
        };
        return (entry, new Features(new SpecRef("S-101", default), [.. refs.Select(r => (r.Ref, r.Drawn.Type))]));
    }

    /// <summary>
    /// A view of 50–51° N, 2–1° W over one S-101 cell: a lateral buoy, two
    /// lights, a beacon and a sounding in view; a coastline (a line, no
    /// symbol); a wreck outside the view; a buoy hidden by SCAMIN; and a
    /// light an S-98 rule suppressed.
    /// </summary>
    private sealed class Scene : IDisposable
    {
        public Scene()
        {
            Dataset = Legend.Add("101GB00TEST.000", "S-101", 50, -2, 51, -1);
            var (entry, processor) = Cell(
                Dataset.Id.Value,
                [
                    new("LateralBuoy", "BOYLAT13", 50.5, -1.5),
                    new("LightAllAround", "LIGHTS11", 50.4, -1.4),
                    new("LightAllAround", "LIGHTS11", 50.3, -1.3),
                    new("BCNLAT", "BCNLAT21", 50.2, -1.2), // an S-57 acronym in the feature list
                    new("Sounding", "SOUNDG12", 50.6, -1.6),
                    new("Coastline", null, 50.7, -1.7),
                    new("Wreck", "WRECKS01", 52.0, -1.5), // outside the view
                    new("CardinalBuoy", "BOYCAR01", 50.5, -1.5, ScaleMinimum: 1000), // not drawn at this scale
                    new("Landmark", "LNDMRK01", 50.5, -1.5), // suppressed below
                ],
                suppressed: new HashSet<string>(StringComparer.Ordinal) { Dataset.Id.Value + ":8" });
            Entry = entry;
            ((Dictionary<DatasetEntry, IDatasetProcessor>)Legend.Loader.Processors).Add(Dataset, processor);
            Legend.Viewport.Publish(50, -2, 51, -1);
            Legend.Loader.SetEntries(entry);
            Service = Legend.Create();
            Service.Refresh();
            Find = new LegendFindService(Service, Legend.Viewport, Legend.Clock, action => action());
            Map = new ChartReferenceMap(Service, Find);
        }

        public LegendServiceTests.Fixture Legend { get; } = new();

        public ChartReferenceServiceTests.Fixture Catalogues { get; } = new("S-101");

        public DatasetEntry Dataset { get; }

        public LayerStackEntry Entry { get; }

        public LegendService Service { get; }

        public LegendFindService Find { get; }

        public ChartReferenceMap Map { get; }

        public async Task<ChartReferenceViewModel> OpenAsync()
        {
            var vm = new ChartReferenceViewModel(Catalogues.Service, new ChartReferenceGlyphCache(PaletteType.Day));
            vm.ConnectMap(Map);
            vm.Activate();
            await vm.Loading;
            Assert.NotNull(vm.Index);
            return vm;
        }

        public void Dispose()
        {
            Map.Dispose();
            Find.Dispose();
            Service.Dispose();
            Catalogues.Dispose();
        }
    }

    // ── In view ──────────────────────────────────────────────────────────

    [Fact]
    public void The_symbols_in_view_are_the_catalogue_keys_symbols_for_the_same_view()
    {
        using var scene = new Scene();
        scene.Service.IsTabShown = true;
        scene.Service.Refresh();

        var drawn = scene.Service.DrawnInView("S-101");

        var section = Assert.Single(scene.Service.Sections);
        Assert.Equal(section.Rows.Select(r => r.Label).Order(StringComparer.Ordinal), drawn.Symbols.Order(StringComparer.Ordinal));
        Assert.Equal(["BCNLAT21", "BOYLAT13", "LIGHTS11", "SOUNDG12"], drawn.Symbols.Order(StringComparer.Ordinal));
        // Every class drawn in view, a line's too; not the wreck outside it,
        // the buoy SCAMIN hides or the landmark an S-98 rule removed.
        Assert.Equal(
            ["BCNLAT", "Coastline", "LateralBuoy", "LightAllAround", "Sounding"],
            drawn.FeatureTypes.Order(StringComparer.OrdinalIgnoreCase));

        // Another product draws nothing.
        Assert.Empty(scene.Service.DrawnInView("S-401").Symbols);
    }

    [Fact]
    public void A_hidden_dataset_draws_nothing_in_view()
    {
        using var scene = new Scene();
        scene.Dataset.IsVisible = false;

        var drawn = scene.Service.DrawnInView("S-101");

        Assert.Empty(drawn.Symbols);
        Assert.Empty(drawn.FeatureTypes);
    }

    [Fact]
    public async Task In_view_lists_the_symbols_and_feature_types_drawn_and_never_unreached_items()
    {
        using var scene = new Scene();
        using var vm = await scene.OpenAsync();
        var all = vm.Rows.Count();
        Assert.False(vm.InView);
        Assert.True(vm.CanShowInView);

        vm.InView = true;

        // Feature rows match by the class in the processor's feature list,
        // by code or S-57 acronym (Lateral Beacon is BCNLAT).
        Assert.Equal(
            ["Coastline", "LateralBeacon", "LateralBuoy", "LightAllAround", "Sounding"],
            vm.Rows.Select(r => r.Key.Code).Order(StringComparer.Ordinal));
        Assert.All(vm.Items.OfType<ChartReferenceHeaderViewModel>(), h => Assert.True(h.Count > 0));
        Assert.Equal(vm.Rows.Count(), vm.Items.OfType<ChartReferenceHeaderViewModel>().Sum(h => h.Count));

        // Symbol rows match by name; a family is in view when a member is (D3).
        vm.Mode = ChartReferenceMode.Symbols;
        var codes = vm.Rows.Select(r => r.Key.Code).ToList();
        Assert.Equal(["BCNLAT21", "BOYLAT13", "LIGHTS11", "SOUNDG"], codes.Order(StringComparer.Ordinal));
        Assert.True(Assert.IsType<PortrayalItemEntry>(vm.Rows.Single(r => r.Key.Code == "SOUNDG").Entry).IsFamily);
        Assert.DoesNotContain(vm.Items.OfType<ChartReferenceHeaderViewModel>(), h => h.Title == "Not used by a rule");

        // A search is limited too.
        vm.Query = "WRECKS01";
        Assert.Empty(vm.Items);
        Assert.True(vm.ShowNoMatches);
        vm.Query = string.Empty;
        Assert.Contains("Turn off In view", vm.NoMatchesDescription, StringComparison.Ordinal);

        vm.InView = false;
        vm.Mode = ChartReferenceMode.Features;
        Assert.Equal(all, vm.Rows.Count());
    }

    [Fact]
    public async Task In_view_follows_the_map_on_pan_end_never_under_the_pointer()
    {
        using var scene = new Scene();
        using var vm = await scene.OpenAsync();
        vm.Mode = ChartReferenceMode.Symbols;
        vm.InView = true;
        Assert.DoesNotContain(vm.Rows, r => r.Key.Code == "WRECKS01");

        // Pan north over the wreck: nothing changes while the map moves...
        scene.Legend.Viewport.Publish(51.5, -2, 52.5, -1);
        Assert.DoesNotContain(vm.Rows, r => r.Key.Code == "WRECKS01");

        // ...and the list follows once it has been still.
        scene.Legend.Clock.Advance(LegendService.PanSettleDelay);
        Assert.Equal(["WRECKS01"], vm.Rows.Select(r => r.Key.Code));
    }

    // ── Hover to find ────────────────────────────────────────────────────

    [Fact]
    public async Task Hovering_a_symbol_row_rings_the_features_the_Legend_row_rings()
    {
        using var scene = new Scene();
        scene.Service.IsTabShown = true;
        scene.Service.Refresh();
        using var vm = await scene.OpenAsync();
        vm.Mode = ChartReferenceMode.Symbols;
        vm.InView = true;

        var legend = scene.Service.FindFeatures("S-101", "LIGHTS11");
        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "LIGHTS11"));
        Assert.Null(scene.Find.Current); // after the hover delay, as in the Legend tab
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);

        var found = Assert.IsType<LegendFeatureFind>(scene.Find.Current);
        Assert.NotNull(legend);
        Assert.Equal(2, legend.MatchCount);
        Assert.Equal(legend.MatchCount, found.MatchCount);
        Assert.Equal(legend.Rings.SelectMany(r => r.Points), found.Rings.SelectMany(r => r.Points));
        Assert.Null(scene.Find.HoveredRow); // not a Legend row

        vm.LeaveRow(vm.Rows.Single(r => r.Key.Code == "LIGHTS11"));
        Assert.Null(scene.Find.Current);
    }

    [Fact]
    public async Task Hovering_a_feature_or_family_row_rings_its_features_and_a_map_move_clears_them()
    {
        using var scene = new Scene();
        using var vm = await scene.OpenAsync();

        // A feature type rings its features, by code and by S-57 acronym.
        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "LightAllAround"));
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(2, scene.Find.Current?.MatchCount);
        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "LateralBeacon"));
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(1, scene.Find.Current?.MatchCount);

        // A line rings its curve.
        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "Coastline"));
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        var coast = Assert.Single(Assert.IsType<LegendFeatureFind>(scene.Find.Current).Rings);
        Assert.NotEmpty(coast.Curves);

        // A family rings its members.
        vm.Mode = ChartReferenceMode.Symbols;
        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "SOUNDG"));
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(1, scene.Find.Current?.MatchCount);

        // The rings are for the view they were found in.
        scene.Legend.Viewport.Publish(50.1, -2, 51.1, -1);
        Assert.Null(scene.Find.Current);
    }

    [Fact]
    public void Hover_rings_at_most_the_Legend_tabs_cap()
    {
        var legend = new LegendServiceTests.Fixture();
        var dataset = legend.Add("101GB00MANY.000", "S-101", 50, -2, 51, -1);
        var (entry, processor) = Cell(dataset.Id.Value, [.. Enumerable.Range(0, 130).Select(i => new Drawn("Light", "LIGHTS11", 50.5, -1.5))]);
        ((Dictionary<DatasetEntry, IDatasetProcessor>)legend.Loader.Processors).Add(dataset, processor);
        legend.Viewport.Publish(50, -2, 51, -1);
        legend.Loader.SetEntries(entry);
        using var service = legend.Create();

        var found = service.FindReferenceFeatures(new ChartReferenceTarget("S-101", "S-101/Symbol/LIGHTS11", ["LIGHTS11"], []));

        Assert.Equal(130, found.MatchCount);
        Assert.Equal(LegendFeatureFinder.MaxRings, found.RingedCount);
        Assert.True(found.IsCapped);
    }

    // ── Show all from the Legend tab ─────────────────────────────────────

    [Fact]
    public void A_catalogue_key_that_lists_fewer_symbols_than_it_draws_links_to_Chart_reference()
    {
        var legend = new LegendServiceTests.Fixture();
        var dataset = legend.Add("101GB00MANY.000", "S-101", 50, -2, 51, -1);
        var (entry, _) = Cell(dataset.Id.Value, [.. Enumerable.Range(0, 15).Select(i => new Drawn("Light", $"SYMB{i:00}", 50.5, -1.5))]);
        legend.Viewport.Publish(50, -2, 51, -1);
        legend.Loader.SetEntries(entry);
        using var service = legend.Create();
        service.IsTabShown = true;
        service.Refresh();
        var opened = new List<string>();
        using var tab = new LegendViewModel(service, new ClockViewTime(new FakeTimeProvider()), new TestStationCharts.FixedTimeFormat(TimeFormat.Utc), find: null, opened.Add);

        var section = Assert.Single(tab.Sections);
        Assert.Equal("12 of 15 symbols drawn in view", section.Caption);
        Assert.True(section.HasChartReferenceLink);
        section.ShowInChartReferenceCommand.Execute(null);
        Assert.Equal(["S-101"], opened);

        // Without the activity there is no link.
        using var bare = new LegendViewModel(service, new ClockViewTime(new FakeTimeProvider()), new TestStationCharts.FixedTimeFormat(TimeFormat.Utc), find: null);
        Assert.False(Assert.Single(bare.Sections).HasChartReferenceLink);
    }

    [Fact]
    public void A_catalogue_key_listing_every_symbol_drawn_has_no_link()
    {
        using var scene = new Scene();
        scene.Service.IsTabShown = true;
        scene.Service.Refresh();

        var section = Assert.Single(scene.Service.Sections);
        Assert.Equal("Symbols drawn in view", section.Caption);
        Assert.Null(section.ChartReferenceSpec);
    }

    [Fact]
    public async Task Show_all_opens_the_activity_at_the_product_in_Symbols_mode_with_In_view_on()
    {
        using var scene = new Scene();
        using var catalogues = new ChartReferenceServiceTests.Fixture("S-101", "S-201");
        using var vm = new ChartReferenceViewModel(catalogues.Service, new ChartReferenceGlyphCache(PaletteType.Day));
        vm.ConnectMap(scene.Map);
        vm.Query = "beacon";
        var layout = Layout(vm, out var tab);
        Assert.Equal("S-101", vm.SelectedProduct?.Spec.Name);
        vm.SelectedProduct = vm.Products.Single(p => p.Spec.Name == "S-201");
        layout.IsLeftDockOpen = false;

        vm.ShowAllInView("S-101");

        Assert.True(layout.IsLeftDockOpen);
        Assert.Same(tab, layout.SelectedLeftTab);
        Assert.Equal("S-101", vm.SelectedProduct?.Spec.Name);
        Assert.Equal(ChartReferenceMode.Symbols, vm.Mode);
        Assert.True(vm.InView);
        Assert.Equal(string.Empty, vm.Query);
        vm.Activate();
        await vm.Loading;
        Assert.Equal(["BCNLAT21", "BOYLAT13", "LIGHTS11", "SOUNDG"], vm.Rows.Select(r => r.Key.Code).Order(StringComparer.Ordinal));

        // Showing all again with the activity open keeps it open.
        vm.ShowAllInView("S-101");
        Assert.True(layout.IsLeftDockOpen);
        Assert.Same(tab, layout.SelectedLeftTab);
    }

    [Fact]
    public void The_Help_menu_and_the_activity_bar_open_the_activity_with_In_view_off()
    {
        using var scene = new Scene();
        using var vm = new ChartReferenceViewModel(scene.Catalogues.Service, new ChartReferenceGlyphCache(PaletteType.Day));
        vm.ConnectMap(scene.Map);
        var layout = Layout(vm, out var tab);

        vm.ShowAllInView("S-101");
        Assert.True(vm.InView);
        layout.IsLeftDockOpen = false;
        layout.ShowChartReferenceCommand.Execute(null);
        Assert.True(layout.IsLeftDockOpen);
        Assert.False(vm.InView);

        vm.ShowAllInView("S-101");
        layout.IsLeftDockOpen = false;
        layout.SelectTabCommand.Execute(tab);
        Assert.True(layout.IsLeftDockOpen);
        Assert.False(vm.InView);
    }

    // ── Hover rings end with the panel (#1655) ───────────────────────────

    /// <summary>The activity, shown in the left dock with a row hovered and its rings drawn.</summary>
    private async Task<(ChartReferenceViewModel Vm, ShellLayoutViewModel Layout, IActivityTab Other, IActivityTab Page)> HoveredAsync(Scene scene)
    {
        var vm = await scene.OpenAsync();
        var tab = new Tab(vm);
        var other = new Tab(new object(), "Library", 10);
        var page = new Tab(new object(), ShellLayoutViewModel.ChartReferencePageTabId, 53, fillsContent: true);
        var layout = Layout([tab, other, page]);
        layout.ShowChartReferenceCommand.Execute(null);
        Assert.True(layout.IsLeftDockShown);
        Assert.True(vm.IsTabShown);

        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "LightAllAround"));
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(2, scene.Find.Current?.MatchCount);
        return (vm, layout, other, page);
    }

    [Fact]
    public async Task Closing_the_left_dock_clears_a_hovers_rings()
    {
        using var scene = new Scene();
        var (vm, layout, _, _) = await HoveredAsync(scene);
        using var owned = vm;

        layout.IsLeftDockOpen = false;

        Assert.False(vm.IsTabShown);
        Assert.Null(scene.Find.Current);
        // The hover ended: the Legend following the view doesn't bring the rings back.
        scene.Service.Refresh();
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Null(scene.Find.Current);
    }

    [Fact]
    public async Task Selecting_another_activity_clears_a_hovers_rings()
    {
        using var scene = new Scene();
        var (vm, layout, other, _) = await HoveredAsync(scene);
        using var owned = vm;

        layout.SelectedLeftTab = other;

        Assert.True(layout.IsLeftDockShown);
        Assert.False(vm.IsTabShown);
        Assert.Null(scene.Find.Current);
    }

    [Fact]
    public async Task Opening_the_page_clears_a_hovers_rings()
    {
        using var scene = new Scene();
        var (vm, layout, _, _) = await HoveredAsync(scene);
        using var owned = vm;

        layout.ShowChartReferencePageCommand.Execute(null);

        Assert.True(layout.IsContentPanelOpen);
        Assert.False(vm.IsTabShown);
        Assert.Null(scene.Find.Current);

        // Back from the page, the activity is shown again.
        layout.IsLeftDockOpen = false;
        Assert.True(layout.IsLeftDockShown);
        Assert.True(vm.IsTabShown);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("search")]
    [InlineData("in view")]
    public async Task Rebuilding_the_list_clears_a_hovers_rings(string change)
    {
        using var scene = new Scene();
        var (vm, _, _, _) = await HoveredAsync(scene);
        using var owned = vm;

        switch (change)
        {
            case "mode": vm.Mode = ChartReferenceMode.Symbols; break;
            case "search": vm.Query = "light"; break;
            default: vm.InView = true; break;
        }

        Assert.Null(scene.Find.Current);
        scene.Service.Refresh();
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Null(scene.Find.Current);
    }

    [Fact]
    public async Task Changing_the_product_clears_a_hovers_rings()
    {
        using var scene = new Scene();
        using var catalogues = new ChartReferenceServiceTests.Fixture("S-101", "S-201");
        using var vm = new ChartReferenceViewModel(catalogues.Service, new ChartReferenceGlyphCache(PaletteType.Day));
        vm.ConnectMap(scene.Map);
        vm.Activate();
        await vm.Loading;
        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "LightAllAround"));
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(2, scene.Find.Current?.MatchCount);

        vm.SelectedProduct = vm.Products.Single(p => p.Spec.Name == "S-201");

        Assert.Null(scene.Find.Current);
    }

    [Fact]
    public async Task Following_the_map_keeps_a_hover()
    {
        using var scene = new Scene();
        using var vm = await scene.OpenAsync();
        vm.InView = true;
        vm.HoverRow(vm.Rows.Single(r => r.Key.Code == "LightAllAround"));
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(2, scene.Find.Current?.MatchCount);

        // A pan that changes what is drawn rebuilds the list where the user
        // is; the row under the pointer is still hovered, so its rings come
        // back once the Legend has followed the view.
        scene.Legend.Viewport.Publish(50.35, -2, 51.35, -1);
        Assert.Null(scene.Find.Current);
        scene.Legend.Clock.Advance(LegendService.PanSettleDelay);
        scene.Legend.Clock.Advance(LegendFindService.HoverDelay);
        Assert.Equal(1, scene.Find.Current?.MatchCount);
    }

    // ── The view ─────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task The_In_view_toggle_limits_the_list()
    {
        using var scene = new Scene();
        using var vm = await scene.OpenAsync();
        var view = new ChartReferenceView { DataContext = vm };
        using var host = ViewHost.Show(view, 320, 800);

        var toggle = host.Find<Avalonia.Controls.Primitives.ToggleButton>("ChartReference.InView");
        Assert.True(toggle.IsVisible);
        Assert.Equal("In view", Avalonia.Automation.AutomationProperties.GetName(toggle));

        // An icon toggle, as the Library's Show coverage and At view time (#1657).
        Assert.Contains("panelIcon", toggle.Classes);
        Assert.Contains("coverageToggle", toggle.Classes);
        Assert.IsType<FluentIcons.Avalonia.FluentIcon>(toggle.Content);
        Assert.NotNull(ToolTip.GetTip(toggle));

        toggle.IsChecked = true;
        host.Settle();

        Assert.True(vm.InView);
        Assert.Equal(5, vm.Rows.Count());
        Assert.Equal(host.Window.FindResource("AccentSubtleBrush"), toggle.Background);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed class Tab(object viewModel, string id = ShellLayoutViewModel.ChartReferenceTabId, int order = 52, bool fillsContent = false) : IActivityTab
    {
        public string Id => id;
        public int Order => order;
        public bool FillsContent => fillsContent;
        public bool ShowsInActivityBar => !fillsContent;
        public string Title => "Chart reference";
        public string Name => Title;
        public string Tooltip => "Tip";
        public object ViewModel { get; } = viewModel;
        public Control CreateView() => new ContentControl();
        public bool PersistAsLastSelected => false;
        public TabDock Dock => TabDock.Left;
        public bool AutoOpenOnContentSignal => true;
        public Control CreateIcon() => new Border { Width = 22, Height = 22 };
        public bool IsVisible => true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private ShellLayoutViewModel Layout(ChartReferenceViewModel vm, out IActivityTab tab)
    {
        tab = new Tab(vm);
        return Layout([tab]);
    }

    private ShellLayoutViewModel Layout(IReadOnlyList<IActivityTab> tabs)
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsPath };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        return new ShellLayoutViewModel(settings, tabs, timeline, action => action());
    }
}
