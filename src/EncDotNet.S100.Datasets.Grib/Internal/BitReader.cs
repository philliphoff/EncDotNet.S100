namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// Reads unsigned integers of 0 to 32 bits, most significant bit first, from a
/// GRIB2 data section.
/// </summary>
internal ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _bitPosition;

    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bitPosition = 0;
    }

    /// <summary>The position in bits from the start of the data.</summary>
    public readonly long BitPosition => _bitPosition;

    /// <summary>Reads <paramref name="bits"/> bits (0 to 32) as an unsigned integer.</summary>
    /// <exception cref="GribFormatException">The read runs past the end of the data.</exception>
    public uint Read(int bits)
    {
        if (bits == 0)
            return 0;

        var end = _bitPosition + bits;
        if (end > (long)_data.Length * 8)
            throw new GribFormatException("The data section (Section 7) ends before all packed values were read.");

        // Gather the (at most five) octets that hold the bits into a 64-bit
        // accumulator, then shift the wanted bits down.
        var firstByte = (int)(_bitPosition >> 3);
        var lastByte = (int)((end - 1) >> 3);
        ulong accumulator = 0;
        for (var i = firstByte; i <= lastByte; i++)
            accumulator = (accumulator << 8) | _data[i];

        var trailing = (int)(((lastByte + 1L) * 8) - end);
        _bitPosition = end;
        return (uint)((accumulator >> trailing) & ((1UL << bits) - 1));
    }

    /// <summary>Moves to the next octet boundary, if not already on one.</summary>
    public void AlignToOctet() => _bitPosition = (_bitPosition + 7) & ~7L;
}
