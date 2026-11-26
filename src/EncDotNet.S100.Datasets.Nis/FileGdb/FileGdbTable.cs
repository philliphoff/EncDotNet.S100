using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>
/// A read-only view of one table of a file geodatabase: its fields from the
/// <c>.gdbtable</c> header and its rows, found through the <c>.gdbtablx</c>
/// offsets.
/// </summary>
/// <remarks>
/// <para>
/// Written from the OpenFileGDB format description the GDAL project publishes
/// (<c>FileGDB_specification</c>), for the file version ArcGIS 10 and later
/// write. Rows are read one at a time with positioned reads, so a large table
/// is never loaded whole.
/// </para>
/// <para>
/// Field values come back as <see cref="int"/> (16- and 32-bit integers),
/// <see cref="long"/>, <see cref="double"/> (both float sizes),
/// <see cref="string"/> (text and XML), <see cref="DateTime"/> (dates, as
/// UTC-less local values the way the geodatabase stores them),
/// <see cref="Guid"/>, <see cref="NisGeometry"/>, or <see langword="null"/>.
/// Binary and raster fields are read past and come back as <see langword="null"/>.
/// </para>
/// </remarks>
internal sealed class FileGdbTable : IDisposable
{
    private const int HeaderLength = 40;
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    private readonly SafeFileHandle _handle;
    private readonly long[] _offsets;

    private FileGdbTable(string name, SafeFileHandle handle, IReadOnlyList<FileGdbField> fields, long[] offsets, int nullableCount)
    {
        Name = name;
        _handle = handle;
        Fields = fields;
        _offsets = offsets;
        NullableCount = nullableCount;
    }

    /// <summary>The table's name in the geodatabase catalogue.</summary>
    public string Name { get; }

    /// <summary>The table's fields, including its object id.</summary>
    public IReadOnlyList<FileGdbField> Fields { get; }

    /// <summary>The geometry field, or <see langword="null"/> when the table has none.</summary>
    public FileGdbField? GeometryField => Fields.FirstOrDefault(f => f.Type == FileGdbFieldType.Geometry);

    private int NullableCount { get; }

