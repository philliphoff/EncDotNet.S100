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
    public void Format_DegreesAreZeroPadded()
    {
        var text = LatLonFormatter.Format(1.0, 2.0);
        Assert.Equal("01°00.000'N  002°00.000'E", text);
    }

    [Fact]
    public void Format_MaxDegreesFitWithoutTruncation()
    {
        var text = LatLonFormatter.Format(89.0, 179.0);
        Assert.Equal("89°00.000'N  179°00.000'E", text);
    }

    [Fact]
    public void Format_RoundsMinutesToThreeDecimals()
    {
        // 0.5° → 30.0000 minutes; ensure no trailing rounding artifact.
        var text = LatLonFormatter.Format(45.5, 90.5);
        Assert.Equal("45°30.000'N  090°30.000'E", text);
    }

    [Fact]
    public void Placeholder_IsEmpty()
    {
        Assert.Empty(LatLonFormatter.Placeholder);
    }

    [Fact]
    public void FormatDecimal_RendersSignedDecimalDegrees()
    {
        var text = LatLonFormatter.FormatDecimal(47.601234, -122.334567);
        Assert.Equal("47.601234, -122.334567", text);
    }

    [Fact]
    public void FormatDecimal_PadsToSixDecimalPlaces()
    {
        var text = LatLonFormatter.FormatDecimal(0.0, 5.5);
        Assert.Equal("0.000000, 5.500000", text);
    }

    [Theory]
    [InlineData("50°46.024'N  001°15.558'W", 50.767067, -1.2593)]
    [InlineData("50°46,024'N 001°15,558'W", 50.767067, -1.2593)]
    [InlineData("50 46.024 N 1 15.558 W", 50.767067, -1.2593)]
    [InlineData("N50°46.024' W001°15.558'", 50.767067, -1.2593)]
    [InlineData("001°15.558'W 50°46.024'N", 50.767067, -1.2593)]
    [InlineData("50°46'01.44\"N 1°15'33.48\"W", 50.767067, -1.2593)]
    [InlineData("50.767067, -1.2593", 50.767067, -1.2593)]
    [InlineData("50.767067 -1.2593", 50.767067, -1.2593)]
    [InlineData("12°34.562'S  056°12.345'E", -12.576033, 56.20575)]
    public void TryParse_accepts_the_app_format_and_common_variants(string text, double lat, double lon)
    {
        Assert.True(LatLonFormatter.TryParse(text, out var parsedLat, out var parsedLon));
        Assert.Equal(lat, parsedLat, 5);
        Assert.Equal(lon, parsedLon, 5);
    }

    [Theory]
    [InlineData(50.767067, -1.2593)]
    [InlineData(-33.8568, 151.2153)]
    [InlineData(0.0, 0.0)]
    [InlineData(89.999, -179.999)]
    public void TryParse_round_trips_Format(double lat, double lon)
    {
        Assert.True(LatLonFormatter.TryParse(LatLonFormatter.Format(lat, lon), out var parsedLat, out var parsedLon));
        Assert.Equal(lat, parsedLat, 4);
        Assert.Equal(lon, parsedLon, 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello")]
    [InlineData("50°46.024'N")]
    [InlineData("91°00.000'N 001°00.000'W")]
    [InlineData("50°00.000'N 181°00.000'W")]
    [InlineData("50°61.000'N 001°00.000'W")]
    [InlineData("50°46.024'E 001°15.558'W")]
    [InlineData("50.5 30 N 1 15 W")]
    [InlineData("-50°46.024'N 001°15.558'W")]
    [InlineData("50 1 2 3 4 5 6")]
    public void TryParse_rejects_text_that_is_not_one_position(string text)
    {
        Assert.False(LatLonFormatter.TryParse(text, out _, out _));
    }
}
