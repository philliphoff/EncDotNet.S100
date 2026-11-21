using System.Buffers.Binary;
using System.IO.Compression;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The image a GeoTIFF export covers: whole XYZ tiles at the maximum zoom,
/// snapped so that every overview level is whole tiles of a lower zoom.
/// </summary>
/// <param name="MaxZoom">The zoom of the full-resolution image.</param>
/// <param name="Levels">How many overview levels follow it: zooms <c>MaxZoom − 1</c> down to <see cref="MinZoom"/>.</param>
/// <param name="FirstColumn">The first tile column at <paramref name="MaxZoom"/>.</param>
/// <param name="FirstRow">The first tile row at <paramref name="MaxZoom"/>.</param>
/// <param name="Columns">The image width in tiles at <paramref name="MaxZoom"/>.</param>
/// <param name="Rows">The image height in tiles at <paramref name="MaxZoom"/>.</param>
internal sealed record GeoTiffGrid(int MaxZoom, int Levels, int FirstColumn, int FirstRow, int Columns, int Rows)
{
    /// <summary>The lowest zoom written: the smallest overview.</summary>
    public int MinZoom => MaxZoom - Levels;

    /// <summary>
    /// The grid for <paramref name="area"/> (EPSG:3857 metres) from
    /// <paramref name="minZoom"/> to <paramref name="maxZoom"/>. Overviews stop
    /// at <paramref name="minZoom"/> or where the image is down to a tile or
    /// two, whichever comes first; the area is widened to whole tiles of that
    /// smallest overview, which the export leaves empty.
    /// </summary>
    /// <returns><see langword="null"/> when the area crosses the antimeridian.</returns>
    public static GeoTiffGrid? For(TileArea area, int minZoom, int maxZoom)
    {
        int n = XyzTileGrid.TilesPerAxis(maxZoom);
        double size = XyzTileGrid.TileWorldSize(maxZoom);
        long first = (long)Math.Floor((area.MinX + XyzTileGrid.Extent) / size);
        long last = Math.Max(first, (long)Math.Ceiling((area.MaxX + XyzTileGrid.Extent) / size) - 1);
        if (first < 0 || last >= n)
            return null;

        var (firstRow, lastRow) = XyzTileGrid.Rows(maxZoom, area.MinY, area.MaxY);
        int x0 = (int)first, x1 = (int)last;

        long span = Math.Max(x1 - x0 + 1, lastRow - firstRow + 1);
        int levels = Math.Min(maxZoom - Math.Clamp(minZoom, 0, maxZoom), span <= 1 ? 0 : (int)Math.Ceiling(Math.Log2(span)));

        int sx0 = (x0 >> levels) << levels;
        int sx1 = (((x1 >> levels) + 1) << levels) - 1;
        int sy0 = (firstRow >> levels) << levels;
        int sy1 = (((lastRow >> levels) + 1) << levels) - 1;
        return new GeoTiffGrid(maxZoom, levels, sx0, sy0, sx1 - sx0 + 1, sy1 - sy0 + 1);
    }

    /// <summary>The tile range of zoom <paramref name="zoom"/>: first column and row, and size in tiles.</summary>
    public (int Column, int Row, int Columns, int Rows) Level(int zoom)
    {
        int shift = MaxZoom - zoom;
        return (FirstColumn >> shift, FirstRow >> shift, Columns >> shift, Rows >> shift);
    }
}

/// <summary>
/// Writes tiles into one Cloud-Optimized GeoTIFF in EPSG:3857: the maximum
/// zoom is the full-resolution image and each lower zoom an overview, as laid
/// out by a <see cref="GeoTiffGrid"/>.
/// </summary>
/// <remarks>
/// <para>
/// The TIFF tiles are the XYZ tiles. PNG and WebP tiles are decoded and
/// stored as Deflate-compressed RGBA with an unassociated alpha band; JPEG
/// tiles are stored as they are, with JPEG compression. Tiles that were never
/// written (nothing drawn, or outside the data) are left sparse: GDAL reads
/// them as transparent (RGBA) or black (JPEG).
/// </para>
/// <para>
/// Tiles arrive in any order from several threads, so their data goes to a
/// scratch file first. <see cref="Complete"/> then writes the GeoTIFF as a
/// COG: the image file directories (full resolution, then each overview)
/// first, then the tile data, smallest overview first. It is a BigTIFF when
/// the data passes 4 GB. As for the other single-file containers, the file is
/// written beside the output and moved over it when finished.
/// </para>
/// </remarks>
internal sealed class GeoTiffTileSink : ITileSink
{
    private readonly string _outputPath;
    private readonly string _partialPath;
    private readonly string _dataPath;
    private readonly GeoTiffGrid _grid;
    private readonly TileImageFormat _format;
    private readonly FileStream _data;
    private readonly Dictionary<(int Zoom, int X, int Y), (long Offset, int Length)> _tiles = [];
    private readonly Lock _gate = new();
    private bool _completed;

