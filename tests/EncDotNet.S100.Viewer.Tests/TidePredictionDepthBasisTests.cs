using EncDotNet.S100.Collections;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;
using static EncDotNet.S100.Viewer.Tests.DepthBasisEvaluatorTests;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tide predictions in the DEPTHS indicator and the Timeline (#1453, tide
/// predictions design D2, D9, D14, D15).
/// </summary>
public sealed class TidePredictionDepthBasisTests
{
    private const int Mllw = 12;
    private static readonly DateTimeOffset ViewTime = new(2026, 11, 12, 22, 20, 0, TimeSpan.Zero);
    private static readonly DateTime Applied = ViewTime.UtcDateTime;
    private static readonly GeoBounds View = new(47.5, -122.5, 47.7, -122.3);
    private static readonly GeoBounds InView = new(47.55, -122.45, 47.65, -122.35);

    private static readonly TidePredictionsInView InUse =
        new(true, ["SEATTLE 9447130", "Lockheed Shipyard 9447110", "Eagle Harbor 9445719"], Mllw, Stale: false, ListDate: ViewTime);

    [Fact]
    public void Only_predictions_adjusting_reads_predicted()
    {
        var basis = Evaluate(Adjusted(), InUse, Chart());

        Assert.Equal(DepthBasisKind.Adjusted, basis.Kind);
        Assert.Equal(PredictedShare.All, basis.Predicted);
        var forms = DepthBasisText.Pill(basis, TimeFormat.Utc);
        Assert.Equal("+ water level 22:20 UTC · predicted", forms.Full);
        Assert.Equal("+ predicted 22:20 UTC", forms.Short);
        Assert.Equal("Predicted 22:20 UTC", forms.Compact);
    }

    [Fact]
    public void S104_and_predictions_together_read_partly_predicted()
    {
        var basis = Evaluate(Adjusted(), InUse, Chart(), S104());

        Assert.Equal(PredictedShare.Part, basis.Predicted);
        Assert.Equal("+ water level 22:20 UTC · partly predicted", DepthBasisText.Pill(basis, TimeFormat.Utc).Full);
        Assert.Equal("Partly predicted 22:20 UTC", DepthBasisText.Pill(basis, TimeFormat.Utc).Short);
    }

    [Fact]
    public void The_flyout_lists_the_predicted_stations_and_what_predictions_leave_out()
    {
        var basis = Evaluate(Adjusted(), InUse, Chart());
        var lines = DepthBasisText.WaterLevelLines(basis, waterLevelLoaded: false, TimeFormat.Utc);

        Assert.Contains("Predicted · 3 NOAA stations · SEATTLE 9447130, Lockheed Shipyard 9447110, +1", lines);
        Assert.Contains("S-104 first; predictions only where no S-104 covers.", lines);
        Assert.Contains("Predictions leave out wind, pressure and river flow.", lines);
    }

    [Fact]
    public void The_flyout_offers_predictions_only_when_they_would_help()
    {
        var off = InUse with { InUse = false };

        Assert.True(Evaluate(NoWaterLevelAdjustment.Instance, off, Chart()).OffersPredictions);
        Assert.False(Evaluate(NoWaterLevelAdjustment.Instance, off, Chart(), S104()).OffersPredictions);
        Assert.False(Evaluate(NoWaterLevelAdjustment.Instance, off with { Stations = [] }, Chart()).OffersPredictions);
        Assert.False(Evaluate(NoWaterLevelAdjustment.Instance, InUse, Chart()).OffersPredictions);
    }

    [Fact]
    public void The_stale_notice_shows_only_while_predictions_are_in_use()
    {
        Assert.True(Evaluate(Adjusted(), InUse with { Stale = true }, Chart()).PredictionsStale);
        Assert.False(Evaluate(Adjusted(), InUse with { InUse = false, Stale = true }, Chart()).PredictionsStale);
    }

    [Fact]
    public void Get_depth_basis_reports_the_predicted_share()
    {
        var dto = McpTools.GetDepthBasisTool.ToDto(Evaluate(Adjusted(), InUse, Chart()));

        Assert.Equal("all", dto.Predicted);
        var row = Assert.Single(dto.WaterLevelSources);
        Assert.Equal("prediction", row.Spec);
        Assert.Equal("prediction", row.Kind);
        Assert.Equal(3, dto.PredictedStations!.Count);
    }

    [Fact]
    public void With_only_predictions_the_Timeline_shows_the_next_48_hours()
    {
        var now = new DateTime(2026, 11, 12, 22, 20, 0, DateTimeKind.Utc);
        var service = new GlobalTimeService { Clock = new FakeTimeProvider(new DateTimeOffset(now)) };
        var timeline = new TimelineViewModel(service, timeFormat: null, new FakeTimeProvider(new DateTimeOffset(now)), action => action(), refresher: null, progress: null, scope: null, library: null);
        Assert.False(timeline.IsActive);

        service.SetOpenEndedTime(true);

        Assert.True(timeline.IsActive);
        Assert.True(timeline.IsPredictionsOnly);
        Assert.Equal(now, service.CurrentTime);
        Assert.Equal(TimeMode.Live, service.Mode);
        Assert.Equal(now, timeline.VisibleStart);
        Assert.Equal(now.AddHours(48), timeline.VisibleEnd);
        Assert.Equal("Only predictions are loaded, and they have no end: showing the next 48 hours.", timeline.PresetTooltip);

        // BY TIME steps work; the view time is the Timeline's to set.
        Assert.NotNull(timeline.StepTarget(TimelineStepKind.Hour, +1));
        service.SetCurrentTime(now.AddHours(6));
        Assert.Equal(now.AddHours(6), service.CurrentTime);

        service.SetOpenEndedTime(false);
        Assert.False(timeline.IsActive);
        Assert.Null(service.CurrentTime);
    }

    private static DepthBasis Evaluate(IWaterLevelAdjustmentState adjustment, TidePredictionsInView predictions, params DepthBasisDataset[] datasets) =>
        DepthBasisEvaluator.Evaluate(new DepthBasisInputs(datasets, View, ViewTime, adjustment, predictions));

    private static DepthBasisDataset Chart() => new("US5WA22M", "S-101", true, InView, Mllw);

    private static DepthBasisDataset S104() => new(
        "104US00_PSOFS", "S-104", true, InView, Mllw, Applied.AddHours(-6), Applied.AddHours(6), WaterLevelSourceKind.Grid);

    private static FakeAdjustment Adjusted() => new(new ChartAdjustmentState(
        WaterLevelAdjustmentMode.AtViewTime,
        Applied,
        [new WaterLevelSourceInfo(WaterLevelSourceInfo.PredictionDatasetId, WaterLevelSourceKind.Prediction, Mllw)],
        [new DataModel.CoverageArea
        {
            ExteriorRing = [new(47, -123), new(47, -122), new(48, -122), new(48, -123), new(47, -123)],
        }],
        ApplyUndeclaredDatum: false));
}
