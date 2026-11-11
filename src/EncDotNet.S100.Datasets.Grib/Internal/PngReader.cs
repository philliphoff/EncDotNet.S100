using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// A minimal PNG decoder for GRIB2 template 5.41: non-interlaced grey,
/// grey-and-alpha, RGB and RGBA images, read to unfiltered rows. Written
/// from the PNG specification (ISO/IEC 15948, W3C PNG second edition).
/// </summary>
/// <remarks>
/// Palette images and Adam7 interlacing aren't used by GRIB2 encoders and
/// fail with a message. Chunk CRCs aren't checked: the GRIB2 message has
/// its own framing, and a corrupt image fails in decompression or filtering.
/// </remarks>
internal static class PngReader
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PngImage Read(ReadOnlySpan<byte> png)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
            throw new GribFormatException("Template 5.41 data isn't a PNG image (the PNG signature is missing).");

        var position = Signature.Length;
        int width = 0, height = 0, bitDepth = 0, colourType = -1;
        using var compressed = new MemoryStream();
        var sawHeader = false;
        while (true)
        {
            if (position + 8 > png.Length)
                throw new GribFormatException("The PNG image ends before its IEND chunk.");

            var length = BinaryPrimitives.ReadUInt32BigEndian(png[position..]);
            var type = png.Slice(position + 4, 4);
            if (length > int.MaxValue || position + 12 + (long)length > png.Length)
                throw new GribFormatException("A PNG chunk runs past the end of the image.");

            var body = png.Slice(position + 8, (int)length);
            if (type.SequenceEqual("IHDR"u8))
            {
                if (body.Length < 13)
                    throw new GribFormatException("The PNG IHDR chunk is too short.");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
                bitDepth = body[8];
                colourType = body[9];
                if (body[10] != 0 || body[11] != 0)
                    throw new GribFormatException("The PNG image uses an unknown compression or filter method.");
                if (body[12] != 0)
                    throw new UnsupportedGribException("Interlaced (Adam7) PNG images in template 5.41 aren't supported.");
                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }

            position += 12 + (int)length;
        }

        if (!sawHeader)
            throw new GribFormatException("The PNG image has no IHDR chunk.");

        var channels = colourType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            3 => throw new UnsupportedGribException("Palette PNG images in template 5.41 aren't supported."),
            _ => throw new GribFormatException(string.Create(CultureInfo.InvariantCulture, $"The PNG image has an invalid colour type {colourType}.")),
        };
        var validDepth = colourType == 0 ? bitDepth is 1 or 2 or 4 or 8 or 16 : bitDepth is 8 or 16;
        if (!validDepth)
            throw new GribFormatException(string.Create(CultureInfo.InvariantCulture, $"The PNG image has an invalid bit depth {bitDepth} for colour type {colourType}."));

        var bitsPerPixel = channels * bitDepth;
        var rowBytes = checked((int)(((long)width * bitsPerPixel + 7) / 8));
        var filterUnit = Math.Max(1, bitsPerPixel / 8);
        var pixels = new byte[checked((long)rowBytes * height)];

        compressed.Position = 0;
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            var filtered = new byte[rowBytes + 1];
            for (var row = 0; row < height; row++)
            {
                try
                {
                    inflater.ReadExactly(filtered);
                }
                catch (EndOfStreamException ex)
                {
                    throw new GribFormatException("The PNG image data ends before its last row.", ex);
                }

                var current = pixels.AsSpan(row * rowBytes, rowBytes);
                var previous = row == 0 ? Span<byte>.Empty : pixels.AsSpan((row - 1) * rowBytes, rowBytes);
                Unfilter(filtered[0], filtered.AsSpan(1), current, previous, filterUnit);
            }
        }

        return new PngImage(width, height, bitsPerPixel, rowBytes, pixels);
    }

    private static void Unfilter(byte filter, ReadOnlySpan<byte> source, Span<byte> current, ReadOnlySpan<byte> previous, int unit)
    {
        for (var i = 0; i < source.Length; i++)
        {
            int left = i >= unit ? current[i - unit] : 0;
            int up = previous.IsEmpty ? 0 : previous[i];
            int upLeft = i >= unit && !previous.IsEmpty ? previous[i - unit] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) >> 1,
                4 => Paeth(left, up, upLeft),
                _ => throw new GribFormatException(string.Create(CultureInfo.InvariantCulture, $"The PNG image uses an unknown row filter {filter}.")),
            };
            current[i] = (byte)(source[i] + predictor);
        }
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
}

/// <summary>A decoded PNG image: unfiltered rows of packed pixels.</summary>
internal sealed class PngImage(int width, int height, int bitsPerPixel, int rowBytes, byte[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public int BitsPerPixel { get; } = bitsPerPixel;

    public ReadOnlySpan<byte> Row(int row) => pixels.AsSpan(row * rowBytes, rowBytes);
}
