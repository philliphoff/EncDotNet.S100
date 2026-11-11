using EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

namespace EncDotNet.S100.Datasets.Grib.Tests.Jpeg2000;

/// <summary>The MQ decoder and the raw bit decoder on their own.</summary>
public class MqCoderTests
{
    [Fact]
    public void Decodes_the_T88_test_sequence()
    {
        // ITU-T T.88 (JBIG2) Annex H.2: the same MQ coder, one context
        // starting at state 0. 256 decisions, most significant bit first.
        byte[] decisions = [0x00, 0x02, 0x00, 0x51, 0x00, 0x00, 0x00, 0xC0, 0x03, 0x52, 0x87, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA,
            0x82, 0xC0, 0x20, 0x00, 0xFC, 0xD7, 0x9E, 0xF6, 0xBF, 0x7F, 0xED, 0x90, 0x4F, 0x46, 0xA3, 0xBF];
        byte[] coded = [0x84, 0xC7, 0x3B, 0xFC, 0xE1, 0xA1, 0x43, 0x04, 0x02, 0x20, 0x00, 0x00, 0x41, 0x0D, 0xBB, 0x86,
            0xF4, 0x31, 0x7F, 0xFF, 0x88, 0xFF, 0x37, 0x47, 0x1A, 0xDB, 0x6A, 0xDF, 0xFF, 0xAC];
        var decoder = new MqDecoder();
        decoder.ResetContexts();
        decoder.Start(coded, 0, coded.Length);

        // Context 1 starts at state 0, MPS 0 (context 0 starts at state 4).
        for (var i = 0; i < decisions.Length * 8; i++)
            Assert.Equal((decisions[i / 8] >> (7 - (i % 8))) & 1, decoder.Decode(1));
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 0.9)]
    [InlineData(3, 0.02)]
    [InlineData(4, 0.999)]
    public void Decodes_what_the_reference_encoder_wrote(int seed, double probability)
    {
        var random = new Random(seed);
        var symbols = new (int Context, int Bit)[20000];
        for (var i = 0; i < symbols.Length; i++)
        {
            var cx = random.Next(MqDecoder.ContextCount);
            symbols[i] = (cx, random.NextDouble() < (cx % 2 == 0 ? probability : 1 - probability) ? 1 : 0);
        }

        var encoder = new MqTestEncoder();
        foreach (var (cx, bit) in symbols)
            encoder.Encode(cx, bit);
        var coded = encoder.Flush();

        var decoder = new MqDecoder();
        decoder.ResetContexts();
        decoder.Start(coded, 0, coded.Length);
        foreach (var (cx, bit) in symbols)
            Assert.Equal(bit, decoder.Decode(cx));
    }

    [Fact]
    public void Raw_decoder_skips_the_stuffed_bit_after_0xFF()
    {
        // 0xFF, then 0x7F holds 7 bits (its top bit is the stuffed 0): 1111111.
        var raw = new RawBitDecoder();
        raw.Start([0xFF, 0x7F, 0x80], 0, 3);
        var bits = Enumerable.Range(0, 8 + 7 + 8).Select(_ => raw.Decode()).ToArray();

        Assert.Equal([1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0], bits);

        // Past the end it reads 1s.
        Assert.Equal(1, raw.Decode());
    }

    /// <summary>The MQ encoder of T.800 C.2, software conventions, for the tests only.</summary>
    private sealed class MqTestEncoder
    {
        private static readonly ushort[] Qe =
        [
            0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801,
            0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401, 0x5101, 0x4801, 0x3801, 0x3401,
            0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101,
            0x0AC1, 0x09C1, 0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085,
            0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
        ];

        private static readonly int[] NextMps =
        [
            1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20,
            21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
            41, 42, 43, 44, 45, 45, 46,
        ];

        private static readonly int[] NextLps =
        [
            1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18,
            19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
            38, 39, 40, 41, 42, 43, 46,
        ];

        private readonly int[] _index = new int[MqDecoder.ContextCount];
        private readonly int[] _mps = new int[MqDecoder.ContextCount];
        private readonly List<byte> _out = [];
        private uint _a = 0x8000;
        private uint _c;
        private int _ct = 12;
        private int _b;

        public MqTestEncoder()
        {
            _index[0] = 4;
            _index[MqDecoder.RunLengthContext] = 3;
            _index[MqDecoder.UniformContext] = 46;
        }

        public void Encode(int cx, int d)
        {
            var i = _index[cx];
            uint qe = Qe[i];
            _a -= qe;
            if (d == _mps[cx])
            {
                if ((_a & 0x8000) == 0)
                {
                    if (_a < qe)
                        _a = qe;
                    else
                        _c += qe;
                    _index[cx] = NextMps[i];
                    Renormalise();
                }
                else
                {
                    _c += qe;
                }
            }
            else
            {
                if (_a < qe)
                    _c += qe;
                else
                    _a = qe;
                if (i is 0 or 6 or 14)
                    _mps[cx] = 1 - _mps[cx];
                _index[cx] = NextLps[i];
                Renormalise();
            }
        }

        public byte[] Flush()
        {
            // SETBITS, then two BYTEOUTs.
            var temp = _c + _a;
            _c |= 0xFFFF;
            if (_c >= temp)
                _c -= 0x8000;
            _c <<= _ct;
            ByteOut();
            _c <<= _ct;
            ByteOut();
            if (_b != 0xFF)
                Emit();
            return [.. _out.Skip(1)];
        }

        private void Renormalise()
        {
            do
            {
                _a <<= 1;
                _c <<= 1;
                if (--_ct == 0)
                    ByteOut();
            }
            while ((_a & 0x8000) == 0);
        }

        private void ByteOut()
        {
            if (_b == 0xFF)
            {
                Emit();
                _b = (int)(_c >> 20);
                _c &= 0xFFFFF;
                _ct = 7;
            }
            else if (_c < 0x8000000)
            {
                Emit();
                _b = (int)(_c >> 19);
                _c &= 0x7FFFF;
                _ct = 8;
            }
            else
            {
                _b++;
                if (_b == 0xFF)
                {
                    _c &= 0x7FFFFFF;
                    Emit();
                    _b = (int)(_c >> 20);
                    _c &= 0xFFFFF;
                    _ct = 7;
                }
                else
                {
                    Emit();
                    _b = (int)(_c >> 19);
                    _c &= 0x7FFFF;
                    _ct = 8;
                }
            }
        }

        // Writes the byte held in B. The first is the byte before the
        // buffer that INITENC points at, which Flush drops.
        private void Emit() => _out.Add((byte)_b);
    }
}
