using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// Walks a file's messages and sections and lists its fields, reading only
/// the small sections (0, 1, 3, 4, 5 and the Section 6 indicator) and
/// skipping the bitmaps and data.
/// </summary>
/// <remarks>
/// <para>
/// Section 0 is 16 octets: "GRIB", two reserved octets, the discipline, the
/// edition and the total length as a 64-bit integer. Then each section starts
/// with its 4-octet length and 1-octet number, and the message ends with
/// "7777" (Section 8). After a Section 7 the next section may be 2, 3 or 4,
/// starting another field that reuses the sections before it.
/// </para>
/// <para>
/// Bytes between messages (padding, or a WMO bulletin header) are skipped by
/// searching for the next "GRIB".
/// </para>
/// </remarks>
internal static class GribIndexer
{
    private const int SearchChunk = 64 * 1024;

    public static List<GribField> Index(GribSource source)
    {
        var fields = new List<GribField>();
        long position = 0;
        var messageIndex = 0;
        while (true)
        {
            var start = FindMessage(source, position);
            if (start < 0)
                break;

            var length = IndexMessage(source, start, messageIndex, fields);
            position = start + length;
            messageIndex++;
        }

        if (messageIndex == 0)
            throw new GribFormatException("The file holds no GRIB message (no \"GRIB\" indicator found).");

        return fields;
    }

