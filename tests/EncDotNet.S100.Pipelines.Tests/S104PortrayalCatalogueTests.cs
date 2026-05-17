using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="S104PortrayalCatalogue"/> Day / Dusk / Night
/// palette parity (PR-H). S-104 has no IHO portrayal catalogue; this
/// catalogue ships hand-coded palette variants used by the viewer.
/// Tests guard the byte-for-byte Day palette (legacy compatibility)
/// and prove that <see cref="ICoveragePortrayalCatalogue.SwitchPalette"/>
/// actually swaps the band table.
/// </summary>
public sealed class S104PortrayalCatalogueTests
{
    private static readonly (float Min, float Max, string Color)[] LegacyDayBands =
    [
        (-5.0f,  -2.0f, "#08519C"),
        (-2.0f,  -1.0f, "#3182BD"),
        (-1.0f,  -0.5f, "#6BAED6"),
        (-0.5f,  -0.2f, "#9ECAE1"),
        (-0.2f,   0.0f, "#C6DBEF"),
        ( 0.0f,   0.2f, "#C7E9C0"),
        ( 0.2f,   0.5f, "#A1D99B"),
        ( 0.5f,   1.0f, "#74C476"),
        ( 1.0f,   2.0f, "#31A354"),
        ( 2.0f,   5.0f, "#006D2C"),
    ];

    [Fact]
    public void Day_palette_returns_existing_blue_green_diverging_scheme()
    {
        var catalogue = new S104PortrayalCatalogue();
        catalogue.SwitchPalette(PaletteType.Day);

        var scheme = catalogue.ResolveColorScheme(new MarinerSettings());

        Assert.Equal("waterLevelHeight", scheme.FieldName);
        Assert.Equal(LegacyDayBands.Length, scheme.Bands.Count);

        for (int i = 0; i < LegacyDayBands.Length; i++)
        {
            var expected = LegacyDayBands[i];
            Assert.Equal(expected.Min, scheme.Bands[i].MinValue);
            Assert.Equal(expected.Max, scheme.Bands[i].MaxValue);
            Assert.Equal(expected.Color, scheme.Bands[i].Color, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Dusk_palette_differs_from_Day_for_at_least_one_band()
    {
        var catalogue = new S104PortrayalCatalogue();

        catalogue.SwitchPalette(PaletteType.Day);
        var day = catalogue.ResolveColorScheme(new MarinerSettings());

        catalogue.SwitchPalette(PaletteType.Dusk);
        var dusk = catalogue.ResolveColorScheme(new MarinerSettings());

        Assert.Equal(day.Bands.Count, dusk.Bands.Count);
        Assert.True(
            day.Bands.Zip(dusk.Bands)
                .Any(p => !string.Equals(p.First.Color, p.Second.Color, StringComparison.OrdinalIgnoreCase)),
            "Dusk palette must differ from Day for at least one band.");
    }

    [Fact]
    public void Night_palette_differs_from_Day_for_at_least_one_band()
    {
        var catalogue = new S104PortrayalCatalogue();

        catalogue.SwitchPalette(PaletteType.Day);
        var day = catalogue.ResolveColorScheme(new MarinerSettings());

        catalogue.SwitchPalette(PaletteType.Night);
        var night = catalogue.ResolveColorScheme(new MarinerSettings());

        Assert.Equal(day.Bands.Count, night.Bands.Count);
        Assert.True(
            day.Bands.Zip(night.Bands)
                .Any(p => !string.Equals(p.First.Color, p.Second.Color, StringComparison.OrdinalIgnoreCase)),
            "Night palette must differ from Day for at least one band.");
    }

    [Fact]
    public void SwitchPalette_actually_swaps_active_band_list()
    {
        // Regression guard for the pre-PR-H stub SwitchPalette that
        // updated ActivePalette but left ResolveColorScheme returning
        // the Day band table regardless.
        var catalogue = new S104PortrayalCatalogue();

        catalogue.SwitchPalette(PaletteType.Day);
        var dayFirst = catalogue.ResolveColorScheme(new MarinerSettings()).Bands[0].Color;

        catalogue.SwitchPalette(PaletteType.Night);
        var nightFirst = catalogue.ResolveColorScheme(new MarinerSettings()).Bands[0].Color;

        catalogue.SwitchPalette(PaletteType.Dusk);
        var duskFirst = catalogue.ResolveColorScheme(new MarinerSettings()).Bands[0].Color;

        Assert.NotEqual(dayFirst, nightFirst);
        Assert.NotEqual(dayFirst, duskFirst);
        Assert.NotEqual(nightFirst, duskFirst);
    }

    [Fact]
    public void NoData_color_is_populated_per_palette()
    {
        var catalogue = new S104PortrayalCatalogue();

        catalogue.SwitchPalette(PaletteType.Day);
        var dayNoData = catalogue.ResolveColorScheme(new MarinerSettings()).NoDataColor;

        catalogue.SwitchPalette(PaletteType.Dusk);
        var duskNoData = catalogue.ResolveColorScheme(new MarinerSettings()).NoDataColor;

        catalogue.SwitchPalette(PaletteType.Night);
        var nightNoData = catalogue.ResolveColorScheme(new MarinerSettings()).NoDataColor;

        Assert.False(string.IsNullOrEmpty(dayNoData));
        Assert.False(string.IsNullOrEmpty(duskNoData));
        Assert.False(string.IsNullOrEmpty(nightNoData));

        // Day stays transparent; Dusk/Night use opaque greys per the
        // catalogue's documented choice.
        Assert.Equal("#00000000", dayNoData, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(dayNoData, duskNoData, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(dayNoData, nightNoData, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(duskNoData, nightNoData, StringComparer.OrdinalIgnoreCase);
    }
}
