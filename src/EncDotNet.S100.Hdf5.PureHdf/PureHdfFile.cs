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
        private readonly Lazy<IReadOnlyList<string>> _groupNames;
        private readonly Lazy<HashSet<string>> _attributeNames;

        internal PureHdfGroup(IH5Group group)
        {
            _group = group;
            _groupNames = new Lazy<IReadOnlyList<string>>(BuildGroupNames);
            _attributeNames = new Lazy<HashSet<string>>(BuildAttributeNames);
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

        // Cached: PureHDF files are read-only in our usage. Building once and
        // returning the same list avoids the O(N) child enumeration that
        // S102DatasetReader.Read (and friends) trigger multiple times per group.
        public IReadOnlyList<string> GroupNames => _groupNames.Value;

        // Cached HashSet: AttributeExists is called repeatedly during reader
        // schema validation (e.g. S102DatasetReader.Read does 5 probes on the
        // root). HashSet lookup is O(1) vs the original O(N) attribute scan.
        public bool AttributeExists(string name) => _attributeNames.Value.Contains(name);

        private IReadOnlyList<string> BuildGroupNames()
        {
            var names = new List<string>();
            foreach (var child in _group.Children())
            {
                if (child is IH5Group g)
                    names.Add(g.Name);
            }
            return names;
        }

        private HashSet<string> BuildAttributeNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attr in _group.Attributes())
                names.Add(attr.Name);
            return names;
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

        public double ReadDoubleAttribute(string name)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                var attr = _group.Attribute(name);
                var type = attr.Type;

                if (type.Class == H5DataTypeClass.FloatingPoint)
                {
                    return type.Size switch
                    {
                        4 => attr.Read<float>(),
                        8 => attr.Read<double>(),
                        _ => throw new NotSupportedException(
                            $"Unsupported floating-point size {type.Size} bytes for attribute '{name}'."),
                    };
                }

                // Tolerate integers that happen to be used where the spec
                // allows either; widening to double never loses precision
                // for 32-bit-or-smaller integers and is what callers want.
                if (type.Class == H5DataTypeClass.FixedPoint)
                {
                    return ReadInt64Attribute(name);
                }

                throw new NotSupportedException(
                    $"Attribute '{name}' has class {type.Class}; expected FloatingPoint or FixedPoint.");
            }
            finally
            {
                Telemetry.ReadDuration.Record(GetElapsedMs(start), new KeyValuePair<string, object?>("kind", "attribute"));
            }
        }

        public long ReadInt64Attribute(string name)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                var attr = _group.Attribute(name);
                var type = attr.Type;

                // Accept both raw fixed-point integers and enumerations
                // (which are fixed-point integers with a name table) since
                // S-100 attributes like dataCodingFormat are spec-defined
                // enums but treated as integer values by callers.
                bool signed;
                if (type.Class == H5DataTypeClass.FixedPoint)
                {
                    signed = type.FixedPoint.IsSigned;
                }
                else if (type.Class == H5DataTypeClass.Enumerated)
                {
                    // S-100 enumerations are spec-defined as unsigned 8-bit
                    // values (and PureHDF does not expose the underlying
                    // base-type signedness through IH5DataType).
                    signed = false;
                }
                else
                {
                    throw new NotSupportedException(
                        $"Attribute '{name}' has class {type.Class}; expected FixedPoint or Enumerated.");
                }

                return (type.Size, signed) switch
                {
                    (1, true) => attr.Read<sbyte>(),
                    (1, false) => attr.Read<byte>(),
                    (2, true) => attr.Read<short>(),
                    (2, false) => attr.Read<ushort>(),
                    (4, true) => attr.Read<int>(),
                    (4, false) => attr.Read<uint>(),
                    (8, true) => attr.Read<long>(),
                    (8, false) => checked((long)attr.Read<ulong>()),
                    _ => throw new NotSupportedException(
                        $"Unsupported fixed-point size {type.Size} bytes (signed={signed}) for attribute '{name}'."),
                };
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
                var raw = _group.Attribute(name).Read<string>();
                return TrimNullPadding(raw);
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

        public RawCompoundDataset ReadRawCompoundDataset(string name)
        {
            using var activity = Telemetry.ActivitySource.StartActivity("s100.hdf5.dataset.read");
            activity?.SetTag("s100.hdf5.dataset", name);
            activity?.SetTag("s100.hdf5.dataset.kind", "compound.raw");
            var start = Stopwatch.GetTimestamp();
            try
            {
                var dataset = _group.Dataset(name);
                var type = dataset.Type;

                if (type.Class != H5DataTypeClass.Compound)
                {
                    throw new InvalidOperationException(
                        $"Dataset '{name}' has class {type.Class}; expected Compound.");
                }

                int recordSize = (int)type.Size;
                var rawMembers = type.Compound.Members;
                var members = new List<CompoundMemberInfo>(rawMembers.Length);
                foreach (var m in rawMembers)
                {
                    members.Add(new CompoundMemberInfo(
                        m.Name,
                        m.Offset,
                        (int)m.Type.Size,
                        ClassifyMember(m.Type)));
                }

                var bytes = dataset.Read<byte[]>();
                Telemetry.ReadBytes.Add(bytes.LongLength);
                activity?.SetTag("s100.hdf5.read.bytes", bytes.LongLength);
                activity?.SetTag("s100.hdf5.element.count", bytes.Length / Math.Max(1, recordSize));

                return new RawCompoundDataset(bytes, recordSize, members);
            }
            finally
            {
                Telemetry.ReadDuration.Record(GetElapsedMs(start), new KeyValuePair<string, object?>("kind", "dataset"));
            }
        }

        private static CompoundMemberKind ClassifyMember(IH5DataType type)
        {
            // S-104 stores waterLevelTrend as an enumeration over uint8 in
            // conforming files, and as a plain f32 in some UKHO files; we
            // classify the underlying base type so callers can decode either.
            if (type.Class == H5DataTypeClass.FloatingPoint)
            {
                return type.Size switch
                {
                    4 => CompoundMemberKind.Float32,
                    8 => CompoundMemberKind.Float64,
                    _ => CompoundMemberKind.Other,
                };
            }

            if (type.Class == H5DataTypeClass.FixedPoint || type.Class == H5DataTypeClass.Enumerated)
            {
                bool signed = type.Class == H5DataTypeClass.FixedPoint
                    ? type.FixedPoint.IsSigned
                    // Enumerated types in S-100 (trend, etc.) are always
                    // backed by unsigned fixed-point integers per the spec.
                    : false;

                return (type.Size, signed) switch
                {
                    (1, true) => CompoundMemberKind.Int8,
                    (1, false) => CompoundMemberKind.UInt8,
                    (2, true) => CompoundMemberKind.Int16,
                    (2, false) => CompoundMemberKind.UInt16,
                    (4, true) => CompoundMemberKind.Int32,
                    (4, false) => CompoundMemberKind.UInt32,
                    (8, true) => CompoundMemberKind.Int64,
                    (8, false) => CompoundMemberKind.UInt64,
                    _ => CompoundMemberKind.Other,
                };
            }

            return CompoundMemberKind.Other;
        }

        private static string TrimNullPadding(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            int end = value.Length;
            while (end > 0 && value[end - 1] == '\0')
                end--;

            return end == value.Length ? value : value[..end];
        }

        private static double GetElapsedMs(long startTimestamp) =>
            (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }
}

