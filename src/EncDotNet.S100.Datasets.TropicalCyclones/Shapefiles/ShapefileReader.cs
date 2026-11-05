using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.TropicalCyclones.Shapefiles;

/// <summary>
/// The geometry of one shapefile record, in geographic degrees: x is
/// longitude, y latitude.
/// </summary>
internal abstract record ShapeGeometry;

/// <summary>A point record.</summary>
internal sealed record ShapePoint(GeoPosition Position) : ShapeGeometry;

/// <summary>A polyline record: one list of positions per part.</summary>
internal sealed record ShapeLine(IReadOnlyList<IReadOnlyList<GeoPosition>> Parts) : ShapeGeometry;

/// <summary>A polygon record: its rings grouped into surfaces, each an outer ring and its holes.</summary>
internal sealed record ShapePolygon(IReadOnlyList<SurfacePart> Surfaces) : ShapeGeometry;

/// <summary>
/// One shapefile record: its geometry (<see langword="null"/> for a null
/// shape or a shape type this reader skips) and its DBF attributes, trimmed,
/// by upper-case field name.
/// </summary>
internal sealed record ShapefileRecord(ShapeGeometry? Geometry, IReadOnlyDictionary<string, string> Attributes)
{
    /// <summary>The trimmed text of <paramref name="field"/>, or <see langword="null"/> when it's absent or blank.</summary>
    public string? Text(string field) =>
        Attributes.TryGetValue(field, out var value) && value.Length > 0 ? value : null;

    /// <summary>
    /// The number in <paramref name="field"/>, or <see langword="null"/> when
    /// it's absent, blank, not a number, or one of NHC's "missing" values
    /// (9999 and above).
    /// </summary>
    public double? Number(string field) =>
        Text(field) is { } text
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
        && Math.Abs(value) < 9999
            ? value
            : null;
}

/// <summary>
/// A small reader for ESRI shapefiles: the geometry (<c>.shp</c>) and the
/// attribute table (<c>.dbf</c>) of points, polylines and polygons, including
/// their Z and M forms with only x and y kept. It reads what NHC's GIS files
/// hold and nothing more: multipoint and multipatch records come back with no
/// geometry, and the index (<c>.shx</c>) and projection (<c>.prj</c>) are not
/// read.
/// </summary>
/// <remarks>
/// Written from the ESRI Shapefile Technical Description (1998) and the dBASE
/// III table layout. Polygon rings are grouped the way that description says:
/// clockwise rings are outer rings and counter-clockwise rings are holes,
/// each hole going to the smallest outer ring that holds it. A file whose
/// rings all run counter-clockwise is read as outer rings only.
/// </remarks>
internal static class ShapefileReader
{
    private const int FileCode = 9994;
    private const int HeaderLength = 100;

    /// <summary>Reads the records of a shapefile from its <c>.shp</c> and <c>.dbf</c> bytes.</summary>
    /// <param name="shp">The geometry file.</param>
    /// <param name="dbf">The attribute table, or <see langword="null"/> for records with no attributes.</param>
    /// <exception cref="InvalidDataException">The bytes aren't a shapefile, or the two files disagree.</exception>
    public static IReadOnlyList<ShapefileRecord> Read(ReadOnlySpan<byte> shp, ReadOnlySpan<byte> dbf)
    {
        var geometries = ReadGeometries(shp);
        var table = dbf.IsEmpty ? null : ReadTable(dbf);
        if (table is not null && table.Count != geometries.Count)
            throw new InvalidDataException($"The .shp holds {geometries.Count} records and the .dbf {table.Count}.");

        var records = new List<ShapefileRecord>(geometries.Count);
        for (var i = 0; i < geometries.Count; i++)
        {
            var attributes = table?[i];
            if (attributes is null && table is not null)
                continue; // Deleted in the table.
            records.Add(new ShapefileRecord(geometries[i], attributes ?? new Dictionary<string, string>()));
        }

        return records;
    }

    /// <summary>Reads the geometry of every record of a <c>.shp</c> file.</summary>
    internal static IReadOnlyList<ShapeGeometry?> ReadGeometries(ReadOnlySpan<byte> shp)
    {
        if (shp.Length < HeaderLength || BinaryPrimitives.ReadInt32BigEndian(shp) != FileCode)
            throw new InvalidDataException("Not a shapefile: the .shp doesn't start with file code 9994.");

        var length = Math.Min(shp.Length, (long)BinaryPrimitives.ReadInt32BigEndian(shp[24..]) * 2);
        var geometries = new List<ShapeGeometry?>();
        var offset = HeaderLength;
        while (offset + 12 <= length)
        {
            var contentBytes = BinaryPrimitives.ReadInt32BigEndian(shp[(offset + 4)..]) * 2;
            var start = offset + 8;
            if (contentBytes < 4 || start + contentBytes > length)
                throw new InvalidDataException($"A .shp record at byte {offset} runs past the end of the file.");
            geometries.Add(ReadShape(shp.Slice(start, contentBytes)));
            offset = start + contentBytes;
        }

        return geometries;
    }

    private static ShapeGeometry? ReadShape(ReadOnlySpan<byte> content)
    {
        var type = BinaryPrimitives.ReadInt32LittleEndian(content);
        switch (type)
        {
            case 1 or 11 or 21: // Point, PointZ, PointM.
                return new ShapePoint(Position(content, 4));
            case 3 or 13 or 23: // PolyLine and its Z and M forms.
                return new ShapeLine(ReadParts(content));
            case 5 or 15 or 25: // Polygon and its Z and M forms.
                return new ShapePolygon(GroupRings(ReadParts(content)));
            default: // Null shape, multipoint, multipatch.
                return null;
        }
    }

