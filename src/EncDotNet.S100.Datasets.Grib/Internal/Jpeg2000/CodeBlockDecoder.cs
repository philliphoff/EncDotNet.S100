using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

/// <summary>A subband's orientation (T.800 Annex B.5).</summary>
internal enum SubbandOrientation
{
    /// <summary>Low-pass in both directions.</summary>
    LL = 0,

    /// <summary>High-pass horizontally, low-pass vertically.</summary>
    HL = 1,

    /// <summary>Low-pass horizontally, high-pass vertically.</summary>
    LH = 2,

    /// <summary>High-pass in both directions.</summary>
    HH = 3,
}

/// <summary>
/// A code-block's position in its subband and the coded data the packets
/// have brought for it, split into codeword segments (T.800 B.10.7.2).
/// </summary>
internal sealed class CodeBlock
{
    public CodeBlock(int x0, int y0, int x1, int y1)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
    }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    /// <summary>Whether a packet has included the code-block yet.</summary>
    public bool Included { get; set; }

    /// <summary>The magnitude bit-planes coded, Mb less the zero bit-planes.</summary>
    public int BitPlanes { get; set; }

    /// <summary>The state variable that sizes the segment length fields (B.10.7.1).</summary>
    public int LBlock { get; set; } = 3;

    /// <summary>The coded data of every segment, one after another.</summary>
    public byte[] Data { get; private set; } = [];

    public int DataLength { get; private set; }

    public int SegmentCount { get; private set; }

    /// <summary>Per segment, the coding passes it holds so far.</summary>
    public int[] SegmentPasses { get; private set; } = new int[1];

    /// <summary>Per segment, its length in bytes so far.</summary>
    public int[] SegmentLengths { get; private set; } = new int[1];

    /// <summary>Per segment, the most passes it can hold before it's terminated.</summary>
    public int[] SegmentMaxPasses { get; private set; } = new int[1];

    public int TotalPasses { get; set; }

    /// <summary>Opens a new codeword segment.</summary>
    public void AddSegment(CodeBlockStyle style)
    {
        if (SegmentCount == SegmentPasses.Length)
        {
            var size = SegmentCount * 2;
            SegmentPasses = Grow(SegmentPasses, size);
            SegmentLengths = Grow(SegmentLengths, size);
            SegmentMaxPasses = Grow(SegmentMaxPasses, size);
        }

        // Where the encoder terminates (Table D.9): every pass with TERMALL;
        // with BYPASS, after the first ten passes and then after each raw
        // pair (significance propagation, refinement) and each cleanup pass.
        int max;
        if ((style & CodeBlockStyle.TerminateAll) != 0)
            max = 1;
        else if ((style & CodeBlockStyle.Bypass) != 0)
            max = SegmentCount == 0 ? 10 : SegmentMaxPasses[SegmentCount - 1] is 1 or 10 ? 2 : 1;
        else
            max = int.MaxValue;

        SegmentPasses[SegmentCount] = 0;
        SegmentLengths[SegmentCount] = 0;
        SegmentMaxPasses[SegmentCount] = max;
        SegmentCount++;
    }

    /// <summary>Records <paramref name="passes"/> more passes for the last segment.</summary>
    public void AddPasses(int passes)
    {
        SegmentPasses[SegmentCount - 1] += passes;
        TotalPasses += passes;
    }

    /// <summary>Appends a packet's bytes for segment <paramref name="segment"/>.</summary>
    public void AppendData(int segment, ReadOnlySpan<byte> bytes)
    {
        if (DataLength + bytes.Length > Data.Length)
        {
            var data = new byte[Math.Max(DataLength + bytes.Length, Data.Length * 2)];
            Data.AsSpan(0, DataLength).CopyTo(data);
            Data = data;
        }

        bytes.CopyTo(Data.AsSpan(DataLength));
        DataLength += bytes.Length;
        SegmentLengths[segment] += bytes.Length;
    }

    private static int[] Grow(int[] array, int size)
    {
        var grown = new int[size];
        array.CopyTo(grown, 0);
        return grown;
    }
}

/// <summary>
/// Tier-1 decoding (T.800 Annex D): the significance propagation, magnitude
/// refinement and cleanup passes over a code-block's bit-planes, with the
/// MQ decoder or, for bypassed passes, raw bits. Every code-block style
/// flag is honoured; predictable termination needs nothing from a decoder.
/// </summary>
/// <remarks>
/// <para>
/// Each sample's state is one <see cref="int"/> in a grid with a one-sample
/// border: the significance of its eight neighbours and the signs of its
/// four direct ones, kept up to date as samples become significant, plus
/// its own significance, sign, refinement and "coded in this bit-plane's
/// significance pass" bits. Contexts then come from lookup tables.
/// </para>
/// <para>
/// Magnitudes are kept doubled, with the midpoint of the interval the
/// decoded bits leave: a sample that becomes significant in bit-plane
/// <c>p</c> is <c>3 × 2^p</c>, and each refinement adds or takes away
/// <c>2^p</c>. Halving gives the exact value when every bit-plane was
/// decoded, and the midpoint reconstruction (r = ½) otherwise. That allows
/// 30 coded bit-planes in an <see cref="int"/>.
/// </para>
/// </remarks>
internal sealed class CodeBlockDecoder
{
    /// <summary>The most magnitude bit-planes a code-block can code here.</summary>
    public const int MaxBitPlanes = 30;

