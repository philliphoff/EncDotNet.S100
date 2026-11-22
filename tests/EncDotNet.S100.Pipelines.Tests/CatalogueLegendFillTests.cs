using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Specifications;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Area fill rows and fitted glyphs for Chart reference (#1636, symbol
/// dictionary design note §3.4, R3): <see cref="CatalogueLegendBuilder.Fill"/>
/// builds a row from a catalogue area fill, <see cref="LegendGlyphRenderer"/>
/// tiles its pattern in a swatch, and <see cref="LegendGlyphRenderer.RenderFitted"/>
/// fits any row to a box. Against the bundled S-101 portrayal catalogue.
/// </summary>
public sealed class CatalogueLegendFillTests
{
    [Fact]
    public async Task A_catalogue_area_fill_becomes_a_fill_row_with_its_lattice()
    {
        var legend = await BuilderAsync(PaletteType.Day, fills: ["DRGARE01"], symbols: ["DRGARE01P"]);

        var row = legend.Fill("DRGARE01", "Dredged area");

        Assert.Equal("Dredged area", row.Label);
        Assert.NotNull(row.Fill.Svg);
        Assert.DoesNotContain("class=\"", row.Fill.Svg, StringComparison.Ordinal); // palette classes resolved
        Assert.Null(row.Fill.Color); // the chart shows through the pattern
        Assert.Equal((3.5, 0.0, 0.0, 3.5), (row.Fill.V1X, row.Fill.V1Y, row.Fill.V2X, row.Fill.V2Y));
    }

    [Fact]
    public async Task A_fill_row_is_built_in_the_palette_it_is_asked_for()
    {
        var day = (await BuilderAsync(PaletteType.Day, fills: ["PRTSUR01"], symbols: ["PRTSUR01P"])).Fill("PRTSUR01", "Incompletely surveyed");
        var night = (await BuilderAsync(PaletteType.Night, fills: ["PRTSUR01"], symbols: ["PRTSUR01P"])).Fill("PRTSUR01", "Incompletely surveyed");

        Assert.NotEqual(day.Fill.Svg, night.Fill.Svg);
        Assert.Equal(night.Fill with { Svg = day.Fill.Svg }, day.Fill);
    }

    [Fact]
    public async Task A_missing_fill_or_resolver_throws()
    {
        var legend = await BuilderAsync(PaletteType.Day, fills: ["DRGARE01"], symbols: ["DRGARE01P"]);
        Assert.Throws<KeyNotFoundException>(() => legend.Fill("NOSUCH01", "None"));

        var withoutFills = new CatalogueLegendBuilder(ColorPalette.Default, _ => null);
        Assert.Throws<InvalidOperationException>(() => withoutFills.Fill("DRGARE01", "Dredged area"));
    }

    [Theory]
    [InlineData("DRGARE01", "DRGARE01P")]
    [InlineData("PRTSUR01", "PRTSUR01P")]
    public async Task An_area_fill_renders_its_pattern_tiled_across_the_swatch(string fillName, string symbolName)
    {
        var row = (await BuilderAsync(PaletteType.Day, fills: [fillName], symbols: [symbolName])).Fill(fillName, fillName);

        // At legend size and fitted to a 40 px tile the pattern repeats: it
        // shows in each quarter of the glyph.
        Assert.Equal((14, 10), LegendGlyphRenderer.SizeOf(row));
        using (var legendSize = LegendGlyphRenderer.Render(row, 4f))
            Assert.True(Inked(legendSize).Count > 0);

        using var tile = LegendGlyphRenderer.RenderFitted(row, 40, 40, 2f);
        Assert.Equal((80, 80), (tile.Width, tile.Height));
        var inked = Inked(tile);
        foreach (var (left, top) in new[] { (0, 0), (40, 0), (0, 40), (40, 40) })
            Assert.Contains(inked, p => p.X >= left && p.X < left + 40 && p.Y >= top && p.Y < top + 40);

        // Two lattice cells across the shorter side.
        Assert.Equal(40 / (LegendGlyphRenderer.FillCellsAcross * Math.Max(Math.Abs(row.Fill.V1X), Math.Abs(row.Fill.V2Y))), LegendGlyphRenderer.FillPixelsPerMillimetre(row.Fill, 40, 40), 6);
    }

    [Theory]
    [InlineData(40, 40)]
    [InlineData(80, 40)]
    [InlineData(40, 120)]
    [InlineData(160, 160)]
    public async Task A_fitted_symbol_fills_its_box_and_keeps_its_aspect_ratio(int width, int height)
    {
        var symbol = (await BuilderAsync(PaletteType.Day, symbols: ["BCNLAT21"])).Symbol("BCNLAT21", "Lateral beacon");
        var picture = EncDotNet.S100.Renderers.Skia.Scene.SkiaDisplayListRenderer.GetSymbolPicture(symbol.Svg);
        Assert.NotNull(picture);
        double aspect = picture.CullRect.Width / picture.CullRect.Height;

        using var glyph = LegendGlyphRenderer.RenderFitted(symbol, width, height, 2f);
        var inked = Inked(glyph);
        double inkWidth = inked.Max(p => p.X) - inked.Min(p => p.X) + 1;
        double inkHeight = inked.Max(p => p.Y) - inked.Min(p => p.Y) + 1;

        // It reaches the box on one side and keeps the symbol's proportions.
        Assert.True(inkWidth >= glyph.Width * 0.9 || inkHeight >= glyph.Height * 0.9, $"{inkWidth}×{inkHeight} in {glyph.Width}×{glyph.Height}");
        Assert.Equal(aspect, inkWidth / inkHeight, 0.15 * aspect);
    }

    [Fact]
    public async Task A_fitted_line_sample_shows_a_whole_interval()
    {
        var legend = await BuilderAsync(PaletteType.Day, lineStyles: ["ACHARE51"], symbols: ["EMAREMG1", "EMACHAR1"]);
        var row = legend.Line("ACHARE51", "Anchorage area");
        Assert.Equal(32.3, row.Line.IntervalLength, 3);

        // A 40 px tile is too short for the 32.3 mm interval at chart size, so
        // it draws smaller; a wide box draws at chart size.
        double tile = LegendGlyphRenderer.FittedLineScale(row.Line, 40);
        Assert.True(tile * 38 >= row.Line.IntervalLength, $"{tile} mm per pixel");
        Assert.Equal(LegendGlyphRenderer.ChartMillimetresPerPixel, LegendGlyphRenderer.FittedLineScale(row.Line, 400));

        using var glyph = LegendGlyphRenderer.RenderFitted(row, 40, 40, 2f);
        var inked = Inked(glyph);
        Assert.True(inked.Max(p => p.X) - inked.Min(p => p.X) > glyph.Width * 0.8);
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

    // A builder over the bundled S-101 catalogue in a palette, with the named
    // assets loaded up front, as a processor's pre-warm loads them.
    private static async Task<CatalogueLegendBuilder> BuilderAsync(
        PaletteType palette,
        string[]? fills = null,
        string[]? symbols = null,
        string[]? lineStyles = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var source = Specification.CreatePortrayalCatalogueSource("S-101");
        using var provider = await PortrayalCatalogueProvider.OpenAsync(source, cancellationToken: ct);
        var catalogue = new S101PortrayalCatalogue(provider);
        await catalogue.SwitchPaletteAsync(palette, ct);

        var svgs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in symbols ?? [])
            svgs[name] = (await catalogue.GetSymbolAsync(name, ct)).SvgContent;
        var areaFills = new Dictionary<string, AreaFill>(StringComparer.Ordinal);
        foreach (var name in fills ?? [])
            areaFills[name] = await catalogue.GetAreaFillAsync(name, ct);
        var styles = new Dictionary<string, LineStyle>(StringComparer.Ordinal);
        foreach (var name in lineStyles ?? [])
            styles[name] = await catalogue.GetLineStyleAsync(name, ct);

        return new CatalogueLegendBuilder(
            catalogue.ActivePalette,
            svgs.GetValueOrDefault,
            styles.GetValueOrDefault,
            areaFills.GetValueOrDefault);
    }
}
