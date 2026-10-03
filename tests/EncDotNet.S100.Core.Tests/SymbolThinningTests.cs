using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Core.Tests;

/// <summary>
/// Tests for <see cref="SymbolThinning"/>: the S-98 Appendix G-1.1 grid
/// algorithm (S-111 Ed 2.0.0 §9.3.2, Eqn 9.2 / 9.3) and the point-by-point
/// method used for station series and ungeorectified meshes.
/// </summary>
public class SymbolThinningTests
{
    [Theory]
    // S-98 Appendix G-1.1 worked example: D = 36 mm, Lsmax = 30.4 mm.
    [InlineData(36.0, 30.4, 0.5, 2)]
    [InlineData(36.0, 30.4, 0.33, 3)]
    // Lsmax / D < Rmax: every cell is drawn.
    [InlineData(36.0, 10.0, 0.5, 1)]
    [InlineData(36.0, 17.9, 0.5, 1)]
    // Exactly at the ratio: the inequality R < Rmax fails with n = 1.
    [InlineData(10.0, 5.0, 0.5, 2)]
    // Strongly zoomed out: D shrinks to a pixel.
    [InlineData(1.0, 30.0, 0.5, 61)]
    public void GridIncrement_follows_Eqn_9_3(double diagonal, double maxLength, double maxRatio, int expected)
    {
        Assert.Equal(expected, SymbolThinning.GridIncrement(diagonal, maxLength, maxRatio));
    }

    [Theory]
    [InlineData(0.0, 10.0, 0.5)]
    [InlineData(10.0, 0.0, 0.5)]
    [InlineData(10.0, 10.0, 0.0)]
    [InlineData(double.NaN, 10.0, 0.5)]
    [InlineData(double.PositiveInfinity, 10.0, 0.5)]
    public void GridIncrement_degenerate_inputs_draw_every_cell(double diagonal, double maxLength, double maxRatio)
    {
        Assert.Equal(1, SymbolThinning.GridIncrement(diagonal, maxLength, maxRatio));
    }

    [Fact]
    public void GridIncrement_always_satisfies_Eqn_9_2()
    {
        foreach (double d in new[] { 0.7, 3.0, 12.5, 40.0 })
        {
            foreach (double l in new[] { 1.0, 15.1, 37.8, 98.3 })
            {
                int n = SymbolThinning.GridIncrement(d, l, 0.5);
                Assert.True(l / (n * d) < 0.5, $"D={d} L={l} n={n}");
                // ... and n is no larger than needed: n - 1 would break it.
                if (n > 1)
                    Assert.True(l / ((n - 1) * d) >= 0.5 * (n - 1) / n, $"D={d} L={l} n={n}");
            }
        }
    }

    [Fact]
    public void ThinGrid_draws_every_nth_row_and_column_from_the_seed()
    {
        // 9 × 9 cells, 10 units apart (diagonal 14.14). Symbols of length 10:
        // R = 10 / 14.14 = 0.71 ≥ 0.5, so n = 1 + fix(10 / 7.07) = 2.
        var grid = Grid(9, 9, spacing: 10);
        grid.Priority[(3 * 9) + 5] = 2; // the fastest cell seeds the lattice

        var selected = new List<int>();
        SymbolThinning.ThinGrid(
            9, 9, grid.X, grid.Y, grid.Scale, grid.Priority,
            lengthPerScale: 10, SymbolRect.Everything, maxRatio: 0.5, selected);

        var cells = selected.Select(i => (Row: i / 9, Col: i % 9)).ToList();
        Assert.Contains((3, 5), cells);
        Assert.All(cells, c => Assert.True(c.Row % 2 == 1 && c.Col % 2 == 1, $"({c.Row},{c.Col})"));
        Assert.Equal(4 * 4, cells.Count);
    }

    [Fact]
    public void ThinGrid_seeds_on_the_largest_symbol_before_priority()
    {
        var grid = Grid(9, 9, spacing: 10);
        grid.Priority[(2 * 9) + 2] = 9;            // fastest but small symbol
        grid.Scale[(4 * 9) + 7] = 1.5f;            // largest symbol
        grid.Priority[(4 * 9) + 7] = 5;

        var selected = new List<int>();
        SymbolThinning.ThinGrid(
            9, 9, grid.X, grid.Y, grid.Scale, grid.Priority,
            lengthPerScale: 10, SymbolRect.Everything, maxRatio: 0.5, selected);

        Assert.Contains((4 * 9) + 7, selected);
    }

    [Fact]
    public void ThinGrid_skips_cells_without_a_symbol_and_outside_the_displayed_field()
    {
        var grid = Grid(5, 5, spacing: 100); // far apart: n = 1
        grid.Scale[0] = float.NaN;           // no data
        grid.Scale[1] = 0;                   // no band

        var selected = new List<int>();
        var displayed = new SymbolRect(-1, -1, 250, 250); // rows/cols 0..2
        SymbolThinning.ThinGrid(
            5, 5, grid.X, grid.Y, grid.Scale, grid.Priority,
            lengthPerScale: 10, displayed, maxRatio: 0.5, selected);

        Assert.DoesNotContain(0, selected);
        Assert.DoesNotContain(1, selected);
        Assert.All(selected, i => Assert.True(i / 5 <= 2 && i % 5 <= 2));
        Assert.Equal(9 - 2, selected.Count);
    }

