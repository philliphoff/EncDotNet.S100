#:package NetTopologySuite
#:project ../../src/EncDotNet.S100.Rendering.Scene/EncDotNet.S100.Rendering.Scene.csproj

// Builds the bundled offline land basemap (issue #731) from the Natural Earth
// 1:10m "land" GeoJSON:
//
//   ogr2ogr -f GeoJSON ne_10m_land.geojson ne_10m_land.shp
//   dotnet run tools/BuildBasemap/BuildBasemap.cs ne_10m_land.geojson \
//       src/EncDotNet.S100.Renderers.Skia/Assets/Basemap/ne_10m_land.bin
//
// The output holds several levels of detail. Each level is simplified to a
// tolerance in EPSG:3857 metres and cut into a square grid of tiles, so a
// renderer draws only the tiles in view, at a detail that matches its scale.
// Tiles are clipped with a small overlap so neighbouring tiles' anti-aliased
// edges never leave a seam. The format is read by NaturalEarthBasemap; keep
// the two in step.
//
// Format (little-endian; "varint" = LEB128, "zigzag" = signed LEB128 zigzag):
//   "S1LAND" (6 bytes), version byte (1), varint levelCount
//   per level: double tolerance (m), varint gridSize, varint blockLength
//   then each level's Brotli-compressed block, in level order:
//     varint tileCount
//     per tile: varint column, varint row, varint polygonCount
//       per polygon: varint ringCount (shell first, then holes)
//         per ring: varint pointCount, then pointCount × (zigzag dx, zigzag dy)
//         in whole metres, each relative to the previous point (the first to
//         the origin). Rings are stored open; the reader closes them.

using System.IO.Compression;
using System.Text.Json;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;
using NetTopologySuite.Simplify;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: dotnet run BuildBasemap.cs <ne_10m_land.geojson> <output.bin>");
    return 1;
}

// Coarsest first. A renderer picks the coarsest level whose tolerance is no
// more than one pixel, so the error never exceeds a pixel. Grid sizes keep a
// viewport at the level's scales to a handful of tiles.
var levels = new (double Tolerance, int GridSize)[]
{
    (8000, 1),
    (2000, 8),
    (500, 32),
    (0, 64),
};

var factory = new GeometryFactory(new PrecisionModel(), 3857);
var source = LoadProjected(args[0], factory);
Console.WriteLine($"source: {source.Count} polygons, {source.Sum(p => p.NumPoints)} points");

var blocks = new List<byte[]>();
foreach (var (tolerance, gridSize) in levels)
{
    var block = BuildLevel(source, tolerance, gridSize, factory, out int tiles, out int points);
    var compressed = Compress(block);
    blocks.Add(compressed);
    Console.WriteLine(
        $"level tol={tolerance} m grid={gridSize}: {tiles} tiles, {points} points, " +
        $"{block.Length / 1024} KiB raw, {compressed.Length / 1024} KiB compressed");
}

using (var output = File.Create(args[1]))
using (var writer = new BinaryWriter(output))
{
    writer.Write("S1LAND"u8);
    writer.Write((byte)1);
    WriteVarint(writer, (uint)levels.Length);
    for (int i = 0; i < levels.Length; i++)
    {
        writer.Write(levels[i].Tolerance);
        WriteVarint(writer, (uint)levels[i].GridSize);
        WriteVarint(writer, (uint)blocks[i].Length);
    }
    foreach (var block in blocks)
        writer.Write(block);
}

Console.WriteLine($"wrote {args[1]} ({new FileInfo(args[1]).Length / 1024} KiB)");
return 0;

static List<Polygon> LoadProjected(string path, GeometryFactory factory)
{
    var polygons = new List<Polygon>();
    using var stream = File.OpenRead(path);
    using var doc = JsonDocument.Parse(stream);
    foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
    {
        var geometry = feature.GetProperty("geometry");
        var coordinates = geometry.GetProperty("coordinates");
        switch (geometry.GetProperty("type").GetString())
        {
            case "Polygon":
                polygons.Add(ReadPolygon(coordinates, factory));
                break;
            case "MultiPolygon":
                foreach (var member in coordinates.EnumerateArray())
                    polygons.Add(ReadPolygon(member, factory));
                break;
        }
    }

    // Clamping the poles to the Web-Mercator limit can fold a ring onto
    // itself (Antarctica), so repair anything the projection made invalid.
    var valid = new List<Polygon>(polygons.Count);
    foreach (var polygon in polygons)
    {
        if (polygon.IsValid)
        {
            valid.Add(polygon);
            continue;
        }
        var fixedGeometry = GeometryFixer.Fix(polygon);
        for (int i = 0; i < fixedGeometry.NumGeometries; i++)
        {
            if (fixedGeometry.GetGeometryN(i) is Polygon part && !part.IsEmpty)
                valid.Add(part);
        }
    }
    return valid;
}

static Polygon ReadPolygon(JsonElement rings, GeometryFactory factory)
{
    var projected = new List<LinearRing>();
    foreach (var ring in rings.EnumerateArray())
    {
        var coordinates = new List<Coordinate>();
        foreach (var point in ring.EnumerateArray())
        {
            double lat = Math.Clamp(point[1].GetDouble(), -WebMercator.MaxLatitude, WebMercator.MaxLatitude);
            var (x, y) = WebMercator.FromLonLat(point[0].GetDouble(), lat);
            coordinates.Add(new Coordinate(x, y));
        }
        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0].Copy());
        if (coordinates.Count >= 4)
            projected.Add(factory.CreateLinearRing(coordinates.ToArray()));
    }
    return factory.CreatePolygon(projected[0], projected.Skip(1).ToArray());
}

