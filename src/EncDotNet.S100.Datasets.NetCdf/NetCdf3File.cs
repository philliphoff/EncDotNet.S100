using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// Reads NetCDF-3 files: the classic format (<c>CDF\x01</c>) and the 64-bit
/// offset format (<c>CDF\x02</c>), written from the Unidata "NetCDF Classic
/// Format Specification" (NetCDF User Guide, File Format appendix).
/// </summary>
/// <remarks>
/// <para>The header grammar, all big-endian:</para>
/// <code>
/// header    = magic numrecs dim_list gatt_list var_list
/// dim_list  = ABSENT | NC_DIMENSION(0x0A) nelems [name dim_length ...]
/// att_list  = ABSENT | NC_ATTRIBUTE(0x0C) nelems [name nc_type nelems values ...]
/// var_list  = ABSENT | NC_VARIABLE(0x0B)  nelems [name nelems [dimid ...] vatt_list nc_type vsize begin ...]
/// ABSENT    = 8 zero bytes; names and values are padded to 4 bytes
/// begin     = 32-bit offset (classic) or 64-bit offset (CDF\x02)
/// </code>
/// <para>
/// Fixed-size variables are stored contiguously at <c>begin</c>. Record
/// variables (whose first dimension is the record dimension, length 0 in the
/// header) are interleaved: record <c>r</c> of each starts at
/// <c>begin + r × recsize</c>, where <c>recsize</c> is the sum of every
/// record variable's per-record size, each padded to 4 bytes, except that a
/// file with a single record variable isn't padded. A <c>numrecs</c> of
/// <c>0xFFFFFFFF</c> (streaming) is worked out from the file length.
/// </para>
/// </remarks>
internal sealed class NetCdf3File : NetCdfFile
{
    private const int NcDimension = 0x0A;
    private const int NcVariable = 0x0B;
    private const int NcAttribute = 0x0C;
    private const uint Streaming = 0xFFFFFFFF;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly Dictionary<NetCdfVariable, Layout> _layouts = new(ReferenceEqualityComparer.Instance);
    private long _recordSize;
    private NetCdfFormat _format;

    public NetCdf3File(Stream stream, bool leaveOpen)
    {
        _stream = stream;
        _leaveOpen = leaveOpen;
        _stream.Position = 0;
        var header = new HeaderReader(stream);
        try
        {
            ReadHeader(header);
        }
        catch (EndOfStreamException ex)
        {
            throw new NetCdfFormatException("The NetCDF-3 header ends early; the file is truncated or damaged.", ex);
        }
    }

    public override NetCdfFormat Format => _format;

    private void ReadHeader(HeaderReader reader)
    {
        var magic = reader.ReadBytes(4);
        _format = magic[3] == 2 ? NetCdfFormat.Offset64 : NetCdfFormat.Classic;
        bool offset64 = _format == NetCdfFormat.Offset64;
        uint numrecs = reader.ReadUInt32();

        // Dimensions.
        var rawDimensions = new List<(string Name, long Length)>();
        int dimensionCount = ReadListHeader(reader, NcDimension, "dimension");
        for (int i = 0; i < dimensionCount; i++)
        {
            string name = reader.ReadName();
            long length = reader.ReadNonNegative();
            rawDimensions.Add((name, length));
        }

        GlobalAttributes = ReadAttributes(reader);

        // Variables: read the header entries first; the record dimension's
        // length may only be known once the record size is.
        var entries = new List<(string Name, int[] DimensionIds, IReadOnlyList<NetCdfAttribute> Attributes, int Type, long Begin)>();
        int variableCount = ReadListHeader(reader, NcVariable, "variable");
        for (int i = 0; i < variableCount; i++)
        {
            string name = reader.ReadName();
            int rank = checked((int)reader.ReadNonNegative());
            var ids = new int[rank];
            for (int d = 0; d < rank; d++)
            {
                ids[d] = checked((int)reader.ReadNonNegative());
                if (ids[d] >= rawDimensions.Count)
                    throw new NetCdfFormatException($"Variable '{name}' refers to dimension {ids[d]}, but the file has {rawDimensions.Count}.");
            }

            var attributes = ReadAttributes(reader);
            int type = reader.ReadInt32();
            reader.ReadUInt32(); // vsize: recomputed below, as the spec allows it to be wrong for large variables.
            long begin = offset64 ? reader.ReadInt64() : reader.ReadUInt32();
            entries.Add((name, ids, attributes, type, begin));
        }

        int recordDimension = rawDimensions.FindIndex(d => d.Length == 0);

        // Record size: every record variable's per-record bytes, padded to 4,
        // unless there's only one record variable.
        var recordSlabs = new List<long>();
        foreach (var entry in entries)
        {
            if (entry.DimensionIds.Length > 0 && entry.DimensionIds[0] == recordDimension && recordDimension >= 0)
                recordSlabs.Add(SlabBytes(entry.DimensionIds.AsSpan(1), rawDimensions, entry.Type, entry.Name));
        }

        _recordSize = recordSlabs.Count == 1 ? recordSlabs[0] : recordSlabs.Sum(Pad4);

        long records = numrecs;
        if (numrecs == Streaming)
        {
            long firstRecordBegin = entries
                .Where(e => e.DimensionIds.Length > 0 && e.DimensionIds[0] == recordDimension)
                .Select(e => e.Begin)
                .DefaultIfEmpty(_stream.Length)
                .Min();
            records = _recordSize > 0 ? Math.Max(0, (_stream.Length - firstRecordBegin) / _recordSize) : 0;
        }

        var dimensions = rawDimensions
            .Select((d, i) => i == recordDimension
                ? new NetCdfDimension(d.Name, records, IsUnlimited: true)
                : new NetCdfDimension(d.Name, d.Length))
            .ToArray();
        Dimensions = dimensions;

        var variables = new List<NetCdfVariable>(entries.Count);
        foreach (var entry in entries)
        {
            var dataType = ToDataType(entry.Type, entry.Name);
            var variable = new NetCdfVariable(
                entry.Name,
                dataType,
                entry.DimensionIds.Select(id => dimensions[id]).ToArray(),
                entry.Attributes);
            bool isRecord = entry.DimensionIds.Length > 0 && entry.DimensionIds[0] == recordDimension && recordDimension >= 0;
            _layouts[variable] = new Layout(entry.Begin, ElementSize(entry.Type), entry.Type, isRecord);
            variables.Add(variable);
        }

        Variables = variables;
    }

