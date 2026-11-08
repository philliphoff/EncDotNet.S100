using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class DialogWindowFitTests
{
    [Theory]
    [InlineData(1400, 1304)]  // room to spare: window − 96
    [InlineData(1016, 920)]   // exactly the wizard's width
    [InlineData(800, 704)]    // the main window's minimum
    [InlineData(700, 640)]    // never below the minimum
    public void Width_is_capped_at_the_window_less_its_sides_but_not_below_640(double window, double expected) =>
        Assert.Equal(expected, DialogWindowFit.MaxWidthFor(window));

    [Theory]
    [InlineData(900, 852)]
    [InlineData(30, 0)]
    public void Height_is_capped_at_the_window_less_its_margin(double window, double expected) =>
        Assert.Equal(expected, DialogWindowFit.MaxHeightFor(window));
}
