using System.IO.Compression;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// A single land polygon of the bundled Natural Earth basemap, expressed in
/// <b>EPSG:3857 Web-Mercator metres</b>. The exterior shell and any interior
/// holes are consumed both by the vector/composite scene path (as
/// <see cref="AreaPaintOp"/>s) and by the coverage single-render path (drawn
/// directly through its centred-fit projection). Rings are closed (the last
/// point repeats the first).
/// </summary>
/// <param name="WorldShell">Exterior ring in EPSG:3857 metres.</param>
/// <param name="WorldHoles">Interior (hole) rings in EPSG:3857 metres.</param>
public sealed record LandPolygon(
    IReadOnlyList<(double X, double Y)> WorldShell,
    IReadOnlyList<IReadOnlyList<(double X, double Y)>> WorldHoles);

/// <summary>
/// One tile of a <see cref="LandLevel"/>: the land polygons clipped to a square
/// cell of the level's grid over the single-world EPSG:3857 square. Polygons
/// overlap neighbouring tiles slightly, so tiles drawn side by side never leave
/// an anti-aliasing seam.
/// </summary>
public sealed class LandTile
{
    internal LandTile(int column, int row, IReadOnlyList<LandPolygon> polygons)
    {
        Column = column;
        Row = row;
        Polygons = polygons;
    }

    /// <summary>Grid column, counted east from the antimeridian (−180°).</summary>
    public int Column { get; }

    /// <summary>Grid row, counted north from the southern Web-Mercator limit.</summary>
    public int Row { get; }

    /// <summary>The land polygons in this tile, in EPSG:3857 metres.</summary>
    public IReadOnlyList<LandPolygon> Polygons { get; }
}

/// <summary>
/// One level of detail of the bundled land basemap (issue #731): the land
/// simplified to <see cref="Tolerance"/> and cut into a
/// <see cref="GridSize"/>×<see cref="GridSize"/> grid of <see cref="LandTile"/>s,
/// so a renderer draws only the tiles in view. Tiles are decoded on first use.
/// </summary>
public sealed class LandLevel
{
    private readonly Lazy<LandTile?[]> _tiles;

    internal LandLevel(double tolerance, int gridSize, Func<LandTile?[]> decode)
    {
        Tolerance = tolerance;
        GridSize = gridSize;
        _tiles = new Lazy<LandTile?[]>(decode, isThreadSafe: true);
    }

    /// <summary>
    /// The simplification tolerance in EPSG:3857 metres: no point of the
    /// level's coastline is further than this from the source coastline.
    /// <c>0</c> for the full-resolution level.
    /// </summary>
    public double Tolerance { get; }

    /// <summary>The number of tiles along each side of the world square.</summary>
    public int GridSize { get; }

    /// <summary>The side of one tile, in EPSG:3857 metres.</summary>
    public double TileSize => WebMercator.Circumference / GridSize;

    /// <summary>
    /// The tiles that hold land and intersect the given EPSG:3857 rectangle.
    /// Only the single world (<c>±Circumference/2</c>) is covered; a caller
    /// that draws adjacent world copies offsets its query rectangle per copy.
    /// </summary>
    public IReadOnlyList<LandTile> GetTiles(double minX, double minY, double maxX, double maxY)
    {
        var tiles = _tiles.Value;
        double half = WebMercator.Circumference / 2.0;
        double size = TileSize;
        int c0 = Math.Max(0, (int)Math.Floor((minX + half) / size));
        int c1 = Math.Min(GridSize - 1, (int)Math.Floor((maxX + half) / size));
        int r0 = Math.Max(0, (int)Math.Floor((minY + half) / size));
        int r1 = Math.Min(GridSize - 1, (int)Math.Floor((maxY + half) / size));

        var result = new List<LandTile>();
        for (int row = r0; row <= r1; row++)
        {
            for (int column = c0; column <= c1; column++)
            {
                if (tiles[row * GridSize + column] is { } tile)
                    result.Add(tile);
            }
        }
        return result;
    }
}

