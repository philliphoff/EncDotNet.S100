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

    /// <summary>
    /// <c>timePoint</c> follows the same ISO 8601 grammar as the validator
    /// (<c>Iso8601Text</c>, #738): basic, extended and mixed forms, offsets,
    /// fractional seconds, and no zone (taken as UTC).
    /// </summary>
    [Theory]
    [InlineData("20260902T105406Z", "2026-09-02T10:54:06")]
    [InlineData("20260902T105406+0000", "2026-09-02T10:54:06")]
    [InlineData("2026-09-02T05:54:06-05:00", "2026-09-02T10:54:06")]
    [InlineData("20260902T125406+02", "2026-09-02T10:54:06")]
    [InlineData("20260902T10:54:06Z", "2026-09-02T10:54:06")]
    [InlineData("2026-09-02T10:54:06.25Z", "2026-09-02T10:54:06.25")]
    [InlineData("20260902T105406", "2026-09-02T10:54:06")]
    public void Read_TimePoint_AcceptsIso8601Forms(string timePoint, string expectedUtc)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111FixtureBuilder.WriteFile(path, new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 0.75f, SurfaceCurrentDirection = 120f } }, 1, 1,
                useF64GridAttrs: true,
                useUnsignedCounts: false,
                timePoint: timePoint);

            using var file = PureHdfFile.Open(path);
            var coverage = Assert.Single(S111DatasetReader.Read(file).Coverages);

            var expected = DateTime.SpecifyKind(
                DateTime.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            Assert.Equal(expected, coverage.TimePoint);
            Assert.Equal(DateTimeKind.Utc, coverage.TimePoint.Kind);
        }
        finally { File.Delete(path); }
    }

    /// <summary>A malformed <c>timePoint</c> is a schema error naming the attribute and group.</summary>
    [Theory]
    [InlineData("not-a-time")]
    [InlineData("20230229T000000Z")]
    [InlineData("20260902T250000Z")]
    public void Read_MalformedTimePoint_ThrowsSchemaException(string timePoint)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111FixtureBuilder.WriteFile(path, new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 0.75f, SurfaceCurrentDirection = 120f } }, 1, 1,
                useF64GridAttrs: true,
                useUnsignedCounts: false,
                timePoint: timePoint);

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<EncDotNet.S100.Hdf5.S100DatasetSchemaException>(() => S111DatasetReader.Read(file));

            Assert.Equal("S-111", ex.Product);
            Assert.Equal("timePoint", ex.AttributeOrDataset);
            Assert.EndsWith("/Group_001", ex.GroupPath, StringComparison.Ordinal);
            Assert.Contains(timePoint, ex.Message, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }
}
