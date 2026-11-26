using System.Buffers.Binary;
using System.Text;

namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>A forward reader over little-endian bytes, with the file geodatabase's variable-length integers.</summary>
internal ref struct SpanCursor
{
    private readonly ReadOnlySpan<byte> _bytes;
    private int _at;

    public SpanCursor(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        _at = 0;
    }

    public readonly bool AtEnd => _at >= _bytes.Length;

    public byte Byte() => Take(1)[0];

    public short Int16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public double Double() => BinaryPrimitives.ReadDoubleLittleEndian(Take(8));

    public byte[] Bytes(int count) => Take(count).ToArray();

    public string Utf16(int characters) => Encoding.Unicode.GetString(Take(characters * 2));

    /// <summary>An unsigned integer, seven bits a byte, low bits first; the top bit says another byte follows.</summary>
    public ulong VarUInt()
    {
        ulong value = 0;
        int shift = 0;
        while (true)
        {
            byte b = Byte();
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("A variable-length integer is too long.");
        }
    }

    /// <summary>
    /// A signed integer: the first byte holds six bits and the sign (0x40),
    /// later bytes seven bits each; the top bit says another byte follows.
    /// </summary>
    public long VarInt()
    {
        byte b = Byte();
        bool negative = (b & 0x40) != 0;
        long value = b & 0x3F;
        int shift = 6;
        while ((b & 0x80) != 0)
        {
            b = Byte();
            value |= (long)(b & 0x7F) << shift;
            shift += 7;
            if (shift > 63) throw new InvalidDataException("A variable-length integer is too long.");
        }
        return negative ? -value : value;
    }

    /// <summary>Whether the next five bytes are a zero byte and a grid count of one to three.</summary>
    public readonly bool LooksLikeGridHeader()
        => _at + 5 <= _bytes.Length
            && _bytes[_at] == 0
            && BinaryPrimitives.ReadInt32LittleEndian(_bytes.Slice(_at + 1, 4)) is >= 1 and <= 3;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || _at + count > _bytes.Length)
            throw new InvalidDataException("Unexpected end of file geodatabase data.");
        var span = _bytes.Slice(_at, count);
        _at += count;
        return span;
    }
}
