using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="ViewerDatasetCatalog"/> covering the
/// per-spec wiring added by PR MCP-3 (S-101, S-104, S-111, S-131)
/// and the bounds correctness fix that replaced the world-bounds
/// fallback for GML specs.
/// </summary>
public class ViewerDatasetCatalogTests
{
    private const string DatasetsDir = "TestData";

    private static string Path(string spec, string fileName) =>
        System.IO.Path.Combine(DatasetsDir, spec, fileName);

    [SkippableFact]
    public void S57_entry_is_projected_as_S101()
    {
        var path = Path("S57", System.IO.Path.Combine("US5MA1BO", "US5MA1BO.000"));
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(path, "S-57");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.Equal("S-101", loaded.Spec.Name);
        Assert.IsType<S101DatasetData>(loaded.Data);
    }

    [SkippableFact]
    public void S101_entry_is_projected_as_S101()
    {
        var path = Path("S101", System.IO.Path.Combine("DATASET_FILES", "101AA00DS0003.000"));
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(path, "S-101");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.Equal("S-101", loaded.Spec.Name);
        Assert.IsType<S101DatasetData>(loaded.Data);
    }

    [SkippableFact]
    public void S104_entry_is_projected_with_real_bounds()
    {
        var path = Path("S104", "104US004SC1CP_20251217T12Z.h5");
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(path, "S-104");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.Equal("S-104", loaded.Spec.Name);
        Assert.IsType<S104CoverageData>(loaded.Data);
        AssertBoundsAreNotWorld(loaded.Bounds);
    }

    [SkippableFact]
    public void S111_entry_is_projected_with_real_bounds()
    {
        var path = Path("S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(path, "S-111");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.Equal("S-111", loaded.Spec.Name);
        Assert.IsType<S111CoverageData>(loaded.Data);
        AssertBoundsAreNotWorld(loaded.Bounds);
    }

    [SkippableFact]
    public void S131_entry_is_projected_as_S131()
    {
        var path = Path("S131", "harbour_point.gml");
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(path, "S-131");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.Equal("S-131", loaded.Spec.Name);
        Assert.IsType<S131DatasetData>(loaded.Data);
    }

    [SkippableFact]
    public void S124_entry_has_computed_bounds_not_world()
    {
        var path = Path("S124", "navwarn_point.gml");
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(path, "S-124");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.IsType<S124DatasetData>(loaded.Data);
        AssertBoundsAreNotWorld(loaded.Bounds);
    }

    [Fact]
    public void Exchange_set_entry_is_skipped()
    {
        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(
            filePath: "ignored.gml",
            productSpec: "S-124",
            source: new FakeAssetSource(),
            relativePath: "ignored.gml",
            displayName: "ignored.gml");

        loader.RaiseLoaded(entry);

        Assert.Empty(catalog.Datasets);
    }

    [Fact]
    public void Removed_entry_drops_from_snapshot()
    {
        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);

        var path = Path("S124", "navwarn_point.gml");
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var entry = new DatasetEntry(path, "S-124");
        loader.RaiseLoaded(entry);
        Assert.Single(catalog.Datasets);

        loader.RaiseRemoved(entry);
        Assert.Empty(catalog.Datasets);
    }

    private static void AssertBoundsAreNotWorld(BoundingBox bounds)
    {
        Assert.False(
            bounds.SouthLatitude == -90
                && bounds.WestLongitude == -180
                && bounds.NorthLatitude == 90
                && bounds.EastLongitude == 180,
            "Expected dataset-specific bounds; got the world fallback.");
    }

    private sealed class FakeAssetSource : IAssetSource
    {
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream());

        public void Dispose() { }
    }
}
