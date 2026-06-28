using System;
using System.IO;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Routing.Persistence;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class RoutePersistenceServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly ViewerDataPaths _paths;

    public RoutePersistenceServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "RoutePersistenceTests_" + Guid.NewGuid().ToString("n"));
        _paths = new ViewerDataPaths(_dir);
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
    public void Initialize_LoadsPersistedRoutesIntoService()
    {
        // Seed a routes.json directly through the store.
        var seed = new RouteCollection();
        var route = seed.CreateRoute("Seeded", "route-1");
        route.AppendWaypoint(new GeoPosition(1, 2));
        route.AppendWaypoint(new GeoPosition(3, 4));
        RouteStore.Save(seed, _paths.RoutesFilePath);

        var routes = new RoutesService();
        using var persistence = new RoutePersistenceService(routes, _paths, new ViewerSettings());
        persistence.Initialize();

        var loaded = Assert.Single(routes.Routes.Routes);
        Assert.Equal("route-1", loaded.Id);
        Assert.Equal(2, loaded.Waypoints.Count);
    }

    [Fact]
    public void Flush_AfterChange_WritesRoutesToDisk()
    {
        var routes = new RoutesService();
        using var persistence = new RoutePersistenceService(routes, _paths, new ViewerSettings());
        persistence.Initialize();

        var route = routes.Routes.CreateRoute("New", "route-9");
        route.AppendWaypoint(new GeoPosition(10, 20));

        persistence.Flush();

        Assert.True(File.Exists(_paths.RoutesFilePath));

        var reread = new RouteCollection();
        RouteStore.Load(reread, _paths.RoutesFilePath);
        var persisted = Assert.Single(reread.Routes);
        Assert.Equal("route-9", persisted.Id);
    }

    [Fact]
    public void ReadOnly_DoesNotWriteOnFlush()
    {
        var routes = new RoutesService();
        using var persistence = new RoutePersistenceService(
            routes, _paths, new ViewerSettings { IsReadOnly = true });
        persistence.Initialize();

        routes.Routes.CreateRoute("Ephemeral");

        persistence.Flush();

        Assert.False(File.Exists(_paths.RoutesFilePath));
    }

    [Fact]
    public void ReadOnly_StillLoadsExistingRoutes()
    {
        var seed = new RouteCollection();
        seed.CreateRoute("Existing", "route-1");
        RouteStore.Save(seed, _paths.RoutesFilePath);

        var routes = new RoutesService();
        using var persistence = new RoutePersistenceService(
            routes, _paths, new ViewerSettings { IsReadOnly = true });
        persistence.Initialize();

        Assert.Single(routes.Routes.Routes);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        var routes = new RoutesService();
        using var persistence = new RoutePersistenceService(routes, _paths, new ViewerSettings());
        persistence.Initialize();
        persistence.Initialize(); // second call must be a no-op, not throw.

        routes.Routes.CreateRoute("One");
        persistence.Flush();

        var reread = new RouteCollection();
        RouteStore.Load(reread, _paths.RoutesFilePath);
        Assert.Single(reread.Routes);
    }
}
