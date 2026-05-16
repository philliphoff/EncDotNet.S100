using System.Reflection;
using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// PR-B: verifies that the S-104 reader translates HDF5-backend
/// "missing attribute" failures and the data-coding-format guard
/// into typed, contextual exceptions.
/// </summary>
public class S104DatasetReaderErrorTests
{
    [Fact]
    public void Read_MissingGridOriginLatitude_ThrowsSchemaExceptionWithContext()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            WriteFileMissingAttribute(path, omit: "gridOriginLatitude");

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetSchemaException>(() => S104DatasetReader.Read(file));

            Assert.Equal("S-104", ex.Product);
            Assert.EndsWith("/WaterLevel/WaterLevel.01", ex.GroupPath, StringComparison.Ordinal);
            Assert.Equal("gridOriginLatitude", ex.AttributeOrDataset);
            Assert.NotNull(ex.SpecReference);
            Assert.NotNull(ex.InnerException);

            Assert.Contains("S-104", ex.Message);
            Assert.Contains("gridOriginLatitude", ex.Message);
            Assert.Contains("/WaterLevel/WaterLevel.01", ex.Message);
            Assert.Contains(ex.SpecReference!, ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_UnsupportedDataCodingFormat_ThrowsNotSupportedException()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            WriteFileWithDcf(path, dcf: 8);

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetNotSupportedException>(() => S104DatasetReader.Read(file));

            Assert.Equal("S-104", ex.Product);
            Assert.Contains("8", ex.Feature);
            Assert.Contains("time series at fixed stations", ex.Feature);
            Assert.Contains("not yet supported", ex.Message);
        }
        finally { File.Delete(path); }
    }

    private static void WriteFileMissingAttribute(string path, string omit)
    {
        var attrs = new Dictionary<string, object>
        {
            ["gridOriginLatitude"] = 50.0,
            ["gridOriginLongitude"] = -1.0,
            ["gridSpacingLatitudinal"] = 0.01,
            ["gridSpacingLongitudinal"] = 0.01,
            ["numPointsLatitudinal"] = 1,
            ["numPointsLongitudinal"] = 1,
        };
        attrs.Remove(omit);

        var instance = new H5Group
        {
            Attributes = attrs,
            ["Group_001"] = new H5Group
            {
                Attributes = new() { ["timePoint"] = "20210401T000000Z" },
                ["values"] = new[] { new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 } },
            },
        };

        var file = new H5File
        {
            Attributes = new() { ["horizontalCRS"] = 4326 },
            ["WaterLevel"] = new H5Group
            {
                Attributes = new() { ["dataCodingFormat"] = (byte)2 },
                ["WaterLevel.01"] = instance,
            },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
    }

    private static void WriteFileWithDcf(string path, byte dcf)
    {
        var file = new H5File
        {
            Attributes = new() { ["horizontalCRS"] = 4326 },
            ["WaterLevel"] = new H5Group
            {
                Attributes = new() { ["dataCodingFormat"] = dcf },
            },
        };
        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
    }
}
