using System.Reflection;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Crs.ProjNet;
using PureHDF;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for issue #239 — S-102 <b>Edition 2.1</b> cells.
/// Edition 2.1 encodes the horizontal CRS under the older
/// <c>horizontalDatumValue</c> root attribute (the S-100 Part 10c
/// gridded-coverage convention) rather than Edition 3.0.0's
/// <c>horizontalCRS</c>. These cells are typically UTM-georeferenced, so they
/// previously resolved no CRS, defaulted to EPSG:4326, and rendered blank.
/// </summary>
public class S102Edition21Tests
{
    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    /// <summary>
    /// Writes a synthetic S-102 Edition 2.1 HDF5 cell: UTM Zone 17N
    /// (EPSG:32617) georeferencing carried via <c>horizontalDatumValue</c>,
    /// with a small regular grid of real depths.
    /// </summary>
    private static string WriteEdition21Utm(
        int epsg = 32617,
        double originEasting = 300_000.0,
        double originNorthing = 4_600_000.0,
        double spacing = 16.0,
        int rows = 8,
        int cols = 8,
        bool useHorizontalDatumValue = true)
    {
        var path = Path.GetTempFileName() + ".h5";

        var values = new SpecBathyRow[rows * cols];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SpecBathyRow { Depth = 4.0f + (i % 6), Uncertainty = 0.1f };

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = originNorthing,
                ["gridOriginLongitude"] = originEasting,
                ["gridSpacingLatitudinal"] = spacing,
                ["gridSpacingLongitudinal"] = spacing,
                ["numPointsLatitudinal"] = rows,
                ["numPointsLongitudinal"] = cols,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };

        var rootAttributes = new Dictionary<string, object>
        {
            ["productSpecification"] = "INT.IHO.S-102.2.1",
        };
        if (useHorizontalDatumValue)
        {
            rootAttributes["horizontalDatumReference"] = "EPSG";
            rootAttributes["horizontalDatumValue"] = epsg;
        }
        else
        {
            rootAttributes["horizontalCRS"] = epsg;
        }

        var file = new H5File
        {
            Attributes = rootAttributes,
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
        return path;
    }

    [Fact]
    public void Read_Edition21_ResolvesHorizontalCrsFromHorizontalDatumValue()
    {
        var path = WriteEdition21Utm(epsg: 32632, useHorizontalDatumValue: true);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S102DatasetReader.Read(hdf);

            Assert.Equal(32632, dataset.HorizontalCRS);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_Edition30_StillPrefersHorizontalCrs()
    {
        var path = WriteEdition21Utm(epsg: 32617, useHorizontalDatumValue: false);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S102DatasetReader.Read(hdf);

            Assert.Equal(32617, dataset.HorizontalCRS);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RenderHeadless_Edition21Utm_IsNotBlank()
    {
        var path = WriteEdition21Utm();
        using var manager = CreateCatalogueManager();
        var processor = new S102DatasetProcessor(
            path,
            manager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory());

        try
        {
            using var bitmap = await processor.RenderHeadlessAsync(256, 256);
            AssertNonBlank(bitmap);
        }
        finally
        {
            processor.Dispose();
            File.Delete(path);
        }
    }

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

        Assert.Fail("Edition 2.1 S-102 cell rendered a blank (all-white) bitmap.");
    }
}