/// <summary>
/// The bundled, offline, public-domain <b>Natural Earth 1:10m land</b> basemap
/// (issues #295, #411, #731) as a headless, Mapsui-free source of land geometry.
/// </summary>
/// <remarks>
/// <para>
/// This is the single source of land geometry for the headless render paths
/// and the interactive viewer. The embedded asset
/// (<c>Assets/Basemap/ne_10m_land.bin</c>, built by
/// <c>tools/BuildBasemap/BuildBasemap.cs</c>) holds the full-resolution
/// Natural Earth land plus coarser simplifications of it, each a
/// <see cref="LandLevel"/> already projected to EPSG:3857 and cut into tiles.
/// A renderer picks a level for its scale with <see cref="SelectLevel"/> and
/// draws only the tiles in view, so a harbour-scale view gets the full detail
/// while a world view draws a few tens of thousands of points.
/// </para>
/// <para>
/// The land is filled with a muted, parchment-like tone
/// (<see cref="LandFill"/> = <c>238,232,220</c>) — the same colour the viewer's
/// offline basemap uses — with no outline. Ops carry no scale-visibility limits
/// so the basemap draws at every fitted scale.
/// </para>
/// </remarks>
public static class NaturalEarthBasemap
{
    private const string LandResource =
        "EncDotNet.S100.Renderers.Skia.Assets.Basemap.ne_10m_land.bin";

    /// <summary>
    /// Land fill — a muted, parchment-like tone drawn over the chart's water
    /// back-colour. Matches the interactive viewer's offline basemap fill.
    /// </summary>
    public static readonly RgbaColor LandFill = new(238, 232, 220);

    private static readonly Lazy<IReadOnlyList<LandLevel>> LazyLevels =
        new(LoadLevels, isThreadSafe: true);

    /// <summary>
    /// The levels of detail, coarsest first; the last is the full-resolution
    /// Natural Earth land. Empty if the embedded asset is missing.
    /// </summary>
    public static IReadOnlyList<LandLevel> Levels => LazyLevels.Value;

    /// <summary>
    /// Picks the coarsest level whose <see cref="LandLevel.Tolerance"/> is no
    /// more than one pixel, so the simplified coastline is never off by more
    /// than a pixel; below the finest tolerance, the full-resolution level.
    /// </summary>
    /// <param name="metresPerPixel">EPSG:3857 metres per output pixel.</param>
    public static LandLevel? SelectLevel(double metresPerPixel)
    {
        var levels = Levels;
        foreach (var level in levels)
        {
            if (level.Tolerance <= metresPerPixel)
                return level;
        }
        return levels.Count > 0 ? levels[^1] : null;
    }

    /// <summary>
    /// The land polygons for an EPSG:3857 rectangle drawn at
    /// <paramref name="metresPerPixel"/>: those of every tile of the
    /// <see cref="SelectLevel"/> level that intersects the rectangle.
    /// </summary>
    public static IReadOnlyList<LandPolygon> GetLandPolygons(
        double minX, double minY, double maxX, double maxY, double metresPerPixel)
    {
        var level = SelectLevel(metresPerPixel);
        if (level is null)
            return Array.Empty<LandPolygon>();

        var polygons = new List<LandPolygon>();
        foreach (var tile in level.GetTiles(minX, minY, maxX, maxY))
            polygons.AddRange(tile.Polygons);
        return polygons;
    }

    /// <summary>
    /// The land in <paramref name="viewport"/>, at a level of detail matching
    /// its pixel size, lowered to a <see cref="VectorScene"/> of
    /// parchment-filled <see cref="AreaPaintOp"/>s, suitable for drawing beneath
    /// chart layers via <c>SkiaDisplayListRenderer.RenderOnto</c> or a
    /// <c>VectorCompositeLayer</c> against that same viewport.
    /// </summary>
    public static VectorScene GetLandScene(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        double metresPerPixel = (maxX - minX) / Math.Max(1, viewport.WidthPixels);

        var polygons = GetLandPolygons(minX, minY, maxX, maxY, metresPerPixel);
        var ops = new List<PaintOp>(polygons.Count);
        foreach (var polygon in polygons)
        {
            ops.Add(new AreaPaintOp
            {
                FeatureReference = "basemap:ne_10m_land",
                WorldShell = polygon.WorldShell,
                WorldHoles = polygon.WorldHoles,
                Fill = LandFill,
                OutlineColor = RgbaColor.Transparent,
                OutlineWidthPx = 0,
            });
        }

        return new VectorScene(ops);
    }

