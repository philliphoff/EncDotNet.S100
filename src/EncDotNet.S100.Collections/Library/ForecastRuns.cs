using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// Forecast-run facts of a library item from an S-100 forecast feed (#685,
/// NOAA's S-111): its model, run time and valid window.
/// </summary>
public static class ForecastRuns
{
    /// <summary>True when <paramref name="item"/> is one run's dataset from a forecast feed.</summary>
    public static bool IsForecast(CollectionItem item) =>
        item.Properties.ContainsKey(S100ForecastFeedIndexer.RunProperty);

    /// <summary>The model the item belongs to (e.g. <c>cbofs</c>).</summary>
    public static string? ModelOf(CollectionItem item) => item.Properties.GetValueOrDefault(S100ForecastFeedIndexer.ModelProperty);

    /// <summary>How far ahead of its run the item forecasts.</summary>
    public static TimeSpan? Horizon(CollectionItem item) =>
        S100ForecastFeedIndexer.RunOf(item) is { } run && S100ForecastFeedIndexer.ValidToOf(item) is { } to ? to - run : null;

    /// <summary>
    /// The valid window of the run shown for <paramref name="item"/>: the
    /// downloaded copy's run (<paramref name="localRun"/>) when there is one,
    /// else the catalogue's latest, each lasting the item's
    /// <see cref="Horizon"/>. <see langword="null"/> when either is unknown.
    /// </summary>
    public static (DateTimeOffset Run, DateTimeOffset ValidTo)? ShownWindow(CollectionItem item, DateTimeOffset? localRun) =>
        (localRun ?? S100ForecastFeedIndexer.RunOf(item)) is { } run && Horizon(item) is { } horizon
            ? (run, run + horizon)
            : null;

    /// <summary>
    /// The S-102 tile in the same grid cell as an S-111 tile (#685): NOAA names
    /// both on one tile grid, so <c>111US00_CBOFS_US4VA1DD</c> pairs with
    /// <c>102US004VA1DD</c>. <see langword="null"/> for a name with no tile cell.
    /// </summary>
    public static string? BathymetryTwinOf(string tileName)
    {
        ArgumentNullException.ThrowIfNull(tileName);
        var cell = tileName[(tileName.LastIndexOf('_') + 1)..];
        return cell.Length == 8 && cell.StartsWith("US", StringComparison.Ordinal) && char.IsAsciiDigit(cell[2])
            ? "102US00" + cell[2..]
            : null;
    }
}
