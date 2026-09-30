using EncDotNet.S100.Renderers.Mapsui;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Pins <see cref="OverscaleCurtainRegionCache"/> to
/// <see cref="OverscaleCurtain.ComputeRegions"/>: over any sequence of zoom
/// steps and cell-set changes, the memoised result must be exactly what the
/// uncached computation returns (issue #691).
/// </summary>
public class OverscaleCurtainRegionCacheTests
{
    private static readonly GeometryFactory Gf = new();

    private static readonly int[] Bands = [1_500_000, 350_000, 90_000, 22_000, 12_000];

    private static Polygon Rect(double minX, double minY, double maxX, double maxY) =>
        Gf.CreatePolygon(
        [
            new Coordinate(minX, minY),
            new Coordinate(maxX, minY),
            new Coordinate(maxX, maxY),
            new Coordinate(minX, maxY),
            new Coordinate(minX, minY),
        ]);

    private static OverscaleCellInput Cell(string name, Geometry coverage, int denominator) => new()
    {
        Name = name,
        Coverage = coverage,
        CompilationScaleDenominator = denominator,
    };

    /// <summary>Overlapping cells across several bands, near 40°N like the NY test set.</summary>
    private static OverscaleCellInput[] RandomCells(int seed, int count)
    {
        var random = new Random(seed);
        var cells = new OverscaleCellInput[count];
        for (var i = 0; i < count; i++)
        {
            var band = Bands[random.Next(Bands.Length)];
            var size = band / 20.0 * (0.5 + random.NextDouble());
            var x = random.NextDouble() * 200_000;
            var y = 4_900_000 + random.NextDouble() * 200_000;
            cells[i] = Cell($"C{i:000}", Rect(x, y, x + size, y + size), band);
        }

        return cells;
    }

    private static void AssertSame(IReadOnlyList<OverscaleRegion> expected, IReadOnlyList<OverscaleRegion> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Name, actual[i].Name);
            Assert.Equal(expected[i].Factor, actual[i].Factor);
            Assert.True(
                expected[i].Region.EqualsExact(actual[i].Region),
                $"Region for {expected[i].Name} differs.");
        }
    }

    // A zoom in from world view to street level and back out, in fine steps.
    private static IEnumerable<double> ZoomSweep()
    {
        var levels = Enumerable.Range(0, 81).Select(i => 5_000.0 / Math.Pow(2, i / 8.0)).ToArray();
        return levels.Concat(levels.Reverse());
    }

    [Theory]
    [InlineData(1, 40)]
    [InlineData(2, 120)]
    [InlineData(3, 250)]
    public void ZoomSweep_MatchesUncachedComputation_AtEveryStep(int seed, int count)
    {
        var cells = RandomCells(seed, count);
        var cache = new OverscaleCurtainRegionCache();
        var nonEmpty = 0;

        foreach (var resolution in ZoomSweep())
        {
            // A fresh snapshot list per step, as the loader returns one per call.
            var snapshot = cells.Select(c => Cell(c.Name, c.Coverage, c.CompilationScaleDenominator)).ToArray();
            var expected = OverscaleCurtain.ComputeRegions(snapshot, resolution);
            AssertSame(expected, cache.ComputeRegions(snapshot, resolution));
            if (expected.Count > 0)
                nonEmpty++;
        }

        Assert.True(nonEmpty > 10, "the sweep should exercise overscaled cells");
    }

    [Fact]
    public void CellSetChanges_InvalidateTheCache()
    {
        var cells = RandomCells(seed: 7, count: 60).ToList();
        var cache = new OverscaleCurtainRegionCache();
        const double resolution = 2.0; // most cells overscaled

        void Check() => AssertSame(
            OverscaleCurtain.ComputeRegions(cells, resolution),
            cache.ComputeRegions(cells, resolution));

        Check();

        // A finer cell loaded over existing ones changes their regions.
        cells.Add(Cell("Fine", Rect(0, 4_900_000, 200_000, 5_100_000), 5_000));
        Check();

        // Unloaded again.
        cells.RemoveAt(cells.Count - 1);
        Check();

        // Same count, one coverage replaced (a reload).
        cells[3] = Cell(cells[3].Name, Rect(10, 4_950_000, 90_000, 5_050_000), cells[3].CompilationScaleDenominator);
        Check();

        // Same coverage, different scale.
        cells[5] = Cell(cells[5].Name, cells[5].Coverage, 4_000);
        Check();

        // Renamed.
        cells[8] = Cell("Renamed", cells[8].Coverage, cells[8].CompilationScaleDenominator);
        Check();

        // Reordered (the subtraction order follows the list).
        cells.Reverse();
        Check();

        // Everything unloaded.
        cells.Clear();
        Check();
    }

    [Fact]
    public void FinerCellFullyCovering_YieldsNoRegion_OnRepeatedCalls()
    {
        var cells = new[]
        {
            Cell("Coastal", Rect(-100, -100, 100, 100), 90_000),
            Cell("Harbour", Rect(-500, -500, 500, 500), 22_000),
        };
        var cache = new OverscaleCurtainRegionCache();

        for (var i = 0; i < 3; i++)
        {
            var regions = cache.ComputeRegions(cells, 5.0);
            Assert.DoesNotContain(regions, r => r.Name == "Coastal");
            Assert.Contains(regions, r => r.Name == "Harbour");
        }
    }

    [Fact]
    public void NonPositiveResolution_ReturnsEmpty()
    {
        var cells = new[] { Cell("Coastal", Rect(-500, -500, 500, 500), 45_000) };
        var cache = new OverscaleCurtainRegionCache();

        Assert.Empty(cache.ComputeRegions(cells, 0.0));
        Assert.Empty(cache.ComputeRegions(cells, -1.0));
        Assert.Empty(cache.ComputeRegions(cells, double.NaN));
    }
}
