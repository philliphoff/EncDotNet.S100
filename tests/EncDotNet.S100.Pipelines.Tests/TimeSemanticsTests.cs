using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The time semantics the time-aware processors report (#1065, design note
/// <c>docs/design/weather-sources.md</c> §8.4), which replace the host's
/// per-product time rules with the same behaviour.
/// </summary>
public sealed class TimeSemanticsTests
{
    [Fact]
    public void S104_water_level_holds_each_forecast_step_for_its_typical_step()
    {
        var semantics = S104DatasetProcessor.WaterLevelTimeSemantics;

        Assert.Equal(TimeStepKind.Forecast, semantics.Kind);
        Assert.Equal(TimeStepSelection.AtOrBefore, semantics.Selection);
        Assert.Null(semantics.Validity);
    }

    [Fact]
    public void S411_ice_charts_are_observations_held_for_fourteen_days()
    {
        var semantics = S411DatasetProcessor.SeaIceTimeSemantics;

        Assert.Equal(TimeStepKind.Observation, semantics.Kind);
        Assert.Equal(TimeStepSelection.AtOrBefore, semantics.Selection);
        Assert.Equal(TimeSpan.FromDays(14), semantics.Validity);
    }

    [Fact]
    public void A_processor_that_does_not_say_snaps_forecasts_to_the_nearest_step()
    {
        ITimeAwareDatasetProcessor processor = new Untold();

        Assert.Equal(new TimeSemantics(TimeStepKind.Forecast, TimeStepSelection.Nearest), processor.TimeSemantics);
        Assert.Null(processor.ForecastRun);
    }

    private sealed class Untold : ITimeAwareDatasetProcessor
    {
        public IReadOnlyList<DateTime> AvailableTimes => [];
    }
}
