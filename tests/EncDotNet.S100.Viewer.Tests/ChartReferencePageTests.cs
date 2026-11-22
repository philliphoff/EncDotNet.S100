using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.ViewModels.ChartReference;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Chart reference page (#1639, symbol dictionary design note §4.5, R6):
/// Open as page in the full-content panel, sharing the activity's view model,
/// with preview switches that never change the chart, over the real
/// <see cref="ChartReferenceService"/> and the bundled catalogues.
/// </summary>
public sealed class ChartReferencePageTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task Opening_and_closing_the_page_keeps_the_product_and_selection()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101", "S-201");
        using var reference = new ChartReferenceViewModel(f.Service, new ChartReferenceGlyphCache(PaletteType.Day));
        using var page = new ChartReferencePageViewModel(reference, f.Service);
        var activityTab = new ActivityTab<ChartReferenceViewModel, ChartReferenceView>(
            ShellLayoutViewModel.ChartReferenceTabId, 52, "Chart reference", "Chart reference", () => new Border(), reference, true, null);
        var pageTab = new ActivityTab<ChartReferencePageViewModel, ChartReferencePageView>(
            ShellLayoutViewModel.ChartReferencePageTabId, 53, "Chart reference", "Open as page", () => new Border(), page, false, null,
            fillsContent: true, showsInActivityBar: false);
        var layout = Layout(activityTab, pageTab);
        reference.OpenPageCommand = layout.ShowChartReferencePageCommand;

        // Only code opens the page: it has no activity-bar button.
        Assert.Contains(activityTab, layout.LeftDockTopTabs);
        Assert.DoesNotContain(pageTab, layout.LeftDockTopTabs);
        Assert.DoesNotContain(pageTab, layout.LeftDockBottomTabs);

        // The activity, on S-101, with Lateral Beacon selected.
        layout.ShowChartReferenceCommand.Execute(null);
        Assert.True(layout.IsLeftDockShown);
        reference.Activate();
        reference.SelectedProduct = reference.Products.Single(p => p.Spec.Name == "S-101");
        await reference.Loading;
        var beacon = Key(reference, "LateralBeacon");
        reference.Open(beacon);

        // Open as page: across the content area, on the same product and entry.
        Assert.True(reference.CanOpenPage);
        reference.OpenPageCommand!.Execute(null);
        Assert.True(layout.IsContentPanelOpen);
        Assert.Same(pageTab, layout.ContentPanelTab);
        Assert.Equal("Back to Chart reference", layout.ContentPanelCloseTooltip);
        page.Show();
        Assert.Equal("S-101", page.Reference.SelectedProduct?.Spec.Name);
        Assert.Equal(beacon, page.SelectedTile?.Key);
        Assert.True(page.SelectedTile!.IsExpanded);
        Assert.Equal(reference.Rows.Count(), page.Tiles.Count);

        // A tile chosen on the page is selected in the activity too.
        var depth = page.Tiles.Single(t => t.Key.Code == "DepthArea");
        page.SelectCommand.Execute(depth);
        Assert.Same(depth, page.SelectedTile);
        Assert.Equal(depth.Key, reference.SelectedRow?.Key);
        Assert.True(reference.SelectedRow!.IsExpanded);
        Assert.False(page.Tiles.Single(t => t.Key == beacon).IsExpanded);

        // Closing returns to the activity, with that selection.
        layout.CloseDockCommand.Execute("Left");
        Assert.False(layout.IsContentPanelOpen);
        Assert.Same(activityTab, layout.SelectedLeftTab);
        Assert.True(layout.IsLeftDockShown);
        Assert.Equal("Close settings", layout.ContentPanelCloseTooltip);
        Assert.Equal(depth.Key, reference.SelectedRow?.Key);

        // A change in the activity carries over when the page opens again.
        reference.Mode = ChartReferenceMode.Symbols;
        var symbol = new ReferenceKey(reference.Index!.Spec, ReferenceKind.Symbol, "BCNLAT21");
        reference.Open(symbol);
        layout.ShowChartReferencePageCommand.Execute(null);
        Assert.True(layout.IsContentPanelOpen);
        Assert.Equal(symbol, page.SelectedTile?.Key);
        Assert.Equal(reference.Rows.Count(), page.Tiles.Count);
        Assert.All(page.Tiles, t => Assert.NotEqual(ReferenceKind.FeatureType, t.Key.Kind));
    }

    [Fact]
    public async Task The_preview_switches_start_from_the_chart_and_never_change_its_settings()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath })
        {
            SimplifiedSymbols = true,
            SelectedPalette = PaletteType.Dusk,
        };
        using var chartGlyphs = new ChartReferenceGlyphCache(settings);
        using var reference = new ChartReferenceViewModel(f.Service, chartGlyphs, settings);
        using var page = new ChartReferencePageViewModel(reference, f.Service, settings);
        page.Show();
        await reference.Loading;

        // The switches start at the chart's palette and symbol style.
        Assert.Equal(PaletteType.Dusk, page.PreviewPalette);
        Assert.Equal(1, page.PaletteIndex);
        Assert.True(page.PreviewSimplified);
        Assert.Equal(0, page.SymbolStyleIndex);
        Assert.Equal("BCNLAT21", Tile(page, "LateralBeacon").Lead.ItemId);

        var changes = new List<string?>();
        PropertyChangedEventHandler record = (_, e) => changes.Add(e.PropertyName);
        settings.PropertyChanged += record;
        settings.PaletteChanged += p => changes.Add($"PaletteChanged {p}");

        // Night and Paper chart: the page's glyphs change, the chart's don't.
        page.PaletteIndex = 2;
        page.SymbolStyleIndex = 1;

        Assert.Equal(PaletteType.Night, page.PreviewPalette);
        Assert.False(page.PreviewSimplified);
        Assert.Equal("BCNTOW30", Tile(page, "LateralBeacon").Lead.ItemId);
        Assert.Equal(PaletteType.Dusk, settings.SelectedPalette);
        Assert.True(settings.SimplifiedSymbols);
        Assert.Equal(PaletteType.Dusk, chartGlyphs.Palette);
        Assert.Equal("BCNLAT21", reference.Rows.Single(r => r.Key.Code == "LateralBeacon").Lead.ItemId);
        Assert.Empty(changes);

        // Showing the page again starts from the chart's values once more.
        page.Show();
        Assert.Equal(PaletteType.Dusk, page.PreviewPalette);
        Assert.True(page.PreviewSimplified);
        Assert.Equal("BCNLAT21", Tile(page, "LateralBeacon").Lead.ItemId);
        Assert.Empty(changes);
        settings.PropertyChanged -= record;
    }

    [AvaloniaFact]
    public async Task The_page_shows_chips_switches_a_virtualised_tile_grid_and_the_selected_entry_at_1280_by_720()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101", "S-201");
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }) { SimplifiedSymbols = true };
        using var reference = new ChartReferenceViewModel(f.Service, new ChartReferenceGlyphCache(PaletteType.Day), settings, a => Dispatcher.UIThread.Post(a));
        using var page = new ChartReferencePageViewModel(reference, f.Service, settings, a => Dispatcher.UIThread.Post(a));
        using var host = ViewHost.Show(new ChartReferencePageView { DataContext = page }, 1280, 720);
        await reference.Loading;
        Dispatcher.UIThread.RunJobs();
        reference.SelectedProduct = reference.Products.Single(p => p.Spec.Name == "S-101");
        await reference.Loading;
        Dispatcher.UIThread.RunJobs();
        host.Settle();

        // A chip per product; search; Features / Symbols and the two switches.
        var chips = host.Find<ListBox>("ChartReferencePage.Products");
        Assert.Equal(2, host.FindAll<ListBoxItem>("ChartReferencePage.Product").Count);
        Assert.Same(reference.SelectedProduct, chips.SelectedItem);
        host.Find<TextBox>("ChartReferencePage.Query");
        Assert.Equal(0, host.Find<TabControl>("ChartReferencePage.Mode").SelectedIndex);
        Assert.Equal((int)settings.SelectedPalette, host.Find<TabControl>("ChartReferencePage.Palette").SelectedIndex);
        Assert.Equal(0, host.Find<TabControl>("ChartReferencePage.SymbolStyle").SelectedIndex);
        Assert.True(host.IsShown("ChartReferencePage.Entry"));

        // Several tiles to a line, under section headings; virtualised, so
        // only the lines in view are realised.
        Assert.True(page.Columns >= 4, $"{page.Columns} columns");
        Assert.NotEmpty(host.FindAll<StackPanel>("ChartReferencePage.Section"));
        var tiles = host.FindAll<Button>("ChartReferencePage.Tile");
        Assert.NotEmpty(tiles);
        Assert.True(tiles.Count < page.Tiles.Count / 2, $"{tiles.Count} of {page.Tiles.Count} tiles realised");
        Assert.All(tiles, t => Assert.Equal(((ChartReferenceRowViewModel)t.DataContext!).Name, Avalonia.Automation.AutomationProperties.GetName(t)));
        Assert.NotEmpty(host.FindAll<TextBlock>("ChartReferencePage.Tile.Category"));

        // The tiles fill in as their glyphs are drawn.
        await page.GlyphsDrawn;
        Dispatcher.UIThread.RunJobs();
        host.Settle();
        Assert.Contains(
            host.FindAll<Border>("ChartReferencePage.Tile.Glyph"),
            t => t.GetVisualDescendants().OfType<Image>().Any(i => i.IsVisible && i.Source is Bitmap));

        // Clicking a tile selects its entry, here and in the activity, and shows it on the right.
        var tile = tiles.First(t => ((ChartReferenceRowViewModel)t.DataContext!).HasGlyph);
        var row = (ChartReferenceRowViewModel)tile.DataContext!;
        host.Click(tile);
        Assert.Equal(row.Key, reference.SelectedRow?.Key);
        Assert.Same(row, page.SelectedTile);
        Assert.Equal(row.Name, host.Find<TextBlock>("ChartReferencePage.Entry.Name").Text);
        Assert.True(host.IsShown("ChartReferencePage.Entry.Details"));
        await page.GlyphsDrawn;
        Dispatcher.UIThread.RunJobs();
        Assert.IsAssignableFrom<Bitmap>(page.HeroGlyph);

        // The palette switch previews only.
        var palette = settings.SelectedPalette;
        host.Find<TabControl>("ChartReferencePage.Palette").SelectedIndex = 2;
        host.Settle();
        Assert.Equal(PaletteType.Night, page.PreviewPalette);
        Assert.Equal(palette, settings.SelectedPalette);
    }

    private ShellLayoutViewModel Layout(params IActivityTab[] tabs)
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsPath };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        return new ShellLayoutViewModel(settings, tabs, timeline, action => action());
    }

    private static ReferenceKey Key(ChartReferenceViewModel reference, string code) =>
        new(reference.Index!.Spec, ReferenceKind.FeatureType, code);

    private static ChartReferenceRowViewModel Tile(ChartReferencePageViewModel page, string code) =>
        page.Tiles.Single(t => t.Key.Code == code);
}
