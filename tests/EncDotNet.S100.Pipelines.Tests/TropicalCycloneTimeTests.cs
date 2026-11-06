using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1297 (vector weather S3, V11–V16): a tropical cyclone advisory is
/// time-aware with continuous selection; the storm and its radii are drawn
/// at the time step (estimated between forecast points with a dashed ring
/// and "est.", on the best track before the advisory, not at all outside the
/// track and forecast) while the cone and tracks always draw; picks describe
/// the storm at that time. Fixture: Hurricane Ian, advisory 20 (synoptic
/// 00:00 UTC 28 Sep 2022, issued 03:00 UTC).
/// </summary>
public sealed class TropicalCycloneTimeTests
{
    private static readonly DateTime Synoptic = new(2022, 9, 28, 0, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TropicalCycloneDatasetProcessor Ian() => new(TropicalCycloneFixtures.IanForecast);

    [Fact]
    public void The_advisory_is_time_aware_with_continuous_selection_from_the_track_start()
    {
        var processor = Ian();
        ITimeAwareDatasetProcessor timeAware = processor;

        Assert.Equal(TimeStepSelection.Continuous, timeAware.TimeSemantics.Selection);
        Assert.Equal(TimeStepKind.Forecast, timeAware.TimeSemantics.Kind);
        Assert.Equal(processor.Advisory.Forecast.Select(p => p.ValidTime), timeAware.AvailableTimes);
        Assert.Equal(Synoptic, timeAware.AvailableTimes[0]);
        Assert.Equal(Synoptic - processor.Advisory.TrackStart, timeAware.TimeSemantics.Validity);
        Assert.Equal(new DateTime(2022, 9, 28, 3, 0, 0, DateTimeKind.Utc), timeAware.IssueTime);
        Assert.Equal(new DateTime(2022, 9, 28, 9, 0, 0, DateTimeKind.Utc), timeAware.NextIssueTime);
    }

    [Fact]
    public async Task Between_forecast_points_the_storm_is_estimated_with_a_dashed_ring_and_est()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var processor = Ian();
            var at = Synoptic.AddHours(6);
            var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = at }, Ct);
            var storm = Instructions(result).Where(i => i.FeatureReference == "AL092022").ToArray();

            Assert.Contains(storm.OfType<PointInstruction>(), p => p.SymbolReference == TropicalCycloneStyle.EstimatedRingSymbol);
            Assert.DoesNotContain(storm.OfType<PointInstruction>(), p => p.SymbolReference == TropicalCycloneStyle.RingSymbol);
            Assert.StartsWith("est. Wed 06:00 · Adv 20", storm.OfType<TextInstruction>().ElementAt(1).Text, StringComparison.Ordinal);

            // The storm sits between the forecast points, and the radii with it.
            var position = processor.StormDrawnAt(at)!.Point.Position;
            var stormGeometry = result.GeometryProvider.GetGeometry("AL092022")!;
            Assert.Equal(position.Longitude, stormGeometry.Coordinates[0].Longitude, 6);
            Assert.Equal(position.Latitude, stormGeometry.Coordinates[0].Latitude, 6);
            var ring = result.GeometryProvider.GetGeometry("AL092022 34 kt")!.Coordinates;
            Assert.InRange(position.Longitude, ring.Min(c => c.Longitude), ring.Max(c => c.Longitude));
            Assert.InRange(position.Latitude, ring.Min(c => c.Latitude), ring.Max(c => c.Latitude));
            Assert.Contains("AL092022 cone days 1-3", Instructions(result).Select(i => i.FeatureReference));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task On_a_forecast_point_the_storm_is_drawn_solid()
    {
        var result = await Ian().BuildVectorPortrayalAsync(new RenderContext { TimeStep = Synoptic.AddHours(24) }, Ct);
        var storm = Instructions(result).Where(i => i.FeatureReference == "AL092022").OfType<PointInstruction>().ToArray();

        Assert.Contains(storm, p => p.SymbolReference == TropicalCycloneStyle.RingSymbol);
        Assert.DoesNotContain(storm, p => p.SymbolReference == TropicalCycloneStyle.EstimatedRingSymbol);
    }

    [Fact]
    public async Task Before_the_advisory_the_storm_follows_the_best_track()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var result = await Ian().BuildVectorPortrayalAsync(new RenderContext { TimeStep = Synoptic.AddHours(-9) }, Ct);
            var storm = Instructions(result).Where(i => i.FeatureReference == "AL092022").ToArray();

            Assert.Contains(storm.OfType<PointInstruction>(), p => p.SymbolReference == TropicalCycloneStyle.RingSymbol);
            Assert.Equal("best track Tue 15:00", storm.OfType<TextInstruction>().ElementAt(1).Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task Beyond_the_forecast_there_is_no_storm_or_radii_but_the_cone_stays()
    {
        var processor = Ian();
        var beyond = processor.Advisory.ForecastEnd.AddHours(6);
        var result = await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = beyond }, Ct);
        var references = Instructions(result).Select(i => i.FeatureReference).ToHashSet();

        Assert.DoesNotContain("AL092022", references);
        Assert.DoesNotContain("AL092022 34 kt", references);
        Assert.Contains("AL092022 cone days 1-3", references);
        Assert.Contains("AL092022 track", references);

        // A pick on the cone says why there's no storm.
        var cone = processor.EnumerateFeatures().Single(f => f.FeatureRef == "AL092022 cone");
        var rows = processor.GetFeatureInfoAt(cone.Ordinal)!.Attributes.ToDictionary(a => a.Code);
        Assert.Equal(TropicalCycloneDatasetProcessor.BeyondForecastText, rows["atViewTime"].RawValue);
        Assert.DoesNotContain("position", rows.Keys);
        Assert.Null(processor.GetFeatureInfo("AL092022 34 kt"));
    }

    [Fact]
    public async Task The_pick_card_describes_the_storm_at_the_view_time()
    {
        var processor = Ian();
        var at = Synoptic.AddHours(6);
        await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = at }, Ct);
        var storm = processor.StormDrawnAt(at)!;

        var info = processor.GetFeatureInfo("AL092022")!;
        var rows = info.Attributes.ToDictionary(a => a.Code);

        Assert.Equal(at, rows["atViewTime"].DateTimeValue);
        Assert.Equal(TropicalCycloneDatasetProcessor.EstimateNote, rows["viewTimeBasis"].RawValue);
        Assert.Equal("Storm, estimated between forecast points", rows["pickedPart"].RawValue);
        Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"{storm.Point.MaxWindKnots} kn"), rows["maxWind"].RawValue, StringComparison.Ordinal);
        Assert.Contains("radius34", rows.Keys);
        Assert.Equal(
            ["notice", "name", "advisory", "issued", "nextAdvisory", "atViewTime", "viewTimeBasis", "position", "maxWind"],
            info.Attributes.Select(a => a.Code).Take(9));

        // The hit is the storm where it's drawn now.
        var hits = processor.HitTestFeatures(storm.Point.Position.Latitude, storm.Point.Position.Longitude, 1000);
        Assert.Equal("AL092022", processor.EnumerateFeatures().Single(f => f.Ordinal == hits.Single().Ordinal).FeatureRef);
    }

    [Fact]
    public async Task Without_a_time_step_the_storm_is_at_the_advisory()
    {
        var processor = Ian();
        await processor.BuildVectorPortrayalAsync(new RenderContext { TimeStep = Synoptic.AddHours(6) }, Ct);
        await processor.BuildVectorPortrayalAsync(new RenderContext(), Ct);

        var rows = processor.GetFeatureInfo("AL092022")!.Attributes.ToDictionary(a => a.Code);
        Assert.Equal("Storm, at the advisory", rows["pickedPart"].RawValue);
        Assert.DoesNotContain("atViewTime", rows.Keys);
    }

    private static IEnumerable<DrawingInstruction> Instructions(VectorPortrayalResult result) =>
        result.SubLayers.SelectMany(l => l.Instructions);
}
