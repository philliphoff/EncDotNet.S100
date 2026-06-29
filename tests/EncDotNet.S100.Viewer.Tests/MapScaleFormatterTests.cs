using EncDotNet.S100.Viewer;

namespace EncDotNet.S100.Viewer.Tests;

public class MapScaleFormatterTests
{
    [Fact]
    public void Format_InvalidResolution_ReturnsPlaceholder()
    {
        Assert.Equal(MapScaleFormatter.Placeholder, MapScaleFormatter.Format(0, 0));
        Assert.Equal(MapScaleFormatter.Placeholder, MapScaleFormatter.Format(double.NaN, 0));
        Assert.Equal(MapScaleFormatter.Placeholder, MapScaleFormatter.Format(-5, 0));
    }

    [Fact]
    public void Format_AtEquator_ProducesScaleDenominator()
    {
        // At the equator (centerY = 0) there is no mercator distortion, so the
        // denominator is resolution / 0.00028. A resolution of ~50.4 m/px gives
        // 50.4 / 0.00028 = 180 000.
        var text = MapScaleFormatter.Format(50.4, 0.0);
        Assert.StartsWith("1:", text);
        Assert.Equal("1:180\u00A0000", text);
    }

    [Fact]
    public void Format_RoundsToThreeSignificantFigures()
    {
        var text = MapScaleFormatter.Format(50.4 * 1.234, 0.0);
        Assert.Equal("1:222\u00A0000", text);
    }
}