static byte[] BuildLevel(
    List<Polygon> source, double tolerance, int gridSize, GeometryFactory factory,
    out int tileCount, out int pointCount)
{
    double half = WebMercator.Circumference / 2.0;
    double tileSize = WebMercator.Circumference / gridSize;
    // Overlap between neighbouring tiles; several pixels at every scale the
    // level is drawn at, so anti-aliased tile edges are always covered.
    double margin = tileSize / 256.0;

    // Simplify whole polygons before clipping, so neighbouring tiles agree on
    // the shared coastline inside their overlap.
    var simplified = new List<Geometry>();
    foreach (var polygon in source)
    {
        var envelope = polygon.EnvelopeInternal;
        if (Math.Max(envelope.Width, envelope.Height) < tolerance)
            continue; // Smaller than a pixel wherever this level is drawn.
        var geometry = tolerance > 0 ? TopologyPreservingSimplifier.Simplify(polygon, tolerance) : polygon;
        if (!geometry.IsEmpty)
            simplified.Add(geometry);
    }

    var tiles = new SortedDictionary<(int Row, int Column), List<Polygon>>();
    foreach (var geometry in simplified)
    {
        var envelope = geometry.EnvelopeInternal;
        int c0 = Math.Max(0, (int)Math.Floor((envelope.MinX - margin + half) / tileSize));
        int c1 = Math.Min(gridSize - 1, (int)Math.Floor((envelope.MaxX + margin + half) / tileSize));
        int r0 = Math.Max(0, (int)Math.Floor((envelope.MinY - margin + half) / tileSize));
        int r1 = Math.Min(gridSize - 1, (int)Math.Floor((envelope.MaxY + margin + half) / tileSize));
        for (int row = r0; row <= r1; row++)
        {
            for (int column = c0; column <= c1; column++)
            {
                Geometry clipped = geometry;
                if (gridSize > 1)
                {
                    var clip = factory.ToGeometry(new Envelope(
                        column * tileSize - half - margin, (column + 1) * tileSize - half + margin,
                        row * tileSize - half - margin, (row + 1) * tileSize - half + margin));
                    if (!clip.EnvelopeInternal.Intersects(envelope))
                        continue;
                    clipped = clip.EnvelopeInternal.Contains(envelope)
                        ? geometry
                        : OverlayNGRobust.Overlay(geometry, clip, SpatialFunction.Intersection);
                }

                for (int i = 0; i < clipped.NumGeometries; i++)
                {
                    if (clipped.GetGeometryN(i) is not Polygon part || part.IsEmpty)
                        continue;
                    if (!tiles.TryGetValue((row, column), out var list))
                        tiles[(row, column)] = list = new List<Polygon>();
                    list.Add(part);
                }
            }
        }
    }

    using var buffer = new MemoryStream();
    using var writer = new BinaryWriter(buffer);
    tileCount = 0;
    pointCount = 0;
    var encodedTiles = new List<(int Row, int Column, List<List<(long X, long Y)>[]> Polygons)>();
    foreach (var ((row, column), polygons) in tiles)
    {
        var encoded = new List<List<(long X, long Y)>[]>();
        foreach (var polygon in polygons)
        {
            var shell = Quantise(polygon.ExteriorRing);
            if (shell is null)
                continue;
            var rings = new List<List<(long X, long Y)>> { shell };
            foreach (var hole in polygon.InteriorRings)
            {
                var quantised = Quantise(hole);
                if (quantised is not null)
                    rings.Add(quantised);
            }
            encoded.Add(rings.ToArray());
        }
        if (encoded.Count > 0)
            encodedTiles.Add((row, column, encoded));
    }

    WriteVarint(writer, (uint)encodedTiles.Count);
    foreach (var (row, column, polygons) in encodedTiles)
    {
        tileCount++;
        WriteVarint(writer, (uint)column);
        WriteVarint(writer, (uint)row);
        WriteVarint(writer, (uint)polygons.Count);
        foreach (var rings in polygons)
        {
            WriteVarint(writer, (uint)rings.Length);
            foreach (var ring in rings)
            {
                WriteVarint(writer, (uint)ring.Count);
                long px = 0, py = 0;
                foreach (var (x, y) in ring)
                {
                    WriteZigzag(writer, x - px);
                    WriteZigzag(writer, y - py);
                    px = x;
                    py = y;
                    pointCount++;
                }
            }
        }
    }

    writer.Flush();
    return buffer.ToArray();
}

// Rounds a ring to whole metres and drops the repeated points that rounding
// makes, returning it open (without the closing point), or null if fewer than
// three distinct points remain.
static List<(long X, long Y)>? Quantise(LineString ring)
{
    var points = new List<(long X, long Y)>(ring.NumPoints);
    foreach (var coordinate in ring.Coordinates)
    {
        var point = ((long)Math.Round(coordinate.X), (long)Math.Round(coordinate.Y));
        if (points.Count == 0 || points[^1] != point)
            points.Add(point);
    }
    if (points.Count > 1 && points[0] == points[^1])
        points.RemoveAt(points.Count - 1);
    return points.Count >= 3 ? points : null;
}

static byte[] Compress(byte[] data)
{
    using var output = new MemoryStream();
    using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize))
        brotli.Write(data);
    return output.ToArray();
}

static void WriteVarint(BinaryWriter writer, ulong value)
{
    while (value >= 0x80)
    {
        writer.Write((byte)(value | 0x80));
        value >>= 7;
    }
    writer.Write((byte)value);
}

static void WriteZigzag(BinaryWriter writer, long value)
    => WriteVarint(writer, (ulong)((value << 1) ^ (value >> 63)));
