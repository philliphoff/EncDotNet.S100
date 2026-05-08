using System.Globalization;
using EncDotNet.S100.Pipelines;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class DepthFormattingTests
{
    [Theory]
    [InlineData(0.0, DepthUnit.Metres, 0.0)]
    [InlineData(30.0, DepthUnit.Metres, 30.0)]
    [InlineData(1.0, DepthUnit.Feet, 3.28084)]
    [InlineData(1.0, DepthUnit.Fathoms, 0.546806649168854)]
    public void ToDisplay_ConvertsFromMetres(double m, DepthUnit unit, double expected)
    {
        Assert.Equal(expected, DepthFormatting.ToDisplay(m, unit), 4);
    }

    [Theory]
    [InlineData(DepthUnit.Metres)]
    [InlineData(DepthUnit.Feet)]
    [InlineData(DepthUnit.Fathoms)]
    [InlineData(DepthUnit.FathomsFeet)]
    public void RoundTrip_ToDisplayThenToMetres(DepthUnit unit)
    {
        const double original = 30.5;
        double display = DepthFormatting.ToDisplay(original, unit);
        double back = DepthFormatting.ToMetres(display, unit);
        Assert.Equal(original, back, 6);
    }

    [Fact]
    public void Format_FathomsFeet_SplitsIntoFathomsAndFeet()
    {
        // 32 ft = 5 fathoms 2 feet
        double metres = 32.0 / DepthFormatting.FeetPerMetre;
        var formatted = DepthFormatting.Format(metres, DepthUnit.FathomsFeet);
        Assert.Equal("5fm 2ft", formatted);
    }

    [Fact]
    public void Format_Metres_UsesInvariantCulture()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // uses comma as decimal separator
            var formatted = DepthFormatting.Format(30.5, DepthUnit.Metres);
            Assert.Equal("30.5 m", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }

    [Theory]
    [InlineData("30", DepthUnit.Metres, 30.0)]
    [InlineData("30.5 m", DepthUnit.Metres, 30.5)]
    [InlineData("100ft", DepthUnit.Metres, 30.4800)] // unit suffix overrides active unit
    [InlineData("100", DepthUnit.Feet, 30.4800)]
    [InlineData("5fm 2ft", DepthUnit.FathomsFeet, 9.7536)]
    [InlineData("5 fm", DepthUnit.FathomsFeet, 9.144)]
    [InlineData("12 ft", DepthUnit.FathomsFeet, 3.6576)]
    public void TryParse_ParsesSupportedForms(string text, DepthUnit unit, double expectedMetres)
    {
        Assert.True(DepthFormatting.TryParse(text, unit, out var m));
        Assert.Equal(expectedMetres, m, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a number")]
    [InlineData("abc fm")]
    public void TryParse_ReturnsFalseOnInvalid(string text)
    {
        Assert.False(DepthFormatting.TryParse(text, DepthUnit.Metres, out _));
    }

    [Fact]
    public void TryParse_NegativeFathomsFeet()
    {
        Assert.True(DepthFormatting.TryParse("-5fm 2ft", DepthUnit.FathomsFeet, out var m));
        Assert.Equal(-9.7536, m, 3);
    }

    [Theory]
    [InlineData(DepthUnit.Metres, "m")]
    [InlineData(DepthUnit.Feet, "ft")]
    [InlineData(DepthUnit.Fathoms, "fm")]
    [InlineData(DepthUnit.FathomsFeet, "fm/ft")]
    public void UnitAbbreviation_KnownUnits(DepthUnit unit, string expected)
    {
        Assert.Equal(expected, DepthFormatting.UnitAbbreviation(unit));
    }
}

public class MarinerSettingsDefaultsTests
{
    [Fact]
    public void Default_MatchesS101PortrayalCatalogueDefaults()
    {
        // Source: src/EncDotNet.S100.Specifications/content/S101/pc/portrayal_catalogue.xml
        // Lines ~9681-9802 declare these context parameter defaults.
        var d = MarinerSettings.Default;
        Assert.Equal(30.0, d.SafetyContour);
        Assert.Equal(30.0, d.SafetyDepth);
        Assert.Equal(2.0, d.ShallowContour);
        Assert.Equal(30.0, d.DeepContour);
        Assert.False(d.FourShades);
        Assert.True(d.ShallowWaterDangers);
        Assert.True(d.PlainBoundaries);
        Assert.False(d.SimplifiedSymbols);
        Assert.False(d.FullLightLines);
        Assert.False(d.RadarOverlay);
        Assert.False(d.IgnoreScaleMinimum);
        Assert.Equal(DepthUnit.Metres, d.DepthUnit);
        Assert.Equal("", d.NationalLanguage);
    }
}
