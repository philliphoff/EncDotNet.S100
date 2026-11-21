using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>The nearest navigable point (#1559) over fake masks and depths.</summary>
public sealed class NavigableSnapTests
{
    // Land west of 0.002° (about 220 m east of the meridian); 10 m of water
    // east of it, with a 3 m shoal north of 0.006°.
    private static readonly RouteCheckSources Coast = new()
    {
        WaterMask = new FakeMask(p => p.Longitude < 0.002 ? WaterMaskClass.Land : WaterMaskClass.Water),
        Depth = new FakeDepth((lat, lon) => lon < 0.002 ? null : FakeDepth.DepthArea(lat > 0.006 ? 3 : 10)),
    };

    [Fact]
    public void A_point_in_deep_water_stays_put()
    {
        var result = NavigableSnap.Snap(Coast, new GeoPosition(0.003, 0.005), new NavigableSnapRequest(5), TestContext.Current.CancellationToken);

        Assert.Equal(NavigableSnapStatus.Navigable, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal(10, result.DepthMeters);
        Assert.Equal(0, result.DistanceMeters);
    }

    [Fact]
    public void A_point_on_land_moves_east_to_the_water()
    {
        var point = new GeoPosition(0.003, 0.0010);

        var result = NavigableSnap.Snap(Coast, point, new NavigableSnapRequest(5), TestContext.Current.CancellationToken);

        Assert.Equal(NavigableSnapStatus.Moved, result.Status);
        Assert.Equal("land", result.Reason);
        Assert.True(result.Position!.Value.Longitude >= 0.002);
        Assert.InRange(result.DistanceMeters, 100, 200);
        Assert.InRange(result.BearingDegrees, 80, 100);
        Assert.True(result.DepthMeters >= 5);
    }

    [Fact]
    public void A_point_on_the_shoal_moves_south_into_deep_enough_water()
    {
        var result = NavigableSnap.Snap(Coast, new GeoPosition(0.0065, 0.005), new NavigableSnapRequest(5), TestContext.Current.CancellationToken);

        Assert.Equal("shoal", result.Reason);
        Assert.True(result.Position!.Value.Latitude < 0.006);
        Assert.InRange(result.BearingDegrees, 170, 190);
        Assert.Equal(3, result.PointDepthMeters);
    }

    [Fact]
    public void Clearance_keeps_the_point_off_the_land()
    {
        // 50 m off the coast is navigable, but not with 200 m of room.
        var point = new GeoPosition(0.003, 0.00245);

        var bare = NavigableSnap.Snap(Coast, point, new NavigableSnapRequest(5), TestContext.Current.CancellationToken);
        var roomy = NavigableSnap.Snap(Coast, point, new NavigableSnapRequest(5, ClearanceMeters: 200), TestContext.Current.CancellationToken);

        Assert.Equal(NavigableSnapStatus.Navigable, bare.Status);
        Assert.Equal("too_close", roomy.Reason);
        Assert.True(roomy.ClearanceMeters >= 200);
        Assert.True(roomy.Position!.Value.Longitude - 0.002 > 200 / PassageGeodesy.MetersPerDegreeLatitude);
    }

    [Fact]
    public void Nothing_within_the_radius_says_so()
    {
        var inland = new GeoPosition(0.003, -0.05);

        var result = NavigableSnap.Snap(Coast, inland, new NavigableSnapRequest(5, SearchRadiusMeters: 500), TestContext.Current.CancellationToken);

        Assert.Equal(NavigableSnapStatus.NotFound, result.Status);
        Assert.Equal("land", result.Reason);
        Assert.Null(result.Position);
    }

    [Fact]
    public void A_snapped_point_is_navigable_on_its_own()
    {
        var moved = NavigableSnap.Snap(Coast, new GeoPosition(0.003, 0.001), new NavigableSnapRequest(5, ClearanceMeters: 50), TestContext.Current.CancellationToken);

        var again = NavigableSnap.Snap(Coast, moved.Position!.Value, new NavigableSnapRequest(5, ClearanceMeters: 50), TestContext.Current.CancellationToken);

        Assert.Equal(NavigableSnapStatus.Navigable, again.Status);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 90)]
    [InlineData(0, -1, 180)]
    [InlineData(-1, 0, 270)]
    public void Bearings_are_degrees_true(double dLon, double dLat, double expected) =>
        Assert.Equal(expected, NavigableSnap.InitialBearingDegrees(new GeoPosition(0, 0), new GeoPosition(dLat * 0.01, dLon * 0.01)), 3);

    private sealed class FakeMask(Func<GeoPosition, WaterMaskClass> classify) : IWaterMask
    {
        public WaterMaskClass Classify(GeoPosition position) => classify(position);
    }
}
