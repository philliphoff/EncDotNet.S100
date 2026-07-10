using EncDotNet.S100.Viewer;
using Mapsui;
using Xunit;

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
}
