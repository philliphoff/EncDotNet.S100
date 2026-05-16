using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S111.Tests;

public class S111DatasetReaderHardeningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_AcceptsBothF32AndF64GridAttrs(bool useF64)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.5f, SurfaceCurrentDirection = 90f } };
            S111FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: useF64, useUnsignedCounts: false);

            using var file = PureHdfFile.Open(path);
            var dataset = S111DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Equal(50.0, coverage.OriginLatitude, precision: 6);
            Assert.Equal(-1.0, coverage.OriginLongitude, precision: 6);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_AcceptsBothSignedAndUnsignedCountAttrs(bool unsigned)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1f, SurfaceCurrentDirection = 0f } };
            S111FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: false, useUnsignedCounts: unsigned);

            using var file = PureHdfFile.Open(path);
            var dataset = S111DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Equal(1, coverage.NumPointsLatitudinal);
            Assert.Equal(1, coverage.NumPointsLongitudinal);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_AcceptsSpecMemberNames()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[]
            {
                new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 2.5f, SurfaceCurrentDirection = 45f },
                new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.0f, SurfaceCurrentDirection = 180f },
            };
            S111FixtureBuilder.WriteFile(path, values, 1, 2, useF64GridAttrs: true, useUnsignedCounts: false);

            using var file = PureHdfFile.Open(path);
            var dataset = S111DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Equal(2, coverage.Values.Length);
            Assert.Equal(2.5f, coverage.Values[0].Speed);
            Assert.Equal(45f, coverage.Values[0].Direction);
            Assert.Equal(1.0f, coverage.Values[1].Speed);
            Assert.Equal(180f, coverage.Values[1].Direction);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_AcceptsLegacyMemberNames()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S111FixtureBuilder.LegacyRow { Speed = 0.5f, Direction = 270f } };
            S111FixtureBuilder.WriteFile(path, values, 1, 1, useF64GridAttrs: false, useUnsignedCounts: false);

            using var file = PureHdfFile.Open(path);
            var dataset = S111DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Single(coverage.Values);
            Assert.Equal(0.5f, coverage.Values[0].Speed);
            Assert.Equal(270f, coverage.Values[0].Direction);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Near-UKHO end-to-end fixture combining F64 grid attrs, spec compound
    /// member names, and the canonical timePoint format. The reader must
    /// open it cleanly and round-trip values.
    /// </summary>
    [Fact]
    public void Read_NearUkhoFixture_RoundTripsCleanly()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[]
            {
                new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 0.75f, SurfaceCurrentDirection = 120f },
            };
            S111FixtureBuilder.WriteFile(path, values, 1, 1,
                useF64GridAttrs: true,
                useUnsignedCounts: false,
                timePoint: "20210401T000000Z");

            using var file = PureHdfFile.Open(path);
            var dataset = S111DatasetReader.Read(file);
            var coverage = Assert.Single(dataset.Coverages);

            Assert.Equal(new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc), coverage.TimePoint);
            Assert.Equal(0.75f, coverage.Values[0].Speed);
            Assert.Equal(120f, coverage.Values[0].Direction);
        }
        finally { File.Delete(path); }
    }
}
