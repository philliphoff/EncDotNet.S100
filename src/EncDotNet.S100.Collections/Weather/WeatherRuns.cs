using System.Globalization;
using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>
/// Run facts of the Library's weather items (#1075; design note
/// weather-sources.md §6.3): models run every 6 hours, so an item records its
/// run (<see cref="S100ForecastFeedIndexer.RunProperty"/>, shared with the
/// S-100 forecast feeds so the Library's forecast handling applies), and a
/// run older than <see cref="StaleAfter"/> is stale, with "load latest run"
/// offered. A pinned run is a copy kept on purpose, so it isn't.
/// </summary>
public static class WeatherRuns
{
    /// <summary>How old a run may be before it is stale: 12 hours (two GFS cycles).</summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromHours(12);

    /// <summary>The item property that marks a weather run, holding the hours after which it is stale.</summary>
    public const string StaleAfterProperty = "staleAfterHours";

    /// <summary>The item property that marks a pinned run (a kept copy), <c>true</c>.</summary>
    public const string PinnedProperty = "pinned";

    /// <summary>The item property naming the data's licence.</summary>
    public const string LicenceProperty = "licence";

    /// <summary>The item property holding the attribution the provider asks for.</summary>
    public const string AttributionProperty = "attribution";

    /// <summary>The item property naming, on a pinned run, the managed folder it is kept in (relative to the downloads root).</summary>
    public const string DownloadFolderProperty = "downloadFolder";

    /// <summary>The item property naming the weather service kind (<c>gribFilter</c>, <c>gribIndex</c>, <c>nwsAlerts</c>, <c>erddapGriddap</c>).</summary>
    public const string ServiceProperty = "weatherService";

    /// <summary>True when <paramref name="item"/> is a weather item whose runs can go stale.</summary>
    public static bool IsWeatherRun(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Properties.ContainsKey(StaleAfterProperty) && S100ForecastFeedIndexer.RunOf(item) is not null;
    }

    /// <summary>True when <paramref name="item"/> is a pinned run.</summary>
    public static bool IsPinned(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Properties.TryGetValue(PinnedProperty, out var value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>How old a run may be for <paramref name="item"/> before it is stale (its own setting, else <see cref="StaleAfter"/>).</summary>
    public static TimeSpan StaleAfterOf(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Properties.TryGetValue(StaleAfterProperty, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0
                ? TimeSpan.FromHours(hours)
                : StaleAfter;
    }

    /// <summary>
    /// True when the run shown for <paramref name="item"/> — the downloaded
    /// copy's (<paramref name="localRun"/>), else the listed one — is older than
    /// <see cref="StaleAfterOf"/> at <paramref name="now"/>. A pinned run never is.
    /// </summary>
    public static bool IsStale(CollectionItem item, DateTimeOffset? localRun, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!IsWeatherRun(item) || IsPinned(item))
            return false;
        var run = localRun ?? S100ForecastFeedIndexer.RunOf(item);
        return run is { } shown && now - shown > StaleAfterOf(item);
    }

    /// <summary>The licence <paramref name="item"/> carries, if any.</summary>
    public static string? LicenceOf(CollectionItem item) =>
        item?.Properties.GetValueOrDefault(LicenceProperty) is { Length: > 0 } value ? value : null;

    /// <summary>The attribution <paramref name="item"/> carries, if any.</summary>
    public static string? AttributionOf(CollectionItem item) =>
        item?.Properties.GetValueOrDefault(AttributionProperty) is { Length: > 0 } value ? value : null;

    /// <summary>
    /// The line a layer row or pick report shows for an item's source:
    /// "NOAA/NCEP GFS-Wave · Public domain", the attribution or the licence alone, or null.
    /// </summary>
    public static string? CreditOf(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Credit(AttributionOf(item), LicenceOf(item));
    }

    /// <summary>Joins an attribution and a licence as a layer row or pick report shows them; null when both are.</summary>
    public static string? Credit(string? attribution, string? licence) =>
        (attribution, licence) switch
        {
            ({ Length: > 0 } a, { Length: > 0 } l) => $"{a} · {l}",
            ({ Length: > 0 } a, _) => a,
            (_, { Length: > 0 } l) => l,
            _ => null,
        };

    /// <summary>The download name a pinned copy of a run is kept under: <c>gfswave.run-2026102800</c>.</summary>
    public static string PinnedName(string name, DateTimeOffset run)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return name + PinnedSuffix + run.ToUniversalTime().ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
    }

    /// <summary>The run a pinned copy's download name gives, or null for a name that isn't one.</summary>
    public static DateTimeOffset? RunOfPinnedName(string name, out string? itemName)
    {
        ArgumentNullException.ThrowIfNull(name);
        itemName = null;
        var at = name.LastIndexOf(PinnedSuffix, StringComparison.Ordinal);
        if (at <= 0
            || !DateTimeOffset.TryParseExact(name[(at + PinnedSuffix.Length)..], "yyyyMMddHH", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var run))
        {
            return null;
        }

        itemName = name[..at];
        return run;
    }

    private const string PinnedSuffix = ".run-";
}
