using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections.Weather;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Library panel's weather-run actions (#1075; design note
/// weather-sources.md §6.3): "Load latest run" for a stale run, and pinning a
/// run so the next one doesn't replace it.
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

        var sourceId = row.Source.Id;
        var key = row.Item.Key;
        _library.Refresh(sourceId: sourceId);
        await _library.WhenIdle().ConfigureAwait(true);

        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source?.Index?.Items.FirstOrDefault(i => i.Key == key && !WeatherRuns.IsPinned(i)) is not { } item)
            return;
        var collection = _library.Collections.FirstOrDefault(c => c.Sources.Any(s => s.Id == sourceId))?.Definition.Name;
        var latest = new LibraryItemViewModel(item, source, _loader.StateOf, _downloader, collection, RetryDownloadAsync, _time, _runFormat);
        if (latest.CanLoadAfterDownload || _downloader.IsOutdated(item))
            await DownloadRowsAsync([latest], load: true).ConfigureAwait(true);
        else
            await LoadLabelledAsync([latest], defer: false).ConfigureAwait(true);
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
