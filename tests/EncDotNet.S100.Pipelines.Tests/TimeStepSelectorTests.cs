using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The renderer-neutral time rule (#1676) that the viewer's map and
/// <c>s100 tiles serve</c> share: which step a dataset draws at a time, by
/// its <see cref="TimeSemantics"/>.
/// </summary>
public sealed class TimeStepSelectorTests
{
    private static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double hours) => Origin.AddHours(hours);

    // Hourly steps 00:00–03:00, a gap, then 10:00–12:00: a typical step of an hour.
    private static readonly DateTime[] Steps = [At(0), At(1), At(2), At(3), At(10), At(11), At(12)];

    private static readonly TimeSpan Any = TimeSpan.MaxValue;

    [Fact]
    public void Nearest_draws_the_nearest_step_within_the_tolerance_either_side()
    {
        var hour = TimeSpan.FromHours(1);

        Assert.Equal(At(1), TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, hour, At(1.4)));
        Assert.Equal(At(2), TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, hour, At(1.6)));

        // Before the first step and after the last, within the tolerance.
        Assert.Equal(At(0), TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, hour, At(-1)));
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, hour, At(-1.1)));
        Assert.Equal(At(12), TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, hour, At(13)));
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, hour, At(13.1)));

        // In the gap.
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, hour, At(6)));

        // With any tolerance, the nearest step however far.
        Assert.Equal(At(0), TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, Any, At(-48)));
        Assert.Equal(At(12), TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, Any, At(24 * 365)));
        Assert.Equal(At(3), TimeStepSelector.Select(Steps, TimeStepSelection.Nearest, Any, At(6)));
    }

    [Fact]
    public void At_or_before_never_draws_a_later_step()
    {
        var hour = TimeSpan.FromHours(1);

        Assert.Equal(At(1), TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(1)));
        Assert.Equal(At(1), TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(1.9)));
        Assert.Equal(At(3), TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(4)));
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(4.1)));
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(9.9)));

        // Before the first step, nothing, whatever the tolerance.
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(-0.1)));
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, Any, At(-0.1)));

        // After the last, held for the tolerance, or however long.
        Assert.Equal(At(12), TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(13)));
        Assert.Null(TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, hour, At(13.1)));
        Assert.Equal(At(12), TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, Any, At(24 * 365)));
        Assert.Equal(At(3), TimeStepSelector.Select(Steps, TimeStepSelection.AtOrBefore, Any, At(9.9)));
    }

    [Fact]
    public void Continuous_draws_the_time_itself_to_the_minute_before_and_after_the_steps()
    {
        var time = At(5).AddMinutes(17).AddSeconds(42);

        Assert.Equal(At(5).AddMinutes(17), TimeStepSelector.Select(Steps, TimeStepSelection.Continuous, TimeSpan.Zero, time));
        Assert.Equal(At(-30), TimeStepSelector.Select(Steps, TimeStepSelection.Continuous, TimeSpan.Zero, At(-30)));
        Assert.Equal(At(100), TimeStepSelector.Select(Steps, TimeStepSelection.Continuous, TimeSpan.Zero, At(100)));
        Assert.Equal(DateTimeKind.Utc, TimeStepSelector.Select(Steps, TimeStepSelection.Continuous, TimeSpan.Zero, time)!.Value.Kind);
        Assert.Null(TimeStepSelector.Select([], TimeStepSelection.Continuous, TimeSpan.Zero, time));
    }

    [Fact]
    public void The_tolerance_is_the_validity_or_the_typical_step()
    {
        Assert.Equal(TimeSpan.FromHours(1), TimeStepSelector.ToleranceOf(TimeSemantics.Default, Steps));
        Assert.Equal(TimeSpan.FromHours(1), TimeStepSelector.ToleranceOf(TimeSemantics.Default, [At(0)]));
        Assert.Equal(TimeSpan.FromDays(14), TimeStepSelector.ToleranceOf(S411DatasetProcessor.SeaIceTimeSemantics, Steps));

        // A continuous dataset's is how long before its first step it draws.
        var cyclone = new TimeSemantics(TimeStepKind.Forecast, TimeStepSelection.Continuous, TimeSpan.FromDays(3));
        Assert.Equal(TimeSpan.FromDays(3), TimeStepSelector.ToleranceOf(cyclone, Steps));
        Assert.Equal(TimeSpan.Zero, TimeStepSelector.ToleranceOf(cyclone with { Validity = null }, Steps));
    }

    [Fact]
    public void The_range_follows_the_selection()
    {
        Assert.Equal((At(-1), At(13)), TimeStepSelector.RangeOf(TimeSemantics.Default, Steps));
        Assert.Equal((At(0), At(13)), TimeStepSelector.RangeOf(S104DatasetProcessor.WaterLevelTimeSemantics, Steps));
        var cyclone = new TimeSemantics(TimeStepKind.Forecast, TimeStepSelection.Continuous, TimeSpan.FromDays(3));
        Assert.Equal((At(-72), At(12)), TimeStepSelector.RangeOf(cyclone, Steps));
        Assert.Null(TimeStepSelector.RangeOf(TimeSemantics.Default, []));
    }

    [Fact]
    public void The_viewer_selection_is_the_same_rule()
    {
        var hour = TimeSpan.FromHours(1);
        foreach (var kind in Enum.GetValues<MapsuiTimeSelectionKind>())
        {
            Assert.Equal(kind, MapsuiTimeSelection.FromSelection(MapsuiTimeSelection.ToSelection(kind)));
            for (double hours = -2; hours <= 14; hours += 0.25)
            {
                Assert.Equal(
                    TimeStepSelector.Select(Steps, MapsuiTimeSelection.ToSelection(kind), hour, At(hours)),
                    MapsuiTimeSelection.Select(Steps, kind, hour, At(hours)));
            }
        }
    }
}
