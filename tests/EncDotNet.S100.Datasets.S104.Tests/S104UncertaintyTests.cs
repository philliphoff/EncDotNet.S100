using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Testing.Scenarios.Hdf5;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// The reader surfaces S-104 water-level uncertainty: the values record's
/// <c>uncertainty</c> member, overriding the instance's <c>uncertainty</c>
/// dataset, with −1.0 as the fill value (S-104 Ed 2.0.0 §10.2.2.4,
/// §10.2.2.5, Table 10-3).
/// </summary>
public sealed class S104UncertaintyTests
{
    private static readonly S104FixtureBuilder.GridGeoreference Grid = new(50, -1, 0.01, 0.01, 1, 2);

    private static readonly Dictionary<string, object> Root = new()
    {
        ["horizontalCRS"] = 4326,
        ["issueDate"] = "20240101",
    };

    private static S104Dataset Read<TRow>(TRow[] values, float? instanceUncertainty = null)
        where TRow : struct
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        try
        {
            S104FixtureBuilder.WriteTimeSeries(path, Grid, [("20240101T000000Z", values)], Root, instanceUncertainty: instanceUncertainty);
            using var hdf = PureHdfFile.Open(path);
            return S104DatasetReader.Read(hdf);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static S104FixtureBuilder.UncertaintyRow Row(float height, float uncertainty) =>
        new() { WaterLevelHeight = height, WaterLevelTrend = 3, Uncertainty = uncertainty };

    [Fact]
    public void Per_record_uncertainty_is_read()
    {
        var dataset = Read([Row(1.0f, 0.25f), Row(1.1f, 0.5f)]);

        var coverage = Assert.Single(dataset.Coverages);
        Assert.Equal(0.25, coverage.UncertaintyAt(0)!.Value, 6);
        Assert.Equal(0.5, coverage.UncertaintyAt(1)!.Value, 6);
    }

    [Fact]
    public void Fill_value_is_not_an_uncertainty()
    {
        var coverage = Assert.Single(Read([Row(1.0f, -1f), Row(1.1f, 0.5f)]).Coverages);

        Assert.Null(coverage.UncertaintyAt(0));
        Assert.Equal(0.5, coverage.UncertaintyAt(1)!.Value, 6);
    }

    [Fact]
    public void Instance_uncertainty_applies_where_the_record_has_none()
    {
        var coverage = Assert.Single(Read([Row(1.0f, -1f), Row(1.1f, 0.5f)], instanceUncertainty: 0.3f).Coverages);

        Assert.Equal(0.3, coverage.UncertaintyAt(0)!.Value, 6);
        Assert.Equal(0.5, coverage.UncertaintyAt(1)!.Value, 6);
    }

    [Fact]
    public void Instance_uncertainty_applies_without_a_record_member()
    {
        var rows = new[]
        {
            new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.0f, WaterLevelTrend = 3 },
            new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.1f, WaterLevelTrend = 3 },
        };

        var coverage = Assert.Single(Read(rows, instanceUncertainty: 0.2f).Coverages);

        Assert.Equal(0.2, coverage.UncertaintyAt(1)!.Value, 6);
    }

    [Fact]
    public void No_uncertainty_anywhere_is_null()
    {
        var rows = new[] { new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.0f, WaterLevelTrend = 3 } };
        var dataset = Read(rows.Concat(rows).ToArray());

        var coverage = Assert.Single(dataset.Coverages);
        Assert.Null(coverage.Uncertainties);
        Assert.Null(coverage.UncertaintyAt(0));
    }
}
