using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Spec-compliance tests for <see cref="S111PortrayalCatalogue"/> using
/// the bundled S-111 portrayal catalogue (Ed 2.0.0) from
/// <c>EncDotNet.S100.Specifications</c>. Mirrors the PR #98 / S-102
/// taxonomy: palette loading, palette switching, NoData propagation,
/// XSLT-driven band table parsing, and defensive XSLT-shape failures.
/// </summary>
public sealed class S111PortrayalCatalogueTests : IDisposable
{
    private readonly IAssetSource _source;
    private readonly PortrayalCatalogueProvider _provider;

    // The legacy hand-coded table that used to live in
    // S111PortrayalCatalogue. Kept here as a regression guard for the
    // XSLT-driven parser — every row must match.
    private static readonly (float Min, float Max, string Token, string Symbol, bool ScaleByValue, float ScaleFactor, string Label)[] LegacyBands =
    [
        (0.0f, 0.5f, "SCBN1", "SCAROW01", false, 0.40f, "0\u20130.5 kn"),
        (0.5f, 1.0f, "SCBN2", "SCAROW02", false, 0.40f, "0.5\u20131 kn"),
        (1.0f, 2.0f, "SCBN3", "SCAROW03", false, 0.40f, "1\u20132 kn"),
        (2.0f, 3.0f, "SCBN4", "SCAROW04", true, 0.20f, "2\u20133 kn"),
        (3.0f, 5.0f, "SCBN5", "SCAROW05", true, 0.20f, "3\u20135 kn"),
        (5.0f, 7.0f, "SCBN6", "SCAROW06", true, 0.20f, "5\u20137 kn"),
        (7.0f, 10.0f, "SCBN7", "SCAROW07", true, 0.20f, "7\u201310 kn"),
        (10.0f, 13.0f, "SCBN8", "SCAROW08", true, 0.20f, "10\u201313 kn"),
        (13.0f, float.MaxValue, "SCBN9", "SCAROW09", false, 2.60f, "> 13 kn"),
    ];

    public S111PortrayalCatalogueTests()
    {
        _source = Specification.CreatePortrayalCatalogueSource("S-111");
        _provider = PortrayalCatalogueProvider.OpenAsync(_source).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _source.Dispose();
    }

    private S111PortrayalCatalogue CreateCatalogue() => new(_provider);

