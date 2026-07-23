using System.Reflection;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the shared headless catalog plumbing —
/// <see cref="LoadedDatasetProjector"/> and <see cref="FileDatasetCatalog"/> —
/// that backs the CLI <c>identify</c> command. These verify the same
/// projection the Avalonia viewer uses works from raw files with no viewer or
/// MCP server present.
/// </summary>
public sealed class CatalogProjectionTests
{
    private static string FixturePath(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", relative);

    [SkippableFact]
    public void Project_gml_dataset_yields_vector_data_with_bounds()
    {
        var path = FixturePath(Path.Combine("S411", "display_modes.gml"));
        Skip.IfNot(File.Exists(path), $"Fixture not found: {path}");

        using var stream = File.OpenRead(path);
        var projected = LoadedDatasetProjector.Project(new DatasetId("s411-1"), "S-411", stream);

        Assert.NotNull(projected);
        Assert.Equal("S-411", projected!.Spec.Name);
        Assert.IsType<S411DatasetData>(projected.Data);

        // The fixture polygon sits around 66N, 84-85W, so the extent must be a
        // real sub-world box, not the whole-world fallback.
        Assert.NotEqual(LoadedDatasetProjector.WorldBounds, projected.Bounds);
    }

    [SkippableFact]
    public void Project_s102_coverage_yields_coverage_data_with_nonworld_bounds()
    {
        var path = FixturePath("102US004MI1CI262227.h5");
        Skip.IfNot(File.Exists(path), $"Fixture not found: {path}");

        using var stream = File.OpenRead(path);
        var projected = LoadedDatasetProjector.Project(new DatasetId("s102-1"), "S-102", stream);

        Assert.NotNull(projected);
        Assert.Equal("S-102", projected!.Spec.Name);
        Assert.IsType<S102CoverageData>(projected.Data);
        Assert.NotEqual(LoadedDatasetProjector.WorldBounds, projected.Bounds);
    }

    [Fact]
    public void Project_unknown_spec_returns_null()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var projected = LoadedDatasetProjector.Project(new DatasetId("x"), "S-999", stream);

        Assert.Null(projected);
    }

    [SkippableFact]
    public void FileDatasetCatalog_build_projects_successes_and_reports_warnings()
    {
        var gml = FixturePath(Path.Combine("S411", "display_modes.gml"));
        Skip.IfNot(File.Exists(gml), $"Fixture not found: {gml}");

        var missing = FixturePath(Path.Combine("S411", "does-not-exist.gml"));

        var catalog = FileDatasetCatalog.Build(
        [
            new FileDatasetInput(new DatasetId("good"), "S-411", gml),
            new FileDatasetInput(new DatasetId("missing"), "S-411", missing),
            new FileDatasetInput(new DatasetId("unsupported"), "S-999", gml),
        ]);

        Assert.Single(catalog.Datasets);
        Assert.Equal("good", catalog.Datasets[0].Id.Value);
        Assert.Equal(2, catalog.Warnings.Count);
        Assert.Contains(catalog.Warnings, w => w.Contains("missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.Warnings, w => w.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    }

    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    /// <summary>
    /// Writes a synthetic S-102 tile georeferenced in a projected CRS
    /// (UTM zone 31N, EPSG:32631) so the grid origin/spacing are native metres.
    /// </summary>
    private static string WriteProjectedS102(int horizontalCrs)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");

        var values = new SpecBathyRow[4];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SpecBathyRow { Depth = 12.5f, Uncertainty = 0.25f };

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 5_750_000.0,
                ["gridOriginLongitude"] = 592_000.0,
                ["gridSpacingLatitudinal"] = 100.0,
                ["gridSpacingLongitudinal"] = 100.0,
                ["numPointsLatitudinal"] = 2,
                ["numPointsLongitudinal"] = 2,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };

        var file = new H5File
        {
            Attributes = new Dictionary<string, object>
            {
                ["productSpecification"] = "INT.IHO.S-102.3.0.0",
                ["horizontalCRS"] = horizontalCrs,
            },
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
        return path;
    }

    [Fact]
    public void FileDatasetCatalog_build_reprojects_projected_s102_bounds_when_transforms_supplied()
    {
        // Regression: identify passes a ProjNet transform factory to Build so a
        // projected (UTM) S-102 tile's WGS-84 bounds match the WGS-84 point-in-
        // bounds test used when sampling. Without transforms the naive fallback
        // leaves the bounds in native metres, so the tile is never matched.
        var path = WriteProjectedS102(horizontalCrs: 32631);
        try
        {
            var inputs = new[] { new FileDatasetInput(new DatasetId("s102-utm"), "S-102", path) };

            var reprojected = FileDatasetCatalog.Build(inputs, new ProjNetCrsTransformFactory());
            Assert.Single(reprojected.Datasets);
            var bounds = reprojected.Datasets[0].Bounds;

            // UTM 31N easting 592000 / northing 5_750_000 is roughly 4°E, 51.9°N.
            Assert.InRange(bounds.WestLongitude, 3.0, 6.0);
            Assert.InRange(bounds.EastLongitude, 3.0, 6.0);
            Assert.InRange(bounds.SouthLatitude, 50.0, 53.0);

            // Without a transform factory the native metres leak through, proving
            // the parameter is actually threaded into the projection step.
            var naive = FileDatasetCatalog.Build(inputs);
            Assert.True(naive.Datasets[0].Bounds.EastLongitude > 1000.0);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FileDatasetCatalog_build_falls_back_to_world_bounds_for_unsupported_s102_crs()
    {
        // An unsupported/malformed horizontal CRS must not break dataset load:
        // ProjectS102 catches the transform failure and falls back to a safe
        // world extent so the tile still loads.
        var path = WriteProjectedS102(horizontalCrs: 9999);
        try
        {
            var inputs = new[] { new FileDatasetInput(new DatasetId("s102-badcrs"), "S-102", path) };

            var catalog = FileDatasetCatalog.Build(inputs, new ProjNetCrsTransformFactory());

            Assert.Single(catalog.Datasets);
            Assert.Equal(LoadedDatasetProjector.WorldBounds, catalog.Datasets[0].Bounds);
        }
        finally { File.Delete(path); }
    }
}