    private static int ReadListHeader(HeaderReader reader, int tag, string what)
    {
        int found = reader.ReadInt32();
        long count = reader.ReadNonNegative();
        if (found == 0 && count == 0)
            return 0; // ABSENT
        if (found != tag)
        {
            throw new NetCdfFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The NetCDF-3 header has tag 0x{found:X} where the {what} list (tag 0x{tag:X}) belongs; the file is damaged."));
        }

        return checked((int)count);
    }

    private static List<NetCdfAttribute> ReadAttributes(HeaderReader reader)
    {
        var attributes = new List<NetCdfAttribute>();
        int count = ReadListHeader(reader, NcAttribute, "attribute");
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadName();
            int type = reader.ReadInt32();
            int length = checked((int)reader.ReadNonNegative());
            int size = ElementSize(type);
            var bytes = reader.ReadBytes(checked(length * size));
            reader.Skip(Pad4(length * size) - length * size);

            if (type == 2)
            {
                attributes.Add(new NetCdfAttribute(name, DecodeText(bytes)));
                continue;
            }

            var values = new double[length];
            for (int v = 0; v < length; v++)
                values[v] = Decode(bytes.AsSpan(v * size, size), type);
            attributes.Add(new NetCdfAttribute(name, ToDataType(type, name), values));
        }