    private static long IndexMessage(GribSource source, long start, int messageIndex, List<GribField> fields)
    {
        Span<byte> indicator = stackalloc byte[16];
        if (start + 8 > source.Length)
            throw new GribFormatException(Offset(start, "The file ends inside a GRIB indicator section (Section 0)."));
        source.ReadExactly(start, indicator[..8]);

        var edition = indicator[7];
        if (edition == 1)
        {
            throw new UnsupportedGribException(Offset(
                start,
                "The message is GRIB edition 1, which isn't supported; only GRIB edition 2 is. Convert the file to GRIB2 (for example with ecCodes' grib_set -s edition=2, or cnvgrib) or fetch the GRIB2 product."));
        }

        if (edition != 2)
            throw new UnsupportedGribException(Offset(start, string.Create(CultureInfo.InvariantCulture, $"The message is GRIB edition {edition}, which isn't supported; only GRIB edition 2 is.")));

        if (start + 16 > source.Length)
            throw new GribFormatException(Offset(start, "The file ends inside a GRIB indicator section (Section 0)."));
        source.ReadExactly(start, indicator);
        var discipline = indicator[6];
        var total = GribBinary.UInt64(indicator, 8);
        if (total < 16 + 4 || total > (ulong)(source.Length - start))
        {
            throw new GribFormatException(Offset(
                start,
                string.Create(CultureInfo.InvariantCulture, $"The message's length ({total} bytes) runs past the end of the file ({source.Length - start} bytes left); the file may be truncated.")));
        }

        var end = start + (long)total;
        var sectionStart = start + 16;
        Span<byte> header = stackalloc byte[5];
        Span<byte> bitmapIndicator = stackalloc byte[1];

        GribIdentification? identification = null;
        GribGrid? grid = null;
        string? gridReason = null;
        GribProduct? product = null;
        string? productReason = null;
        GribDataRepresentation? drs = null;
        string? drsReason = null;
        var bitmapSeen = false;
        long bitmapOffset = -1, bitmapLength = 0;
        long definedBitmapOffset = -1, definedBitmapLength = 0;
        string? bitmapReason = null;
        var fieldNumber = 0;

        while (true)
        {
            if (sectionStart + 4 > end)
                throw new GribFormatException(Offset(start, "The message ends without its end section (\"7777\")."));

            source.ReadExactly(sectionStart, header[..4]);
            if (header[..4].SequenceEqual("7777"u8))
            {
                if (sectionStart + 4 != end)
                    throw new GribFormatException(Offset(start, "The message's end section (\"7777\") isn't where Section 0's length puts it."));
                break;
            }

            if (sectionStart + 5 > end)
                throw new GribFormatException(Offset(start, "The message ends inside a section header."));
            source.ReadExactly(sectionStart, header);
            var sectionLength = (long)GribBinary.UInt32(header, 0);
            var number = header[4];
            if (sectionLength < 5 || sectionStart + sectionLength > end - 4)
            {
                throw new GribFormatException(Offset(
                    sectionStart,
                    string.Create(CultureInfo.InvariantCulture, $"Section {number} has a length of {sectionLength} bytes, which doesn't fit in the message.")));
            }

            switch (number)
            {
                case 1:
                    identification = GribSections.ParseIdentification(source.ReadBytes(sectionStart, sectionLength));
                    break;
                case 2:
                    break;
                case 3:
                    grid = GribSections.ParseGrid(source.ReadBytes(sectionStart, sectionLength), out gridReason);
                    break;
                case 4:
                    product = GribSections.ParseProduct(source.ReadBytes(sectionStart, sectionLength), out productReason);
                    break;
                case 5:
                    drs = GribSections.ParseDataRepresentation(source.ReadBytes(sectionStart, sectionLength), out drsReason);
                    bitmapSeen = false;
                    break;
                case 6:
                    if (sectionLength < 6)
                        throw new GribFormatException(Offset(sectionStart, "Section 6 is too short for its bitmap indicator."));
                    source.ReadExactly(sectionStart + 5, bitmapIndicator);
                    bitmapSeen = true;
                    bitmapReason = null;
                    switch (bitmapIndicator[0])
                    {
                        case 0:
                            bitmapOffset = definedBitmapOffset = sectionStart + 6;
                            bitmapLength = definedBitmapLength = sectionLength - 6;
                            break;
                        case 254:
                            if (definedBitmapOffset < 0)
                                throw new GribFormatException(Offset(sectionStart, "Section 6 reuses a previously defined bitmap (indicator 254), but the message defines none before it."));
                            bitmapOffset = definedBitmapOffset;
                            bitmapLength = definedBitmapLength;
                            break;
                        case 255:
                            bitmapOffset = -1;
                            bitmapLength = 0;
                            break;
                        default:
                            bitmapOffset = -1;
                            bitmapLength = 0;
                            bitmapReason = string.Create(
                                CultureInfo.InvariantCulture,
                                $"The field uses predetermined bitmap {bitmapIndicator[0]} (Section 6 indicator, code table 6.0), which isn't supported.");
                            break;
                    }

                    break;
                case 7:
                    if (identification is null || grid is null || product is null || drs is null || !bitmapSeen)
                    {
                        throw new GribFormatException(Offset(
                            sectionStart,
                            "A data section (Section 7) comes before the sections it needs (1, 3, 4, 5 and 6)."));
                    }

                    fields.Add(new GribField(
                        index: fields.Count,
                        messageIndex: messageIndex,
                        fieldNumber: fieldNumber++,
                        messageOffset: start,
                        messageLength: (long)total,
                        discipline: discipline,
                        identification: identification,
                        grid: grid,
                        product: product,
                        dataRepresentation: drs,
                        hasBitmap: bitmapOffset >= 0,
                        unsupportedReason: gridReason ?? productReason ?? drsReason ?? bitmapReason,
                        bitmapOffset: bitmapOffset,
                        bitmapLength: bitmapLength,
                        dataOffset: sectionStart + 5,
                        dataLength: sectionLength - 5));
                    break;
                default:
                    throw new GribFormatException(Offset(
                        sectionStart,
                        string.Create(CultureInfo.InvariantCulture, $"Unknown section number {number}; GRIB2 sections are numbered 0 to 8.")));
            }

            sectionStart += sectionLength;
        }

        if (fieldNumber == 0)
            throw new GribFormatException(Offset(start, "The message has no data section (Section 7)."));

        return (long)total;
    }

    /// <summary>Returns the offset of the next "GRIB" at or after <paramref name="from"/>, or −1.</summary>
    private static long FindMessage(GribSource source, long from)
    {
        var buffer = new byte[SearchChunk];
        var position = from;
        while (position + 4 <= source.Length)
        {
            var count = (int)Math.Min(buffer.Length, source.Length - position);
            var span = buffer.AsSpan(0, count);
            source.ReadExactly(position, span);
            var found = span.IndexOf("GRIB"u8);
            if (found >= 0)
                return position + found;
            if (position + count >= source.Length)
                break;

            // Overlap by three bytes so an indicator across chunks is found.
            position += count - 3;
        }

        return -1;
    }

    private static string Offset(long offset, string message) =>
        string.Create(CultureInfo.InvariantCulture, $"{message} (At byte {offset} of the file.)");
}
