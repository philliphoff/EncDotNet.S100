using System.Buffers.Binary;
using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

/// <summary>The progression orders of COD's SGcod (T.800 Table A.16).</summary>
internal enum ProgressionOrder
{
    /// <summary>Layer, resolution, component, position.</summary>
    Lrcp = 0,

    /// <summary>Resolution, layer, component, position.</summary>
    Rlcp = 1,

    /// <summary>Resolution, position, component, layer.</summary>
    Rpcl = 2,

    /// <summary>Position, component, resolution, layer.</summary>
    Pcrl = 3,

    /// <summary>Component, position, resolution, layer.</summary>
    Cprl = 4,
}

/// <summary>The code-block style flags of SPcod (T.800 Table A.19).</summary>
[Flags]
internal enum CodeBlockStyle
{
    None = 0,

    /// <summary>Selective arithmetic coding bypass.</summary>
    Bypass = 0x01,

    /// <summary>Reset the context probabilities at each coding pass boundary.</summary>
    Reset = 0x02,

    /// <summary>Terminate each coding pass.</summary>
    TerminateAll = 0x04,

    /// <summary>Vertically stripe-causal contexts.</summary>
    VerticallyCausal = 0x08,

    /// <summary>Predictable termination.</summary>
    PredictableTermination = 0x10,

    /// <summary>Segmentation symbols at the end of each cleanup pass.</summary>
    SegmentationSymbols = 0x20,
}

/// <summary>The parts of COD that apply to the whole tile (Scod and SGcod).</summary>
internal sealed record TileCodingStyle(ProgressionOrder Progression, int Layers, bool StartOfPacketMarkers, bool EndOfPacketHeaderMarkers);

/// <summary>The parts of COD or COC that apply to one component (SPcod or SPcoc).</summary>
/// <param name="Levels">The number of decomposition levels, N_L.</param>
/// <param name="CodeBlockWidthExponent">xcb, the log2 of the nominal code-block width.</param>
/// <param name="CodeBlockHeightExponent">ycb, the log2 of the nominal code-block height.</param>
/// <param name="Style">The code-block style flags.</param>
/// <param name="Reversible">True for the 5/3 reversible transform, false for the 9/7 irreversible one.</param>
/// <param name="PrecinctExponents">Per resolution, PPx in the low and PPy in the high nibble; null for the default 2^15.</param>
internal sealed record ComponentCodingStyle(int Levels, int CodeBlockWidthExponent, int CodeBlockHeightExponent, CodeBlockStyle Style, bool Reversible, byte[]? PrecinctExponents)
{
    public int PrecinctWidthExponent(int resolution) => PrecinctExponents is null ? 15 : PrecinctExponents[resolution] & 0x0F;

    public int PrecinctHeightExponent(int resolution) => PrecinctExponents is null ? 15 : PrecinctExponents[resolution] >> 4;
}

/// <summary>A QCD or QCC marker segment (T.800 A.6.4, A.6.5).</summary>
/// <param name="Style">0 no quantisation, 1 scalar derived, 2 scalar expounded.</param>
/// <param name="GuardBits">The number of guard bits, G.</param>
/// <param name="Exponents">The exponents ε, one per subband (one in all for derived).</param>
/// <param name="Mantissas">The mantissas μ, matching <paramref name="Exponents"/> (zero without quantisation).</param>
internal sealed record Quantization(int Style, int GuardBits, int[] Exponents, int[] Mantissas)
{
    /// <summary>The exponent and mantissa of subband <paramref name="band"/> (0 is LL, then HL, LH, HH from the lowest level).</summary>
    public (int Exponent, int Mantissa) ForBand(int band, int levels, int decompositionLevel)
    {
        if (Style == 1)
            return (Exponents[0] - levels + decompositionLevel, Mantissas[0]);

        if (band >= Exponents.Length)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 quantisation marker gives {Exponents.Length} subbands, fewer than the {(3 * levels) + 1} the decomposition has."));
        }