    private const int SigN = 1, SigS = 2, SigW = 4, SigE = 8, SigNW = 16, SigNE = 32, SigSW = 64, SigSE = 128;
    private const int NegN = 1 << 8, NegS = 1 << 9, NegW = 1 << 10, NegE = 1 << 11;
    private const int Significant = 1 << 12, Visited = 1 << 13, Refined = 1 << 14, Negative = 1 << 15;
    private const int Neighbours = 0xFF;

    // Zero coding contexts (Table D.1) by the eight neighbour bits, for
    // LL and LH, for HL, and for HH.
    private static readonly byte[][] ZeroCodingContexts = [BuildZeroCoding(0), BuildZeroCoding(1), BuildZeroCoding(2)];

    // Sign coding (Tables D.2, D.3) by the four direct neighbours'
    // significance (bits 0 to 3) and signs (bits 4 to 7): (context << 1) | xor.
    private static readonly byte[] SignCoding = BuildSignCoding();

    private readonly MqDecoder _mq = new();
    private readonly RawBitDecoder _raw = new();
    private int[] _flags = [];
    private int[] _magnitudes = [];

    /// <summary>
    /// Decodes a code-block into <paramref name="output"/>, row by row
    /// (<c>width × height</c>): signed doubled magnitudes, as described
    /// above, zero where nothing is significant.
    /// </summary>
    /// <exception cref="GribFormatException">The code-block has more passes than its bit-planes allow.</exception>
    public void Decode(CodeBlock block, SubbandOrientation orientation, CodeBlockStyle style, int[] output)
    {
        int w = block.X1 - block.X0, h = block.Y1 - block.Y0;
        var n = w * h;
        Array.Clear(output, 0, n);
        if (block.TotalPasses == 0 || block.BitPlanes == 0 || n == 0)
            return;

        if (block.TotalPasses > (3 * block.BitPlanes) - 2)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"A JPEG 2000 code-block has {block.TotalPasses} coding passes, more than its {block.BitPlanes} bit-planes allow."));
        }

        var fw = w + 2;
        var flagCount = fw * (h + 2);
        if (_flags.Length < flagCount)
            _flags = new int[flagCount];
        if (_magnitudes.Length < n)
            _magnitudes = new int[n];
        Array.Clear(_flags, 0, flagCount);
        Array.Clear(_magnitudes, 0, n);

        var zc = ZeroCodingContexts[orientation switch
        {
            SubbandOrientation.HL => 1,
            SubbandOrientation.HH => 2,
            _ => 0,
        }];
        var causal = (style & CodeBlockStyle.VerticallyCausal) != 0;
        var bypass = (style & CodeBlockStyle.Bypass) != 0;
        var reset = (style & CodeBlockStyle.Reset) != 0;
        var segmentationSymbols = (style & CodeBlockStyle.SegmentationSymbols) != 0;

        _mq.ResetContexts();
        var pass = 0;
        var type = 2;
        var plane = block.BitPlanes - 1;
        var offset = 0;
        for (var s = 0; s < block.SegmentCount; s++)
        {
            var raw = bypass && pass >= 10 && type != 2;
            if (raw)
                _raw.Start(block.Data, offset, block.SegmentLengths[s]);
            else
                _mq.Start(block.Data, offset, block.SegmentLengths[s]);
            offset += block.SegmentLengths[s];

            for (var k = 0; k < block.SegmentPasses[s]; k++)
            {
                switch (type)
                {
                    case 0 when raw:
                        SignificanceRaw(w, h, plane, causal);
                        break;
                    case 0:
                        Significance(w, h, plane, zc, causal);
                        break;
                    case 1 when raw:
                        RefinementRaw(w, h, plane);
                        break;
                    case 1:
                        Refinement(w, h, plane);
                        break;
                    default:
                        Cleanup(w, h, plane, zc, causal);
                        if (segmentationSymbols)
                        {
                            for (var i = 0; i < 4; i++)
                                _mq.Decode(MqDecoder.UniformContext);
                        }

                        break;
                }

                if (reset)
                    _mq.ResetContexts();

                pass++;
                if (++type == 3)
                {
                    type = 0;
                    plane--;
                }
            }
        }

        var flags = _flags;
        var magnitudes = _magnitudes;
        for (var y = 0; y < h; y++)
        {
            var f = ((y + 1) * fw) + 1;
            var m = y * w;
            for (var x = 0; x < w; x++, f++, m++)
                output[m] = (flags[f] & Negative) != 0 ? -magnitudes[m] : magnitudes[m];
        }
    }

    private void Significance(int w, int h, int plane, byte[] zc, bool causal)
    {
        var f = _flags;
        var magnitudes = _magnitudes;
        var fw = w + 2;
        var value = 3 << plane;
        for (var y0 = 0; y0 < h; y0 += 4)
        {
            var rows = Math.Min(4, h - y0);
            for (var x = 0; x < w; x++)
            {
                var i = ((y0 + 1) * fw) + x + 1;
                for (var k = 0; k < rows; k++, i += fw)
                {
                    var fl = f[i];
                    if ((fl & (Significant | Visited)) != 0 || (fl & Neighbours) == 0)
                        continue;

                    if (_mq.Decode(zc[fl & Neighbours]) != 0)
                    {
                        int sc = SignCoding[(fl & 0xF) | ((fl >> 4) & 0xF0)];
                        var negative = _mq.Decode(sc >> 1) ^ (sc & 1);
                        magnitudes[((y0 + k) * w) + x] = value;
                        SetSignificant(i, fw, negative != 0, causal && k == 0);
                    }

                    f[i] |= Visited;
                }
            }
        }
    }

    private void SignificanceRaw(int w, int h, int plane, bool causal)
    {
        var f = _flags;
        var fw = w + 2;
        var value = 3 << plane;
        for (var y0 = 0; y0 < h; y0 += 4)
        {
            var rows = Math.Min(4, h - y0);
            for (var x = 0; x < w; x++)
            {
                var i = ((y0 + 1) * fw) + x + 1;
                for (var k = 0; k < rows; k++, i += fw)
                {
                    var fl = f[i];
                    if ((fl & (Significant | Visited)) != 0 || (fl & Neighbours) == 0)
                        continue;

                    if (_raw.Decode() != 0)
                    {
                        var negative = _raw.Decode();
                        _magnitudes[((y0 + k) * w) + x] = value;
                        SetSignificant(i, fw, negative != 0, causal && k == 0);
                    }

                    f[i] |= Visited;
                }
            }
        }
    }

    private void Refinement(int w, int h, int plane)
    {
        var f = _flags;
        var magnitudes = _magnitudes;
        var fw = w + 2;
        var step = 1 << plane;
        for (var y0 = 0; y0 < h; y0 += 4)
        {
            var rows = Math.Min(4, h - y0);
            for (var x = 0; x < w; x++)
            {
                var i = ((y0 + 1) * fw) + x + 1;
                for (var k = 0; k < rows; k++, i += fw)
                {
                    var fl = f[i];
                    if ((fl & (Significant | Visited)) != Significant)
                        continue;

                    var cx = (fl & Refined) != 0 ? 16 : (fl & Neighbours) != 0 ? 15 : 14;
                    magnitudes[((y0 + k) * w) + x] += _mq.Decode(cx) != 0 ? step : -step;
                    f[i] = fl | Refined;
                }
            }
        }
    }

    private void RefinementRaw(int w, int h, int plane)
    {
        var f = _flags;
        var fw = w + 2;
        var step = 1 << plane;
        for (var y0 = 0; y0 < h; y0 += 4)
        {
            var rows = Math.Min(4, h - y0);
            for (var x = 0; x < w; x++)
            {
                var i = ((y0 + 1) * fw) + x + 1;
                for (var k = 0; k < rows; k++, i += fw)
                {
                    var fl = f[i];
                    if ((fl & (Significant | Visited)) != Significant)
                        continue;

                    _magnitudes[((y0 + k) * w) + x] += _raw.Decode() != 0 ? step : -step;
                    f[i] = fl | Refined;
                }
            }
        }
    }

    private void Cleanup(int w, int h, int plane, byte[] zc, bool causal)
    {
        var f = _flags;
        var magnitudes = _magnitudes;
        var fw = w + 2;
        var value = 3 << plane;
        for (var y0 = 0; y0 < h; y0 += 4)
        {
            var rows = Math.Min(4, h - y0);
            for (var x = 0; x < w; x++)
            {
                var top = ((y0 + 1) * fw) + x + 1;
                var k = 0;

                // Run-length mode: a full column of four samples, none
                // significant, coded or with a significant neighbour.
                if (rows == 4
                    && ((f[top] | f[top + fw] | f[top + (2 * fw)] | f[top + (3 * fw)]) & (Significant | Visited | Neighbours)) == 0)
                {
                    if (_mq.Decode(MqDecoder.RunLengthContext) == 0)
                        continue;

                    k = _mq.Decode(MqDecoder.UniformContext) << 1;
                    k |= _mq.Decode(MqDecoder.UniformContext);
                    var i = top + (k * fw);
                    var fl = f[i];
                    int sc = SignCoding[(fl & 0xF) | ((fl >> 4) & 0xF0)];
                    var negative = _mq.Decode(sc >> 1) ^ (sc & 1);
                    magnitudes[((y0 + k) * w) + x] = value;
                    SetSignificant(i, fw, negative != 0, causal && k == 0);
                    k++;
                }

                for (var i = top + (k * fw); k < rows; k++, i += fw)
                {
                    var fl = f[i];
                    if ((fl & (Significant | Visited)) == 0 && _mq.Decode(zc[fl & Neighbours]) != 0)
                    {
                        int sc = SignCoding[(fl & 0xF) | ((fl >> 4) & 0xF0)];
                        var negative = _mq.Decode(sc >> 1) ^ (sc & 1);
                        magnitudes[((y0 + k) * w) + x] = value;
                        SetSignificant(i, fw, negative != 0, causal && k == 0);
                    }

                    f[i] &= ~Visited;
                }
            }
        }
    }

    // Marks sample i significant and tells its neighbours. With vertically
    // causal contexts, a sample on a stripe's first row stays invisible to
    // the stripe above.
    private void SetSignificant(int i, int fw, bool negative, bool hideFromRowAbove)
    {
        var f = _flags;
        if (negative)
        {
            f[i] |= Significant | Negative;
            if (!hideFromRowAbove)
            {
                f[i - fw - 1] |= SigSE;
                f[i - fw] |= SigS | NegS;
                f[i - fw + 1] |= SigSW;
            }

            f[i - 1] |= SigE | NegE;
            f[i + 1] |= SigW | NegW;
            f[i + fw - 1] |= SigNE;
            f[i + fw] |= SigN | NegN;
            f[i + fw + 1] |= SigNW;
        }
        else
        {
            f[i] |= Significant;
            if (!hideFromRowAbove)
            {
                f[i - fw - 1] |= SigSE;
                f[i - fw] |= SigS;
                f[i - fw + 1] |= SigSW;
            }

            f[i - 1] |= SigE;
            f[i + 1] |= SigW;
            f[i + fw - 1] |= SigNE;
            f[i + fw] |= SigN;
            f[i + fw + 1] |= SigNW;
        }
    }

    private static byte[] BuildZeroCoding(int kind)
    {
        var table = new byte[256];
        for (var bits = 0; bits < 256; bits++)
        {
            var h = ((bits & SigW) != 0 ? 1 : 0) + ((bits & SigE) != 0 ? 1 : 0);
            var v = ((bits & SigN) != 0 ? 1 : 0) + ((bits & SigS) != 0 ? 1 : 0);
            var d = ((bits & SigNW) != 0 ? 1 : 0) + ((bits & SigNE) != 0 ? 1 : 0)
                + ((bits & SigSW) != 0 ? 1 : 0) + ((bits & SigSE) != 0 ? 1 : 0);
            int cx;
            if (kind == 2)
            {
                var hv = h + v;
                cx = d >= 3 ? 8
                    : d == 2 ? (hv >= 1 ? 7 : 6)
                    : d == 1 ? (hv >= 2 ? 5 : hv == 1 ? 4 : 3)
                    : (hv >= 2 ? 2 : hv == 1 ? 1 : 0);
            }
            else
            {
                // HL swaps the roles of the horizontal and vertical neighbours.
                if (kind == 1)
                    (h, v) = (v, h);
                cx = h == 2 ? 8
                    : h == 1 ? (v >= 1 ? 7 : d >= 1 ? 6 : 5)
                    : v == 2 ? 4
                    : v == 1 ? 3
                    : d >= 2 ? 2
                    : d;
            }

            table[bits] = (byte)cx;
        }

        return table;
    }

    private static byte[] BuildSignCoding()
    {
        var table = new byte[256];
        for (var bits = 0; bits < 256; bits++)
        {
            static int Contribution(int bits, int sig, int neg) => (bits & sig) == 0 ? 0 : (bits & neg) != 0 ? -1 : 1;

            // Bits 0 to 3: N, S, W, E significant; bits 4 to 7: their signs.
            var v = Math.Clamp(Contribution(bits, 1, 16) + Contribution(bits, 2, 32), -1, 1);
            var h = Math.Clamp(Contribution(bits, 4, 64) + Contribution(bits, 8, 128), -1, 1);
            int cx, xor;
            if (h == 0)
            {
                cx = v == 0 ? 9 : 10;
                xor = v < 0 ? 1 : 0;
            }
            else
            {
                cx = v == 0 ? 12 : v == h ? 13 : 11;
                xor = h < 0 ? 1 : 0;
            }

            table[bits] = (byte)((cx << 1) | xor);
        }

        return table;
    }
}
