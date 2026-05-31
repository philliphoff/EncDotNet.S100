using EncDotNet.S100.Viewer.Services;
using Mapsui;
using Mapsui.Projections;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class MapViewportNotifierTests
{
    [Fact]
    public void TryProject_returns_null_when_viewport_unsized()
    {
        var vp = new Viewport(centerX: 0, centerY: 0, resolution: 1, rotation: 0, width: 0, height: 0);
        Assert.Null(MapViewportNotifier.TryProject(vp));
    }

    [Fact]
    public void TryProject_returns_null_when_height_zero()
    {
        var vp = new Viewport(0, 0, 1, 0, 100, 0);
        Assert.Null(MapViewportNotifier.TryProject(vp));
    }

    [Fact]
    public void TryProject_world_view_returns_clamped_full_extents()
    {
        // A "whole world" view: centered on (0,0), resolution chosen
        // so the half-extents reach the Mercator clamp.
        // halfW = width * res / 2 = 1024 * 39135.76 / 2 ≈ 20037508.34
        var vp = new Viewport(0, 0, resolution: 39135.76, rotation: 0, width: 1024, height: 1024);

        var snap = MapViewportNotifier.TryProject(vp);
        Assert.NotNull(snap);

        // Longitudes hit ±180 at the clamp.
        Assert.InRange(snap!.MinLongitude, -180.001, -179.5);
        Assert.InRange(snap.MaxLongitude, 179.5, 180.001);

        // Latitudes are clamped to ±~85.0511° (Mercator limit).
        Assert.InRange(snap.MinLatitude, -85.1, -84.9);
        Assert.InRange(snap.MaxLatitude, 84.9, 85.1);
    }

    [Fact]
    public void TryProject_small_view_round_trips_through_spherical_mercator()
    {
        // Drive the projection in the opposite direction first so the
        // test asserts agreement, not magic numbers.
        var (xMin, yMin) = SphericalMercator.FromLonLat(-1.0, -1.0);
        var (xMax, yMax) = SphericalMercator.FromLonLat(1.0, 1.0);
        var halfW = (xMax - xMin) / 2.0;
        var halfH = (yMax - yMin) / 2.0;
        var centerX = (xMax + xMin) / 2.0;
        var centerY = (yMax + yMin) / 2.0;
        const double width = 256.0;
        var resolution = (halfW * 2.0) / width;
        var height = (halfH * 2.0) / resolution;

        var vp = new Viewport(centerX, centerY, resolution, rotation: 0, width: width, height: height);
        var snap = MapViewportNotifier.TryProject(vp);

        Assert.NotNull(snap);
        Assert.Equal(-1.0, snap!.MinLongitude, 4);
        Assert.Equal(1.0, snap.MaxLongitude, 4);
        Assert.Equal(-1.0, snap.MinLatitude, 4);
        Assert.Equal(1.0, snap.MaxLatitude, 4);
    }
}
