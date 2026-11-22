using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Glyphs for Chart reference (#1636, symbol dictionary design note §3.4,
/// R3): an area fill row in the Legend tab, as every other kind of row, and
/// <see cref="ChartReferenceGlyphCache"/>, which keeps fitted glyphs per spec,
/// item, palette and size and drops them on a palette change (legends D10).
/// </summary>
public sealed class ChartReferenceGlyphTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task A_fill_row_is_a_fill_kind_on_the_chart_background()
    {
        var fill = await S101FillAsync("DRGARE01", "Dredged area");
        var tile = LegendViewModel.TileOf(PaletteType.Day);

        var row = new LegendRowViewModel(fill, tile);

        Assert.Equal(LegendRowKind.Fill, LegendRows.KindOf(fill));
        Assert.Equal(LegendRowKind.Fill, row.Kind);
        Assert.False(row.IsSwatch);
        Assert.True(row.HasGlyph);
        Assert.Null(row.Color);
        Assert.Same(tile, row.Tile);
        Assert.Equal((18, 13), (row.GlyphWidth, row.GlyphHeight)); // 14 × 10, shown 1.25 times larger
    }

    [AvaloniaFact]
    public async Task The_Legend_tab_draws_every_kind_of_row_including_a_fill()
    {
        var fill = await S101FillAsync("DRGARE01", "Dredged area");
        var legend = new List<LegendEntry>(LayerStackLegendTests.FrontsLegend()) { fill };

        var f = new LegendServiceTests.Fixture();
        f.Add("101AA00DS0001.000", "S-101", 40, -75, 41, -74);
        f.Loader.SetEntries(LegendServiceTests.VectorEntry("101AA00DS0001.000", "S-101", "s101.areas", "Areas: 101AA00DS0001.000", S98DisplayPlane.BaseChartUnder, legend));
        f.Viewport.Publish(39, -76, 42, -73);
        using var service = f.Create();
        using var tab = new LegendViewModel(
            service,
            new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero))),
            new FixedFormat(),
            find: null)
        { IsAllLoaded = true };
        using var host = ViewHost.Show(new LegendView { DataContext = tab }, width: 360, height: 700);

        var rows = host.FindAll<Border>("Legend.Row");
        Assert.Equal(legend.Select(e => e.Label), rows.Select(AutomationProperties.GetName));
        Assert.Equal(
            [LegendRowKind.Line, LegendRowKind.Line, LegendRowKind.Line, LegendRowKind.Symbol, LegendRowKind.Area, LegendRowKind.Swatch, LegendRowKind.Fill],
            rows.Select(r => Assert.IsType<LegendRowViewModel>(r.DataContext).Kind));

        // Every row but the plain swatch shows its drawn glyph, the fill's too.
        foreach (var row in rows)
        {
            var vm = (LegendRowViewModel)row.DataContext!;
            var image = row.GetVisualDescendants().OfType<Image>().Single();
            Assert.Equal(vm.HasGlyph, image.IsVisible);
            if (vm.HasGlyph)
                Assert.IsAssignableFrom<Bitmap>(image.Source);
        }
    }

    [AvaloniaFact]
    public void The_cache_draws_a_glyph_once_per_spec_item_and_size()
    {
        using var cache = new ChartReferenceGlyphCache(PaletteType.Day);
        var builds = new List<PaletteType>();
        LegendEntry Build(PaletteType palette)
        {
            builds.Add(palette);
            return LayerStackLegendTests.FrontsLegend()[3];
        }

        var tile = cache.Get(Key("S-101", ReferenceKind.Symbol, "BCNLAT21"), 40, 40, Build);
        Assert.Same(tile, cache.Get(Key("S-101", ReferenceKind.Symbol, "BCNLAT21"), 40, 40, Build));
        var bitmap = Assert.IsAssignableFrom<Bitmap>(tile);
        Assert.Equal((80, 80), (bitmap.PixelSize.Width, bitmap.PixelSize.Height)); // at the device scale

        var hero = Assert.IsAssignableFrom<Bitmap>(cache.Get(Key("S-101", ReferenceKind.Symbol, "BCNLAT21"), 160, 120, Build));
        Assert.Equal((320, 240), (hero.PixelSize.Width, hero.PixelSize.Height));
        cache.Get(Key("S-401", ReferenceKind.Symbol, "BCNLAT21"), 40, 40, Build);
        cache.Get(Key("S-101", ReferenceKind.AreaFill, "BCNLAT21"), 40, 40, Build);

        Assert.Equal(4, builds.Count);
        Assert.Equal(4, cache.Count);

        // An item with nothing to draw is remembered as such.
        int none = 0;
        Assert.Null(cache.Get(Key("S-101", ReferenceKind.Symbol, "LIGHTS"), 40, 40, _ => { none++; return null; }));
        Assert.Null(cache.Get(Key("S-101", ReferenceKind.Symbol, "LIGHTS"), 40, 40, _ => { none++; return null; }));
        Assert.Equal(1, none);
    }

    [AvaloniaFact]
    public async Task A_palette_change_drops_the_glyphs_and_draws_them_again_in_the_new_palette()
    {
        var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = _settingsPath }) { SelectedPalette = PaletteType.Day };
        using var cache = new ChartReferenceGlyphCache(settings);
        var fills = new Dictionary<PaletteType, FillLegendEntry>
        {
            [PaletteType.Day] = await S101FillAsync("PRTSUR01", "Incompletely surveyed", PaletteType.Day),
            [PaletteType.Night] = await S101FillAsync("PRTSUR01", "Incompletely surveyed", PaletteType.Night),
        };
        var builds = new List<PaletteType>();
        LegendEntry Build(PaletteType palette)
        {
            builds.Add(palette);
            return fills[palette];
        }

        var day = cache.Get(Key("S-101", ReferenceKind.AreaFill, "PRTSUR01"), 40, 40, Build);
        Assert.Equal(Color.FromRgb(201, 237, 255), Assert.IsAssignableFrom<ISolidColorBrush>(cache.Tile).Color);
        int raised = 0;
        cache.PaletteChanged += (_, _) => raised++;

        settings.SelectedPalette = PaletteType.Night;

        Assert.Equal(1, raised);
        Assert.Equal(PaletteType.Night, cache.Palette);
        Assert.Equal(0, cache.Count);
        Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(cache.Tile).Color);
        var night = cache.Get(Key("S-101", ReferenceKind.AreaFill, "PRTSUR01"), 40, 40, Build);
        Assert.NotSame(day, night);
        Assert.Equal([PaletteType.Day, PaletteType.Night], builds);

        // The same palette again changes nothing; once disposed the cache stops following.
        cache.SetPalette(PaletteType.Night);
        Assert.Equal(1, raised);
        cache.Dispose();
        settings.SelectedPalette = PaletteType.Dusk;
        Assert.Equal(PaletteType.Night, cache.Palette);
    }

    private static ReferenceKey Key(string spec, ReferenceKind kind, string code) => new(new SpecRef(spec, default), kind, code);

    private sealed class FixedFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    // A fill row from the bundled S-101 portrayal catalogue, in a palette.
    private static async Task<FillLegendEntry> S101FillAsync(string name, string label, PaletteType palette = PaletteType.Day)
    {
        var ct = TestContext.Current.CancellationToken;
        using var source = Specification.CreatePortrayalCatalogueSource("S-101");
        using var provider = await PortrayalCatalogueProvider.OpenAsync(source, cancellationToken: ct);
        var catalogue = new S101PortrayalCatalogue(provider);
        await catalogue.SwitchPaletteAsync(palette, ct);
        var fill = await catalogue.GetAreaFillAsync(name, ct);
        var symbol = await catalogue.GetSymbolAsync(fill.PatternSymbol!, ct);
        var legend = new CatalogueLegendBuilder(
            catalogue.ActivePalette,
            n => n == symbol.Name ? symbol.SvgContent : null,
            areaFills: n => n == name ? fill : null);
        return legend.Fill(name, label);
    }
}
