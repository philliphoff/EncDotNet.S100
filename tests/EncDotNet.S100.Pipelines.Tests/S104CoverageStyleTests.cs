using EncDotNet.S100.Datasets.S104;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="S104CoverageStyle"/>, the SoundCharts S-104
/// portrayal (S-104 Ed 2.0.0 §9 defines none): one ink per palette, no colour
/// fill, isoline intervals and label formats (#937, #483).
/// </summary>
public sealed class S104CoverageStyleTests
{
    [Theory]
    [InlineData(PaletteType.Day, "#0F5E73", "#FFFFFF", "#FFFFFF", "#F6FBFE")]
    [InlineData(PaletteType.Dusk, "#9FD3DE", "#3B4650", "#3B4650", "#3B4650")]
    [InlineData(PaletteType.Night, "#3E7C88", "#0B0F14", "#0B0F14", "#0B0F14")]
    public async Task Each_palette_has_its_own_ink(PaletteType palette, string ink, string fill, string halo, string isolineHalo)
    {
        var style = new S104CoverageStyle();
        await style.SwitchPaletteAsync(palette, TestContext.Current.CancellationToken);

        Assert.Equal(RgbaColor.FromHex(ink), style.ActiveInk.Ink);
        Assert.Equal(RgbaColor.FromHex(fill), style.ActiveInk.SymbolFill);
        Assert.Equal(RgbaColor.FromHex(halo), style.ActiveInk.LabelHalo);
        Assert.Equal(RgbaColor.FromHex(isolineHalo), style.ActiveInk.IsolineLabelHalo);
        Assert.Equal(style.ActiveInk, S104CoverageStyle.GetInk(palette));
    }

    [Fact]
    public void Night_ink_and_grounds_stay_under_luminance_0_2()
    {
        var night = S104CoverageStyle.GetInk(PaletteType.Night);

        Assert.All(
            [night.Ink, night.SymbolFill, night.LabelHalo, night.IsolineLabelHalo],
            color => Assert.True(RelativeLuminance(color) < 0.2, $"{color} is too bright for Night."));
    }

    [Fact]
    public async Task Surface_has_no_colour_fill()
    {
        var style = new S104CoverageStyle();
        await style.SwitchPaletteAsync(PaletteType.Day, TestContext.Current.CancellationToken);

        Assert.Null(style.ResolveColorScheme(new MarinerSettings()));
    }

    [Theory]
    [InlineData(-0.4, 0.5, 0.25)]
    [InlineData(0.0, 0.99, 0.25)]
    [InlineData(0.0, 1.0, 0.5)]
    [InlineData(-1.2, 2.3, 0.5)]
    public void Interval_is_finer_for_a_range_under_one_metre(double minimum, double maximum, double expected) =>
        Assert.Equal(expected, S104CoverageStyle.SelectIsolineInterval(minimum, maximum));

    [Fact]
    public void Levels_are_the_interval_multiples_inside_the_range()
    {
        Assert.Equal([-0.5, 0.0, 0.5, 1.0, 1.5], S104CoverageStyle.IsolineLevels(-0.7, 1.6, 0.5));
        Assert.Equal([0.25, 0.5], S104CoverageStyle.IsolineLevels(0.25, 0.6, 0.25));
        Assert.Empty(S104CoverageStyle.IsolineLevels(0.1, 0.4, 0.5));
    }

    [Theory]
    [InlineData(1.0, 0.5, "1.0")]
    [InlineData(0.5, 0.5, "0.5")]
    [InlineData(0.0, 0.5, "0")]
    [InlineData(-0.5, 0.5, "−0.5")]
    [InlineData(0.25, 0.25, "0.25")]
    [InlineData(-0.75, 0.25, "−0.75")]
    [InlineData(0.0, 0.25, "0")]
    public void Isoline_labels_use_one_decimal_or_two_for_fine_steps(double level, double interval, string expected) =>
        Assert.Equal(expected, S104CoverageStyle.FormatIsolineLabel(level, interval));

    [Theory]
    [InlineData(1.42, "+1.42")]
    [InlineData(-0.12, "−0.12")]
    [InlineData(0.0, "+0.00")]
    [InlineData(-0.001, "+0.00")]
    [InlineData(0.875, "+0.88")]
    public void Station_values_always_show_the_sign_and_two_decimals(double height, string expected) =>
        Assert.Equal(expected, S104CoverageStyle.FormatStationValue(height));

    private static double RelativeLuminance(RgbaColor color)
    {
        static double Channel(byte value)
        {
            double c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
