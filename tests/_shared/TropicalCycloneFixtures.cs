using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// The tropical cyclone fixtures of issue #1294: NHC's archived GIS zips for
/// Hurricane Ian, advisory 20 (in <c>tests/datasets/TC</c>, copied to
/// <c>TestData/TC</c>; see the README there), and a writer for synthetic
/// advisories in NHC's layout.
/// </summary>
internal static class TropicalCycloneFixtures
{
    /// <summary>The folder of Ian's zips under the test output.</summary>
    public static string IanFolder => Path.Combine(AppContext.BaseDirectory, "TestData", "TC", "al092022");

    /// <summary>Ian advisory 20, 1100 PM EDT Tue Sep 27 2022: forecast points, track and cone.</summary>
    public static string IanForecast => Path.Combine(IanFolder, "al092022_5day_020.zip");

    /// <summary>Ian advisory 20's forecast wind radii.</summary>
    public static string IanWindRadii => Path.Combine(IanFolder, "al092022_fcst_020.zip");

    /// <summary>Ian's best track, as archived after the season (through 1 October 2022 06Z).</summary>
    public static string IanBestTrack => Path.Combine(IanFolder, "al092022_best_track.zip");

    /// <summary>Copies Ian's three zips into <paramref name="folder"/> and returns the forecast zip's new path.</summary>
    public static string CopyIan(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in new[] { IanForecast, IanWindRadii, IanBestTrack })
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
        return Path.Combine(folder, Path.GetFileName(IanForecast));
    }
}

/// <summary>
/// Writes synthetic NHC GIS zips (shapefiles with NHC's field names), for
/// cases the archive doesn't hold small: a final advisory with no cone,
/// missing radii, the southern hemisphere.
/// </summary>
internal static class SyntheticNhc
{
    /// <summary>A forecast point, as one <c>_5day_pts</c> record.</summary>
    public sealed record Point(int Tau, string ValidTime, double Latitude, double Longitude, int MaxWind, int Gust, string StormType, string Letter, int Pressure = 9999);

    /// <summary>One wind radii row, as one <c>_forecastradii</c> or best track <c>_radii</c> record.</summary>
    public sealed record Radius(int Tau, string Time, int Threshold, double Ne, double Se, double Sw, double Nw, double Latitude, double Longitude);

    /// <summary>A best track point, as one best track <c>_pts</c> record.</summary>
    public sealed record TrackPoint(DateTime Time, double Latitude, double Longitude, int Intensity, string StormType, string Name);

    /// <summary>Writes a forecast zip; <paramref name="cone"/> null leaves the <c>_pgn</c> layer out.</summary>
    public static byte[] ForecastZip(string atcf, string advisory, string advDate, string name, IReadOnlyList<Point> points, IReadOnlyList<(double Latitude, double Longitude)>? cone)
    {
        var stem = $"{atcf.ToLowerInvariant()}-{advisory}_5day";
        string[] header = ["STORMNAME", "STORMTYPE", "ADVDATE", "ADVISNUM", "STORMNUM", "FCSTPRD", "BASIN"];
        string[] Header() => [name, points[0].StormType, advDate, advisory.TrimStart('0'), atcf.Substring(2, 2), "120", atcf[..2].ToUpperInvariant()];

        var entries = new Dictionary<string, byte[]>();
        AddLayer(entries, $"{stem}_lin", 3, [Line(points.Select(p => (p.Latitude, p.Longitude)).ToArray())], header, [Header()]);
        if (cone is not null)
            AddLayer(entries, $"{stem}_pgn", 5, [Line(cone)], header, [Header()]);
        AddLayer(
            entries,
            $"{stem}_pts",
            1,
            [.. points.Select(p => PointShape(p.Latitude, p.Longitude))],
            ["ADVDATE", "ADVISNUM", "BASIN", "DVLBL", "FCSTPRD", "GUST", "LAT", "LON", "MAXWIND", "MSLP", "SSNUM", "STORMNAME", "STORMTYPE", "TCDVLP", "TAU", "TCDIR", "TCSPD", "TIMEZONE", "VALIDTIME"],
            [.. points.Select(p => new[]
            {
                advDate, advisory.TrimStart('0'), atcf[..2].ToUpperInvariant(), p.Letter, "120", F(p.Gust), F(p.Latitude), F(p.Longitude), F(p.MaxWind), F(p.Pressure),
                "0.0", $"Tropical Cyclone {name}", p.StormType, p.StormType, p.Tau.ToString(CultureInfo.InvariantCulture), "9999.0", "9999.0", "UTC", p.ValidTime,
            })]);
        return Zip(entries);
    }