        return (Exponents[band], Mantissas[band]);
    }
}

/// <summary>The coding settings a main or tile-part header gives.</summary>
internal sealed class CodingSettings
{
    public TileCodingStyle? Tile { get; set; }

    public ComponentCodingStyle? FromCod { get; set; }

    public ComponentCodingStyle? FromCoc { get; set; }

    public Quantization? FromQcd { get; set; }

    public Quantization? FromQcc { get; set; }
}

/// <summary>One tile's tile-parts, in order.</summary>
internal sealed class TileParts
{
    public CodingSettings Settings { get; } = new();

    public List<(int Index, int Offset, int Length)> Parts { get; } = [];
}

/// <summary>
/// The main header and tile-parts of a JPEG 2000 Part 1 codestream
/// (ITU-T T.800 | ISO/IEC 15444-1 Annex A), limited to one component.
/// </summary>
/// <remarks>
/// TLM, PLM, PLT, CRG and COM are skipped: the packets are read in order
/// and need no index. Features GRIB2 doesn't use fail with
/// <see cref="UnsupportedGribException"/> naming them: more than one
/// component, regions of interest (RGN), progression order changes (POC),
/// packed packet headers (PPM, PPT), Part 2 extensions and HTJ2K (Part 15).
/// </remarks>
internal sealed class Codestream
{
    private const ushort Soc = 0xFF4F, Cap = 0xFF50, Siz = 0xFF51, Cod = 0xFF52, Coc = 0xFF53, Tlm = 0xFF55, Prf = 0xFF56,
        Plm = 0xFF57, Plt = 0xFF58, Cpf = 0xFF59, Qcd = 0xFF5C, Qcc = 0xFF5D, Rgn = 0xFF5E, Poc = 0xFF5F, Ppm = 0xFF60,
        Ppt = 0xFF61, Crg = 0xFF63, Com = 0xFF64, Sot = 0xFF90, Sod = 0xFF93, Eoc = 0xFFD9;

    private Codestream(byte[] data)
    {
        Data = data;
    }

    /// <summary>The codestream's bytes.</summary>
    public byte[] Data { get; }

    public int Xsiz { get; private set; }

    public int Ysiz { get; private set; }

    public int XOsiz { get; private set; }

    public int YOsiz { get; private set; }

    public int XTsiz { get; private set; }

    public int YTsiz { get; private set; }

    public int XTOsiz { get; private set; }

    public int YTOsiz { get; private set; }

    /// <summary>The component's bit depth, 1 to 31.</summary>
    public int Precision { get; private set; }

    /// <summary>Whether the component's samples are signed.</summary>
    public bool Signed { get; private set; }

    /// <summary>The component's horizontal subsampling, XRsiz.</summary>
    public int XRsiz { get; private set; }

    /// <summary>The component's vertical subsampling, YRsiz.</summary>
    public int YRsiz { get; private set; }

    public int TilesAcross { get; private set; }

    public int TilesDown { get; private set; }

    public CodingSettings Main { get; } = new();

    /// <summary>The tiles that have at least one tile-part, by tile index.</summary>
    public SortedDictionary<int, TileParts> Tiles { get; } = [];

    /// <summary>Parses a codestream, or the contiguous codestream box of a JP2 file.</summary>
    /// <exception cref="GribFormatException">The codestream is malformed.</exception>
    /// <exception cref="UnsupportedGribException">The codestream uses a feature this decoder doesn't support.</exception>
    public static Codestream Parse(ReadOnlySpan<byte> data)
    {
        data = Jp2Container.FindCodestream(data);
        var codestream = new Codestream(data.ToArray());
        codestream.ParseMarkers();
        return codestream;
    }

