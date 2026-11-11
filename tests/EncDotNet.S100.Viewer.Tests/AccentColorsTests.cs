using Avalonia.Media;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class AccentColorsTests
{
    private static readonly Color Accent = Color.Parse("#007ACC");

    [Theory]
    [InlineData(ChromeTheme.Light)]
    [InlineData(ChromeTheme.Dark)]
    public void ForTheme_StockThemes_ReturnsAccentUnchanged(ChromeTheme theme)
    {
        Assert.Equal(Accent, AccentColors.ForTheme(Accent, theme));
    }

    [Theory]
    [InlineData(ChromeTheme.S100Night)]
    [InlineData(ChromeTheme.S100Dusk)]
    public void ForTheme_LowLightThemes_DimsAndDesaturates(ChromeTheme theme)
    {
        var muted = AccentColors.ForTheme(Accent, theme);

        double originalLuma = Luma(Accent);
        double mutedLuma = Luma(muted);
        Assert.True(mutedLuma < originalLuma, "Muted accent should be dimmer.");

        double originalSpread = ChannelSpread(Accent);
        double mutedSpread = ChannelSpread(muted);
        Assert.True(mutedSpread < originalSpread, "Muted accent should be less saturated.");
    }

    [Fact]
    public void ForTheme_PreservesAlpha()
    {
        var translucent = Color.FromArgb(0x80, 0x00, 0x7A, 0xCC);
        var muted = AccentColors.ForTheme(translucent, ChromeTheme.S100Night);
        Assert.Equal(0x80, muted.A);
    }

    private static double Luma(Color c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);

    private static double ChannelSpread(Color c)
    {
        int max = System.Math.Max(c.R, System.Math.Max(c.G, c.B));
        int min = System.Math.Min(c.R, System.Math.Min(c.G, c.B));
        return max - min;
    }
}
