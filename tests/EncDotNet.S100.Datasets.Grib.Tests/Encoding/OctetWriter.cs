using System.Buffers.Binary;

namespace EncDotNet.S100.Datasets.Grib.Tests.Encoding;

/// <summary>Big-endian GRIB2 octets, with sign-and-magnitude signed integers.</summary>
internal sealed class OctetWriter
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;

    public OctetWriter U8(int value)
    {
        _bytes.Add(checked((byte)value));
        return this;
    }

    public OctetWriter U16(int value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, checked((ushort)value));
        return Bytes(b);
    }

    public OctetWriter U32(long value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, checked((uint)value));
        return Bytes(b);
    }

    public OctetWriter U64(long value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, checked((ulong)value));
        return Bytes(b);
    }

    public OctetWriter F32(float value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(b, value);
        return Bytes(b);
    }

    public OctetWriter S8(int value) => U8((value < 0 ? 0x80 : 0) | checked((byte)Math.Abs(value)));

    public OctetWriter S16(int value) => SignMagnitude(value, 2);

    public OctetWriter S32(long value) => SignMagnitude(value, 4);

    public OctetWriter SignMagnitude(long value, int octets)
    {
        var magnitude = (ulong)Math.Abs(value);
        var sign = 1UL << ((octets * 8) - 1);
        if (magnitude >= sign)
            throw new ArgumentOutOfRangeException(nameof(value), $"{value} doesn't fit in {octets} sign-and-magnitude octets.");
        var raw = magnitude | (value < 0 ? sign : 0);
        for (var i = octets - 1; i >= 0; i--)
            _bytes.Add((byte)(raw >> (i * 8)));
        return this;
    }

    public OctetWriter Bytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            _bytes.Add(b);
        return this;
    }

    public byte[] ToArray() => [.. _bytes];

    /// <summary>A section: its 4-octet length, its number, then the body.</summary>
    public static byte[] Section(int number, Action<OctetWriter> body)
    {
        var content = new OctetWriter();
        body(content);
        return new OctetWriter().U32(content.Length + 5).U8(number).Bytes(content.ToArray()).ToArray();
    }
}
