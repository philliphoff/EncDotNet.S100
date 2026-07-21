using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Core.Tests;

public class VerticalDatumsTests
{
    [Theory]
    [InlineData(3, "Mean Sea Level")]
    [InlineData(10, "Approximate Lowest Astronomical Tide")]
    [InlineData(23, "Lowest Astronomical Tide")]
    [InlineData(30, "Highest Astronomical Tide")]
    [InlineData(49, "Hydrographic Zero")]
    public void GetLabel_KnownCode_ReturnsRegisterLabel(int code, string expected)
    {
        Assert.Equal(expected, VerticalDatums.GetLabel(code));
    }

    [Fact]
    public void GetLabel_NullCode_ReturnsUnknown()
    {
        Assert.Equal("Unknown", VerticalDatums.GetLabel(null));
    }

    [Fact]
    public void GetLabel_UnrecognisedCode_ReturnsUnknownWithCode()
    {
        // 42 is intentionally absent from the S-100 register.
        Assert.Equal("Unknown (code 42)", VerticalDatums.GetLabel(42));
    }

    [Fact]
    public void TryGetLabel_KnownCode_ReturnsTrueAndLabel()
    {
        Assert.True(VerticalDatums.TryGetLabel(23, out var label));
        Assert.Equal("Lowest Astronomical Tide", label);
    }

    [Fact]
    public void TryGetLabel_UnknownCode_ReturnsFalse()
    {
        Assert.False(VerticalDatums.TryGetLabel(999, out _));
    }

    [Fact]
    public void Enum_CodesMatch_RegisterLabels()
    {
        Assert.Equal(
            "Approximate Lowest Astronomical Tide",
            VerticalDatums.GetLabel((int)VerticalDatum.ApproximateLowestAstronomicalTide));
        Assert.Equal(
            "Lowest Astronomical Tide",
            VerticalDatums.GetLabel((int)VerticalDatum.LowestAstronomicalTide));
    }
}
