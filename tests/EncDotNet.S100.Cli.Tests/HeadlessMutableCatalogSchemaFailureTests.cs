using System.Reflection;
using EncDotNet.S100.Cli.Infrastructure;
using PureHDF;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>s100 mcp serve</c>'s catalog must skip a non-conforming HDF5 dataset (one
/// whose reader raises <c>S100DatasetSchemaException</c>) and report why in the
/// load outcome, rather than failing the <c>open_dataset</c> call.
/// </summary>
public sealed class HeadlessMutableCatalogSchemaFailureTests
{
    [Fact]
    public async Task Load_of_dataset_missing_required_grid_attribute_reports_problem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schema-failure-{Guid.NewGuid():N}.h5");
        try
        {
            WriteS102MissingGridOriginLatitude(path);
            using var catalog = new HeadlessMutableCatalog();

            var outcome = await catalog.LoadAsync(path, "S-102", TestContext.Current.CancellationToken);

            Assert.Empty(outcome.Added);
            Assert.Empty(catalog.Datasets);
            var problem = Assert.Single(outcome.Problems ?? []);
            Assert.Contains("gridOriginLatitude", problem, StringComparison.Ordinal);
            Assert.Contains("BathymetryCoverage.01", problem, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteS102MissingGridOriginLatitude(string path)
    {
        var file = new H5File
        {
            Attributes = new() { ["horizontalCRS"] = 4326 },
            ["BathymetryCoverage"] = new H5Group
            {
                ["BathymetryCoverage.01"] = new H5Group
                {
                    Attributes = new()
                    {
                        ["gridOriginLongitude"] = -1.0,
                        ["gridSpacingLatitudinal"] = 0.01,
                        ["gridSpacingLongitudinal"] = 0.01,
                        ["numPointsLatitudinal"] = 1,
                        ["numPointsLongitudinal"] = 1,
                    },
                    ["Group_001"] = new H5Group
                    {
                        ["values"] = new[] { new BathyRow { Depth = 12.5f, Uncertainty = 0.1f } },
                    },
                },
            },
        };

        file.Write(path, new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
    }

    private struct BathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
