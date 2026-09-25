using System.Reflection;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using PureHDF;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Coverage extents for projected (UTM) grids. S-100 Part 10c grid
/// georeferencing is in the grid's native CRS, so for a UTM tile the
/// <c>gridOrigin*</c> / <c>gridSpacing*</c> values — and therefore
/// <see cref="CoverageMetadata.NativeExtent"/> and
/// <see cref="DatasetMetadata.Extent"/> — are metres. These tests pin the
/// contract that native extents stay native, that
/// <c>GetGeographicExtent</c> yields WGS-84 degrees, and that every consumer
/// needing degrees (fallback viewports, headless render bounds, the S-111
/// fallback extent, catalogue bounds) reprojects rather than reading metres
/// as latitude/longitude.
/// </summary>
public class ProjectedCoverageExtentTests
{
    // UTM zone 17N (central meridian 81°W). Easting 300 km / northing 4600 km
    // is roughly 41.5°N, 83.4°W (western Lake Erie).
    private const int Utm17N = 32617;
    private const double OriginEasting = 300_000.0;
    private const double OriginNorthing = 4_600_000.0;

    private static readonly ProjNetCrsTransformFactory Factory = new();

    // ---- CoverageMetadata / DatasetMetadata helpers -------------------------

    [Fact]
    public void CoverageMetadata_GetGeographicExtent_GeographicGrid_ReturnsNativeExtentWithoutTransform()
    {
        var native = new BoundingBox(41.0, -83.0, 41.5, -82.5);
        var metadata = BuildCoverageMetadata("4326", native);

        var extent = metadata.GetGeographicExtent(new ThrowingFactory());

        Assert.Same(native, extent);
    }

    [Fact]
    public void CoverageMetadata_GetGeographicExtent_UtmGrid_IsEnvelopeOfAllFourReprojectedCorners()
    {
        var native = new BoundingBox(OriginNorthing, OriginEasting, OriginNorthing + 50_000, OriginEasting + 50_000);
        var metadata = BuildCoverageMetadata(Utm17N.ToString(), native);

        var extent = metadata.GetGeographicExtent(Factory);

        var toWgs84 = Factory.Create($"EPSG:{Utm17N}", "EPSG:4326");
        var corners = new[]
        {
            toWgs84.Transform(native.WestLongitude, native.SouthLatitude),
            toWgs84.Transform(native.WestLongitude, native.NorthLatitude),
            toWgs84.Transform(native.EastLongitude, native.SouthLatitude),
            toWgs84.Transform(native.EastLongitude, native.NorthLatitude),
        };
        Assert.Equal(corners.Min(c => c.Y), extent.SouthLatitude, 9);
        Assert.Equal(corners.Max(c => c.Y), extent.NorthLatitude, 9);
        Assert.Equal(corners.Min(c => c.X), extent.WestLongitude, 9);
        Assert.Equal(corners.Max(c => c.X), extent.EastLongitude, 9);

        // West of the central meridian, grid north diverges from true north,
        // so the SW/NE diagonal alone would clip the envelope: the NW corner
        // lies further west than the SW corner.
        var (swLon, _) = corners[0];
        Assert.True(extent.WestLongitude < swLon);

        AssertNearLakeErie(extent);
    }

