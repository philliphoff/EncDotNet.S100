using Avalonia.Controls;
using Avalonia.Media;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The status bar's MARKING pill as drawn (#1371, decision 10): above
/// UNCLASSIFIED it's solid ink and its marking stays readable; UNCLASSIFIED
/// is outlined.
/// </summary>
public sealed class AmlMarkingIndicatorViewTests
{
    [AvaloniaTheory]
    [InlineData(ChromeTheme.Light)]
    [InlineData(ChromeTheme.Dark)]
    public void A_marking_above_unclassified_is_readable_on_its_solid_ink(ChromeTheme theme)
    {
        var pill = new AmlMarkingIndicatorViewModel();
        pill.Update([new AmlLoadedSet("AML trial set", ["RESTRICTED"])]);
        using var host = ViewHost.Show(new AmlMarkingIndicatorView { DataContext = pill }, width: 400, height: 60, theme: theme);

        var button = host.Find<Button>(b => b.Name == "AmlMarkingButton");
        var value = host.Find<TextBlock>(t => t.Text == "RESTRICTED");

        var background = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(value.Foreground).Color;
        Assert.Equal(255, background.A);
        Assert.Equal(255, foreground.A);
        Assert.True(Math.Abs(Luminance(background) - Luminance(foreground)) > 0.5, $"{foreground} on {background}");
    }

    [AvaloniaFact]
    public void Unclassified_is_outlined_not_filled()
    {
        var pill = new AmlMarkingIndicatorViewModel();
        pill.Update([new AmlLoadedSet("UKHO", ["UNCLASSIFIED"])]);
        using var host = ViewHost.Show(new AmlMarkingIndicatorView { DataContext = pill }, width: 400, height: 60);

        var button = host.Find<Button>(b => b.Name == "AmlMarkingButton");

        Assert.True(button.Background is null || (button.Background is ISolidColorBrush { Color.A: 0 }));
    }

    private static double Luminance(Color c) => ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255;
}
