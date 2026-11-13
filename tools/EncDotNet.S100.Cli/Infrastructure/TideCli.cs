using System.Globalization;
using System.Text.Json;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// What the <c>s100 tides</c> commands share: where the station catalogue is
/// kept, and parsing of positions, distances, steps and times.
/// </summary>
internal static class TideCli
{
    /// <summary>
    /// The offline catalogue in the user's local application data, which
    /// <c>s100 tides update</c> writes and the other commands and
    /// <c>s100 mcp serve</c> read.
    /// </summary>
    public static string DefaultCataloguePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EncDotNet.S100",
        "Tides",
        "noaa-tide-stations.json.gz");

    /// <summary>The <c>--catalogue</c> option's description.</summary>
    public const string CatalogueDescription =
        "The tide station catalogue: a tide-stations JSON file (.json, or .json.gz). Defaults to the offline cache 's100 tides update' writes.";

    /// <summary>The catalogue file a command uses.</summary>
    public static string CataloguePath(string? option) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(option) ? DefaultCataloguePath : option);

    /// <summary>The catalogue source for a path, saying how to get one when it's missing.</summary>
    public static TideCatalogueFileSource Source(string path) => new(path, MissingHint(path));

    /// <summary>What to say when the catalogue file isn't there.</summary>
    public static string MissingHint(string path) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultCataloguePath), StringComparison.Ordinal)
            ? $"Run 's100 tides update' to download NOAA's stations (about 10 minutes) into {path}, or pass --catalogue."
            : $"There's no catalogue at {path}. Run 's100 tides update --catalogue {path}' to download NOAA's stations there.";

    /// <summary>The catalogue's provenance and age, in a line.</summary>
    public static string DescribeCatalogue(TideCatalogueInfo info) =>
        Describe(info.Provider, info.Date, info.AgeDays, info.Stale, $", {info.StationCount} stations");

    /// <summary>A prediction's provenance and the catalogue's age at <paramref name="now"/>, in a line.</summary>
    public static string DescribeSource(string? provider, DateTimeOffset? date, DateTimeOffset now)
    {
        int? age = date is { } d ? Math.Max(0, (int)(now - d).TotalDays) : null;
        var stale = date is not { } retrieved || now - retrieved > TideStationCatalogue.OfflineStaleAfter;
        return Describe(provider, date, age, stale, string.Empty);
    }

    private static string Describe(string? provider, DateTimeOffset? date, int? ageDays, bool stale, string count)
    {
        var when = date is { } d
            ? $"retrieved {d.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({Days(ageDays ?? 0)} ago)"
            : "date unknown";
        var warning = stale ? ". It's over 12 months old or undated: run 's100 tides update' to refresh it" : string.Empty;
        return $"Stations: {provider ?? "unknown provider"}, {when}{count}{warning}.";
    }

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    /// <summary>Parses <c>lat,lon</c> in decimal degrees.</summary>
    public static bool TryParsePosition(string? text, out GeoPosition position)
    {
        position = default;
        var parts = (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return false;
        }

        position = new GeoPosition(latitude, longitude);
        return true;
    }

    /// <summary>
    /// Parses a distance in kilometres, or with a unit: <c>20</c>,
    /// <c>20km</c>, <c>5000m</c>, <c>10nm</c>.
    /// </summary>
    public static bool TryParseDistanceMeters(string? text, out double meters)
    {
        meters = 0;
        var value = (text ?? string.Empty).Trim().ToLowerInvariant();
        var factor = 1000.0;
        if (value.EndsWith("km", StringComparison.Ordinal))
        {
            value = value[..^2];
        }
        else if (value.EndsWith("nm", StringComparison.Ordinal))
        {
            value = value[..^2];
            factor = 1852;
        }
        else if (value.EndsWith('m'))
        {
            value = value[..^1];
            factor = 1;
        }

        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !(number > 0) || double.IsInfinity(number))
            return false;

        meters = number * factor;
        return true;
    }

    /// <summary>
    /// Parses a step: minutes (<c>10</c>), or with a unit (<c>10m</c>,
    /// <c>1h</c>, <c>90s</c>), or <c>hh:mm[:ss]</c>.
    /// </summary>
    public static bool TryParseStep(string? text, out TimeSpan step)
    {
        step = default;
        var value = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Contains(':', StringComparison.Ordinal))
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out step) && step > TimeSpan.Zero;

        Func<double, TimeSpan> unit = TimeSpan.FromMinutes;
        if (value.EndsWith("min", StringComparison.Ordinal))
            value = value[..^3];
        else if (value.EndsWith('m'))
            value = value[..^1];
        else if (value.EndsWith('h'))
            (value, unit) = (value[..^1], TimeSpan.FromHours);
        else if (value.EndsWith('s'))
            (value, unit) = (value[..^1], TimeSpan.FromSeconds);

        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !(number > 0) || number > 1_000_000)
            return false;

        step = TimeSpan.FromSeconds(Math.Round(unit(number).TotalSeconds));
        return step > TimeSpan.Zero;
    }

    /// <summary>
    /// Parses a time: ISO 8601 with or without an offset, or a date. A time
    /// with no offset is in <paramref name="zone"/>; a date is its midnight
    /// there.
    /// </summary>
    public static bool TryParseTime(string? text, TimeZoneInfo zone, out DateTimeOffset time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        var hasOffset = value.EndsWith('Z') || value.EndsWith('z')
            || (value.Length > 10 && value.LastIndexOfAny(['+', '-']) > 10);
        if (hasOffset)
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return false;

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        time = new DateTimeOffset(local, zone.GetUtcOffset(local));
        return true;
    }

    /// <summary>A time as the table and CSV show it: in the zone, with its offset.</summary>
    public static string FormatTime(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);

    /// <summary>A time as ISO 8601 in the zone.</summary>
    public static string FormatIsoTime(DateTimeOffset time, TimeZoneInfo zone) =>
        zone == TimeZoneInfo.Utc
            ? time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static readonly JsonSerializerOptions IndentedJson = new(McpJson.Options) { WriteIndented = true };

    /// <summary>A tool result as indented JSON, as the MCP tool returns it.</summary>
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, IndentedJson.GetTypeInfo(typeof(T)));

    /// <summary>Writes a tool error to standard error and returns the exit code for it.</summary>
    public static int Fail(ToolError error)
    {
        Console.Error.WriteLine($"Error: {error.Message}");
        if (error is TideStationNotFound { Candidates.Count: > 0 } notFound)
        {
            foreach (var candidate in notFound.Candidates)
                Console.Error.WriteLine($"  {candidate}");
        }

        return 2;
    }
}