        return attributes;
    }

    private protected override double[] ReadCore(NetCdfVariable variable, long[] start, long[] count, int total)
    {
        var layout = _layouts[variable];
        int rank = variable.Dimensions.Count;
        var result = new double[total];
        int size = layout.ElementSize;

        if (rank == 0)
        {
            ReadRun(layout.Begin, 1, result, layout);
            return result;
        }

        // Element strides within one record (record variables) or within the
        // variable (fixed-size variables).
        int first = layout.IsRecord ? 1 : 0;
        var strides = new long[rank];
        long stride = 1;
        for (int d = rank - 1; d >= first; d--)
        {
            strides[d] = stride;
            stride *= variable.Dimensions[d].Length;
        }

        // The innermost run of contiguous values: the last dimension, widened
        // over each trailing dimension read in full. Records aren't contiguous,
        // so a record variable's run never includes the record dimension; one
        // with no other dimension reads one value per record.
        int runDimension;
        long run;
        if (layout.IsRecord && rank == 1)
        {
            runDimension = 1;
            run = 1;
        }
        else
        {
            runDimension = rank - 1;
            run = count[rank - 1];
            while (runDimension > first &&
                   start[runDimension] == 0 && count[runDimension] == variable.Dimensions[runDimension].Length)
            {
                runDimension--;
                run *= count[runDimension];
            }
        }

        // Odometer over the dimensions outside the run (0 .. runDimension-1).
        var index = new long[rank];
        var buffer = new double[run];
        int written = 0;
        while (true)
        {
            long offset = layout.Begin;
            if (layout.IsRecord)
                offset += (start[0] + index[0]) * _recordSize;
            for (int d = first; d < rank; d++)
                offset += (start[d] + (d < runDimension ? index[d] : 0)) * strides[d] * size;

            ReadRun(offset, (int)run, buffer, layout);
            buffer.CopyTo(result.AsSpan(written));
            written += (int)run;

            int dim = runDimension - 1;
            while (dim >= 0)
            {
                if (++index[dim] < count[dim])
                    break;
                index[dim] = 0;
                dim--;
            }

            if (dim < 0)
                break;
        }

        return result;
    }

    private void ReadRun(long offset, int length, Span<double> destination, Layout layout)
    {
        var bytes = new byte[length * layout.ElementSize];
        _stream.Position = offset;
        try
        {
            _stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException ex)
        {
            throw new NetCdfFormatException("A NetCDF-3 variable's data runs past the end of the file; the file is truncated.", ex);
        }

        for (int i = 0; i < length; i++)
            destination[i] = Decode(bytes.AsSpan(i * layout.ElementSize, layout.ElementSize), layout.Type);
    }

    private protected override void DisposeCore()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }

    private static long SlabBytes(ReadOnlySpan<int> dimensionIds, List<(string Name, long Length)> dimensions, int type, string name)
    {
        long elements = 1;
        foreach (var id in dimensionIds)
            elements *= dimensions[id].Length;
        return elements * ElementSize(type, name);
    }

    private static long Pad4(long bytes) => (bytes + 3) & ~3L;

    private static int ElementSize(int type, string? name = null) => type switch
    {
        1 or 2 => 1,
        3 => 2,
        4 or 5 => 4,
        6 => 8,
        _ => throw new NetCdfFormatException(
            name is null
                ? string.Create(CultureInfo.InvariantCulture, $"The NetCDF-3 header has an unknown type code {type}.")
                : string.Create(CultureInfo.InvariantCulture, $"'{name}' has an unknown NetCDF-3 type code {type}.")),
    };

    private static NetCdfDataType ToDataType(int type, string name) => type switch
    {
        1 => NetCdfDataType.Byte,
        2 => NetCdfDataType.Char,
        3 => NetCdfDataType.Short,
        4 => NetCdfDataType.Int,
        5 => NetCdfDataType.Float,
        6 => NetCdfDataType.Double,
        _ => throw new NetCdfFormatException(
            string.Create(CultureInfo.InvariantCulture, $"'{name}' has an unknown NetCDF-3 type code {type}.")),
    };

    private static double Decode(ReadOnlySpan<byte> bytes, int type) => type switch
    {
        1 => (sbyte)bytes[0],
        2 => bytes[0],
        3 => BinaryPrimitives.ReadInt16BigEndian(bytes),
        4 => BinaryPrimitives.ReadInt32BigEndian(bytes),
        5 => BinaryPrimitives.ReadSingleBigEndian(bytes),
        6 => BinaryPrimitives.ReadDoubleBigEndian(bytes),
        _ => double.NaN,
    };

    private static string DecodeText(byte[] bytes)
    {
        int length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
            length--;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    private readonly record struct Layout(long Begin, int ElementSize, int Type, bool IsRecord);

    // Sequential big-endian reads of the header.
    private sealed class HeaderReader(Stream stream)
    {
        private readonly byte[] _scratch = new byte[8];

        public byte[] ReadBytes(int count)
        {
            var bytes = new byte[count];
            stream.ReadExactly(bytes);
            return bytes;
        }

        public void Skip(long count)
        {
            if (count > 0)
                stream.Position += count;
        }

        public int ReadInt32()
        {
            stream.ReadExactly(_scratch.AsSpan(0, 4));
            return BinaryPrimitives.ReadInt32BigEndian(_scratch);
        }

        public uint ReadUInt32()
        {
            stream.ReadExactly(_scratch.AsSpan(0, 4));
            return BinaryPrimitives.ReadUInt32BigEndian(_scratch);
        }

        public long ReadInt64()
        {
            stream.ReadExactly(_scratch.AsSpan(0, 8));
            return BinaryPrimitives.ReadInt64BigEndian(_scratch);
        }

        public long ReadNonNegative()
        {
            int value = ReadInt32();
            if (value < 0)
                throw new NetCdfFormatException("The NetCDF-3 header has a negative count; the file is damaged.");
            return value;
        }

        public string ReadName()
        {
            int length = checked((int)ReadNonNegative());
            var bytes = ReadBytes(length);
            Skip(Pad4(length) - length);
            return Encoding.UTF8.GetString(bytes).Normalize(NormalizationForm.FormC);
        }
    }
}