    [Fact]
    public void DatasetMetadata_GetGeographicExtent_NullExtent_ReturnsNull()
    {
        var metadata = new DatasetMetadata { Spec = new SpecRef("S-102", default), HorizontalCrsEpsg = Utm17N };

        Assert.Null(metadata.GetGeographicExtent(new ThrowingFactory()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4326)]
    public void DatasetMetadata_GetGeographicExtent_GeographicCrs_ReturnsExtentWithoutTransform(int? epsg)
    {
        var extent = new BoundingBox(41.0, -83.0, 41.5, -82.5);
        var metadata = new DatasetMetadata
        {
            Spec = new SpecRef("S-101", default),
            Extent = extent,
            HorizontalCrsEpsg = epsg,
        };

        Assert.Same(extent, metadata.GetGeographicExtent(new ThrowingFactory()));
    }

    [Fact]
    public void DatasetMetadata_GetGeographicExtent_UtmCrs_Reprojects()
    {
        var metadata = new DatasetMetadata
        {
            Spec = new SpecRef("S-102", default),
            Extent = new BoundingBox(OriginNorthing, OriginEasting, OriginNorthing + 1_000, OriginEasting + 1_000),
            HorizontalCrsEpsg = Utm17N,
        };

        AssertNearLakeErie(metadata.GetGeographicExtent(Factory)!);
    }

    // ---- S-102 --------------------------------------------------------------

    [Fact]
    public void S102_Metadata_UtmGrid_ExtentStaysNativeAndGeographicExtentIsDegrees()
    {
        var path = WriteUtmS102();
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor(path, manager, new MoonSharpLuaEngine(), Factory);
        try
        {
            var metadata = processor.Metadata;

            // DatasetMetadata.Extent is native (metres), labelled by the CRS
            // code — the same value S102DatasetReader.ReadMetadata probes.
            Assert.Equal(Utm17N, metadata.HorizontalCrsEpsg);
            Assert.Equal(OriginNorthing, metadata.Extent!.SouthLatitude, 6);
            Assert.Equal(OriginEasting, metadata.Extent.WestLongitude, 6);

            AssertNearLakeErie(metadata.GetGeographicExtent(Factory)!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task S102_BuildCoveragePortrayal_WithoutViewport_FramesGridInDegrees()
    {
        var path = WriteUtmS102();
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor(path, manager, new MoonSharpLuaEngine(), Factory);
        try
        {
            var result = await processor.BuildCoveragePortrayalAsync();

            var grid = Assert.IsType<GridCoverageSubLayer>(Assert.Single(result.SubLayers));
            AssertViewportNearLakeErie(grid.Viewport);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- S-104 --------------------------------------------------------------

    [Fact]
    public async Task S104_BuildCoveragePortrayal_UtmGrid_FramesGridInDegrees()
    {
        var path = WriteUtmS104();
        var processor = new S104DatasetProcessor(path, Factory);
        try
        {
            var result = await processor.BuildCoveragePortrayalAsync();

            var grid = result.SubLayers.OfType<GridCoverageSubLayer>().Single();
            AssertViewportNearLakeErie(grid.Viewport);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task S104_RenderHeadless_UtmGrid_IsNotBlank()
    {
        var path = WriteUtmS104();
        var processor = new S104DatasetProcessor(path, Factory);
        try
        {
            using var bitmap = await processor.RenderHeadlessAsync(128, 128);
            AssertNonBlank(bitmap);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void S104_CatalogBounds_UtmGrid_AreWgs84()
    {
        var path = WriteUtmS104();
        try
        {
            using var stream = File.OpenRead(path);
            var dataset = LoadedDatasetProjector.Project(new DatasetId("s104-utm"), "S-104", stream, transforms: Factory);

            Assert.NotNull(dataset);
            AssertNearLakeErie(dataset!.Bounds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- S-111 --------------------------------------------------------------

    [Fact]
    public async Task S111_BuildCoveragePortrayal_UtmGrid_ViewportAndFallbackExtentAreDegrees()
    {
        var path = WriteUtmS111();
        using var manager = CreateCatalogueManager();
        var processor = new S111DatasetProcessor(path, manager, Factory);
        try
        {
            var result = await processor.BuildCoveragePortrayalAsync();

            var arrows = result.SubLayers.OfType<ArrowCoverageSubLayer>().Single();
            AssertViewportNearLakeErie(arrows.Viewport);
            AssertNearLakeErie(new BoundingBox(
                arrows.FallbackExtent.MinLatitude,
                arrows.FallbackExtent.MinLongitude,
                arrows.FallbackExtent.MaxLatitude,
                arrows.FallbackExtent.MaxLongitude));
        }
        finally
        {
            processor.Dispose();
            File.Delete(path);
        }
    }

    [Fact]
    public void S111_CatalogBounds_UtmGrid_AreWgs84()
    {
        var path = WriteUtmS111();
        try
        {
            using var stream = File.OpenRead(path);
            var dataset = LoadedDatasetProjector.Project(new DatasetId("s111-utm"), "S-111", stream, transforms: Factory);

            Assert.NotNull(dataset);
            AssertNearLakeErie(dataset!.Bounds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- Fixtures and assertions --------------------------------------------

    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    /// <summary>
    /// Writes a synthetic S-102 Edition 3.0.0 cell georeferenced in UTM 17N:
    /// an 8×8 grid at 16 m spacing, with real depths.
    /// </summary>
    private static string WriteUtmS102()
    {
        const int rows = 8, cols = 8;
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");

        var values = new SpecBathyRow[rows * cols];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SpecBathyRow { Depth = 4.0f + (i % 6), Uncertainty = 0.1f };

        var file = new H5File
        {
            Attributes = new()
            {
                ["productSpecification"] = "INT.IHO.S-102.3.0.0",
                ["horizontalCRS"] = Utm17N,
            },
            ["BathymetryCoverage"] = new H5Group
            {
                ["BathymetryCoverage.01"] = new H5Group
                {
                    Attributes = new()
                    {
                        ["gridOriginLatitude"] = OriginNorthing,
                        ["gridOriginLongitude"] = OriginEasting,
                        ["gridSpacingLatitudinal"] = 16.0,
                        ["gridSpacingLongitudinal"] = 16.0,
                        ["numPointsLatitudinal"] = rows,
                        ["numPointsLongitudinal"] = cols,
                    },
                    ["Group_001"] = new H5Group { ["values"] = values },
                },
            },
        };

        file.Write(path, new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name));
        return path;
    }

    private static string WriteUtmS104()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        var values = Enumerable.Range(0, 16)
            .Select(i => new S104FixtureBuilder.SpecRow { WaterLevelHeight = 0.5f + i * 0.1f, WaterLevelTrend = 1 })
            .ToArray();
        return S104FixtureBuilder.WriteFile(
            path, values, 4, 4, useF64GridAttrs: true, useUnsignedCounts: false,
            projected: new S104FixtureBuilder.ProjectedGrid(Utm17N, OriginNorthing, OriginEasting, 500.0));
    }

    private static string WriteUtmS111()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");
        var values = Enumerable.Range(0, 16)
            .Select(i => new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.0f + i * 0.1f, SurfaceCurrentDirection = 45f })
            .ToArray();
        return S111FixtureBuilder.WriteFile(
            path, values, 4, 4, useF64GridAttrs: true, useUnsignedCounts: false,
            projected: new S111FixtureBuilder.ProjectedGrid(Utm17N, OriginNorthing, OriginEasting, 500.0));
    }

    private static CoverageMetadata BuildCoverageMetadata(string crs, BoundingBox nativeExtent) => new()
    {
        Spec = new SpecRef("S-102", default),
        NativeExtent = nativeExtent,
        GridMetadata = new GridMetadata
        {
            NumRows = 10,
            NumColumns = 10,
            OriginLatitude = nativeExtent.SouthLatitude,
            OriginLongitude = nativeExtent.WestLongitude,
            SpacingLatitudinal = (nativeExtent.NorthLatitude - nativeExtent.SouthLatitude) / 10,
            SpacingLongitudinal = (nativeExtent.EastLongitude - nativeExtent.WestLongitude) / 10,
        },
        HorizontalCRS = crs,
        VerticalDatum = "MSL",
        NoDataValue = 1_000_000f,
        ValueFields = [],
    };

    private static void AssertNearLakeErie(BoundingBox extent)
    {
        Assert.InRange(extent.SouthLatitude, 41.0, 42.5);
        Assert.InRange(extent.NorthLatitude, 41.0, 42.5);
        Assert.InRange(extent.WestLongitude, -84.0, -82.5);
        Assert.InRange(extent.EastLongitude, -84.0, -82.5);
        Assert.True(extent.SouthLatitude < extent.NorthLatitude);
        Assert.True(extent.WestLongitude < extent.EastLongitude);
    }

    private static void AssertViewportNearLakeErie(Viewport viewport) =>
        AssertNearLakeErie(new BoundingBox(
            viewport.MinLatitude, viewport.MinLongitude, viewport.MaxLatitude, viewport.MaxLongitude));

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }

    private static void AssertNonBlank(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red != 255 || p.Green != 255 || p.Blue != 255)
                    return;
            }

        Assert.Fail("Projected coverage rendered a blank (all-white) bitmap.");
    }

    /// <summary>Fails the test if a geographic extent is needlessly reprojected.</summary>
    private sealed class ThrowingFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) =>
            throw new InvalidOperationException($"Unexpected transform {sourceCrs} → {targetCrs}.");
    }
}
