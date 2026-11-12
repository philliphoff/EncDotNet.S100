using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// Predicted water level in the clearance check (#1451): the source kind,
/// S-104 first, lower confidence, its findings, and the reach measured by
/// water.
/// </summary>
public class WaterLevelPredictionTests
{
    private static readonly DateTime T0 = Fixture.Departure.UtcDateTime;

    [Fact]
    public void S104_is_used_where_it_covers_and_the_prediction_elsewhere()
    {
        // S-104 covers 05:00–07:00 only; the prediction has no end.
        var s104 = new FakeSeries(T0.AddHours(-1), T0.AddHours(1), _ => 0.5, datum: Fixture.Mllw);
        var source = WaterLevelSources.WithFallback(
            new FakeWater((_, _) => [s104]),
            new FakeWater((_, _) => [Predicted(_ => 1.5)]));

        var series = source.Locate(new GeoPosition(0, 0));
        var inside = WaterLevelSnapshot.Resolve(series, T0, Fixture.Mllw);
        var after = WaterLevelSnapshot.Resolve(series, T0.AddHours(3), Fixture.Mllw);

        Assert.Equal(WaterLevelSourceKind.Station, inside.Source!.Kind);
        Assert.Equal(0.5, inside.Sample!.Value.HeightMeters);
        Assert.True(after.Source!.IsPredicted);
        Assert.Equal(WaterLevelStatus.Applied, after.Status);
        Assert.Equal(1.5, after.Sample!.Value.HeightMeters);
    }

    [Fact]
    public void A_predicted_water_level_is_lower_confidence_and_named_in_an_info_finding()
    {
        var plan = Fixture.Plan(Fixture.Leg(draft: 10, required: 2));
        var result = Check(plan, Predicted(_ => 1.0), depthMeters: 20);

        Assert.All(result.Samples, s => Assert.True(s.Confidence.HasFlag(ConfidenceReasons.WaterLevelPredicted)));
        Assert.All(result.Samples, s => Assert.Equal(UkcState.MeetsSettings, s.State));
        var stretch = Assert.Single(result.LowConfidence);
        Assert.Equal(PassageStretchKind.WaterLevelPredicted, stretch.Kind);

        var findings = ClearanceFindings.Build(plan, result);
        var finding = Assert.Single(findings);
        Assert.Equal(RouteFindingCodes.WaterLevelPredicted, finding.Code);
        Assert.Equal(RouteFindingGroup.Info, finding.Group);
        Assert.StartsWith("Water level predicted for 6.0 NM", finding.Title, StringComparison.Ordinal);
        Assert.Contains("Predicted · Seattle 9447130", finding.Meta, StringComparison.Ordinal);
        Assert.Contains("not a forecast", finding.Meta, StringComparison.Ordinal);
        var station = Assert.Single(((ClearanceDetails)finding.Details!).WaterLevelSources);
        Assert.Equal("9447130", station.StationId);
    }

    [Fact]
    public void Tight_on_a_predicted_water_level_is_a_check()
    {
        // 12,3 m + 0 m − 10 m draft = 2,3 m against 2 m required: Tight.
        var plan = Fixture.Plan(Fixture.Leg(draft: 10, required: 2));
        var result = Check(plan, Predicted(_ => 0), depthMeters: 12.3);

        var tight = Assert.Single(ClearanceFindings.Build(plan, result), f => f.Code == RouteFindingCodes.TightOnPredicted);
        Assert.Equal(RouteFindingGroup.Check, tight.Group);
        Assert.Contains("Tight on predicted water level", tight.Title, StringComparison.Ordinal);
        Assert.Contains("Seattle 9447130", tight.Meta, StringComparison.Ordinal);
        Assert.Equal(UkcState.Tight, ((ClearanceDetails)tight.Details!).State);
    }

    [Fact]
    public void A_prediction_on_another_datum_is_reported_not_applied()
    {
        // The depths are on LAT; the station is predicted on MLLW.
        var plan = Fixture.Plan(Fixture.Leg(draft: 10, required: 2));
        var result = Check(plan, Predicted(_ => 1.0, datum: Fixture.Mllw), depthMeters: 20, depthDatum: Fixture.Lat);

        Assert.All(result.Samples, s =>
        {
            Assert.Equal(WaterLevelStatus.DatumMismatch, s.WaterLevel.Status);
            Assert.True(s.WaterLevel.Source!.IsPredicted);
            Assert.Null(s.ClearanceMeters);
            Assert.False(s.Confidence.HasFlag(ConfidenceReasons.WaterLevelPredicted));
        });
        Assert.Contains(ClearanceFindings.Build(plan, result), f => f.Code == RouteFindingCodes.DatumMismatch);
    }

