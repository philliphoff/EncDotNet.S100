using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;
using ZstdSharp;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// The warm tile cache's file format: premultiplied RGBA pixels compressed
/// with Zstandard, behind a small header. Exports keep PNG; this format is
/// only for the cache (map rendering slice 11).
/// </summary>
/// <remarks>
/// <para>
/// Measured on 95 Washington chart tiles at 516&#160;px (one core, median):
/// PNG at quality 100 encodes in 5.1&#160;ms and decodes in 0.62&#160;ms to
/// 27.9&#160;KB; Zstandard level 3 encodes in 0.51&#160;ms and decodes in
/// 0.17&#160;ms to 20.2&#160;KB. LZ4 encodes faster (0.16&#160;ms) but decodes
/// slower and is larger (29.7&#160;KB).
/// </para>
/// <para>
/// Layout: the magic <c>S1TZ</c>, the width and height as little-endian
/// 16-bit integers, then the Zstandard frame. A tile whose pixels are all
/// zero (fully transparent) has no file content at all: it is stored as an
/// empty marker, and reads back as <see cref="EmptyImage"/> without
/// decompressing anything.
/// </para>
/// </remarks>
internal static class TileCodec
{
    /// <summary>The Zstandard compression level.</summary>
    internal const int Level = 3;

    private const int HeaderLength = 8;
    private static ReadOnlySpan<byte> Magic => "S1TZ"u8;

    [ThreadStatic]
    private static Compressor? _compressor;

    [ThreadStatic]
    private static Decompressor? _decompressor;

    private static readonly Lock EmptyGate = new();
    private static readonly Dictionary<(int Width, int Height), nint> EmptyPixels = [];

    /// <summary>
    /// Encodes <paramref name="image"/>: an empty array when every pixel is
    /// zero, otherwise the header and the compressed pixels; or
    /// <see langword="null"/> when the pixels can't be read.
    /// </summary>
    /// <param name="image">The tile image.</param>
    /// <returns>The encoded tile, or <see langword="null"/>.</returns>
    public static byte[]? Encode(SKImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
        {
            return null;
        }

        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var raw = new byte[info.BytesSize];
        unsafe
        {
            fixed (byte* pixels = raw)
            {
                if (!image.ReadPixels(info, (nint)pixels, info.RowBytes, 0, 0))
                {
                    return null;
                }
            }
        }

        return EncodePixels(raw, image.Width, image.Height);
    }

    /// <summary>Encodes premultiplied RGBA8888 <paramref name="pixels"/> of the given size.</summary>
    /// <param name="pixels">The pixels, row by row, with no row padding.</param>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The encoded tile; empty when every pixel is zero.</returns>
    public static byte[] EncodePixels(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (pixels.IndexOfAnyExcept((byte)0) < 0)
        {
            return [];
        }

        var compressor = _compressor ??= new Compressor(Level);
        var bound = Compressor.GetCompressBound(pixels.Length);
        var buffer = new byte[HeaderLength + bound];
        Magic.CopyTo(buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), (ushort)height);
        var written = compressor.Wrap(pixels, buffer.AsSpan(HeaderLength));
        return buffer.AsSpan(0, HeaderLength + written).ToArray();
    }

    /// <summary>
    /// Decodes an encoded tile into a raster image, or returns
    /// <see langword="null"/> when the bytes aren't a valid tile.
    /// </summary>
    /// <param name="encoded">The encoded tile (not empty).</param>
    /// <returns>The decoded image, or <see langword="null"/>.</returns>
    public static SKImage? Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length <= HeaderLength || !encoded[..4].SequenceEqual(Magic))
        {
            return null;
        }

        int width = BinaryPrimitives.ReadUInt16LittleEndian(encoded[4..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(encoded[6..]);
        if (width == 0 || height == 0)
        {
            return null;
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        try
        {
            unsafe
            {
                var span = new Span<byte>((void*)bitmap.GetPixels(), info.BytesSize);
                var decompressor = _decompressor ??= new Decompressor();
                if (decompressor.Unwrap(encoded[HeaderLength..], span) != info.BytesSize)
                {
                    return null;
                }
            }

            bitmap.SetImmutable();
            return SKImage.FromBitmap(bitmap);
        }
        catch
        {
            return null;
        }
        finally
        {
            // The image keeps its own reference to the pixels.
            bitmap.Dispose();
        }
    }

    /// <summary>
    /// A fully transparent image of the given size that shares one zeroed
    /// pixel buffer per size, so an empty marker costs neither a decode nor a
    /// pixel allocation. Each call returns a new image the caller owns and may
    /// dispose; the shared buffer is never freed.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>A transparent image.</returns>
    public static SKImage EmptyImage(int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        nint pixels;
        lock (EmptyGate)
        {
            if (!EmptyPixels.TryGetValue((width, height), out pixels))
            {
                unsafe
                {
                    pixels = (nint)NativeMemory.AllocZeroed((nuint)info.BytesSize);
                }

                EmptyPixels[(width, height)] = pixels;
            }
        }

        using var pixmap = new SKPixmap(info, pixels, info.RowBytes);
        return SKImage.FromPixels(pixmap);
    }
}
