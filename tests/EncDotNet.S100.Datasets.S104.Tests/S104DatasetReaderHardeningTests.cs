using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// Reader-hardening tests (PR-A): tolerant numeric attribute reads,
/// compound-member name mapping, and NULLPAD string handling. The
/// fixtures are synthetic and built on the fly so the test suite has
/// no dependency on real producer files.
/// </summary>
public class S104DatasetReaderHardeningTests
{
    /// <summary>
    /// Grid-georef attributes are spec-allowed to be either F32 or F64.
    /// The reader must accept both and return identical double values.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_AcceptsBothF32AndF64GridAttrs(bool useF64)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 } };
            S104FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: useF64, useUnsignedCounts: false);

            using var file = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(file);

            var coverage = Assert.Single(dataset.Coverages);
            Assert.Equal(50.0, coverage.OriginLatitude, precision: 6);
            Assert.Equal(-1.0, coverage.OriginLongitude, precision: 6);
            Assert.Equal(0.01, coverage.SpacingLatitudinal, precision: 6);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Count attributes (numPointsLatitudinal / numPointsLongitudinal) are
    /// spec-allowed to be either signed or unsigned. The reader must accept
    /// both widths.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_AcceptsBothSignedAndUnsignedCountAttrs(bool unsigned)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 } };
            S104FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: false, useUnsignedCounts: unsigned);

            using var file = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(file);

            var coverage = Assert.Single(dataset.Coverages);
            Assert.Equal(1, coverage.NumPointsLatitudinal);
            Assert.Equal(1, coverage.NumPointsLongitudinal);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The S-104 Feature Catalogue names compound members
    /// <c>waterLevelHeight</c> and <c>waterLevelTrend</c>. The reader
    /// must accept those exact names (no positional fallback required).
    /// </summary>
    [Fact]
    public void Read_AcceptsSpecMemberNames()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[]
            {
                new S104FixtureBuilder.SpecRow { WaterLevelHeight = 2.5f, WaterLevelTrend = 1 },
                new S104FixtureBuilder.SpecRow { WaterLevelHeight = -1.25f, WaterLevelTrend = 3 },
            };
            S104FixtureBuilder.WriteFile(path, values, 1, 2, useF64GridAttrs: true, useUnsignedCounts: false);

            using var file = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Equal(2, coverage.Values.Length);
            Assert.Equal(2.5f, coverage.Values[0].Height);
            Assert.Equal((byte)1, coverage.Values[0].Trend);
            Assert.Equal(-1.25f, coverage.Values[1].Height);
            Assert.Equal((byte)3, coverage.Values[1].Trend);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// UKHO production dcf2 files use <c>surfaceHeight</c> and <c>trend</c>
    /// as the compound member names. The reader must accept that variant.
    /// </summary>
    [Fact]
    public void Read_AcceptsUkhoMemberNames()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[]
            {
                new S104FixtureBuilder.UkhoRow { SurfaceHeight = 3.25f, Trend = 2.0f },
            };
            S104FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: true, useUnsignedCounts: false);

            using var file = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Single(coverage.Values);
            Assert.Equal(3.25f, coverage.Values[0].Height);
            // UKHO stores trend as f32; the reader rounds to the nearest enum byte.
            Assert.Equal((byte)2, coverage.Values[0].Trend);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Existing in-tree synthetic fixtures (and the real NOAA file via
    /// PureHDF's positional fallback) use the C# names <c>Height</c> and
    /// <c>Trend</c>. The reader must continue to accept them.
    /// </summary>
    [Fact]
    public void Read_AcceptsLegacyMemberNames()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[]
            {
                new S104FixtureBuilder.LegacyRow { Height = 1.0f, Trend = 1 },
            };
            S104FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: false, useUnsignedCounts: false);

            using var file = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Single(coverage.Values);
            Assert.Equal(1.0f, coverage.Values[0].Height);
            Assert.Equal((byte)1, coverage.Values[0].Trend);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Near-UKHO end-to-end fixture combining F64 grid attrs, the
    /// <c>surfaceHeight</c> / <c>trend</c> compound shape with trend as
    /// f32, and a fixed-length nullpad timePoint. The reader must open it
    /// cleanly and round-trip values.
    /// </summary>
    [Fact]
    public void Read_NearUkhoFixture_RoundTripsCleanly()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[]
            {
                new S104FixtureBuilder.UkhoRow { SurfaceHeight = 0.75f, Trend = 3.0f },
                new S104FixtureBuilder.UkhoRow { SurfaceHeight = -0.5f, Trend = 1.0f },
            };
            S104FixtureBuilder.WriteFile(path, values, 1, 2,
                useF64GridAttrs: true,
                useUnsignedCounts: false,
                timePoint: "20210401T000000Z");

            using var file = PureHdfFile.Open(path);
            var dataset = S104DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Equal(new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc), coverage.TimePoint);
            Assert.Equal(2, coverage.Values.Length);
            Assert.Equal(0.75f, coverage.Values[0].Height);
            Assert.Equal((byte)3, coverage.Values[0].Trend);
            Assert.Equal(-0.5f, coverage.Values[1].Height);
            Assert.Equal((byte)1, coverage.Values[1].Trend);
        }
        finally { File.Delete(path); }
    }
}
