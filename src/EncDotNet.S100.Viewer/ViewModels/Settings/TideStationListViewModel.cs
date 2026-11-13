using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Tides;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The line under <b>Use tide predictions where there's no S-104</b>
/// (tide predictions design Q7b, D15): the station list's date and counts, or
/// its download progress, with <b>Download</b> or <b>Refresh</b>.
/// </summary>
internal sealed class TideStationListViewModel : ViewModelBase
{
    private readonly TideStationListService _list;

    /// <summary>Creates the line.</summary>
    /// <param name="list">The station list.</param>
    public TideStationListViewModel(TideStationListService list)
    {
        ArgumentNullException.ThrowIfNull(list);
        _list = list;
        DownloadCommand = new AsyncRelayCommand(() => _list.DownloadAsync(), () => !IsDownloading);
        CancelCommand = new RelayCommand(_list.CancelDownload, () => IsDownloading);
        _list.Changed += (_, _) => Raise();
        _ = _list.EnsureLoadedAsync();
    }

    /// <summary>"NOAA station list · 3 Mar 2026 · 3,502 tide and 4,217 current stations", or what's happening.</summary>
    public string Text
    {
        get
        {
            if (_list.Activity == TideStationListActivity.Downloading)
            {
                return _list.DownloadProgress is { TotalStations: > 0 } p
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Tides_List_DownloadingProgressFormat, p.CompletedStations, p.TotalStations)
                    : Strings.Tides_List_Downloading;
            }

            if (_list.Catalogue is not { } catalogue)
            {
                return Strings.Tides_List_None;
            }

            var date = catalogue.Retrieved is { } retrieved
                ? TimeFormatting.FormatDateOnly(DateOnly.FromDateTime(retrieved.UtcDateTime))
                : Strings.Tides_List_DateUnknown;
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Tides_List_Format,
                catalogue.Provider ?? Strings.Tides_ProviderUnknown,
                date,
                catalogue.Stations.Count,
                catalogue.CurrentStations.Count);
        }
    }

    /// <summary>Why the last download failed, or the stale notice; empty when neither.</summary>
    public string Note => _list.LastError is { } error
        ? string.Format(CultureInfo.CurrentCulture, Strings.Tides_List_FailedFormat, error)
        : _list.IsStale ? Strings.Tides_List_Stale : string.Empty;

    /// <summary>Whether <see cref="Note"/> has text.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>Whether the list is downloading.</summary>
    public bool IsDownloading => _list.Activity == TideStationListActivity.Downloading;

    /// <summary>"Download" with no list, else "Refresh".</summary>
    public string DownloadLabel => _list.Catalogue is null ? Strings.Tides_List_Download : Strings.Tides_List_Refresh;

    /// <summary>Downloads NOAA's list (about ten minutes).</summary>
    public IAsyncRelayCommand DownloadCommand { get; }

    /// <summary>Stops a download.</summary>
    public IRelayCommand CancelCommand { get; }

    private void Raise()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(DownloadLabel));
        DownloadCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
