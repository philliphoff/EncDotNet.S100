using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Core.Tests;

public class DepthTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Zero_equals_default()
    {
        Assert.Equal(default, Depth.Zero);
        Assert.Equal(0.0, Depth.Zero.TotalMetres);
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(-3.5)]  // drying height above datum keeps its sign
    public void FromMetres_round_trips_and_preserves_sign(double metres)
    {
        Assert.Equal(metres, Depth.FromMetres(metres).TotalMetres, Tolerance);
    }

    [Fact]
    public void FromFeet_and_FromFathoms_use_exact_factors()
    {
        Assert.Equal(30.48, Depth.FromFeet(100.0).TotalMetres, Tolerance);
        Assert.Equal(9.144, Depth.FromFathoms(5.0).TotalMetres, Tolerance);
    }

    [Fact]
    public void Conversion_properties_match_length()
    {
        var depth = Depth.FromMetres(9.144);
        Assert.Equal(30.0, depth.TotalFeet, Tolerance);
        Assert.Equal(5.0, depth.TotalFathoms, Tolerance);
    }

    [Fact]
    public void AsLength_and_implicit_conversion_yield_same_metres()
    {
        var depth = Depth.FromMetres(15.0);
        Length viaMethod = depth.AsLength();
        Length viaImplicit = depth;

        Assert.Equal(15.0, viaMethod.TotalMetres, Tolerance);
        Assert.Equal(15.0, viaImplicit.TotalMetres, Tolerance);
    }

    [Fact]
    public void FromLength_wraps_existing_length()
    {
        var depth = Depth.FromLength(Length.FromFeet(6.0));
        Assert.Equal(1.8288, depth.TotalMetres, Tolerance);
    }

    [Fact]
    public void Abs_returns_magnitude()
    {
        Assert.Equal(3.5, Depth.FromMetres(-3.5).Abs().TotalMetres, Tolerance);
    }

    [Fact]
    public void Comparison_operators_order_by_depth()
    {
        var shallow = Depth.FromMetres(2.0);
        var deep = Depth.FromMetres(20.0);

        Assert.True(shallow < deep);
        Assert.True(deep > shallow);
        Assert.True(shallow <= Depth.FromMetres(2.0));
        Assert.True(deep >= Depth.FromMetres(20.0));
        Assert.True(shallow.CompareTo(deep) < 0);
    }

    [Fact]
    public void Value_equality_holds_for_equal_metres()
    {
        Assert.Equal(Depth.FromFathoms(5.0), Depth.FromMetres(9.144));
    }
}