    /// <summary>Writes a wind radii zip of <c>_forecastradii</c> rows, each with a small square polygon.</summary>
    public static byte[] WindRadiiZip(string atcf, string advisory, string validTime, IReadOnlyList<Radius> rows)
    {
        var entries = new Dictionary<string, byte[]>();
        AddLayer(
            entries,
            $"{atcf.ToLowerInvariant()}_{validTime}_forecastradii",
            5,
            [.. rows.Select(r => Line(Square(r.Latitude, r.Longitude, r.Threshold / 100.0)))],
            ["RADII", "STORMID", "BASIN", "STORMNUM", "ADVNUM", "VALIDTIME", "SYNOPTIME", "TIMEZONE", "TAU", "NE", "SE", "SW", "NW"],
            [.. rows.Select(r => new[]
            {
                F(r.Threshold), atcf.ToLowerInvariant(), atcf[..2].ToLowerInvariant(), atcf.Substring(2, 2), advisory.TrimStart('0'), r.Time, r.Time, "UTC",
                F(r.Tau), F(r.Ne), F(r.Se), F(r.Sw), F(r.Nw),
            })]);
        return Zip(entries);
    }

    /// <summary>Writes a best track zip of <c>_pts</c> and <c>_radii</c> layers.</summary>
    public static byte[] BestTrackZip(string atcf, IReadOnlyList<TrackPoint> points, IReadOnlyList<Radius> radii)
    {
        var stem = atcf.ToUpperInvariant();
        var entries = new Dictionary<string, byte[]>();
        AddLayer(
            entries,
            $"{stem}_pts",
            1,
            [.. points.Select(p => PointShape(p.Latitude, p.Longitude))],
            ["STORMNAME", "DTG", "YEAR", "MONTH", "DAY", "HHMM", "MSLP", "BASIN", "STORMNUM", "STORMTYPE", "INTENSITY", "SS", "LAT", "LON"],
            [.. points.Select(p => new[]
            {
                p.Name, p.Time.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + ".0", F(p.Time.Year), p.Time.ToString("MM", CultureInfo.InvariantCulture),
                F(p.Time.Day), p.Time.ToString("HHmm", CultureInfo.InvariantCulture), "1000.0", atcf[..2].ToLowerInvariant(), atcf.Substring(2, 2),
                p.StormType, F(p.Intensity), "0.0", F(p.Latitude), F(p.Longitude),
            })]);
        AddLayer(
            entries,
            $"{stem}_radii",
            5,
            [.. radii.Select(r => Line(Square(r.Latitude, r.Longitude, r.Threshold / 100.0)))],
            ["RADII", "STORMID", "BASIN", "STORMNUM", "ADVNUM", "SYNOPTIME", "TIMEZONE", "NE", "SE", "SW", "NW"],
            [.. radii.Select(r => new[]
            {
                F(r.Threshold), stem, atcf[..2].ToLowerInvariant(), atcf.Substring(2, 2), "0.0", r.Time, "UTC", F(r.Ne), F(r.Se), F(r.Sw), F(r.Nw),
            })]);
        return Zip(entries);
    }

    /// <summary>A clockwise square ring around a centre, as a shapefile outer ring.</summary>
    public static (double Latitude, double Longitude)[] Square(double latitude, double longitude, double half) =>
    [
        (latitude + half, longitude - half), (latitude + half, longitude + half), (latitude - half, longitude + half),
        (latitude - half, longitude - half), (latitude + half, longitude - half),
    ];

    private static string F(double value) => value.ToString("0.0###", CultureInfo.InvariantCulture);

    private abstract record Shape;

    private sealed record PointShapeRecord(double Latitude, double Longitude) : Shape;

    private sealed record LineShapeRecord(IReadOnlyList<(double Latitude, double Longitude)> Positions) : Shape;

    private static Shape PointShape(double latitude, double longitude) => new PointShapeRecord(latitude, longitude);

    private static Shape Line(IReadOnlyList<(double Latitude, double Longitude)> positions) => new LineShapeRecord(positions);

