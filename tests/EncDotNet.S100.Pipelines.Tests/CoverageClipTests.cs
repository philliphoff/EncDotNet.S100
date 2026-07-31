using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the zoom-aware screen-space projection of cross-cell
/// overlap-suppression clip regions (<see cref="CoverageClip"/>, issue #438
/// Phase 2): each finer coverage projects to the live viewport as an even-odd
/// difference-clip <c>SKPath</c>, a finer coverage whose scale band has zoomed
/// out (resolution past its cutoff) is dropped so the coarser cell paints in
/// full, and clearing removes all clips.
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
    public void BuildActiveDifferencePaths_NoRegion_ReturnsEmpty()
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer, null);

        Assert.Empty(CoverageClip.BuildActiveDifferencePaths(layer, MakeViewport(), resolution: 1));
    }

    [Fact]
    public void BuildActiveDifferencePaths_ActiveFiner_ProjectsToScreenBounds()
    {
        var layer = new MemoryLayer();
        // World square (-50,-50)-(50,50) -> screen (50,50)-(150,150).
        CoverageClip.Set(layer, [new FinerCoverage(Square(-50, -50, 100), CutoffResolution: 2.0)]);

        var paths = CoverageClip.BuildActiveDifferencePaths(layer, MakeViewport(), resolution: 1);

        var path = Assert.Single(paths);
        var bounds = path.Bounds;
        Assert.Equal(50f, bounds.Left, 3);
        Assert.Equal(50f, bounds.Top, 3);
        Assert.Equal(150f, bounds.Right, 3);
        Assert.Equal(150f, bounds.Bottom, 3);
    }

    [Fact]
    public void BuildActiveDifferencePaths_FinerZoomedOut_IsDropped()
    {
        var layer = new MemoryLayer();
        // Cutoff 0.5 < live resolution 1 -> the finer cell is hidden, so it must
        // not clip the coarser cell (no blank hole).
        CoverageClip.Set(layer, [new FinerCoverage(Square(-50, -50, 100), CutoffResolution: 0.5)]);

        Assert.Empty(CoverageClip.BuildActiveDifferencePaths(layer, MakeViewport(), resolution: 1));
    }

    [Fact]
    public void BuildActiveDifferencePaths_CutoffAtOrAboveResolution_IsActive()
    {
        var layer = new MemoryLayer();
        // A finer cell whose content is still drawing (cutoff >= live resolution)
        // clips the coarser cell where they overlap.
        CoverageClip.Set(layer, [new FinerCoverage(Square(-50, -50, 100), CutoffResolution: 1000)]);

        Assert.Single(CoverageClip.BuildActiveDifferencePaths(layer, MakeViewport(), resolution: 1000));
    }

    [Fact]
    public void BuildActiveDifferencePaths_MixedBands_KeepsOnlyActive()
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer,
        [
            new FinerCoverage(Square(-50, -50, 40), CutoffResolution: 2.0),  // active (>=1)
            new FinerCoverage(Square(10, 10, 40), CutoffResolution: 0.25),   // dropped (<1)
        ]);

        Assert.Single(CoverageClip.BuildActiveDifferencePaths(layer, MakeViewport(), resolution: 1));
    }

    [Fact]
    public void BuildActiveDifferencePaths_PolygonWithHole_IsEvenOddWithBothRings()
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
        CoverageClip.Set(layer, [new FinerCoverage(Gf.CreatePolygon(shell, [hole]), CutoffResolution: double.MaxValue)]);

        var path = Assert.Single(CoverageClip.BuildActiveDifferencePaths(layer, MakeViewport(), resolution: 1));

        // Even-odd fill lets the inner ring punch a hole so the coarse cell still
        // shows through a finer cell's no-coverage gaps.
        Assert.Equal(SkiaSharp.SKPathFillType.EvenOdd, path.FillType);
        // Exterior (4 unique + close) and hole (4 unique + close) both emitted.
        Assert.True(path.PointCount >= 8, $"expected both rings, got {path.PointCount} points");
    }

    [Fact]
    public void Set_NullRegion_ClearsPreviousAttachment()
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer, [new FinerCoverage(Square(-50, -50, 100), CutoffResolution: double.MaxValue)]);
        Assert.NotNull(CoverageClip.Get(layer));

        CoverageClip.Set(layer, null);

        Assert.Null(CoverageClip.Get(layer));
        Assert.Empty(CoverageClip.BuildActiveDifferencePaths(layer, MakeViewport(), resolution: 1));
    }
}