    private static IReadOnlyList<LandLevel> LoadLevels()
    {
        using var stream = typeof(NaturalEarthBasemap).Assembly.GetManifestResourceStream(LandResource);
        if (stream is null)
            return Array.Empty<LandLevel>();

        var data = new byte[stream.Length];
        stream.ReadExactly(data);

        var reader = new ByteReader(data);
        if (!data.AsSpan(0, 6).SequenceEqual("S1LAND"u8) || data[6] != 1)
            throw new InvalidDataException("Unrecognised land basemap asset.");
        reader.Position = 7;

        int count = (int)reader.ReadVarint();
        var headers = new (double Tolerance, int GridSize, int Length)[count];
        for (int i = 0; i < count; i++)
            headers[i] = (reader.ReadDouble(), (int)reader.ReadVarint(), (int)reader.ReadVarint());

        var levels = new LandLevel[count];
        int offset = reader.Position;
        for (int i = 0; i < count; i++)
        {
            var (tolerance, gridSize, length) = headers[i];
            var block = new ReadOnlyMemory<byte>(data, offset, length);
            levels[i] = new LandLevel(tolerance, gridSize, () => DecodeLevel(block, gridSize));
            offset += length;
        }
        return levels;
    }

    private static LandTile?[] DecodeLevel(ReadOnlyMemory<byte> block, int gridSize)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(new MemoryStream(block.ToArray()), CompressionMode.Decompress))
            brotli.CopyTo(output);

        var reader = new ByteReader(output.GetBuffer(), (int)output.Length);
        var tiles = new LandTile?[gridSize * gridSize];
        int tileCount = (int)reader.ReadVarint();
        for (int t = 0; t < tileCount; t++)
        {
            int column = (int)reader.ReadVarint();
            int row = (int)reader.ReadVarint();
            int polygonCount = (int)reader.ReadVarint();
            var polygons = new LandPolygon[polygonCount];
            for (int p = 0; p < polygonCount; p++)
            {
                int ringCount = (int)reader.ReadVarint();
                var shell = ReadRing(ref reader);
                var holes = new IReadOnlyList<(double X, double Y)>[ringCount - 1];
                for (int h = 0; h < holes.Length; h++)
                    holes[h] = ReadRing(ref reader);
                polygons[p] = new LandPolygon(shell, holes);
            }
            tiles[row * gridSize + column] = new LandTile(column, row, polygons);
        }
        return tiles;
    }

    private static (double X, double Y)[] ReadRing(ref ByteReader reader)
    {
        // Stored open; close it so consumers see a proper ring.
        int count = (int)reader.ReadVarint();
        var points = new (double X, double Y)[count + 1];
        long x = 0, y = 0;
        for (int i = 0; i < count; i++)
        {
            x += reader.ReadZigzag();
            y += reader.ReadZigzag();
            points[i] = (x, y);
        }
        points[count] = points[0];
        return points;
    }

    private struct ByteReader
    {
        private readonly byte[] _data;
        private readonly int _length;

        public ByteReader(byte[] data, int length = -1)
        {
            _data = data;
            _length = length < 0 ? data.Length : length;
            Position = 0;
        }

        public int Position { get; set; }

        public ulong ReadVarint()
        {
            ulong value = 0;
            int shift = 0;
            while (true)
            {
                if (Position >= _length)
                    throw new InvalidDataException("Truncated land basemap asset.");
                byte b = _data[Position++];
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    return value;
                shift += 7;
            }
        }

        public long ReadZigzag()
        {
            ulong value = ReadVarint();
            return (long)(value >> 1) ^ -(long)(value & 1);
        }

        public double ReadDouble()
        {
            double value = BitConverter.ToDouble(_data, Position);
            Position += sizeof(double);
            return value;
        }
    }
}
