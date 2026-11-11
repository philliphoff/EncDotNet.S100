using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Library panel's weather-run actions (#1075; design note
/// weather-sources.md §6.3): "Load latest run" for a stale run, and pinning a
/// run so the next one doesn't replace it. A source whose service refreshes
/// on its own (<see cref="WeatherService.RefreshHours"/>: WPC's surface
/// analysis, every 3 hours, vector-weather.md §6.4) is re-read once that long
/// has passed since its last index while its latest item is loaded, and a
/// newer item is downloaded and loaded in its place. NHC's storms are re-read
/// the same way, but a new advisory for a loaded storm is announced, with
/// Load latest run as the notification's action, rather than loaded (§6.4,
/// decision Q4); a storm NHC no longer lists is marked final and reopened, so
/// it is drawn as its final advisory (§7).
/// </summary>
internal sealed partial class LibraryPanelViewModel
{
    /// <summary>
    /// Re-reads the selected weather run's source, downloads its latest run
    /// (replacing the last unless pinned) and loads it.
    /// </summary>
    public ICommand LoadLatestRunCommand { get; }

    /// <summary>Keeps the selected weather run's downloaded copy beside the latest, so the next run doesn't replace it.</summary>
    public ICommand PinRunCommand { get; }

    /// <summary>Deletes the selected pinned run.</summary>
    public ICommand UnpinRunCommand { get; }

    private void NotifyWeatherRunCommands()
    {
        ((AsyncRelayCommand)LoadLatestRunCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PinRunCommand).NotifyCanExecuteChanged();
        ((RelayCommand)UnpinRunCommand).NotifyCanExecuteChanged();
    }

    private async Task LoadLatestRunAsync()
    {
        if (_selectedItem is not { CanLoadLatestRun: true } row)
            return;

        await LoadLatestAsync(row.Source.Id, [row.Item.Key]).ConfigureAwait(true);
    }

