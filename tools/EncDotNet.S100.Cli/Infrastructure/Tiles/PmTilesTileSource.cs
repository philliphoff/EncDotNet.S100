using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using Entry = EncDotNet.S100.Cli.Infrastructure.Tiles.PmTilesTileSink.Entry;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Reads a PMTiles v3 archive
/// (https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md).
/// </summary>
/// <remarks>
/// The header, metadata and the directories read so far are cached until the
/// file's size or write time changes, so an archive written again while it is
/// served is re-read. The file is opened for each read and never held open.
/// </remarks>
internal sealed class PmTilesTileSource : ITileSetSource
{
    private const int MaxDirectoryDepth = 4;

    private readonly Lock _gate = new();
    private Archive _archive;

    public PmTilesTileSource(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _archive = Archive.Load(Path);
    }

    public string Path { get; }

    public TileContainer Container => TileContainer.PmTiles;

    public TileImageFormat Format => Current().Format;

    public byte[]? Read(int zoom, int x, int y)
    {
        if (!TileSource.IsValid(zoom, x, y))
            return null;

        var archive = Current();
        ulong tileId = PmTilesTileSink.ZxyToTileId(zoom, x, y);
        using var file = OpenHandle(Path);

        long directoryOffset = archive.RootOffset;
        int directoryLength = archive.RootLength;
        for (int depth = 0; depth < MaxDirectoryDepth; depth++)
        {
            var entries = archive.Directory(file, directoryOffset, directoryLength);
            if (FindEntry(entries, tileId) is not { } entry)
                return null;

            if (entry.RunLength > 0)
                return ReadExactly(file, archive.DataOffset + entry.Offset, entry.Length);

            directoryOffset = archive.LeavesOffset + entry.Offset;
            directoryLength = entry.Length;
        }

        throw new InvalidDataException($"'{Path}' nests its directories more than {MaxDirectoryDepth} deep.");
    }

    public JsonObject ToTileJson()
    {
        var archive = Current();
        var json = archive.Metadata.DeepClone().AsObject();
        json.Remove("tiles");
        json["tilejson"] = "3.0.0";
        json["scheme"] = "xyz";
        json["format"] = TileSetMetadata.FormatToken(archive.Format);
        json["name"] ??= System.IO.Path.GetFileNameWithoutExtension(Path);
        json["minzoom"] ??= archive.MinZoom;
        json["maxzoom"] ??= archive.MaxZoom;
        json["bounds"] ??= new JsonArray(archive.Bounds.West, archive.Bounds.South, archive.Bounds.East, archive.Bounds.North);
        json["center"] ??= new JsonArray(archive.Center.Longitude, archive.Center.Latitude, archive.Center.Zoom);
        return json;
    }

    /// <summary>
    /// The entry that holds <paramref name="tileId"/>: the tile run covering it,
    /// or the leaf directory that may; <see langword="null"/> when neither does.
    /// </summary>
    internal static Entry? FindEntry(IReadOnlyList<Entry> entries, ulong tileId)
    {
        int low = 0;
        int high = entries.Count - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            var candidate = entries[middle].TileId;
            if (candidate < tileId)
                low = middle + 1;
            else if (candidate > tileId)
                high = middle - 1;
            else
                return entries[middle];
        }

