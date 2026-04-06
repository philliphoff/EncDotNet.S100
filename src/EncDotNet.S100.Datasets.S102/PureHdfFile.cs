using PureHDF;

namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// <see cref="IHdf5File"/> implementation backed by PureHDF.
/// </summary>
public sealed class PureHdfFile : IHdf5File
{
    private readonly IDisposable _fileHandle;

    private PureHdfFile(IDisposable fileHandle, IH5Group rootGroup)
    {
        _fileHandle = fileHandle;
        Root = new PureHdfGroup(rootGroup);
    }

    /// <inheritdoc />
    public IHdf5Group Root { get; }

    /// <summary>Opens an HDF5 file from a path on disk.</summary>
    public static PureHdfFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var file = H5File.OpenRead(path);
        return new PureHdfFile(file, file);
    }

    /// <summary>Opens an HDF5 file from a readable stream.</summary>
    public static PureHdfFile Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var file = H5File.Open(stream);
        return new PureHdfFile(file, file);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _fileHandle.Dispose();
    }

    private sealed class PureHdfGroup : IHdf5Group
    {
        private readonly IH5Group _group;

        internal PureHdfGroup(IH5Group group)
        {
            _group = group;
        }

        public IHdf5Group OpenGroup(string name)
        {
            return new PureHdfGroup(_group.Group(name));
        }

        public IReadOnlyList<string> GroupNames
        {
            get
            {
                var names = new List<string>();

                foreach (var child in _group.Children())
                {
                    if (child is IH5Group g)
                        names.Add(g.Name);
                }

                return names;
            }
        }

        public bool AttributeExists(string name)
        {
            foreach (var attr in _group.Attributes())
            {
                if (attr.Name == name)
                    return true;
            }

            return false;
        }

        public T ReadAttribute<T>(string name) where T : unmanaged
        {
            return _group.Attribute(name).Read<T>();
        }

        public string ReadStringAttribute(string name)
        {
            return _group.Attribute(name).Read<string>();
        }

        public T[] ReadDataset<T>(string name) where T : unmanaged
        {
            return _group.Dataset(name).Read<T[]>();
        }
    }
}
