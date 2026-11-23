using Avalonia.Controls;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The chart reference's other entry points (#1681, symbol dictionary design
/// note §4.6, R9b): the map's context menu <b>Look up in reference</b>, the
/// feature type terms and <b>Open in reference</b> of Feature search hits,
/// and the terms of a product's own Legend rows. A type the reference
/// doesn't hold gets no item and stays plain.
/// </summary>
public sealed class ReferenceEntryPointTests
{
    private static readonly SpecRef S101 = new("S-101", default);

    private static readonly SpecRef S124 = new("S-124", default);

    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle class=\"sCHBLK\" cx=\"5\" cy=\"5\" r=\"4\"/></svg>";

    private static ReferenceTerms Terms(ChartReferenceServiceTests.Fixture f, ReferenceNavigator nav) =>
        ReferenceTerms.For(f.Service, new ChartReferenceGlyphCache(PaletteType.Day), nav, examiner: null, urls: null);

    private static PickHit Hit(string spec, string type, string? name = null) => new()
    {
        ProductSpec = spec,
        FeatureType = type,
        FeatureTypeName = name,
        FeatureRef = $"{type}:1",
    };

    /// <summary>Pumps the dispatcher until <paramref name="done"/> holds, for up to five seconds.</summary>
    private static void Until(ViewHost host, Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < deadline)
        {
            host.Settle();
            Thread.Sleep(10);
        }

