using EncDotNet.S100.DataModel;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
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
        Rank = denom is int scale ? new CellRank(scale) : null,
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
    public void CollectFinerCoverages_CutoffDenominator_OverridesRankForCutoff()
    {
        // An S-57 cell ranks by its compilation scale (10000) but draws out to
        // its largest SCAMIN (40000): it keeps suppressing until that window.
        var coarse = Cell(Square(0, 0, 10), 90000);
        var finerCell = new OverlapSuppressionCell
        {
            Layers = [new MemoryLayer()],
            Coverage = Square(0, 0, 5),
            Rank = new CellRank(10000),
            CutoffScaleDenominator = 40000,
        };

        var finer = OverlapSuppression.CollectFinerCoverages(coarse, [coarse, finerCell]);

        var contribution = Assert.Single(finer!);
        Assert.Equal(40000 * MapsuiDisplayListRenderer.DenomToResolutionMetres, contribution.CutoffResolution, 6);
    }

    [Fact]
    public void CollectFinerCoverages_RanksByScaleDenominator_NotCutoff()
    {
        // The coarse cell's cutoff (5000) is smaller than the finer cell's rank,
        // but ranking uses the rank only, so the coarse cell is not
        // treated as finer.
        var coarse = new OverlapSuppressionCell
        {
            Layers = [new MemoryLayer()],
            Coverage = Square(0, 0, 10),
            Rank = new CellRank(90000),
            CutoffScaleDenominator = 5000,
        };
        var finerCell = Cell(Square(0, 0, 5), 10000);

        Assert.Null(OverlapSuppression.CollectFinerCoverages(finerCell, [coarse, finerCell]));
        Assert.Single(OverlapSuppression.CollectFinerCoverages(coarse, [coarse, finerCell])!);
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
    public void CollectFinerCoverages_SameMinimumFinerMaximum_Clips()
    {
        // IC-ENC Cowes: harbour 101GB00502038 (1:22 000 to 1:12 000) under
        // berthing 101GB00602793 (1:22 000 to 1:3 500). As peers both drew,
        // doubling every label; the finer maximum now suppresses the harbour
        // cell, out to the shared 1:22 000 window.
        var harbour = new OverlapSuppressionCell { Layers = [new MemoryLayer()], Coverage = Square(0, 0, 10), Rank = new CellRank(22000, 12000) };
        var berthing = new OverlapSuppressionCell { Layers = [new MemoryLayer()], Coverage = Square(0, 0, 5), Rank = new CellRank(22000, 3500) };

        var contribution = Assert.Single(OverlapSuppression.CollectFinerCoverages(harbour, [harbour, berthing])!);
        Assert.Same(berthing.Coverage, contribution.Coverage);
        Assert.Equal(22000 * MapsuiDisplayListRenderer.DenomToResolutionMetres, contribution.CutoffResolution, 6);
        Assert.Null(OverlapSuppression.CollectFinerCoverages(berthing, [harbour, berthing]));
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