    /// <summary>The effective coding settings of a tile: tile-part values override main header ones, COC overrides COD.</summary>
    public (TileCodingStyle Tile, ComponentCodingStyle Component, Quantization Quantization) Resolve(TileParts tile)
    {
        var t = tile.Settings;
        var tileStyle = t.Tile ?? Main.Tile ?? throw new GribFormatException("The JPEG 2000 codestream has no COD marker segment.");
        var component = t.FromCoc ?? t.FromCod ?? Main.FromCoc ?? Main.FromCod
            ?? throw new GribFormatException("The JPEG 2000 codestream has no COD marker segment.");
        var quantization = t.FromQcc ?? t.FromQcd ?? Main.FromQcc ?? Main.FromQcd
            ?? throw new GribFormatException("The JPEG 2000 codestream has no QCD marker segment.");
        return (tileStyle, component, quantization);
    }

    private void ParseMarkers()
    {
        var d = Data;
        if (d.Length < 4 || ReadUInt16(0) != Soc)
            throw new GribFormatException("The data isn't a JPEG 2000 codestream (the SOC marker is missing).");
        if (ReadUInt16(2) != Siz)
            throw new GribFormatException("The JPEG 2000 codestream's SIZ marker segment doesn't follow SOC.");

        var position = 2;
        var sawSiz = false;
        while (true)
        {
            var marker = ReadSegment(position, out var body, out var next);
            if (marker == Sot)
                break;

            switch (marker)
            {
                case Siz:
                    ParseSiz(body);
                    sawSiz = true;
                    break;
                case Cap:
                case Cpf:
                    throw new UnsupportedGribException("The JPEG 2000 codestream is HTJ2K (ITU-T T.814 | ISO/IEC 15444-15, a CAP or CPF marker), which isn't supported; only JPEG 2000 Part 1 is.");
                default:
                    ParseCommon(marker, body, Main, mainHeader: true);
                    break;
            }

            position = next;
        }

        if (!sawSiz)
            throw new GribFormatException("The JPEG 2000 codestream has no SIZ marker segment.");

        // Tile-parts, until EOC or the end of the data.
        while (position + 2 <= d.Length)
        {
            var marker = ReadUInt16(position);
            if (marker == Eoc)
                return;
            if (marker != Sot)
            {
                throw new GribFormatException(
                    string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has marker 0x{marker:X4} at byte {position} where a tile-part (SOT) or EOC should start."));
            }

            position = ParseTilePart(position);
        }
    }

    private int ParseTilePart(int sotPosition)
    {
        ReadSegment(sotPosition, out var sot, out var position);
        if (sot.Length < 8)
            throw new GribFormatException("The JPEG 2000 SOT marker segment is too short.");
        int index = BinaryPrimitives.ReadUInt16BigEndian(sot);
        long length = BinaryPrimitives.ReadUInt32BigEndian(sot[2..]);
        int partIndex = sot[6];
        if (index >= TilesAcross * TilesDown)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"A JPEG 2000 tile-part names tile {index}, but the image has {TilesAcross * TilesDown} tiles."));
        }

        if (!Tiles.TryGetValue(index, out var tile))
        {
            tile = new TileParts();
            Tiles.Add(index, tile);
        }

        while (true)
        {
            if (position + 2 > Data.Length)
                throw new GribFormatException("The JPEG 2000 codestream ends inside a tile-part header.");
            if (ReadUInt16(position) == Sod)
            {
                position += 2;
                break;
            }

            var marker = ReadSegment(position, out var body, out var next);
            ParseCommon(marker, body, tile.Settings, mainHeader: false);
            position = next;
        }

        long end;
        if (length == 0)
        {
            // The last tile-part: it runs to EOC.
            end = Data.Length >= 2 && ReadUInt16(Data.Length - 2) == Eoc ? Data.Length - 2 : Data.Length;
        }
        else
        {
            end = sotPosition + length;
            if (end > Data.Length)
                throw new GribFormatException("A JPEG 2000 tile-part runs past the end of the codestream.");
        }

        if (end < position)
            throw new GribFormatException("A JPEG 2000 tile-part is shorter than its header.");

        tile.Parts.Add((partIndex, position, (int)(end - position)));
        return (int)end;
    }

    private void ParseCommon(ushort marker, ReadOnlySpan<byte> body, CodingSettings settings, bool mainHeader)
    {
        switch (marker)
        {
            case Cod:
                ParseCod(body, settings);
                break;
            case Coc:
                if (body.Length < 2)
                    throw new GribFormatException("The JPEG 2000 COC marker segment is too short.");
                if (body[0] != 0)
                    throw new GribFormatException("The JPEG 2000 COC marker segment names a component the image doesn't have.");
                settings.FromCoc = ParseComponentStyle(body[2..], explicitPrecincts: (body[1] & 1) != 0, "COC");
                break;
            case Qcd:
                settings.FromQcd = ParseQuantization(body, "QCD");
                break;
            case Qcc:
                if (body.Length < 1 || body[0] != 0)
                    throw new GribFormatException("The JPEG 2000 QCC marker segment names a component the image doesn't have.");
                settings.FromQcc = ParseQuantization(body[1..], "QCC");
                break;
            case Rgn:
                throw new UnsupportedGribException("The JPEG 2000 codestream has a region of interest (RGN marker), which isn't supported.");
            case Poc:
                throw new UnsupportedGribException("The JPEG 2000 codestream has progression order changes (POC marker), which aren't supported.");
            case Ppm:
            case Ppt:
                throw new UnsupportedGribException(
                    string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has packed packet headers ({(marker == Ppm ? "PPM" : "PPT")} marker), which aren't supported."));
            case Tlm:
            case Plm:
            case Plt:
            case Crg:
            case Com:
            case Prf:
                // Indexes, registration and comments: not needed to decode.
                break;
            case Siz:
                throw new GribFormatException("The JPEG 2000 codestream has a second SIZ marker segment.");
            default:
                if (!mainHeader || marker < 0xFF30)
                {
                    throw new GribFormatException(
                        string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has unexpected marker 0x{marker:X4} in a {(mainHeader ? "main" : "tile-part")} header."));
                }

                // An unknown informational marker segment in the main header: skip it.
                break;
        }
    }

    private void ParseSiz(ReadOnlySpan<byte> s)
    {
        if (s.Length < 36)
            throw new GribFormatException("The JPEG 2000 SIZ marker segment is too short.");
        int rsiz = BinaryPrimitives.ReadUInt16BigEndian(s);
        if ((rsiz & 0x4000) != 0)
            throw new UnsupportedGribException("The JPEG 2000 codestream is HTJ2K (ITU-T T.814 | ISO/IEC 15444-15, Rsiz bit 14), which isn't supported; only JPEG 2000 Part 1 is.");
        if ((rsiz & 0x8000) != 0)
        {
            throw new UnsupportedGribException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream uses JPEG 2000 Part 2 extensions (Rsiz 0x{rsiz:X4}), which aren't supported; only Part 1 is."));
        }

        var xsiz = ReadSize(s, 2);
        var ysiz = ReadSize(s, 6);
        var xosiz = ReadSize(s, 10);
        var yosiz = ReadSize(s, 14);
        var xtsiz = ReadSize(s, 18);
        var ytsiz = ReadSize(s, 22);
        var xtosiz = ReadSize(s, 26);
        var ytosiz = ReadSize(s, 30);
        int components = BinaryPrimitives.ReadUInt16BigEndian(s[34..]);
        if (components != 1)
        {
            throw new UnsupportedGribException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has {components} components, which isn't supported; only single-component (greyscale) images are, as GRIB2 uses."));
        }

        if (s.Length < 39)
            throw new GribFormatException("The JPEG 2000 SIZ marker segment is too short for its component.");
        if (xosiz >= xsiz || yosiz >= ysiz || xtsiz == 0 || ytsiz == 0 || xtosiz > xosiz || ytosiz > yosiz
            || (long)xtosiz + xtsiz <= xosiz || (long)ytosiz + ytsiz <= yosiz)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 SIZ marker segment has an inconsistent image or tile grid ({xsiz} × {ysiz}, offset {xosiz}, {yosiz}; tiles {xtsiz} × {ytsiz}, offset {xtosiz}, {ytosiz})."));
        }

        var ssiz = s[36];
        var precision = (ssiz & 0x7F) + 1;
        if (precision > 31)
        {
            throw new UnsupportedGribException(
                string.Create(CultureInfo.InvariantCulture, $"JPEG 2000 samples of {precision} bits aren't supported; at most 31 are."));
        }

        if (s[37] == 0 || s[38] == 0)
            throw new GribFormatException("The JPEG 2000 SIZ marker segment has a component subsampling of 0.");

        Xsiz = xsiz;
        Ysiz = ysiz;
        XOsiz = xosiz;
        YOsiz = yosiz;
        XTsiz = xtsiz;
        YTsiz = ytsiz;
        XTOsiz = xtosiz;
        YTOsiz = ytosiz;
        Precision = precision;
        Signed = (ssiz & 0x80) != 0;
        XRsiz = s[37];
        YRsiz = s[38];
        TilesAcross = (int)(((long)xsiz - xtosiz + xtsiz - 1) / xtsiz);
        TilesDown = (int)(((long)ysiz - ytosiz + ytsiz - 1) / ytsiz);
        if ((long)TilesAcross * TilesDown > 65535)
            throw new GribFormatException("The JPEG 2000 codestream has more than 65,535 tiles.");
    }

    private static void ParseCod(ReadOnlySpan<byte> s, CodingSettings settings)
    {
        if (s.Length < 5)
            throw new GribFormatException("The JPEG 2000 COD marker segment is too short.");
        var scod = s[0];
        var progression = s[1];
        if (progression > 4)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 COD marker segment has unknown progression order {progression}."));
        }

        int layers = BinaryPrimitives.ReadUInt16BigEndian(s[2..]);
        if (layers == 0)
            throw new GribFormatException("The JPEG 2000 COD marker segment has 0 quality layers.");

        // s[4] is the multiple component transformation, meaningless with one component.
        settings.Tile = new TileCodingStyle((ProgressionOrder)progression, layers, (scod & 2) != 0, (scod & 4) != 0);
        settings.FromCod = ParseComponentStyle(s[5..], explicitPrecincts: (scod & 1) != 0, "COD");
    }

    private static ComponentCodingStyle ParseComponentStyle(ReadOnlySpan<byte> s, bool explicitPrecincts, string name)
    {
        if (s.Length < 5)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment is too short."));
        }

        int levels = s[0];
        int xcb = s[1] + 2, ycb = s[2] + 2;
        int style = s[3];
        int transform = s[4];
        if (levels > 32)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment has {levels} decomposition levels; at most 32 are allowed."));
        }

        if (xcb > 10 || ycb > 10 || xcb + ycb > 12)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment has an invalid code-block size, 2^{xcb} × 2^{ycb}."));
        }

        if ((style & 0x40) != 0)
            throw new UnsupportedGribException("The JPEG 2000 codestream uses HT code-blocks (HTJ2K, ISO/IEC 15444-15), which aren't supported; only JPEG 2000 Part 1 is.");
        if ((style & 0x80) != 0)
        {
            throw new UnsupportedGribException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has code-block style 0x{style:X2}, which isn't supported; only the Part 1 styles are."));
        }

        if (transform > 1)
        {
            throw new UnsupportedGribException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream uses wavelet transformation {transform} (a Part 2 arbitrary kernel), which isn't supported; only the 9/7 irreversible (0) and 5/3 reversible (1) are."));
        }

        byte[]? precincts = null;
        if (explicitPrecincts)
        {
            if (s.Length < 5 + levels + 1)
            {
                throw new GribFormatException(
                    string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment is too short for its precinct sizes."));
            }

            precincts = s.Slice(5, levels + 1).ToArray();
            for (var r = 1; r <= levels; r++)
            {
                if ((precincts[r] & 0x0F) == 0 || (precincts[r] >> 4) == 0)
                {
                    throw new GribFormatException(
                        string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment has a precinct size of 1 at resolution {r}, which is only allowed at resolution 0."));
                }
            }
        }

        return new ComponentCodingStyle(levels, xcb, ycb, (CodeBlockStyle)style, transform == 1, precincts);
    }

    private static Quantization ParseQuantization(ReadOnlySpan<byte> s, string name)
    {
        if (s.Length < 1)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment is too short."));
        }

        var style = s[0] & 0x1F;
        var guard = s[0] >> 5;
        var values = s[1..];
        int[] exponents, mantissas;
        switch (style)
        {
            case 0:
                exponents = new int[values.Length];
                mantissas = new int[values.Length];
                for (var i = 0; i < values.Length; i++)
                    exponents[i] = values[i] >> 3;
                break;
            case 1:
            case 2:
                var count = values.Length / 2;
                if (count == 0 || (style == 1 && count != 1))
                {
                    throw new GribFormatException(
                        string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment has the wrong length for its quantisation style {style}."));
                }

                exponents = new int[count];
                mantissas = new int[count];
                for (var i = 0; i < count; i++)
                {
                    int v = BinaryPrimitives.ReadUInt16BigEndian(values[(2 * i)..]);
                    exponents[i] = v >> 11;
                    mantissas[i] = v & 0x7FF;
                }

                break;
            default:
                throw new GribFormatException(
                    string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment has unknown quantisation style {style}."));
        }

        if (exponents.Length == 0)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 {name} marker segment gives no subbands."));
        }

        return new Quantization(style, guard, exponents, mantissas);
    }

    private ushort ReadSegment(int position, out ReadOnlySpan<byte> body, out int next)
    {
        if (position + 4 > Data.Length)
            throw new GribFormatException("The JPEG 2000 codestream ends inside a header.");
        var marker = ReadUInt16(position);
        if ((marker & 0xFF00) != 0xFF00)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 codestream has no marker at byte {position}."));
        }

        int length = ReadUInt16(position + 2);
        if (length < 2 || position + 2 + length > Data.Length)
        {
            throw new GribFormatException(
                string.Create(CultureInfo.InvariantCulture, $"The JPEG 2000 marker segment 0x{marker:X4} at byte {position} runs past the end of the codestream."));
        }

        body = Data.AsSpan(position + 4, length - 2);
        next = position + 2 + length;
        return marker;
    }

    private ushort ReadUInt16(int position) => BinaryPrimitives.ReadUInt16BigEndian(Data.AsSpan(position));

    private static int ReadSize(ReadOnlySpan<byte> s, int offset)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(s[offset..]);
        if (value > int.MaxValue)
            throw new UnsupportedGribException("JPEG 2000 images or tiles larger than 2^31 − 1 samples across aren't supported.");
        return (int)value;
    }
}

/// <summary>Finds the codestream in a JP2 file (ISO/IEC 15444-1 Annex I), or passes a raw codestream through.</summary>
internal static class Jp2Container
{
    private static ReadOnlySpan<byte> Signature => [0, 0, 0, 0x0C, (byte)'j', (byte)'P', (byte)' ', (byte)' ', 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>Returns the contents of the contiguous codestream box ("jp2c"), or <paramref name="data"/> if it isn't a JP2 file.</summary>
    public static ReadOnlySpan<byte> FindCodestream(ReadOnlySpan<byte> data)
    {
        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature))
            return data;

        var position = 0;
        while (position + 8 <= data.Length)
        {
            long length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            var type = data.Slice(position + 4, 4);
            var header = 8;
            if (length == 1)
            {
                if (position + 16 > data.Length)
                    break;
                length = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(position + 8)..]);
                header = 16;
            }
            else if (length == 0)
            {
                length = data.Length - position;
            }

            if (length < header || position + length > data.Length)
                break;
            if (type.SequenceEqual("jp2c"u8))
                return data.Slice(position + header, (int)length - header);
            position += (int)length;
        }

        throw new GribFormatException("The JP2 file has no contiguous codestream box (jp2c).");
    }
}
