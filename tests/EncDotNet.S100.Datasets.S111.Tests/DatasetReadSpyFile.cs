using EncDotNet.S100.Hdf5;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// <see cref="IHdf5File"/> decorator that counts every dataset-array read
/// (<see cref="IHdf5Group.ReadDataset{T}"/> /
/// <see cref="IHdf5Group.ReadRawCompoundDataset"/>) performed anywhere in the
/// group tree, so a test can assert that a metadata-only read never touches
/// the heavy payload arrays (issue #460).
/// </summary>
internal sealed class DatasetReadSpyFile : IHdf5File
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
