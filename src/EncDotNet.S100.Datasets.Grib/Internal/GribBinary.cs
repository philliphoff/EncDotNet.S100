using System.Buffers.Binary;

namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// Big-endian reads of GRIB2 octets. Offsets are zero-based, so the spec's
/// "octet 13" of a section is index 12.
/// </summary>
/// <remarks>
/// GRIB2 signed integers are sign and magnitude, not two's complement: the
/// most significant bit is the sign and the rest is the magnitude (WMO Manual
/// on Codes, FM 92 GRIB, regulation 92.1.5). A value with every bit set is
/// "missing" for unsigned fields.
/// </remarks>
internal static class GribBinary
{
    public static ushort UInt16(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadUInt16BigEndian(s[offset..]);

    public static uint UInt32(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadUInt32BigEndian(s[offset..]);

    public static ulong UInt64(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadUInt64BigEndian(s[offset..]);

    public static float Float32(ReadOnlySpan<byte> s, int offset) => BinaryPrimitives.ReadSingleBigEndian(s[offset..]);

    /// <summary>A one-octet sign-and-magnitude integer.</summary>
    public static int SignedInt8(ReadOnlySpan<byte> s, int offset)
    {
        var raw = s[offset];
        var magnitude = raw & 0x7F;
        return (raw & 0x80) != 0 ? -magnitude : magnitude;
    }

    /// <summary>A two-octet sign-and-magnitude integer.</summary>
    public static int SignedInt16(ReadOnlySpan<byte> s, int offset)
    {
        var raw = UInt16(s, offset);
        var magnitude = raw & 0x7FFF;
        return (raw & 0x8000) != 0 ? -magnitude : magnitude;
    }

    /// <summary>A four-octet sign-and-magnitude integer.</summary>
    public static int SignedInt32(ReadOnlySpan<byte> s, int offset)
    {
        var raw = UInt32(s, offset);
        var magnitude = (int)(raw & 0x7FFF_FFFF);
        return (raw & 0x8000_0000) != 0 ? -magnitude : magnitude;
    }

    /// <summary>A sign-and-magnitude integer of <paramref name="octets"/> octets (1 to 4).</summary>
    public static long SignedInt(ReadOnlySpan<byte> s, int offset, int octets)
    {
        ulong raw = 0;
        for (var i = 0; i < octets; i++)
            raw = (raw << 8) | s[offset + i];
        var signBit = 1UL << ((octets * 8) - 1);
        var magnitude = (long)(raw & (signBit - 1));
        return (raw & signBit) != 0 ? -magnitude : magnitude;
    }
}
