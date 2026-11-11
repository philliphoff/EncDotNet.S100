using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Writes tiles into a single PMTiles v3 archive
/// (https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md).
/// </summary>
/// <remarks>
/// Tiles arrive in any order and are spooled to a temporary file next to the
/// output; identical tiles are stored once. <see cref="Complete"/> then writes
/// the archive with the tile contents in tile-ID order (a clustered archive),
/// gzip-compressed directories and metadata, and leaf directories when the root
/// directory would not fit in the first 16 KiB.
/// </remarks>
internal sealed class PmTilesTileSink : ITileSink
{
    internal const int HeaderLength = 127;
    private const int MaxRootDirectoryLength = 16384 - HeaderLength;

    private readonly string _outputPath;
    private readonly string _spoolPath;
    private readonly FileStream _spool;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (long Offset, int Length)> _contents = new(StringComparer.Ordinal);
    private readonly List<(ulong TileId, long Offset, int Length)> _tiles = [];
    private bool _completed;

    public PmTilesTileSink(string outputPath)
    {
        _outputPath = Path.GetFullPath(outputPath);
        _spoolPath = _outputPath + ".spool";
        _spool = new FileStream(_spoolPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16);
    }

    public void Write(int zoom, int x, int y, byte[] data)
    {
        ulong tileId = ZxyToTileId(zoom, x, y);
        string hash = Convert.ToHexString(SHA256.HashData(data));

        lock (_gate)
        {
            if (!_contents.TryGetValue(hash, out var content))
            {
                content = (_spool.Length, data.Length);
                _spool.Seek(0, SeekOrigin.End);
                _spool.Write(data);
                _contents[hash] = content;
            }

            _tiles.Add((tileId, content.Offset, content.Length));
        }
    }

