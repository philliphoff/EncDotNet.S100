using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

/// <summary>A decoded single-component image.</summary>
/// <param name="Width">The component's width in samples.</param>
/// <param name="Height">The component's height in samples.</param>
/// <param name="Precision">The bit depth.</param>
/// <param name="Signed">Whether samples are signed.</param>
/// <param name="Samples">The samples, row by row.</param>
internal sealed record Jpeg2000Image(int Width, int Height, int Precision, bool Signed, int[] Samples);

/// <summary>
/// A JPEG 2000 Part 1 codestream decoder (ITU-T T.800 | ISO/IEC 15444-1)
/// for the single-component images GRIB2 template 5.40 holds. Written from
/// T.800; see <see cref="Codestream"/> for the markers it reads and rejects.
/// </summary>
/// <remarks>
/// <para>Supported:</para>
/// <list type="bullet">
/// <item><description>A raw codestream, or a JP2 file's codestream box.</description></item>
/// <item><description>One component of 1 to 31 bits, unsigned or signed, with any subsampling.</description></item>
/// <item><description>Image and tile offsets, any number of tiles and tile-parts.</description></item>
/// <item><description>COD and COC, QCD and QCC, in the main header and the first tile-part's.</description></item>
/// <item><description>All five progression orders, any number of quality layers, default and custom precincts, SOP and EPH markers.</description></item>
/// <item><description>Every code-block style: bypass, reset, terminate all, vertically causal, predictable termination, segmentation symbols.</description></item>
/// <item><description>The 5/3 reversible and 9/7 irreversible transforms with 0 to 32 levels; no, derived and expounded quantisation.</description></item>
/// </list>
/// <para>
/// The image is decoded at full resolution with every layer. Code-blocks
/// are limited to 30 coded magnitude bit-planes.
/// </para>
/// </remarks>
internal static class Jpeg2000Decoder
{
    /// <summary>Decodes a codestream.</summary>
    /// <exception cref="GribFormatException">The codestream is malformed.</exception>
    /// <exception cref="UnsupportedGribException">The codestream uses a feature this decoder doesn't support.</exception>
    public static Jpeg2000Image Decode(ReadOnlySpan<byte> data, CancellationToken cancellationToken = default)
    {
        var codestream = Codestream.Parse(data);
        int cx0 = CeilDiv(codestream.XOsiz, codestream.XRsiz), cx1 = CeilDiv(codestream.Xsiz, codestream.XRsiz);
        int cy0 = CeilDiv(codestream.YOsiz, codestream.YRsiz), cy1 = CeilDiv(codestream.Ysiz, codestream.YRsiz);
        long count = (long)(cx1 - cx0) * (cy1 - cy0);
        if (count > Array.MaxLength)
            throw new UnsupportedGribException("The JPEG 2000 image is too large to decode.");

        var samples = new int[count];
        var tileCount = codestream.TilesAcross * codestream.TilesDown;
        if (codestream.Tiles.Count != tileCount)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has tile-parts for {codestream.Tiles.Count} of its {tileCount} tiles."));
        }

        var tier1 = new CodeBlockDecoder();
        foreach (var (index, parts) in codestream.Tiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tile = new TileDecoder(codestream, index, parts, tier1);
            tile.Decode(cancellationToken);
            tile.CopyTo(samples, cx0, cy0, cx1 - cx0);
        }

        return new Jpeg2000Image(cx1 - cx0, cy1 - cy0, codestream.Precision, codestream.Signed, samples);
    }

    internal static int CeilDiv(int a, int b) => (int)(((long)a + b - 1) / b);

    // ⌈a / 2^k⌉ for any integer a, with k up to 62.
    internal static int CeilDivPow2(long a, int k) => (int)((a + (1L << k) - 1) >> k);
}

/// <summary>A subband of one resolution level, with its precincts' code-blocks.</summary>
internal sealed class Subband
{
    public required SubbandOrientation Orientation { get; init; }

    public required int X0 { get; init; }

    public required int Y0 { get; init; }

    public required int X1 { get; init; }

    public required int Y1 { get; init; }

    /// <summary>Where the subband starts in the tile-component buffer.</summary>
    public required int BufferX { get; init; }

    public required int BufferY { get; init; }

    /// <summary>Mb, the subband's magnitude bit-planes (E-2).</summary>
    public required int MagnitudeBits { get; init; }

