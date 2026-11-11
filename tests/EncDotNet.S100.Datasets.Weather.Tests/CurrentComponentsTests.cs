using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather.Tests;

public class CurrentComponentsTests
{
    [Theory]
    // Flowing north sets to the north.
    [InlineData(0, 1, 0)]
    // Flowing east sets to the east.
    [InlineData(1, 0, 90)]
    // Flowing south sets to the south.
    [InlineData(0, -1, 180)]
    // Flowing west sets to the west.
    [InlineData(-1, 0, 270)]
    // Flowing north-east.
    [InlineData(1, 1, 45)]
    // Slack water.
    [InlineData(0, 0, 0)]
    public void To_direction_is_oceanographic_degrees_true(double u, double v, double expected)
    {
        var direction = CurrentComponents.GetToDirection(Speed.FromMetresPerSecond(u), Speed.FromMetresPerSecond(v));

        Assert.Equal(expected, direction.TotalDegrees, 9);
    }

    [Fact]
    public void A_current_sets_the_opposite_way_to_a_wind_with_the_same_components()
    {
        var u = Speed.FromMetresPerSecond(2);
        var v = Speed.FromMetresPerSecond(-1);

        var current = CurrentComponents.GetToDirection(u, v).TotalDegrees;
        var wind = WindComponents.GetFromDirection(u, v).TotalDegrees;

        // The wind comes from where the current goes.
        Assert.Equal((wind + 180) % 360, current, 9);
        Assert.Equal(2.236, CurrentComponents.GetSpeed(u, v).TotalMetresPerSecond, 3);
    }

    [Fact]
    public void Direction_just_west_of_north_stays_below_360()
    {
        Assert.True(CurrentComponents.ToDirectionCell([-1e-7f, 10f]) < 360f);
    }
}
