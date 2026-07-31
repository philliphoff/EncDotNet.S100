using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies cross-cell scale-band overlap suppression
/// (<see cref="OverlapSuppression"/>, issue #438 Phase 2): a coarser cell gathers
/// the finer, overlapping in-band cells whose coverage the renderer subtracts,
/// equal-band siblings never mutually clip, a finer cell is never clipped by a
/// coarser one, and each finer contribution carries the finer cell's zoom-out
/// cutoff so suppression is zoom-aware.
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
    public void CollectFinerCoverages_NoFinerOverlap_ReturnsNull()
    {
        var coarse = Cell(Square(0, 0, 10), 90000);
        var farFiner = Cell(Square(100, 100, 5), 10000);

        var finer = OverlapSuppression.CollectFinerCoverages(coarse, [coarse, farFiner]);

        Assert.Null(finer);
    }

    [Fact]
    public void CollectFinerCoverages_FinerPartialOverlap_IncludesFinerCoverage()
    {
        // Finer cell covers the bottom-left 5x5 quadrant of the coarse cell.
        var coarse = Cell(Square(0, 0, 10), 90000);
        var finerCell = Cell(Square(0, 0, 5), 10000);

        var finer = OverlapSuppression.CollectFinerCoverages(coarse, [coarse, finerCell]);

        Assert.NotNull(finer);
        var contribution = Assert.Single(finer!);
        Assert.Same(finerCell.Coverage, contribution.Coverage);
        // Cutoff is derived from the finer cell's own scale denominator so the
        // renderer drops it once the viewport zooms out past the finer cell's
        // content: 10000 * 0.00028 m/px at the (near-equator) coverage centroid.
        Assert.Equal(10000 * MapsuiDisplayListRenderer.DenomToResolutionMetres, contribution.CutoffResolution, 6);
    }

    [Fact]
    public void CollectFinerCoverages_MultipleFinerOverlaps_IncludesAll()
    {
        var coarse = Cell(Square(0, 0, 10), 90000);
        var finerA = Cell(Square(0, 0, 5), 10000);
        var finerB = Cell(Square(5, 5, 5), 20000);

        var finer = OverlapSuppression.CollectFinerCoverages(coarse, [coarse, finerA, finerB]);

        Assert.NotNull(finer);
        Assert.Equal(2, finer!.Count);
    }

    [Fact]
    public void CollectFinerCoverages_EqualBandSibling_DoesNotClip()
    {
        var a = Cell(Square(0, 0, 10), 90000);
        var b = Cell(Square(5, 0, 10), 90000);

        Assert.Null(OverlapSuppression.CollectFinerCoverages(a, [a, b]));
    }

    [Fact]
    public void CollectFinerCoverages_CoarserOverlap_DoesNotClipFinerCell()
    {
        // From the finer cell's perspective a coarser overlapping cell must not
        // suppress it (larger-scale-in only).
        var finerCell = Cell(Square(0, 0, 5), 10000);
        var coarse = Cell(Square(0, 0, 10), 90000);

        Assert.Null(OverlapSuppression.CollectFinerCoverages(finerCell, [finerCell, coarse]));
    }

    [Fact]
    public void CollectFinerCoverages_NullCoverageOrScale_ReturnsNull()
    {
        var noCoverage = Cell(null, 90000);
        var noScale = Cell(Square(0, 0, 10), null);
        var finerCell = Cell(Square(0, 0, 5), 10000);

        Assert.Null(OverlapSuppression.CollectFinerCoverages(noCoverage, [noCoverage, finerCell]));
        Assert.Null(OverlapSuppression.CollectFinerCoverages(noScale, [noScale, finerCell]));
    }

    [Fact]
    public void CollectFinerCoverages_FinerCellWithNullCoverage_DoesNotSuppress()
    {
        // A finer cell whose coverage has been nulled (e.g. the viewer hides it,
        // so it is no longer drawing) must not clip the coarser cell — otherwise
        // hiding the finer cell would leave the "blank hole" its content filled.
        var coarse = Cell(Square(0, 0, 10), 90000);
        var hiddenFiner = Cell(null, 10000);

        Assert.Null(OverlapSuppression.CollectFinerCoverages(coarse, [coarse, hiddenFiner]));
    }

    [Fact]
    public void CollectFinerCoverages_CutoffDerivedFromEachFinerDenominator()
    {
        // Each finer contribution carries a cutoff derived from that finer cell's
        // own denominator, so a finer cell stops suppressing exactly when its own
        // content is hidden out of scale band.
        var coarse = Cell(Square(0, 0, 10), 90000);
        var finerA = Cell(Square(0, 0, 5), 10000);
        var finerB = Cell(Square(5, 5, 5), 20000);

        var finer = OverlapSuppression.CollectFinerCoverages(coarse, [coarse, finerA, finerB]);

        Assert.NotNull(finer);
        var cutoffs = finer!.Select(f => f.CutoffResolution).OrderBy(c => c).ToList();
        Assert.Equal(10000 * MapsuiDisplayListRenderer.DenomToResolutionMetres, cutoffs[0], 6);
        Assert.Equal(20000 * MapsuiDisplayListRenderer.DenomToResolutionMetres, cutoffs[1], 6);
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
