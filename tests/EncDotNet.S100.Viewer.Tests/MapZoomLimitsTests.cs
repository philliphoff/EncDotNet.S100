using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Tests;

public class MapZoomLimitsTests
{
    [Fact]
    public void ResolutionForScale_UsesEquatorialPixelSize()
    {
        // 1:500,000,000 * 0.00028 m/px = 140,000 m/px at the equator.
        Assert.Equal(140_000.0, MapZoomLimits.ResolutionForScale(500_000_000.0), 6);
        // 1:1,000 * 0.00028 = 0.28 m/px.
        Assert.Equal(0.28, MapZoomLimits.ResolutionForScale(1_000.0), 9);
    }

    [Fact]
    public void MinScaleIsFinerThanMaxScale()
    {
        Assert.True(MapZoomLimits.MinScaleDenominator < MapZoomLimits.MaxScaleDenominator);
    }

    [Fact]
    public void Apply_SetsOverrideZoomBoundsFromScaleDenominators()
    {
        var navigator = new Navigator();

        MapZoomLimits.Apply(navigator);

        var bounds = navigator.OverrideZoomBounds;
        Assert.NotNull(bounds);

        var expectedMin = MapZoomLimits.ResolutionForScale(MapZoomLimits.MinScaleDenominator);
        var expectedMax = MapZoomLimits.ResolutionForScale(MapZoomLimits.MaxScaleDenominator);

        // MMinMax orders arguments: Min = finest resolution, Max = coarsest.
        Assert.Equal(expectedMin, bounds!.Min, 9);
        Assert.Equal(expectedMax, bounds.Max, 6);
    }

    [Fact]
    public void Apply_ClampsZoomOutToMaxScale()
    {
        var navigator = new Navigator();
        navigator.SetSize(800, 600);
        MapZoomLimits.Apply(navigator);

        // Exercise the same limiter path SetViewportWithLimit uses when the
        // user zooms. Ask for a resolution far past the zoom-out floor.
        var requested = navigator.Viewport with { Resolution = 10_000_000.0 };
        var limited = navigator.Limiter.Limit(requested, navigator.PanBounds, navigator.ZoomBounds);

        var maxResolution = MapZoomLimits.ResolutionForScale(MapZoomLimits.MaxScaleDenominator);
        Assert.Equal(maxResolution, limited.Resolution, 6);
    }

    [Fact]
    public void Apply_ClampsZoomInToMinScale()
    {
        var navigator = new Navigator();
        navigator.SetSize(800, 600);
        MapZoomLimits.Apply(navigator);

        // Ask for a resolution far past the zoom-in ceiling.
        var requested = navigator.Viewport with { Resolution = 0.0001 };
        var limited = navigator.Limiter.Limit(requested, navigator.PanBounds, navigator.ZoomBounds);

        var minResolution = MapZoomLimits.ResolutionForScale(MapZoomLimits.MinScaleDenominator);
        Assert.Equal(minResolution, limited.Resolution, 9);
    }

    [Fact]
    public void Apply_DoesNotConstrainPanning()
    {
        // The cross-antimeridian fix relies on being able to pan far beyond
        // one world (into continuous EPSG:3857 space). Applying zoom limits
        // must not introduce pan bounds that would clip that.
        var navigator = new Navigator();
        navigator.SetSize(800, 600);
        MapZoomLimits.Apply(navigator);

        Assert.Null(navigator.PanBounds);

        var requested = navigator.Viewport with { CenterX = 30_000_000.0, Resolution = 1_000.0 };
        var limited = navigator.Limiter.Limit(requested, navigator.PanBounds, navigator.ZoomBounds);

        Assert.Equal(30_000_000.0, limited.CenterX, 3);
    }

    [Fact]
    public void ResolutionForScale_CorrectsForLatitude()
    {
        // Web-mercator stretches ground distance by 1/cos(60°) = 2.
        Assert.Equal(0.56, MapZoomLimits.ResolutionForScale(1_000.0, 60.0), 9);
    }

    [Fact]
    public void ResolutionForScale_ClampsBeyondMercatorLimit()
    {
        Assert.Equal(
            MapZoomLimits.ResolutionForScale(1_000.0, MapZoomLimits.MaxMercatorLatitude),
            MapZoomLimits.ResolutionForScale(1_000.0, 90.0),
            9);
    }

