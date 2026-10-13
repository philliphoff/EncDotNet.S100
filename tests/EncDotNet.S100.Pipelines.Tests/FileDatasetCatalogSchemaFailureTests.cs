using System.Reflection;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A non-conforming HDF5 dataset (one whose reader raises
/// <c>S100DatasetSchemaException</c>) must be skipped and reported in
/// <see cref="FileDatasetCatalog.Warnings"/> rather than aborting the whole
/// build — <c>s100 identify</c> and headless catalogs rely on that.
/// </summary>
public sealed class FileDatasetCatalogSchemaFailureTests
{
    [Fact]
    public void Build_skips_dataset_missing_required_grid_attribute_and_reports_warning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schema-failure-{Guid.NewGuid():N}.h5");
        var gml = Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "display_modes.gml");
        Assert.SkipUnless(File.Exists(gml), $"Fixture not found: {gml}");
        try
        {
            WriteS102MissingGridOriginLatitude(path);

            var catalog = FileDatasetCatalog.Build(
            [
                new FileDatasetInput(new DatasetId("bad"), "S-102", path),
                new FileDatasetInput(new DatasetId("good"), "S-411", gml),
            ]);

            var loaded = Assert.Single(catalog.Datasets);
            Assert.Equal("good", loaded.Id.Value);
            var warning = Assert.Single(catalog.Warnings);
            Assert.Contains(path, warning, StringComparison.Ordinal);
            Assert.Contains("gridOriginLatitude", warning, StringComparison.Ordinal);
            Assert.Contains("BathymetryCoverage.01", warning, StringComparison.Ordinal);
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
