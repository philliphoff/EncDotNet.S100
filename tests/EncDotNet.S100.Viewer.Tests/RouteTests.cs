using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Routing;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class RouteTests
{
    private static GeoPosition P(double lat, double lon) => new(lat, lon);

    [Fact]
    public void NewRoute_IsEmptyWithGeneratedIdAndInfo()
    {
        var route = new Route();
        Assert.False(string.IsNullOrWhiteSpace(route.Id));
        Assert.Empty(route.Waypoints);
        Assert.Empty(route.Legs);
        Assert.NotNull(route.Info);
        Assert.Equal(0.0, route.TotalDistanceNm());
    }

    [Fact]
    public void Ctor_UsesSuppliedIdAndName()
    {
        var route = new Route("route-1", "Approach");
        Assert.Equal("route-1", route.Id);
        Assert.Equal("Approach", route.Name);
        Assert.Equal("Approach", route.Info.Name);
    }

    [Fact]
    public void AppendWaypoint_FirstWaypoint_AddsNoLeg()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        Assert.Single(route.Waypoints);
        Assert.Empty(route.Legs);
    }

    [Fact]
    public void AppendWaypoint_SecondWaypoint_AddsOneLeg()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        route.AppendWaypoint(P(41.0, -73.0));
        Assert.Equal(2, route.Waypoints.Count);
        Assert.Single(route.Legs);
    }

    [Fact]
    public void LegInvariant_HoldsAfterAppends()
    {
        var route = new Route();
        for (var i = 0; i < 5; i++)
            route.AppendWaypoint(P(40.0 + i, -74.0));
        Assert.Equal(5, route.Waypoints.Count);
        Assert.Equal(4, route.Legs.Count);
    }

    [Fact]
    public void InsertWaypoint_AtFront_PrependsLegAndPreservesExistingLegAttributes()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        route.AppendWaypoint(P(41.0, -73.0));
        route.Legs[0].Note = "original";

        route.InsertWaypoint(0, P(39.0, -75.0));

        Assert.Equal(3, route.Waypoints.Count);
        Assert.Equal(2, route.Legs.Count);
        // The fresh leg leads; the original segment keeps its note.
        Assert.Null(route.Legs[0].Note);
        Assert.Equal("original", route.Legs[1].Note);
    }

    [Fact]
    public void InsertWaypoint_InMiddle_KeepsInboundLegAttributes()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0)); // wp0
        route.AppendWaypoint(P(42.0, -72.0)); // wp1
        route.Legs[0].Note = "leg-a";

        route.InsertWaypoint(1, P(41.0, -73.0)); // split leg0

        Assert.Equal(3, route.Waypoints.Count);
        Assert.Equal(2, route.Legs.Count);
        // Inbound half retains the attributes; outbound half is fresh.
        Assert.Equal("leg-a", route.Legs[0].Note);
        Assert.Null(route.Legs[1].Note);
    }

    [Fact]
    public void InsertWaypoint_OutOfRange_Throws()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.InsertWaypoint(5, P(1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.InsertWaypoint(-1, P(1, 1)));
    }

    [Fact]
    public void RemoveWaypoint_Interior_MergesLegsKeepingInboundAttributes()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0)); // wp0
        route.AppendWaypoint(P(41.0, -73.0)); // wp1
        route.AppendWaypoint(P(42.0, -72.0)); // wp2
        route.Legs[0].Note = "in";
        route.Legs[1].Note = "out";

        route.RemoveWaypoint(1);

        Assert.Equal(2, route.Waypoints.Count);
        Assert.Single(route.Legs);
        Assert.Equal("in", route.Legs[0].Note);
    }

    [Fact]
    public void RemoveWaypoint_Last_DropsTrailingLeg()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        route.AppendWaypoint(P(41.0, -73.0));
        route.AppendWaypoint(P(42.0, -72.0));
        route.Legs[0].Note = "keep";

        route.RemoveWaypoint(2);

        Assert.Equal(2, route.Waypoints.Count);
        Assert.Single(route.Legs);
        Assert.Equal("keep", route.Legs[0].Note);
    }

    [Fact]
    public void RemoveWaypoint_First_DropsLeadingLeg()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        route.AppendWaypoint(P(41.0, -73.0));
        route.AppendWaypoint(P(42.0, -72.0));
        route.Legs[1].Note = "survivor";

        route.RemoveWaypoint(0);

        Assert.Equal(2, route.Waypoints.Count);
        Assert.Single(route.Legs);
        Assert.Equal("survivor", route.Legs[0].Note);
    }

    [Fact]
    public void RemoveWaypoint_DownToOne_LeavesNoLegs()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        route.AppendWaypoint(P(41.0, -73.0));
        route.RemoveWaypoint(0);
        Assert.Single(route.Waypoints);
        Assert.Empty(route.Legs);
    }

    [Fact]
    public void MoveWaypoint_UpdatesPositionAndMetrics()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        route.AppendWaypoint(P(40.0, -73.0));
        var before = route.ComputeLegMetrics(0).DistanceNm;

        route.MoveWaypoint(1, P(40.0, -72.0));

        Assert.Equal(P(40.0, -72.0), route.Waypoints[1].Position);
        Assert.True(route.ComputeLegMetrics(0).DistanceNm > before);
    }

    [Fact]
    public void Clear_EmptiesWaypointsAndLegs()
    {
        var route = new Route();
        route.AppendWaypoint(P(40.0, -74.0));
        route.AppendWaypoint(P(41.0, -73.0));
        route.Clear();
        Assert.Empty(route.Waypoints);
        Assert.Empty(route.Legs);
    }

    [Fact]
    public void ComputeLegMetrics_Loxodrome_PureEastBearingIs090()
    {
        var route = new Route();
        route.AppendWaypoint(P(0.0, 0.0));
        route.AppendWaypoint(P(0.0, 10.0));
        var m = route.ComputeLegMetrics(0);
        Assert.Equal(0, m.LegIndex);
        Assert.InRange(m.InitialBearingDegrees, 89.9, 90.1);
        Assert.True(m.DistanceNm > 0);
    }

    [Fact]
    public void ComputeLegMetrics_GeodesicVsLoxodrome_DifferOnLongEastWestLeg()
    {
        var loxo = new Route();
        loxo.AppendWaypoint(P(60.0, -10.0));
        loxo.AppendWaypoint(P(60.0, 10.0));

        var geo = new Route();
        geo.AppendWaypoint(P(60.0, -10.0));
        geo.AppendWaypoint(P(60.0, 10.0));
        geo.Legs[0].GeometryType = RouteLegGeometryType.Geodesic;

        var loxoDist = loxo.ComputeLegMetrics(0).DistanceNm;
        var geoDist = geo.ComputeLegMetrics(0).DistanceNm;

        // At high latitude the great circle is shorter than the rhumb line.
        Assert.True(geoDist < loxoDist);
    }

    [Fact]
    public void TotalDistanceNm_SumsAllLegs()
    {
        var route = new Route();
        route.AppendWaypoint(P(0.0, 0.0));
        route.AppendWaypoint(P(0.0, 10.0));
        route.AppendWaypoint(P(0.0, 20.0));

        var sum = route.ComputeLegMetrics(0).DistanceNm + route.ComputeLegMetrics(1).DistanceNm;
        Assert.Equal(sum, route.TotalDistanceNm(), 6);
    }

    [Fact]
    public void ComputeLegMetrics_InvalidIndex_Throws()
    {
        var route = new Route();
        route.AppendWaypoint(P(0.0, 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => route.ComputeLegMetrics(0));
    }

    [Fact]
    public void Changed_RaisedOnStructuralEdits()
    {
        var route = new Route();
        var count = 0;
        route.Changed += (_, _) => count++;

        route.AppendWaypoint(P(0.0, 0.0));
        route.AppendWaypoint(P(0.0, 1.0));
        route.MoveWaypoint(0, P(0.1, 0.0));
        route.RemoveWaypoint(0);

        Assert.Equal(4, count);
    }

    [Fact]
    public void Name_SetterRaisesChangedOnlyWhenValueDiffers()
    {
        var route = new Route(name: "a");
        var count = 0;
        route.Changed += (_, _) => count++;

        route.Name = "a";   // no change
        route.Name = "b";   // change
        Assert.Equal(1, count);
        Assert.Equal("b", route.Name);
    }

    [Fact]
    public void Reverse_ReversesWaypointsAndLegsPreservingGeometry()
    {
        var route = new Route(name: "r");
        route.AppendWaypoint(P(0.0, 0.0));
        route.AppendWaypoint(P(0.0, 1.0));
        route.AppendWaypoint(P(0.0, 2.0));
        route.Legs[0].GeometryType = RouteLegGeometryType.Geodesic;

        var changed = 0;
        route.Changed += (_, _) => changed++;
        route.Reverse();

        Assert.Equal(1, changed);
        Assert.Equal(2.0, route.Waypoints[0].Position.Longitude, 6);
        Assert.Equal(1.0, route.Waypoints[1].Position.Longitude, 6);
        Assert.Equal(0.0, route.Waypoints[2].Position.Longitude, 6);
        // The geodesic leg travels with its geographic segment: it described
        // the 0->1 segment, which after reversal is the trailing leg (1->0).
        Assert.Equal(RouteLegGeometryType.Geodesic, route.Legs[1].GeometryType);
        Assert.Equal(RouteLegGeometryType.Loxodrome, route.Legs[0].GeometryType);
    }

    [Fact]
    public void Reverse_NoOpForFewerThanTwoWaypoints()
    {
        var route = new Route(name: "r");
        route.AppendWaypoint(P(0.0, 0.0));

        var changed = 0;
        route.Changed += (_, _) => changed++;
        route.Reverse();

        Assert.Equal(0, changed);
    }
}
