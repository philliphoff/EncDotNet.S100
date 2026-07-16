using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Core.Tests;

public class AngleTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Zero_equals_default()
    {
        Assert.Equal(default, Angle.Zero);
        Assert.Equal(0.0, Angle.Zero.TotalDegrees);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(180.0, Math.PI)]
    [InlineData(90.0, Math.PI / 2)]
    [InlineData(360.0, 2 * Math.PI)]
    public void Degrees_and_radians_are_consistent(double degrees, double radians)
    {
        Assert.Equal(radians, Angle.FromDegrees(degrees).TotalRadians, Tolerance);
        Assert.Equal(degrees, Angle.FromRadians(radians).TotalDegrees, Tolerance);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(45.0, 45.0)]
    [InlineData(360.0, 0.0)]
    [InlineData(370.0, 10.0)]
    [InlineData(-10.0, 350.0)]
    [InlineData(-370.0, 350.0)]
    [InlineData(720.0, 0.0)]
    public void Normalized_folds_into_zero_to_360(double input, double expected)
    {
        Assert.Equal(expected, Angle.FromDegrees(input).Normalized().TotalDegrees, Tolerance);
    }

    [Fact]
    public void Arithmetic_operators_combine_angles()
    {
        var a = Angle.FromDegrees(90.0);
        var b = Angle.FromDegrees(30.0);

        Assert.Equal(120.0, (a + b).TotalDegrees, Tolerance);
        Assert.Equal(60.0, (a - b).TotalDegrees, Tolerance);
        Assert.Equal(-90.0, (-a).TotalDegrees, Tolerance);
        Assert.Equal(180.0, (a * 2.0).TotalDegrees, Tolerance);
        Assert.Equal(180.0, (2.0 * a).TotalDegrees, Tolerance);
        Assert.Equal(45.0, (a / 2.0).TotalDegrees, Tolerance);
    }

    [Fact]
    public void Comparison_operators_order_by_degrees()
    {
        var small = Angle.FromDegrees(10.0);
        var large = Angle.FromDegrees(20.0);

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= Angle.FromDegrees(10.0));
        Assert.True(large >= Angle.FromDegrees(20.0));
        Assert.True(small.CompareTo(large) < 0);
    }

    [Fact]
    public void Value_equality_holds_for_equal_degrees()
    {
        Assert.Equal(Angle.FromRadians(Math.PI), Angle.FromDegrees(180.0));
    }

    [Fact]
    public void ToString_is_invariant_degrees()
    {
        Assert.Equal("90°", Angle.FromDegrees(90.0).ToString());
    }
}