    [Fact]
    public void Reach_is_straight_without_a_mask_and_ends_at_the_reach()
    {
        var reach = new WaterReach(mask: null, reachMeters: 10_000);
        var station = new GeoPosition(0, 0);

        Assert.Equal(PassageGeodesy.GreatCircleDistanceMeters(station, new GeoPosition(0, 0.05)), reach.DistanceMeters(station, new GeoPosition(0, 0.05))!.Value, 6);
        Assert.Null(reach.DistanceMeters(station, new GeoPosition(0, 0.1)));
    }

    [Fact]
    public void Land_makes_the_reach_go_round_it_or_cuts_it_off()
    {
        var station = new GeoPosition(0.1, 0.05);
        var across = new GeoPosition(0.1, 0.12); // 7,8 km east, behind the wall
        var straight = PassageGeodesy.GreatCircleDistanceMeters(station, across);

        // A wall of land at 0,08–0,09° E from 0,08° N northwards: the way round
        // its south end is about 9 km.
        var headland = new WaterReach(new FakeMask((lat, lon) => lon is >= 0.08 and <= 0.09 && lat >= 0.08));
        var round = headland.DistanceMeters(station, across);
        Assert.NotNull(round);
        Assert.InRange(round!.Value, 8_900, 9_400);
        Assert.True(round > straight);

        // The same wall across the whole sound: no way round within 10 km.
        var wall = new WaterReach(new FakeMask((_, lon) => lon is >= 0.08 and <= 0.09));
        Assert.Null(wall.DistanceMeters(station, across));

        // Uncharted ground doesn't block: straight line.
        var uncharted = new WaterReach(new FakeMask((_, _) => false, uncharted: true));
        Assert.Equal(straight, uncharted.DistanceMeters(station, across)!.Value, 6);
    }

    [Fact]
    public void A_station_on_charted_land_still_reaches_the_water()
    {
        // The station's gauge sits 100 m inside a land area.
        var shore = new WaterReach(new FakeMask((_, lon) => lon < 0.001));
        var station = new GeoPosition(0, 0);
        var point = new GeoPosition(0, 0.03);

        Assert.Equal(PassageGeodesy.GreatCircleDistanceMeters(station, point), shore.DistanceMeters(station, point)!.Value, 6);
    }

    private static PassageCheckResult Check(PassagePlan plan, IWaterLevelSeries series, double depthMeters, int depthDatum = Fixture.Mllw) =>
        Checks.Run(
            new ClearanceCheck(),
            plan,
            Fixture.Settings(),
            new RouteCheckSources
            {
                Depth = new FakeDepth((_, _) => FakeDepth.Bathymetry(depthMeters, depthDatum)),
                WaterLevel = new FakeWater((_, _) => [series]),
            }).Detail!.Clearance;

    private static PredictedSeries Predicted(Func<DateTime, double> heightAt, int datum = Fixture.Mllw) => new(heightAt, datum);

    /// <summary>A harmonic prediction at Seattle: no start, no end, highs and lows as its steps.</summary>
    private sealed class PredictedSeries(Func<DateTime, double> heightAt, int datum) : IWaterLevelSeries
    {
        public WaterLevelSourceInfo Source { get; } = new(WaterLevelSourceInfo.PredictionDatasetId, WaterLevelSourceKind.Prediction, datum, "9447130", 4200)
        {
            StationName = "Seattle",
        };

        public DateTime Start => DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

        public DateTime End => DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

        public WaterLevelReading? ValueAt(DateTime utc) => new WaterLevelReading(heightAt(utc), utc);

        public IReadOnlyList<DateTime> StepsWithin(DateTime start, DateTime end) => [];
    }

    /// <summary>A mask with land wherever <paramref name="isLand"/> says, water (or uncharted) elsewhere.</summary>
    private sealed class FakeMask(Func<double, double, bool> isLand, bool uncharted = false) : IWaterMask
    {
        public WaterMaskClass Classify(GeoPosition position) =>
            uncharted ? WaterMaskClass.Uncharted
            : isLand(position.Latitude, position.Longitude) ? WaterMaskClass.Land
            : WaterMaskClass.Water;
    }
}
