using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Chart reference activity (#1637, symbol dictionary design note §4.5,
/// R4), over the real <see cref="ChartReferenceService"/> and the bundled
/// catalogues: the product combo, search, Features / Symbols, the list's
/// sections, expansion and links, and the view.
/// </summary>
public sealed class ChartReferenceViewModelTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task The_first_product_loads_and_feature_types_are_grouped_by_Chart_1_section()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101", "S-201");
        f.Loaded.Add("S-201");
        using var vm = await OpenAsync(f);

        Assert.Equal(["S-201", "S-101"], vm.Products.Select(p => p.Spec.Name));
        Assert.True(vm.Products[0].IsLoaded);
        Assert.Equal("S-201", vm.SelectedProduct?.Spec.Name);

        vm.SelectedProduct = vm.Products[1];
        await vm.Loading;
        var index = Assert.IsType<ChartReferenceIndex>(vm.Index);

        // A header first, then rows; every non-abstract feature type once.
        Assert.IsType<ChartReferenceHeaderViewModel>(vm.Items[0]);
        Assert.Equal(index.FeatureTypes.Count(t => !t.IsAbstract), vm.Rows.Count());
        var headers = vm.Items.OfType<ChartReferenceHeaderViewModel>().Select(h => h.Title).ToList();
        Assert.Equal(headers.Count, headers.Distinct().Count());
        Assert.Contains(headers, h => h.StartsWith("P, Q, R, S – ", StringComparison.Ordinal));
        Assert.Equal("No Chart 1 section", headers[^1]);
        Assert.Equal(headers.Where(h => h != "No Chart 1 section").OrderBy(h => h, StringComparer.Ordinal), headers.SkipLast(1));
        Assert.Equal(vm.Rows.Count(), vm.Items.OfType<ChartReferenceHeaderViewModel>().Sum(h => h.Count));
        Assert.False(vm.ShowNoCatalogue);
        Assert.False(vm.ShowNoMatches);
        Assert.Equal($"{vm.Rows.Count()} feature types", vm.Summary);

        // A row: name, S-57 acronym and the most visible display category (D6).
        var beacon = Row(vm, "LateralBeacon");
        Assert.Equal(("Lateral Beacon", "BCNLAT", "Standard"), (beacon.Name, beacon.Code, beacon.CategoryTag));
        Assert.Equal("Base", Row(vm, "DepthArea").CategoryTag);
        Assert.Equal("Other", Row(vm, "Sounding").CategoryTag);
    }

    [Fact]
    public async Task The_index_is_built_when_the_panel_is_first_shown_not_at_startup()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = new ChartReferenceViewModel(f.Service, new ChartReferenceGlyphCache(PaletteType.Day));

        Assert.Equal("S-101", vm.SelectedProduct?.Spec.Name);
        Assert.Null(vm.Index);
        Assert.Same(Task.CompletedTask, vm.Loading);

        vm.Activate();
        await vm.Loading;
        Assert.NotNull(vm.Index);
    }

    [Fact]
    public async Task Search_ranks_an_S57_acronym_first_and_suggests_the_other_mode_when_nothing_matches()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await OpenAsync(f);

        vm.Query = "BCNLAT";

        // A search lists hits in rank order, without sections.
        Assert.All(vm.Items, i => Assert.IsType<ChartReferenceRowViewModel>(i));
        Assert.Equal("LateralBeacon", vm.Rows.First().Key.Code);
        Assert.Equal(vm.Items.Count == 1 ? "1 match" : $"{vm.Items.Count} matches", vm.Summary);

        vm.Query = "no such thing at all";
        Assert.Empty(vm.Items);
        Assert.True(vm.ShowNoMatches);
        Assert.Contains("S-57 acronym", vm.NoMatchesDescription, StringComparison.Ordinal);
        vm.Mode = ChartReferenceMode.Symbols;
        Assert.True(vm.ShowNoMatches);
        Assert.Contains("Features", vm.NoMatchesDescription, StringComparison.Ordinal);

        vm.ClearQueryCommand.Execute(null);
        Assert.Equal(string.Empty, vm.Query);
        Assert.False(vm.ShowNoMatches);
        Assert.NotEmpty(vm.Items);
    }

    [Fact]
    public async Task Symbols_mode_lists_items_with_families_collapsed_and_unreached_ones_last()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await OpenAsync(f);
        var index = Assert.IsType<ChartReferenceIndex>(vm.Index);

        vm.ModeIndex = 1;

        Assert.Equal(ChartReferenceMode.Symbols, vm.Mode);
        var headers = vm.Items.OfType<ChartReferenceHeaderViewModel>().ToList();
        Assert.Equal("Not used by a rule", headers[^1].Title);
        var last = vm.Items.ToList().IndexOf(headers[^1]);
        var after = vm.Items.Skip(last + 1).Cast<ChartReferenceRowViewModel>().ToList();
        Assert.All(after, r => Assert.True(Assert.IsType<PortrayalItemEntry>(r.Entry).IsUnreached));
        Assert.Equal(index.Items.Count(i => i.IsUnreached && i.Family is null), after.Count);

        // SOUNDG is one row, its members aren't rows of their own (D3).
        var soundg = Row(vm, "SOUNDG");
        Assert.Equal("Family", soundg.CategoryTag);
        Assert.DoesNotContain(vm.Rows, r => Assert.IsType<PortrayalItemEntry>(r.Entry).Family is not null);
        Assert.Equal("Line", Row(vm, "CBLSUB06").CategoryTag);
        Assert.Equal("Fill", Row(vm, "DRGARE01").CategoryTag);
        Assert.Equal(index.Items.Count(i => i.Family is null), vm.Rows.Count());
    }

    [Fact]
    public async Task Selecting_a_row_opens_its_entry_and_Open_only_selects()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await OpenAsync(f);
        var opened = new List<ReferenceKey>();
        vm.OpenEntry = opened.Add;

        // A header can't be selected.
        vm.SelectedItem = vm.Items[0];
        Assert.Null(vm.SelectedItem);
        Assert.Empty(opened);

        // Selecting a row in the list opens its entry in the Reference tab (R7).
        var beacon = Row(vm, "LateralBeacon");
        vm.SelectedItem = beacon;
        Assert.Equal([beacon.Key], opened);

        // Open (the page's tiles) selects the row without opening it.
        var symbol = new ReferenceKey(vm.Index!.Spec, ReferenceKind.Symbol, "BCNLAT21");
        vm.Open(symbol);
        Assert.Equal(ChartReferenceMode.Symbols, vm.Mode);
        Assert.Equal(symbol, vm.SelectedRow?.Key);
        Assert.Equal([beacon.Key], opened);

        // A rebuild that keeps the selected row doesn't open it again.
        vm.Query = "BCNLAT";
        Assert.Equal(symbol, vm.SelectedRow?.Key);
        Assert.Equal([beacon.Key], opened);
    }

    [Fact]
    public async Task A_link_to_a_family_member_searches_for_it()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await OpenAsync(f);
        var member = Assert.IsType<PortrayalItemEntry>(vm.Index!.Items.First(i => i.Family == "SOUNDG"));

        vm.Open(member.Key);

        Assert.Equal(member.Code, vm.Query);
        Assert.Equal(member.Key, vm.SelectedRow?.Key);
    }

    [Fact]
    public async Task Without_a_catalogue_the_list_says_so()
    {
        using var features = new EncDotNet.S100.Features.FeatureCatalogueManager((string _) => null);
        using var portrayals = new EncDotNet.S100.Portrayals.PortrayalCatalogueManager();
        using var service = new ChartReferenceService(features, portrayals, () => [], bundledSpecs: []);
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);

        using var vm = new ChartReferenceViewModel(service, cache);
        Assert.False(vm.ShowNoCatalogue); // Not until the panel is shown.
        vm.Activate();
        await vm.Loading;

        Assert.Empty(vm.Products);
        Assert.Null(vm.SelectedProduct);
        Assert.True(vm.ShowNoCatalogue);
        Assert.False(vm.ShowNoMatches);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task A_catalogue_change_reloads_the_index_and_opening_a_dataset_reorders_the_products()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101", "S-201");
        using var vm = await OpenAsync(f);
        Assert.Equal("S-101", vm.SelectedProduct?.Spec.Name);
        var before = vm.Index;

        f.Portrayals.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        await vm.Loading;
        Assert.NotNull(vm.Index);
        Assert.NotSame(before, vm.Index);

        // The selection stays on S-101 while S-201 moves first.
        f.Loaded.Add("S-201");
        vm.RefreshProducts();
        Assert.Equal(["S-201", "S-101"], vm.Products.Select(p => p.Spec.Name));
        Assert.Equal("S-101", vm.SelectedProduct?.Spec.Name);
        Assert.Same(vm.Products[1], vm.SelectedProduct);
    }

    [Fact]
    public async Task Simplified_symbols_changes_the_lead_glyph()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }) { SimplifiedSymbols = false };
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        using var vm = new ChartReferenceViewModel(f.Service, cache, settings);
        vm.Activate();
        await vm.Loading;
        Assert.Equal("BCNTOW30", Row(vm, "LateralBeacon").Lead.ItemId);

        settings.SimplifiedSymbols = true;

        Assert.Equal("BCNLAT21", Row(vm, "LateralBeacon").Lead.ItemId);
        Assert.False(Row(vm, "DataCoverage").HasGlyph);
        Assert.True(Row(vm, "DataCoverage").HasNoGlyph);
    }

    [AvaloniaFact]
    public async Task Glyphs_are_drawn_off_the_UI_thread_cached_and_drawn_again_after_a_palette_change()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        using var vm = new ChartReferenceViewModel(f.Service, cache, post: a => Dispatcher.UIThread.Post(a));
        await LoadAsync(vm);

        foreach (var code in new[] { "LateralBeacon", "DepthArea", "CableSubmarine", "Coastline", "DredgedArea" })
        {
            var row = Row(vm, code);
            Assert.Null(row.Glyph);
            await vm.GlyphsDrawn;
            Dispatcher.UIThread.RunJobs();
            var bitmap = Assert.IsAssignableFrom<Bitmap>(row.Glyph);
            Assert.Equal((80, 80), (bitmap.PixelSize.Width, bitmap.PixelSize.Height));
            Assert.True(cache.TryGet(row.Lead.Key, ChartReferenceViewModel.GlyphSize, ChartReferenceViewModel.GlyphSize, out var held));
            Assert.Same(held, row.Glyph);
        }

        cache.SetPalette(PaletteType.Night);
        Dispatcher.UIThread.RunJobs();
        var again = Row(vm, "LateralBeacon");
        Assert.Null(again.Glyph);
        await vm.GlyphsDrawn;
        Dispatcher.UIThread.RunJobs();
        Assert.IsAssignableFrom<Bitmap>(again.Glyph);
        Assert.Same(cache.Tile, again.Tile);
    }

    [AvaloniaFact]
    public async Task The_view_shows_the_controls_a_virtualised_list_and_opens_a_selected_row()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        using var vm = new ChartReferenceViewModel(f.Service, cache, post: a => Dispatcher.UIThread.Post(a));
        await LoadAsync(vm);
        using var host = ViewHost.Show(new ChartReferenceView { DataContext = vm }, 320, 800);

        var product = host.Find<ComboBox>("ChartReference.Product");
        Assert.Same(vm.SelectedProduct, product.SelectedItem);
        host.Find<TextBox>("ChartReference.Query");
        Assert.Equal(0, host.Find<TabControl>("ChartReference.Mode").SelectedIndex);
        var list = host.Find<ListBox>("ChartReference.List");

        // Virtualised: only the rows in view are realised, not all 180-odd.
        var rows = host.FindAll<ListBoxItem>("ChartReference.Row");
        Assert.NotEmpty(rows);
        Assert.True(rows.Count < vm.Rows.Count() / 2, $"{rows.Count} rows realised");
        Assert.NotEmpty(host.FindAll<ListBoxItem>("ChartReference.Section"));
        Assert.All(rows, r => Assert.Equal(((ChartReferenceRowViewModel)r.DataContext!).Name, Avalonia.Automation.AutomationProperties.GetName(r)));

        // The tiles fill in as their glyphs are drawn.
        await vm.GlyphsDrawn;
        host.Settle();
        var tiles = host.FindAll<Border>("ChartReference.Row.Tile");
        Assert.Contains(tiles, t => t.GetVisualDescendants().OfType<Image>().Any(i => i.IsVisible && i.Source is Bitmap));

        // Selecting a row in the list opens its entry in the Reference tab; it doesn't expand.
        var opened = new List<ReferenceKey>();
        vm.OpenEntry = opened.Add;
        var first = rows.First();
        list.SelectedItem = first.DataContext;
        host.Settle();
        Assert.Same(vm.SelectedItem, list.SelectedItem);
        Assert.Equal([((ChartReferenceRowViewModel)first.DataContext!).Key], opened);
        Assert.Empty(host.FindAll<Control>("ChartReference.Row.Details"));

        // Switching mode through the segments.
        host.Find<TabControl>("ChartReference.Mode").SelectedIndex = 1;
        host.Settle();
        Assert.Equal(ChartReferenceMode.Symbols, vm.Mode);
    }

    private static async Task<ChartReferenceViewModel> OpenAsync(ChartReferenceServiceTests.Fixture f)
    {
        var vm = new ChartReferenceViewModel(f.Service, new ChartReferenceGlyphCache(PaletteType.Day));
        vm.Activate();
        await vm.Loading;
        Assert.NotNull(vm.Index);
        return vm;
    }

    // In the app the index is applied on the UI thread.
    private static async Task LoadAsync(ChartReferenceViewModel vm)
    {
        vm.Activate();
        await vm.Loading;
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.Index);
    }

    private static ChartReferenceRowViewModel Row(ChartReferenceViewModel vm, string code) =>
        vm.Rows.Single(r => r.Key.Code == code);
}
