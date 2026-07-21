using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// Tests that the S-104 reader surfaces the real <c>verticalDatum</c> root
/// attribute (S-104 Ed 2.0.0 §10.2.4) instead of a hard-coded placeholder, and
/// that the coverage source resolves it to the S-100 register label.
/// </summary>
public class S104VerticalDatumTests
{
    private static string WriteFile(int? verticalDatum)
    {
        var path = Path.GetTempFileName() + ".h5";
        var values = new S104FixtureBuilder.SpecRow[4];
        for (int i = 0; i < values.Length; i++)
            values[i] = new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.5f, WaterLevelTrend = 1 };

        return S104FixtureBuilder.WriteFile(
            path, values, numLat: 2, numLon: 2,
            useF64GridAttrs: true, useUnsignedCounts: false,
            verticalDatum: verticalDatum);
    }

    [Fact]
    public void Read_ParsesVerticalDatumCode()
    {
        var path = WriteFile(verticalDatum: 23);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(hdf);

            Assert.Equal(23, dataset.VerticalDatum);
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
            var dataset = S104DatasetReader.Read(hdf);

            Assert.Null(dataset.VerticalDatum);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CoverageSource_SurfacesResolvedDatumLabel()
    {
        var path = WriteFile(verticalDatum: 23);
        try
        {
            using var hdf = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(hdf);
            var source = new S104CoverageSource(dataset);

            Assert.Equal("Lowest Astronomical Tide", source.Metadata.VerticalDatum);
        }
        finally { File.Delete(path); }
    }
}
