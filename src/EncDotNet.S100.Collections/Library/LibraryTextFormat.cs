using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Collections.Library;

/// <summary>How a host shows times to its user: in their time zone or in UTC (#730).</summary>
public enum LibraryTimeFormat
{
    /// <summary>The user's local time zone, in the current culture's short formats.</summary>
    Local,

    /// <summary>UTC, marked with a <c>Z</c>.</summary>
    Utc,
}

/// <summary>
/// Formats the values the Library shows (sizes, positions, run times, ages,
/// property labels, URLs) the same way in every host (#792).
/// </summary>
public static partial class LibraryTextFormat
{
    /// <summary>A byte count for display, e.g. <c>1.6 MB</c>.</summary>
    /// <param name="bytes">The byte count.</param>
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes} B")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }

    /// <summary>A position in degrees and decimal minutes, e.g. <c>37°48.500'N  122°24.000'W</c>.</summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    public static string LatLon(double latitude, double longitude) =>
        $"{DegreesMinutes(latitude, 'N', 'S', 2)}  {DegreesMinutes(longitude, 'E', 'W', 3)}";

    /// <summary>A latitude or longitude in degrees and decimal minutes, e.g. <c>37°48.500'N</c>.</summary>
    /// <param name="value">The value in decimal degrees.</param>
    /// <param name="positive">The hemisphere letter for a positive value.</param>
    /// <param name="negative">The hemisphere letter for a negative value.</param>
    /// <param name="degreeWidth">The digits the degrees are padded to (2 for latitude, 3 for longitude).</param>
    public static string DegreesMinutes(double value, char positive, char negative, int degreeWidth)
    {
        var hemi = value >= 0 ? positive : negative;
        var abs = Math.Abs(value);
        var deg = (int)Math.Floor(abs);
        var min = (abs - deg) * 60.0;
        // Guard against floating-point rounding pushing minutes to 60.000.
        if (min >= 60.0)
        {
            deg += 1;
            min = 0.0;
        }

        var degText = deg.ToString(CultureInfo.InvariantCulture).PadLeft(degreeWidth, '0');
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{degText}°{min.ToString("00.000", CultureInfo.InvariantCulture)}'{hemi}");
    }

    /// <summary>A forecast run time in UTC: the culture's short date and <c>HH:mmZ</c>.</summary>
    /// <param name="time">The run time.</param>
    public static string Run(DateTimeOffset time) =>
        time.UtcDateTime.ToString("d", CultureInfo.CurrentCulture) + " " + time.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "Z";

    /// <summary>A forecast run time as the user reads it: their Local/UTC setting (#730).</summary>
    /// <param name="time">The run time.</param>
    /// <param name="format">Whether to show local time or UTC.</param>
    /// <param name="zone">The user's time zone.</param>
    public static string Run(DateTimeOffset time, LibraryTimeFormat format, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (format == LibraryTimeFormat.Utc)
            return Run(time);
        var local = TimeZoneInfo.ConvertTimeFromUtc(time.UtcDateTime, zone);
        return local.ToString("d", CultureInfo.CurrentCulture) + " " + local.ToString("t", CultureInfo.CurrentCulture);
    }

    /// <summary>"39 h left", or "Ended 9 h ago".</summary>
    /// <param name="end">The end of the window.</param>
    /// <param name="now">The current time.</param>
    public static string TimeLeft(DateTimeOffset end, DateTimeOffset now) => TimeLeft(end - now);

    /// <summary>"39 h left" (or days past 72 h), or "Ended 9 h ago" for a negative span.</summary>
    /// <param name="span">The time left.</param>
    public static string TimeLeft(TimeSpan span)
    {
        var c = CultureInfo.CurrentCulture;
        var past = span < TimeSpan.Zero;
        var hours = (int)Math.Floor(Math.Abs(span.TotalHours));
        var amount = hours >= 72
            ? string.Format(c, LibraryText.Get("Library_Forecast_DaysFormat"), hours / 24)
            : string.Format(c, LibraryText.Get("Library_Forecast_HoursFormat"), Math.Max(hours, past ? 0 : 1));
        return string.Format(c, LibraryText.Get(past ? "Library_Forecast_EndedFormat" : "Library_Forecast_LeftFormat"), amount);
    }

    /// <summary>"12 min", "2 h", "3 days".</summary>
    /// <param name="age">The age.</param>
    public static string Age(TimeSpan age) => age switch
    {
        { TotalHours: < 1 } => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_AgeMinutesFormat"), Math.Max(1, (int)age.TotalMinutes)),
        { TotalHours: < 48 } => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_AgeHoursFormat"), (int)age.TotalHours),
        _ => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_AgeDaysFormat"), (int)age.TotalDays),
    };

    /// <summary>The time when <paramref name="when"/> is today (local), else the date.</summary>
    /// <param name="when">The local time.</param>
    public static string When(DateTimeOffset when) =>
        when.Date == DateTime.Today
            ? when.ToString("t", CultureInfo.CurrentCulture)
            : when.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>A readable label for an item property key: curated, else the camelCase key split into words.</summary>
    /// <param name="key">The property key.</param>
    public static string PropertyLabel(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (LibraryText.PropertyLabel(key) is { } label)
            return label;

        var words = new StringBuilder(key.Length + 4);
        for (var i = 0; i < key.Length; i++)
        {
            var ch = key[i];
            if (i == 0)
                words.Append(char.ToUpperInvariant(ch));
            else if (char.IsUpper(ch) && !char.IsUpper(key[i - 1]))
                words.Append(' ').Append(char.ToLowerInvariant(ch));
            else
                words.Append(ch);
        }

        return words.ToString();
    }

    /// <summary>"S-57 · ENC cell", "S-101 · Electronic Navigational Chart (2.0.0)".</summary>
    /// <param name="item">The item.</param>
    public static string Product(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var name = item.ProductSpec == "S-57" ? LibraryText.Get("Library_Product_S57") : LibraryText.SpecDisplayName(item.ProductSpec);
        var text = name is null ? item.ProductSpec : $"{item.ProductSpec} · {name}";
        return item.ProductSpecVersion is { } version ? $"{text} ({version})" : text;
    }

    /// <summary>"ienccloud.us · U37IL257.zip": the host and file of a download URL.</summary>
    /// <param name="uri">The URL.</param>
    public static string ShortUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var file = Path.GetFileName(uri.AbsolutePath);
        return string.IsNullOrEmpty(file) ? uri.Host : $"{uri.Host} · {Uri.UnescapeDataString(file)}";
    }

    /// <summary>
    /// A shared feed's URL with its access token (the path before
    /// <c>feed.json</c>) masked to its last four characters:
    /// <c>http://bridge-pc:8100/••••3f9a/feed.json</c>.
    /// </summary>
    /// <param name="feedUri">The feed URL.</param>
    public static string MaskToken(Uri feedUri)
    {
        ArgumentNullException.ThrowIfNull(feedUri);
        var segments = feedUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return feedUri.AbsoluteUri;
        var masked = segments[..^1].Select(s => "••••" + (s.Length > 4 ? s[^4..] : string.Empty)).Append(segments[^1]);
        return $"{feedUri.Scheme}://{feedUri.Authority}/{string.Join('/', masked)}";
    }

    /// <summary>A community-list package's description without its leading timestamp ("23.10.2025 15:17 - …").</summary>
    /// <param name="title">The package's title.</param>
    public static string PackageTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var cleaned = LeadingTimestamp().Replace(title, string.Empty).Trim();
        return cleaned.Length == 0 ? title : cleaned;
    }

    [GeneratedRegex(@"^\s*\d{1,2}\.\d{1,2}\.\d{4}(\s+\d{1,2}:\d{2}(:\d{2})?)?\s*[-–—]\s*")]
    private static partial Regex LeadingTimestamp();

    /// <summary>"1:22,000 – 1:90,000", "≤ 1:90,000", "≥ 1:22,000", or null for neither.</summary>
    /// <param name="minimum">The minimum display scale denominator.</param>
    /// <param name="maximum">The maximum display scale denominator.</param>
    public static string? Scales(int? minimum, int? maximum)
    {
        var c = CultureInfo.CurrentCulture;
        return (minimum, maximum) switch
        {
            (null, null) => null,
            ({ } min, { } max) => $"1:{min.ToString("N0", c)} – 1:{max.ToString("N0", c)}",
            ({ } min, null) => $"≤ 1:{min.ToString("N0", c)}",
            (null, { } max) => $"≥ 1:{max.ToString("N0", c)}",
        };
    }
}
