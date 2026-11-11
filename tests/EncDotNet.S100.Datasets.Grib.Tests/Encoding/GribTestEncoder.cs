using System.IO.Compression;

namespace EncDotNet.S100.Datasets.Grib.Tests.Encoding;

/// <summary>
/// A test-only GRIB2 encoder, written from the WMO FM 92 templates, for
/// building messages that exercise each part of the decoder: templates 3.0,
/// 4.0, 4.8, 5.0, 5.2, 5.3 and 5.41, the bitmap, and repeated sections.
/// </summary>
/// <remarks>
/// It's deterministic, so a decoded field re-encoded with the same packing
/// and reference value gives the same bytes: the round-trip tests rely on it.
/// </remarks>
internal static class GribTestEncoder
{
    /// <summary>Encodes one message holding <paramref name="fields"/>; fields after the first repeat sections 3 to 7, or 4 to 7 when the grid is unchanged.</summary>
    public static byte[] Message(IReadOnlyList<TestField> fields, byte[]? localUse = null)
    {
        if (fields.Count == 0)
            throw new ArgumentException("A message needs a field.", nameof(fields));

        var first = fields[0];
        var body = new OctetWriter();
        body.Bytes(IdentificationSection(first));
        if (localUse is not null)
            body.Bytes(OctetWriter.Section(2, w => w.Bytes(localUse)));

        bool[]? previousBitmap = null;
        TestGrid? previousGrid = null;
        foreach (var field in fields)
        {
            if (field.Grid != previousGrid)
                body.Bytes(GridSection(field.Grid));
            previousGrid = field.Grid;
            body.Bytes(ProductSection(field));
            var (section5, section6, section7, bitmap) = DataSections(field, previousBitmap);
            body.Bytes(section5).Bytes(section6).Bytes(section7);
            if (bitmap is not null)
                previousBitmap = bitmap;
        }

        var content = body.ToArray();
        var message = new OctetWriter()
            .Bytes("GRIB"u8).U8(0).U8(0).U8(first.Discipline).U8(2).U64(16 + content.Length + 4)
            .Bytes(content)
            .Bytes("7777"u8);
        return message.ToArray();
    }

    /// <summary>Encodes a message with one field.</summary>
    public static byte[] Message(TestField field) => Message([field]);

    /// <summary>Concatenates messages into a file.</summary>
    public static byte[] File(params byte[][] messages) => [.. messages.SelectMany(m => m)];

    private static byte[] IdentificationSection(TestField f) => OctetWriter.Section(1, w => w
        .U16(f.Centre).U16(0).U8(2).U8(1).U8(1)
        .U16(f.ReferenceTime.Year).U8(f.ReferenceTime.Month).U8(f.ReferenceTime.Day)
        .U8(f.ReferenceTime.Hour).U8(f.ReferenceTime.Minute).U8(f.ReferenceTime.Second)
        .U8(0).U8(1));

    private static byte[] GridSection(TestGrid g) => OctetWriter.Section(3, w =>
    {
        w.U8(0).U32((long)g.Ni * g.Nj).U8(0).U8(0).U16(g.TemplateNumber);
        w.U8(6).U8(0).U32(0).U8(0).U32(0).U8(0).U32(0);
        w.U32(g.Ni).U32(g.Nj).U32(0).U32(0xFFFF_FFFF);
        w.S32(Micro(g.FirstLatitude)).S32(Micro(g.FirstLongitude));
        w.U8(g.IncrementsGiven ? 0x30 : 0x00);
        w.S32(Micro(g.LastLatitude)).S32(Micro(g.LastLongitude));
        if (g.IncrementsGiven)
            w.U32(Micro(g.IIncrement)).U32(Micro(g.JIncrement));
        else
            w.U32(0xFFFF_FFFF).U32(0xFFFF_FFFF);
        w.U8(g.ScanningMode);
    });

    private static long Micro(double degrees) => (long)Math.Round(degrees * 1e6);

