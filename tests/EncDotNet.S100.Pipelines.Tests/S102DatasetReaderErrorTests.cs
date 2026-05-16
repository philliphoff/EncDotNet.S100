using System.Reflection;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// PR-B: verifies that the S-102 reader translates HDF5-backend
/// "missing attribute" failures into typed, contextual exceptions.
/// </summary>
public class S102DatasetReaderErrorTests
{
    [Fact]
    public void Read_MissingGridOriginLatitude_ThrowsSchemaExceptionWithContext()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var instance = new H5Group
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
                    ["values"] = new[] { new SpecBathyRow { Depth = 12.5f, Uncertainty = 0.1f } },
                },
            };

            var file = new H5File
            {
                Attributes = new() { ["horizontalCRS"] = 4326 },
                ["BathymetryCoverage"] = new H5Group
                {
                    ["BathymetryCoverage.01"] = instance,
                },
            };

            var options = new H5WriteOptions(
                FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
            file.Write(path, options);

            using var hdf = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetSchemaException>(() => S102DatasetReader.Read(hdf));

            Assert.Equal("S-102", ex.Product);
            Assert.EndsWith("/BathymetryCoverage/BathymetryCoverage.01", ex.GroupPath, StringComparison.Ordinal);
            Assert.Equal("gridOriginLatitude", ex.AttributeOrDataset);
            Assert.NotNull(ex.InnerException);
            Assert.Contains("S-102", ex.Message);
        }
        finally { File.Delete(path); }
    }

    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }
}
