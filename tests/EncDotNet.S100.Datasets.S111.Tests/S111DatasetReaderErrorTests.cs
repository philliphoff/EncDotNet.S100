using System.Reflection;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// PR-B: verifies that the S-111 reader translates HDF5-backend
/// "missing attribute" failures and the data-coding-format guard
/// into typed, contextual exceptions.
/// </summary>
public class S111DatasetReaderErrorTests
{
    [Fact]
    public void Read_MissingGridOriginLatitude_ThrowsSchemaExceptionWithContext()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var attrs = new Dictionary<string, object>
            {
                ["gridOriginLongitude"] = -1.0,
                ["gridSpacingLatitudinal"] = 0.01,
                ["gridSpacingLongitudinal"] = 0.01,
                ["numPointsLatitudinal"] = 1,
                ["numPointsLongitudinal"] = 1,
            };

            var instance = new H5Group
            {
                Attributes = attrs,
                ["Group_001"] = new H5Group
                {
                    Attributes = new() { ["timePoint"] = "20210401T000000Z" },
                    ["values"] = new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.0f, SurfaceCurrentDirection = 45f } },
                },
            };

            var file = new H5File
            {
                Attributes = new() { ["horizontalDatumValue"] = 4326 },
                ["SurfaceCurrent"] = new H5Group
                {
                    Attributes = new() { ["dataCodingFormat"] = (byte)2 },
                    ["SurfaceCurrent.01"] = instance,
                },
            };
            var options = new H5WriteOptions(
                FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
            file.Write(path, options);

            using var f = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetSchemaException>(() => S111DatasetReader.Read(f));

            Assert.Equal("S-111", ex.Product);
            Assert.EndsWith("/SurfaceCurrent/SurfaceCurrent.01", ex.GroupPath, StringComparison.Ordinal);
            Assert.Equal("gridOriginLatitude", ex.AttributeOrDataset);
            Assert.NotNull(ex.InnerException);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_UnsupportedDataCodingFormat_ThrowsNotSupportedException()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var file = new H5File
            {
                Attributes = new() { ["horizontalDatumValue"] = 4326 },
                ["SurfaceCurrent"] = new H5Group
                {
                    Attributes = new() { ["dataCodingFormat"] = (byte)7 },
                },
            };
            var options = new H5WriteOptions(
                FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
            file.Write(path, options);

            using var f = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetNotSupportedException>(() => S111DatasetReader.Read(f));

            Assert.Equal("S-111", ex.Product);
            Assert.Contains("7", ex.Feature);
            Assert.Contains("not yet supported", ex.Message);
        }
        finally { File.Delete(path); }
    }
}
