using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using NetTopologySuite.Geometries;


namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the screen-space projection of cross-cell overlap-suppression clip
/// regions (<see cref="CoverageClip"/>, issue #438 Phase 2): an attached
/// EPSG:3857 region projects to the live viewport as an even-odd
/// <c>SKPath</c>, an empty region yields a draw-nothing path, and clearing a
/// region removes the clip so the layer paints in full again.
/// </summary>
public class CoverageClipTests
{
    private static readonly GeometryFactory Gf = new();

    // A 200x200 viewport centred on the mercator origin at resolution 1, so
    // world (x, y) maps to screen (x + 100, 100 - y): world +Y (north) is up,
    // screen +Y is down.
    private static Mapsui.Viewport MakeViewport() => new(0, 0, resolution: 1, rotation: 0, width: 200, height: 200);

    private static Polygon Square(double minX, double minY, double size)
    {
        var ring = Gf.CreateLinearRing(
        [
            new Coordinate(minX, minY),
            new Coordinate(minX + size, minY),
            new Coordinate(minX + size, minY + size),
            new Coordinate(minX, minY + size),
            new Coordinate(minX, minY),
        ]);
        return Gf.CreatePolygon(ring);
    }

    [Fact]
    public void BuildScreenPath_NoRegion_ReturnsNull()
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer, null);

        Assert.Null(CoverageClip.BuildScreenPath(layer, MakeViewport()));
    }

    [Fact]
    public void BuildScreenPath_EmptyRegion_ReturnsEmptyPath()
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer, Gf.CreatePolygon());

        var path = CoverageClip.BuildScreenPath(layer, MakeViewport());

        Assert.NotNull(path);
        Assert.Equal(0, path!.PointCount);
    }

    [Fact]
    public void BuildScreenPath_Polygon_ProjectsToScreenBounds()
    {
        var layer = new MemoryLayer();
        // World square (-50,-50)-(50,50) -> screen (50,50)-(150,150).
        CoverageClip.Set(layer, Square(-50, -50, 100));

        var path = CoverageClip.BuildScreenPath(layer, MakeViewport());

        Assert.NotNull(path);
        var bounds = path!.Bounds;
        Assert.Equal(50f, bounds.Left, 3);
        Assert.Equal(50f, bounds.Top, 3);
        Assert.Equal(150f, bounds.Right, 3);
        Assert.Equal(150f, bounds.Bottom, 3);
    }

    [Fact]
    public void BuildScreenPath_PolygonWithHole_IsEvenOddWithBothRings()
    {
        var layer = new MemoryLayer();
        var shell = Gf.CreateLinearRing(
        [
            new Coordinate(-50, -50),
            new Coordinate(50, -50),
            new Coordinate(50, 50),
            new Coordinate(-50, 50),
            new Coordinate(-50, -50),
        ]);
        var hole = Gf.CreateLinearRing(
        [
            new Coordinate(-10, -10),
            new Coordinate(10, -10),
            new Coordinate(10, 10),
            new Coordinate(-10, 10),
            new Coordinate(-10, -10),
        ]);
        CoverageClip.Set(layer, Gf.CreatePolygon(shell, [hole]));

        var path = CoverageClip.BuildScreenPath(layer, MakeViewport());

        Assert.NotNull(path);
        // Even-odd fill lets the inner ring punch a hole so the coarse cell
        // still shows through a finer cell's no-coverage gaps.
        Assert.Equal(SkiaSharp.SKPathFillType.EvenOdd, path!.FillType);
        // Exterior (4 unique + close) and hole (4 unique + close) both emitted.
        Assert.True(path.PointCount >= 8, $"expected both rings, got {path.PointCount} points");
    }

    [Fact]
    public void Set_NullRegion_ClearsPreviousAttachment()
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer, Square(-50, -50, 100));
        Assert.NotNull(CoverageClip.Get(layer));

        CoverageClip.Set(layer, null);

        Assert.Null(CoverageClip.Get(layer));
        Assert.Null(CoverageClip.BuildScreenPath(layer, MakeViewport()));
    }
}