    /// <summary>The quantisation step Δb (E-3), 1 without quantisation.</summary>
    public required float StepSize { get; init; }

    /// <summary>The precincts' code-blocks and tag trees, by precinct index.</summary>
    public required PrecinctBand[] Precincts { get; init; }
}

/// <summary>A precinct's code-blocks in one subband, in raster order, and its two tag trees.</summary>
internal sealed class PrecinctBand
{
    public PrecinctBand(CodeBlock[] blocks, int across, int down)
    {
        Blocks = blocks;
        if (blocks.Length > 0)
        {
            Inclusion = new TagTree(across, down);
            ZeroBitPlanes = new TagTree(across, down);
        }
    }

    public CodeBlock[] Blocks { get; }

    public TagTree? Inclusion { get; }

    public TagTree? ZeroBitPlanes { get; }
}

/// <summary>A resolution level: its bounds, precinct grid and subbands.</summary>
internal sealed class Resolution
{
    public required ResolutionBounds Bounds { get; init; }

    public required int PrecinctWidthExponent { get; init; }

    public required int PrecinctHeightExponent { get; init; }

    public required int PrecinctsAcross { get; init; }

    public required int PrecinctsDown { get; init; }

    public required Subband[] Bands { get; init; }
}

/// <summary>
/// Decodes one tile: builds its resolutions, subbands, precincts and
/// code-blocks (T.800 Annex B), reads its packets (Tier-2), decodes the
/// code-blocks (Tier-1), dequantises, inverts the wavelet transform and
/// shifts the DC level back.
/// </summary>
internal sealed class TileDecoder
{
    private readonly Codestream _codestream;
    private readonly int _index;
    private readonly TileParts _parts;
    private readonly CodeBlockDecoder _tier1;
    private readonly TileCodingStyle _style;
    private readonly ComponentCodingStyle _component;
    private readonly Quantization _quantization;
    private readonly int _tx0, _ty0, _tx1, _ty1;
    private readonly int _tcx0, _tcy0, _tcx1, _tcy1;
    private readonly Resolution[] _resolutions;
    private readonly List<(CodeBlock Block, int Segment, int Length)> _contributions = [];
    private int[]? _result;

    public TileDecoder(Codestream codestream, int index, TileParts parts, CodeBlockDecoder tier1)
    {
        _codestream = codestream;
        _index = index;
        _parts = parts;
        _tier1 = tier1;
        (_style, _component, _quantization) = codestream.Resolve(parts);

        // B-7 to B-12: the tile on the reference grid, then the tile-component.
        var p = index % codestream.TilesAcross;
        var q = index / codestream.TilesAcross;
        _tx0 = (int)Math.Max(codestream.XTOsiz + ((long)p * codestream.XTsiz), codestream.XOsiz);
        _ty0 = (int)Math.Max(codestream.YTOsiz + ((long)q * codestream.YTsiz), codestream.YOsiz);
        _tx1 = (int)Math.Min(codestream.XTOsiz + ((long)(p + 1) * codestream.XTsiz), codestream.Xsiz);
        _ty1 = (int)Math.Min(codestream.YTOsiz + ((long)(q + 1) * codestream.YTsiz), codestream.Ysiz);
        _tcx0 = Jpeg2000Decoder.CeilDiv(_tx0, codestream.XRsiz);
        _tcy0 = Jpeg2000Decoder.CeilDiv(_ty0, codestream.YRsiz);
        _tcx1 = Jpeg2000Decoder.CeilDiv(_tx1, codestream.XRsiz);
        _tcy1 = Jpeg2000Decoder.CeilDiv(_ty1, codestream.YRsiz);
        _resolutions = BuildResolutions();
    }

    private int Width => _tcx1 - _tcx0;

    private int Height => _tcy1 - _tcy0;

    public void Decode(CancellationToken cancellationToken)
    {
        var data = TileData(out var offset, out var length);
        ReadPackets(data, offset, offset + length, cancellationToken);
        _result = Reconstruct(cancellationToken);
    }

    /// <summary>Copies the decoded tile-component into the image's samples.</summary>
    public void CopyTo(int[] samples, int cx0, int cy0, int stride)
    {
        var result = _result ?? throw new InvalidOperationException("The tile hasn't been decoded.");
        for (var y = 0; y < Height; y++)
            result.AsSpan(y * Width, Width).CopyTo(samples.AsSpan(((_tcy0 - cy0 + y) * stride) + (_tcx0 - cx0)));
    }

