using System.Diagnostics;
using System.Runtime.InteropServices;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf.Diagnostics;
using PureHDF;

namespace EncDotNet.S100.Hdf5.PureHdf;

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
        using var activity = Telemetry.ActivitySource.StartActivity("s100.hdf5.file.open");
        activity?.SetTag(TelemetryTags.DatasetPath, path);
        var file = H5File.OpenRead(path);
        return new PureHdfFile(file, file);
    }

    /// <summary>Opens an HDF5 file from a readable stream.</summary>
    public static PureHdfFile Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var activity = Telemetry.ActivitySource.StartActivity("s100.hdf5.file.open");
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
            using var activity = Telemetry.ActivitySource.StartActivity("s100.hdf5.open");
            activity?.SetTag(TelemetryTags.Hdf5Kind, "group");
            var start = Stopwatch.GetTimestamp();
            try
            {
                return new PureHdfGroup(_group.Group(name));
            }
            finally
            {
                Telemetry.ReadDuration.Record(GetElapsedMs(start), new KeyValuePair<string, object?>("kind", "group"));
            }
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
            var start = Stopwatch.GetTimestamp();
            try
            {
                return _group.Attribute(name).Read<T>();
            }
            finally
            {
                Telemetry.ReadDuration.Record(GetElapsedMs(start), new KeyValuePair<string, object?>("kind", "attribute"));
            }
        }

        public string ReadStringAttribute(string name)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                return _group.Attribute(name).Read<string>();
            }
            finally
            {
                Telemetry.ReadDuration.Record(GetElapsedMs(start), new KeyValuePair<string, object?>("kind", "attribute"));
            }
        }

        public T[] ReadDataset<T>(string name) where T : unmanaged
        {
            using var activity = Telemetry.ActivitySource.StartActivity("s100.hdf5.dataset.read");
            activity?.SetTag("s100.hdf5.dataset", name);
            var start = Stopwatch.GetTimestamp();
            T[] result;
            try
            {
                result = _group.Dataset(name).Read<T[]>();
            }
            finally
            {
                Telemetry.ReadDuration.Record(GetElapsedMs(start), new KeyValuePair<string, object?>("kind", "dataset"));
            }
            long bytes = (long)result.Length * Marshal.SizeOf<T>();
            Telemetry.ReadBytes.Add(bytes);
            activity?.SetTag("s100.hdf5.read.bytes", bytes);
            activity?.SetTag("s100.hdf5.element.count", result.Length);
            return result;
        }

        private static double GetElapsedMs(long startTimestamp) =>
            (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }
}

