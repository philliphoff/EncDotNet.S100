using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.TropicalCyclones.Shapefiles;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>A file that isn't a readable NHC tropical cyclone advisory.</summary>
public sealed class NhcFormatException : FormatException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What's wrong with the file.</param>
    public NhcFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the error that caused it.</summary>
    /// <param name="message">What's wrong with the file.</param>
    /// <param name="innerException">The underlying error.</param>
    public NhcFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads a <see cref="TropicalCycloneAdvisory"/> from the US National
/// Hurricane Center's GIS shapefile zips (<see href="https://www.nhc.noaa.gov/gis/"/>).
/// </summary>
/// <remarks>
/// <para>What each zip gives (design note <c>vector-weather.md</c> §8.1, V32):</para>
/// <list type="bullet">
/// <item><description>
/// <b>Forecast</b>, <c>{ATCF}_5day_{adv}.zip</c>, required: the forecast
/// points (<c>_5day_pts</c>: <c>TAU</c>, <c>VALIDTIME</c>, <c>MAXWIND</c>,
/// <c>GUST</c>, <c>MSLP</c>, <c>STORMTYPE</c>, <c>DVLBL</c>, <c>TCDVLP</c>,
/// <c>SSNUM</c>, <c>TCDIR</c>, <c>TCSPD</c>, <c>ADVDATE</c>, <c>ADVISNUM</c>,
/// <c>FCSTPRD</c>), the track line (<c>_5day_lin</c>) and the cone
/// (<c>_5day_pgn</c>, absent or empty on some final advisories). The
/// watches and warnings it can also hold (<c>_ww_wwlin</c>) aren't read.
/// </description></item>
/// <item><description>
/// <b>Wind radii</b>, <c>{ATCF}_fcst_{adv}.zip</c>, optional: one polygon per
/// forecast time and threshold (<c>_forecastradii</c>: <c>TAU</c>,
/// <c>RADII</c>, <c>NE</c>, <c>SE</c>, <c>SW</c>, <c>NW</c>), and the
/// initial radii again (<c>_initialradii</c>), used only when the forecast
/// radii are missing.
/// </description></item>
/// <item><description>
/// <b>Best track</b>, <c>{ATCF}_best_track.zip</c>, optional: the past points
/// (<c>_pts</c>: <c>YEAR</c>, <c>MONTH</c>, <c>DAY</c>, <c>HHMM</c>,
/// <c>INTENSITY</c>, <c>MSLP</c>, <c>STORMTYPE</c>, <c>SS</c>,
/// <c>STORMNAME</c>) and observed radii (<c>_radii</c>, by
/// <c>SYNOPTIME</c>). The wind swath isn't read.
/// </description></item>
/// </list>
/// <para>
/// The field names were checked against NHC's archived files for 2022 and
/// 2023 (for example <c>al092022_5day_020.zip</c>). Coordinates are NHC's,
/// on a sphere of radius 6,371,200 m, and are taken as WGS-84 degrees: the
/// difference is far below what the data can resolve.
/// </para>
/// <para>
/// Times: <c>ADVDATE</c> is local time with a US zone abbreviation
/// (<c>"1100 PM EDT Tue Sep 27 2022"</c>) and becomes
/// <see cref="TropicalCycloneAdvisory.IssueTime"/> with that offset;
/// <c>VALIDTIME</c> is day and time in UTC (<c>"28/0000"</c>), placed in the
/// month nearest the issue time plus the point's tau.
/// </para>
/// </remarks>
public static partial class NhcAdvisoryReader
{
    // NHC's local zones, as they appear in ADVDATE.
    private static readonly Dictionary<string, TimeSpan> ZoneOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UTC"] = TimeSpan.Zero,
        ["GMT"] = TimeSpan.Zero,
        ["Z"] = TimeSpan.Zero,
        ["AST"] = TimeSpan.FromHours(-4),
        ["ADT"] = TimeSpan.FromHours(-3),
        ["EST"] = TimeSpan.FromHours(-5),
        ["EDT"] = TimeSpan.FromHours(-4),
        ["CST"] = TimeSpan.FromHours(-6),
        ["CDT"] = TimeSpan.FromHours(-5),
        ["MST"] = TimeSpan.FromHours(-7),
        ["MDT"] = TimeSpan.FromHours(-6),
        ["PST"] = TimeSpan.FromHours(-8),
        ["PDT"] = TimeSpan.FromHours(-7),
        ["AKST"] = TimeSpan.FromHours(-9),
        ["AKDT"] = TimeSpan.FromHours(-8),
        ["HST"] = TimeSpan.FromHours(-10),
        ["HDT"] = TimeSpan.FromHours(-9),
        ["SST"] = TimeSpan.FromHours(-11),
        ["CHST"] = TimeSpan.FromHours(10),
    };

    /// <summary>
    /// Reads the advisory at <paramref name="path"/>: a forecast zip, with its
    /// wind radii and best track zips when they're beside it, or a folder
    /// holding one storm's zips, whose latest advisory is read
    /// (<see cref="NhcAdvisoryFiles.Find"/>).
    /// </summary>
    /// <param name="path">A forecast zip or a folder.</param>
    /// <exception cref="NhcFormatException">The path isn't an NHC advisory, or a zip can't be read.</exception>
    public static TropicalCycloneAdvisory Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var files = NhcAdvisoryFiles.Find(path);
        if (files is null)
        {
            // A forecast zip renamed by a download: read it alone.
            if (File.Exists(path))
            {
                using var alone = File.OpenRead(path);
                return Read(alone, null, null);
            }

            throw new NhcFormatException($"{Path.GetFileName(path)}: not an NHC forecast zip ({{ATCF}}_5day_{{adv}}.zip) or a folder holding one storm's.");
        }

        return Read(files);
    }

    /// <summary>Reads the advisory whose zips are <paramref name="files"/>.</summary>
    /// <param name="files">The advisory's zips.</param>
    /// <exception cref="NhcFormatException">A zip can't be read.</exception>
    public static TropicalCycloneAdvisory Read(NhcAdvisoryFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var forecast = File.OpenRead(files.ForecastZip);
        using var radii = files.WindRadiiZip is { } r ? File.OpenRead(r) : null;
        using var best = files.BestTrackZip is { } b ? File.OpenRead(b) : null;
        return Read(forecast, radii, best);
    }

    /// <summary>
    /// Reads an advisory from the bytes of its zips. The streams are read but
    /// not disposed.
    /// </summary>
    /// <param name="forecastZip">The forecast zip, <c>{ATCF}_5day_{adv}.zip</c>.</param>
    /// <param name="windRadiiZip">The wind radii zip, <c>{ATCF}_fcst_{adv}.zip</c>, or <see langword="null"/>.</param>
    /// <param name="bestTrackZip">The best track zip, <c>{ATCF}_best_track.zip</c>, or <see langword="null"/>.</param>
    /// <exception cref="NhcFormatException">A zip isn't a zip, or the forecast zip has no forecast points.</exception>
    public static TropicalCycloneAdvisory Read(Stream forecastZip, Stream? windRadiiZip, Stream? bestTrackZip)
    {
        ArgumentNullException.ThrowIfNull(forecastZip);

        var forecastLayers = ReadZip(forecastZip, "forecast", "_5day_pts", "_5day_lin", "_5day_pgn");
        if (!forecastLayers.TryGetValue("_5day_pts", out var pointsLayer))
            throw new NhcFormatException("The forecast zip has no forecast points (_5day_pts.shp).");
        var points = pointsLayer.Records;
        if (points.Count == 0)
            throw new NhcFormatException("The forecast zip's forecast points (_5day_pts.shp) are empty.");

        var first = points[0];
        var identity = IdentityFromEntry(pointsLayer.EntryName);
        var lineRecords = forecastLayers.TryGetValue("_5day_lin", out var line) ? line.Records : [];
        var coneRecords = forecastLayers.TryGetValue("_5day_pgn", out var cone) ? cone.Records : [];

        var radiiLayers = windRadiiZip is null
            ? []
            : ReadZip(windRadiiZip, "wind radii", "_forecastradii", "_initialradii");
        var radiiRecords = radiiLayers.TryGetValue("_forecastradii", out var forecastRadii) && forecastRadii.Records.Count > 0
            ? forecastRadii.Records
            : radiiLayers.TryGetValue("_initialradii", out var initialRadii) ? initialRadii.Records : [];

        var atcfId = identity.AtcfId
            ?? radiiRecords.Select(r => r.Text("STORMID")).FirstOrDefault(TropicalCycloneStorm.IsAtcfId)?.ToUpperInvariant()
            ?? FallbackAtcfId(first);
        var advisory = TrimAdvisory(first.Text("ADVISNUM") ?? identity.Advisory ?? string.Empty);

        var issueTime = ParseAdvisoryDate(first.Text("ADVDATE"))
            ?? radiiRecords.Where(r => r.Number("TAU") == 0).Select(r => ParseDtg(r.Text("VALIDTIME"))).OfType<DateTime>().Select(t => new DateTimeOffset(t, TimeSpan.Zero)).Cast<DateTimeOffset?>().FirstOrDefault()
            ?? throw new NhcFormatException("The forecast points have no advisory time (ADVDATE) the reader can read.");
        var synopticFallback = new DateTime(issueTime.UtcDateTime.Ticks - (issueTime.UtcDateTime.Ticks % TimeSpan.FromHours(6).Ticks), DateTimeKind.Utc);

        var radiiByTau = radiiRecords
            .GroupBy(r => (int)Math.Round(r.Number("TAU") ?? 0))
            .ToDictionary(g => g.Key, g => Radii(g));

        var forecast = points
            .Select(p => ForecastPoint(p, issueTime.UtcDateTime, synopticFallback, radiiByTau))
            .OfType<TropicalCycloneForecastPoint>()
            .OrderBy(p => p.Tau)
            .ToArray();
        if (forecast.Length == 0)
            throw new NhcFormatException("None of the forecast points has a position.");

        var track = lineRecords
            .Select(r => r.Geometry)
            .OfType<ShapeLine>()
            .SelectMany(l => l.Parts)
            .SelectMany(p => p)
            .ToArray();

        var coneSurfaces = coneRecords
            .Select(r => r.Geometry)
            .OfType<ShapePolygon>()
            .SelectMany(p => p.Surfaces)
            .ToArray();

        var name = (lineRecords.Concat(coneRecords).Select(r => r.Text("STORMNAME")).FirstOrDefault(n => n is not null)
            ?? StripClass(first.Text("STORMNAME"))
            ?? atcfId).Trim();

        var pastTrack = bestTrackZip is null ? [] : ReadBestTrack(bestTrackZip, forecast[0].ValidTime);

        return new TropicalCycloneAdvisory
        {
            Storm = new TropicalCycloneStorm(atcfId, name),
            Advisory = advisory,
            IssueTime = issueTime,
            ForecastPeriodHours = (int)Math.Round(first.Number("FCSTPRD") ?? forecast[^1].Tau),
            Forecast = forecast,
            ForecastTrack = track.Length >= 2 ? track : [.. forecast.Select(p => p.Position)],
            Cone = coneSurfaces.Length == 0 ? null : TropicalCycloneCone.Create(coneSurfaces, forecast),
            PastTrack = pastTrack,
        };
    }

    /// <summary>
    /// Whether <paramref name="leadingBytes"/> start a zip whose first entry is
    /// one of an NHC forecast zip's layers (<c>al092022-020_5day_lin.dbf</c>,
    /// <c>…_5day_pgn…</c>, <c>…_5day_pts…</c> or <c>…_ww_wwlin…</c>), whatever
    /// the zip is called.
    /// </summary>
    /// <param name="leadingBytes">The file's first bytes; the zip's first local header is enough.</param>
    public static bool LooksLikeForecastZip(ReadOnlySpan<byte> leadingBytes)
    {
        // Local file header: signature, …, name length at 26, extra length at 28, name at 30.
        if (leadingBytes.Length < 30 || !leadingBytes.StartsWith("PK\u0003\u0004"u8))
            return false;
        var nameLength = leadingBytes[26] | (leadingBytes[27] << 8);
        if (nameLength == 0 || 30 + nameLength > leadingBytes.Length)
            return false;
        var name = System.Text.Encoding.ASCII.GetString(leadingBytes.Slice(30, nameLength));
        return ForecastEntryRegex().IsMatch(Path.GetFileName(name));
    }

    private sealed record Layer(string EntryName, IReadOnlyList<ShapefileRecord> Records);

    // Reads the named layers of a zip: each is the .shp and .dbf entries whose
    // names end with the suffix.
    private static Dictionary<string, Layer> ReadZip(Stream stream, string what, params string[] suffixes)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var layers = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);
            foreach (var suffix in suffixes)
            {
                var shp = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(suffix + ".shp", StringComparison.OrdinalIgnoreCase));
                if (shp is null)
                    continue;
                var stem = shp.FullName[..^4];
                var dbf = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, stem + ".dbf", StringComparison.OrdinalIgnoreCase));
                layers[suffix] = new Layer(shp.Name, ShapefileReader.Read(ReadEntry(shp), dbf is null ? [] : ReadEntry(dbf)));
            }

            return layers;
        }
        catch (InvalidDataException ex)
        {
            throw new NhcFormatException($"The {what} zip can't be read: {ex.Message}", ex);
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream(entry.Length is > 0 and < int.MaxValue ? (int)entry.Length : 0);
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // "al092022-020_5day_pts.shp" → AL092022, 020.
    private static (string? AtcfId, string? Advisory) IdentityFromEntry(string entryName)
    {
        var m = EntryIdentityRegex().Match(entryName);
        return m.Success ? (m.Groups["id"].Value.ToUpperInvariant(), m.Groups["adv"].Value) : (null, null);
    }

    private static string FallbackAtcfId(ShapefileRecord point)
    {
        var basin = point.Text("BASIN")?.ToUpperInvariant() ?? "XX";
        var number = (int)(point.Number("STORMNUM") ?? 0);
        var year = ParseAdvisoryDate(point.Text("ADVDATE"))?.Year ?? 0;
        return string.Create(CultureInfo.InvariantCulture, $"{basin}{number:00}{year:0000}");
    }

    private static string TrimAdvisory(string advisory)
    {
        var trimmed = advisory.Trim().TrimStart('0');
        return trimmed.Length == 0 || !char.IsAsciiDigit(trimmed[0]) ? "0" + trimmed : trimmed;
    }

    // "Hurricane Ian" → "Ian"; "Post-Tropical Cyclone Ian" → "Ian".
    private static string? StripClass(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;
        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 1 ? words[^1] : fullName;
    }

    private static TropicalCycloneForecastPoint? ForecastPoint(
        ShapefileRecord record,
        DateTime issueUtc,
        DateTime synopticFallback,
        IReadOnlyDictionary<int, IReadOnlyList<TropicalCycloneWindRadii>> radiiByTau)
    {
        var position = Position(record);
        if (position is null)
            return null;
        var tau = (int)Math.Round(record.Number("TAU") ?? 0);
        var code = record.Text("STORMTYPE") ?? string.Empty;
        var category = (int?)record.Number("SSNUM");
        return new TropicalCycloneForecastPoint
        {
            Tau = tau,
            ValidTime = ParseDayTime(record.Text("VALIDTIME"), synopticFallback.AddHours(tau)) ?? synopticFallback.AddHours(tau),
            Position = position.Value,
            MaxWindKnots = Knots(record.Number("MAXWIND")),
            GustKnots = Knots(record.Number("GUST")),
            PressureHectopascals = Pressure(record.Number("MSLP")),
            Classification = TropicalCycloneClasses.FromCode(code),
            ClassCode = code,
            ClassLetter = record.Text("DVLBL") ?? string.Empty,
            ClassDescription = record.Text("TCDVLP") ?? string.Empty,
            SaffirSimpsonCategory = category is >= 1 and <= 5 ? category : null,
            MotionDirectionDegrees = record.Number("TCDIR"),
            MotionSpeedKnots = record.Number("TCSPD"),
            Radii = radiiByTau.TryGetValue(tau, out var radii) ? radii : [],
        };
    }

    private static IReadOnlyList<TropicalCycloneTrackPoint> ReadBestTrack(Stream zip, DateTime until)
    {
        var layers = ReadZip(zip, "best track", "_pts", "_radii");
        if (!layers.TryGetValue("_pts", out var points))
            return [];

        var radiiByTime = layers.TryGetValue("_radii", out var radii)
            ? radii.Records
                .Select(r => (Time: ParseDtg(r.Text("SYNOPTIME")), Record: r))
                .Where(r => r.Time is not null)
                .GroupBy(r => r.Time!.Value)
                .ToDictionary(g => g.Key, g => Radii(g.Select(r => r.Record)))
            : [];

        var track = new List<TropicalCycloneTrackPoint>();
        foreach (var record in points.Records)
        {
            if (Position(record) is not { } position || BestTrackTime(record) is not { } time || time > until)
                continue;
            var code = record.Text("STORMTYPE") ?? string.Empty;
            var category = (int?)record.Number("SS");
            track.Add(new TropicalCycloneTrackPoint
            {
                Time = time,
                Position = position,
                MaxWindKnots = Knots(record.Number("INTENSITY")),
                PressureHectopascals = Pressure(record.Number("MSLP")),
                Classification = TropicalCycloneClasses.FromCode(code),
                ClassCode = code,
                SaffirSimpsonCategory = category is >= 1 and <= 5 ? category : null,
                Name = record.Text("STORMNAME") ?? string.Empty,
                Radii = radiiByTime.TryGetValue(time, out var r) ? r : [],
            });
        }

        return [.. track.OrderBy(p => p.Time)];
    }

    // The radii of one time, thresholds ascending, all-zero rows left out.
    private static IReadOnlyList<TropicalCycloneWindRadii> Radii(IEnumerable<ShapefileRecord> records) =>
    [
        .. records
            .Select(r => new TropicalCycloneWindRadii(
                (int)Math.Round(r.Number("RADII") ?? 0),
                r.Number("NE") ?? 0,
                r.Number("SE") ?? 0,
                r.Number("SW") ?? 0,
                r.Number("NW") ?? 0,
                r.Geometry is ShapePolygon polygon ? polygon.Surfaces : []))
            .Where(r => r.ThresholdKnots > 0 && !r.IsEmpty)
            .OrderBy(r => r.ThresholdKnots),
    ];

    private static GeoPosition? Position(ShapefileRecord record)
    {
        if (record.Geometry is ShapePoint point
            && double.IsFinite(point.Position.Latitude) && double.IsFinite(point.Position.Longitude))
        {
            return point.Position;
        }

        return record.Number("LAT") is { } lat && record.Number("LON") is { } lon
            ? new GeoPosition(lat, lon)
            : null;
    }

    private static int? Knots(double? value) => value is { } v && v >= 0 ? (int)Math.Round(v) : null;

    private static int? Pressure(double? value) => value is { } v && v > 0 ? (int)Math.Round(v) : null;

    private static DateTime? BestTrackTime(ShapefileRecord record)
    {
        if (record.Number("YEAR") is { } year && record.Number("MONTH") is { } month && record.Number("DAY") is { } day)
        {
            var hhmm = (int)(record.Number("HHMM") ?? 0);
            try
            {
                return new DateTime((int)year, (int)month, (int)day, hhmm / 100, hhmm % 100, 0, DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Fall through to the DTG.
            }
        }

        return ParseDtg(record.Text("DTG"));
    }

    /// <summary>Parses a <c>yyyyMMddHH</c> date-time group (<c>"2022092803"</c> or <c>"2022092218.0"</c>) as UTC.</summary>
    internal static DateTime? ParseDtg(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var digits = text.Split('.')[0].Trim();
        return digits.Length == 10
            && DateTime.TryParseExact(digits, "yyyyMMddHH", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
                ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
                : null;
    }

    /// <summary>
    /// Parses NHC's <c>ADVDATE</c>, for example <c>"1100 PM EDT Tue Sep 27 2022"</c>
    /// or <c>"500 AM HST Mon Aug 07 2023"</c>, with the offset of its zone.
    /// </summary>
    internal static DateTimeOffset? ParseAdvisoryDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || AdvisoryDateRegex().Match(text.Trim()) is not { Success: true } m)
            return null;
        if (!ZoneOffsets.TryGetValue(m.Groups["zone"].Value, out var offset))
            return null;

        var hhmm = int.Parse(m.Groups["hh"].Value + m.Groups["mm"].Value, CultureInfo.InvariantCulture);
        var hour = hhmm / 100;
        var minute = hhmm % 100;
        if (hour is < 1 or > 12 || minute > 59)
            return null;
        var pm = string.Equals(m.Groups["ampm"].Value, "PM", StringComparison.OrdinalIgnoreCase);
        hour = (hour % 12) + (pm ? 12 : 0);

        if (!DateTime.TryParseExact(
                $"{m.Groups["month"].Value} {m.Groups["day"].Value.PadLeft(2, '0')} {m.Groups["year"].Value}",
                "MMM dd yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return null;
        }

        return new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, offset);
    }

    /// <summary>
    /// Parses a <c>VALIDTIME</c> of day and time in UTC (<c>"28/0000"</c>) in
    /// the month that puts it nearest <paramref name="near"/>.
    /// </summary>
    internal static DateTime? ParseDayTime(string? text, DateTime near)
    {
        if (string.IsNullOrWhiteSpace(text) || DayTimeRegex().Match(text.Trim()) is not { Success: true } m)
            return null;
        var day = int.Parse(m.Groups["day"].Value, CultureInfo.InvariantCulture);
        var hour = int.Parse(m.Groups["hh"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(m.Groups["mm"].Value, CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59)
            return null;

        DateTime? best = null;
        for (var shift = -1; shift <= 1; shift++)
        {
            var month = new DateTime(near.Year, near.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(shift);
            if (day < 1 || day > DateTime.DaysInMonth(month.Year, month.Month))
                continue;
            var candidate = new DateTime(month.Year, month.Month, day, hour, minute, 0, DateTimeKind.Utc);
            if (best is null || Math.Abs((candidate - near).Ticks) < Math.Abs((best.Value - near).Ticks))
                best = candidate;
        }

        return best;
    }

    [GeneratedRegex(@"^(?<hh>\d{1,2}):?(?<mm>\d{2})\s*(?<ampm>AM|PM)\s+(?<zone>[A-Za-z]{1,4})\s+[A-Za-z]{3}\s+(?<month>[A-Za-z]{3})\s+(?<day>\d{1,2})\s+(?<year>\d{4})$", RegexOptions.IgnoreCase)]
    private static partial Regex AdvisoryDateRegex();

    [GeneratedRegex(@"^(?<day>\d{1,2})/(?<hh>\d{2})(?<mm>\d{2})$")]
    private static partial Regex DayTimeRegex();

    [GeneratedRegex(@"^(?<id>[A-Za-z]{2}[0-9]{6})-(?<adv>[0-9]{3}[A-Za-z]?)_", RegexOptions.IgnoreCase)]
    private static partial Regex EntryIdentityRegex();

    [GeneratedRegex(@"^[A-Za-z]{2}[0-9]{6}-[0-9]{3}[A-Za-z]?_(5day_(lin|pgn|pts)|ww_wwlin)\.", RegexOptions.IgnoreCase)]
    private static partial Regex ForecastEntryRegex();
}