    // Box (4 doubles), number of parts, number of points, the parts' first
    // point indices, then the points as x, y pairs.
    private static IReadOnlyList<IReadOnlyList<GeoPosition>> ReadParts(ReadOnlySpan<byte> content)
    {
        var partCount = BinaryPrimitives.ReadInt32LittleEndian(content[36..]);
        var pointCount = BinaryPrimitives.ReadInt32LittleEndian(content[40..]);
        var pointsAt = 44 + (4 * partCount);
        if (partCount < 0 || pointCount < 0 || pointsAt + (16L * pointCount) > content.Length)
            throw new InvalidDataException("A .shp record's part or point count doesn't fit the record.");

        var parts = new List<IReadOnlyList<GeoPosition>>(partCount);
        for (var p = 0; p < partCount; p++)
        {
            var first = BinaryPrimitives.ReadInt32LittleEndian(content[(44 + (4 * p))..]);
            var end = p + 1 < partCount ? BinaryPrimitives.ReadInt32LittleEndian(content[(48 + (4 * p))..]) : pointCount;
            if (first < 0 || end > pointCount || end < first)
                throw new InvalidDataException("A .shp record's part index is out of range.");
            var part = new GeoPosition[end - first];
            for (var i = first; i < end; i++)
                part[i - first] = Position(content, pointsAt + (16 * i));
            parts.Add(part);
        }

        return parts;
    }

    private static GeoPosition Position(ReadOnlySpan<byte> content, int at) =>
        new(
            Latitude: BinaryPrimitives.ReadDoubleLittleEndian(content[(at + 8)..]),
            Longitude: BinaryPrimitives.ReadDoubleLittleEndian(content[at..]));

    /// <summary>
    /// Groups polygon rings into surfaces: clockwise rings are outer rings,
    /// counter-clockwise ones holes of the smallest outer ring holding them.
    /// </summary>
    internal static IReadOnlyList<SurfacePart> GroupRings(IReadOnlyList<IReadOnlyList<GeoPosition>> rings)
    {
        var usable = rings.Where(r => r.Count >= 4).ToArray();
        var outers = usable.Where(r => SignedArea(r) < 0).ToList();
        if (outers.Count == 0)
            return [.. usable.Select(r => new SurfacePart(r, []))];

        var holes = outers.Select(_ => new List<IReadOnlyList<GeoPosition>>()).ToList();
        var outerCount = outers.Count;
        foreach (var ring in usable.Where(r => SignedArea(r) >= 0))
        {
            var owner = Enumerable.Range(0, outerCount)
                .Where(i => Contains(outers[i], ring[0]))
                .OrderBy(i => Math.Abs(SignedArea(outers[i])))
                .DefaultIfEmpty(-1)
                .First();
            if (owner >= 0)
            {
                holes[owner].Add(ring);
            }
            else
            {
                // A stray counter-clockwise ring stands on its own.
                outers.Add(ring);
                holes.Add([]);
            }
        }

        return [.. outers.Select((o, i) => new SurfacePart(o, holes[i]))];
    }

    // Shoelace in (longitude, latitude): positive counter-clockwise.
    internal static double SignedArea(IReadOnlyList<GeoPosition> ring)
    {
        var sum = 0.0;
        for (var i = 0; i < ring.Count - 1; i++)
            sum += (ring[i].Longitude * ring[i + 1].Latitude) - (ring[i + 1].Longitude * ring[i].Latitude);
        return sum / 2;
    }

    private static bool Contains(IReadOnlyList<GeoPosition> ring, GeoPosition point)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Latitude > point.Latitude) != (b.Latitude > point.Latitude)
                && point.Longitude < ((b.Longitude - a.Longitude) * (point.Latitude - a.Latitude) / (b.Latitude - a.Latitude)) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>
    /// Reads a dBASE table: one dictionary per record, by upper-case field
    /// name, with <see langword="null"/> for a record marked deleted.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyDictionary<string, string>?> ReadTable(ReadOnlySpan<byte> dbf)
    {
        if (dbf.Length < 33)
            throw new InvalidDataException("Not a dBASE table: the .dbf is too short.");

        var recordCount = BinaryPrimitives.ReadInt32LittleEndian(dbf[4..]);
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(dbf[8..]);
        var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(dbf[10..]);

        var fields = new List<(string Name, int Offset, int Length)>();
        var fieldOffset = 1; // Byte 0 of a record is the deletion flag.
        for (var at = 32; at + 32 <= headerLength && dbf[at] != 0x0D; at += 32)
        {
            var nameBytes = dbf.Slice(at, 11);
            var nul = nameBytes.IndexOf((byte)0);
            var name = Encoding.ASCII.GetString(nul < 0 ? nameBytes : nameBytes[..nul]).Trim().ToUpperInvariant();
            int length = dbf[at + 16];
            fields.Add((name, fieldOffset, length));
            fieldOffset += length;
        }

        if (recordCount < 0 || headerLength + ((long)recordCount * recordLength) > dbf.Length || fieldOffset > recordLength)
            throw new InvalidDataException("The .dbf's records don't fit the file.");

        var records = new List<IReadOnlyDictionary<string, string>?>(recordCount);
        for (var r = 0; r < recordCount; r++)
        {
            var record = dbf.Slice(headerLength + (r * recordLength), recordLength);
            if (record[0] == (byte)'*')
            {
                records.Add(null);
                continue;
            }

            var values = new Dictionary<string, string>(fields.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (name, offset, length) in fields)
                values[name] = Encoding.Latin1.GetString(record.Slice(offset, length)).Trim().TrimEnd('\0');
            records.Add(values);
        }

        return records;
    }
}
