using System;
using EncDotNet.S100.Quantities;
using Xunit;

namespace EncDotNet.S100.Core.Tests;

public class SpeedTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Zero_equals_default()
    {
        Assert.Equal(default, Speed.Zero);
        Assert.Equal(0.0, Speed.Zero.TotalMetresPerSecond);
    }

    [Fact]
    public void FromKnots_uses_nautical_mile_per_hour()
    {
        // 1 knot = 1852 m / 3600 s.
        Assert.Equal(1852.0 / 3600.0, Speed.FromKnots(1.0).TotalMetresPerSecond, Tolerance);
        Assert.Equal(10.0, Speed.FromKnots(10.0).TotalKnots, Tolerance);
    }

    [Fact]
    public void FromKilometresPerHour_converts()
    {
        Assert.Equal(1000.0 / 3600.0, Speed.FromKilometresPerHour(1.0).TotalMetresPerSecond, Tolerance);
        Assert.Equal(36.0, Speed.FromMetresPerSecond(10.0).TotalKilometresPerHour, Tolerance);
    }

    [Fact]
    public void Cross_unit_round_trip_knots_to_ms()
    {
        // A feed reporting 20 kn and one reporting the same in m/s must agree.
        var fromKnots = Speed.FromKnots(20.0);
        var fromMs = Speed.FromMetresPerSecond(fromKnots.TotalMetresPerSecond);
        Assert.Equal(20.0, fromMs.TotalKnots, Tolerance);
    }

    [Fact]
    public void DistanceOver_multiplies_by_time()
    {
        var speed = Speed.FromMetresPerSecond(3.0);
        Length distance = speed.DistanceOver(TimeSpan.FromSeconds(10));
        Assert.Equal(30.0, distance.TotalMetres, Tolerance);
    }

    [Fact]
    public void Abs_returns_magnitude()
    {
        Assert.Equal(5.0, Speed.FromMetresPerSecond(-5.0).Abs().TotalMetresPerSecond, Tolerance);
    }

    [Fact]
    public void Arithmetic_operators_combine_speeds()
    {
        var a = Speed.FromMetresPerSecond(10.0);
        var b = Speed.FromMetresPerSecond(4.0);

        Assert.Equal(14.0, (a + b).TotalMetresPerSecond, Tolerance);
        Assert.Equal(6.0, (a - b).TotalMetresPerSecond, Tolerance);
        Assert.Equal(-10.0, (-a).TotalMetresPerSecond, Tolerance);
        Assert.Equal(20.0, (a * 2.0).TotalMetresPerSecond, Tolerance);
        Assert.Equal(20.0, (2.0 * a).TotalMetresPerSecond, Tolerance);
        Assert.Equal(5.0, (a / 2.0).TotalMetresPerSecond, Tolerance);
        Assert.Equal(2.5, a / b, Tolerance);
    }

    [Fact]
    public void Comparison_operators_order_by_speed()
    {
        var slow = Speed.FromKnots(5.0);
        var fast = Speed.FromKnots(20.0);

        Assert.True(slow < fast);
        Assert.True(fast > slow);
        Assert.True(slow <= Speed.FromKnots(5.0));
        Assert.True(fast >= Speed.FromKnots(20.0));
        Assert.True(slow.CompareTo(fast) < 0);
    }

    [Fact]
    public void Value_equality_holds_for_equal_ms()
    {
        Assert.Equal(Speed.FromKnots(1.0), Speed.FromMetresPerSecond(1852.0 / 3600.0));
    }

    [Fact]
    public void ToString_is_invariant_metres_per_second()
    {
        Assert.Equal("5 m/s", Speed.FromMetresPerSecond(5.0).ToString());
    }
}
