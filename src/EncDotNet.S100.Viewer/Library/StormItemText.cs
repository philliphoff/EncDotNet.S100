using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// How an NHC storm item reads in the Library (design note
/// <c>vector-weather.md</c> §6.4, V27): named by the storm
/// ("HURRICANE ZELDA · AL14"), its subtitle the advisory shown and when the
/// next is due (or that a newer one is available), its summary the forecast
/// end and intensity, and its swatch the storm's class symbol.
/// </summary>
internal static class StormItemText
{
    /// <summary>True for an item of the NHC storm source (<see cref="WeatherServiceKind.NhcStorms"/>).</summary>
    /// <param name="item">A Library item.</param>
    public static bool IsStorm(CollectionItem item) =>
        item?.Properties.GetValueOrDefault(WeatherRuns.ServiceProperty) == ServiceName;

    /// <summary>"HURRICANE ZELDA · AL14": the class word, the name and the short ATCF id.</summary>
    /// <param name="item">A storm item.</param>
    public static string Name(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var word = TropicalCycloneStyle.ClassWord(ClassOf(item));
        var name = item.Properties.GetValueOrDefault(WeatherFeedIndexer.StormNameProperty) is { Length: > 0 } n
            ? $"{word} {n.ToUpperInvariant()}"
            : word;
        var shortId = item.Key.Length >= 4 ? item.Key[..4].ToUpperInvariant() : item.Key.ToUpperInvariant();
        return string.Format(CultureInfo.CurrentCulture, Strings.Library_StormNameFormat, name, shortId);
    }

    /// <summary>
    /// The advisory shown: the downloaded copy's (read from its forecast zip's
    /// name), else the listed one; <c>"17"</c>.
    /// </summary>
    /// <param name="item">The listed storm item.</param>
    /// <param name="effective">The item as it's opened: its downloaded copy when there is one.</param>
    public static string? ShownAdvisory(CollectionItem item, CollectionItem effective)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (effective?.Location is LocalItemLocation local
            && NhcAdvisoryFiles.TryParseFileName(Path.GetFileName(local.RelativePath), out var kind, out _, out var advisory)
            && kind == NhcFileKind.Forecast)
        {
            return NhcActiveStorms.TrimAdvisory(advisory);
        }

        return ListedAdvisory(item);
    }

    /// <summary>The latest advisory NHC lists for the storm: <c>"18"</c>.</summary>
    public static string? ListedAdvisory(CollectionItem item) =>
        item?.Properties.GetValueOrDefault(WeatherFeedIndexer.AdvisoryProperty) is { Length: > 0 } advisory ? advisory : null;

    /// <summary>
    /// "Advisory 17 · 11/4/2026 3:00 PM · next 11/4/2026 9:00 PM", or, when NHC
    /// lists a newer advisory than the one downloaded, "… · advisory 18 available".
    /// </summary>
    /// <param name="item">The listed storm item.</param>
    /// <param name="effective">The item as it's opened.</param>
    /// <param name="shownRun">The shown advisory's issue time: the downloaded copy's, else the listed one.</param>
    /// <param name="newerListed">True when NHC lists a newer advisory than the downloaded one.</param>
    /// <param name="format">Writes a time as the user reads it.</param>
    public static string? Subtitle(
        CollectionItem item, CollectionItem effective, DateTimeOffset? shownRun, bool newerListed, Func<DateTimeOffset, string> format)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(format);
        if (ShownAdvisory(item, effective) is not { } advisory || shownRun is not { } issued)
            return item.Title;
        var c = CultureInfo.CurrentCulture;
        var text = string.Format(c, Strings.Library_StormAdvisoryFormat, advisory, format(issued));
        return newerListed && ListedAdvisory(item) is { } listed
            ? text + " · " + string.Format(c, Strings.Library_StormNewerFormat, listed)
            : text + " · " + string.Format(c, Strings.Library_StormNextFormat, format(TropicalCycloneDatasetProcessor.NextAdvisoryDue(issued)));
    }

    /// <summary>"Forecast to 11/9/2026 3:00 PM · 115 kn".</summary>
    /// <param name="item">The storm item.</param>
    /// <param name="validTo">The end of the shown advisory's forecast, if known.</param>
    /// <param name="format">Writes a time as the user reads it.</param>
    public static string Summary(CollectionItem item, DateTimeOffset? validTo, Func<DateTimeOffset, string> format)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(format);
        var c = CultureInfo.CurrentCulture;
        var parts = new List<string>(2);
        if (validTo is { } end)
            parts.Add(string.Format(c, Strings.Library_StormForecastToFormat, format(end)));
        else if (item.Properties.GetValueOrDefault(WeatherFeedIndexer.BasinProperty) is { Length: > 0 } basin)
            parts.Add(basin);
        if (IntensityOf(item) is { } wind)
            parts.Add(string.Format(c, Strings.Library_StormIntensityFormat, wind));
        return string.Join(" · ", parts);
    }

    /// <summary>"Advisory 17 is over 7 hours old": the stale-run warning of a storm (V28).</summary>
    /// <param name="advisory">The shown advisory.</param>
    /// <param name="staleAfter">How old an advisory may be before it's stale.</param>
    public static string StaleText(string advisory, TimeSpan staleAfter) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Library_StaleAdvisoryFormat, advisory, (int)Math.Round(staleAfter.TotalHours));

    /// <summary>
    /// The class symbol the item's swatch shows (V27): the storm's class,
    /// as the chart draws it, mirrored south of the equator.
    /// </summary>
    /// <param name="item">The storm item.</param>
    public static string Symbol(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var latitude = double.TryParse(item.Properties.GetValueOrDefault(WeatherFeedIndexer.LatitudeProperty),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ? lat : 0;
        return TropicalCycloneStyle.SymbolFor(ClassOf(item), SaffirSimpson(IntensityOf(item)), latitude);
    }

    /// <summary>The Saffir–Simpson category of a hurricane's wind, or null below 64 kt.</summary>
    /// <param name="knots">The maximum sustained wind.</param>
    public static int? SaffirSimpson(int? knots) => knots switch
    {
        >= 137 => 5,
        >= 113 => 4,
        >= 96 => 3,
        >= 83 => 2,
        >= 64 => 1,
        _ => null,
    };

    private static TropicalCycloneClass ClassOf(CollectionItem item) =>
        TropicalCycloneClasses.FromCode(item.Properties.GetValueOrDefault(WeatherFeedIndexer.StormClassProperty));

    private static int? IntensityOf(CollectionItem item) =>
        int.TryParse(item.Properties.GetValueOrDefault(WeatherFeedIndexer.IntensityProperty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var wind)
            ? wind
            : null;

    private static readonly string ServiceName =
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(nameof(WeatherServiceKind.NhcStorms));
}
