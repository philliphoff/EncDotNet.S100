using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The protective-marking chip (#1236, decision 3) as drawn: a marking above
/// UNCLASSIFIED is solid ink with its text in the background colour, and
/// UNCLASSIFIED is outlined and muted. The Library shows it on rows (#1370).
/// </summary>
public sealed class AmlChipsViewTests
{
    [AvaloniaTheory]
    [InlineData(ChromeTheme.Light)]
    [InlineData(ChromeTheme.Dark)]
    public void A_marking_above_unclassified_is_readable_on_its_solid_ink(ChromeTheme theme)
    {
        using var host = ViewHost.Show(new AmlMarkingChip { Marking = "restricted" }, width: 300, height: 100, theme: theme);

        var chip = host.Find<Border>(b => b.Name == "PART_Chip");
        var text = host.Find<TextBlock>(t => t.Name == "PART_Text");

        Assert.Equal("RESTRICTED", text.Text);
        AssertReadable(chip, text);
    }

    /// <summary>
    /// The chip's text is opaque and stands out from its fill: a transparent
    /// brush (an unset theme colour) would leave a blank ink box.
    /// </summary>
    internal static void AssertReadable(Border chip, TextBlock text)
    {
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(chip.Background).Color;
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color;
        Assert.Equal(255, background.A);
        Assert.Equal(255, foreground.A);
        Assert.True(Math.Abs(Luminance(background) - Luminance(foreground)) > 0.5, $"{foreground} on {background}");
    }

    private static double Luminance(Color c) => ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255;

    [AvaloniaFact]
    public void Unclassified_is_outlined_not_filled()
    {
        using var host = ViewHost.Show(new AmlMarkingChip { Marking = "UNCLASSIFIED" }, width: 300, height: 100);

        var chip = host.Find<Border>(b => b.Name == "PART_Chip");

        Assert.Null(chip.Background);
        Assert.NotNull(host.Find<TextBlock>(t => t.Name == "PART_Text").Foreground);
    }
}
