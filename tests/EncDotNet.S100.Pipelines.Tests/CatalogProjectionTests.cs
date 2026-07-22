using EncDotNet.S100.Datasets.Pipelines.Catalog;

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
}
