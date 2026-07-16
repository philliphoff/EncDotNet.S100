using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Tests for <see cref="S111DatasetReader.ReadMetadata"/> — the phased
/// "peek" path (issue #460). Verifies the metadata-only read yields the same
/// extent and time coverage as a full read and never touches the per-step
/// <c>values</c> arrays.
/// </summary>
public class S111DatasetReaderMetadataTests
{
    [Fact]
    public void ReadMetadata_ExtentAndTimeMatchFullReadCoverageSource()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.5f, SurfaceCurrentDirection = 90f } };
            S111FixtureBuilder.WriteFile(path, values, numLat: 3, numLon: 4, useF64GridAttrs: true, useUnsignedCounts: false);

            DatasetMetadata meta;
            using (var hdf = PureHdfFile.Open(path))
                meta = S111DatasetReader.ReadMetadata(hdf);

            BoundingBox fullExtent;
            IReadOnlyList<DateTime> fullTimes;
            using (var hdf = PureHdfFile.Open(path))
            {
                var dataset = S111DatasetReader.Read(hdf);
                var source = new S111CoverageSource(dataset);
                fullExtent = source.Metadata.Extent;
                fullTimes = source.AvailableTimes;
            }

            Assert.NotNull(meta.Extent);
            Assert.Equal(fullExtent.SouthLatitude, meta.Extent!.SouthLatitude, precision: 9);
            Assert.Equal(fullExtent.WestLongitude, meta.Extent.WestLongitude, precision: 9);
            Assert.Equal(fullExtent.NorthLatitude, meta.Extent.NorthLatitude, precision: 9);
            Assert.Equal(fullExtent.EastLongitude, meta.Extent.EastLongitude, precision: 9);

            Assert.NotNull(meta.TimeCoverage);
            Assert.Equal(fullTimes.Min(), meta.TimeCoverage!.Value.Start);
            Assert.Equal(fullTimes.Max(), meta.TimeCoverage.Value.End);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadMetadata_MultiStep_TimeCoverageMatchesFullRead()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var steps = new[]
            {
                new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1f, SurfaceCurrentDirection = 10f } },
                new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 2f, SurfaceCurrentDirection = 20f } },
                new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 3f, SurfaceCurrentDirection = 30f } },
            };
            S111FixtureBuilder.WriteMultiStepFile(path, steps, numLat: 1, numLon: 1);

            DatasetMetadata meta;
            using (var hdf = PureHdfFile.Open(path))
                meta = S111DatasetReader.ReadMetadata(hdf);

            IReadOnlyList<DateTime> fullTimes;
            using (var hdf = PureHdfFile.Open(path))
                fullTimes = new S111CoverageSource(S111DatasetReader.Read(hdf)).AvailableTimes;

            Assert.NotNull(meta.TimeCoverage);
            Assert.Equal(fullTimes.Min(), meta.TimeCoverage!.Value.Start);
            Assert.Equal(fullTimes.Max(), meta.TimeCoverage.Value.End);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadMetadata_SurfacesSpecAndCrs()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.5f, SurfaceCurrentDirection = 90f } };
            S111FixtureBuilder.WriteFile(path, values, numLat: 1, numLon: 1, useF64GridAttrs: false, useUnsignedCounts: false);

            using var hdf = PureHdfFile.Open(path);
            var meta = S111DatasetReader.ReadMetadata(hdf);

            Assert.Equal("S-111", meta.Spec.Name);
            Assert.Equal(4326, meta.HorizontalCrsEpsg);
            Assert.Null(meta.DisplayScale);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadMetadata_DoesNotReadValueArrays_ButFullReadDoes()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var values = new[] { new S111FixtureBuilder.SpecRow { SurfaceCurrentSpeed = 1.5f, SurfaceCurrentDirection = 90f } };
            S111FixtureBuilder.WriteFile(path, values, numLat: 2, numLon: 2, useF64GridAttrs: true, useUnsignedCounts: false);

            using (var spy = new DatasetReadSpyFile(PureHdfFile.Open(path)))
            {
                _ = S111DatasetReader.ReadMetadata(spy);
                Assert.Equal(0, spy.DatasetReadCount);
            }

            using (var spy = new DatasetReadSpyFile(PureHdfFile.Open(path)))
            {
                _ = S111DatasetReader.Read(spy);
                Assert.True(spy.DatasetReadCount > 0);
            }
        }
        finally { File.Delete(path); }
    }
}