        host.Settle();
        Assert.True(done(), "timed out");
    }

    // ---- Feature keys ----

    [Fact]
    public void A_feature_key_is_in_the_catalogue_its_product_is_drawn_with()
    {
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "LateralBeacon"), ReferenceTerms.FeatureKeyOf("S-101", "LateralBeacon"));
        Assert.Equal(new ReferenceKey(S124, ReferenceKind.FeatureType, "NavwarnPart"), ReferenceTerms.FeatureKeyOf("S124", "NavwarnPart"));
        Assert.Null(ReferenceTerms.FeatureKeyOf("S-101", " "));
        Assert.Null(ReferenceTerms.FeatureKeyOf(null, "LateralBeacon"));
        // An S-57 cell is drawn with S-101's catalogue, and its features carry S-101 codes.
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "Wreck"), ReferenceTerms.FeatureKeyOf("S-57", "Wreck"));
    }

    // ---- The map's context menu ----

    [AvaloniaFact]
    public void Look_up_in_reference_opens_the_feature_type_under_the_pointer()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var builder = new MapReferenceMenu(Terms(f, nav));
        var target = new Border { Width = 200, Height = 200, Background = Avalonia.Media.Brushes.Transparent };
        using var host = ViewHost.Show(target, 400, 400);

        var menuTask = builder.BuildAsync([Hit("S-101", "LateralBeacon", "Lateral beacon")]);
        Until(host, () => menuTask.IsCompleted);
        var menu = menuTask.Result;
        Assert.NotNull(menu);
        Assert.Equal(MapReferenceMenu.MenuAutomationId, Avalonia.Automation.AutomationProperties.GetAutomationId(menu));
        menu.Open(target);
        host.Settle();

        // One feature type: one item, no submenu.
        var item = host.Find<MenuItem>(MapReferenceMenu.AutomationId);
        Assert.Equal("Look up in reference", Avalonia.Automation.AutomationProperties.GetName(item));
        Assert.Empty(item.Items);
        Assert.Equal("Open Lateral beacon in the Reference tab", ToolTip.GetTip(item));
        host.Click(item);
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "LateralBeacon"), nav.Current);
    }

    [AvaloniaFact]
    public void Several_features_under_the_pointer_give_a_submenu_in_the_picks_order_once_each()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var builder = new MapReferenceMenu(Terms(f, nav));
        var target = new Border { Width = 200, Height = 200, Background = Avalonia.Media.Brushes.Transparent };
        using var host = ViewHost.Show(target, 400, 400);

        IReadOnlyList<PickHit> hits =
        [
            Hit("S-101", "LateralBeacon", "Lateral beacon"),
            Hit("S-101", "NoSuchFeatureType"),
            Hit("S-101", "LateralBeacon", "Lateral beacon"),
            Hit("S-101", "DepthArea"),
        ];
        var itemsTask = builder.ItemsAsync(hits);
        Until(host, () => itemsTask.IsCompleted);
        var items = itemsTask.Result;
        Assert.Equal(["LateralBeacon", "DepthArea"], items.Select(i => i.Key.Code));
        // A hit without the pick's name takes the catalogue's.
        Assert.Equal("Lateral beacon", items[0].Name);
        Assert.Equal("Depth Area", items[1].Name, ignoreCase: true);

        var menu = builder.Build(items)!;
        menu.Open(target);
        host.Settle();
        var lookUp = host.Find<MenuItem>(MapReferenceMenu.AutomationId);
        Assert.Equal(2, lookUp.Items.Count);
        lookUp.IsSubMenuOpen = true;
        host.Settle();
        var features = host.FindAll<MenuItem>(MapReferenceMenu.ItemAutomationId);
        Assert.Equal(["Lateral beacon", items[1].Name], features.Select(Avalonia.Automation.AutomationProperties.GetName));
        host.Click(features[1]);
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "DepthArea"), nav.Current);
    }

    [AvaloniaFact]
    public void An_unknown_feature_type_gives_no_menu_item()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var builder = new MapReferenceMenu(Terms(f, nav));
        using var host = ViewHost.Show(new Border(), 100, 100);

        // Not in the catalogue, a product without a catalogue, and nothing at all.
        var unknown = builder.BuildAsync([Hit("S-101", "NoSuchFeatureType"), Hit("S-999", "Thing")]);
        var none = builder.BuildAsync([]);
        Until(host, () => unknown.IsCompleted && none.IsCompleted);
        Assert.Null(unknown.Result);
        Assert.Null(none.Result);
    }

    [AvaloniaFact]
    public void An_S57_feature_opens_its_S101_entry_and_an_acronym_left_untranslated_stays_plain()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var builder = new MapReferenceMenu(Terms(f, nav));
        var target = new Border { Width = 200, Height = 200, Background = Avalonia.Media.Brushes.Transparent };
        using var host = ViewHost.Show(target, 400, 400);

        // An S-57 cell's features are translated to S-101 codes.
        var itemsTask = builder.ItemsAsync([Hit("S-57", "LateralBeacon", "Lateral beacon"), Hit("S-57", "BCNLAT")]);
        Until(host, () => itemsTask.IsCompleted);
        var item = Assert.Single(itemsTask.Result);
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "LateralBeacon"), item.Key);

        builder.Build(itemsTask.Result)!.Open(target);
        host.Settle();
        host.Click(host.Find<MenuItem>(MapReferenceMenu.AutomationId));
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "LateralBeacon"), nav.Current);
    }

    // ---- Feature search ----

    private sealed class Processor : IDatasetProcessor
    {
        public SpecRef Spec { get; } = new("S-101", default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }

    private sealed class SearchService(IReadOnlyList<FeatureSearchHit> hits) : IFeatureSearchService
    {
        public (IReadOnlyList<FeatureSearchHit> Hits, int TotalMatched) Search(string? query, int limit) => (hits, hits.Count);
    }

    private static FeatureSearchHit SearchHit(string spec, string type, string name, int ordinal) => new()
    {
        Processor = new Processor(),
        DatasetFileName = "101GB00TEST.000",
        ProductSpec = spec,
        FeatureRef = $"{type}:{ordinal}",
        Ordinal = ordinal,
        FeatureType = type,
        FeatureTypeName = name,
    };

    [AvaloniaFact]
    public void A_Feature_search_hits_type_is_a_term_and_its_menu_opens_the_entry()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var nav = new ReferenceNavigator();
        var search = new SearchService(
        [
            SearchHit("S-101", "LateralBeacon", "Lateral beacon", 0),
            SearchHit("S-101", "NoSuchFeatureType", "No such feature type", 1),
            SearchHit("S-57", "BCNLAT", "Beacon, lateral", 2),
            SearchHit("S-57", "Wreck", "Wreck", 3),
        ]);
        var vm = new FeatureSearchViewModel(search, new StubPickService()) { Terms = Terms(f, nav) };
        using var host = ViewHost.Show(new FeatureSearchView { DataContext = vm }, 400, 600);
        vm.Query = "beacon";
        vm.SearchNow();
        Assert.Equal(4, vm.Results.Count);
        Until(host, () => vm.TermsResolved.IsCompleted);

        var (beacon, unknown, s57, s57Wreck) = (vm.Results[0], vm.Results[1], vm.Results[2], vm.Results[3]);
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "LateralBeacon"), beacon.TermKey);
        Assert.True(beacon.IsInReference);
        Assert.False(unknown.IsInReference);
        // An S-57 hit's S-101 code is in the reference; an acronym left untranslated isn't.
        Assert.False(s57.IsInReference);
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "Wreck"), s57Wreck.TermKey);
        Assert.True(s57Wreck.IsInReference);

        // The known type is a term; the others are plain text.
        var terms = host.FindAll<ReferenceTerm>("Reference.Term");
        Assert.Equal(4, terms.Count);
        Until(host, () => terms[0].IsTerm && terms[3].IsTerm);
        Assert.False(terms[1].IsTerm);
        Assert.False(terms[2].IsTerm);
        Assert.Equal("Lateral beacon", terms[0].Text);

        // A click on the term opens its entry.
        host.Click(terms[0]);
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "LateralBeacon"), nav.Current);

        // Open in reference is offered only for the known type.
        Assert.True(vm.OpenInReferenceCommand.CanExecute(beacon));
        Assert.False(vm.OpenInReferenceCommand.CanExecute(unknown));
        Assert.False(vm.OpenInReferenceCommand.CanExecute(s57));

        var rows = host.All<ListBoxItem>().ToList();
        var menu = host.RightClick(rows[0].GetVisualDescendants().OfType<TextBlock>().Last());
        var open = host.Find<MenuItem>("Search.Result.OpenInReference");
        Assert.Equal("Open in reference", Avalonia.Automation.AutomationProperties.GetName(open));
        Assert.Equal("Open Lateral beacon in the Reference tab", ToolTip.GetTip(open));
        var shown = 0;
        nav.ShowRequested += (_, _) => shown++;
        host.Click(open);
        Assert.Equal(1, shown);
        Assert.Equal(new ReferenceKey(S101, ReferenceKind.FeatureType, "LateralBeacon"), nav.Current);
        Assert.False(menu.IsOpen);

        // An unknown type's row opens no menu.
        Assert.Throws<InvalidOperationException>(() => host.RightClick(rows[1].GetVisualDescendants().OfType<TextBlock>().Last()));
    }

    // ---- A product's own Legend rows (layer stack legends) ----

    [Fact]
    public void The_catalogue_legend_builder_names_what_each_row_draws()
    {
        var palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day);
        var builder = new CatalogueLegendBuilder(palette, symbols: _ => Svg);
        Assert.Equal("BOYCAN01", builder.Symbol("BOYCAN01", "Can buoy").SymbolName);
        Assert.Null(builder.Swatch("#FF0000", "Red").FeatureType);
    }

    [Fact]
    public void A_legend_row_names_its_feature_type_first_then_its_symbol_line_or_fill()
    {
        var sample = new LegendLineSample { Color = "#000000", Width = 0.3 };
        var fill = new LegendFillPattern { Color = "#FFFFFF" };
        Assert.Equal(new ReferenceKey(S124, ReferenceKind.FeatureType, "NavwarnPart"),
            LegendRowViewModel.TermKeyOf(new SymbolLegendEntry(Svg, "Warning") { SymbolName = "X", FeatureType = "NavwarnPart" }, "S-124"));
        Assert.Equal(new ReferenceKey(S124, ReferenceKind.Symbol, "X"),
            LegendRowViewModel.TermKeyOf(new SymbolLegendEntry(Svg, "Warning") { SymbolName = "X" }, "S-124"));
        Assert.Equal(new ReferenceKey(S124, ReferenceKind.LineStyle, "L"),
            LegendRowViewModel.TermKeyOf(new LineLegendEntry(sample, "Line") { LineStyleName = "L" }, "S-124"));
        Assert.Equal(new ReferenceKey(S124, ReferenceKind.AreaFill, "F"),
            LegendRowViewModel.TermKeyOf(new FillLegendEntry(fill, "Fill") { AreaFillName = "F" }, "S-124"));
        Assert.Null(LegendRowViewModel.TermKeyOf(new SwatchLegendEntry("#FF0000", "Red"), "S-124"));
        Assert.Null(LegendRowViewModel.TermKeyOf(new SymbolLegendEntry(Svg, "Warning") { SymbolName = "X" }, null));
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class NoGeometry : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) => null;
    }

    [AvaloniaFact]
    public void A_products_own_legend_row_is_a_term_and_an_unknown_name_stays_plain()
    {
        var legend = new LegendServiceTests.Fixture();
        var dataset = legend.Add("S124_warning.gml", "S-124", 50, -2, 51, -1);
        var subLayer = new VectorSubLayer
        {
            LayerKey = "s124.warnings",
            LayerName = "Navigational warnings",
            Instructions = [],
            Plane = S98DisplayPlane.BaseChartOver,
            Legend =
            [
                new SwatchLegendEntry("#00000000", "Warning area") { FeatureType = "NavwarnPart" },
                new SymbolLegendEntry(Svg, "Warning position") { SymbolName = "NavigationalWarningFeaturePartPoint" },
                new SymbolLegendEntry(Svg, "Mystery mark") { SymbolName = "NoSuchSymbol" },
                new SwatchLegendEntry("#FF0000", "Red"),
            ],
        };
        var result = new VectorPortrayalResult
        {
            SubLayers = [subLayer],
            Palette = FrontPipFixtures.Palette.GetColorPalette(PaletteType.Day),
            GeometryProvider = new NoGeometry(),
            Product = "S-124",
            Spec = S124,
            SourceDatasetId = dataset.Id.Value,
            Info = "test",
        };
        legend.Viewport.Publish(50, -2, 51, -1);
        legend.Loader.SetEntries(new LayerStackEntry(
            new MemoryLayer(dataset.Id.Value),
            new SubLayerStackItem(new VectorStackPayload(result, subLayer), subLayer.Plane, 0, dataset.Id.Value)));
        using var service = legend.Create();
        service.IsTabShown = true;
        service.Refresh();
        var section = Assert.Single(service.Sections);
        Assert.Equal("S-124", section.CatalogueSpec);

        using var find = new LegendFindService(service, legend.Viewport, legend.Clock, action => action());
        using var catalogues = new ChartReferenceServiceTests.Fixture("S-124");
        var nav = new ReferenceNavigator();
        using var tab = new LegendViewModel(service, new ClockViewTime(new FakeTimeProvider()), new Utc(), find)
        {
            Terms = Terms(catalogues, nav),
        };
        using var host = ViewHost.Show(new LegendView { DataContext = tab }, 360, 600);

        var rows = tab.Sections.Single().Rows;
        Assert.Equal(
            [
                new ReferenceKey(S124, ReferenceKind.FeatureType, "NavwarnPart"),
                new ReferenceKey(S124, ReferenceKind.Symbol, "NavigationalWarningFeaturePartPoint"),
                new ReferenceKey(S124, ReferenceKind.Symbol, "NoSuchSymbol"),
                (ReferenceKey?)null,
            ],
            rows.Select(r => r.TermKey));

        var terms = host.FindAll<ReferenceTerm>("Reference.Term");
        Assert.Equal(3, terms.Count);
        Until(host, () => terms[0].IsTerm && terms[1].IsTerm);
        // A name the index doesn't hold is plain, and its row keeps keyboard focus.
        Assert.False(terms[2].IsTerm);
        Assert.False(terms[2].Focusable);
        var plainRow = terms[2].GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("legendRow"));
        Assert.True(plainRow.Focusable);
        var termRow = terms[1].GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("legendRow"));
        Assert.False(termRow.Focusable);

        host.Click(terms[0]);
        Assert.Equal(new ReferenceKey(S124, ReferenceKind.FeatureType, "NavwarnPart"), nav.Current);
        host.Click(terms[1]);
        Assert.Equal(new ReferenceKey(S124, ReferenceKind.Symbol, "NavigationalWarningFeaturePartPoint"), nav.Current);
    }
}
