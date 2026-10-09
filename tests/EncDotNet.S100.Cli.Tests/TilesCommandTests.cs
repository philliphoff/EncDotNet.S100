using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// End-to-end and format tests for <c>s100 tiles</c> (issue #847): the XYZ
/// directory and PMTiles v3 containers, option validation and the tile-count
/// guard.
/// </summary>
public sealed class TilesCommandTests
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    [Fact]
    public void Xyz_output_writes_tiles_for_each_zoom_and_a_tilejson()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        var output = TempPath("xyz");
        try
        {
            int exit = CliApp.Build().Run(
                ["tiles", S57, "-o", output, "--min-zoom", "12", "--max-zoom", "13", "--parallel", "2"]);

            Assert.Equal(0, exit);
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(output, XyzDirectoryTileSink.TileJsonFileName)))!;
            Assert.Equal("3.0.0", (string?)json["tilejson"]);
            Assert.Equal(12, (int?)json["minzoom"]);
            Assert.Equal(13, (int?)json["maxzoom"]);
            Assert.Equal("{z}/{x}/{y}.png", (string?)json["tiles"]![0]);
            Assert.Equal("day", (string?)json["s100"]!["palette"]);

            foreach (var zoom in new[] { "12", "13" })
            {
                var tiles = Directory.GetFiles(Path.Combine(output, zoom), "*.png", SearchOption.AllDirectories);
                Assert.NotEmpty(tiles);
                using var bitmap = SKBitmap.Decode(tiles[0]);
                Assert.Equal(256, bitmap.Width);
                Assert.Equal(256, bitmap.Height);
            }

            // Somewhere the chart actually drew.
            Assert.Contains(
                Directory.GetFiles(Path.Combine(output, "13"), "*.png", SearchOption.AllDirectories),
                path =>
                {
                    using var bitmap = SKBitmap.Decode(path);
                    return bitmap.Pixels.Any(p => p.Alpha == 255);
                });
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Pmtiles_output_holds_the_same_tiles_as_the_xyz_output()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        var xyz = TempPath("xyz");
        var pmtiles = TempPath("archive") + ".pmtiles";
        try
        {
            string[] common = ["--min-zoom", "12", "--max-zoom", "14", "--tile-size", "512", "--format", "webp"];
            Assert.Equal(0, CliApp.Build().Run(["tiles", S57, "-o", xyz, .. common]));
            Assert.Equal(0, CliApp.Build().Run(["tiles", S57, "-o", pmtiles, .. common]));

            var archive = PmTilesArchive.Read(pmtiles);
            Assert.Equal(12, archive.MinZoom);
            Assert.Equal(14, archive.MaxZoom);
            Assert.Equal(4, archive.TileType); // webp
            Assert.True(archive.Clustered);
            Assert.False(File.Exists(pmtiles + ".spool"));

            var xyzTiles = Directory.GetFiles(xyz, "*.webp", SearchOption.AllDirectories);
            Assert.Equal(xyzTiles.Length, archive.AddressedTiles);
            foreach (var path in xyzTiles)
            {
                var parts = Path.GetRelativePath(xyz, path).Split(Path.DirectorySeparatorChar);
                int zoom = int.Parse(parts[0]);
                int x = int.Parse(parts[1]);
                int y = int.Parse(Path.GetFileNameWithoutExtension(parts[2]));
                var tile = archive.GetTile(zoom, x, y);
                Assert.NotNull(tile);
                using var bitmap = SKBitmap.Decode(tile);
                Assert.Equal(512, bitmap.Width);
            }

            var metadata = JsonNode.Parse(archive.Metadata)!;
            Assert.Equal("webp", (string?)metadata["format"]);
            Assert.Null(metadata["tiles"]);
        }
        finally
        {
            if (Directory.Exists(xyz))
                Directory.Delete(xyz, recursive: true);
            if (File.Exists(pmtiles))
                File.Delete(pmtiles);
        }
    }

    [Fact]
    public void Skip_empty_leaves_out_tiles_with_nothing_drawn()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        var all = TempPath("all");
        var some = TempPath("some");
        try
        {
            // The bbox reaches well past the cell, so some tiles are empty.
            string[] common = ["--min-zoom", "13", "--max-zoom", "13", "--bbox", "-70.45,41.2,-70.2,41.4"];
            Assert.Equal(0, CliApp.Build().Run(["tiles", S57, "-o", all, .. common]));
            Assert.Equal(0, CliApp.Build().Run(["tiles", S57, "-o", some, "--skip-empty", .. common]));

            int allCount = Directory.GetFiles(all, "*.png", SearchOption.AllDirectories).Length;
            int someCount = Directory.GetFiles(some, "*.png", SearchOption.AllDirectories).Length;
            Assert.InRange(someCount, 1, allCount - 1);
        }
        finally
        {
            foreach (var directory in new[] { all, some })
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Large_tile_sets_need_yes()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        var output = TempPath("big") + ".pmtiles";
        try
        {
            int exit = CliApp.Build().Run(["tiles", S57, "-o", output, "--min-zoom", "0", "--max-zoom", "22"]);

            Assert.Equal(2, exit);
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (File.Exists(output))
                File.Delete(output);
        }
    }

    [Theory]
    [InlineData("--tile-size", "300")]
    [InlineData("--container", "mbtiles")]
    [InlineData("--format", "gif")]
    [InlineData("--metatile", "0")]
    [InlineData("--min-zoom", "25")]
    public void Invalid_options_are_rejected(string option, string value)
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        var output = TempPath("invalid");
        int exit = CliApp.Build().Run(["tiles", S57, "-o", output, option, value]);

        Assert.NotEqual(0, exit);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void Output_is_required()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        Assert.NotEqual(0, CliApp.Build().Run(["tiles", S57]));
    }

    [Theory]
    [InlineData(0, 0, 0, 0UL)]
    [InlineData(1, 0, 0, 1UL)]
    [InlineData(1, 0, 1, 2UL)]
    [InlineData(1, 1, 1, 3UL)]
    [InlineData(1, 1, 0, 4UL)]
    [InlineData(2, 0, 0, 5UL)]
    public void Tile_ids_follow_the_pmtiles_hilbert_order(int zoom, int x, int y, ulong expected)
    {
        // Values from the PMTiles v3 specification's test vectors.
        Assert.Equal(expected, PmTilesTileSink.ZxyToTileId(zoom, x, y));
    }

    [Fact]
    public void Tile_ids_are_unique_and_dense_across_zoom_levels()
    {
        var ids = new HashSet<ulong>();
        for (int zoom = 0; zoom <= 4; zoom++)
        {
            int n = 1 << zoom;
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    Assert.True(ids.Add(PmTilesTileSink.ZxyToTileId(zoom, x, y)));
        }

        Assert.Equal(Enumerable.Range(0, ids.Count).Select(i => (ulong)i).ToHashSet(), ids);
    }

    [Fact]
    public void Large_directories_split_into_leaves_that_resolve_every_entry()
    {
        var entries = Enumerable.Range(0, 40_000)
            .Select(i => new PmTilesTileSink.Entry((ulong)(i * 3), i * 10L, 10, 1 + i % 2))
            .ToList();

        var (root, leaves) = PmTilesTileSink.BuildDirectories(entries);

        Assert.InRange(root.Length, 1, 16384 - PmTilesTileSink.HeaderLength);
        Assert.NotEmpty(leaves);

        var resolved = new List<PmTilesArchive.DirectoryEntry>();
        foreach (var rootEntry in PmTilesArchive.ParseDirectory(root))
        {
            Assert.Equal(0, rootEntry.RunLength);
            resolved.AddRange(PmTilesArchive.ParseDirectory(
                leaves.AsSpan((int)rootEntry.Offset, rootEntry.Length).ToArray()));
        }

        Assert.Equal(entries.Count, resolved.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].TileId, resolved[i].TileId);
            Assert.Equal(entries[i].Offset, resolved[i].Offset);
            Assert.Equal(entries[i].RunLength, resolved[i].RunLength);
        }
    }

    private static string TempPath(string label) =>
        Path.Combine(Path.GetTempPath(), $"s100-cli-tiles-{label}-{Guid.NewGuid():N}");

    /// <summary>A minimal, independent PMTiles v3 reader for the assertions.</summary>
    private sealed class PmTilesArchive
    {
        private readonly byte[] _bytes;

        private PmTilesArchive(byte[] bytes) => _bytes = bytes;

        public int MinZoom => _bytes[100];

        public int MaxZoom => _bytes[101];

        public int TileType => _bytes[99];

        public bool Clustered => _bytes[96] == 1;

        public long AddressedTiles => (long)UInt64(72);

        public string Metadata => Encoding.UTF8.GetString(Gunzip(Slice(24, 32)));

        public static PmTilesArchive Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("PMTiles"u8.ToArray(), bytes[..7]);
            Assert.Equal(3, bytes[7]);
            Assert.Equal(2, bytes[97]); // gzip directories
            return new PmTilesArchive(bytes);
        }

        public byte[]? GetTile(int zoom, int x, int y)
        {
            ulong id = PmTilesTileSink.ZxyToTileId(zoom, x, y);
            var directory = ParseDirectory(Slice(8, 16));
            for (int depth = 0; depth < 4; depth++)
            {
                var entry = directory.LastOrDefault(e => e.TileId <= id);
                if (entry is null)
                    return null;

                if (entry.RunLength == 0)
                {
                    long leaves = (long)UInt64(40);
                    directory = ParseDirectory(Bytes(leaves + entry.Offset, entry.Length));
                    continue;
                }

                if (id >= entry.TileId + (ulong)entry.RunLength)
                    return null;

                long data = (long)UInt64(56);
                return _bytes.AsSpan((int)(data + entry.Offset), entry.Length).ToArray();
            }

            return null;
        }

        public sealed record DirectoryEntry(ulong TileId, long Offset, int Length, int RunLength);

        public static List<DirectoryEntry> ParseDirectory(byte[] compressed)
        {
            var data = Gunzip(compressed);
            int position = 0;
            int count = (int)Varint(data, ref position);
            var ids = new ulong[count];
            ulong last = 0;
            for (int i = 0; i < count; i++)
                ids[i] = last += Varint(data, ref position);
            var runs = Enumerable.Range(0, count).Select(_ => (int)Varint(data, ref position)).ToArray();
            var lengths = Enumerable.Range(0, count).Select(_ => (int)Varint(data, ref position)).ToArray();
            var entries = new List<DirectoryEntry>(count);
            for (int i = 0; i < count; i++)
            {
                ulong raw = Varint(data, ref position);
                long offset = raw == 0 && i > 0
                    ? entries[i - 1].Offset + entries[i - 1].Length
                    : (long)raw - 1;
                entries.Add(new DirectoryEntry(ids[i], offset, lengths[i], runs[i]));
            }

            return entries;
        }

        /// <summary>The section whose offset and length the header holds at the given fields.</summary>
        private byte[] Slice(int offsetField, int lengthField) =>
            Bytes((long)UInt64(offsetField), (int)UInt64(lengthField));

        private byte[] Bytes(long offset, int length) => _bytes.AsSpan((int)offset, length).ToArray();

        private ulong UInt64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(offset));

        private static ulong Varint(byte[] data, ref int position)
        {
            ulong value = 0;
            for (int shift = 0; ; shift += 7)
            {
                byte b = data[position++];
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    return value;
            }
        }

        private static byte[] Gunzip(byte[] data)
        {
            using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
