using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Watches loaded NHC storms for new advisories (#1395; vector-weather.md
/// §6.4, decision Q4). A newer advisory is announced once, with Load latest
/// run as the notification's action, rather than loaded; a storm NHC no
/// longer lists is marked final.
/// </summary>
/// <remarks>Runs on the UI thread. Remembers the advisories it announced, so each is announced once.</remarks>
internal sealed class StormAdvisoryWatch
{
    private readonly CollectionLibrary _library;
    private readonly MarkFinalStormsWorkflow _markFinal;
    private readonly LoadLatestWeatherRunWorkflow _loadLatest;
    private readonly INotificationService _notifications;
    private readonly HashSet<(Guid Source, string Key, DateTimeOffset Run)> _announced = [];

    /// <summary>Creates the watch.</summary>
    /// <param name="library">The Library the storm sources are in.</param>
    /// <param name="markFinal">Finds the loaded storms and marks final those NHC no longer lists.</param>
    /// <param name="loadLatest">Loads a storm's latest advisory (the notification's action).</param>
    /// <param name="notifications">Where new advisories are announced.</param>
    public StormAdvisoryWatch(
        CollectionLibrary library,
        MarkFinalStormsWorkflow markFinal,
        LoadLatestWeatherRunWorkflow loadLatest,
        INotificationService notifications)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(markFinal);
        ArgumentNullException.ThrowIfNull(loadLatest);
        ArgumentNullException.ThrowIfNull(notifications);
        _library = library;
        _markFinal = markFinal;
        _loadLatest = loadLatest;
        _notifications = notifications;
    }

    /// <summary>True when <paramref name="sourceId"/> is an NHC storms source.</summary>
    /// <param name="sourceId">The source.</param>
    public bool IsStormSource(Guid sourceId) =>
        _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId)?.Definition
            is WeatherFeedSource { Service.Kind: WeatherServiceKind.NhcStorms };

    /// <summary>
    /// Re-reads an NHC source and, for each loaded storm among
    /// <paramref name="keys"/>, announces a newer advisory (once) or, when NHC
    /// no longer lists the storm, marks it final.
    /// </summary>
    /// <param name="sourceId">The NHC source.</param>
    /// <param name="keys">The keys of the loaded storms.</param>
    public async Task CheckAsync(Guid sourceId, IReadOnlyCollection<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var storms = _markFinal.LoadedStorms(sourceId, keys);
        _library.Refresh(sourceId: sourceId);
        await _library.WhenIdle().ConfigureAwait(true);

        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source?.Index is not { } index)
            return;
        foreach (var (key, (_, loadedRun)) in storms)
        {
            if (index.Items.FirstOrDefault(i => i.Key == key && !WeatherRuns.IsPinned(i)) is not { } item
                || S100ForecastFeedIndexer.RunOf(item) is not { } run
                || (loadedRun is { } opened && run <= opened)
                || !_announced.Add((sourceId, key, run)))
            {
                continue;
            }

            Announce(sourceId, item);
        }

        await _markFinal.RunAsync(source, index, storms).ConfigureAwait(true);
    }

    /// <summary>
    /// Raises the Information notification for a new advisory of a loaded
    /// storm (V28): "HURRICANE ZELDA: advisory 18 is available", with Load
    /// latest run as its action.
    /// </summary>
    private void Announce(Guid sourceId, CollectionItem item)
    {
        var name = item.Properties.GetValueOrDefault(WeatherFeedIndexer.StormNameProperty) is { Length: > 0 }
            ? StormItemText.Name(item)
            : item.Key.ToUpperInvariant();
        var shortName = name.Split(" · ")[0];
        var key = item.Key;
        _notifications.Create(string.Format(CultureInfo.CurrentCulture, Strings.Toast_StormAdvisoryTitleFormat,
                shortName, StormItemText.ListedAdvisory(item) ?? "?"))
            .WithSeverity(NotificationSeverity.Info)
            .WithContent(Strings.Toast_StormAdvisoryContent)
            .WithAction(Strings.Library_LoadLatestRun, () => _ = _loadLatest.RunAsync(sourceId, [key]), isPrimary: true)
            .Persistent()
            .Show();
    }
}
