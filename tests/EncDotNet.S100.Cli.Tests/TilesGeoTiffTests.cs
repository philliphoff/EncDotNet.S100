using System.Buffers.Binary;
using System.IO.Compression;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>GeoTIFF output: <c>tiles export -o x.tif</c>.</summary>
public sealed class TilesGeoTiffTests : IDisposable
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Directory.CreateTempSubdirectory("s100-geotiff-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The EPSG:3857 area of tiles <paramref name="x0"/>…<paramref name="x1"/>, <paramref name="y0"/>…<paramref name="y1"/> at <paramref name="zoom"/>, shrunk a little.</summary>
    private static TileArea Area(int zoom, int x0, int y0, int x1, int y1)
    {
        var (minX, _, _, maxY) = XyzTileGrid.TileWorldBounds(zoom, x0, y0);
        var (_, minY, maxX, _) = XyzTileGrid.TileWorldBounds(zoom, x1, y1);
        return new TileArea(minX + 1, minY + 1, maxX - 1, maxY - 1);
    }

    [Fact]
    public void The_grid_snaps_to_whole_tiles_of_the_smallest_overview()
    {
        // Tiles 5…9 × 3…4 at zoom 10: 5 across, so 3 overviews down to zoom 7.
        var grid = GeoTiffGrid.For(Area(10, 5, 3, 9, 4), minZoom: 0, maxZoom: 10)!;
        Assert.Equal(3, grid.Levels);
        Assert.Equal(7, grid.MinZoom);
        Assert.Equal((0, 0, 16, 8), (grid.FirstColumn, grid.FirstRow, grid.Columns, grid.Rows));
        Assert.Equal((0, 0, 2, 1), grid.Level(7));
        Assert.Equal((0, 0, 8, 4), grid.Level(9));

        // The minimum zoom stops the overviews first.
        var shallow = GeoTiffGrid.For(Area(10, 5, 3, 9, 4), minZoom: 9, maxZoom: 10)!;
        Assert.Equal(1, shallow.Levels);
        Assert.Equal((4, 2, 6, 4), (shallow.FirstColumn, shallow.FirstRow, shallow.Columns, shallow.Rows));

        // One tile needs no overviews.
        var single = GeoTiffGrid.For(Area(12, 100, 200, 100, 200), minZoom: 0, maxZoom: 12)!;
        Assert.Equal((0, 100, 200, 1, 1), (single.Levels, single.FirstColumn, single.FirstRow, single.Columns, single.Rows));

        // Across the antimeridian there's no single image.
        var wrapped = new TileArea(XyzTileGrid.Extent - 1000, 0, XyzTileGrid.Extent + 1000, 1000);
        Assert.Null(GeoTiffGrid.For(wrapped, 0, 10));
    }

    private static byte[] Png(SKColor colour, int size = 256)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(colour);
        return bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static TileSetMetadata Metadata(TileImageFormat format, int minZoom, int maxZoom) => new()
    {
        Name = "Test",
        Description = "For tests",
        Format = format,
        MinZoom = minZoom,
        MaxZoom = maxZoom,
        Bounds = (0, 0, 1, 1),
        TilePixelSize = 256,
        Settings = new Dictionary<string, string>(),
    };

    [Fact]
    public void Tiles_become_a_cog_with_overviews_and_georeferencing()
    {
        // Zoom 3 tiles 4…5 × 2…3, one overview at zoom 2 (tiles 2 × 1).
        var grid = GeoTiffGrid.For(Area(3, 4, 2, 5, 3), minZoom: 2, maxZoom: 3)!;
        Assert.Equal((1, 4, 2, 2, 2), (grid.Levels, grid.FirstColumn, grid.FirstRow, grid.Columns, grid.Rows));

        var path = Path.Combine(_root, "rgba.tif");
        var red = new SKColor(200, 10, 20, 255);
        var green = new SKColor(0, 150, 0, 128);
        using (var sink = new GeoTiffTileSink(path, grid, TileImageFormat.Png))
        {
            sink.Write(3, 5, 3, Png(red));
            sink.Write(2, 2, 1, Png(green));
            sink.Write(3, 9, 9, Png(red)); // outside the image: dropped
            sink.Write(1, 1, 0, Png(red)); // below the smallest overview: dropped

            // Still being written: nothing at the output path.
            Assert.False(File.Exists(path));
            sink.Complete(Metadata(TileImageFormat.Png, 2, 3));
        }

        Assert.False(File.Exists(TileSetFile.PartialPath(path)));
        var tiff = Tiff.Read(path);
        Assert.False(tiff.Big);
        Assert.Equal(2, tiff.Directories.Count);

        var full = tiff.Directories[0];
        Assert.Equal(512, full.Single(Tag.ImageWidth));
        Assert.Equal(512, full.Single(Tag.ImageLength));
        Assert.Equal(8, full.Single(Tag.Compression));
        Assert.Equal([8L, 8, 8, 8], full.Values[Tag.BitsPerSample]);
        Assert.Equal(2, full.Single(Tag.ExtraSamples));
        Assert.False(full.Values.ContainsKey(Tag.NewSubfileType));

        // Georeferenced at the top-left corner of tile 4/2 at zoom 3, in EPSG:3857.
        var (originX, _, _, originY) = XyzTileGrid.TileWorldBounds(3, 4, 2);
        Assert.Equal(XyzTileGrid.TileWorldSize(3) / 256, full.Doubles[Tag.ModelPixelScale][0], 6);
        Assert.Equal(originX, full.Doubles[Tag.ModelTiepoint][3], 6);
        Assert.Equal(originY, full.Doubles[Tag.ModelTiepoint][4], 6);
        Assert.Contains(3857L, full.Values[Tag.GeoKeyDirectory]);

        // Tile (1, 1) of the full image is XYZ 5/3; the others are sparse.
        Assert.Equal([0L, 0, 0], full.Values[Tag.TileByteCounts][..3]);
        var pixels = Inflate(tiff.Tile(full, 3));
        Assert.Equal(256 * 256 * 4, pixels.Length);
        Assert.Equal(new byte[] { 200, 10, 20, 255 }, pixels[..4]);

        var overview = tiff.Directories[1];
        Assert.Equal(1, overview.Single(Tag.NewSubfileType));
        Assert.Equal(256, overview.Single(Tag.ImageWidth));
        Assert.False(overview.Values.ContainsKey(Tag.ModelTiepoint));
        Assert.Equal(new byte[] { 0, 150, 0, 128 }, Inflate(tiff.Tile(overview, 0))[..4]);

        // COG order: every IFD before the data, the overview's data before the full image's.
        long firstData = full.Values[Tag.TileOffsets][3];
        Assert.True(overview.Values[Tag.TileOffsets][0] < firstData);
        Assert.True(tiff.DirectoryEnd <= overview.Values[Tag.TileOffsets][0]);
    }

    [Fact]
    public void Jpeg_tiles_are_stored_as_they_are()
    {
        var grid = GeoTiffGrid.For(Area(5, 3, 3, 3, 3), minZoom: 0, maxZoom: 5)!;
        var path = Path.Combine(_root, "jpeg.tif");
        byte[] jpeg;
        using (var bitmap = new SKBitmap(256, 256))
        {
            bitmap.Erase(SKColors.Teal);
            jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 80).ToArray();
        }

        using (var sink = new GeoTiffTileSink(path, grid, TileImageFormat.Jpeg))
        {
            sink.Write(5, 3, 3, jpeg);
            sink.Complete(Metadata(TileImageFormat.Jpeg, 5, 5));
        }

        var tiff = Tiff.Read(path);
        var full = Assert.Single(tiff.Directories);
        Assert.Equal(7, full.Single(Tag.Compression));
        Assert.Equal(6, full.Single(Tag.Photometric));
        Assert.Equal(3, full.Single(Tag.SamplesPerPixel));
        Assert.Equal(jpeg, tiff.Tile(full, 0));
    }

    [Fact]
    public void An_unfinished_export_leaves_nothing_behind()
    {
        var path = Path.Combine(_root, "unfinished.tif");
        File.WriteAllText(path, "old");
        var grid = GeoTiffGrid.For(Area(5, 3, 3, 3, 3), 0, 5)!;
        using (var sink = new GeoTiffTileSink(path, grid, TileImageFormat.Png))
            sink.Write(5, 3, 3, Png(SKColors.Red));

        Assert.Equal("old", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_root));
    }

    [Fact]
    public void A_bigtiff_layout_reads_back()
    {
        var directory = new TiffDirectory();
        directory.Set(TiffTag.ImageWidth, TiffType.Long, 256);
        directory.Set(TiffTag.BitsPerSample, TiffType.Short, [8, 8, 8, 8]);
        directory.Set(TiffTag.TileOffsets, [5_000_000_000L, 6_000_000_000L]);
        directory.Set(TiffTag.ModelPixelScale, [1.5, 1.5, 0.0]);
        var layout = new TiffLayout(big: true);

        var path = Path.Combine(_root, "big.tif");
        using (var stream = File.Create(path))
        {
            layout.WriteHeaderAndDirectories(stream, [directory]);
            Assert.Equal(layout.Measure([directory]), stream.Position);
        }

        var tiff = Tiff.Read(path);
        Assert.True(tiff.Big);
        var read = Assert.Single(tiff.Directories);
        Assert.Equal(256, read.Single(Tag.ImageWidth));
        Assert.Equal([8L, 8, 8, 8], read.Values[Tag.BitsPerSample]);
        Assert.Equal([5_000_000_000L, 6_000_000_000L], read.Values[Tag.TileOffsets]);
        Assert.Equal([1.5, 1.5, 0.0], read.Doubles[Tag.ModelPixelScale]);
    }

    [Theory]
    [InlineData("out.tif", null)]
    [InlineData("out.TIFF", null)]
    [InlineData("out", "geotiff")]
    [InlineData("out", "cog")]
    public void Container_resolves_to_geotiff(string output, string? option)
    {
        Assert.True(Commands.TilesExportCommand.TryResolveContainer(option, output, out var container));
        Assert.Equal(TileContainer.GeoTiff, container);
    }

    [Fact]
    public void An_exported_chart_holds_the_rendered_tiles()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var xyz = Path.Combine(_root, "xyz");
        var tif = Path.Combine(_root, "chart.tif");
        string[] common = ["--min-zoom", "11", "--max-zoom", "13"];
        Assert.Equal(0, CliApp.Build().Run(["tiles", "export", S57, "-o", xyz, .. common], TestContext.Current.CancellationToken));
        Assert.Equal(0, CliApp.Build().Run(["tiles", "export", S57, "-o", tif, .. common], TestContext.Current.CancellationToken));

        var tiff = Tiff.Read(tif);
        var full = tiff.Directories[0];
        var origin = (X: full.Doubles[Tag.ModelTiepoint][3], Y: full.Doubles[Tag.ModelTiepoint][4]);
        double size = XyzTileGrid.TileWorldSize(13);
        int firstColumn = (int)Math.Round((origin.X + XyzTileGrid.Extent) / size);
        int firstRow = (int)Math.Round((XyzTileGrid.Extent - origin.Y) / size);
        int across = (int)(full.Single(Tag.ImageWidth) / 256);

        // Every zoom-13 tile is in the GeoTIFF, decoded to the same pixels.
        var files = Directory.GetFiles(Path.Combine(xyz, "13"), "*.png", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            int x = int.Parse(Path.GetFileName(Path.GetDirectoryName(file)!));
            int y = int.Parse(Path.GetFileNameWithoutExtension(file));
            var pixels = Inflate(tiff.Tile(full, (y - firstRow) * across + (x - firstColumn)));
            Assert.Equal(GeoTiffTileSink.DecodeRgba(File.ReadAllBytes(file)), pixels);
        }
    }

    private static byte[] Inflate(byte[] data)
    {
        using var zlib = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static class Tag
    {
        public const int NewSubfileType = 254, ImageWidth = 256, ImageLength = 257, BitsPerSample = 258,
            Compression = 259, Photometric = 262, SamplesPerPixel = 277, TileOffsets = 324, TileByteCounts = 325,
            ExtraSamples = 338, ModelPixelScale = 33550, ModelTiepoint = 33922, GeoKeyDirectory = 34735;
    }

    /// <summary>A minimal little-endian TIFF/BigTIFF reader, independent of the writer.</summary>
    private sealed record Tiff(byte[] Bytes, bool Big, List<Tiff.Ifd> Directories, long DirectoryEnd)
    {
        public sealed record Ifd(Dictionary<int, long[]> Values, Dictionary<int, double[]> Doubles)
        {
            public long Single(int tag) => Assert.Single(Values[tag]);
        }

        public byte[] Tile(Ifd ifd, int index) =>
            Bytes.AsSpan((int)ifd.Values[Tag.TileOffsets][index], (int)ifd.Values[Tag.TileByteCounts][index]).ToArray();

        public static Tiff Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var span = bytes.AsSpan();
            Assert.Equal((byte)'I', span[0]);
            int version = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
            bool big = version == 43;
            Assert.True(version is 42 or 43);
            long next = big ? (long)BinaryPrimitives.ReadUInt64LittleEndian(span[8..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            var directories = new List<Ifd>();
            long end = 0;
            while (next != 0)
            {
                int at = (int)next;
                long count = big ? (long)BinaryPrimitives.ReadUInt64LittleEndian(span[at..]) : BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
                at += big ? 8 : 2;
                var values = new Dictionary<int, long[]>();
                var doubles = new Dictionary<int, double[]>();
                for (int i = 0; i < count; i++, at += big ? 20 : 12)
                {
                    int tag = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
                    int type = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 2)..]);
                    long n = big ? (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(at + 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[(at + 4)..]);
                    int valueAt = at + (big ? 12 : 8);
                    int size = type switch { 3 => 2, 4 => 4, 12 or 16 => 8, _ => throw new InvalidDataException($"type {type}") };
                    int inline = big ? 8 : 4;
                    int data = n * size <= inline
                        ? valueAt
                        : (int)(big ? (long)BinaryPrimitives.ReadUInt64LittleEndian(span[valueAt..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[valueAt..]));
                    end = Math.Max(end, n * size <= inline ? 0 : data + n * size);
                    if (type == 12)
                    {
                        var read = new double[n];
                        for (int k = 0; k < n; k++)
                            read[k] = BinaryPrimitives.ReadDoubleLittleEndian(span[(data + k * 8)..]);
                        doubles[tag] = read;
                        continue;
                    }

                    var list = new long[n];
                    for (int k = 0; k < n; k++)
                    {
                        list[k] = type switch
                        {
                            3 => BinaryPrimitives.ReadUInt16LittleEndian(span[(data + k * 2)..]),
                            4 => BinaryPrimitives.ReadUInt32LittleEndian(span[(data + k * 4)..]),
                            _ => (long)BinaryPrimitives.ReadUInt64LittleEndian(span[(data + k * 8)..]),
                        };
                    }

                    values[tag] = list;
                }

                next = big ? (long)BinaryPrimitives.ReadUInt64LittleEndian(span[at..]) : BinaryPrimitives.ReadUInt32LittleEndian(span[at..]);
                end = Math.Max(end, at + (big ? 8 : 4));
                directories.Add(new Ifd(values, doubles));
            }

            return new Tiff(bytes, big, directories, end);
        }
    }
}
