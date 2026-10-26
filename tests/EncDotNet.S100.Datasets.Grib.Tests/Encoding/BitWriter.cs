namespace EncDotNet.S100.Datasets.Grib.Tests.Encoding;

/// <summary>Writes unsigned integers of 0 to 32 bits, most significant bit first.</summary>
internal sealed class BitWriter
{
    private readonly List<byte> _bytes = [];
    private int _current;
    private int _used;

    public void Write(ulong value, int bits)
    {
        if (bits < 0 || bits > 32)
            throw new ArgumentOutOfRangeException(nameof(bits));
        if (bits < 64 && value >> bits != 0)
            throw new ArgumentOutOfRangeException(nameof(value), $"{value} doesn't fit in {bits} bits.");

        for (var i = bits - 1; i >= 0; i--)
        {
            _current = (_current << 1) | (int)((value >> i) & 1);
            if (++_used == 8)
                Flush();
        }
    }

    public void Align()
    {
        if (_used == 0)
            return;
        _current <<= 8 - _used;
        Flush();
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        Align();
        foreach (var b in bytes)
            _bytes.Add(b);
    }

    public byte[] ToArray()
    {
        Align();
        return [.. _bytes];
    }

    private void Flush()
    {
        _bytes.Add((byte)_current);
        _current = 0;
        _used = 0;
    }

    /// <summary>The bits needed to hold <paramref name="value"/> (0 for 0).</summary>
    public static int BitsFor(ulong value)
    {
        var bits = 0;
        while (value != 0)
        {
            bits++;
            value >>= 1;
        }

        return bits;
    }
}
