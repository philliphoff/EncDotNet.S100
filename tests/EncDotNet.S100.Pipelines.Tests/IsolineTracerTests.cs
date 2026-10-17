using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="IsolineTracer"/>, which traces the S-104 water-level
/// isolines through a regular grid with marching squares.
/// </summary>
public sealed class IsolineTracerTests
{
    [Fact]
    public void A_ramp_gives_one_straight_line_per_level_at_the_interpolated_column()
    {
        // Values rise 1 per column: 0, 1, 2, 3 in every row.
        var values = Grid(3, 4, (_, c) => c);

        var lines = IsolineTracer.Trace(values, 3, 4, -9999f, [0.5, 2.25]);

        Assert.Equal(2, lines.Count);
        AssertVertical(Assert.Single(lines, l => l.Level == 0.5), column: 0.5, rows: 2);
        AssertVertical(Assert.Single(lines, l => l.Level == 2.25), column: 2.25, rows: 2);
    }

    [Fact]
    public void Levels_outside_the_values_give_no_lines()
    {
        var values = Grid(3, 3, (r, c) => r + c);

        Assert.Empty(IsolineTracer.Trace(values, 3, 3, -9999f, [-1.0, 10.0]));
    }

    [Fact]
    public void A_peak_gives_a_closed_loop()
    {
        var values = Grid(5, 5, (r, c) => r == 2 && c == 2 ? 1f : 0f);

        var line = Assert.Single(IsolineTracer.Trace(values, 5, 5, -9999f, [0.5]));

        Assert.Equal(line.Points[0], line.Points[^2], precision: 9);
        Assert.Equal(line.Points[1], line.Points[^1], precision: 9);
        for (int i = 0; i < line.Points.Length; i += 2)
        {
            Assert.InRange(line.Points[i], 1.0, 3.0);
            Assert.InRange(line.Points[i + 1], 1.0, 3.0);
        }
    }

    [Fact]
    public void Fill_values_break_the_lines()
    {
        // A ramp along the columns with the whole middle row missing: the line
        // must not cross the cells that touch it.
        var values = Grid(5, 4, (r, c) => r == 2 ? -9999f : c);

        var lines = IsolineTracer.Trace(values, 5, 4, -9999f, [1.5]);

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line =>
        {
            for (int i = 1; i < line.Points.Length; i += 2)
                Assert.True(line.Points[i] <= 1.0 + 1e-9 || line.Points[i] >= 3.0 - 1e-9, $"Row {line.Points[i]} touches the gap.");
        });
    }

    [Fact]
    public void A_coarse_grid_is_subdivided_into_many_vertices()
    {
        var values = Grid(5, 6, (r, c) => r * 0.6f + c * 0.4f);

        var line = Assert.Single(IsolineTracer.Trace(values, 5, 6, -9999f, [2.0]));

        Assert.True(line.Points.Length / 2 > 20, $"Only {line.Points.Length / 2} vertices.");
    }

    private static void AssertVertical(TracedIsoline line, double column, int rows)
    {
        for (int i = 0; i < line.Points.Length; i += 2)
            Assert.Equal(column, line.Points[i], precision: 9);

        var ys = Enumerable.Range(0, line.Points.Length / 2).Select(i => line.Points[2 * i + 1]).ToArray();
        Assert.Equal(0, ys.Min(), precision: 9);
        Assert.Equal(rows, ys.Max(), precision: 9);
    }

    private static float[] Grid(int rows, int columns, Func<int, int, float> value)
    {
        var values = new float[rows * columns];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
                values[r * columns + c] = value(r, c);
        return values;
    }
}