    private Resolution[] BuildResolutions()
    {
        var levels = _component.Levels;
        var resolutions = new Resolution[levels + 1];
        int xcb = _component.CodeBlockWidthExponent, ycb = _component.CodeBlockHeightExponent;
        var guard = _quantization.GuardBits;
        var precision = _codestream.Precision;
        for (var r = 0; r <= levels; r++)
        {
            var shift = levels - r;
            var bounds = new ResolutionBounds(
                Jpeg2000Decoder.CeilDivPow2(_tcx0, shift),
                Jpeg2000Decoder.CeilDivPow2(_tcy0, shift),
                Jpeg2000Decoder.CeilDivPow2(_tcx1, shift),
                Jpeg2000Decoder.CeilDivPow2(_tcy1, shift));
            int ppx = _component.PrecinctWidthExponent(r), ppy = _component.PrecinctHeightExponent(r);
            var across = bounds.Width > 0 ? Jpeg2000Decoder.CeilDivPow2(bounds.X1, ppx) - (bounds.X0 >> ppx) : 0;
            var down = bounds.Height > 0 ? Jpeg2000Decoder.CeilDivPow2(bounds.Y1, ppy) - (bounds.Y0 >> ppy) : 0;
            if ((long)across * down > 1 << 24)
                throw new UnsupportedGribException("The JPEG 2000 codestream has too many precincts to decode.");

            // Code-blocks are no larger than the precinct's share of the subband (B-17).
            var bandPpx = r == 0 ? ppx : ppx - 1;
            var bandPpy = r == 0 ? ppy : ppy - 1;
            var cbw = Math.Min(xcb, bandPpx);
            var cbh = Math.Min(ycb, bandPpy);

            SubbandOrientation[] orientations = r == 0 ? [SubbandOrientation.LL] : [SubbandOrientation.HL, SubbandOrientation.LH, SubbandOrientation.HH];
            var bands = new Subband[orientations.Length];
            var lowWidth = r == 0 ? 0 : resolutions[r - 1].Bounds.Width;
            var lowHeight = r == 0 ? 0 : resolutions[r - 1].Bounds.Height;
            for (var b = 0; b < orientations.Length; b++)
            {
                var orientation = orientations[b];
                var nb = r == 0 ? levels : levels - r + 1;
                int xob = orientation is SubbandOrientation.HL or SubbandOrientation.HH ? 1 : 0;
                int yob = orientation is SubbandOrientation.LH or SubbandOrientation.HH ? 1 : 0;

                // B-15: the subband's bounds.
                long xOffset = nb == 0 ? 0 : (long)xob << (nb - 1);
                long yOffset = nb == 0 ? 0 : (long)yob << (nb - 1);
                var x0 = Jpeg2000Decoder.CeilDivPow2(_tcx0 - xOffset, nb);
                var y0 = Jpeg2000Decoder.CeilDivPow2(_tcy0 - yOffset, nb);
                var x1 = Jpeg2000Decoder.CeilDivPow2(_tcx1 - xOffset, nb);
                var y1 = Jpeg2000Decoder.CeilDivPow2(_tcy1 - yOffset, nb);

                // Annex E: Mb and the step size.
                var bandIndex = r == 0 ? 0 : (3 * (r - 1)) + 1 + b;
                var (exponent, mantissa) = _quantization.ForBand(bandIndex, levels, nb);
                var gain = orientation switch
                {
                    SubbandOrientation.LL => 0,
                    SubbandOrientation.HH => 2,
                    _ => 1,
                };
                var magnitudeBits = guard + exponent - 1;
                var step = _quantization.Style == 0
                    ? 1f
                    : (float)(Math.Pow(2, precision + gain - exponent) * (1 + (mantissa / 2048.0)));

                var precincts = new PrecinctBand[across * down];
                for (var py = 0; py < down; py++)
                {
                    for (var px = 0; px < across; px++)
                    {
                        // The precinct's share of the subband, clipped to it.
                        var prx0 = (long)((bounds.X0 >> ppx) + px) << bandPpx;
                        var pry0 = (long)((bounds.Y0 >> ppy) + py) << bandPpy;
                        var cx0 = (int)Math.Max(prx0, x0);
                        var cy0 = (int)Math.Max(pry0, y0);
                        var cx1 = (int)Math.Min(prx0 + (1L << bandPpx), x1);
                        var cy1 = (int)Math.Min(pry0 + (1L << bandPpy), y1);
                        precincts[(py * across) + px] = BuildPrecinct(cx0, cy0, cx1, cy1, cbw, cbh);
                    }
                }

                bands[b] = new Subband
                {
                    Orientation = orientation,
                    X0 = x0,
                    Y0 = y0,
                    X1 = x1,
                    Y1 = y1,
                    BufferX = xob == 1 ? lowWidth : 0,
                    BufferY = yob == 1 ? lowHeight : 0,
                    MagnitudeBits = magnitudeBits,
                    StepSize = step,
                    Precincts = precincts,
                };
            }

            resolutions[r] = new Resolution
            {
                Bounds = bounds,
                PrecinctWidthExponent = ppx,
                PrecinctHeightExponent = ppy,
                PrecinctsAcross = across,
                PrecinctsDown = down,
                Bands = bands,
            };
        }

        return resolutions;
    }

