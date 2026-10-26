using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

/// <summary>
/// Reads packet header bits (T.800 B.10.1): most significant bit first, with
/// a 0 bit stuffed after each 0xFF byte.
/// </summary>
internal ref struct PacketHeaderReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private int _buffer;
    private int _ct;

    public PacketHeaderReader(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
        _buffer = 0;
        _ct = 0;
    }

    /// <summary>The position of the next unread byte.</summary>
    public readonly int Position => _position;

    /// <summary>Reads one bit.</summary>
    /// <exception cref="GribFormatException">The data ends inside the packet header.</exception>
    public int ReadBit()
    {
        if (_ct == 0)
        {
            if (_position >= _data.Length)
                throw new GribFormatException("The JPEG 2000 codestream ends inside a packet header.");
            _ct = _buffer == 0xFF ? 7 : 8;
            _buffer = _data[_position++];
        }

        _ct--;
        return (_buffer >> _ct) & 1;
    }

    /// <summary>Reads <paramref name="bits"/> bits (0 to 31) as an unsigned integer.</summary>
    public int Read(int bits)
    {
        var value = 0;
        for (var i = 0; i < bits; i++)
            value = (value << 1) | ReadBit();
        return value;
    }

    /// <summary>
    /// Ends the header on a byte boundary. When the last byte read was 0xFF,
    /// the stuffed byte after it is part of the header too.
    /// </summary>
    public void Align()
    {
        if (_buffer == 0xFF && _ct < 8)
        {
            if (_position >= _data.Length)
                throw new GribFormatException("The JPEG 2000 codestream ends inside a packet header.");
            _position++;
        }

        _ct = 0;
        _buffer = 0;
    }
}

/// <summary>
/// A tag tree (T.800 B.10.2): a quadtree of minima over a grid of
/// code-blocks, coded top down so that a value is sent only as far as it's
/// needed. Used for the code-blocks' first layer and their zero bit-planes.
/// </summary>
internal sealed class TagTree
{
    private const int Unknown = int.MaxValue;

    private readonly int[] _value;
    private readonly int[] _low;
    private readonly int[] _parent;

    /// <summary>Creates a tree over <paramref name="width"/> × <paramref name="height"/> leaves, all unknown.</summary>
    public TagTree(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "A tag tree needs at least one leaf.");

        // Count the nodes level by level, leaves first, up to the 1 × 1 root.
        var total = 0;
        int w = width, h = height;
        while (true)
        {
            total += w * h;
            if (w == 1 && h == 1)
                break;
            w = (w + 1) / 2;
            h = (h + 1) / 2;
        }

        _value = new int[total];
        _low = new int[total];
        _parent = new int[total];
        Array.Fill(_value, Unknown);

        var start = 0;
        w = width;
        h = height;
        while (w > 1 || h > 1)
        {
            int pw = (w + 1) / 2, ph = (h + 1) / 2;
            var parentStart = start + (w * h);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                    _parent[start + (y * w) + x] = parentStart + ((y / 2) * pw) + (x / 2);
            }

            start = parentStart;
            w = pw;
            h = ph;
        }

        _parent[start] = -1;
        Width = width;
        Height = height;
    }

    /// <summary>The number of leaf columns.</summary>
    public int Width { get; }

    /// <summary>The number of leaf rows.</summary>
    public int Height { get; }

    /// <summary>
    /// Reads as many bits as are needed to tell whether leaf
    /// <paramref name="leaf"/>'s value is below <paramref name="threshold"/>.
    /// </summary>
    /// <returns>Whether the leaf's value is below the threshold.</returns>
    public bool Decode(ref PacketHeaderReader reader, int leaf, int threshold)
    {
        Span<int> path = stackalloc int[32];
        var depth = 0;
        for (var node = leaf; node >= 0; node = _parent[node])
            path[depth++] = node;

        var low = 0;
        for (var i = depth - 1; i >= 0; i--)
        {
            var node = path[i];
            if (low > _low[node])
                _low[node] = low;
            else
                low = _low[node];

            while (low < threshold && low < _value[node])
            {
                if (reader.ReadBit() == 1)
                    _value[node] = low;
                else
                    low++;
            }

            _low[node] = low;
        }

        return _value[leaf] < threshold;
    }

    /// <summary>Reads leaf <paramref name="leaf"/>'s value in full.</summary>
    /// <exception cref="GribFormatException">The value is implausibly large.</exception>
    public int DecodeValue(ref PacketHeaderReader reader, int leaf)
    {
        var threshold = 1;
        while (!Decode(ref reader, leaf, threshold))
        {
            if (++threshold > 255)
            {
                throw new GribFormatException(
                    string.Create(CultureInfo.InvariantCulture, $"A JPEG 2000 tag tree value exceeds {threshold - 1}."));
            }
        }

        return threshold - 1;
    }
}
