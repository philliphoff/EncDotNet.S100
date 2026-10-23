using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation.Tests;

public class PassageScheduleTests
{
    private static readonly DateTimeOffset Departure = new(2024, 6, 1, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Legs_get_distances_durations_and_etas()
    {
        // A minute of latitude is a nautical mile on the model sphere to within 0,1 %.
        var a = new GeoPosition(0, 0);
        var b = new GeoPosition(0.5, 0);
        var c = new GeoPosition(1.0, 0);
        var schedule = PassageSchedule.Compute([Fixture.Leg([a, b], 5, speed: 10), Fixture.Leg([b, c], 5, speed: 15)], Departure);

        Assert.InRange(schedule.Legs[0].DistanceNauticalMiles, 29.97, 30.03);
        AssertClose(Departure.AddHours(3), Departure + schedule.Legs[0].Duration!.Value);
        AssertClose(Departure.AddHours(2), Departure + schedule.Legs[1].Duration!.Value);
        AssertClose(Departure.AddHours(3), schedule.Legs[1].Start!.Value);
        AssertClose(Departure.AddHours(5), schedule.Arrival!.Value);
        Assert.InRange(schedule.TotalDistanceMeters / PassageGeodesy.MetersPerNauticalMile, 59.94, 60.06);

        var halfway = schedule.EtaAt(1, schedule.Legs[1].DistanceMeters / 2)!.Value;
        AssertClose(Departure.AddHours(4), halfway);
    }

    // Within 0,1 % of five hours.
    private static void AssertClose(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.InRange((actual - expected).Duration().TotalSeconds, 0, 18);

    [Fact]
    public void A_leg_without_speed_leaves_later_etas_unknown()
    {
        var a = new GeoPosition(0, 0);
        var b = new GeoPosition(0.1, 0);
        var c = new GeoPosition(0.2, 0);
        var schedule = PassageSchedule.Compute([Fixture.Leg([a, b], 5), Fixture.Leg([b, c], 5, speed: 10)], Departure);

        Assert.Null(schedule.Legs[0].Duration);
        Assert.Equal(Departure, schedule.Legs[0].Start);
        Assert.Null(schedule.Legs[1].Start);
        Assert.NotNull(schedule.Legs[1].Duration);
        Assert.Null(schedule.Arrival);
        Assert.Null(schedule.EtaAt(1, 10));
    }

    [Fact]
    public void Invalid_legs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => PassageSchedule.Compute([Fixture.Leg([new GeoPosition(0, 0)], 5)], Departure));
        Assert.Throws<ArgumentException>(() => PassageSchedule.Compute(
            [Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 1)], 5, speed: -1)], Departure));
    }
}

public class PassageGeodesyTests
{
    [Fact]
    public void Rhumb_and_great_circle_agree_on_a_meridian()
    {
        var a = new GeoPosition(50, -1);
        var b = new GeoPosition(51, -1);

        Assert.InRange(PassageGeodesy.RhumbDistanceMeters(a, b) / PassageGeodesy.MetersPerNauticalMile, 59.94, 60.06);
        Assert.Equal(PassageGeodesy.GreatCircleDistanceMeters(a, b), PassageGeodesy.RhumbDistanceMeters(a, b), 3);
    }

    [Theory]
    [InlineData(LegGeometry.Rhumb)]
    [InlineData(LegGeometry.GreatCircle)]
    public void Interpolation_splits_distance_in_proportion(LegGeometry geometry)
    {
        var a = new GeoPosition(50.64, -0.935);
        var b = new GeoPosition(50.888, -1.395);
        var total = PassageGeodesy.DistanceMeters(a, b, geometry);

        var p = PassageGeodesy.Interpolate(a, b, 0.25, geometry);

        Assert.Equal(total * 0.25, PassageGeodesy.DistanceMeters(a, p, geometry), 0);
        Assert.Equal(total * 0.75, PassageGeodesy.DistanceMeters(p, b, geometry), 0);
        Assert.Equal(a, PassageGeodesy.Interpolate(a, b, 0, geometry));
    }
}

public class SharedRuleTests
{
    [Fact]
    public void The_depth_priority_chain_is_bathymetry_then_dredged_then_depth_area_then_sounding()
    {
        var bathy = new ChartedDepth(10, ChartedDepthKind.Bathymetry);
        var dredged = new ChartedDepth(9, ChartedDepthKind.DredgedArea);
        var area = new ChartedDepth(5, ChartedDepthKind.DepthArea);
        var sounding = new ChartedDepth(4, ChartedDepthKind.Sounding);

        Assert.Same(bathy, ChartedDepthResolver.Resolve(bathy, dredged, area, sounding));
        Assert.Same(dredged, ChartedDepthResolver.Resolve(null, dredged, area, sounding));
        Assert.Same(area, ChartedDepthResolver.Resolve(null, null, area, sounding));
        Assert.Same(sounding, ChartedDepthResolver.Resolve(null, null, null, sounding));
        Assert.Null(ChartedDepthResolver.Resolve(null, null, null, null));
        Assert.Same(area, ChartedDepthResolver.Shoaler(dredged, area));
    }

    [Theory]
    [InlineData("12.5", true, 12.5)]
    [InlineData("-1.2", true, -1.2)]
    [InlineData("abc", false, 0)]
    [InlineData(null, false, 0)]
    public void Depth_attributes_parse_with_the_invariant_culture(string? raw, bool ok, double expected)
    {
        Assert.Equal(ok, ChartedDepthResolver.TryReadMeters(raw, out var metres));
        Assert.Equal(expected, metres, 9);
    }

    [Fact]
    public void Water_level_preference_is_finest_then_latest_then_id()
    {
        var older = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = older.AddDays(1);

        Assert.True(WaterLevelPreference.Compare(0.01, older, "b", 0.02, newer, "a") < 0);
        Assert.True(WaterLevelPreference.Compare(0.01, older, "a", 0.01, newer, "b") > 0);
        Assert.True(WaterLevelPreference.Compare(0.01, newer, "b", 0.01, null, "a") < 0);
        Assert.True(WaterLevelPreference.Compare(0.01, null, "a", 0.01, null, "b") < 0);
    }

    [Theory]
    [InlineData(23, 23, DatumAgreement.Same)]
    [InlineData(23, 12, DatumAgreement.Different)]
    [InlineData(null, 23, DatumAgreement.Unknown)]
    [InlineData(23, null, DatumAgreement.Unknown)]
    [InlineData(4326, 23, DatumAgreement.Unknown)]
    public void Datum_agreement(int? depth, int? water, DatumAgreement expected) =>
        Assert.Equal(expected, VerticalDatumCheck.Compare(depth, water));
}