    private static PrecinctBand BuildPrecinct(int x0, int y0, int x1, int y1, int cbw, int cbh)
    {
        if (x1 <= x0 || y1 <= y0)
            return new PrecinctBand([], 0, 0);

        var first = x0 >> cbw;
        var top = y0 >> cbh;
        var across = Jpeg2000Decoder.CeilDivPow2(x1, cbw) - first;
        var down = Jpeg2000Decoder.CeilDivPow2(y1, cbh) - top;
        var blocks = new CodeBlock[across * down];
        for (var j = 0; j < down; j++)
        {
            for (var i = 0; i < across; i++)
            {
                blocks[(j * across) + i] = new CodeBlock(
                    Math.Max((first + i) << cbw, x0),
                    Math.Max((top + j) << cbh, y0),
                    Math.Min((first + i + 1) << cbw, x1),
                    Math.Min((top + j + 1) << cbh, y1));
            }
        }

        return new PrecinctBand(blocks, across, down);
    }

    private byte[] TileData(out int offset, out int length)
    {
        var parts = _parts.Parts;
        if (parts.Count == 1)
        {
            offset = parts[0].Offset;
            length = parts[0].Length;
            return _codestream.Data;
        }

        // Tile-parts may be interleaved with other tiles'; their bodies, in
        // order, make the tile's packet stream.
        var ordered = parts.OrderBy(t => t.Index).ToList();
        var data = new byte[ordered.Sum(t => (long)t.Length)];
        var at = 0;
        foreach (var part in ordered)
        {
            _codestream.Data.AsSpan(part.Offset, part.Length).CopyTo(data.AsSpan(at));
            at += part.Length;
        }

        offset = 0;
        length = data.Length;
        return data;
    }

    // Tier-2 (B.12): the packets in the tile's progression order.
    private void ReadPackets(byte[] data, int position, int end, CancellationToken cancellationToken)
    {
        var layers = _style.Layers;
        var levels = _component.Levels;
        switch (_style.Progression)
        {
            case ProgressionOrder.Lrcp:
                for (var l = 0; l < layers; l++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var r = 0; r <= levels; r++)
                    {
                        for (var k = 0; k < PrecinctCount(r); k++)
                            position = ReadPacket(data, position, end, l, r, k);
                    }
                }

                break;

            case ProgressionOrder.Rlcp:
                for (var r = 0; r <= levels; r++)
                {
                    for (var l = 0; l < layers; l++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (var k = 0; k < PrecinctCount(r); k++)
                            position = ReadPacket(data, position, end, l, r, k);
                    }
                }

                break;

            case ProgressionOrder.Rpcl:
                // With one component, position order within a resolution is
                // the precincts' raster order.
                for (var r = 0; r <= levels; r++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var k = 0; k < PrecinctCount(r); k++)
                    {
                        for (var l = 0; l < layers; l++)
                            position = ReadPacket(data, position, end, l, r, k);
                    }
                }

                break;

