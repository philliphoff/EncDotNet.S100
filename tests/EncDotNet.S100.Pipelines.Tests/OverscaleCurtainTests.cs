using EncDotNet.S100.Renderers.Mapsui;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the on-chart overscale-curtain geometry (<see cref="OverscaleCurtain"/>,
/// issue #441 / S-52 <c>AP(OVERSC01)</c> Form A): only overscaled cells get a
/// region, a strictly-finer overlapping cell is subtracted so its in-scale
/// footprint stays curtain-free, equal-band siblings are not subtracted, and the
/// regions sort worst-offender first.
/// </summary>
public class OverscaleCurtainTests
{
    private static readonly GeometryFactory Gf = new();

    // Axis-aligned rectangles near the equator (cos φ ≈ 1) so the overscale
    // maths is easy to reason about.
    private static Polygon Rect(double minX, double minY, double maxX, double maxY)
    {
        var ring = Gf.CreateLinearRing(
        [
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        ]);
        return Gf.CreatePolygon(ring);
    }

    private static OverscaleCellInput Cell(string name, Polygon coverage, int compilationDenominator) => new()
    {
        Name = name,
        Coverage = coverage,
        CompilationScaleDenominator = compilationDenominator,
    };

    // The compilation resolution at the (equator) coverage centre, i.e. the
    // viewport resolution at which the factor is exactly 1.0.
    private static double CompilationResolution(int denominator)
        => MapsuiDisplayListRenderer.DenominatorToResolution(denominator, 0.0);

    [Fact]
    public void ComputeRegions_InScale_ReturnsEmpty()
    {
        var cells = new[] { Cell("Coastal", Rect(-500, -500, 500, 500), 45000) };

        var regions = OverscaleCurtain.ComputeRegions(cells, CompilationResolution(45000));

        Assert.Empty(regions);
    }

    [Fact]
    public void ComputeRegions_Overscaled_ReturnsFullCoverageWhenNoFinerCell()
    {
        var coverage = Rect(-500, -500, 500, 500);
        var cells = new[] { Cell("Coastal", coverage, 45000) };

        // Half the compilation resolution => 2x overscale.
        var regions = OverscaleCurtain.ComputeRegions(cells, CompilationResolution(45000) / 2.0);

        var region = Assert.Single(regions);
        Assert.Equal("Coastal", region.Name);
        Assert.Equal(2.0, region.Factor, 6);
        // No finer cell => the region is the whole coverage.
        Assert.Equal(coverage.Area, region.Region.Area, 3);
    }

    [Fact]
    public void ComputeRegions_FinerCellFootprint_IsSubtracted()
    {
        // Coastal (coarse) fully contains Harbour (fine). At a resolution where
        // both are overscaled, the coastal curtain must exclude the harbour
        // footprint (a finer cell draws on top there), while the harbour keeps
        // its own full region.
        var coastal = Rect(-500, -500, 500, 500);   // area 1_000_000
        var harbour = Rect(-100, -100, 100, 100);   // area 160_000
        var cells = new[]
        {
            Cell("Coastal", coastal, 90000),
            Cell("Harbour", harbour, 22000),
        };

        var regions = OverscaleCurtain.ComputeRegions(cells, 5.0);

        Assert.Equal(2, regions.Count);
        // Worst offender (coastal) first.
        Assert.Equal("Coastal", regions[0].Name);
        Assert.Equal("Harbour", regions[1].Name);

        // Coastal region = coastal area minus harbour area (a hole).
        Assert.Equal(coastal.Area - harbour.Area, regions[0].Region.Area, 3);
        // Coastal region must not contain the harbour centre.
        Assert.False(regions[0].Region.Contains(Gf.CreatePoint(new Coordinate(0, 0))));
        // Harbour region is its full footprint (no finer cell over it).
        Assert.Equal(harbour.Area, regions[1].Region.Area, 3);
    }

    [Fact]
    public void ComputeRegions_FinerCellInScale_StillSubtractedFromOverscaledCoarser()
    {
        // Coastal is overscaled; the finer harbour is exactly in scale (not
        // overscaled itself). The harbour footprint must still be curtain-free
        // because the finer, in-scale cell draws on top there.
        var coastal = Rect(-500, -500, 500, 500);
        var harbour = Rect(-100, -100, 100, 100);

        // Choose a resolution: overscaled for coastal, in-scale for harbour.
        var resolution = CompilationResolution(22000); // harbour factor == 1.0 (in scale)
        var cells = new[]
        {
            Cell("Coastal", coastal, 90000),
            Cell("Harbour", harbour, 22000),
        };

        var regions = OverscaleCurtain.ComputeRegions(cells, resolution);

        // Only coastal is overscaled, but its region excludes the harbour.
        var region = Assert.Single(regions);
        Assert.Equal("Coastal", region.Name);
        Assert.Equal(coastal.Area - harbour.Area, region.Region.Area, 3);
    }

    [Fact]
    public void ComputeRegions_EqualBandSiblings_NotSubtracted()
    {
        // Two overlapping cells of the same compilation scale must not subtract
        // from one another (equal band is not "strictly finer").
        var west = Rect(-500, -500, 100, 500);
        var east = Rect(-100, -500, 500, 500);
        var cells = new[]
        {
            Cell("West", west, 45000),
            Cell("East", east, 45000),
        };

        var regions = OverscaleCurtain.ComputeRegions(cells, CompilationResolution(45000) / 2.0);

        Assert.Equal(2, regions.Count);
        Assert.Equal(west.Area, regions.Single(r => r.Name == "West").Region.Area, 3);
        Assert.Equal(east.Area, regions.Single(r => r.Name == "East").Region.Area, 3);
    }

    [Fact]
    public void ComputeRegions_FinerCellFullyCoversCoarser_YieldsNoCoarserRegion()
    {
        // A finer cell that fully covers the overscaled coarser cell leaves it
        // with no visible (and therefore no curtain) area.
        var coastal = Rect(-100, -100, 100, 100);
        var harbour = Rect(-500, -500, 500, 500); // fully contains coastal
        var cells = new[]
        {
            Cell("Coastal", coastal, 90000),
            Cell("Harbour", harbour, 22000),
        };

        var regions = OverscaleCurtain.ComputeRegions(cells, 5.0);

        // Coastal is fully covered by the finer harbour => no coastal region.
        Assert.DoesNotContain(regions, r => r.Name == "Coastal");
        Assert.Contains(regions, r => r.Name == "Harbour");
    }

    [Fact]
    public void ComputeRegions_NonPositiveResolution_ReturnsEmpty()
    {
        var cells = new[] { Cell("Coastal", Rect(-500, -500, 500, 500), 45000) };

        Assert.Empty(OverscaleCurtain.ComputeRegions(cells, 0.0));
        Assert.Empty(OverscaleCurtain.ComputeRegions(cells, -1.0));
        Assert.Empty(OverscaleCurtain.ComputeRegions(cells, double.NaN));
    }
}
