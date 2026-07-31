using System.Globalization;

namespace EncDotNet.S100.Viewer.Tests;

public class WidthConverterTests
{
    private static object? Inset(object? value, object? parameter) =>
        WidthInsetConverter.Instance.Convert(value, typeof(double), parameter, CultureInfo.InvariantCulture);

    private static object? Proportional(object? value, object? parameter) =>
        ProportionalWidthConverter.Instance.Convert(value, typeof(double), parameter, CultureInfo.InvariantCulture);

    [Fact]
    public void Inset_SubtractsParameter()
    {
        Assert.Equal(336d, Inset(360d, "24"));
    }

    [Fact]
    public void Inset_ClampsToZero()
    {
        Assert.Equal(0d, Inset(10d, "24"));
    }

    [Fact]
    public void Inset_NaNWidth_ReturnsNaN()
    {
        Assert.Equal(double.NaN, Inset(double.NaN, "24"));
    }

    [Fact]
    public void Inset_NonWidth_ReturnsNaN()
    {
        Assert.Equal(double.NaN, Inset(null, "24"));
    }

    [Fact]
    public void Proportional_MultipliesByFraction()
    {
        Assert.Equal(200d, Proportional(400d, "0.5"));
    }

    [Fact]
    public void Proportional_InfinityWidth_ReturnsNaN()
    {
        Assert.Equal(double.NaN, Proportional(double.PositiveInfinity, "0.55"));
    }
}