    public GeoTiffTileSink(string outputPath, GeoTiffGrid grid, TileImageFormat format)
    {
        _outputPath = Path.GetFullPath(outputPath);
        _partialPath = TileSetFile.PartialPath(_outputPath);
        _dataPath = _partialPath + ".tiles";
        _grid = grid;
        _format = format;
        File.Delete(_partialPath);
        _data = new FileStream(_dataPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
    }

    public void Write(int zoom, int x, int y, byte[] data)
    {
        if (zoom < _grid.MinZoom || zoom > _grid.MaxZoom)
            return;
        var (column, row, columns, rows) = _grid.Level(zoom);
        if (x < column || y < row || x >= column + columns || y >= row + rows)
            return;

        // Encoded off the lock: decoding and compressing is the costly part.
        byte[] payload = _format == TileImageFormat.Jpeg ? data : DeflateRgba(data);
        lock (_gate)
        {
            long offset = _data.Length;
            _data.Seek(offset, SeekOrigin.Begin);
            _data.Write(payload);
            _tiles[(zoom, x, y)] = (offset, payload.Length);
        }
    }

    public void Complete(TileSetMetadata metadata)
    {
        lock (_gate)
        {
            _data.Flush();
            using (var output = new FileStream(_partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                WriteTiff(output, metadata.TilePixelSize);
            _data.Dispose();
            TileSetFile.Publish(_partialPath, _outputPath);
            _completed = true;
        }
    }

    public void Dispose()
    {
        _data.Dispose();
        if (!_completed)
            TileSetFile.DeletePartial(_partialPath);
    }

    /// <summary>A PNG or WebP tile as zlib-compressed, unpremultiplied RGBA rows.</summary>
    internal static byte[] DeflateRgba(byte[] encoded)
    {
        var rgba = DecodeRgba(encoded);
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(rgba);
        return buffer.ToArray();
    }

    /// <summary>
    /// Decodes an image straight to unpremultiplied RGBA, so semi-transparent
    /// pixels keep their exact colour (no premultiplied round trip).
    /// </summary>
    internal static byte[] DecodeRgba(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("A tile couldn't be decoded.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = codec.GetPixels(info, out byte[] pixels);
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidDataException($"A tile couldn't be decoded: {result}.");
        return pixels;
    }

    private void WriteTiff(Stream output, int tileSize)
    {
        // The levels, full resolution first, as their IFDs are ordered.
        var levels = Enumerable.Range(0, _grid.Levels + 1).Select(i => _grid.MaxZoom - i).ToArray();
        long dataBytes = _tiles.Values.Sum(t => (long)t.Length);
        long tileCount = levels.Sum(z => { var l = _grid.Level(z); return (long)l.Columns * l.Rows; });
        bool big = dataBytes + tileCount * 16 + 65536 > uint.MaxValue;
        var writer = new TiffLayout(big);

        // Build each IFD with its tile offsets still zero, to size the header.
        var directories = levels.Select((zoom, index) => BuildDirectory(zoom, index == 0, tileSize)).ToArray();
        long dataStart = writer.Measure(directories);

        // Tile data, smallest overview first: fill in the offsets.
        long next = dataStart;
        foreach (int index in Enumerable.Range(0, levels.Length).Reverse())
        {
            int zoom = levels[index];
            var (column, row, columns, rows) = _grid.Level(zoom);
            var offsets = new long[columns * rows];
            var counts = new long[columns * rows];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    if (_tiles.TryGetValue((zoom, column + c, row + r), out var tile))
                    {
                        offsets[r * columns + c] = next;
                        counts[r * columns + c] = tile.Length;
                        next += tile.Length;
                    }
                }
            }

            directories[index].Set(TiffTag.TileOffsets, offsets);
            directories[index].Set(TiffTag.TileByteCounts, counts);
        }

        writer.WriteHeaderAndDirectories(output, directories);
        if (output.Position != dataStart)
            throw new InvalidOperationException("The GeoTIFF header isn't the size it was measured at.");

        var buffer = new byte[1 << 16];
        foreach (int index in Enumerable.Range(0, levels.Length).Reverse())
        {
            int zoom = levels[index];
            var (column, row, columns, rows) = _grid.Level(zoom);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    if (!_tiles.TryGetValue((zoom, column + c, row + r), out var tile))
                        continue;
                    _data.Seek(tile.Offset, SeekOrigin.Begin);
                    int remaining = tile.Length;
                    while (remaining > 0)
                    {
                        int read = _data.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                        if (read <= 0)
                            throw new EndOfStreamException("A tile's data is missing from the scratch file.");
                        output.Write(buffer, 0, read);
                        remaining -= read;
                    }
                }
            }
        }
    }

