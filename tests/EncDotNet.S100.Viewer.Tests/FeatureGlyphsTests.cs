using EncDotNet.S100.Viewer.Services;
using Xunit;
using Icon = FluentIcons.Common.Icon;

namespace EncDotNet.S100.Viewer.Tests;

public class FeatureGlyphsTests
{
    [Theory]
    [InlineData("BeaconLateral", Icon.Flag)]
    [InlineData("LightAllAround", Icon.WeatherSunny)]
    [InlineData("Lighthouse", Icon.BuildingLighthouse)]
    [InlineData("BuoyLateral", Icon.MyLocation)]
    [InlineData("DepthArea", Icon.Water)]
    [InlineData("Soundings", Icon.Water)]
    [InlineData("Fairway", Icon.Channel)]
    [InlineData("VehicleShip", Icon.VehicleShip)]
    public void ForFeatureType_KnownCategories_MapToExpectedGlyph(string code, Icon expected)
    {
        Assert.Equal(expected, FeatureGlyphs.ForFeatureType(code));
    }

    [Fact]
    public void ForFeatureType_LighthouseWinsOverLight()
    {
        // "Lighthouse" contains "light"; the more specific keyword must win.
        Assert.Equal(Icon.BuildingLighthouse, FeatureGlyphs.ForFeatureType("Lighthouse"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomethingUnmapped")]
    public void ForFeatureType_UnknownOrEmpty_ReturnsFallback(string? code)
    {
        Assert.Equal(FeatureGlyphs.Fallback, FeatureGlyphs.ForFeatureType(code));
    }

    [Fact]
    public void ForFeatureType_IsCaseInsensitive()
    {
        Assert.Equal(Icon.Flag, FeatureGlyphs.ForFeatureType("beaconlateral"));
    }
}
