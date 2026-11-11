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

    [Theory]
    [InlineData(0.0, 50000)]
    [InlineData(-32.383, 50000)]
    [InlineData(60.0, 20000)]
    public void ScaleDenominatorToResolution_RoundTripsThroughStatusBarConversion(double latitude, double scale)
    {
        var resolution = MapScaleFormatter.ScaleDenominatorToResolution(scale, latitude);

        Assert.Equal(scale, MapScaleFormatter.ResolutionToScaleDenominator(resolution, latitude)!.Value, 6);
    }

    [Fact]
    public void ScaleDenominatorToResolution_CorrectsForMercatorStretch()
    {
        // At 60° web-mercator stretches ground distance by 1/cos(60°) = 2.
        var resolution = MapScaleFormatter.ScaleDenominatorToResolution(50000, 60.0);

        Assert.Equal(50000 * MapScaleFormatter.PixelSizeMeters * 2, resolution, 6);
    }

    [Fact]
    public void ScaleDenominatorToResolution_ReadsBackInFormat()
    {
        var latitude = 50.0;
        var (_, mercatorY) = Mapsui.Projections.SphericalMercator.FromLonLat(0, latitude);
        var resolution = MapScaleFormatter.ScaleDenominatorToResolution(50000, latitude);

        Assert.Equal("1:50\u00A0000", MapScaleFormatter.Format(resolution, mercatorY));
    }

    [Fact]
    public void ResolutionToScaleDenominator_InvalidResolution_ReturnsNull()
    {
        Assert.Null(MapScaleFormatter.ResolutionToScaleDenominator(0, 0));
        Assert.Null(MapScaleFormatter.ResolutionToScaleDenominator(double.NaN, 0));
    }
}