    [Fact]
    public void Apply_InstallsLatitudeAwareLimiter_Once()
    {
        var navigator = new Navigator();

        MapZoomLimits.Apply(navigator);
        var limiter = navigator.Limiter;
        MapZoomLimits.Apply(navigator);

        Assert.IsType<MapZoomLimits.LatitudeAwareLimiter>(limiter);
        Assert.Same(limiter, navigator.Limiter);
    }

    [Theory]
    [InlineData(-32.383)]
    [InlineData(50.0)]
    [InlineData(60.0)]
    [InlineData(-80.0)]
    public void Apply_ClampsZoomIn_ToMinScaleOnTheStatusBar_AtAnyLatitude(double latitude)
    {
        var navigator = new Navigator();
        navigator.SetSize(800, 600);
        MapZoomLimits.Apply(navigator);
        var (_, y) = SphericalMercator.FromLonLat(0, latitude);

        var requested = navigator.Viewport with { CenterY = y, Resolution = 0.0001 };
        var limited = navigator.Limiter.Limit(requested, navigator.PanBounds, navigator.ZoomBounds);

        Assert.Equal(
            MapZoomLimits.MinScaleDenominator,
            MapScaleFormatter.ResolutionToScaleDenominator(limited.Resolution, latitude)!.Value,
            6);
    }

    [Theory]
    [InlineData(-32.383)]
    [InlineData(60.0)]
    [InlineData(85.0)]
    public void Apply_ClampsZoomOut_ToTheEquatorialResolution_AtAnyLatitude(double latitude)
    {
        // The zoom-out floor bounds how small the world gets on screen, so it
        // is one EPSG:3857 resolution everywhere (issue #749); the status bar
        // reads MaxScaleDenominator * cos(latitude) there.
        var navigator = new Navigator();
        navigator.SetSize(800, 600);
        MapZoomLimits.Apply(navigator);
        var (_, y) = SphericalMercator.FromLonLat(0, latitude);

        var requested = navigator.Viewport with { CenterY = y, Resolution = 100_000_000.0 };
        var limited = navigator.Limiter.Limit(requested, navigator.PanBounds, navigator.ZoomBounds);

        Assert.Equal(MapZoomLimits.ResolutionForScale(MapZoomLimits.MaxScaleDenominator), limited.Resolution, 6);
        Assert.Equal(
            MapZoomLimits.MaxScaleDenominator * Math.Cos(latitude * Math.PI / 180.0),
            MapScaleFormatter.ResolutionToScaleDenominator(limited.Resolution, latitude)!.Value,
            0);
    }

    [Fact]
    public void Apply_LeavesInBandViewportsUntouched()
    {
        var navigator = new Navigator();
        navigator.SetSize(800, 600);
        MapZoomLimits.Apply(navigator);
        var (_, y) = SphericalMercator.FromLonLat(0, 60.0);
        var resolution = MapZoomLimits.ResolutionForScale(50_000.0, 60.0);

        var requested = navigator.Viewport with { CenterY = y, Resolution = resolution };
        var limited = navigator.Limiter.Limit(requested, navigator.PanBounds, navigator.ZoomBounds);

        Assert.Equal(resolution, limited.Resolution, 9);
    }

    [Fact]
    public void Navigator_CenterOnAndZoomTo_IsClampedAtTheTargetLatitude()
    {
        // The scripted set_viewport path: a request finer than 1:1 000 at
        // 32°S used to settle at about 1:844 (the equatorial clamp).
        var navigator = new Navigator();
        navigator.SetSize(800, 600);
        MapZoomLimits.Apply(navigator);
        var latitude = -32.383;
        var (x, y) = SphericalMercator.FromLonLat(61.75, latitude);
        // Mapsui applies the first viewport of a fresh navigator unlimited
        // (initialisation); the live map is always past that point.
        navigator.CenterOnAndZoomTo(new MPoint(x, y), 10, duration: 0);

        navigator.CenterOnAndZoomTo(new MPoint(x, y), 0.001, duration: 0);

        Assert.Equal(
            MapZoomLimits.MinScaleDenominator,
            MapScaleFormatter.ResolutionToScaleDenominator(navigator.Viewport.Resolution, latitude)!.Value,
            6);
    }

