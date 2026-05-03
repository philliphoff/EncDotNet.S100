using EncDotNet.S100.Viewer;

namespace EncDotNet.S100.Viewer.Tests;

public class LatLonFormatterTests
{
    [Fact]
    public void Format_NorthEast_UsesCorrectHemispheres()
    {
        // 12.5760278° = 12° 34.5617'
        var text = LatLonFormatter.Format(12.576028, 56.205750);
        Assert.Equal("12°34.562'N  056°12.345'E", text);
    }

    [Fact]
    public void Format_SouthWest_UsesCorrectHemispheres()
    {
        var text = LatLonFormatter.Format(-12.576028, -56.205750);
        Assert.Equal("12°34.562'S  056°12.345'W", text);
    }

    [Fact]
    public void Format_Equator_RendersAsNorthAndEast()
    {
        var text = LatLonFormatter.Format(0.0, 0.0);
        Assert.Equal("00°00.000'N  000°00.000'E", text);
    }

    [Fact]
    public void Format_LatitudePadsToTwoDegreeDigits_LongitudeToThree()
    {
        var text = LatLonFormatter.Format(1.0, 2.0);
        Assert.Equal("01°00.000'N  002°00.000'E", text);
    }

    [Fact]
    public void Format_RoundsMinutesToThreeDecimals()
    {
        // 0.5° → 30.0000 minutes; ensure no trailing rounding artifact.
        var text = LatLonFormatter.Format(45.5, 90.5);
        Assert.Equal("45°30.000'N  090°30.000'E", text);
    }

    [Fact]
    public void Placeholder_IsEmDash()
    {
        Assert.Equal("—", LatLonFormatter.Placeholder);
    }
}
