using EncDotNet.S100.Crs.ProjNet;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// <see cref="ICrsTransform"/> implementations must be safe to share between
/// threads: the route checks' S-102 and S-104 grids reproject through one
/// transform from concurrent checks.
/// </summary>
public sealed class ProjNetTransformConcurrencyTests
{
    [Theory]
    [InlineData("EPSG:4326", "EPSG:32630")]
    [InlineData("EPSG:32630", "EPSG:4326")]
    [InlineData("EPSG:32630", "EPSG:3857")]
    public void A_shared_transform_gives_the_same_answers_from_many_threads(string source, string target)
    {
        var transform = new ProjNetCrsTransformFactory().Create(source, target);
        var inputs = Enumerable.Range(0, 20_000)
            .Select(i => source == "EPSG:4326"
                ? (X: -6 + (i % 200) * 0.03, Y: 48 + (i / 200) * 0.05)
                : (X: 300_000 + (i % 200) * 1_000.0, Y: 5_300_000 + (i / 200) * 2_000.0))
            .ToArray();
        var expected = inputs.Select(p => transform.Transform(p.X, p.Y)).ToArray();

        var actual = new (double X, double Y)[inputs.Length];
        Parallel.For(0, inputs.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            actual[i] = transform.Transform(inputs[i].X, inputs[i].Y));

        Assert.Equal(expected, actual);
    }
}