    [Fact]
    public void Apply_ClampsAtTheFinalLatitude_WhenPanBoundsMoveTheCentre()
    {
        // A loaded dataset's extent becomes Mapsui's pan bounds, so a viewport
        // requested at 60°N is pulled back to the data near 32°S. The zoom
        // clamp must use the latitude the viewport ends up at.
        var map = new Map();
        var (x0, y0) = SphericalMercator.FromLonLat(61.667, -32.467);
        var (x1, y1) = SphericalMercator.FromLonLat(61.833, -32.300);
        map.Layers.Add(new Mapsui.Layers.MemoryLayer
        {
            Features = [new Mapsui.Layers.PointFeature(x0, y0), new Mapsui.Layers.PointFeature(x1, y1)],
        });
        var navigator = map.Navigator;
        navigator.SetSize(727, 635);
        MapZoomLimits.Apply(navigator);
        Assert.NotNull(navigator.PanBounds);
        var (x, y) = SphericalMercator.FromLonLat(5, 60);

        var requested = navigator.Viewport with { CenterX = x, CenterY = y, Resolution = 0.001 };
        var limited = navigator.Limiter.Limit(requested, navigator.PanBounds, navigator.ZoomBounds);

        var finalLatitude = MapScaleFormatter.MercatorYToLatitudeDegrees(limited.CenterY);
        Assert.InRange(finalLatitude, -32.5, -32.2);
        Assert.Equal(
            MapZoomLimits.MinScaleDenominator,
            MapScaleFormatter.ResolutionToScaleDenominator(limited.Resolution, finalLatitude)!.Value,
            6);
    }

    [Fact]
    public void Navigator_ZoomOutFromAnotherLatitude_SettlesAtTheFloor()
    {
        // Issue #749: when the navigator clamps a requested resolution it
        // pulls the centre back toward the previous viewport's centre
        // (Mapsui's LimitXYProportionalToResolution). With a latitude-
        // corrected floor computed at the requested centre, the map settled
        // short of the floor at the latitude it ended up at.
        var navigator = new Navigator();
        navigator.SetSize(727, 635);
        MapZoomLimits.Apply(navigator);
        var (px, py) = SphericalMercator.FromLonLat(61.3, -32.3);
        navigator.CenterOnAndZoomTo(new MPoint(px, py), 10, duration: 0);
        navigator.CenterOnAndZoomTo(new MPoint(px, py), 100_000, duration: 0);

        navigator.CenterOnAndZoomTo(
            new MPoint(0, 0), MapZoomLimits.ResolutionForScale(2_000_000_000.0), duration: 0);

        Assert.Equal(MaxResolution, navigator.Viewport.Resolution, 6);
    }

    [Fact]
    public void Navigator_WheelZoomOutAboutThePointer_NeverPassesTheFloor()
    {
        // Zooming out about a pointer above the centre walks the centre
        // toward the pole. A latitude-corrected floor let the world shrink to
        // a few dozen pixels there; the world must stay at least as wide as
        // it is at the equatorial floor.
        var navigator = new Navigator();
        navigator.SetSize(727, 635);
        var worldMaxY = SphericalMercator.FromLonLat(0, MapZoomLimits.MaxMercatorLatitude).y;
        navigator.OverridePanBounds = new MRect(-worldMaxY, -worldMaxY, worldMaxY, worldMaxY);
        MapZoomLimits.Apply(navigator);
        var (px, py) = SphericalMercator.FromLonLat(20, 40);
        navigator.CenterOnAndZoomTo(new MPoint(px, py), 10, duration: 0);
        navigator.CenterOnAndZoomTo(new MPoint(px, py), 50_000, duration: 0);

        for (var tick = 0; tick < 40; tick++)
        {
            navigator.MouseWheelZoomContinuous(1.25, new Mapsui.Manipulations.ScreenPosition(500, 200));
            Assert.True(
                navigator.Viewport.Resolution <= MaxResolution * (1 + 1e-9),
                $"tick {tick}: zoomed out past the floor ({navigator.Viewport.Resolution} m/px)");
        }

        Assert.Equal(MaxResolution, navigator.Viewport.Resolution, 6);
    }

    private static double MaxResolution =>
        MapZoomLimits.ResolutionForScale(MapZoomLimits.MaxScaleDenominator);
}
