using Avalonia.Controls;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Legends slice 9 (#1411): the Library coverage section (D8), catalogue keys
/// of the symbols drawn in view (D7), and the right dock folding in a narrow
/// window (D9).
/// </summary>
public sealed class LegendSlice9Tests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"legend-slice9-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
            File.Delete(_settingsPath);
    }

    // ── Library coverage section (D8) ──────────────────────────────────

    [Fact]
    public void The_Library_section_lists_the_outline_styles_drawn_in_the_Library_order()
    {
        var section = LibraryCoverageLegend.Build(
            [LibraryPrimaryAvailability.Listed, LibraryPrimaryAvailability.Local, LibraryPrimaryAvailability.Online],
            outlined: 7,
            selectedAccent: (0x20, 0x60, 0xd0));

        Assert.NotNull(section);
        Assert.Equal(LibraryCoverageLegend.SectionId, section.Id);
        Assert.Equal("7 outlined", section.Source);
        Assert.True(section.IsInView);
        Assert.Equal(["Local", "Online", "Listed", "Selected"], section.Rows.Select(r => r.Label));
        Assert.All(section.Rows, r => Assert.Equal(LegendRowKind.Line, LegendRows.KindOf(r)));

        // Same colours and dashes as the map's outlines and the Library row swatches.
        var online = (LineLegendEntry)section.Rows.ElementAt(1);
        var color = LibraryOutlineStyles.Online.Color;
        Assert.Equal(new RgbaColor(color.R, color.G, color.B).ToHex(), online.Line.Color);
        Assert.Single(online.Line.Dashes);
        Assert.Equal((5 + 3) * 0.32, online.Line.IntervalLength, 3);

        var selected = (LineLegendEntry)section.Rows.Last();
        Assert.Empty(selected.Line.Dashes);
        Assert.Equal(LibraryOutlineStyles.SelectedWidth * 0.32, selected.Line.Width, 3);
    }

    [Fact]
    public void A_dotted_outline_becomes_dots_the_legend_can_draw()
    {
        var sample = LibraryCoverageLegend.Sample(LibraryOutlineStyles.Listed);

        var dash = Assert.Single(sample.Dashes);
        Assert.True(dash.Length > 0); // a zero-length dash draws nothing with butt caps
        Assert.Equal(3 * 0.32, sample.IntervalLength, 3); // same spacing as the map's dots
    }

    [Fact]
    public void Without_outlines_on_the_map_there_is_no_Library_section()
    {
        Assert.Null(LibraryCoverageLegend.Build([], outlined: 0, selectedAccent: null));
    }

    [Fact]
    public void The_Library_section_goes_on_top_of_the_product_sections()
    {
        var f = new LegendServiceTests.Fixture();
        f.Loader.SetEntries(LegendServiceTests.VectorEntry(
            "storm.json", "TC", "weather.tc.track", "Track", S98DisplayPlane.MarinerOverlay,
            [new SwatchLegendEntry("#FF0000", "Hurricane")]));
        using var service = f.Create();
        var source = new LibrarySource();
        service.AddSource(source);

        source.Set(LibraryCoverageLegend.Build([LibraryPrimaryAvailability.Local], 1, null));

        Assert.Equal([LibraryCoverageLegend.SectionId, "TC"], service.Sections.Select(s => s.Id));
        Assert.Equal("1 outlined", service.Sections[0].Source);

        source.Set(null);
        Assert.Equal(["TC"], service.Sections.Select(s => s.Id));
    }

    private sealed class LibrarySource : ILegendSectionSource
    {
        public IReadOnlyList<LegendSection> Sections { get; private set; } = [];

        public event EventHandler? Changed;

        public void Set(LegendSection? section)
        {
            Sections = section is null ? [] : [section];
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // ── Catalogue keys (D7) ────────────────────────────────────────────

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle class=\"sCHBLK\" cx=\"5\" cy=\"5\" r=\"4\"/></svg>";

    /// <summary>An S-101 cell's linework drawing the given symbols, one feature each, at the given positions.</summary>
    private static LayerStackEntry S101Cell(
        string datasetId,
        IEnumerable<(string Symbol, double Lat, double Lon)> points,
        double? scaleMinimum = null,
        IReadOnlySet<string>? suppressed = null)
    {
        var list = points.Select((p, i) => (Ref: $"{datasetId}:{i}", p.Symbol, p.Lat, p.Lon)).ToArray();
        var geometry = new PointGeometry(list.ToDictionary(p => p.Ref, p => new GeoPosition(p.Lat, p.Lon)));
        var subLayer = new VectorSubLayer
        {
            LayerKey = "s101.linework",
            LayerName = "Linework: " + datasetId,
            Instructions = [.. list.Select(p => (DrawingInstruction)new PointInstruction
            {
                FeatureReference = p.Ref,
                SymbolReference = p.Symbol,
                ScaleMinimum = scaleMinimum,
            })],
            Plane = S98DisplayPlane.BaseChartOver,
            SourceFeatureType = "s101.linework",
        };
        var result = new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day),
            GeometryProvider = geometry,
            Product = "S-101",
            Spec = new SpecRef("S-101", default),
            SourceDatasetId = datasetId,
            Info = "test",
            SymbolProvider = _ => Svg,
            SymbolTitleProvider = name => name == "BOYCAN01" ? "can buoy, paper-chart" : null,
        };
        return new LayerStackEntry(
            new MemoryLayer(datasetId),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 0, datasetId, subLayer.SourceFeatureType))
        {
            SuppressedFeatures = suppressed,
        };
    }

    private sealed class PointGeometry(Dictionary<string, GeoPosition> points) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) =>
            points.TryGetValue(featureReference, out var at)
                ? new FeatureGeometry { Type = GeometryType.Point, Coordinates = [at] }
                : null;
    }

    [Fact]
    public void An_S101_section_lists_the_symbols_drawn_in_view_most_drawn_first()
    {
        var f = new LegendServiceTests.Fixture();
        f.Add("101GB00TEST.000", "S-101", 50, -2, 51, -1);
        f.Viewport.Publish(50, -2, 51, -1);
        f.Loader.SetEntries(S101Cell("101GB00TEST.000",
        [
            ("LIGHTS11", 50.5, -1.5),
            ("BOYCAN01", 50.4, -1.4),
            ("BOYCAN01", 50.3, -1.3),
            ("WRECKS01", 52.0, -1.5), // outside the view
        ]));
        using var service = f.Create();
        service.Refresh();

        var section = Assert.Single(service.Sections);
        Assert.Equal("S-101", section.Id);
        Assert.Equal("Electronic navigational chart", section.Title);
        Assert.Equal("Symbols drawn in view", section.Caption);
        Assert.Equal(["Can buoy, paper-chart", "LIGHTS11"], section.Rows.Select(r => r.Label));
        Assert.All(section.Rows, r => Assert.Equal(LegendRowKind.Symbol, LegendRows.KindOf(r)));
        Assert.DoesNotContain("sCHBLK", ((SymbolLegendEntry)section.Rows.First()).Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalogue_key_lists_twelve_and_counts_the_rest()
    {
        var f = new LegendServiceTests.Fixture();
        f.Viewport.Publish(50, -2, 51, -1);
        f.Loader.SetEntries(S101Cell("101GB00TEST.000",
            Enumerable.Range(0, 15).Select(i => ($"SYMB{i:00}", 50.5, -1.5))));
        using var service = f.Create();
        service.Refresh();

        var section = Assert.Single(service.Sections);
        Assert.Equal(CatalogueSymbolKey.MaxRows, section.Rows.Count());
        Assert.Equal("12 of 15 symbols drawn in view", section.Caption);
    }

    [Fact]
    public void A_catalogue_key_skips_symbols_hidden_by_SCAMIN_or_an_S98_rule()
    {
        var f = new LegendServiceTests.Fixture();
        f.Viewport.Publish(50, -2, 51, -1); // about 1:35 000
        f.Loader.SetEntries(
            S101Cell("coarse.000", [("BOYCAN01", 50.5, -1.5)], scaleMinimum: 20_000),
            S101Cell("fine.000", [("LIGHTS11", 50.5, -1.5), ("WRECKS01", 50.5, -1.5)], suppressed: new HashSet<string> { "fine.000:1" }));
        using var service = f.Create();
        service.Refresh();

        var section = Assert.Single(service.Sections);
        Assert.Equal(["LIGHTS11"], section.Rows.Select(r => r.Label));
        Assert.Equal(["fine.000", "coarse.000"], section.DatasetIds);
    }

    [Fact]
    public void Catalogue_keys_are_counted_only_while_the_tab_shows_or_MCP_asks()
    {
        var f = new LegendServiceTests.Fixture();
        f.Viewport.Publish(50, -2, 51, -1);
        f.Loader.SetEntries(S101Cell("101GB00TEST.000", [("LIGHTS11", 50.5, -1.5)]));
        using var service = f.Create();

        Assert.Empty(service.Sections); // built when the service starts: tab not shown

        service.IsTabShown = true;
        Assert.Equal(["S-101"], service.Sections.Select(s => s.Id));
    }

    [Fact]
    public void A_catalogue_without_symbols_in_view_has_no_section()
    {
        var f = new LegendServiceTests.Fixture();
        f.Viewport.Publish(10, 10, 11, 11);
        f.Loader.SetEntries(S101Cell("101GB00TEST.000", [("LIGHTS11", 50.5, -1.5)]));
        using var service = f.Create();
        service.Refresh();

        Assert.Empty(service.Sections);
    }

    [Fact]
    public void A_symbol_is_labelled_with_the_catalogue_description()
    {
        Assert.Equal("Can buoy, paper-chart", CatalogueSymbolKey.Label("BOYCAN01", " can buoy, paper-chart"));
        Assert.Equal("BOYCAN01", CatalogueSymbolKey.Label("BOYCAN01", null));
        Assert.Equal("can buoy", new Description { Name = "BOYCAN01", DescriptionText = "can buoy" }.Summary);
        Assert.Equal("BOYCAN01", new Description { Name = "BOYCAN01" }.Summary);
        Assert.Null(new Description { Name = "" }.Summary);
    }

    // ── Narrow windows (D9) ────────────────────────────────────────────

    private sealed class Tab : IActivityTab
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

    private ShellLayoutViewModel Layout(double mapWidth)
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsPath };
        var pick = new Tab { Id = ShellLayoutViewModel.PickReportTabId, Order = 10, Title = "Object information" };
        var legend = new Tab { Id = ShellLayoutViewModel.LegendTabId, Order = 20, Title = "Legend" };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        return new ShellLayoutViewModel(settings, [legend, pick], timeline, action => action()) { MapWidth = mapWidth };
    }

    [Fact]
    public void In_a_wide_window_the_dock_opens_beside_the_map()
    {
        var layout = Layout(mapWidth: 1200);

        layout.ToggleLegendCommand.Execute(null);

        Assert.True(layout.IsRightDockShown);
        Assert.False(layout.IsRightDockFolded);
        Assert.True(layout.IsLegendTabShown);
    }

    [Fact]
    public void Where_the_map_would_drop_under_480_px_the_dock_opens_folded_to_its_LEGEND_edge_tab()
    {
        // 800 − 360 − 4 = 436 px of map: too narrow.
        var layout = Layout(mapWidth: 800);

        layout.ToggleLegendCommand.Execute(null);

        Assert.True(layout.IsRightDockOpen);
        Assert.True(layout.IsRightDockFolded);
        Assert.True(layout.IsRightDockNarrowFolded);
        Assert.True(layout.IsRightDockEdgeTabVisible);
        Assert.Equal("Legend", layout.RightDockTitle); // the edge tab's label, upper-cased in the view
        Assert.False(layout.IsRightDockShown); // the map keeps its width
        Assert.False(layout.IsRightDockPanelVisible);
        Assert.False(layout.IsLegendTabShown);
    }

    [Fact]
    public void The_edge_tab_shows_the_dock_over_the_map_and_Escape_or_a_map_click_folds_it()
    {
        var layout = Layout(mapWidth: 800);
        layout.ToggleLegendCommand.Execute(null);

        layout.UnfoldRightDockCommand.Execute(null);

        Assert.True(layout.IsRightDockOverlaid);
        Assert.True(layout.IsRightDockPanelVisible);
        Assert.False(layout.IsRightDockShown); // not in its column: the map isn't resized
        Assert.True(layout.IsRightDockFolded);
        Assert.True(layout.IsLegendTabShown);

        Assert.True(layout.FoldOverlaidRightDock());
        Assert.False(layout.IsRightDockOverlaid);
        Assert.True(layout.IsRightDockEdgeTabVisible);
        Assert.False(layout.FoldOverlaidRightDock()); // nothing over the map now

        // The edge tab toggles: shown, then folded again.
        layout.UnfoldRightDockCommand.Execute(null);
        layout.UnfoldRightDockCommand.Execute(null);
        Assert.False(layout.IsRightDockOverlaid);
    }

    [Fact]
    public void View_Legend_on_a_narrow_LEGEND_fold_shows_it_and_then_puts_it_away()
    {
        var layout = Layout(mapWidth: 800);
        layout.ToggleLegendCommand.Execute(null); // opens folded

        layout.ToggleLegendCommand.Execute(null);
        Assert.True(layout.IsLegendTabShown);
        Assert.True(layout.IsRightDockOverlaid);

        layout.ToggleLegendCommand.Execute(null);
        Assert.False(layout.IsRightDockOpen);
        Assert.False(layout.IsRightDockFolded); // closing forgets the narrow fold

        // Wider now: the next open docks it.
        layout.MapWidth = 1200;
        layout.ToggleLegendCommand.Execute(null);
        Assert.True(layout.IsRightDockShown);
    }

    [Fact]
    public void Turning_Check_passage_off_unfolds_a_Check_passage_fold_but_its_tab_does_not_overlay()
    {
        var layout = Layout(mapWidth: 1200);
        layout.ToggleLegendCommand.Execute(null);
        layout.IsRightDockFolded = true; // Check passage

        Assert.False(layout.IsRightDockNarrowFolded);
        layout.UnfoldRightDockCommand.Execute(null);

        Assert.False(layout.IsRightDockFolded);
        Assert.False(layout.IsRightDockOverlaid);
        Assert.True(layout.IsRightDockShown);
    }
}