    private TiffDirectory BuildDirectory(int zoom, bool fullResolution, int tileSize)
    {
        var (_, _, columns, rows) = _grid.Level(zoom);
        bool jpeg = _format == TileImageFormat.Jpeg;
        var ifd = new TiffDirectory();
        if (!fullResolution)
            ifd.Set(TiffTag.NewSubfileType, TiffType.Long, 1);
        ifd.Set(TiffTag.ImageWidth, TiffType.Long, (long)columns * tileSize);
        ifd.Set(TiffTag.ImageLength, TiffType.Long, (long)rows * tileSize);
        ifd.Set(TiffTag.BitsPerSample, TiffType.Short, jpeg ? [8, 8, 8] : [8, 8, 8, 8]);
        ifd.Set(TiffTag.Compression, TiffType.Short, jpeg ? 7 : 8);
        ifd.Set(TiffTag.Photometric, TiffType.Short, jpeg ? 6 : 2);
        ifd.Set(TiffTag.SamplesPerPixel, TiffType.Short, jpeg ? 3 : 4);
        ifd.Set(TiffTag.PlanarConfiguration, TiffType.Short, 1);
        ifd.Set(TiffTag.TileWidth, TiffType.Long, tileSize);
        ifd.Set(TiffTag.TileLength, TiffType.Long, tileSize);
        ifd.Set(TiffTag.TileOffsets, new long[columns * rows]);
        ifd.Set(TiffTag.TileByteCounts, new long[columns * rows]);
        if (jpeg)
            ifd.Set(TiffTag.YCbCrSubSampling, TiffType.Short, [2, 2]);
        else
            ifd.Set(TiffTag.ExtraSamples, TiffType.Short, 2); // unassociated alpha
        ifd.Set(TiffTag.SampleFormat, TiffType.Short, jpeg ? [1, 1, 1] : [1, 1, 1, 1]);

        if (fullResolution)
        {
            double size = XyzTileGrid.TileWorldSize(zoom);
            double resolution = size / tileSize;
            ifd.Set(TiffTag.ModelPixelScale, [resolution, resolution, 0.0]);
            ifd.Set(TiffTag.ModelTiepoint,
            [
                0.0, 0.0, 0.0,
                -XyzTileGrid.Extent + _grid.FirstColumn * size,
                XyzTileGrid.Extent - _grid.FirstRow * size,
                0.0,
            ]);
            ifd.Set(TiffTag.GeoKeyDirectory, TiffType.Short,
            [
                1, 1, 0, 4,
                1024, 0, 1, 1,    // GTModelTypeGeoKey: projected
                1025, 0, 1, 1,    // GTRasterTypeGeoKey: pixel is area
                3072, 0, 1, 3857, // ProjectedCSTypeGeoKey: EPSG:3857
                3076, 0, 1, 9001, // ProjLinearUnitsGeoKey: metre
            ]);
        }

        return ifd;
    }
}

internal enum TiffTag : ushort
{
    NewSubfileType = 254,
    ImageWidth = 256,
    ImageLength = 257,
    BitsPerSample = 258,
    Compression = 259,
    Photometric = 262,
    SamplesPerPixel = 277,
    PlanarConfiguration = 284,
    TileWidth = 322,
    TileLength = 323,
    TileOffsets = 324,
    TileByteCounts = 325,
    ExtraSamples = 338,
    SampleFormat = 339,
    YCbCrSubSampling = 530,
    ModelPixelScale = 33550,
    ModelTiepoint = 33922,
    GeoKeyDirectory = 34735,
}

internal enum TiffType : ushort
{
    Short = 3,
    Long = 4,
    Double = 12,
    Long8 = 16,
}

/// <summary>The tags of one TIFF image file directory.</summary>
internal sealed class TiffDirectory
{
    private readonly SortedDictionary<TiffTag, (TiffType Type, object Values)> _entries = [];

    public IEnumerable<(TiffTag Tag, TiffType Type, object Values)> Entries =>
        _entries.Select(e => (e.Key, e.Value.Type, e.Value.Values));

    public void Set(TiffTag tag, TiffType type, params long[] values) => _entries[tag] = (type, values);

    /// <summary>Offsets or byte counts: LONG in a TIFF, LONG8 in a BigTIFF.</summary>
    public void Set(TiffTag tag, long[] values) => _entries[tag] = (TiffType.Long8, values);

    public void Set(TiffTag tag, double[] values) => _entries[tag] = (TiffType.Double, values);
}

/// <summary>Writes the header and directories of a little-endian TIFF or BigTIFF.</summary>
internal sealed class TiffLayout(bool big)
{
    private int EntrySize => big ? 20 : 12;

    private int InlineSize => big ? 8 : 4;

