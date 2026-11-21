using EncDotNet.S100.Cli.Commands;

namespace EncDotNet.S100.Cli.Tests;

/// <summary><c>s100 mcp serve --current-station-reach</c> (#1545).</summary>
public sealed class McpServeCurrentStationReachTests
{
    [Fact]
    public void A_positive_reach_with_predictions_is_accepted() =>
        Assert.True(new McpServeCommand.Settings { TidePredictions = true, CurrentStationReach = 1_500 }.Validate().Successful);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_reach_that_is_not_a_positive_distance_is_rejected(double reach) =>
        Assert.False(new McpServeCommand.Settings { TidePredictions = true, CurrentStationReach = reach }.Validate().Successful);

    [Fact]
    public void A_reach_without_predictions_is_rejected() =>
        Assert.False(new McpServeCommand.Settings { CurrentStationReach = 1_500 }.Validate().Successful);
}
