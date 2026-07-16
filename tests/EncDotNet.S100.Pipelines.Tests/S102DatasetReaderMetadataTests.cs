using System.Reflection;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using PureHDF;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="S102DatasetReader.ReadMetadata"/> — the phased
/// "peek" path (issue #460). Verifies the metadata-only read yields the same
/// extent as a full read and never touches the depth/uncertainty
/// <c>values</c> arrays.
/// </summary>
public class S102DatasetReaderMetadataTests
{
    private struct SpecBathyRow
    {
        [H5Name("depth")] public float Depth;
        [H5Name("uncertainty")] public float Uncertainty;
    }

    private static void WriteFile(
        string path,
        double originLat = 50.0,
        double originLon = -1.0,
        double spacingLat = 0.01,
        double spacingLon = 0.02,
        int numLat = 3,
        int numLon = 4,
        int crs = 4326,
        string? productSpecification = "INT.IHO.S-102.3.0.0")
    {
        var values = new SpecBathyRow[numLat * numLon];
        for (int i = 0; i < values.Length; i++)
            values[i] = new SpecBathyRow { Depth = 10f + i, Uncertainty = 0.1f };

        var instance = new H5Group
        {
            Attributes = new()
            {
                ["gridOriginLatitude"] = originLat,
                ["gridOriginLongitude"] = originLon,
                ["gridSpacingLatitudinal"] = spacingLat,
                ["gridSpacingLongitudinal"] = spacingLon,
                ["numPointsLatitudinal"] = numLat,
                ["numPointsLongitudinal"] = numLon,
            },
            ["Group_001"] = new H5Group { ["values"] = values },
        };

        var rootAttrs = new Dictionary<string, object> { ["horizontalCRS"] = crs };
        if (productSpecification is not null)
            rootAttrs["productSpecification"] = productSpecification;

        var file = new H5File
        {
            Attributes = rootAttrs,
            ["BathymetryCoverage"] = new H5Group { ["BathymetryCoverage.01"] = instance },
        };

        var options = new H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<H5NameAttribute>()?.Name);
        file.Write(path, options);
    }

    [Fact]
    public void ReadMetadata_ExtentMatchesFullReadCoverageSource()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            WriteFile(path);

            BoundingBox metaExtent;
            using (var hdf = PureHdfFile.Open(path))
                metaExtent = S102DatasetReader.ReadMetadata(hdf).Extent!;

            // Authoritative extent from a full read via the coverage source.
            BoundingBox fullExtent;
            using (var hdf = PureHdfFile.Open(path))
            {
                var dataset = S102DatasetReader.Read(hdf);
                fullExtent = new S102CoverageSource(dataset).Metadata.Extent;
            }

            Assert.Equal(fullExtent.SouthLatitude, metaExtent.SouthLatitude, precision: 9);
            Assert.Equal(fullExtent.WestLongitude, metaExtent.WestLongitude, precision: 9);
            Assert.Equal(fullExtent.NorthLatitude, metaExtent.NorthLatitude, precision: 9);
            Assert.Equal(fullExtent.EastLongitude, metaExtent.EastLongitude, precision: 9);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadMetadata_SurfacesSpecAndCrs()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            WriteFile(path, crs: 32631, productSpecification: "INT.IHO.S-102.3.0.0");

            using var hdf = PureHdfFile.Open(path);
            var meta = S102DatasetReader.ReadMetadata(hdf);

            Assert.Equal("S-102", meta.Spec.Name);
            Assert.Equal(new SpecVersion(3, 0, 0), meta.Spec.Edition);
            Assert.Equal(32631, meta.HorizontalCrsEpsg);
            Assert.Null(meta.DisplayScale);
            Assert.Null(meta.TimeCoverage);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadMetadata_DoesNotReadValueArrays_ButFullReadDoes()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            WriteFile(path);

            using (var spy = new DatasetReadSpyFile(PureHdfFile.Open(path)))
            {
                _ = S102DatasetReader.ReadMetadata(spy);
                Assert.Equal(0, spy.DatasetReadCount);
            }

            // Control: a full read must read the values array, proving the
            // spy would have caught any stray array read above.
            using (var spy = new DatasetReadSpyFile(PureHdfFile.Open(path)))
            {
                _ = S102DatasetReader.Read(spy);
                Assert.True(spy.DatasetReadCount > 0);
            }
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// <see cref="IHdf5File"/> decorator that counts every dataset-array read
    /// (<see cref="IHdf5Group.ReadDataset{T}"/> /
    /// <see cref="IHdf5Group.ReadRawCompoundDataset"/>) performed anywhere in
    /// the group tree.
    /// </summary>
    private sealed class DatasetReadSpyFile : IHdf5File
    {
        private readonly IHdf5File _inner;
        private int _datasetReadCount;

        public DatasetReadSpyFile(IHdf5File inner) => _inner = inner;

        public int DatasetReadCount => _datasetReadCount;

        public IHdf5Group Root => new SpyGroup(_inner.Root, this);

        public void Dispose() => _inner.Dispose();

        private void OnDatasetRead() => Interlocked.Increment(ref _datasetReadCount);

        private sealed class SpyGroup : IHdf5Group
        {
            private readonly IHdf5Group _inner;
            private readonly DatasetReadSpyFile _owner;

            public SpyGroup(IHdf5Group inner, DatasetReadSpyFile owner)
            {
                _inner = inner;
                _owner = owner;
            }

            public IHdf5Group OpenGroup(string name) => new SpyGroup(_inner.OpenGroup(name), _owner);
            public IReadOnlyList<string> GroupNames => _inner.GroupNames;
            public bool AttributeExists(string name) => _inner.AttributeExists(name);
            public T ReadAttribute<T>(string name) where T : unmanaged => _inner.ReadAttribute<T>(name);
            public double ReadDoubleAttribute(string name) => _inner.ReadDoubleAttribute(name);
            public long ReadInt64Attribute(string name) => _inner.ReadInt64Attribute(name);
            public string ReadStringAttribute(string name) => _inner.ReadStringAttribute(name);

            public T[] ReadDataset<T>(string name) where T : unmanaged
            {
                _owner.OnDatasetRead();
                return _inner.ReadDataset<T>(name);
            }

            public RawCompoundDataset ReadRawCompoundDataset(string name)
            {
                _owner.OnDatasetRead();
                return _inner.ReadRawCompoundDataset(name);
            }
        }
    }
}