    public void Complete(TileSetMetadata metadata)
    {
        lock (_gate)
        {
            _tiles.Sort((a, b) => a.TileId.CompareTo(b.TileId));

            // Lay the contents out in tile-ID order (clustered), each once, and
            // fold consecutive tiles with the same content into runs.
            var newOffsets = new Dictionary<long, long>();
            var entries = new List<Entry>();
            long dataLength = 0;
            foreach (var (tileId, spoolOffset, length) in _tiles)
            {
                if (!newOffsets.TryGetValue(spoolOffset, out long offset))
                {
                    offset = dataLength;
                    newOffsets[spoolOffset] = offset;
                    dataLength += length;
                }

                if (entries.Count > 0
                    && entries[^1] is var last
                    && last.TileId + (ulong)last.RunLength == tileId
                    && last.Offset == offset)
                {
                    entries[^1] = last with { RunLength = last.RunLength + 1 };
                }
                else
                {
                    entries.Add(new Entry(tileId, offset, length, 1));
                }
            }

            var (root, leaves) = BuildDirectories(entries);
            byte[] metadataBytes = Gzip(Encoding.UTF8.GetBytes(
                metadata.ToTileJson(tilesUrl: null).ToJsonString(TileSetMetadata.JsonOptions)));

            long rootOffset = HeaderLength;
            long metadataOffset = rootOffset + root.Length;
            long leavesOffset = metadataOffset + metadataBytes.Length;
            long dataOffset = leavesOffset + leaves.Length;

            var header = new byte[HeaderLength];
            Encoding.ASCII.GetBytes("PMTiles").CopyTo(header, 0);
            header[7] = 3;
            WriteUInt64(header, 8, rootOffset);
            WriteUInt64(header, 16, root.Length);
            WriteUInt64(header, 24, metadataOffset);
            WriteUInt64(header, 32, metadataBytes.Length);
            WriteUInt64(header, 40, leavesOffset);
            WriteUInt64(header, 48, leaves.Length);
            WriteUInt64(header, 56, dataOffset);
            WriteUInt64(header, 64, dataLength);
            WriteUInt64(header, 72, _tiles.Count);
            WriteUInt64(header, 80, entries.Count);
            WriteUInt64(header, 88, newOffsets.Count);
            header[96] = 1; // clustered
            header[97] = 2; // internal compression: gzip
            header[98] = 1; // tile compression: none
            header[99] = metadata.Format switch
            {
                TileImageFormat.Jpeg => 3,
                TileImageFormat.Webp => 4,
                _ => 2,
            };
            header[100] = (byte)metadata.MinZoom;
            header[101] = (byte)metadata.MaxZoom;
            WriteE7(header, 102, metadata.Bounds.West);
            WriteE7(header, 106, metadata.Bounds.South);
            WriteE7(header, 110, metadata.Bounds.East);
            WriteE7(header, 114, metadata.Bounds.North);
            header[118] = (byte)metadata.Center.Zoom;
            WriteE7(header, 119, metadata.Center.Longitude);
            WriteE7(header, 123, metadata.Center.Latitude);

            using (var output = new FileStream(_outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                output.Write(header);
                output.Write(root);
                output.Write(metadataBytes);
                output.Write(leaves);

                var copied = new HashSet<long>();
                var buffer = new byte[64 * 1024];
                foreach (var (_, spoolOffset, length) in _tiles)
                {
                    if (!copied.Add(spoolOffset))
                        continue;

                    if (length > buffer.Length)
                        buffer = new byte[length];
                    _spool.Seek(spoolOffset, SeekOrigin.Begin);
                    _spool.ReadExactly(buffer, 0, length);
                    output.Write(buffer, 0, length);
                }
            }

            _completed = true;
        }
    }

    public void Dispose()
    {
        _spool.Dispose();
        try
        {
            File.Delete(_spoolPath);
        }
        catch (IOException)
        {
        }

        if (!_completed)
        {
            // An unfinished archive is unreadable; do not leave a stale one behind.
            try
            {
                File.Delete(_outputPath);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// The PMTiles tile ID of a tile: the tiles of all lower zoom levels, plus
    /// the tile's position along the zoom level's Hilbert curve.
    /// </summary>
    internal static ulong ZxyToTileId(int zoom, int x, int y)
    {
        if (zoom is < 0 or > 26)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        ulong n = 1UL << zoom;
        if ((ulong)x >= n || (ulong)y >= n)
            throw new ArgumentOutOfRangeException(nameof(x), "The tile lies outside its zoom level.");

        ulong acc = ((1UL << (2 * zoom)) - 1) / 3;
        ulong tx = (ulong)x;
        ulong ty = (ulong)y;
        ulong d = 0;
        for (ulong s = n / 2; s > 0; s /= 2)
        {
            ulong rx = (tx & s) > 0 ? 1UL : 0UL;
            ulong ry = (ty & s) > 0 ? 1UL : 0UL;
            d += s * s * ((3 * rx) ^ ry);
            if (ry == 0)
            {
                if (rx == 1)
                {
                    tx = s - 1 - tx;
                    ty = s - 1 - ty;
                }

                (tx, ty) = (ty, tx);
            }
        }

        return acc + d;
    }

    internal readonly record struct Entry(ulong TileId, long Offset, int Length, int RunLength);

    /// <summary>
    /// Serialises the directory entries, as a root directory alone when it fits
    /// in the first 16 KiB, or else as a root of leaf directories.
    /// </summary>
    internal static (byte[] Root, byte[] Leaves) BuildDirectories(IReadOnlyList<Entry> entries)
    {
        if (entries.Count < 16384)
        {
            var root = SerializeDirectory(entries);
            if (root.Length <= MaxRootDirectoryLength)
                return (root, []);
        }

        double leafSize = Math.Max(4096, entries.Count / 3500.0);
        while (true)
        {
            var rootEntries = new List<Entry>();
            using var leaves = new MemoryStream();
            for (int i = 0; i < entries.Count; i += (int)leafSize)
            {
                int count = Math.Min((int)leafSize, entries.Count - i);
                var leaf = SerializeDirectory(entries.Skip(i).Take(count).ToList());
                rootEntries.Add(new Entry(entries[i].TileId, leaves.Length, leaf.Length, 0));
                leaves.Write(leaf);
            }

            var root = SerializeDirectory(rootEntries);
            if (root.Length <= MaxRootDirectoryLength)
                return (root, leaves.ToArray());

            leafSize *= 1.2;
        }
    }

    internal static byte[] SerializeDirectory(IReadOnlyList<Entry> entries)
    {
        using var buffer = new MemoryStream();
        WriteVarint(buffer, (ulong)entries.Count);

        ulong lastId = 0;
        foreach (var entry in entries)
        {
            WriteVarint(buffer, entry.TileId - lastId);
            lastId = entry.TileId;
        }

        foreach (var entry in entries)
            WriteVarint(buffer, (ulong)entry.RunLength);

        foreach (var entry in entries)
            WriteVarint(buffer, (ulong)entry.Length);

        for (int i = 0; i < entries.Count; i++)
        {
            // 0 means "straight after the previous entry's content".
            if (i > 0 && entries[i].Offset == entries[i - 1].Offset + entries[i - 1].Length)
                WriteVarint(buffer, 0);
            else
                WriteVarint(buffer, (ulong)entries[i].Offset + 1);
        }

        return Gzip(buffer.ToArray());
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(data);
        return output.ToArray();
    }

    private static void WriteUInt64(byte[] header, int offset, long value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(offset), (ulong)value);

    private static void WriteE7(byte[] header, int offset, double degrees) =>
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset), (int)Math.Round(degrees * 1e7));
}
