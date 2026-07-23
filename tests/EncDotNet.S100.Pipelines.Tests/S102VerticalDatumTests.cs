using System.Reflection;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests that the S-102 reader surfaces the real <c>verticalDatum</c> root
/// attribute (S-102 Ed 3.0.0 §12.3) instead of a hard-coded placeholder, and
/// that the coverage source resolves it to the S-100 register label.
/// </summary>
public class S102VerticalDatumTests
{
    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    private static string WriteFile(int? verticalDatum)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");

        var values = new SpecBathyRow[4];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SpecBathyRow { Depth = 12.5f, Uncertainty = 0.25f };

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = 50.0,
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
                ["numPointsLatitudinal"] = 2,
                ["numPointsLongitudinal"] = 2,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };

        var rootAttributes = new Dictionary<string, object>
        {
            ["productSpecification"] = "INT.IHO.S-102.3.0.0",
            ["horizontalCRS"] = 4326,
        };
        if (verticalDatum is int vd)
        {
            rootAttributes["verticalDatum"] = vd;
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
    public void Read_ParsesVerticalDatumCode()
    {
        var path = WriteFile(verticalDatum: 10);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S102DatasetReader.Read(hdf);

            Assert.Equal(10, dataset.VerticalDatum);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_MissingVerticalDatum_IsNull()
    {
        var path = WriteFile(verticalDatum: null);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S102DatasetReader.Read(hdf);

            Assert.Null(dataset.VerticalDatum);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CoverageSource_SurfacesResolvedDatumLabel()
    {
        var path = WriteFile(verticalDatum: 10);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S102DatasetReader.Read(hdf);
            var source = new S102CoverageSource(dataset);

            Assert.Equal("Approximate Lowest Astronomical Tide", source.Metadata.VerticalDatum);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CoverageSource_MissingDatum_ReportsUnknown()
    {
        var path = WriteFile(verticalDatum: null);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S102DatasetReader.Read(hdf);
            var source = new S102CoverageSource(dataset);

            Assert.Equal("Unknown", source.Metadata.VerticalDatum);
        }
        finally { File.Delete(path); }
    }
}
