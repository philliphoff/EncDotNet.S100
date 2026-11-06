using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines.Vector;
using static EncDotNet.S100.TestSupport.CodsusFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1301 (V30, §7): a surface analysis's time steps, which analysis is
/// drawn and hit at a time step, and how a stale one is drawn.
/// </summary>
public sealed class SurfaceAnalysisTimeTests
{
    private static readonly DateTime Analysis = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Forecast12 = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Forecast24 = new(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_analysis_is_one_analysis_step_held_12_hours_stale_after_6_and_shared_with_every_bulletin()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(HighResolution));

        Assert.Equal([Analysis], processor.AvailableTimes);
        Assert.Equal(
            new TimeSemantics(TimeStepKind.Analysis, TimeStepSelection.AtOrBefore, TimeSpan.FromHours(12), TimeSpan.FromHours(6), "surface-analysis"),
            processor.TimeSemantics);
        Assert.Null(processor.ForecastRun);
        Assert.NotNull(processor.IssueTime);
    }

    [Fact]
    public void A_forecast_bulletin_is_forecast_steps_of_a_WPC_run()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(Forecast));

        Assert.Equal([Forecast12, Forecast24], processor.AvailableTimes);
        Assert.Equal(TimeStepKind.Forecast, processor.TimeSemantics.Kind);
        Assert.Equal(SurfaceAnalysisDatasetProcessor.SupersessionKey, processor.TimeSemantics.Supersession);
        Assert.Equal(new DatasetForecastRun("WPC", Forecast12.AddHours(-12)), processor.ForecastRun);
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 1)]
    [InlineData(23.9, 1)]
    [InlineData(36.5, -1)]
    public void The_analysis_at_a_time_is_the_latest_at_or_before_it_within_12_hours(double hoursAfterFirst, int expected)
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(Forecast));

        var drawn = processor.AnalysisAt(Forecast12.AddHours(hoursAfterFirst));

        Assert.Same(expected < 0 ? null : processor.Bulletin.Analyses[expected], drawn);
        Assert.Same(processor.Bulletin.Analyses[0], processor.AnalysisAt(null));
    }

    [Fact]
    public async Task The_time_step_chooses_what_is_drawn_and_hit()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(Forecast));

        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = Forecast24 }, TestContext.Current.CancellationToken);

        Assert.Same(processor.Bulletin.Analyses[1], processor.DrawnAnalysis);
        Assert.All(result.SubLayers[0].Instructions, i => Assert.StartsWith("+24h ", i.FeatureReference, StringComparison.Ordinal));
        var later = processor.EnumerateFeatures().First(f => f.FeatureRef == "+24h COLD 1");
        var line = processor.GetFeatureGeometryAt(later.Ordinal)!.Curves[0];
        var middle = line[line.Count / 2];
        Assert.Contains(processor.HitTestFeatures(middle.Latitude, middle.Longitude, 2_000), h => h.FeatureRef == "+24h COLD 1");
        Assert.Equal(1.0, result.SubLayers[0].Opacity);
    }

    [Fact]
    public async Task A_stale_analysis_is_drawn_at_half_opacity_with_its_age_on_the_centres_and_in_the_pick()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(HighResolution));
        var context = new RenderContext { TimeStep = Analysis, TimeStepAge = TimeSpan.FromHours(9) };

        var stale = await processor.BuildVectorPortrayalAsync(context, TestContext.Current.CancellationToken);

        var layer = Assert.Single(stale.SubLayers);
        Assert.Equal(SurfaceAnalysisStyle.StaleOpacity, layer.Opacity);
        var ages = layer.Instructions.OfType<TextInstruction>().Where(t => t.Text == "9 h old").ToArray();
        Assert.Equal(2 * processor.DrawnAnalysis!.PressureCentres.Count, ages.Length);
        Assert.Equal(TimeSpan.FromHours(9), processor.DrawnAge);
        Assert.Contains("(stale)", stale.Info, StringComparison.Ordinal);

        var low = processor.EnumerateFeatures().First(f => f.FeatureRef == "LOW 1");
        var attributes = processor.GetFeatureInfoAt(low.Ordinal)!.Attributes;
        Assert.True(attributes[0].IsNavigationNotice);
        Assert.True(attributes[1].IsStaleNotice);
        Assert.Equal("This analysis is 9 h old. A newer one is likely.", attributes[1].DisplayValue);

        var current = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = Analysis }, TestContext.Current.CancellationToken);

        Assert.Equal(1.0, current.SubLayers[0].Opacity);
        Assert.DoesNotContain(current.SubLayers[0].Instructions.OfType<TextInstruction>(), t => t.Text.EndsWith(" h old", StringComparison.Ordinal));
        Assert.DoesNotContain(processor.GetFeatureInfoAt(low.Ordinal)!.Attributes, a => a.IsStaleNotice);
        Assert.NotEqual(stale.ContentScope, current.ContentScope);
    }

    [Fact]
    public async Task No_analysis_at_the_time_step_draws_nothing()
    {
        var processor = new SurfaceAnalysisDatasetProcessor(Fixture(HighResolution));

        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = Analysis.AddHours(-1) }, TestContext.Current.CancellationToken);

        Assert.Empty(result.SubLayers[0].Instructions);
        Assert.Null(processor.DrawnAnalysis);
        Assert.Empty(processor.HitTestFeatures(45, -50, 50_000));
    }

    [Fact]
    public void The_age_reads_to_the_nearest_hour()
    {
        Assert.Equal("10 h old", SurfaceAnalysisStyle.FormatAge(TimeSpan.FromHours(9.9)));
        Assert.Equal("9 h old", SurfaceAnalysisStyle.FormatAge(TimeSpan.FromHours(9.4)));
        Assert.Equal("6 h old", SurfaceAnalysisStyle.FormatAge(TimeSpan.FromHours(6)));
    }
}
