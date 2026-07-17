using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies cross-cell scale-band overlap suppression
/// (<see cref="OverlapSuppression"/>, issue #438 Phase 2): a coarser cell is
/// clipped to its coverage minus the union of finer, overlapping in-band cells'
/// coverage, equal-band siblings never mutually clip, and a fully-covered
/// coarser cell resolves to an empty (draw-nothing) region.
/// </summary>
public class OverlapSuppressionTests
{
    private static readonly GeometryFactory Gf = new();

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

    private static OverlapSuppressionCell Cell(Geometry? coverage, int? denom) => new()
    {
        Layers = [new MemoryLayer()],
        Coverage = coverage,
        ScaleDenominator = denom,
    };

    [Fact]
    public void ComputeClip_NoFinerOverlap_ReturnsNull()
    {
        var coarse = Cell(Square(0, 0, 10), 90000);
        var farFiner = Cell(Square(100, 100, 5), 10000);

        var clip = OverlapSuppression.ComputeClip(coarse, [coarse, farFiner]);

        Assert.Null(clip);
    }

    [Fact]
    public void ComputeClip_FinerPartialOverlap_SubtractsUnion()
    {
        // Finer cell covers the bottom-left 5x5 quadrant of the coarse cell.
        var coarse = Cell(Square(0, 0, 10), 90000);
        var finer = Cell(Square(0, 0, 5), 10000);

        var clip = OverlapSuppression.ComputeClip(coarse, [coarse, finer]);

        Assert.NotNull(clip);
        Assert.False(clip!.IsEmpty);
        // Coarse area 100 minus the 5x5 overlap (25) = 75.
        Assert.Equal(75.0, clip.Area, 6);
    }

    [Fact]
    public void ComputeClip_FinerFullyCovers_ReturnsEmpty()
    {
        var coarse = Cell(Square(0, 0, 10), 90000);
        var finer = Cell(Square(-1, -1, 12), 10000);

        var clip = OverlapSuppression.ComputeClip(coarse, [coarse, finer]);

        Assert.NotNull(clip);
        Assert.True(clip!.IsEmpty);
    }

    [Fact]
    public void ComputeClip_EqualBandSibling_DoesNotClip()
    {
        var a = Cell(Square(0, 0, 10), 90000);
        var b = Cell(Square(5, 0, 10), 90000);

        var clip = OverlapSuppression.ComputeClip(a, [a, b]);

        Assert.Null(clip);
    }

    [Fact]
    public void ComputeClip_CoarserOverlap_DoesNotClipFinerCell()
    {
        // From the finer cell's perspective a coarser overlapping cell must not
        // suppress it (larger-scale-in only).
        var finer = Cell(Square(0, 0, 5), 10000);
        var coarse = Cell(Square(0, 0, 10), 90000);

        var clip = OverlapSuppression.ComputeClip(finer, [finer, coarse]);

        Assert.Null(clip);
    }

    [Fact]
    public void ComputeClip_NullCoverageOrScale_ReturnsNull()
    {
        var noCoverage = Cell(null, 90000);
        var noScale = Cell(Square(0, 0, 10), null);
        var finer = Cell(Square(0, 0, 5), 10000);

        Assert.Null(OverlapSuppression.ComputeClip(noCoverage, [noCoverage, finer]));
        Assert.Null(OverlapSuppression.ComputeClip(noScale, [noScale, finer]));
    }

    [Fact]
    public void ComputeClip_HoleInFinerCoverage_LeavesCoarseShowingThrough()
    {
        // A finer cell with a no-coverage hole should not suppress the coarse
        // cell where the hole is, so the difference retains that hole region.
        var coarse = Cell(Square(0, 0, 10), 90000);
        var shell = Gf.CreateLinearRing(
        [
            new Coordinate(0, 0), new Coordinate(10, 0),
            new Coordinate(10, 10), new Coordinate(0, 10), new Coordinate(0, 0),
        ]);
        var hole = Gf.CreateLinearRing(
        [
            new Coordinate(4, 4), new Coordinate(6, 4),
            new Coordinate(6, 6), new Coordinate(4, 6), new Coordinate(4, 4),
        ]);
        var finerWithHole = Cell(Gf.CreatePolygon(shell, [hole]), 10000);

        var clip = OverlapSuppression.ComputeClip(coarse, [coarse, finerWithHole]);

        Assert.NotNull(clip);
        // Only the 2x2 hole (area 4) remains uncovered.
        Assert.Equal(4.0, clip!.Area, 6);
    }

    [Fact]
    public void Apply_AttachesAndClearsClipRegions()
    {
        var coarse = Cell(Square(0, 0, 10), 90000);
        var finer = Cell(Square(0, 0, 5), 10000);
        var cells = new[] { coarse, finer };

        OverlapSuppression.Apply(cells);

        Assert.NotNull(CoverageClip.Get(coarse.Layers[0]));
        Assert.Null(CoverageClip.Get(finer.Layers[0]));

        OverlapSuppression.ClearAll(cells);

        Assert.Null(CoverageClip.Get(coarse.Layers[0]));
        Assert.Null(CoverageClip.Get(finer.Layers[0]));
    }
}