    /// <summary>
    /// Re-reads a weather source, then downloads (when outdated) and loads the
    /// latest of the items with <paramref name="keys"/>.
    /// </summary>
    private async Task LoadLatestAsync(Guid sourceId, IReadOnlyCollection<string> keys)
    {
        var storms = LoadedStorms(sourceId, keys);
        _library.Refresh(sourceId: sourceId);
        await _library.WhenIdle().ConfigureAwait(true);

        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source?.Index is not { } index)
            return;
        await MarkFinalStormsAsync(source, index, storms).ConfigureAwait(true);
        var collection = _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == sourceId))?.Definition.Name;
        var download = new List<LibraryItemViewModel>();
        var load = new List<LibraryItemViewModel>();
        foreach (var item in index.Items.Where(i => keys.Contains(i.Key) && !WeatherRuns.IsPinned(i)))
        {
            var latest = _rows.Create(item, source, collection);
            (latest.CanLoadAfterDownload || _downloader.IsOutdated(item) ? download : load).Add(latest);
        }

        if (download.Count > 0)
            await DownloadRowsAsync(download, load: true).ConfigureAwait(true);
        if (load.Count > 0)
            await LoadRowsAsync(load, defer: false).ConfigureAwait(true);
    }

    private bool _autoRefreshing;

    /// <summary>
    /// Refreshes the weather sources that refresh on their own and are due: a
    /// <see cref="WeatherService.RefreshHours"/> since their last index, with
    /// their latest (unpinned) item loaded. Called on the Library's minute clock.
    /// </summary>
    /// <returns>The sources refreshed.</returns>
    internal async Task<IReadOnlyList<Guid>> RefreshDueWeatherAsync()
    {
        if (_autoRefreshing)
            return [];
        _autoRefreshing = true;
        try
        {
            var now = _time.GetUtcNow();
            var due = _library.Collections
                .SelectMany(c => c.Sources)
                .Where(s => s.Definition is WeatherFeedSource { Service.RefreshHours: > 0 } feed
                    && s.Index is { } index
                    && now - index.IndexedAt >= TimeSpan.FromHours(feed.Service.RefreshHours))
                .Select(s => (s.Id, Keys: s.Index!.Items
                    .Where(i => !WeatherRuns.IsPinned(i) && _loader.StateOf(_downloader.Localize(i)) == LibraryLoadState.Loaded)
                    .Select(i => i.Key)
                    .ToHashSet(StringComparer.Ordinal)))
                .Where(s => s.Keys.Count > 0)
                .ToArray();
            foreach (var (id, keys) in due)
            {
                if (IsStormSource(id))
                    await CheckStormsAsync(id, keys).ConfigureAwait(true);
                else
                    await LoadLatestAsync(id, keys).ConfigureAwait(true);
            }

            return [.. due.Select(d => d.Id)];
        }
        finally
        {
            _autoRefreshing = false;
        }
    }

    /// <summary>The advisories already announced, by source, storm and issue time, so each is announced once.</summary>
    private readonly HashSet<(Guid Source, string Key, DateTimeOffset Run)> _announcedAdvisories = [];

    private bool IsStormSource(Guid sourceId) =>
        _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId)?.Definition
            is WeatherFeedSource { Service.Kind: WeatherServiceKind.NhcStorms };

    /// <summary>
    /// The loaded storms among <paramref name="keys"/> of an NHC source, as
    /// opened (their downloaded copies), with the advisory each was opened at.
    /// </summary>
    private Dictionary<string, (CollectionItem Local, DateTimeOffset? Run)> LoadedStorms(Guid sourceId, IReadOnlyCollection<string> keys)
    {
        var result = new Dictionary<string, (CollectionItem, DateTimeOffset?)>(StringComparer.Ordinal);
        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source is not { Definition: WeatherFeedSource { Service.Kind: WeatherServiceKind.NhcStorms }, Index: { } index })
            return result;
        foreach (var item in index.Items.Where(i => keys.Contains(i.Key) && !WeatherRuns.IsPinned(i)))
        {
            var local = _downloader.Localize(item);
            if (_loader.StateOf(local) == LibraryLoadState.Loaded)
                result[item.Key] = (local, _downloader.LocalPublishedAtOf(item));
        }

        return result;
    }

    /// <summary>
    /// Re-reads an NHC source and, for each loaded storm, announces a newer
    /// advisory (once) or, when NHC no longer lists the storm, marks it final.
    /// </summary>
    private async Task CheckStormsAsync(Guid sourceId, IReadOnlyCollection<string> keys)
    {
        var storms = LoadedStorms(sourceId, keys);
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
                || !_announcedAdvisories.Add((sourceId, key, run)))
            {
                continue;
            }

            AnnounceAdvisory(sourceId, item);
        }

        await MarkFinalStormsAsync(source, index, storms).ConfigureAwait(true);
    }

    /// <summary>
    /// Raises the Information notification for a new advisory of a loaded
    /// storm (V28, decision Q4): "HURRICANE ZELDA: advisory 18 is available",
    /// with Load latest run as its action.
    /// </summary>
    private void AnnounceAdvisory(Guid sourceId, CollectionItem item)
    {
        if (_notifications is null)
            return;
        var name = item.Properties.GetValueOrDefault(WeatherFeedIndexer.StormNameProperty) is { Length: > 0 }
            ? StormItemText.Name(item)
            : item.Key.ToUpperInvariant();
        var shortName = name.Split(" · ")[0];
        var key = item.Key;
        _notifications.Create(string.Format(CultureInfo.CurrentCulture, Strings.Toast_StormAdvisoryTitleFormat,
                shortName, StormItemText.ListedAdvisory(item) ?? "?"))
            .WithSeverity(Services.Notifications.NotificationSeverity.Info)
            .WithContent(Strings.Toast_StormAdvisoryContent)
            .WithAction(Strings.Library_LoadLatestRun, () => _ = LoadLatestAsync(sourceId, [key]), isPrimary: true)
            .Persistent()
            .Show();
    }

    /// <summary>
    /// Marks final each of <paramref name="storms"/> (loaded storms, by key)
    /// that a successful index of <paramref name="source"/> no longer lists:
    /// NHC has issued its last advisory (§7). A marker is written beside its
    /// zips (<see cref="NhcAdvisoryFiles.FinalMarkerName"/>), and the storm is
    /// reopened so it's drawn as its final advisory. It stays loaded.
    /// </summary>
    private async Task MarkFinalStormsAsync(
        LibrarySource source, SourceIndex index, IReadOnlyDictionary<string, (CollectionItem Local, DateTimeOffset? Run)> storms)
    {
        if (storms.Count == 0 || index.Diagnostics.Any(d => d.Severity == IndexDiagnosticSeverity.Error))
            return;
        var reopen = new List<CollectionItem>();
        foreach (var (key, (local, _)) in storms)
        {
            if (index.Items.Any(i => i.Key == key) || local.Location is not LocalItemLocation { IsZip: false } location)
                continue;
            try
            {
                var folder = Path.GetDirectoryName(Path.Combine(location.RootPath, location.RelativePath)) ?? location.RootPath;
                var marker = Path.Combine(folder, NhcAdvisoryFiles.FinalMarkerName(key));
                if (File.Exists(marker))
                    continue;
                await File.WriteAllTextAsync(marker, string.Create(CultureInfo.InvariantCulture,
                    $"NHC stopped listing {key.ToUpperInvariant()} as an active storm by {_time.GetUtcNow():yyyy-MM-dd'T'HH:mm'Z'}.\n")).ConfigureAwait(true);
                reopen.Add(local);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The storm stays as it was drawn.
            }
        }

        if (reopen.Count == 0)
            return;
        _loader.Close(reopen);
        await _open.RunAsync([.. reopen.Select(item => (item, (LibrarySource?)source))], defer: false).ConfigureAwait(true);
    }

    private void PinSelectedRun()
    {
        if (_selectedItem is not { CanPinRun: true } row || _downloader.Pin(row.Item) is null)
            return;
        // The pinned copy is listed once the source re-indexes.
        _library.Refresh(sourceId: row.Source.Id);
    }

    private void UnpinSelectedRun()
    {
        if (_selectedItem is not { IsPinnedRun: true } row || !_downloader.Unpin(row.Item))
            return;
        _library.Refresh(sourceId: row.Source.Id);
    }
}
