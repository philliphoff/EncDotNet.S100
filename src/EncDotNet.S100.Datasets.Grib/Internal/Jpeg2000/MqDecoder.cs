namespace EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

/// <summary>
/// The MQ arithmetic decoder of JPEG 2000 (ITU-T T.800 Annex C), with the
/// 19 contexts the code-block coding passes use.
/// </summary>
/// <remarks>
/// The code register follows the encoder's convention: the more probable
/// symbol takes the upper part of the interval, so a value of <c>C</c> below
/// <c>Qe</c> selects the lower part. Reading past the end of the segment
/// behaves as if the data ended in a marker, feeding 1 bits, as C.3.4
/// requires.
/// </remarks>
internal sealed class MqDecoder
{
    /// <summary>The number of contexts T.800 uses (Table D.7).</summary>
    public const int ContextCount = 19;

    /// <summary>The run-length context.</summary>
    public const int RunLengthContext = 17;

    /// <summary>The uniform context.</summary>
    public const int UniformContext = 18;

    // Table C.2: the probability estimate, the next state after an MPS and
    // after an LPS, and whether an LPS switches the MPS sense.
    private static readonly ushort[] Qe =
    [
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801,
        0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401, 0x5101, 0x4801, 0x3801, 0x3401,
        0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101,
        0x0AC1, 0x09C1, 0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085,
        0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
    ];

    private static readonly byte[] NextMps =
    [
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20,
        21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
        41, 42, 43, 44, 45, 45, 46,
    ];

    private static readonly byte[] NextLps =
    [
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18,
        19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
        38, 39, 40, 41, 42, 43, 46,
    ];

    private static readonly byte[] Switch =
    [
        1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0,
    ];

    // Each context is its state index and MPS sense packed as (index << 1) | mps.
    private readonly byte[] _contexts = new byte[ContextCount];
    private byte[] _data = [];
    private int _position;
    private int _end;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <summary>Sets every context to its initial state (Table D.7).</summary>
    public void ResetContexts()
    {
        Array.Clear(_contexts);
        _contexts[0] = 4 << 1;
        _contexts[RunLengthContext] = 3 << 1;
        _contexts[UniformContext] = 46 << 1;
    }

    /// <summary>Starts decoding a terminated segment (INITDEC, C.3.5). The contexts are kept.</summary>
    /// <param name="data">The buffer holding the segment.</param>
    /// <param name="offset">Where the segment starts.</param>
    /// <param name="length">The segment's length in bytes.</param>
    public void Start(byte[] data, int offset, int length)
    {
        _data = data;
        _position = offset;
        _end = offset + length;
        _c = (uint)Byte(_position) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    /// <summary>Decodes one binary decision in context <paramref name="cx"/> (DECODE, C.3.2).</summary>
    public int Decode(int cx)
    {
        var state = _contexts[cx];
        var index = state >> 1;
        var mps = state & 1;
        uint qe = Qe[index];
        _a -= qe;
        int d;
        if ((_c >> 16) < qe)
        {
            // LPS_EXCHANGE: the lower interval, which is the MPS when it's the larger.
            if (_a < qe)
            {
                d = mps;
                _contexts[cx] = (byte)((NextMps[index] << 1) | mps);
            }
            else
            {
                d = 1 - mps;
                _contexts[cx] = (byte)((NextLps[index] << 1) | (mps ^ Switch[index]));
            }

            _a = qe;
            Renormalise();
        }
        else
        {
            _c -= qe << 16;
            if ((_a & 0x8000) != 0)
                return mps;

            // MPS_EXCHANGE.
            if (_a < qe)
            {
                d = 1 - mps;
                _contexts[cx] = (byte)((NextLps[index] << 1) | (mps ^ Switch[index]));
            }
            else
            {
                d = mps;
                _contexts[cx] = (byte)((NextMps[index] << 1) | mps);
            }

            Renormalise();
        }

        return d;
    }

    private void Renormalise()
    {
        do
        {
            if (_ct == 0)
                ByteIn();
            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);
    }

    // BYTEIN (C.3.4). _position is the byte last read into C.
    private void ByteIn()
    {
        if (Byte(_position) == 0xFF)
        {
            var next = Byte(_position + 1);
            if (next > 0x8F)
            {
                // A marker, or the end of the segment: feed 1 bits.
                _c += 0xFF00;
                _ct = 8;
            }
            else
            {
                _position++;
                _c += (uint)next << 9;
                _ct = 7;
            }
        }
        else
        {
            _position++;
            _c += (uint)Byte(_position) << 8;
            _ct = 8;
        }
    }

    private int Byte(int position) => position < _end ? _data[position] : 0xFF;
}

/// <summary>
/// Reads the raw (bypassed) bits of selective arithmetic coding bypass
/// (T.800 D.6): most significant bit first, with a 0 bit stuffed after each
/// 0xFF byte. Past the end of the segment it reads 1 bits.
/// </summary>
internal sealed class RawBitDecoder
{
    private byte[] _data = [];
    private int _position;
    private int _end;
    private int _c;
    private int _ct;

    /// <summary>Starts reading a segment.</summary>
    public void Start(byte[] data, int offset, int length)
    {
        _data = data;
        _position = offset;
        _end = offset + length;
        _c = 0;
        _ct = 0;
    }

    /// <summary>Reads one bit.</summary>
    public int Decode()
    {
        if (_ct == 0)
        {
            var b = _position < _end ? _data[_position] : 0xFF;
            if (_c == 0xFF)
            {
                if (b > 0x8F)
                {
                    _c = 0xFF;
                    _ct = 8;
                }
                else
                {
                    _c = b;
                    _position++;
                    _ct = 7;
                }
            }
            else
            {
                _c = b;
                _position++;
                _ct = 8;
            }
        }

        _ct--;
        return (_c >> _ct) & 1;
    }
}
