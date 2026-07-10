using EncDotNet.S100.Quantities;
using Xunit;

namespace EncDotNet.S100.Core.Tests;

public class LengthTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Zero_equals_default()
    {
        Assert.Equal(default, Length.Zero);
        Assert.Equal(0.0, Length.Zero.TotalMetres);
    }

    [Fact]
    public void FromMetres_round_trips_metres()
    {
        var length = Length.FromMetres(42.5);
        Assert.Equal(42.5, length.TotalMetres, Tolerance);
    }

    [Theory]
    [InlineData(1.0, 0.3048)]        // 1 ft = 0.3048 m exactly
    [InlineData(100.0, 30.48)]
    [InlineData(-5.0, -1.524)]
    public void FromFeet_uses_exact_international_foot(double feet, double expectedMetres)
    {
        Assert.Equal(expectedMetres, Length.FromFeet(feet).TotalMetres, Tolerance);
    }

    [Theory]
    [InlineData(1.0, 1.8288)]        // 1 fathom = 1.8288 m exactly
    [InlineData(5.0, 9.144)]
    public void FromFathoms_uses_exact_fathom(double fathoms, double expectedMetres)
    {
        Assert.Equal(expectedMetres, Length.FromFathoms(fathoms).TotalMetres, Tolerance);
    }

    [Fact]
    public void FromNauticalMiles_and_kilometres_convert()
    {
        Assert.Equal(1852.0, Length.FromNauticalMiles(1).TotalMetres, Tolerance);
        Assert.Equal(1000.0, Length.FromKilometres(1).TotalMetres, Tolerance);
    }

    [Fact]
    public void Conversion_properties_are_inverse_of_factories()
    {
        var length = Length.FromMetres(12.0);
        Assert.Equal(12.0 / 0.3048, length.TotalFeet, Tolerance);
        Assert.Equal(12.0 / 1.8288, length.TotalFathoms, Tolerance);
        Assert.Equal(12.0 / 1852.0, length.TotalNauticalMiles, Tolerance);
        Assert.Equal(0.012, length.TotalKilometres, Tolerance);
    }

    [Fact]
    public void Abs_returns_magnitude()
    {
        Assert.Equal(3.0, Length.FromMetres(-3.0).Abs().TotalMetres, Tolerance);
        Assert.Equal(3.0, Length.FromMetres(3.0).Abs().TotalMetres, Tolerance);
    }

    [Fact]
    public void Arithmetic_operators_combine_lengths()
    {
        var a = Length.FromMetres(10.0);
        var b = Length.FromMetres(4.0);

        Assert.Equal(14.0, (a + b).TotalMetres, Tolerance);
        Assert.Equal(6.0, (a - b).TotalMetres, Tolerance);
        Assert.Equal(-10.0, (-a).TotalMetres, Tolerance);
        Assert.Equal(20.0, (a * 2.0).TotalMetres, Tolerance);
        Assert.Equal(20.0, (2.0 * a).TotalMetres, Tolerance);
        Assert.Equal(5.0, (a / 2.0).TotalMetres, Tolerance);
        Assert.Equal(2.5, a / b, Tolerance);
    }

    [Fact]
    public void Comparison_operators_order_by_magnitude()
    {
        var shorter = Length.FromMetres(1.0);
        var longer = Length.FromMetres(2.0);

        Assert.True(shorter < longer);
        Assert.True(longer > shorter);
        Assert.True(shorter <= Length.FromMetres(1.0));
        Assert.True(longer >= Length.FromMetres(2.0));
        Assert.True(shorter.CompareTo(longer) < 0);
    }

    [Fact]
    public void Value_equality_holds_for_equal_metres()
    {
        Assert.Equal(Length.FromFeet(100.0), Length.FromMetres(30.48));
    }

    [Fact]
    public void ToString_is_invariant_metres()
    {
        Assert.Equal("12.5 m", Length.FromMetres(12.5).ToString());
    }
}
