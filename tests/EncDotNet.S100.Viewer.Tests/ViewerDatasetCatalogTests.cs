using System.Collections.ObjectModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S101;
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
        AssertBoundsAreNotWorld(loaded.Bounds);
    }

    [Fact]
    public void ResolveS101Edition_parses_declared_product_specification_edition()
    {
        var dataset = S101Dataset.FromDocument(SyntheticDocument("1.0.2"));

        var edition = ViewerDatasetCatalog.ResolveS101Edition(dataset);

        Assert.Equal(new SpecVersion(1, 0, 2), edition);
    }

    [Fact]
    public void ResolveS101Edition_defaults_when_edition_absent()
    {
        var dataset = S101Dataset.FromDocument(SyntheticDocument(""));

        var edition = ViewerDatasetCatalog.ResolveS101Edition(dataset);

        Assert.Equal(default, edition);
    }

    [SkippableFact]
    public void S101_entry_surfaces_declared_edition()
    {
        var path = Path("S101", System.IO.Path.Combine("DATASET_FILES", "101AA00DS0003.000"));
        Skip.IfNot(File.Exists(path), $"Missing fixture {path}");

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(path, "S-101");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.NotEqual(default, loaded.Spec.Edition);
        Assert.Equal(new SpecVersion(1, 0, 2), loaded.Spec.Edition);
    }

    private static S101Document SyntheticDocument(string productSpecificationEdition) =>
        new()
        {
            Identification = new S101DatasetIdentification
            {
                DatasetName = "synthetic",
                ProductSpecification = "INT.IHO.S-101.1.0",
                ProductSpecificationEdition = productSpecificationEdition,
            },
            StructureInfo = new S101DatasetStructureInfo(),
            FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = ReadOnlyDictionary<uint, S101PointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = [],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };

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
    public void Exchange_set_entry_with_in_memory_payload_is_projected()
    {
        // Reads the bundled S-124 fixture and serves it from an
        // IAssetSource (the exchange-set surface). The catalog must
        // accept it even though there is no on-disk FilePath, because
        // LoadedDatasetData already carries everything downstream
        // consumers need.
        var fixture = Path("S124", "navwarn_point.gml");
        Skip.IfNot(File.Exists(fixture), $"Missing fixture {fixture}");
        var bytes = File.ReadAllBytes(fixture);

        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(
            filePath: string.Empty,
            productSpec: "S-124",
            source: new FakeAssetSource(bytes),
            relativePath: "navwarn_point.gml",
            displayName: "navwarn_point.gml");

        loader.RaiseLoaded(entry);

        var loaded = Assert.Single(catalog.Datasets);
        Assert.Equal("S-124", loaded.Spec.Name);
        Assert.IsType<S124DatasetData>(loaded.Data);
    }

    [Fact]
    public void On_disk_entry_with_missing_file_is_skipped()
    {
        var loader = new FakeDatasetLoaderService();
        using var catalog = new ViewerDatasetCatalog(loader);
        var entry = new DatasetEntry(
            filePath: "/does/not/exist.gml",
            productSpec: "S-124");

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

    [Fact]
    public void ComputeS101Bounds_returns_cell_extent_from_point_geometry()
    {
        var dataset = SynthS101WithPoints(
            (Rcid: 1u, Lat: 50.5, Lon: -1.2),
            (Rcid: 2u, Lat: 50.9, Lon: -0.8));

        var bounds = ViewerDatasetCatalog.ComputeS101Bounds(dataset);

        Assert.NotNull(bounds);
        Assert.Equal(50.5, bounds!.SouthLatitude, 6);
        Assert.Equal(-1.2, bounds.WestLongitude, 6);
        Assert.Equal(50.9, bounds.NorthLatitude, 6);
        Assert.Equal(-0.8, bounds.EastLongitude, 6);
    }

    [Fact]
    public void ComputeS101Bounds_returns_null_for_coordinate_free_cell()
    {
        var dataset = SynthS101WithPoints();

        Assert.Null(ViewerDatasetCatalog.ComputeS101Bounds(dataset));
    }

    private static S101Dataset SynthS101WithPoints(
        params (uint Rcid, double Lat, double Lon)[] points)
    {
        const int cmf = 10_000_000;
        var pointRecords = new Dictionary<uint, S101PointRecord>();
        foreach (var (rcid, lat, lon) in points)
        {
            pointRecords[rcid] = new S101PointRecord
            {
                RecordId = rcid,
                Y = (int)System.Math.Round(lat * cmf),
                X = (int)System.Math.Round(lon * cmf),
            };
        }

        var document = new S101Document
        {
            Identification = new S101DatasetIdentification { DatasetName = "synth" },
            StructureInfo = new S101DatasetStructureInfo
            {
                CoordinateMultiplicationFactorX = cmf,
                CoordinateMultiplicationFactorY = cmf,
                CoordinateMultiplicationFactorZ = 10,
            },
            FeatureTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            AttributeTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            Points = pointRecords,
            MultiPoints = ReadOnlyDictionary<uint, S101MultiPointRecord>.Empty,
            CurveSegments = ReadOnlyDictionary<uint, S101CurveSegmentRecord>.Empty,
            CompositeCurves = ReadOnlyDictionary<uint, S101CompositeCurveRecord>.Empty,
            Surfaces = ReadOnlyDictionary<uint, S101SurfaceRecord>.Empty,
            Features = [],
            InformationTypes = ReadOnlyDictionary<uint, S101InformationRecord>.Empty,
            InformationTypeCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            InformationAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            FeatureAssociationCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
            RoleCatalogue = ReadOnlyDictionary<ushort, string>.Empty,
        };
        return S101Dataset.FromDocument(document);
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
        private readonly byte[] _bytes;

        public FakeAssetSource() : this(Array.Empty<byte>()) { }

        public FakeAssetSource(byte[] bytes) { _bytes = bytes; }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));

        public void Dispose() { }
    }
}