        // `high` is now the last entry starting before the tile.
        if (high < 0)
            return null;
        var entry = entries[high];
        if (entry.RunLength == 0 || tileId - entry.TileId < (ulong)entry.RunLength)
            return entry;
        return null;
    }

    /// <summary>Parses a decompressed directory.</summary>
    internal static Entry[] DeserializeDirectory(ReadOnlySpan<byte> data)
    {
        int position = 0;
        ulong count = ReadVarint(data, ref position);
        if (count > (ulong)data.Length)
            throw new InvalidDataException("A PMTiles directory has more entries than bytes.");

        var ids = new ulong[count];
        var runs = new int[count];
        var lengths = new int[count];
        var entries = new Entry[count];

        ulong lastId = 0;
        for (ulong i = 0; i < count; i++)
            ids[i] = lastId += ReadVarint(data, ref position);
        for (ulong i = 0; i < count; i++)
            runs[i] = checked((int)ReadVarint(data, ref position));
        for (ulong i = 0; i < count; i++)
            lengths[i] = checked((int)ReadVarint(data, ref position));
        for (ulong i = 0; i < count; i++)
        {
            ulong value = ReadVarint(data, ref position);
            long offset = value == 0 && i > 0
                ? entries[i - 1].Offset + entries[i - 1].Length
                : checked((long)value - 1);
            entries[i] = new Entry(ids[i], offset, lengths[i], runs[i]);
        }

        return entries;
    }

    private Archive Current()
    {
        var info = new FileInfo(Path);
        lock (_gate)
        {
            if (!info.Exists || (info.Length == _archive.Length && info.LastWriteTimeUtc == _archive.WriteTime))
                return _archive;

            return _archive = Archive.Load(Path);
        }
    }

    private static SafeFileHandle OpenHandle(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static byte[] ReadExactly(SafeFileHandle file, long offset, int length)
    {
        var buffer = new byte[length];
        int total = 0;
        while (total < length)
        {
            int read = RandomAccess.Read(file, buffer.AsSpan(total), offset + total);
            if (read == 0)
                throw new InvalidDataException("A PMTiles archive ends before the data its directory points to.");
            total += read;
        }

        return buffer;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (position >= data.Length)
                throw new InvalidDataException("A PMTiles directory ends inside a number.");
            byte b = data[position++];
            value |= (ulong)(b & 0x7f) << shift;
            if (b < 0x80)
                return value;
        }

        throw new InvalidDataException("A PMTiles directory holds a number longer than 64 bits.");
    }

    /// <summary>An archive's header and metadata, and the directories read from it so far.</summary>
    private sealed class Archive
    {
        private readonly ConcurrentDictionary<(long Offset, int Length), Entry[]> _directories = new();
        private readonly byte _internalCompression;

        private Archive(byte internalCompression) => _internalCompression = internalCompression;

        public required long Length { get; init; }

        public required DateTime WriteTime { get; init; }

        public required long RootOffset { get; init; }

        public required int RootLength { get; init; }

        public required long LeavesOffset { get; init; }

        public required long DataOffset { get; init; }

        public required TileImageFormat Format { get; init; }

        public required int MinZoom { get; init; }

        public required int MaxZoom { get; init; }

        public required (double West, double South, double East, double North) Bounds { get; init; }

        public required (double Longitude, double Latitude, int Zoom) Center { get; init; }

        public required JsonObject Metadata { get; init; }

        public static Archive Load(string path)
        {
            var info = new FileInfo(path);
            using var file = OpenHandle(path);
            if (info.Length < PmTilesTileSink.HeaderLength)
                throw new InvalidDataException($"'{path}' is too short to be a PMTiles archive.");

            var header = ReadExactly(file, 0, PmTilesTileSink.HeaderLength);
            if (Encoding.ASCII.GetString(header, 0, 7) != "PMTiles")
                throw new InvalidDataException($"'{path}' is not a PMTiles archive.");
            if (header[7] != 3)
                throw new NotSupportedException($"'{path}' is a PMTiles version {header[7]} archive; only version 3 can be served.");

            byte internalCompression = header[97];
            if (internalCompression is not (1 or 2 or 3))
                throw new NotSupportedException($"'{path}' compresses its directories in a way that isn't supported (code {internalCompression}).");
            if (header[98] is not (0 or 1))
                throw new NotSupportedException($"'{path}' compresses its tiles (code {header[98]}); only uncompressed raster tiles can be served.");

            var format = header[99] switch
            {
                2 => TileImageFormat.Png,
                3 => TileImageFormat.Jpeg,
                4 => TileImageFormat.Webp,
                1 => throw new NotSupportedException($"'{path}' holds vector tiles; only PNG, JPEG and WebP raster tiles can be served."),
                var type => throw new NotSupportedException($"'{path}' holds tiles of type {type}; only PNG, JPEG and WebP raster tiles can be served."),
            };

            long metadataOffset = Offset(header, 24);
            int metadataLength = checked((int)Offset(header, 32));
            var metadata = new JsonObject();
            if (metadataLength > 0)
            {
                try
                {
                    if (JsonNode.Parse(Decompress(internalCompression, ReadExactly(file, metadataOffset, metadataLength))) is JsonObject json)
                        metadata = json;
                }
                catch (System.Text.Json.JsonException e)
                {
                    throw new InvalidDataException($"'{path}' has metadata that isn't valid JSON: {e.Message}", e);
                }
            }

            return new Archive(internalCompression)
            {
                Length = info.Length,
                WriteTime = info.LastWriteTimeUtc,
                RootOffset = Offset(header, 8),
                RootLength = checked((int)Offset(header, 16)),
                LeavesOffset = Offset(header, 40),
                DataOffset = Offset(header, 56),
                Format = format,
                MinZoom = header[100],
                MaxZoom = header[101],
                Bounds = (E7(header, 102), E7(header, 106), E7(header, 110), E7(header, 114)),
                Center = (E7(header, 119), E7(header, 123), header[118]),
                Metadata = metadata,
            };
        }

        public Entry[] Directory(SafeFileHandle file, long offset, int length) =>
            _directories.GetOrAdd((offset, length), key =>
                DeserializeDirectory(Decompress(_internalCompression, ReadExactly(file, key.Offset, key.Length))));

        private static byte[] Decompress(byte compression, byte[] data)
        {
            if (compression == 1)
                return data;

            using var input = new MemoryStream(data);
            using Stream stream = compression == 2
                ? new GZipStream(input, CompressionMode.Decompress)
                : new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }

        private static long Offset(byte[] header, int offset) =>
            checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(offset)));

        private static double E7(byte[] header, int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset)) / 1e7;
    }
}
