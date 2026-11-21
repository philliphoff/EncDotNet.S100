using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.TropicalCyclones.Tests;

/// <summary>
/// Issue #1297 (V11): the storm at a time is the forecast point on one,
/// interpolated linearly in time between two (great circle, radii per
/// quadrant, the earlier point's class), the best track before the advisory,
/// and nothing outside the track and forecast.
/// </summary>
public sealed class TropicalCycloneStormAtTests
{
    private static readonly DateTime Synoptic = new(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Between_forecast_points_is_an_estimate_with_the_earlier_class_across_a_class_change()
    {
        var advisory = Advisory(
            Point(0, 20, -80, 60, TropicalCycloneClass.TropicalStorm, category: null, pressure: 990),
            Point(12, 22, -82, 80, TropicalCycloneClass.Hurricane, category: 1, pressure: 970));

        var storm = advisory.StormAt(Synoptic.AddHours(6));

        Assert.NotNull(storm);
        Assert.Equal(TropicalCycloneStormSource.Estimated, storm.Source);
        Assert.True(storm.IsEstimated);
        Assert.Equal(TropicalCycloneClass.TropicalStorm, storm.Point.Classification);
        Assert.Null(storm.Point.SaffirSimpsonCategory);
        Assert.Equal(70, storm.Point.MaxWindKnots);
        Assert.Equal(980, storm.Point.PressureHectopascals);
        Assert.Equal(6, storm.Point.Tau);
        Assert.Equal(Synoptic, storm.EarlierTime);
        Assert.Equal(Synoptic.AddHours(12), storm.LaterTime);

        // Halfway along the geodesic, not the straight line in degrees.
        var mid = Wgs84Geodesy.GeodesicInterpolate(new GeoPosition(20, -80), new GeoPosition(22, -82), 0.5);
        Assert.Equal(mid.Latitude, storm.Point.Position.Latitude, 6);
        Assert.Equal(mid.Longitude, storm.Point.Position.Longitude, 6);

        // The motion is the segment's: north-westwards, about 12.7 kn.
        Assert.InRange(storm.Point.MotionDirectionDegrees!.Value, 300, 330);
        Assert.InRange(storm.Point.MotionSpeedKnots!.Value, 12, 14);
    }

    [Fact]
    public void Just_before_the_later_point_still_has_the_earlier_class()
    {
        var advisory = Advisory(
            Point(0, 20, -80, 60, TropicalCycloneClass.TropicalStorm),
            Point(12, 22, -82, 80, TropicalCycloneClass.Hurricane, category: 1));

        var before = advisory.StormAt(Synoptic.AddHours(12).AddMinutes(-1))!;
        var on = advisory.StormAt(Synoptic.AddHours(12))!;

        Assert.Equal(TropicalCycloneClass.TropicalStorm, before.Point.Classification);
        Assert.Equal(TropicalCycloneStormSource.ForecastPoint, on.Source);
        Assert.Equal(TropicalCycloneClass.Hurricane, on.Point.Classification);
        Assert.Same(advisory.Forecast[1], on.Point);
    }

    [Fact]
    public void A_radius_zero_at_one_end_shrinks_to_nothing()
    {
        var advisory = Advisory(
            Point(0, 20, -80, 70, TropicalCycloneClass.Hurricane, category: 1) with
            {
                Radii = [Radius(64, 20, 20, 10, 10), Radius(34, 100, 80, 60, 80)],
            },
            Point(12, 22, -82, 60, TropicalCycloneClass.TropicalStorm) with
            {
                // The 64 kt radius is zero; the 50 kt radius only starts here.
                Radii = [Radius(64, 0, 0, 0, 0), Radius(50, 40, 40, 20, 20), Radius(34, 120, 100, 80, 100)],
            });

        var storm = advisory.StormAt(Synoptic.AddHours(6))!;
        var byThreshold = storm.Point.Radii.ToDictionary(r => r.ThresholdKnots);

        Assert.Equal([34, 50, 64], byThreshold.Keys.Order());
        AssertQuadrants(byThreshold[64], 10, 10, 5, 5);
        AssertQuadrants(byThreshold[50], 20, 20, 10, 10);
        AssertQuadrants(byThreshold[34], 110, 90, 70, 90);
        Assert.All(storm.Point.Radii, r => Assert.Empty(r.Surfaces));

        // At the later point's time the 64 kt radius is gone.
        var almost = advisory.StormAt(Synoptic.AddHours(12).AddSeconds(-1))!;
        Assert.True(almost.Point.Radii.Single(r => r.ThresholdKnots == 64).MaximumNauticalMiles < 0.01);
    }

    [Fact]
    public void A_point_with_no_radii_leaves_the_radii_unknown()
    {
        var advisory = Advisory(
            Point(0, 20, -80, 70, TropicalCycloneClass.Hurricane, category: 1) with { Radii = [Radius(34, 100, 80, 60, 80)] },
            Point(96, 30, -70, 50, TropicalCycloneClass.TropicalStorm));

        Assert.Empty(advisory.StormAt(Synoptic.AddHours(48))!.Point.Radii);
    }

    [Fact]
    public void Crossing_the_antimeridian_stays_on_the_earlier_side()
    {
        var advisory = Advisory(
            Point(0, 15, 179, 50, TropicalCycloneClass.TropicalStorm),
            Point(12, 15, -179, 55, TropicalCycloneClass.TropicalStorm));

        var quarter = advisory.StormAt(Synoptic.AddHours(3))!;
        var mid = advisory.StormAt(Synoptic.AddHours(6))!;
        var late = advisory.StormAt(Synoptic.AddHours(9))!;

        Assert.Equal(179.5, quarter.Point.Position.Longitude, 2);
        Assert.Equal(180, mid.Point.Position.Longitude, 2);
        Assert.Equal(180.5, late.Point.Position.Longitude, 2);
        Assert.InRange(mid.Point.Position.Latitude, 15, 15.01);
        Assert.InRange(mid.Point.MotionDirectionDegrees!.Value, 85, 95);
    }

    [Fact]
    public void Before_the_advisory_follows_the_best_track_with_its_radii()
    {
        var advisory = Advisory(
            Point(0, 20, -80, 70, TropicalCycloneClass.Hurricane, category: 1) with { Radii = [Radius(34, 100, 100, 100, 100)] },
            Point(12, 22, -82, 80, TropicalCycloneClass.Hurricane, category: 1)) with
        {
            PastTrack =
            [
                Track(-12, 18, -78, 50, TropicalCycloneClass.TropicalStorm, [Radius(34, 60, 60, 60, 60)]),
                Track(-6, 19, -79, 60, TropicalCycloneClass.TropicalStorm, [Radius(34, 80, 80, 80, 80)]),
            ],
        };

        var between = advisory.StormAt(Synoptic.AddHours(-9))!;
        var onPoint = advisory.StormAt(Synoptic.AddHours(-6))!;
        var lastLeg = advisory.StormAt(Synoptic.AddHours(-3))!;

        Assert.Equal(TropicalCycloneStormSource.BestTrack, between.Source);
        Assert.False(between.IsEstimated);
        Assert.Equal(55, between.Point.MaxWindKnots);
        Assert.Equal(-9, between.Point.Tau);
        AssertQuadrants(between.Point.Radii.Single(), 70, 70, 70, 70);
        Assert.Equal(TropicalCycloneStormSource.BestTrack, onPoint.Source);
        Assert.Equal(60, onPoint.Point.MaxWindKnots);
        Assert.Equal(-6, onPoint.Point.Tau);

        // The last leg runs to the advisory's own position.
        Assert.Equal(65, lastLeg.Point.MaxWindKnots);
        AssertQuadrants(lastLeg.Point.Radii.Single(), 90, 90, 90, 90);
    }

    [Fact]
    public void Outside_the_track_and_forecast_there_is_no_storm()
    {
        var advisory = Advisory(
            Point(0, 20, -80, 70, TropicalCycloneClass.Hurricane, category: 1),
            Point(12, 22, -82, 80, TropicalCycloneClass.Hurricane, category: 1)) with
        {
            PastTrack = [Track(-12, 18, -78, 50, TropicalCycloneClass.TropicalStorm, [])],
        };

        Assert.Null(advisory.StormAt(Synoptic.AddHours(-12).AddMinutes(-1)));
        Assert.Null(advisory.StormAt(Synoptic.AddHours(12).AddMinutes(1)));
        Assert.NotNull(advisory.StormAt(Synoptic.AddHours(-12)));
        Assert.Equal(TropicalCycloneStormSource.ForecastPoint, advisory.StormAt(Synoptic)!.Source);
    }

    [Fact]
    public void Ian_advisory_20_moves_between_its_forecast_points()
    {
        var advisory = NhcAdvisoryReader.Read(TropicalCycloneFixtures.IanForecast);
        var first = advisory.Forecast[0];
        var second = advisory.Forecast[1];
        var halfway = first.ValidTime + (second.ValidTime - first.ValidTime) / 2;

        var storm = advisory.StormAt(halfway)!;

        Assert.True(storm.IsEstimated);
        Assert.InRange(storm.Point.Position.Latitude, Math.Min(first.Position.Latitude, second.Position.Latitude), Math.Max(first.Position.Latitude, second.Position.Latitude));
        Assert.NotEmpty(storm.Point.Radii);
        Assert.Equal(TropicalCycloneStormSource.BestTrack, advisory.StormAt(first.ValidTime.AddHours(-12))!.Source);
    }

    private static TropicalCycloneAdvisory Advisory(params TropicalCycloneForecastPoint[] points) => new()
    {
        Storm = new TropicalCycloneStorm("AL142026", "Zelda"),
        Advisory = "17",
        IssueTime = new DateTimeOffset(Synoptic.AddHours(3)),
        Forecast = points,
        ForecastTrack = [.. points.Select(p => p.Position)],
    };

    private static TropicalCycloneForecastPoint Point(int tau, double lat, double lon, int wind, TropicalCycloneClass cls, int? category = null, int? pressure = null) => new()
    {
        Tau = tau,
        ValidTime = Synoptic.AddHours(tau),
        Position = new GeoPosition(lat, lon),
        MaxWindKnots = wind,
        PressureHectopascals = pressure,
        Classification = cls,
        ClassCode = cls == TropicalCycloneClass.Hurricane ? "HU" : "TS",
        SaffirSimpsonCategory = category,
    };

    private static TropicalCycloneTrackPoint Track(int hours, double lat, double lon, int wind, TropicalCycloneClass cls, IReadOnlyList<TropicalCycloneWindRadii> radii) => new()
    {
        Time = Synoptic.AddHours(hours),
        Position = new GeoPosition(lat, lon),
        MaxWindKnots = wind,
        Classification = cls,
        ClassCode = "TS",
        Radii = radii,
    };

    private static TropicalCycloneWindRadii Radius(int threshold, double ne, double se, double sw, double nw) =>
        new(threshold, ne, se, sw, nw, []);

    private static void AssertQuadrants(TropicalCycloneWindRadii radius, double ne, double se, double sw, double nw)
    {
        Assert.Equal(ne, radius.NortheastNauticalMiles, 6);
        Assert.Equal(se, radius.SoutheastNauticalMiles, 6);
        Assert.Equal(sw, radius.SouthwestNauticalMiles, 6);
        Assert.Equal(nw, radius.NorthwestNauticalMiles, 6);
    }
}