    private static byte[] ProductSection(TestField f) => OctetWriter.Section(4, w =>
    {
        w.U16(0).U16(f.ProductTemplate);
        w.U8(f.Category).U8(f.Number).U8(2).U8(0).U8(96).U16(0).U8(0);
        w.U8(f.TimeUnit).S32(f.ForecastTime);
        w.U8(f.SurfaceType).S8(f.SurfaceScale).S32(f.SurfaceValue);
        w.U8(255).U8(255).U32(0xFFFF_FFFF);
        if (f.ProductTemplate == 8)
        {
            var e = f.IntervalEnd;
            w.U16(e.Year).U8(e.Month).U8(e.Day).U8(e.Hour).U8(e.Minute).U8(e.Second);
            w.U8(1).U32(0);
            w.U8(f.StatisticalProcess).U8(2).U8(1).S32(f.StatisticalLength).U8(1).S32(0);
        }
        else if (f.ProductTemplate != 0)
        {
            // Unsupported templates: pad so the section is plausible.
            w.Bytes(new byte[16]);
        }
    });

    private static (byte[] Section5, byte[] Section6, byte[] Section7, bool[]? Bitmap) DataSections(TestField f, bool[]? previousBitmap)
    {
        var values = f.Values;
        if (values.Length != (long)f.Grid.Ni * f.Grid.Nj)
            throw new ArgumentException("The values don't match the grid.");

        bool[]? bitmap = null;
        byte[] section6;
        switch (f.Bitmap)
        {
            case TestBitmap.None:
                section6 = OctetWriter.Section(6, w => w.U8(255));
                break;
            case TestBitmap.Explicit:
                bitmap = [.. values.Select(v => !IsMissing(v))];
                var bits = new BitWriter();
                foreach (var present in bitmap)
                    bits.Write(present ? 1UL : 0UL, 1);
                var bitmapBytes = bits.ToArray();
                section6 = OctetWriter.Section(6, w => w.U8(0).Bytes(bitmapBytes));
                break;
            default:
                bitmap = previousBitmap ?? throw new ArgumentException("No previous bitmap to reuse.");
                if (!bitmap.SequenceEqual(values.Select(v => !IsMissing(v))))
                    throw new ArgumentException("The reused bitmap doesn't match the missing values.");
                section6 = OctetWriter.Section(6, w => w.U8(254));
                break;
        }

        var packed = bitmap is null ? values : [.. values.Where((_, i) => bitmap[i])];
        if (f.Packing is RawTestPacking raw)
        {
            var s5raw = OctetWriter.Section(5, w => w.U32(raw.PackedValues).U16(raw.TemplateNumber).Bytes(raw.TemplateBody));
            return (s5raw, section6, OctetWriter.Section(7, w => w.Bytes(raw.Data)), bitmap);
        }

        var (template, templateBody, data) = Pack(packed, f.Packing, f.Grid.Ni);
        var s5 = OctetWriter.Section(5, w => w.U32(packed.Length).U16(template).Bytes(templateBody));
        var s7 = OctetWriter.Section(7, w => w.Bytes(data));
        return (s5, section6, s7, bitmap);
    }

    private static bool IsMissing(double v) => double.IsNaN(v) || double.IsNegativeInfinity(v);

    private static (int Template, byte[] TemplateBody, byte[] Data) Pack(double[] values, TestPacking packing, int ni)
    {
        var mvm = packing is ComplexTestPacking c ? c.MissingValueManagement : 0;
        if (mvm == 0 && values.Any(IsMissing))
            throw new ArgumentException("Missing values need a bitmap or missing value management.");

        // Quantise: X = round((Y × 10^D − R) / 2^E).
        var decimalFactor = Math.Pow(10, packing.DecimalScale);
        var binaryFactor = Math.Pow(2, -packing.BinaryScale);
        var present = values.Where(v => !IsMissing(v)).Select(v => v * decimalFactor).ToArray();
        float reference;
        if (packing.Reference is { } given)
        {
            reference = given;
        }
        else if (present.Length == 0)
        {
            reference = 0;
        }
        else
        {
            var min = present.Min();
            reference = (float)min;
            if (reference > min)
                reference = MathF.BitDecrement(reference);
        }

        var integers = new long[values.Length];
        var missing = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (double.IsNaN(values[i]))
                missing[i] = 1;
            else if (double.IsNegativeInfinity(values[i]))
                missing[i] = 2;
            else
                integers[i] = Math.Max(0, (long)Math.Round(((values[i] * decimalFactor) - reference) * binaryFactor));
        }

        // A constant field has 0 bits, and its reference value is the value
        // itself, unscaled, as ecCodes and NCEP's g2lib write and read it.
        var constant = present.Length > 0 && present.All(v => v == present[0]) && packing.Reference is null
            && packing is SimpleTestPacking { Bits: null or 0 } or PngTestPacking { Depth: 0 };
        if (constant)
            reference = (float)values.First(v => !IsMissing(v));

