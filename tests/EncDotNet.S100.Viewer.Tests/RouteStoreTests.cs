using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Persistence;

namespace EncDotNet.S100.Viewer.Tests;

public class RouteStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public RouteStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "RouteStoreTests_" + Guid.NewGuid().ToString("n"));
        _path = Path.Combine(_dir, "routes.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public void Save_Then_Load_RoundTripsRoutesWaypointsLegsAndActive()
    {
        var source = new RouteCollection();

        var first = source.CreateRoute("First", "route-1");
        first.Info.Author = "Skipper";
        first.Info.Description = "Coastal hop";
        first.Info.ArrivalPortId = "GBSOU";
        first.Info.Vessel = new RouteVesselInfo { Name = "MV Test", Mmsi = "232003000", LengthMeters = 120.5 };
        var w0 = first.AppendWaypoint(new GeoPosition(50.1, -1.2));
        w0.Name = "Departure";
        w0.Number = 1;
        w0.Fixed = true;
        w0.TurnRadiusNm = 0.3;
        first.AppendWaypoint(new GeoPosition(50.4, -1.5));
        first.Legs[0].GeometryType = RouteLegGeometryType.Geodesic;
        first.Legs[0].SpeedOverGroundMaxKnots = 18.0;
        first.Legs[0].Note = "Mind the shoal";

        // A second route, left active, with no legs (single waypoint).
        var second = source.CreateRoute("Second", "route-2");
        second.AppendWaypoint(new GeoPosition(10, 20));
        source.SetActiveRoute(second);

        RouteStore.Save(source, _path);

        Assert.True(File.Exists(_path));

        var loaded = new RouteCollection();
        var any = RouteStore.Load(loaded, _path);

        Assert.True(any);
        Assert.Equal(2, loaded.Routes.Count);
        Assert.Equal("route-2", loaded.ActiveRoute?.Id);

        var loadedFirst = loaded.FindById("route-1");
        Assert.NotNull(loadedFirst);
        Assert.Equal("First", loadedFirst!.Name);
        Assert.Equal("Skipper", loadedFirst.Info.Author);
        Assert.Equal("Coastal hop", loadedFirst.Info.Description);
        Assert.Equal("GBSOU", loadedFirst.Info.ArrivalPortId);
        Assert.Equal("MV Test", loadedFirst.Info.Vessel?.Name);
        Assert.Equal("232003000", loadedFirst.Info.Vessel?.Mmsi);
        Assert.Equal(120.5, loadedFirst.Info.Vessel?.LengthMeters);

        Assert.Equal(2, loadedFirst.Waypoints.Count);
        Assert.Equal(new GeoPosition(50.1, -1.2), loadedFirst.Waypoints[0].Position);
        Assert.Equal("Departure", loadedFirst.Waypoints[0].Name);
        Assert.Equal(1, loadedFirst.Waypoints[0].Number);
        Assert.True(loadedFirst.Waypoints[0].Fixed);
        Assert.Equal(0.3, loadedFirst.Waypoints[0].TurnRadiusNm);

        Assert.Single(loadedFirst.Legs);
        Assert.Equal(RouteLegGeometryType.Geodesic, loadedFirst.Legs[0].GeometryType);
        Assert.Equal(18.0, loadedFirst.Legs[0].SpeedOverGroundMaxKnots);
        Assert.Equal("Mind the shoal", loadedFirst.Legs[0].Note);

        var loadedSecond = loaded.FindById("route-2");
        Assert.NotNull(loadedSecond);
        Assert.Single(loadedSecond!.Waypoints);
        Assert.Empty(loadedSecond.Legs);
    }

    [Fact]
    public void Load_MissingFile_LeavesCollectionEmptyAndReturnsFalse()
    {
        var routes = new RouteCollection();
        routes.CreateRoute("Stale"); // pre-existing content must be cleared.

        var any = RouteStore.Load(routes, _path);

        Assert.False(any);
        Assert.Empty(routes.Routes);
        Assert.Null(routes.ActiveRoute);
    }

    [Fact]
    public void Load_CorruptJson_LeavesCollectionEmptyAndReturnsFalse()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ this is not valid json ]");

        var routes = new RouteCollection();
        routes.CreateRoute("Stale");

        var any = RouteStore.Load(routes, _path);

        Assert.False(any);
        Assert.Empty(routes.Routes);
    }

    [Fact]
    public void Load_UnknownSchemaVersion_IsIgnored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, "{ \"SchemaVersion\": 9999, \"Routes\": [ { \"Id\": \"x\" } ] }");

        var routes = new RouteCollection();
        var any = RouteStore.Load(routes, _path);

        Assert.False(any);
        Assert.Empty(routes.Routes);
    }

    [Fact]
    public void Load_LegCountExceedingWaypoints_IsClampedNotThrown()
    {
        Directory.CreateDirectory(_dir);
        // One waypoint but two legs: a malformed but parseable document.
        File.WriteAllText(_path,
            "{ \"SchemaVersion\": 1, \"Routes\": [ { \"Id\": \"x\", \"Waypoints\": " +
            "[ { \"Latitude\": 1, \"Longitude\": 2 } ], \"Legs\": [ {}, {} ] } ] }");

        var routes = new RouteCollection();
        var any = RouteStore.Load(routes, _path);

        Assert.True(any);
        var route = Assert.Single(routes.Routes);
        Assert.Single(route.Waypoints);
        Assert.Empty(route.Legs);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var first = new RouteCollection();
        first.CreateRoute("Only", "route-1");
        RouteStore.Save(first, _path);

        var second = new RouteCollection();
        second.CreateRoute("Replacement", "route-2");
        RouteStore.Save(second, _path);

        var loaded = new RouteCollection();
        RouteStore.Load(loaded, _path);

        var route = Assert.Single(loaded.Routes);
        Assert.Equal("route-2", route.Id);
    }

    // ----- passage and leg overrides (#897) --------------------------------

    private static readonly string[] PassageOverrideKeys =
    [
        "PlannedSpeedKnots", "DraftMeters", "StaticUnderKeelClearanceMeters",
        "PortCrossTrackDistanceLimitMeters", "StarboardCrossTrackDistanceLimitMeters",
    ];

    [Fact]
    public void WaypointsOnlyRoute_WritesNoPassageKeys()
    {
        var source = new RouteCollection();
        var route = source.CreateRoute("Casual", "casual");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.1));
        route.AppendWaypoint(new GeoPosition(50.2, -1.2));

        RouteStore.Save(source, _path);

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_path));
        var routeElement = doc.RootElement.GetProperty("Routes")[0];
        Assert.False(routeElement.TryGetProperty("Passage", out _));
        foreach (var leg in routeElement.GetProperty("Legs").EnumerateArray())
        {
            foreach (var key in PassageOverrideKeys)
                Assert.False(leg.TryGetProperty(key, out _), $"unexpected leg key {key}");
        }

        var loaded = new RouteCollection();
        RouteStore.Load(loaded, _path);
        var loadedRoute = loaded.FindById("casual")!;
        Assert.True(loadedRoute.Passage.IsEmpty);
        Assert.Equal(2, loadedRoute.Legs.Count);
    }

    [Fact]
    public void Passage_WritesOnlySetFields_AndRoundTrips()
    {
        var source = new RouteCollection();
        var route = source.CreateRoute("Passage", "p");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.1));
        var departure = new DateTimeOffset(2026, 10, 12, 6, 40, 0, TimeSpan.Zero);
        route.SetPassageValue(RoutePassageField.Draft, 4.8);
        route.SetPassageValue(RoutePassageField.Speed, 8.0);
        route.SetDeparture(departure);

        RouteStore.Save(source, _path);

        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_path)))
        {
            var passage = doc.RootElement.GetProperty("Routes")[0].GetProperty("Passage");
            Assert.Equal(
                ["DraftMeters", "SpeedKnots", "Departure"],
                passage.EnumerateObject().Select(p => p.Name).ToArray());
        }

        var loaded = new RouteCollection();
        RouteStore.Load(loaded, _path);
        var p = loaded.FindById("p")!.Passage;
        Assert.Equal(4.8, p.DraftMeters);
        Assert.Equal(8.0, p.SpeedKnots);
        Assert.Equal(departure, p.Departure);
        Assert.Null(p.SquatMeters);
        Assert.Null(p.RequiredUkcMeters);
    }

    [Fact]
    public void LegOverride_Persists_AndResetRemovesTheKey()
    {
        var source = new RouteCollection();
        var route = source.CreateRoute("Overrides", "o");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.1));
        route.AppendWaypoint(new GeoPosition(50.2, -1.2));
        route.SetPassageValue(RoutePassageField.Speed, 10.0);
        route.SetLegOverride(1, RoutePassageField.Speed, 6.0);

        RouteStore.Save(source, _path);

        var loaded = new RouteCollection();
        RouteStore.Load(loaded, _path);
        var loadedRoute = loaded.FindById("o")!;
        Assert.False(loadedRoute.IsLegOverridden(0, RoutePassageField.Speed));
        Assert.True(loadedRoute.IsLegOverridden(1, RoutePassageField.Speed));
        Assert.Equal(6.0, loadedRoute.GetEffectiveLegValue(1, RoutePassageField.Speed));
        Assert.Equal(10.0, loadedRoute.GetEffectiveLegValue(0, RoutePassageField.Speed));
        Assert.Contains("\"PlannedSpeedKnots\": 6", File.ReadAllText(_path));

        loadedRoute.ResetLegOverride(1, RoutePassageField.Speed);
        RouteStore.Save(loaded, _path);

        Assert.DoesNotContain("PlannedSpeedKnots", File.ReadAllText(_path));
        var reloaded = new RouteCollection();
        RouteStore.Load(reloaded, _path);
        Assert.Equal(10.0, reloaded.FindById("o")!.GetEffectiveLegValue(1, RoutePassageField.Speed));
    }

    [Fact]
    public void OldFile_PerLegValues_LoadAsOverrides()
    {
        // A routes.json written before passages existed: no Passage key, and
        // per-leg draft, UKC and cross-track values.
        const string json = """
            {
              "SchemaVersion": 1,
              "ActiveRouteId": "old",
              "Routes": [
                {
                  "Id": "old",
                  "Info": { "Name": "Legacy" },
                  "Waypoints": [
                    { "Latitude": 50.0, "Longitude": -1.0 },
                    { "Latitude": 50.1, "Longitude": -1.1 },
                    { "Latitude": 50.2, "Longitude": -1.2 }
                  ],
                  "Legs": [
                    {
                      "GeometryType": "Loxodrome",
                      "DraftMeters": 5.2,
                      "StaticUnderKeelClearanceMeters": 0.8,
                      "PortCrossTrackDistanceLimitMeters": 100,
                      "StarboardCrossTrackDistanceLimitMeters": 150,
                      "SpeedOverGroundMaxKnots": 12,
                      "SafetyContourMeters": 10
                    },
                    { "GeometryType": "Geodesic" }
                  ]
                }
              ]
            }
            """;
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, json);

        var loaded = new RouteCollection();
        Assert.True(RouteStore.Load(loaded, _path));

        var route = loaded.FindById("old")!;
        Assert.True(route.Passage.IsEmpty);
        Assert.True(route.IsLegOverridden(0, RoutePassageField.Draft));
        Assert.Equal(5.2, route.GetEffectiveLegValue(0, RoutePassageField.Draft));
        Assert.Equal(0.8, route.GetEffectiveLegValue(0, RoutePassageField.RequiredUkc));
        Assert.Equal(100, route.GetEffectiveLegValue(0, RoutePassageField.PortCrossTrack));
        Assert.Equal(150, route.GetEffectiveLegValue(0, RoutePassageField.StarboardCrossTrack));
        Assert.Equal(12, route.Legs[0].SpeedOverGroundMaxKnots);
        Assert.Equal(10, route.Legs[0].SafetyContourMeters);
        Assert.Equal(RouteLegGeometryType.Geodesic, route.Legs[1].GeometryType);
        Assert.False(route.IsLegOverridden(1, RoutePassageField.Draft));

        // Setting a passage draft now changes only the leg that inherits it.
        route.SetPassageValue(RoutePassageField.Draft, 4.0);
        Assert.Equal(5.2, route.GetEffectiveLegValue(0, RoutePassageField.Draft));
        Assert.Equal(4.0, route.GetEffectiveLegValue(1, RoutePassageField.Draft));
    }
}
