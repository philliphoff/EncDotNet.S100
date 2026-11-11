using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Reads the WPC coded surface bulletins: the coded surface analysis
/// (<c>CODSUS</c>) at standard (1°) and high (0.1°) resolution, and the coded
/// forecast bulletins (for example the short-range <c>CODSRP</c>), into
/// <see cref="SurfaceAnalysis"/> models (design note <c>vector-weather.md</c>
/// §8.3, V35). Implemented from WPC's format pages ("Reading the Coded Surface
/// Bulletin", its high-resolution and short-range forecast versions) and
/// checked against the bulletins NOAA publishes.
/// </summary>
/// <remarks>
/// <para><b>The format.</b></para>
/// <list type="bullet">
/// <item><description>
/// An optional header: the WMO heading (<c>ASUS02 KWBC 040000</c>), the
/// product identifier (<c>CODSUS</c>), the title, the office and the issuance
/// line (<c>834 PM EST TUE NOV 03 2026</c>). WPC's own examples start at the
/// valid line.
/// </description></item>
/// <item><description>
/// A valid line. The analysis says <c>VALID 110400Z</c> (month, day, hour UTC).
/// A forecast bulletin has one section per forecast time, each opened by
/// <c>12HR PROG VALID 051200Z</c> (day, hour, minute UTC) or, in WPC's
/// example, <c>SURFACE PROG VALID 201104081800Z</c> (year to minute). The
/// year and month the short forms leave out come from the issuance line,
/// else the WMO heading's day, else the reference time the caller gives,
/// whichever is first available; the reader picks the nearest date that fits.
/// </description></item>
/// <item><description>
/// <c>HIGHS</c> and <c>LOWS</c>: pairs of central pressure (whole hPa) and
/// position.
/// </description></item>
/// <item><description>
/// <c>COLD</c>, <c>WARM</c>, <c>OCFNT</c>, <c>STNRY</c> and <c>TROF</c>: an
/// optional intensity (<c>WK</c>, <c>MDT</c>, <c>STG</c>; troughs have none)
/// and the line's points.
/// </description></item>
/// <item><description>
/// A position is 4 or 5 digits at standard resolution (latitude north in
/// whole degrees, then longitude west, <c>41115</c> = 41°N 115°W) or 7 digits
/// at high resolution (tenths: <c>4051145</c> = 40.5°N 114.5°W). A longitude
/// over 180°W is east of the antimeridian. Mixing the two in one bulletin is
/// an error.
/// </description></item>
/// <item><description>
/// Lines wrap anywhere, even between a pressure and its position, and a code
/// may start mid-line, so the body is read as one stream of
/// whitespace-separated tokens. It ends at <c>$$</c>, <c>**</c>, <c>....</c>
/// or <c>NNNN</c>, or at the end of the text.
/// </description></item>
/// <item><description>
/// A code the reader doesn't know is skipped with its points and listed in
/// <see cref="CodedSurfaceBulletin.SkippedCodes"/>.
/// </description></item>
/// </list>
/// <para>
/// <b>Which side the pips go on: the left of the point order.</b> WPC's pages
/// don't say. The rule comes from the bulletins: walking a front from its first
/// point to its last, the side it moves towards is on the left. Evidence:
/// the 15 high-resolution analyses NOAA issued from 2026-11-03 09Z to
/// 2026-11-05 03Z (as archived by the Iowa Environmental Mesonet), taking each
/// front whose first point is within 1° of a low, where the way it moves is
/// not in doubt:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Cold fronts trail south or south-west from their low and move east. For
/// 66 of 68, the left of the point order faces east.
/// </description></item>
/// <item><description>
/// Warm fronts run east or south-east from their low and move north. For 26
/// of 30, the left of the point order faces north.
/// </description></item>
/// <item><description>
/// Occluded fronts are listed outwards from their low too. For 51 of 59, the
/// left faces north.
/// </description></item>
/// </list>
/// <para>
/// With pips on the right instead, nearly every cold front would point west,
/// into the cold air behind it. WPC's high-resolution example agrees: its cold
/// and warm fronts both start at 56.3–56.4°N 137.4–137.5°W, the cold front
/// running south (its left faces east) and the warm front south-east (its left
/// faces north-east). A stationary front's points give a direction too, but
/// which of its two symbols goes on which side isn't in the bulletin. The
/// tests check the rule against the committed fixture.
/// </para>
/// </remarks>
public static partial class CodedSurfaceBulletinReader
{
    // A file is read whole; WPC's bulletins are a few kilobytes.
    private const int MaxBytes = 4 * 1024 * 1024;

