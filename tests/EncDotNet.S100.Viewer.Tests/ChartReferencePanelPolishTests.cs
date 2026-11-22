using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Chart reference panel polish from first hands-on testing (#1657): rows in
/// the viewer's accentList, glyphs inset in their tiles, In view as an icon
/// toggle, the product combo's Open tag, expanded rows that wrap, symbol rows
/// named by their description, "1 match", and the right dock kept off the
/// study page.
/// </summary>
public sealed class ChartReferencePanelPolishTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    [AvaloniaFact]
    public async Task Rows_use_the_accentList_and_a_selected_row_paints_the_accent_bar()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await LoadAsync(f);
        using var host = ViewHost.Show(new ChartReferenceView { DataContext = vm }, 320, 800);

        var list = host.Find<ListBox>("ChartReference.List");
        Assert.Contains("accentList", list.Classes);

        // Headers stay out of selection.
        var header = host.FindAll<ListBoxItem>("ChartReference.Section")[0];
        Assert.False(header.Focusable);
        Assert.False(header.IsHitTestVisible);

        vm.Open(new ReferenceKey(vm.Index!.Spec, ReferenceKind.FeatureType, "LateralBeacon"));
        host.Settle();

        var selected = Assert.IsType<ListBoxItem>(list.ContainerFromItem(vm.SelectedItem!));
        Assert.True(selected.IsSelected);
        var bar = selected.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Bar");
        Assert.Equal(host.Window.FindResource("AccentBrush"), bar.Background);
        var name = host.Find<TextBlock>("ChartReference.Row.Name", selected);
        Assert.Contains("accentListTitle", name.Classes);
    }

    [AvaloniaFact]
    public async Task A_symbol_keeps_its_inset_inside_the_tile_and_a_fill_still_fills_it()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        using var vm = new ChartReferenceViewModel(f.Service, cache, post: a => Dispatcher.UIThread.Post(a));
        vm.Activate();
        await vm.Loading;
        Dispatcher.UIThread.RunJobs();

        // About 5–6 px on a 40 px tile, in proportion on the 96 px hero.
        Assert.InRange(ChartReferenceGlyphCache.PaddingOf(40, 40), 5f, 6f);
        Assert.InRange(ChartReferenceGlyphCache.PaddingOf(96, 96), 12f, 15f);

        var beacon = Row(vm, "LateralBeacon");
        vm.Mode = ChartReferenceMode.Symbols;
        Dispatcher.UIThread.RunJobs();
        var fill = Row(vm, "DRGARE01");
        _ = beacon.Glyph;
        _ = fill.Glyph;
        await vm.GlyphsDrawn;
        Dispatcher.UIThread.RunJobs();

        // Drawn at 2× device scale: the inset is 2 × 5.6 px.
        using var symbol = Decode(beacon.Glyph);
        int inset = (int)Math.Floor(ChartReferenceGlyphCache.PaddingOf(40, 40) * ChartReferenceGlyphCache.DeviceScale) - 1;
        var inked = Inked(symbol);
        Assert.NotEmpty(inked);
        Assert.All(inked, p => Assert.True(
            p.X >= inset && p.Y >= inset && p.X < symbol.Width - inset && p.Y < symbol.Height - inset,
            $"ink at {p} in the {inset} px inset"));

        // A fill reads as an area: its pattern spans more than an inset glyph could.
        using var area = Decode(fill.Glyph);
        var pattern = Inked(area);
        Assert.NotEmpty(pattern);
        Assert.True(
            pattern.Max(p => p.X) - pattern.Min(p => p.X) > area.Width - 2 * inset,
            $"pattern spans {pattern.Min(p => p.X)}..{pattern.Max(p => p.X)} of {area.Width}");
    }

    [AvaloniaFact]
    public async Task The_product_combos_Open_tag_is_a_separate_element_from_the_product_id()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101", "S-201");
        f.Loaded.Add("S-101");
        using var vm = await LoadAsync(f);
        using var host = ViewHost.Show(new ChartReferenceView { DataContext = vm }, 320, 800);

        var combo = host.Find<ComboBox>("ChartReference.Product");
        var name = host.Find<TextBlock>("ChartReference.Product.Name", combo);
        var tag = host.Find<Border>("ChartReference.Product.OpenTag", combo);
        Assert.Equal("S-101", name.Text);
        Assert.Contains("optionalPill", tag.Classes);
        Assert.Equal("Open", Assert.IsType<TextBlock>(tag.Child).Text);
        Assert.False(tag.IsVisualAncestorOf(name));

        // A screen reader reads the product id, not the record.
        Assert.Equal("S-101", vm.SelectedProduct!.ToString());
    }

    [AvaloniaFact]
    public async Task An_expanded_row_wraps_to_a_320_px_panel()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await LoadAsync(f);
        using var host = ViewHost.Show(new ChartReferenceView { DataContext = vm }, 320, 800);

        vm.Open(new ReferenceKey(vm.Index!.Spec, ReferenceKind.FeatureType, "LateralBeacon"));
        host.Settle();

        var details = host.Find<Control>("ChartReference.Row.Details");
        Assert.True(details.Bounds.Width <= 320, $"details {details.Bounds.Width} px wide");
        var list = host.Find<ListBox>("ChartReference.List");
        var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(vm.SelectedItem!));
        Assert.True(row.Bounds.Width <= 320, $"row {row.Bounds.Width} px wide");
        Assert.All(
            details.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible),
            t => Assert.True(t.Bounds.Width <= 320, $"\"{t.Text}\" is {t.Bounds.Width} px wide"));
        var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
    }

    [Fact]
    public async Task Symbol_rows_show_their_description_in_sentence_case_with_the_code_beneath()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await OpenAsync(f);
        vm.Mode = ChartReferenceMode.Symbols;

        var inform = Row(vm, "INFORM01");
        var entry = Assert.IsType<PortrayalItemEntry>(inform.Entry);
        Assert.Equal("INFORM01", inform.Code);
        Assert.NotEqual("INFORM01", inform.Name);
        Assert.True(inform.ShowsCode);
        Assert.True(char.IsUpper(inform.Name[0]), inform.Name);
        Assert.DoesNotContain('\n', inform.Name);
        Assert.Contains(inform.Name[1..], entry.Definition!, StringComparison.OrdinalIgnoreCase);

        // Families keep their prefix; feature types keep their names.
        Assert.Equal("SOUNDG", Row(vm, "SOUNDG").Name);
        vm.Mode = ChartReferenceMode.Features;
        Assert.Equal("Lateral Beacon", Row(vm, "LateralBeacon").Name);
    }

    [Theory]
    [InlineData("this object has additional information", "This object has additional information")]
    [InlineData("THIS OBJECT HAS ADDITIONAL INFORMATION.", "This object has additional information")]
    [InlineData("Point of the IHO data quality\nsecond line", "Point of the IHO data quality")]
    public void A_symbol_row_title_is_its_descriptions_first_line_in_sentence_case(string definition, string expected)
    {
        var entry = new PortrayalItemEntry
        {
            Key = new ReferenceKey(EncDotNet.S100.Core.SpecRef.Parse("S-101/2.0.0"), ReferenceKind.Symbol, "INFORM01"),
            Name = "INFORM01",
            Definition = definition,
        };

        Assert.Equal(expected, ChartReferenceRowViewModel.NameOf(entry));
    }

    [Fact]
    public async Task The_summary_says_1_match_and_N_matches()
    {
        using var f = new ChartReferenceServiceTests.Fixture("S-101");
        using var vm = await OpenAsync(f);

        vm.Query = "lateral beacon";
        Assert.Single(vm.Items);
        Assert.Equal("1 match", vm.Summary);

        vm.Query = "beacon";
        Assert.True(vm.Items.Count > 1);
        Assert.Equal($"{vm.Items.Count} matches", vm.Summary);
    }

    [Fact]
    public void The_right_dock_stays_folded_while_the_study_page_shows()
    {
        var page = new FakeTab { Id = ShellLayoutViewModel.ChartReferencePageTabId, Order = 53, FillsContent = true };
        var activity = new FakeTab { Id = ShellLayoutViewModel.ChartReferenceTabId, Order = 52 };
        var legend = new FakeTab { Id = ShellLayoutViewModel.LegendTabId, Order = 20, Dock = TabDock.Right, Title = "Legend" };
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new FakeTimeProvider(), dispatch: action => action(), refresher: null, progress: null, scope: null, library: null);
        var layout = new ShellLayoutViewModel(new ViewerSettings { SettingsFilePath = _settingsPath }, [activity, page, legend], timeline, action => action())
        {
            MapWidth = 700, // an 1100 px window with the left dock open
        };
        layout.SelectedLeftTab = activity;

        // D9: the Legend opens folded, and its edge tab shows it over the map.
        layout.ToggleLegendCommand.Execute(null);
        Assert.True(layout.IsRightDockNarrowFolded);
        layout.UnfoldRightDockCommand.Execute(null);
        Assert.True(layout.IsRightDockOverlaid);

        // Opening the study page folds it, and it stays off the page.
        layout.SelectedLeftTab = page;
        Assert.True(layout.IsContentPanelOpen);
        Assert.False(layout.IsRightDockOverlaid);
        Assert.False(layout.IsRightDockPanelVisible);
        layout.UnfoldRightDockCommand.Execute(null);
        layout.ToggleLegendCommand.Execute(null);
        Assert.False(layout.IsRightDockPanelVisible);

        // Back on the activity, the edge tab shows it over the map again.
        layout.IsLeftDockOpen = false;
        Assert.False(layout.IsContentPanelOpen);
        layout.UnfoldRightDockCommand.Execute(null);
        Assert.True(layout.IsRightDockOverlaid);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private sealed class FakeTab : IActivityTab
    {
        public required string Id { get; init; }
        public int Order { get; init; }
        public string Title { get; init; } = "T";
        public string Tooltip { get; init; } = "Tip";
        public object ViewModel { get; init; } = new object();
        public Control CreateView() => new ContentControl();
        public bool PersistAsLastSelected => false;
        public TabDock Dock { get; init; } = TabDock.Left;
        public bool AutoOpenOnContentSignal => false;
        public bool FillsContent { get; init; }
        public Control CreateIcon() => new ContentControl();
        public bool IsVisible => true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
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
    private static async Task<ChartReferenceViewModel> LoadAsync(ChartReferenceServiceTests.Fixture f)
    {
        var vm = new ChartReferenceViewModel(f.Service, new ChartReferenceGlyphCache(PaletteType.Day), post: a => Dispatcher.UIThread.Post(a));
        vm.Activate();
        await vm.Loading;
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.Index);
        return vm;
    }

    private static ChartReferenceRowViewModel Row(ChartReferenceViewModel vm, string code) =>
        vm.Rows.Single(r => r.Key.Code == code);

    private static SKBitmap Decode(Avalonia.Media.IImage? image)
    {
        var bitmap = Assert.IsAssignableFrom<Bitmap>(image);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static List<(int X, int Y)> Inked(SKBitmap bitmap)
    {
        var inked = new List<(int X, int Y)>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 64)
                    inked.Add((x, y));
            }
        }

        return inked;
    }
}
