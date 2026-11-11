using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Library panel's weather-run actions (#1075; design note
/// weather-sources.md §6.3): "Load latest run" for a stale run, and pinning a
/// run so the next one doesn't replace it. Keeping loaded weather up to date
/// on its own is <see cref="Library.WeatherRunMonitor"/>'s job (#1395).
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

    private Task LoadLatestRunAsync() =>
        _selectedItem is { CanLoadLatestRun: true } row
            ? _loadLatest.RunAsync(row.Source.Id, [row.Item.Key])
            : Task.CompletedTask;

    private void PinSelectedRun()
    {
        if (_selectedItem is { CanPinRun: true } row)
            _pinRun.Pin(row.Item, row.Source.Id);
    }

    private void UnpinSelectedRun()
    {
        if (_selectedItem is { IsPinnedRun: true } row)
            _pinRun.Unpin(row.Item, row.Source.Id);
    }
}