    [Fact]
    public void ThinGrid_with_no_symbols_in_view_selects_nothing()
    {
        var grid = Grid(3, 3, spacing: 10);
        var selected = new List<int> { 42 };
        SymbolThinning.ThinGrid(
            3, 3, grid.X, grid.Y, grid.Scale, grid.Priority,
            lengthPerScale: 10, new SymbolRect(1000, 1000, 2000, 2000), maxRatio: 0.5, selected);

        Assert.Empty(selected);
    }

    [Fact]
    public void ThinPoints_keeps_the_faster_of_two_close_points()
    {
        double[] x = [0, 5];
        double[] y = [0, 0];
        double[] length = [10, 10];
        double[] priority = [1, 3];

        var kept = new List<int>();
        SymbolThinning.ThinPoints(x, y, length, priority, maxRatio: 0.5, kept);

        Assert.Equal([1], kept);
    }

    [Fact]
    public void ThinPoints_keeps_points_beyond_the_clearance_radius()
    {
        // Clearance = L / (Rmax · √2) = 10 / 0.707 = 14.14.
        double[] x = [0, 14.2, 0];
        double[] y = [0, 0, 14.0];
        double[] length = [10, 10, 10];
        double[] priority = [1, 1, 1];

        var kept = new List<int>();
        SymbolThinning.ThinPoints(x, y, length, priority, maxRatio: 0.5, kept);

        // Equal priority: the lower index wins, so point 0 is kept, 1 is clear
        // of it and 2 is too close.
        Assert.Equal([0, 1], kept);
    }

    [Fact]
    public void ThinPoints_ignores_points_without_a_symbol()
    {
        double[] x = [0, 100, 200];
        double[] y = [0, 0, 0];
        double[] length = [double.NaN, 0, 10];
        double[] priority = [9, 9, 1];

        var kept = new List<int>();
        SymbolThinning.ThinPoints(x, y, length, priority, maxRatio: 0.5, kept);

        Assert.Equal([2], kept);
    }

    [Fact]
    public void ThinPoints_never_keeps_overlapping_symbols()
    {
        var random = new Random(1234);
        int count = 4000;
        var x = new double[count];
        var y = new double[count];
        var length = new double[count];
        var priority = new double[count];
        for (int i = 0; i < count; i++)
        {
            x[i] = random.NextDouble() * 1000;
            y[i] = random.NextDouble() * 1000;
            length[i] = 15 + random.NextDouble() * 30;
            priority[i] = random.NextDouble();
        }

        var kept = new List<int>();
        SymbolThinning.ThinPoints(x, y, length, priority, maxRatio: 0.5, kept);

        Assert.NotEmpty(kept);
        for (int a = 0; a < kept.Count; a++)
        {
            for (int b = a + 1; b < kept.Count; b++)
            {
                int i = kept[a], j = kept[b];
                double d = Math.Sqrt(((x[i] - x[j]) * (x[i] - x[j])) + ((y[i] - y[j]) * (y[i] - y[j])));
                // Two arrows centred on their pivots touch only when their
                // centres are closer than half their summed lengths.
                Assert.True(d > (length[i] + length[j]) / 2, $"{i} and {j} overlap at {d:F1}");
            }
        }
    }

    [Fact]
    public void ThinPoints_and_ThinGrid_share_the_minimum_spacing()
    {
        // The same 20 × 20 lattice, thinned both ways: the grid method's nearest
        // drawn neighbours (along an axis) and the point method's clearance are
        // both at least Lsmax / (Rmax · √2).
        const int n = 20;
        var grid = Grid(n, n, spacing: 10);
        const double lengthPerScale = 15; // n = 1 + fix(15 / 7.07) = 3
        double minimum = lengthPerScale / (0.5 * Math.Sqrt(2));

        var gridSelected = new List<int>();
        SymbolThinning.ThinGrid(
            n, n, grid.X, grid.Y, grid.Scale, grid.Priority,
            lengthPerScale, SymbolRect.Everything, maxRatio: 0.5, gridSelected);

        var length = grid.Scale.Select(s => s * lengthPerScale).ToArray();
        var priority = grid.Priority.Select(p => (double)p).ToArray();
        var pointsKept = new List<int>();
        SymbolThinning.ThinPoints(grid.X, grid.Y, length, priority, maxRatio: 0.5, pointsKept);

        Assert.Equal(7 * 7, gridSelected.Count);
        Assert.True(MinDistance(grid.X, grid.Y, gridSelected) >= minimum);
        Assert.True(MinDistance(grid.X, grid.Y, pointsKept) >= minimum);
        // Both stay in the same density regime (the point method packs a
        // lattice somewhat tighter, since its spacing is not rounded up to a
        // whole number of cells).
        Assert.InRange(pointsKept.Count, gridSelected.Count, gridSelected.Count * 2);
    }

    private static double MinDistance(double[] x, double[] y, List<int> indices)
    {
        double min = double.PositiveInfinity;
        for (int a = 0; a < indices.Count; a++)
        {
            for (int b = a + 1; b < indices.Count; b++)
            {
                int i = indices[a], j = indices[b];
                min = Math.Min(min, Math.Sqrt(((x[i] - x[j]) * (x[i] - x[j])) + ((y[i] - y[j]) * (y[i] - y[j]))));
            }
        }
        return min;
    }

    private static (double[] X, double[] Y, float[] Scale, float[] Priority) Grid(int rows, int cols, double spacing)
    {
        int count = rows * cols;
        var x = new double[count];
        var y = new double[count];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                x[(r * cols) + c] = c * spacing;
                y[(r * cols) + c] = r * spacing;
            }
        }

        var scale = Enumerable.Repeat(1f, count).ToArray();
        var priority = Enumerable.Repeat(1f, count).ToArray();
        return (x, y, scale, priority);
    }
}
