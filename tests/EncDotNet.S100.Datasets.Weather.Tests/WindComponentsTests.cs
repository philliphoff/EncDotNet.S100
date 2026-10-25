using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather.Tests;

public class WindComponentsTests
{
    [Theory]
    // A wind blowing south (v < 0) comes from the north.
    [InlineData(0, -5, 0)]
    // Blowing west (u < 0) comes from the east.
    [InlineData(-5, 0, 90)]
    // Blowing north comes from the south.
    [InlineData(0, 5, 180)]
    // Blowing east comes from the west.
    [InlineData(5, 0, 270)]
    // Blowing south-west comes from the north-east.
    [InlineData(-1, -1, 45)]
    // Blowing north-east comes from the south-west.
    [InlineData(1, 1, 225)]
    // Blowing south-east comes from the north-west.
    [InlineData(1, -1, 315)]
    // Calm.
    [InlineData(0, 0, 0)]
    public void From_direction_is_meteorological_degrees_true(double u, double v, double expected)
    {
        var direction = WindComponents.GetFromDirection(Speed.FromMetresPerSecond(u), Speed.FromMetresPerSecond(v));

        Assert.Equal(expected, direction.TotalDegrees, 9);
    }

    [Fact]
    public void Direction_just_west_of_north_stays_below_360()
    {
        var direction = WindComponents.GetFromDirection(Speed.FromMetresPerSecond(1e-9), Speed.FromMetresPerSecond(-10));

        Assert.InRange(direction.TotalDegrees, 0, 360);
        Assert.True(direction.TotalDegrees < 360);
        Assert.True(WindComponents.FromDirectionCell([1e-7f, -10f]) < 360f);
    }

    [Fact]
    public void Speed_is_the_vector_magnitude()
    {
        var speed = WindComponents.GetSpeed(Speed.FromMetresPerSecond(3), Speed.FromMetresPerSecond(-4));

        Assert.Equal(5, speed.TotalMetresPerSecond, 9);
    }
}
