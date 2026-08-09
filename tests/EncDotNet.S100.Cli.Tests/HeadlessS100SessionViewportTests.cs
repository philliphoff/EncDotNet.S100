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
        // Latitude is the Mercator midpoint of the box: inside the box, and — because
        // Mercator Y accelerates with latitude — a small step north of the arithmetic
        // mean (50.25), here ≈ +6.6e-4°. Assert the direction and a bounded, non-zero
        // offset rather than a brittle rounded-equality.
        Assert.InRange(current.CenterLatitude, 50.0, 50.5);
        Assert.InRange(current.CenterLatitude - 50.25, 1e-4, 5e-3);
        Assert.True(current.ScaleDenominator > 0 && double.IsFinite(current.ScaleDenominator));
    }

    [Fact]
    public void Set_WithNonZeroRotation_Throws()
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);
        var controller = (IViewportController)session;

        // The composite path is north-up only; a rotated viewport must be
        // rejected so Current never reports something the renderer can't honour.
        Assert.Throws<ArgumentException>(() => controller.Set(new MapViewport(-1.25, 50.5, 50000, 45)));
        Assert.Null(controller.Current); // nothing stored
    }

    [Theory]
    [InlineData(200.0, 50.0, 50000.0)]   // longitude out of range
    [InlineData(-1.25, 87.0, 50000.0)]   // latitude beyond the Web Mercator limit
    [InlineData(-1.25, 50.0, 0.0)]       // non-positive scale
    [InlineData(-1.25, 50.0, double.NaN)] // non-finite scale
    public void Set_WithInvalidFields_Throws(double lon, double lat, double scale)
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);
        var controller = (IViewportController)session;

        Assert.Throws<ArgumentException>(() => controller.Set(new MapViewport(lon, lat, scale)));
        Assert.Null(controller.Current); // nothing stored
    }

    [Fact]
    public void SetToBounds_WithInvertedBox_Throws()
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);
        var controller = (IViewportController)session;

        Assert.Throws<ArgumentException>(() => controller.SetToBounds(new BoundingBox
        {
            WestBoundLongitude = -1.0,
            EastBoundLongitude = -1.5, // west >= east
            SouthBoundLatitude = 50.0,
            NorthBoundLatitude = 50.5,
        }));
        Assert.Null(controller.Current);
    }

    [Fact]
    public void SetToBounds_WithOutOfRangeLatitude_Throws()
    {
        using var catalog = new HeadlessMutableCatalog();
        using var session = new HeadlessS100Session(catalog);
        var controller = (IViewportController)session;

        Assert.Throws<ArgumentException>(() => controller.SetToBounds(new BoundingBox
        {
            WestBoundLongitude = -1.5,
            EastBoundLongitude = -1.0,
            SouthBoundLatitude = 80.0,
            NorthBoundLatitude = 87.0, // beyond the Web Mercator limit
        }));
        Assert.Null(controller.Current);
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