    private static void AddLayer(Dictionary<string, byte[]> entries, string stem, int shapeType, IReadOnlyList<Shape> shapes, string[] fields, IReadOnlyList<string[]> rows)
    {
        entries[stem + ".shp"] = Shp(shapeType, shapes);
        entries[stem + ".dbf"] = Dbf(fields, rows);
    }

    private static byte[] Zip(Dictionary<string, byte[]> entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // NHC's forecast zips start with the line layer's table.
            foreach (var (name, bytes) in entries.OrderBy(e => e.Key.EndsWith(".dbf", StringComparison.Ordinal) ? 0 : 1).ThenBy(e => e.Key, StringComparer.Ordinal))
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Shp(int shapeType, IReadOnlyList<Shape> shapes)
    {
        var records = new List<byte[]>();
        foreach (var shape in shapes)
        {
            byte[] content;
            if (shape is PointShapeRecord p)
            {
                content = new byte[20];
                BinaryPrimitives.WriteInt32LittleEndian(content, 1);
                BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(4), p.Longitude);
                BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(12), p.Latitude);
            }
            else
            {
                var line = (LineShapeRecord)shape;
                content = new byte[44 + 4 + (16 * line.Positions.Count)];
                BinaryPrimitives.WriteInt32LittleEndian(content, shapeType);
                BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(4), line.Positions.Min(x => x.Longitude));
                BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(12), line.Positions.Min(x => x.Latitude));
                BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(20), line.Positions.Max(x => x.Longitude));
                BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(28), line.Positions.Max(x => x.Latitude));
                BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(36), 1);
                BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(40), line.Positions.Count);
                BinaryPrimitives.WriteInt32LittleEndian(content.AsSpan(44), 0);
                for (var i = 0; i < line.Positions.Count; i++)
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(48 + (16 * i)), line.Positions[i].Longitude);
                    BinaryPrimitives.WriteDoubleLittleEndian(content.AsSpan(56 + (16 * i)), line.Positions[i].Latitude);
                }
            }

            records.Add(content);
        }

        var length = 100 + records.Sum(r => 8 + r.Length);
        var shp = new byte[length];
        BinaryPrimitives.WriteInt32BigEndian(shp, 9994);
        BinaryPrimitives.WriteInt32BigEndian(shp.AsSpan(24), length / 2);
        BinaryPrimitives.WriteInt32LittleEndian(shp.AsSpan(28), 1000);
        BinaryPrimitives.WriteInt32LittleEndian(shp.AsSpan(32), shapeType);
        var offset = 100;
        for (var i = 0; i < records.Count; i++)
        {
            BinaryPrimitives.WriteInt32BigEndian(shp.AsSpan(offset), i + 1);
            BinaryPrimitives.WriteInt32BigEndian(shp.AsSpan(offset + 4), records[i].Length / 2);
            records[i].CopyTo(shp, offset + 8);
            offset += 8 + records[i].Length;
        }

        return shp;
    }

    private static byte[] Dbf(string[] fields, IReadOnlyList<string[]> rows)
    {
        const int width = 50;
        var headerLength = 32 + (32 * fields.Length) + 1;
        var recordLength = 1 + (width * fields.Length);
        var dbf = new byte[headerLength + (recordLength * rows.Count) + 1];
        dbf[0] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(dbf.AsSpan(4), rows.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(dbf.AsSpan(8), (ushort)headerLength);
        BinaryPrimitives.WriteUInt16LittleEndian(dbf.AsSpan(10), (ushort)recordLength);
        for (var f = 0; f < fields.Length; f++)
        {
            var at = 32 + (32 * f);
            Encoding.ASCII.GetBytes(fields[f]).CopyTo(dbf, at);
            dbf[at + 11] = (byte)'C';
            dbf[at + 16] = width;
        }

        dbf[headerLength - 1] = 0x0D;
        for (var r = 0; r < rows.Count; r++)
        {
            var at = headerLength + (r * recordLength);
            dbf[at] = (byte)' ';
            for (var f = 0; f < fields.Length; f++)
                Encoding.Latin1.GetBytes(rows[r][f].PadRight(width)[..width]).CopyTo(dbf, at + 1 + (width * f));
        }

        dbf[^1] = 0x1A;
        return dbf;
    }
}
