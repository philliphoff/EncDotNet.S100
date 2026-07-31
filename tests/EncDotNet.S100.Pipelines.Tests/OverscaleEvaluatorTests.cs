using EncDotNet.S100.Renderers.Mapsui;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies per-cell overscale evaluation (<see cref="OverscaleEvaluator"/>,
/// issue #441 / S-52 overscale indication): a cell is overscaled when the view
/// resolution is finer than the cell's compilation scale, cells outside the view
/// are excluded, the worst offender sorts first, and the factor undoes
/// web-mercator latitude distortion.
/// </summary>
public class OverscaleEvaluatorTests
{
    private static readonly GeometryFactory Gf = new();

    // Concentric squares centred near the equator so all cells share a latitude
    // (cos φ ≈ 1) and the overscale maths is easy to reason about.
    private static Polygon CentredSquare(double halfSize)
    {
        var ring = Gf.CreateLinearRing(
        [
            new Coordinate(-halfSize, -halfSize),
            new Coordinate(halfSize, -halfSize),
            new Coordinate(halfSize, halfSize),
            new Coordinate(-halfSize, halfSize),
            new Coordinate(-halfSize, -halfSize),
        ]);
        return Gf.CreatePolygon(ring);
    }

    private static OverscaleCellInput Cell(string name, double halfSize, int compilationDenominator) => new()
    {
        Name = name,
        Coverage = CentredSquare(halfSize),
        CompilationScaleDenominator = compilationDenominator,
    };

    // The compilation resolution at the (equator) coverage centre, i.e. the
    // viewport resolution at which the factor is exactly 1.0.
    private static double CompilationResolution(int denominator)
        => MapsuiDisplayListRenderer.DenominatorToResolution(denominator, 0.0);

    private static readonly Envelope WholeWorld = new(-1000, 1000, -1000, 1000);

    [Fact]
    public void Evaluate_AtCompilationScale_NotOverscaled()
    {
        var cells = new[] { Cell("Coastal", 500, 45000) };

        var report = OverscaleEvaluator.Evaluate(cells, WholeWorld, CompilationResolution(45000));

        Assert.False(report.IsOverscaled);
        Assert.Empty(report.OverscaledCells);
        Assert.Equal(0.0, report.WorstFactor);
        Assert.Equal(1, report.CellsInViewCount);
    }

    [Fact]
    public void Evaluate_ZoomedInPastCompilationScale_ReportsFactor()
    {
        var cells = new[] { Cell("Coastal", 500, 45000) };

        // Half the compilation resolution => 2x overscale.
        var report = OverscaleEvaluator.Evaluate(cells, WholeWorld, CompilationResolution(45000) / 2.0);

        Assert.True(report.IsOverscaled);
        var cell = Assert.Single(report.OverscaledCells);
        Assert.Equal("Coastal", cell.Name);
        Assert.Equal(2.0, cell.Factor, 6);
        Assert.Equal(2.0, report.WorstFactor, 6);
    }

    [Fact]
    public void Evaluate_MultipleCells_WorstFirstAndOnlyOverscaledListed()
    {
        // Nested stack: coastal (coarsest) .. innermost (finest). At res = 5 m/px:
        //   coastal  90000 -> 25.2 / 5  = 5.04x  (overscaled)
        //   harbour  22000 ->  6.16 / 5 = 1.23x  (overscaled)
        //   berth     8000 ->  2.24 / 5 = 0.45x  (in scale)
        var cells = new[]
        {
            Cell("Harbour", 300, 22000),
            Cell("Coastal", 500, 90000),
            Cell("Berth", 150, 8000),
        };

        var report = OverscaleEvaluator.Evaluate(cells, WholeWorld, 5.0);

        Assert.Equal(3, report.CellsInViewCount);
        Assert.Equal(2, report.OverscaledCells.Count);
        // Worst offender first.
        Assert.Equal("Coastal", report.OverscaledCells[0].Name);
        Assert.Equal("Harbour", report.OverscaledCells[1].Name);
        Assert.True(report.OverscaledCells[0].Factor > report.OverscaledCells[1].Factor);
        Assert.Equal(report.OverscaledCells[0].Factor, report.WorstFactor, 6);
    }

    [Fact]
    public void Evaluate_CellOutsideViewport_Excluded()
    {
        var cells = new[] { Cell("Coastal", 500, 45000) };
        var elsewhere = new Envelope(100_000, 200_000, 100_000, 200_000);

        var report = OverscaleEvaluator.Evaluate(cells, elsewhere, CompilationResolution(45000) / 4.0);

        Assert.False(report.IsOverscaled);
        Assert.Equal(0, report.CellsInViewCount);
    }

    [Fact]
    public void Evaluate_ExactlyInScale_NotFlaggedDespiteFloatNoise()
    {
        var cells = new[] { Cell("Coastal", 500, 45000) };

        // Factor == 1.0 exactly must not trip the indicator.
        var report = OverscaleEvaluator.Evaluate(cells, WholeWorld, CompilationResolution(45000));

        Assert.False(report.IsOverscaled);
    }

    [Fact]
    public void Evaluate_NonPositiveResolution_ReturnsNone()
    {
        var cells = new[] { Cell("Coastal", 500, 45000) };

        Assert.False(OverscaleEvaluator.Evaluate(cells, WholeWorld, 0.0).IsOverscaled);
        Assert.False(OverscaleEvaluator.Evaluate(cells, WholeWorld, -1.0).IsOverscaled);
        Assert.False(OverscaleEvaluator.Evaluate(cells, WholeWorld, double.NaN).IsOverscaled);
    }

    [Fact]
    public void Evaluate_LatitudeCorrection_UsesCoverageCentreLatitude()
    {
        // A cell centred at a high northern latitude. Web-mercator inflates ground
        // distance by 1/cos φ, so the compilation resolution (and thus the factor)
        // must use the coverage-centre latitude, not the equator.
        //   φ ≈ 60°N => web-mercator Y ≈ 8 399 738 m.
        const double northingY = 8_399_737.89;
        var ring = Gf.CreateLinearRing(
        [
            new Coordinate(-500, northingY - 500),
            new Coordinate(500, northingY - 500),
            new Coordinate(500, northingY + 500),
            new Coordinate(-500, northingY + 500),
            new Coordinate(-500, northingY - 500),
        ]);
        var cells = new[]
        {
            new OverscaleCellInput
            {
                Name = "North",
                Coverage = Gf.CreatePolygon(ring),
                CompilationScaleDenominator = 45000,
            },
        };
        var viewport = new Envelope(-1000, 1000, northingY - 1000, northingY + 1000);

        var latitude = MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians(northingY);
        var compilationResolution = MapsuiDisplayListRenderer.DenominatorToResolution(45000, latitude);

        var report = OverscaleEvaluator.Evaluate(cells, viewport, compilationResolution / 3.0);

        Assert.True(report.IsOverscaled);
        Assert.Equal(3.0, report.OverscaledCells[0].Factor, 3);
    }
}