    private static readonly string[] EndMarkers = ["$$", "**", "....", "NNNN"];

    /// <summary>
    /// Reads the bulletin at <paramref name="path"/>. When the bulletin has no
    /// header to date it (WPC's own examples don't), the file's last-write time
    /// stands in for when it was issued.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <exception cref="CodedSurfaceBulletinFormatException">The file isn't a coded surface bulletin, or can't be decoded.</exception>
    public static CodedSurfaceBulletin Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
    }

    /// <summary>Reads a bulletin from <paramref name="stream"/>, which stays open.</summary>
    /// <param name="stream">The bulletin's bytes, ASCII.</param>
    /// <param name="referenceTime">
    /// A time near when the bulletin was issued, used to date it only when its
    /// header doesn't (no issuance line); <see langword="null"/> when there's
    /// none, in which case a bulletin without one can't be read.
    /// </param>
    /// <exception cref="CodedSurfaceBulletinFormatException">The bytes aren't a coded surface bulletin, or can't be decoded.</exception>
    public static CodedSurfaceBulletin Read(Stream stream, DateTimeOffset? referenceTime)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
                throw new CodedSurfaceBulletinFormatException($"The file is over {MaxBytes / (1024 * 1024)} MB, too large for a coded surface bulletin.");
            buffer.Write(chunk, 0, read);
        }

        return Parse(Encoding.ASCII.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), referenceTime);
    }

    /// <summary>Reads a bulletin from its text.</summary>
    /// <param name="text">The bulletin.</param>
    /// <param name="referenceTime">
    /// A time near when the bulletin was issued, used to date it only when its
    /// header doesn't; <see langword="null"/> when there's none.
    /// </param>
    /// <exception cref="CodedSurfaceBulletinFormatException">The text isn't a coded surface bulletin, or can't be decoded.</exception>
    public static CodedSurfaceBulletin Parse(string text, DateTimeOffset? referenceTime)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Parser(text, referenceTime).Parse();
    }

    /// <summary>
    /// Whether <paramref name="leadingBytes"/>, a file's first bytes, look like
    /// a coded surface bulletin: ASCII text naming the product
    /// (<c>CODSUS</c>, <c>CODSRP</c> or another <c>COD</c> identifier on its own
    /// line, or the title <c>CODED SURFACE FRONTAL POSITIONS</c>), or text that
    /// opens with a valid line followed by a feature code, as WPC's examples
    /// do.
    /// </summary>
    /// <param name="leadingBytes">The file's first bytes (a kilobyte is plenty).</param>
    public static bool LooksLikeCodedSurfaceBulletin(ReadOnlySpan<byte> leadingBytes)
    {
        if (leadingBytes.IsEmpty)
            return false;
        foreach (var b in leadingBytes)
        {
            // Text only: printable ASCII, whitespace and the NOAAport framing bytes.
            if (b > 0x7E || (b < 0x20 && b is not ((byte)'\n' or (byte)'\r' or (byte)'\t' or 0x01 or 0x03)))
                return false;
        }

        var text = Encoding.ASCII.GetString(leadingBytes);
        return TitleRegex().IsMatch(text) || ProductIdRegex().IsMatch(text) || BareStartRegex().IsMatch(text);
    }

    private static bool IsEndMarker(string line) => Array.IndexOf(EndMarkers, line) >= 0;

    private static SurfaceFrontType? FrontTypeOf(string token) => token switch
    {
        "COLD" => SurfaceFrontType.Cold,
        "WARM" => SurfaceFrontType.Warm,
        "OCFNT" => SurfaceFrontType.Occluded,
        "STNRY" => SurfaceFrontType.Stationary,
        "TROF" => SurfaceFrontType.Trough,
        _ => null,
    };

    private static SurfaceFrontStrength? StrengthOf(string token) => token switch
    {
        "WK" => SurfaceFrontStrength.Weak,
        "MDT" => SurfaceFrontStrength.Moderate,
        "STG" => SurfaceFrontStrength.Strong,
        _ => null,
    };

    private static TimeSpan? ZoneOffset(string zone) => zone switch
    {
        "UTC" or "GMT" or "Z" => TimeSpan.Zero,
        "EST" => TimeSpan.FromHours(-5),
        "EDT" => TimeSpan.FromHours(-4),
        "CST" => TimeSpan.FromHours(-6),
        "CDT" => TimeSpan.FromHours(-5),
        "MST" => TimeSpan.FromHours(-7),
        "MDT" => TimeSpan.FromHours(-6),
        "PST" => TimeSpan.FromHours(-8),
        "PDT" => TimeSpan.FromHours(-7),
        "AKST" => TimeSpan.FromHours(-9),
        "AKDT" => TimeSpan.FromHours(-8),
        "HST" => TimeSpan.FromHours(-10),
        _ => null,
    };

    [GeneratedRegex(@"CODED\s+SURFACE\s+FRONTAL\s+POSITIONS", RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"^[\x01\s]*(?:\d+\s*\n)?[A-Z]{4}\d{2} [A-Z]{4} \d{6}[^\n]*\n+COD[A-Z]{3}\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ProductIdRegex();

    [GeneratedRegex(@"^\s*(?:(?:\d+HR|SURFACE)\s+PROG\s+)?VALID\s+\d{6,12}Z?\s+(?:HIGHS|LOWS|COLD|WARM|OCFNT|STNRY|TROF)\b", RegexOptions.CultureInvariant)]
    private static partial Regex BareStartRegex();

    [GeneratedRegex(@"^(?:(?<hours>\d+)HR\s+PROG\s+|(?<prog>SURFACE\s+PROG)\s+)?VALID\s+(?<time>\d+)Z?$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidLineRegex();

    [GeneratedRegex(@"^[A-Z]{4}\d{2} [A-Z]{4} (?<day>\d{2})(?<hour>\d{2})(?<minute>\d{2})(?: [A-Z]{3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex WmoHeadingRegex();

    [GeneratedRegex(@"^COD[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex AwipsIdRegex();

    [GeneratedRegex(@"^(?<hhmm>\d{3,4}) (?<ampm>AM|PM) (?<zone>[A-Z]{1,4}) [A-Z]{3} (?<month>[A-Z]{3}) (?<day>\d{1,2}) (?<year>\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex IssuanceRegex();

    private sealed class Parser
    {
        private readonly string[] _lines;
        private readonly DateTimeOffset? _referenceTime;
        private readonly List<string> _skipped = [];
        private CodedSurfaceBulletinResolution? _resolution;
        private int _lineNumber;

        public Parser(string text, DateTimeOffset? referenceTime)
        {
            _lines = text.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\u0001", string.Empty, StringComparison.Ordinal)
                .Replace("\u0003", string.Empty, StringComparison.Ordinal)
                .Split('\n');
            _referenceTime = referenceTime;
        }

        public CodedSurfaceBulletin Parse()
        {
            var header = ReadHeader(out int firstValidLine);
            if (firstValidLine < 0)
                throw new CodedSurfaceBulletinFormatException("No VALID line: this isn't a coded surface bulletin.");

            var reference = ReferenceTime(header);
            var sections = new List<(Match Valid, int Line, List<(string Token, int Line)> Tokens)>();
            for (_lineNumber = firstValidLine; _lineNumber < _lines.Length; _lineNumber++)
            {
                var line = _lines[_lineNumber].Trim();
                if (IsEndMarker(line))
                    break;
                if (line.Length == 0)
                    continue;
                var valid = ValidLineRegex().Match(line);
                if (valid.Success)
                {
                    sections.Add((valid, _lineNumber + 1, []));
                    continue;
                }

                foreach (var token in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    sections[^1].Tokens.Add((token, _lineNumber + 1));
            }

            bool isForecast = sections.Any(s => s.Valid.Groups["hours"].Success || s.Valid.Groups["prog"].Success)
                || (header.Title?.Contains("FORECAST", StringComparison.Ordinal) ?? false);
            var centre = header.IssuingCentre ?? CodedSurfaceBulletinHeader.WpcName;
            var analyses = sections
                .Select(s => ReadSection(s.Valid, s.Line, s.Tokens, isForecast, reference, centre))
                .ToArray();

            return new CodedSurfaceBulletin(
                isForecast ? CodedSurfaceBulletinKind.Forecast : CodedSurfaceBulletinKind.Analysis,
                _resolution ?? CodedSurfaceBulletinResolution.HighResolution,
                analyses,
                header,
                _skipped);
        }

        private CodedSurfaceBulletinHeader ReadHeader(out int firstValidLine)
        {
            firstValidLine = -1;
            string? wmo = null, productId = null, title = null, centre = null;
            DateTimeOffset? issued = null;
            for (int i = 0; i < _lines.Length; i++)
            {
                var line = _lines[i].Trim();
                if (ValidLineRegex().IsMatch(line))
                {
                    firstValidLine = i;
                    break;
                }

                if (line.Length == 0)
                    continue;
                if (wmo is null && WmoHeadingRegex().IsMatch(line))
                {
                    wmo = line;
                }
                else if (productId is null && AwipsIdRegex().IsMatch(line))
                {
                    productId = line;
                }
                else if (title is null && TitleRegex().IsMatch(line))
                {
                    title = line;
                }
                else if (centre is null && title is not null && line.Contains("CENTER", StringComparison.Ordinal))
                {
                    centre = line;
                }
                else if (issued is null && IssuanceRegex().Match(line) is { Success: true } m)
                {
                    issued = ParseIssuance(m, i + 1);
                }
            }

            return new CodedSurfaceBulletinHeader
            {
                WmoHeading = wmo,
                ProductId = productId,
                Title = title,
                IssuingCentre = centre,
                IssuedAt = issued,
            };
        }

        private static DateTimeOffset ParseIssuance(Match m, int line)
        {
            var hhmm = int.Parse(m.Groups["hhmm"].Value, CultureInfo.InvariantCulture);
            int hour = hhmm / 100 % 12 + (m.Groups["ampm"].Value == "PM" ? 12 : 0);
            int minute = hhmm % 100;
            var zone = m.Groups["zone"].Value;
            if (ZoneOffset(zone) is not { } offset)
                throw new CodedSurfaceBulletinFormatException($"Line {line}: unknown time zone '{zone}' in the issuance line.");
            if (!DateTime.TryParseExact(
                    $"{m.Groups["year"].Value} {m.Groups["month"].Value} {m.Groups["day"].Value}",
                    "yyyy MMM d",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date)
                || minute > 59)
            {
                throw new CodedSurfaceBulletinFormatException($"Line {line}: can't read the issuance time '{m.Value}'.");
            }

            return new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, offset);
        }

        // The time the short valid forms are resolved against.
        private DateTimeOffset? ReferenceTime(CodedSurfaceBulletinHeader header)
        {
            if (header.IssuedAt is { } issued)
                return issued.ToUniversalTime();
            if (header.WmoHeading is { } wmo && _referenceTime is { } reference)
            {
                // The heading's day and time, in the month nearest the caller's reference.
                var m = WmoHeadingRegex().Match(wmo);
                return Nearest(
                    reference.ToUniversalTime(),
                    MonthsAround(reference.ToUniversalTime()),
                    (year, month) => TryCreate(
                        year,
                        month,
                        Int(m.Groups["day"].Value),
                        Int(m.Groups["hour"].Value),
                        Int(m.Groups["minute"].Value))) ?? reference.ToUniversalTime();
            }

            return _referenceTime?.ToUniversalTime();
        }

        private SurfaceAnalysis ReadSection(
            Match valid,
            int line,
            List<(string Token, int Line)> tokens,
            bool isForecast,
            DateTimeOffset? reference,
            string centre)
        {
            int? hours = valid.Groups["hours"].Success ? Int(valid.Groups["hours"].Value) : null;
            var validTime = ResolveValidTime(valid.Groups["time"].Value, isForecast, hours, reference, line);

            var fronts = new List<SurfaceFront>();
            var centres = new List<PressureCentre>();
            int i = 0;
            while (i < tokens.Count)
            {
                var (token, tokenLine) = tokens[i++];
                if (token is "HIGHS" or "LOWS")
                {
                    var kind = token == "HIGHS" ? PressureCentreKind.High : PressureCentreKind.Low;
                    while (i < tokens.Count && IsDigits(tokens[i].Token))
                    {
                        var (pressure, pressureLine) = tokens[i++];
                        if (i >= tokens.Count || !IsDigits(tokens[i].Token))
                            throw new CodedSurfaceBulletinFormatException($"Line {pressureLine}: the {token} pressure {pressure} has no position after it.");
                        if (pressure.Length is < 3 or > 4)
                            throw new CodedSurfaceBulletinFormatException($"Line {pressureLine}: '{pressure}' isn't a pressure in whole hPa.");
                        var (position, positionLine) = tokens[i++];
                        centres.Add(new PressureCentre(kind, Int(pressure), Position(position, positionLine)));
                    }
                }
                else if (FrontTypeOf(token) is { } type)
                {
                    var strength = SurfaceFrontStrength.Unspecified;
                    if (i < tokens.Count && StrengthOf(tokens[i].Token) is { } given)
                    {
                        strength = given;
                        i++;
                    }

                    var points = new List<GeoPosition>();
                    while (i < tokens.Count && IsDigits(tokens[i].Token))
                    {
                        var (position, positionLine) = tokens[i++];
                        points.Add(Position(position, positionLine));
                    }

                    if (points.Count == 0)
                        throw new CodedSurfaceBulletinFormatException($"Line {tokenLine}: {token} has no points.");
                    fronts.Add(new SurfaceFront(type, strength, points));
                }
                else if (IsDigits(token))
                {
                    throw new CodedSurfaceBulletinFormatException($"Line {tokenLine}: '{token}' isn't after a feature code.");
                }
                else
                {
                    // A code this reader doesn't know: skip it and its points.
                    _skipped.Add(token);
                    while (i < tokens.Count && IsDigits(tokens[i].Token))
                        i++;
                }
            }

            return new SurfaceAnalysis(validTime, hours, centre, fronts, centres);
        }

        private DateTimeOffset ResolveValidTime(string digits, bool isForecast, int? hours, DateTimeOffset? reference, int line)
        {
            if (digits.Length == 12)
            {
                return TryCreate(Int(digits[..4]), Int(digits[4..6]), Int(digits[6..8]), Int(digits[8..10]), Int(digits[10..12]))
                    ?? throw new CodedSurfaceBulletinFormatException($"Line {line}: '{digits}' isn't a valid time.");
            }

            if (digits.Length != 6)
                throw new CodedSurfaceBulletinFormatException($"Line {line}: '{digits}' isn't a valid time (MMDDHH, DDHHMM or YYYYMMDDHHMM).");
            if (reference is not { } r)
            {
                throw new CodedSurfaceBulletinFormatException(
                    $"Line {line}: the bulletin has no issuance line to date 'VALID {digits}Z' with, and no reference time was given.");
            }

            int a = Int(digits[..2]), b = Int(digits[2..4]), c = Int(digits[4..6]);
            DateTimeOffset? result = isForecast
                // DDHHMM: the month nearest the issue time plus the lead time.
                ? Nearest(r.AddHours(hours ?? 0), MonthsAround(r), (year, month) => TryCreate(year, month, a, b, c))
                // MMDDHH: the year nearest the issue time.
                : Nearest(r, [(r.Year - 1, a), (r.Year, a), (r.Year + 1, a)], (year, month) => TryCreate(year, month, b, c, 0));
            return result ?? throw new CodedSurfaceBulletinFormatException($"Line {line}: '{digits}' isn't a valid time.");
        }

        private GeoPosition Position(string token, int line)
        {
            CodedSurfaceBulletinResolution resolution;
            double latitude, westLongitude;
            switch (token.Length)
            {
                case 4 or 5:
                    resolution = CodedSurfaceBulletinResolution.Standard;
                    latitude = Int(token[..2]);
                    westLongitude = Int(token[2..]);
                    break;
                case 7:
                    resolution = CodedSurfaceBulletinResolution.HighResolution;
                    latitude = Int(token[..3]) / 10.0;
                    westLongitude = Int(token[3..]) / 10.0;
                    break;
                default:
                    throw new CodedSurfaceBulletinFormatException($"Line {line}: '{token}' isn't a position (4 or 5 digits, or 7 at high resolution).");
            }

            if (_resolution is null)
            {
                _resolution = resolution;
            }
            else if (_resolution != resolution)
            {
                throw new CodedSurfaceBulletinFormatException(
                    $"Line {line}: '{token}' is a {Describe(resolution)} position in a bulletin of {Describe(_resolution.Value)} ones.");
            }

            if (latitude > 90 || westLongitude >= 360)
                throw new CodedSurfaceBulletinFormatException($"Line {line}: '{token}' is off the globe.");

            // Degrees west, positive east in -180..180: 181°W is 179°E.
            return new GeoPosition(latitude, westLongitude > 180 ? 360 - westLongitude : -westLongitude);
        }

        private static string Describe(CodedSurfaceBulletinResolution resolution) =>
            resolution == CodedSurfaceBulletinResolution.Standard ? "standard (whole-degree)" : "high-resolution (tenth-degree)";

        private static IEnumerable<(int Year, int Month)> MonthsAround(DateTimeOffset time)
        {
            for (int delta = -1; delta <= 1; delta++)
            {
                var month = new DateTime(time.Year, time.Month, 1).AddMonths(delta);
                yield return (month.Year, month.Month);
            }
        }

        private static DateTimeOffset? Nearest(
            DateTimeOffset target,
            IEnumerable<(int Year, int Month)> candidates,
            Func<int, int, DateTimeOffset?> create)
        {
            DateTimeOffset? best = null;
            foreach (var (year, month) in candidates)
            {
                if (create(year, month) is { } time
                    && (best is null || (time - target).Duration() < (best.Value - target).Duration()))
                {
                    best = time;
                }
            }

            return best;
        }

        private static DateTimeOffset? TryCreate(int year, int month, int day, int hour, int minute)
        {
            if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
                || hour > 23 || minute > 59)
            {
                return null;
            }

            return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
        }

        private static bool IsDigits(string token)
        {
            foreach (var ch in token)
            {
                if (ch is < '0' or > '9')
                    return false;
            }

            return token.Length > 0;
        }

        private static int Int(string digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