            default:
                // PCRL, and CPRL, which is the same with one component.
                position = ReadPositionFirst(data, position, end, cancellationToken);
                break;
        }
    }

    private int PrecinctCount(int r) => _resolutions[r].PrecinctsAcross * _resolutions[r].PrecinctsDown;

    // B.12.1.4: positions on the reference grid, top to bottom and left to
    // right, then resolutions, then layers.
    private int ReadPositionFirst(byte[] data, int position, int end, CancellationToken cancellationToken)
    {
        var levels = _component.Levels;
        long xr = _codestream.XRsiz, yr = _codestream.YRsiz;
        long xStep = long.MaxValue, yStep = long.MaxValue;
        for (var r = 0; r <= levels; r++)
        {
            var res = _resolutions[r];
            xStep = Math.Min(xStep, xr << (res.PrecinctWidthExponent + levels - r));
            yStep = Math.Min(yStep, yr << (res.PrecinctHeightExponent + levels - r));
        }

        for (long y = _ty0; y < _ty1; y += yStep - (y % yStep))
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (long x = _tx0; x < _tx1; x += xStep - (x % xStep))
            {
                for (var r = 0; r <= levels; r++)
                {
                    var res = _resolutions[r];
                    if (res.PrecinctsAcross == 0 || res.PrecinctsDown == 0)
                        continue;

                    var shift = levels - r;
                    int ppx = res.PrecinctWidthExponent, ppy = res.PrecinctHeightExponent;
                    var b = res.Bounds;
                    var yHere = y % (yr << (ppy + shift)) == 0
                        || (y == _ty0 && (((long)b.Y0 << shift) % (1L << (ppy + shift))) != 0);
                    var xHere = x % (xr << (ppx + shift)) == 0
                        || (x == _tx0 && (((long)b.X0 << shift) % (1L << (ppx + shift))) != 0);
                    if (!yHere || !xHere)
                        continue;

                    var i = (Jpeg2000Decoder.CeilDivPow2(Jpeg2000Decoder.CeilDiv((int)x, (int)xr), shift) >> ppx) - (b.X0 >> ppx);
                    var j = (Jpeg2000Decoder.CeilDivPow2(Jpeg2000Decoder.CeilDiv((int)y, (int)yr), shift) >> ppy) - (b.Y0 >> ppy);
                    var k = i + (j * res.PrecinctsAcross);
                    for (var l = 0; l < _style.Layers; l++)
                        position = ReadPacket(data, position, end, l, r, k);
                }
            }
        }

        return position;
    }

    // One packet (B.9, B.10): its header, then its code-blocks' data.
    private int ReadPacket(byte[] data, int position, int end, int layer, int r, int k)
    {
        if (position >= end)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 tile {_index} data ends before packet (layer {layer}, resolution {r}, precinct {k})."));
        }

        var span = data.AsSpan(0, end);
        if (_style.StartOfPacketMarkers && position + 6 <= end && data[position] == 0xFF && data[position + 1] == 0x91)
            position += 6;

        var res = _resolutions[r];
        var reader = new PacketHeaderReader(span, position);
        var style = _component.Style;
        var contributions = _contributions;
        contributions.Clear();
        if (reader.ReadBit() == 1)
        {
            foreach (var band in res.Bands)
            {
                var precinct = band.Precincts[k];
                for (var n = 0; n < precinct.Blocks.Length; n++)
                {
                    var block = precinct.Blocks[n];
                    bool included;
                    if (!block.Included)
                        included = precinct.Inclusion!.Decode(ref reader, n, layer + 1);
                    else
                        included = reader.ReadBit() == 1;
                    if (!included)
                        continue;

                    if (!block.Included)
                    {
                        var zero = precinct.ZeroBitPlanes!.DecodeValue(ref reader, n);
                        block.BitPlanes = band.MagnitudeBits - zero;
                        if (block.BitPlanes < 0)
                        {
                            throw new GribFormatException(
                                string.Create(CultureInfo.InvariantCulture, $"A JPEG 2000 code-block has {zero} zero bit-planes, more than its subband's {band.MagnitudeBits}."));
                        }

                        if (block.BitPlanes > CodeBlockDecoder.MaxBitPlanes)
                        {
                            throw new UnsupportedGribException(
                                string.Create(CultureInfo.InvariantCulture, $"A JPEG 2000 code-block codes {block.BitPlanes} magnitude bit-planes; at most {CodeBlockDecoder.MaxBitPlanes} are supported."));
                        }

                        block.Included = true;
                    }

                    var passes = ReadPassCount(ref reader);
                    while (reader.ReadBit() == 1)
                        block.LBlock++;

                    // Split the new passes at the segment boundaries; each
                    // piece's length takes LBlock + ⌊log2(passes)⌋ bits.
                    while (passes > 0)
                    {
                        if (block.SegmentCount == 0 || block.SegmentPasses[block.SegmentCount - 1] == block.SegmentMaxPasses[block.SegmentCount - 1])
                            block.AddSegment(style);
                        var segment = block.SegmentCount - 1;
                        var take = Math.Min(passes, block.SegmentMaxPasses[segment] - block.SegmentPasses[segment]);
                        var bits = block.LBlock + Log2(take);
                        if (bits > 31)
                            throw new GribFormatException("A JPEG 2000 packet header has a codeword segment length of more than 31 bits.");
                        contributions.Add((block, segment, reader.Read(bits)));
                        block.AddPasses(take);
                        passes -= take;
                    }
                }
            }
        }

        reader.Align();
        position = reader.Position;
        if (_style.EndOfPacketHeaderMarkers && position + 2 <= end && data[position] == 0xFF && data[position + 1] == 0x92)
            position += 2;

        foreach (var (block, segment, length) in contributions)
        {
            if (position + (long)length > end)
            {
                throw new GribFormatException(
                    string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 tile {_index} data ends inside a packet (layer {layer}, resolution {r}, precinct {k})."));
            }

            block.AppendData(segment, data.AsSpan(position, length));
            position += length;
        }

        return position;
    }

    // Table B.4: the number of coding passes.
    private static int ReadPassCount(ref PacketHeaderReader reader)
    {
        if (reader.ReadBit() == 0)
            return 1;
        if (reader.ReadBit() == 0)
            return 2;
        var v = reader.Read(2);
        if (v < 3)
            return 3 + v;
        v = reader.Read(5);
        if (v < 31)
            return 6 + v;
        return 37 + reader.Read(7);
    }

    private static int Log2(int v)
    {
        var log = 0;
        while ((v >>= 1) != 0)
            log++;
        return log;
    }

    // Tier-1, dequantisation, the inverse transform and the DC level shift.
    private int[] Reconstruct(CancellationToken cancellationToken)
    {
        int w = Width, h = Height;
        var count = (long)w * h;
        if (count > Array.MaxLength)
            throw new UnsupportedGribException("A JPEG 2000 tile is too large to decode.");

        var reversible = _component.Reversible;
        var ints = reversible ? new int[count] : null;
        var floats = reversible ? null : new float[count];
        var scratch = new int[1 << (_component.CodeBlockWidthExponent + _component.CodeBlockHeightExponent)];
        var style = _component.Style;
        var decoded = 0;
        foreach (var res in _resolutions)
        {
            foreach (var band in res.Bands)
            {
                var halfStep = band.StepSize / 2;
                foreach (var precinct in band.Precincts)
                {
                    foreach (var block in precinct.Blocks)
                    {
                        if ((++decoded & 0xFF) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        if (block.TotalPasses == 0)
                            continue;

                        _tier1.Decode(block, band.Orientation, style, scratch);
                        int bw = block.X1 - block.X0, bh = block.Y1 - block.Y0;
                        var origin = ((band.BufferY + block.Y0 - band.Y0) * w) + band.BufferX + block.X0 - band.X0;
                        for (var y = 0; y < bh; y++)
                        {
                            var at = origin + (y * w);
                            var from = y * bw;
                            if (ints is not null)
                            {
                                // Halve the doubled magnitudes, towards zero.
                                for (var x = 0; x < bw; x++)
                                {
                                    var v = scratch[from + x];
                                    ints[at + x] = v >= 0 ? v >> 1 : -(-v >> 1);
                                }
                            }
                            else
                            {
                                for (var x = 0; x < bw; x++)
                                    floats![at + x] = scratch[from + x] * halfStep;
                            }
                        }
                    }
                }
            }
        }

        var bounds = _resolutions.Select(r => r.Bounds).ToArray();
        var precision = _codestream.Precision;
        var signed = _codestream.Signed;
        var shift = signed ? 0 : 1 << (precision - 1);
        var min = signed ? -(1 << (precision - 1)) : 0;
        var max = signed ? (1 << (precision - 1)) - 1 : (int)((1L << precision) - 1);
        if (ints is not null)
        {
            InverseWavelet.Reversible(ints, w, bounds);
            for (var i = 0; i < ints.Length; i++)
                ints[i] = Math.Clamp(ints[i] + shift, min, max);
            return ints;
        }

        InverseWavelet.Irreversible(floats!, w, bounds);
        var samples = new int[count];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = Math.Round((double)floats![i] + shift);
            samples[i] = (int)Math.Clamp(v, min, max);
        }

        return samples;
    }
}