    [Fact]
    public void Day_palette_loads_all_nine_speed_band_tokens()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);

        for (int i = 1; i <= 9; i++)
        {
            var token = $"SCBN{i}";
            Assert.True(
                catalogue.ActivePalette.TryResolve(token, out var hex) && !string.IsNullOrEmpty(hex),
                $"Day palette must resolve {token}");
        }
    }

    [Fact]
    public void Dusk_palette_loads_and_differs_from_Day_for_at_least_one_band()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);
        var dayColors = ReadBandColors(catalogue);

        catalogue.SwitchPalette(PaletteType.Dusk);
        var duskColors = ReadBandColors(catalogue);

        Assert.Equal(dayColors.Count, duskColors.Count);
        Assert.True(
            dayColors.Zip(duskColors)
                .Any(p => !string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)),
            "Dusk palette must differ from Day for at least one band.");
    }

    [Fact]
    public void Night_palette_loads_and_differs_from_Day_for_at_least_one_band()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);
        var dayColors = ReadBandColors(catalogue);

        catalogue.SwitchPalette(PaletteType.Night);
        var nightColors = ReadBandColors(catalogue);

        Assert.Equal(dayColors.Count, nightColors.Count);
        Assert.True(
            dayColors.Zip(nightColors)
                .Any(p => !string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase)),
            "Night palette must differ from Day for at least one band.");
    }

    [Fact]
    public void Bands_parsed_from_xslt_match_legacy_hand_coded_table()
    {
        // S-111 Ed 2.0.0 portrayal (content/S111/pc/Rules/select_arrow.xsl)
        // emits arrow symbology only; ResolveColorScheme intentionally
        // returns null. The band table is still exposed via the symbol
        // scheme so renderers can pick per-band colour tokens.
        var catalogue = CreateCatalogue();
        Assert.Null(catalogue.ResolveColorScheme(new MarinerSettings()));
        var symbolScheme = catalogue.ResolveSymbolScheme(new MarinerSettings());

        Assert.Equal(LegacyBands.Length, symbolScheme.Bands.Count);

        for (int i = 0; i < LegacyBands.Length; i++)
        {
            var expected = LegacyBands[i];
            var symbolBand = symbolScheme.Bands[i];

            Assert.Equal(expected.Min, symbolBand.MinValue);
            Assert.Equal(expected.Max, symbolBand.MaxValue);
            Assert.Equal(expected.Symbol, symbolBand.SymbolRef);
            Assert.Equal(expected.ScaleByValue, symbolBand.ScaleByValue);
            Assert.Equal(expected.ScaleFactor, symbolBand.ScaleFactor);
            Assert.Equal(expected.Label, symbolBand.Label);
        }
    }

    [Fact]
    public void ResolveColorScheme_returns_null_per_bundled_xslt()
    {
        // The bundled S-111 XSLT defines no <coverageFill> on
        // surfaceCurrentSpeed — only arrow symbology. The catalogue
        // therefore returns null so pipelines skip the coverage colour
        // layer (S-111 Ed 2.0.0 §12, content/S111/pc/Rules/select_arrow.xsl).
        var catalogue = CreateCatalogue();
        Assert.Null(catalogue.ResolveColorScheme(new MarinerSettings()));
    }

    [Fact]
    public void SwitchPalette_to_Night_then_ActivePalette_reflects_change()
    {
        var catalogue = CreateCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);
        Assert.True(catalogue.ActivePalette.TryResolve("SCBN1", out var dayBand1));

        catalogue.SwitchPalette(PaletteType.Night);
        Assert.True(catalogue.ActivePalette.TryResolve("SCBN1", out var nightBand1));

        Assert.NotEqual(dayBand1, nightBand1);
    }

    private static IReadOnlyList<string> ReadBandColors(S111PortrayalCatalogue catalogue)
    {
        var palette = catalogue.ActivePalette;
        var result = new List<string>(9);
        for (int i = 1; i <= 9; i++)
        {
            Assert.True(palette.TryResolve($"SCBN{i}", out var hex), $"Missing SCBN{i}");
            result.Add(hex);
        }
        return result;
    }

    [Fact]
    public void Bands_use_scale_constants_parsed_from_xslt_variables()
    {
        // The XSLT defines scaleFloor = 0.40, scaleFactorIntermediate = 0.20,
        // scaleCeiling = 2.60. Bands 1-3 use scaleFloor, 4-8 intermediate, 9 ceiling.
        var catalogue = CreateCatalogue();
        var symbolScheme = catalogue.ResolveSymbolScheme(new MarinerSettings());

        for (int i = 0; i < 3; i++)
        {
            Assert.False(symbolScheme.Bands[i].ScaleByValue);
            Assert.Equal(0.40f, symbolScheme.Bands[i].ScaleFactor);
        }
        for (int i = 3; i < 8; i++)
        {
            Assert.True(symbolScheme.Bands[i].ScaleByValue);
            Assert.Equal(0.20f, symbolScheme.Bands[i].ScaleFactor);
        }
        Assert.False(symbolScheme.Bands[8].ScaleByValue);
        Assert.Equal(2.60f, symbolScheme.Bands[8].ScaleFactor);
    }

    [Fact]
    public void Parser_throws_on_unexpected_closure_value()
    {
        const string xslt = """
            <xsl:transform xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
              <xsl:template name="select_arrow">
                <xsl:variable name="scaleFloor"><xsl:value-of select="0.40"/></xsl:variable>
                <xsl:variable name="scaleCeiling"><xsl:value-of select="2.60"/></xsl:variable>
                <xsl:variable name="scaleFactorIntermediate"><xsl:value-of select="0.20"/></xsl:variable>
                <lookup>
                  <label>SurfaceCurrentSpeedBand1</label>
                  <range lower="0.00" upper="0.50" closure="openInterval"/>
                </lookup>
              </xsl:template>
            </xsl:transform>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xslt));
        var ex = Assert.Throws<InvalidOperationException>(() => S111SpeedBandReader.Read(stream));
        Assert.Contains("openInterval", ex.Message);
    }

    [Fact]
    public void Parser_throws_on_missing_scale_variable()
    {
        const string xslt = """
            <xsl:transform xmlns:xsl="http://www.w3.org/1999/XSL/Transform" version="1.0">
              <xsl:template name="select_arrow">
                <xsl:variable name="scaleFloor"><xsl:value-of select="0.40"/></xsl:variable>
                <xsl:variable name="scaleCeiling"><xsl:value-of select="2.60"/></xsl:variable>
              </xsl:template>
            </xsl:transform>
            """;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xslt));
        var ex = Assert.Throws<InvalidOperationException>(() => S111SpeedBandReader.Read(stream));
        Assert.Contains("scaleFactorIntermediate", ex.Message);
    }
}