    /// <summary>The index of a field by name, ignoring case, or -1.</summary>
    public int IndexOf(string name)
    {
        for (int i = 0; i < Fields.Count; i++)
        {
            if (string.Equals(Fields[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    /// <summary>Opens a table from its <c>.gdbtable</c> path; the <c>.gdbtablx</c> must sit beside it.</summary>
    /// <exception cref="InvalidDataException">The files aren't a file geodatabase table this reader understands.</exception>
    public static FileGdbTable Open(string name, string gdbtablePath)
    {
        var tablxPath = Path.ChangeExtension(gdbtablePath, ".gdbtablx");
        var offsets = ReadOffsets(tablxPath);
        var handle = File.OpenHandle(gdbtablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.RandomAccess);
        try
        {
            var header = new byte[HeaderLength];
            ReadExactly(handle, header, 0);
            int magic = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (magic != 3)
                throw new InvalidDataException($"{gdbtablePath} is not a version 10 file geodatabase table (magic {magic}).");
            long fieldsOffset = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(32));

            var sizeBytes = new byte[4];
            ReadExactly(handle, sizeBytes, fieldsOffset);
            int size = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
            var section = new byte[size];
            ReadExactly(handle, section, fieldsOffset + 4);
            var (fields, nullable) = ReadFields(section, gdbtablePath);
            return new FileGdbTable(name, handle, fields, offsets, nullable);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Reads every row that isn't deleted, as its object id and its field values in field order.</summary>
    public IEnumerable<(long ObjectId, object?[] Values)> ReadRows()
    {
        var sizeBytes = new byte[4];
        for (int i = 0; i < _offsets.Length; i++)
        {
            long offset = _offsets[i];
            if (offset == 0) continue;
            ReadExactly(_handle, sizeBytes, offset);
            int size = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
            var blob = new byte[size];
            ReadExactly(_handle, blob, offset + 4);
            long objectId = i + 1;
            yield return (objectId, ReadRow(blob, objectId));
        }
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();

    private object?[] ReadRow(byte[] blob, long objectId)
    {
        var values = new object?[Fields.Count];
        var reader = new SpanCursor(blob);
        var nullFlags = reader.Bytes((NullableCount + 7) / 8);
        int nullable = 0;
        for (int i = 0; i < Fields.Count; i++)
        {
            var field = Fields[i];
            if (field.Type == FileGdbFieldType.ObjectId)
            {
                values[i] = objectId;
                continue;
            }
            if (field.IsNullable)
            {
                bool isNull = ((nullFlags[nullable / 8] >> (nullable % 8)) & 1) != 0;
                nullable++;
                if (isNull) continue;
            }
            values[i] = field.Type switch
            {
                FileGdbFieldType.Int16 => (int)reader.Int16(),
                FileGdbFieldType.Int32 => reader.Int32(),
                FileGdbFieldType.Int64 => reader.Int64(),
                FileGdbFieldType.Float32 => (double)reader.Single(),
                FileGdbFieldType.Float64 => reader.Double(),
                FileGdbFieldType.DateTime or FileGdbFieldType.DateOnly => Date(reader.Double()),
                FileGdbFieldType.TimeOnly => Date(reader.Double()),
                FileGdbFieldType.DateTimeWithOffset => DateWithOffset(ref reader),
                FileGdbFieldType.String or FileGdbFieldType.Xml => Encoding.UTF8.GetString(reader.Bytes(checked((int)reader.VarUInt()))),
                FileGdbFieldType.Guid or FileGdbFieldType.GlobalId => new Guid(reader.Bytes(16)),
                FileGdbFieldType.Geometry => FileGdbGeometryReader.Read(reader.Bytes(checked((int)reader.VarUInt())), field.Geometry!),
                FileGdbFieldType.Binary or FileGdbFieldType.Raster => Skip(ref reader),
                _ => throw new InvalidDataException($"{Name}: field {field.Name} has unknown type {(int)field.Type}."),
            };
        }
        return values;

        static object? Skip(ref SpanCursor reader)
        {
            reader.Bytes(checked((int)reader.VarUInt()));
            return null;
        }

        static object DateWithOffset(ref SpanCursor reader)
        {
            var date = Date(reader.Double());
            reader.Int16(); // the UTC offset in minutes, not kept
            return date;
        }
    }

    private static DateTime Date(double days) => Epoch.AddDays(days);

    private static (List<FileGdbField> Fields, int Nullable) ReadFields(byte[] section, string path)
    {
        var reader = new SpanCursor(section);
        int version = reader.Int32();
        if (version is not (3 or 4))
            throw new InvalidDataException($"{path}: unsupported field section version {version}.");
        reader.Int32(); // layer flags: geometry type and Z/M, also stated per geometry field
        int count = reader.UInt16();
        var fields = new List<FileGdbField>(count);
        int nullable = 0;
        for (int i = 0; i < count; i++)
        {
            string name = reader.Utf16(reader.Byte());
            reader.Utf16(reader.Byte()); // alias
            var type = (FileGdbFieldType)reader.Byte();
            bool isNullable;
            FileGdbGeometryInfo? geometry = null;
            switch (type)
            {
                case FileGdbFieldType.ObjectId:
                    reader.Bytes(2);
                    isNullable = false;
                    break;
                case FileGdbFieldType.String:
                    {
                        reader.Int32(); // maximum length
                        byte flags = reader.Byte();
                        isNullable = (flags & 1) != 0;
                        if ((flags & 4) != 0) reader.Bytes(checked((int)reader.VarUInt()));
                        break;
                    }
                case FileGdbFieldType.Geometry:
                    {
                        reader.Byte();
                        isNullable = (reader.Byte() & 1) != 0;
                        geometry = ReadGeometryInfo(ref reader);
                        break;
                    }
                case FileGdbFieldType.Binary or FileGdbFieldType.Guid or FileGdbFieldType.GlobalId or FileGdbFieldType.Xml:
                    reader.Byte();
                    isNullable = (reader.Byte() & 1) != 0;
                    break;
                case FileGdbFieldType.Raster:
                    throw new InvalidDataException($"{path}: raster fields are not supported.");
                default:
                    {
                        reader.Byte(); // width
                        byte flags = reader.Byte();
                        int defaultLength = reader.Byte();
                        isNullable = (flags & 1) != 0;
                        if ((flags & 4) != 0) reader.Bytes(defaultLength);
                        break;
                    }
            }
            if (isNullable) nullable++;
            fields.Add(new FileGdbField(name, type, isNullable, geometry));
        }
        return (fields, nullable);
    }

    private static FileGdbGeometryInfo ReadGeometryInfo(ref SpanCursor reader)
    {
        string wkt = reader.Utf16(reader.UInt16() / 2);
        byte flags = reader.Byte();
        bool hasM = (flags & 2) != 0;
        bool hasZ = (flags & 4) != 0;
        double xOrigin = reader.Double(), yOrigin = reader.Double(), xyScale = reader.Double();
        if (hasM)
        {
            reader.Double();
            reader.Double();
        }
        double zOrigin = 0, zScale = 1;
        if (hasZ)
        {
            zOrigin = reader.Double();
            zScale = reader.Double();
        }
        reader.Double(); // xy tolerance
        if (hasM) reader.Double();
        if (hasZ) reader.Double();
        for (int i = 0; i < 4; i++) reader.Double(); // extent

        // Then optional Z and M ranges, and the spatial index grid sizes after
        // a zero byte and a count of one to three.
        for (int attempt = 0; attempt < 3 && !reader.LooksLikeGridHeader(); attempt++)
        {
            reader.Double();
            reader.Double();
        }
        reader.Byte();
        int grids = reader.Int32();
        for (int i = 0; i < grids; i++) reader.Double();

        return new FileGdbGeometryInfo(wkt, hasZ, hasM, xOrigin, yOrigin, xyScale, zOrigin, zScale);
    }

    // The .gdbtablx: a header, then an offset for every row slot, with a
    // bitmap of which 1024-row blocks are present when the table is sparse.
    private static long[] ReadOffsets(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16)
            throw new InvalidDataException($"{path} is too short to be a file geodatabase table index.");
        int blocks = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        int rows = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        if (size is < 4 or > 8)
            throw new InvalidDataException($"{path}: unsupported offset size {size}.");

        long Offset(int slot)
        {
            long value = 0;
            int at = 16 + slot * size;
            for (int b = size - 1; b >= 0; b--) value = (value << 8) | bytes[at + b];
            return value;
        }

        int trailer = 16 + blocks * 1024 * size;
        int bitmapWords = bytes.Length >= trailer + 16 ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(trailer)) : 0;
        if (bitmapWords == 0)
        {
            var dense = new long[Math.Min(rows, blocks * 1024)];
            for (int i = 0; i < dense.Length; i++) dense[i] = Offset(i);
            return dense;
        }

        int total = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(trailer + 4));
        var bitmap = bytes.AsSpan(trailer + 16, bitmapWords * 4);
        var offsets = new long[total];
        int present = 0;
        for (int block = 0; block * 1024 < total; block++)
        {
            if (((bitmap[block / 8] >> (block % 8)) & 1) == 0) continue;
            for (int j = 0; j < 1024 && block * 1024 + j < total; j++)
                offsets[block * 1024 + j] = Offset(present * 1024 + j);
            present++;
        }
        return offsets;
    }

    private static void ReadExactly(SafeFileHandle handle, byte[] buffer, long offset)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = RandomAccess.Read(handle, buffer.AsSpan(read), offset + read);
            if (n == 0) throw new InvalidDataException("Unexpected end of a file geodatabase table.");
            read += n;
        }
    }
}
