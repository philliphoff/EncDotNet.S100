using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather.Tests;

/// <summary>Builds small weather grids in memory.</summary>
internal static class TestGrids
{
    public static readonly DateTime Run = new(2026, 10, 25, 6, 0, 0, DateTimeKind.Utc);

    public static readonly WeatherLevel TenMetres = WeatherLevel.AboveGround(Length.FromMetres(10));

    public static readonly WeatherOrigin Origin = new("TEST", Run, "Test centre");

    /// <summary>A south-to-north, west-to-east grid at 1° from (0°, 0°).</summary>
    public static WeatherNativeGrid Regular(int rows, int columns) =>
        new(rows, columns, 0, 0, 1, 1);

    /// <summary>Values whose cell (r, c) is <c>r * 100 + c</c>.</summary>
    public static float[] Indexed(int rows, int columns)
    {
        var values = new float[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
                values[r * columns + c] = r * 100 + c;
        }

        return values;
    }

    public static float[] Filled(int count, float value)
    {
        var values = new float[count];
        Array.Fill(values, value);
        return values;
    }

    public static WeatherParameterDefinition Definition(string id) =>
        WeatherParameters.Find(id) ?? throw new InvalidOperationException($"No parameter '{id}'.");

    /// <summary>A parameter with one in-memory step per (hour offset, values) pair.</summary>
    public static WeatherParameter Parameter(string id, params (int Hours, float[] Values)[] steps) =>
        new(
            Definition(id),
            Definition(id).Level ?? WeatherLevel.Surface,
            steps.Select(s => WeatherTimeStep.FromValues(Run.AddHours(s.Hours), s.Values)).ToArray());

    /// <summary>A parameter whose steps count their decodes in <paramref name="counter"/>.</summary>
    public static WeatherParameter Counted(string id, DecodeCounter counter, int cells, params int[] hours) =>
        new(
            Definition(id),
            Definition(id).Level ?? WeatherLevel.Surface,
            hours.Select(h => new WeatherTimeStep(Run.AddHours(h), _ =>
            {
                counter.Record(id, h);
                return Filled(cells, h);
            })).ToArray());
}

/// <summary>Counts step decodes by parameter and hour.</summary>
internal sealed class DecodeCounter
{
    private readonly List<(string Id, int Hours)> _decodes = [];

    public IReadOnlyList<(string Id, int Hours)> Decodes => _decodes;

    public int Total => _decodes.Count;

    public int CountFor(int hours) => _decodes.Count(d => d.Hours == hours);

    public void Record(string id, int hours) => _decodes.Add((id, hours));
}
