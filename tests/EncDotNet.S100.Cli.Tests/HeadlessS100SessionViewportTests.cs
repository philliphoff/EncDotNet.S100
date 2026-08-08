using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Verifies <see cref="HeadlessS100Session"/>'s <see cref="IViewportController"/>
/// implementation: the geographic viewport state it stores and the
/// <see cref="IViewportController.Current"/> echo it exposes (#568). The
/// per-render pixel resolution is covered by
/// <see cref="CompositeViewportBuilderTests"/>.
/// </summary>
public sealed class HeadlessS100SessionViewportTests
{
    [Fact]
    public void Current_Defaults_ToNull_ForAutoFit()
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);

        Assert.Null(((IViewportController)session).Current);
    }

    [Fact]
    public void Set_EchoesTheViewport()
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);
        var controller = (IViewportController)session;

        controller.Set(new MapViewport(-1.25, 50.5, 50000));

        var current = Assert.IsType<MapViewport>(controller.Current);
        Assert.Equal(-1.25, current.CenterLongitude);
        Assert.Equal(50.5, current.CenterLatitude);
        Assert.Equal(50000, current.ScaleDenominator);
        Assert.Equal(0, current.RotationDegrees);
    }

    [Fact]
    public void SetToBounds_EchoesResolvedCentreAndPositiveScale()
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);
        var controller = (IViewportController)session;

        controller.SetToBounds(new BoundingBox
        {
            WestBoundLongitude = -1.5,
            EastBoundLongitude = -1.0,
            SouthBoundLatitude = 50.0,
            NorthBoundLatitude = 50.5,
        });

        var current = Assert.IsType<MapViewport>(controller.Current);
        // Longitude is linear in Web Mercator, so the centre is the exact midpoint.
        Assert.Equal(-1.25, current.CenterLongitude, precision: 9);
        // Latitude is the Mercator midpoint of the box — close to, but not exactly,
        // the arithmetic mean (50.25) because Mercator Y is nonlinear.
        Assert.Equal(50.25, current.CenterLatitude, precision: 2);
        Assert.NotEqual(50.25, current.CenterLatitude);
        Assert.True(current.ScaleDenominator > 0 && double.IsFinite(current.ScaleDenominator));
    }

    [Fact]
    public void Set_ThenSetToBounds_AreMutuallyExclusive()
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);
        var controller = (IViewportController)session;

        controller.Set(new MapViewport(0, 0, 10000));
        controller.SetToBounds(new BoundingBox
        {
            WestBoundLongitude = -1.5,
            EastBoundLongitude = -1.0,
            SouthBoundLatitude = 50.0,
            NorthBoundLatitude = 50.5,
        });

        // The bounds now win: the centre is the box midpoint, not (0,0).
        Assert.Equal(-1.25, controller.Current!.CenterLongitude, precision: 9);

        controller.Set(new MapViewport(2, 3, 20000));

        // The explicit viewport now wins back.
        Assert.Equal(2, controller.Current!.CenterLongitude);
        Assert.Equal(20000, controller.Current!.ScaleDenominator);
    }
}
