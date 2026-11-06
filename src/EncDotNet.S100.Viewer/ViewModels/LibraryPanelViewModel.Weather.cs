using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Library panel's weather-run actions (#1075; design note
/// weather-sources.md §6.3): "Load latest run" for a stale run, and pinning a
/// run so the next one doesn't replace it. A source whose service refreshes
/// on its own (<see cref="WeatherService.RefreshHours"/>: WPC's surface
/// analysis, every 3 hours, vector-weather.md §6.4) is re-read once that long
/// has passed since its last index while its latest item is loaded, and a
/// newer item is downloaded and loaded in its place.
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
        _library.Refresh(sourceId: sourceId);
        await _library.WhenIdle().ConfigureAwait(true);

        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source?.Index is not { } index)
            return;
        var collection = _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == sourceId))?.Definition.Name;
        var download = new List<LibraryItemViewModel>();
        var load = new List<LibraryItemViewModel>();
        foreach (var item in index.Items.Where(i => keys.Contains(i.Key) && !WeatherRuns.IsPinned(i)))
        {
            var latest = new LibraryItemViewModel(item, source, _loader.StateOf, _downloader, collection, RetryDownloadAsync, _time, _runFormat);
            (latest.CanLoadAfterDownload || _downloader.IsOutdated(item) ? download : load).Add(latest);
        }

        if (download.Count > 0)
            await DownloadRowsAsync(download, load: true).ConfigureAwait(true);
        if (load.Count > 0)
            await LoadLabelledAsync(load, defer: false).ConfigureAwait(true);
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
                await LoadLatestAsync(id, keys).ConfigureAwait(true);
            return [.. due.Select(d => d.Id)];
        }
        finally
        {
            _autoRefreshing = false;
        }
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