        var common = new OctetWriter().F32(reference).S16(packing.BinaryScale).S16(packing.DecimalScale);
        switch (packing)
        {
            case SimpleTestPacking simple:
                {
                    var bits = constant ? 0 : simple.Bits ?? BitWriter.BitsFor((ulong)(integers.Length == 0 ? 0 : integers.Max()));
                    var writer = new BitWriter();
                    if (bits > 0)
                    {
                        foreach (var x in integers)
                            writer.Write((ulong)x, bits);
                    }

                    return (0, common.U8(bits).U8(0).ToArray(), writer.ToArray());
                }

            case ComplexTestPacking complex:
                return PackComplex(integers, missing, complex, common);

            case PngTestPacking png:
                return (41, common.U8(png.Depth).U8(0).ToArray(), PackPng(integers, png.Depth, ni));

            default:
                throw new ArgumentException($"Unknown packing {packing}.");
        }
    }

    private static (int Template, byte[] TemplateBody, byte[] Data) PackComplex(long[] integers, int[] missing, ComplexTestPacking p, OctetWriter common)
    {
        var order = p.SpatialDifferencingOrder;
        var mvm = p.MissingValueManagement;
        var count = integers.Length;

        // Spatial differencing over the values that aren't missing.
        long h1 = 0, h2 = 0, gmin = 0;
        var descriptorOctets = 0;
        var packed = (long[])integers.Clone();
        if (order > 0)
        {
            var chain = Enumerable.Range(0, count).Where(i => missing[i] == 0).ToArray();
            var differences = new long[chain.Length];
            for (var k = order; k < chain.Length; k++)
            {
                differences[k] = order == 1
                    ? integers[chain[k]] - integers[chain[k - 1]]
                    : integers[chain[k]] - (2 * integers[chain[k - 1]]) + integers[chain[k - 2]];
            }

            h1 = chain.Length > 0 ? integers[chain[0]] : 0;
            h2 = order == 2 && chain.Length > 1 ? integers[chain[1]] : 0;
            gmin = chain.Length > order ? differences.Skip(order).Min() : 0;
            for (var k = 0; k < chain.Length; k++)
                packed[chain[k]] = k < order ? 0 : differences[k] - gmin;

            var largest = Math.Max(Math.Abs(h1), Math.Max(Math.Abs(h2), Math.Abs(gmin)));
            descriptorOctets = Math.Max(1, (BitWriter.BitsFor((ulong)largest) + 1 + 7) / 8);
        }

        // Groups of GroupSize consecutive values.
        var groupCount = Math.Max(1, (count + p.GroupSize - 1) / p.GroupSize);
        var lengths = new int[groupCount];
        var refs = new long[groupCount];
        var widths = new int[groupCount];
        var groupMarker = new int[groupCount];
        for (var g = 0; g < groupCount; g++)
        {
            var start = g * p.GroupSize;
            var length = Math.Min(p.GroupSize, count - start);
            lengths[g] = length;
            var indices = Enumerable.Range(start, length);
            var present = indices.Where(i => missing[i] == 0).Select(i => packed[i]).ToArray();
            var kinds = indices.Select(i => missing[i]).Distinct().ToArray();
            if (present.Length == 0 && length > 0 && kinds.Length == 1 && mvm > 0)
            {
                groupMarker[g] = kinds[0];
                continue;
            }

            var min = present.Length == 0 ? 0 : present.Min();
            var range = present.Length == 0 ? 0 : present.Max() - min;
            refs[g] = min;
            var anyMissing = kinds.Any(k => k != 0);
            widths[g] = mvm == 0 || (range == 0 && !anyMissing)
                ? BitWriter.BitsFor((ulong)range)
                : BitWriter.BitsFor((ulong)(range + mvm));
        }

        var maxRef = refs.Where((_, g) => groupMarker[g] == 0).DefaultIfEmpty(0).Max();
        var nbits = BitWriter.BitsFor((ulong)(maxRef + (mvm > 0 ? 2 : 0)));
        var allOnes = nbits == 0 ? 0 : (1L << nbits) - 1;
        for (var g = 0; g < groupCount; g++)
        {
            if (groupMarker[g] != 0)
            {
                refs[g] = groupMarker[g] == 1 ? allOnes : allOnes - 1;
                widths[g] = 0;
            }
        }

        var widthRef = widths.Min();
        var widthBits = BitWriter.BitsFor((ulong)(widths.Max() - widthRef));
        var storedLengths = lengths.Take(Math.Max(1, groupCount - 1)).ToArray();
        var lengthRef = storedLengths.Min();
        var lengthBits = BitWriter.BitsFor((ulong)(storedLengths.Max() - lengthRef));

        var data = new BitWriter();
        if (order > 0)
        {
            var descriptors = new OctetWriter().SignMagnitude(h1, descriptorOctets);
            if (order == 2)
                descriptors.SignMagnitude(h2, descriptorOctets);
            descriptors.SignMagnitude(gmin, descriptorOctets);
            data.WriteBytes(descriptors.ToArray());
        }

        foreach (var r in refs)
            data.Write((ulong)r, nbits);
        data.Align();
        foreach (var w in widths)
            data.Write((ulong)(w - widthRef), widthBits);
        data.Align();
        for (var g = 0; g < groupCount; g++)
            data.Write(g == groupCount - 1 ? 0UL : (ulong)(lengths[g] - lengthRef), lengthBits);
        data.Align();
        for (var g = 0; g < groupCount; g++)
        {
            if (widths[g] == 0)
                continue;
            var codes = (1L << widths[g]) - 1;
            for (var i = g * p.GroupSize; i < (g * p.GroupSize) + lengths[g]; i++)
            {
                var x = missing[i] switch
                {
                    1 => codes,
                    2 => codes - 1,
                    _ => packed[i] - refs[g],
                };
                data.Write((ulong)x, widths[g]);
            }
        }

        var body = common.U8(nbits).U8(0)
            .U8(1).U8(mvm)
            .F32(mvm > 0 ? 9999f : 0f).F32(mvm > 1 ? -9999f : 0f)
            .U32(groupCount).U8(widthRef).U8(widthBits)
            .U32(lengthRef).U8(1).U32(lengths[^1]).U8(lengthBits);
        if (order > 0)
            body.U8(order).U8(descriptorOctets);

        return (order > 0 ? 3 : 2, body.ToArray(), data.ToArray());
    }

    private static byte[] PackPng(long[] integers, int depth, int ni)
    {
        var count = integers.Length;
        var width = Math.Max(1, Math.Min(count, ni));
        var height = Math.Max(1, (count + width - 1) / width);
        var (colourType, bitDepth) = depth switch
        {
            24 => (2, 8),
            32 => (6, 8),
            _ => (0, depth),
        };

        var rowBytes = ((width * depth) + 7) / 8;
        var unit = Math.Max(1, depth / 8);
        var raw = new byte[height * rowBytes];
        for (var row = 0; row < height; row++)
        {
            var bits = new BitWriter();
            for (var col = 0; col < width; col++)
            {
                var i = (row * width) + col;
                bits.Write(i < count ? (ulong)integers[i] : 0UL, depth);
            }

            bits.ToArray().CopyTo(raw, row * rowBytes);
        }

        // Filter each row with a different filter type, so the decoder's
        // unfiltering is exercised for all five.
        using var filtered = new MemoryStream();
        for (var row = 0; row < height; row++)
        {
            var filter = (byte)(row % 5);
            filtered.WriteByte(filter);
            for (var i = 0; i < rowBytes; i++)
            {
                int current = raw[(row * rowBytes) + i];
                int left = i >= unit ? raw[(row * rowBytes) + i - unit] : 0;
                int up = row > 0 ? raw[((row - 1) * rowBytes) + i] : 0;
                int upLeft = row > 0 && i >= unit ? raw[((row - 1) * rowBytes) + i - unit] : 0;
                var predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    _ => Paeth(left, up, upLeft),
                };
                filtered.WriteByte((byte)(current - predictor));
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(filtered.ToArray());

        var png = new OctetWriter().Bytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR"u8, new OctetWriter().U32(width).U32(height).U8(bitDepth).U8(colourType).U8(0).U8(0).U8(0).ToArray());
        Chunk(png, "IDAT"u8, compressed.ToArray());
        Chunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void Chunk(OctetWriter png, ReadOnlySpan<byte> type, byte[] data)
    {
        var typed = new OctetWriter().Bytes(type).Bytes(data).ToArray();
        png.U32(data.Length).Bytes(typed).U32(Crc32(typed));
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
        }

        return ~crc;
    }
}