    private int OffsetSize => big ? 8 : 4;

    private int HeaderSize => big ? 16 : 8;

    /// <summary>Where the data after the header and <paramref name="directories"/> starts.</summary>
    public long Measure(IReadOnlyList<TiffDirectory> directories)
    {
        long position = HeaderSize;
        foreach (var directory in directories)
            position += DirectorySize(directory);
        return position;
    }

    public void WriteHeaderAndDirectories(Stream output, IReadOnlyList<TiffDirectory> directories)
    {
        var header = new byte[HeaderSize];
        header[0] = header[1] = (byte)'I';
        if (big)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 43);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(8), (ulong)HeaderSize);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 42);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)HeaderSize);
        }

        output.Write(header);

        long position = HeaderSize;
        for (int i = 0; i < directories.Count; i++)
        {
            long size = DirectorySize(directories[i]);
            long nextDirectory = i + 1 < directories.Count ? position + size : 0;
            output.Write(EncodeDirectory(directories[i], position, nextDirectory));
            position += size;
        }
    }

    private long DirectorySize(TiffDirectory directory)
    {
        int count = directory.Entries.Count();
        long size = (big ? 8 : 2) + (long)count * EntrySize + OffsetSize;
        foreach (var (_, type, values) in directory.Entries)
        {
            long bytes = ValueBytes(type, values);
            if (bytes > InlineSize)
                size += bytes + (bytes & 1);
        }

        return size;
    }

    private TiffType Resolve(TiffType type) => type == TiffType.Long8 && !big ? TiffType.Long : type;

    private long ValueBytes(TiffType type, object values) => Count(values) * TypeSize(Resolve(type));

    private static long Count(object values) => values switch
    {
        long[] l => l.Length,
        double[] d => d.Length,
        _ => throw new ArgumentException("Unsupported TIFF values.", nameof(values)),
    };

    private static int TypeSize(TiffType type) => type switch
    {
        TiffType.Short => 2,
        TiffType.Long => 4,
        TiffType.Double or TiffType.Long8 => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private byte[] EncodeDirectory(TiffDirectory directory, long start, long nextDirectory)
    {
        var entries = directory.Entries.ToList();
        long size = DirectorySize(directory);
        var bytes = new byte[size];
        var span = bytes.AsSpan();
        int at = 0;
        if (big)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span[at..], (ulong)entries.Count);
            at += 8;
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[at..], (ushort)entries.Count);
            at += 2;
        }

        long outOfLine = at + (long)entries.Count * EntrySize + OffsetSize;
        foreach (var (tag, declared, values) in entries)
        {
            var type = Resolve(declared);
            long count = Count(values);
            long valueBytes = ValueBytes(declared, values);
            BinaryPrimitives.WriteUInt16LittleEndian(span[at..], (ushort)tag);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(at + 2)..], (ushort)type);
            int valueAt;
            if (big)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(span[(at + 4)..], (ulong)count);
                valueAt = at + 12;
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[(at + 4)..], (uint)count);
                valueAt = at + 8;
            }

            if (valueBytes <= InlineSize)
            {
                WriteValues(span[valueAt..], type, values);
            }
            else
            {
                if (big)
                    BinaryPrimitives.WriteUInt64LittleEndian(span[valueAt..], (ulong)(start + outOfLine));
                else
                    BinaryPrimitives.WriteUInt32LittleEndian(span[valueAt..], checked((uint)(start + outOfLine)));
                WriteValues(span[(int)outOfLine..], type, values);
                outOfLine += valueBytes + (valueBytes & 1);
            }

            at += EntrySize;
        }

        if (big)
            BinaryPrimitives.WriteUInt64LittleEndian(span[at..], (ulong)nextDirectory);
        else
            BinaryPrimitives.WriteUInt32LittleEndian(span[at..], checked((uint)nextDirectory));
        return bytes;
    }

    private static void WriteValues(Span<byte> target, TiffType type, object values)
    {
        switch (values)
        {
            case double[] doubles:
                for (int i = 0; i < doubles.Length; i++)
                    BinaryPrimitives.WriteDoubleLittleEndian(target[(i * 8)..], doubles[i]);
                break;
            case long[] longs:
                for (int i = 0; i < longs.Length; i++)
                {
                    switch (type)
                    {
                        case TiffType.Short:
                            BinaryPrimitives.WriteUInt16LittleEndian(target[(i * 2)..], checked((ushort)longs[i]));
                            break;
                        case TiffType.Long:
                            BinaryPrimitives.WriteUInt32LittleEndian(target[(i * 4)..], checked((uint)longs[i]));
                            break;
                        default:
                            BinaryPrimitives.WriteUInt64LittleEndian(target[(i * 8)..], (ulong)longs[i]);
                            break;
                    }
                }

                break;
        }
    }
}
