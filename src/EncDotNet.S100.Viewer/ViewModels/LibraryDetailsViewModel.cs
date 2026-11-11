using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Library panel's details pane (#1396): the selected dataset's actions
/// (Load, Download, Open as route, the weather-run actions, Zoom to, the
/// Timeline actions) and its S-102 twin. The panel says which dataset is
/// selected (<see cref="Item"/>) and when its state may have changed
/// (<see cref="Refresh"/>).
/// </summary>
internal sealed class LibraryDetailsViewModel : ViewModelBase
{
    private readonly CollectionLibrary _library;
    private readonly ILibraryDownloader _downloader;
    private readonly OpenLibraryItemsWorkflow _open;
    private readonly DownloadLibraryItemsWorkflow _download;
    private readonly OpenLibraryItemAsRouteWorkflow _openAsRoute;
    private readonly LoadLatestWeatherRunWorkflow _loadLatest;
    private readonly PinWeatherRunWorkflow _pinRun;
    private readonly Services.GlobalTimeService? _viewTime;
    private LibraryItemViewModel? _item;
    private LibraryPairing? _pairing;

    /// <param name="library">The Library, searched for a forecast tile's S-102 twin.</param>
    /// <param name="downloader">Says whether the twin is downloaded, and downloads it.</param>
    /// <param name="open">Opens the dataset (Load).</param>
    /// <param name="download">Downloads the dataset (Download, Load after download).</param>
    /// <param name="openAsRoute">Opens an S-421 dataset as a route.</param>
    /// <param name="loadLatest">Loads a weather source's latest run.</param>
    /// <param name="pinRun">Pins and unpins weather runs.</param>
    /// <param name="viewTime">The time the map shows; <see langword="null"/> without a timeline.</param>
    public LibraryDetailsViewModel(
        CollectionLibrary library,
        ILibraryDownloader downloader,
        OpenLibraryItemsWorkflow open,
        DownloadLibraryItemsWorkflow download,
        OpenLibraryItemAsRouteWorkflow openAsRoute,
        LoadLatestWeatherRunWorkflow loadLatest,
        PinWeatherRunWorkflow pinRun,
        Services.GlobalTimeService? viewTime)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(openAsRoute);
        ArgumentNullException.ThrowIfNull(loadLatest);
        ArgumentNullException.ThrowIfNull(pinRun);
        _library = library;
        _downloader = downloader;
        _open = open;
        _download = download;
        _openAsRoute = openAsRoute;
        _loadLatest = loadLatest;
        _pinRun = pinRun;
        _viewTime = viewTime;

        ZoomToCommand = new RelayCommand(ZoomTo, () => _item?.HasBounds == true);
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => _item?.CanLoad == true);
        DownloadCommand = new AsyncRelayCommand(() => DownloadAsync(load: true), () => _item?.CanDownload == true);
        DownloadOnlyCommand = new AsyncRelayCommand(() => DownloadAsync(load: false), () => _item?.CanDownload == true);
        LoadOrDownloadCommand = new AsyncRelayCommand(
            () => _item?.CanLoadAfterDownload == true ? DownloadAsync(load: true) : LoadAsync(),
            () => _item is { } item && (item.CanLoad || item.CanLoadAfterDownload));
        OpenAsRouteCommand = new AsyncRelayCommand(OpenAsRouteAsync, () => CanOpenAsRoute);
        LoadLatestRunCommand = new AsyncRelayCommand(LoadLatestRunAsync, () => _item?.CanLoadLatestRun == true);
        PinRunCommand = new RelayCommand(PinRun, () => _item?.CanPinRun == true);
        UnpinRunCommand = new RelayCommand(UnpinRun, () => _item?.IsPinnedRun == true);
        ShowOnTimelineCommand = new RelayCommand(
            () =>
            {
                if (_item?.ValidWindow is { } window)
                    ShowOnTimelineRequested?.Invoke(this, window);
            },
            () => HasViewTime && _item?.ValidWindow is not null);
        GoToRunStartCommand = new RelayCommand(
            () =>
            {
                if (_item?.ValidWindow is { } window)
                    GoToTimeRequested?.Invoke(this, window.Start);
            },
            () => HasViewTime && _item?.ValidWindow is not null);
    }

    /// <summary>The dataset the pane shows, or <see langword="null"/> when none is selected.</summary>
    public LibraryItemViewModel? Item
    {
        get => _item;
        set
        {
            if (SetProperty(ref _item, value))
            {
                OnPropertyChanged(nameof(HasValidWindow));
                Refresh();
            }
        }
    }

    /// <summary>
    /// Re-reads what the actions depend on: the dataset's state (after a load
    /// or download), the Timeline's view time, and the twin's availability.
    /// </summary>
    public void Refresh()
    {
        ((RelayCommand)ZoomToCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DownloadCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)DownloadOnlyCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)LoadOrDownloadCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)OpenAsRouteCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)LoadLatestRunCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PinRunCommand).NotifyCanExecuteChanged();
        ((RelayCommand)UnpinRunCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ShowOnTimelineCommand).NotifyCanExecuteChanged();
        ((RelayCommand)GoToRunStartCommand).NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasViewTime));
        OnPropertyChanged(nameof(CanOpenAsRoute));
        UpdatePairing();
    }

    /// <summary>Zooms the map to the dataset.</summary>
    public ICommand ZoomToCommand { get; }

    /// <summary>Loads the dataset now.</summary>
    public ICommand LoadCommand { get; }

    /// <summary>Downloads the online dataset, then loads it ("Load after download").</summary>
    public ICommand DownloadCommand { get; }

    /// <summary>Downloads the online dataset without loading it.</summary>
    public ICommand DownloadOnlyCommand { get; }

    /// <summary>
    /// The header's one Load button: loads a local dataset, or downloads an
    /// online one and then loads it.
    /// </summary>
    public ICommand LoadOrDownloadCommand { get; }

    /// <summary>Loads the S-421 dataset (if it isn't) and opens it as a route in the Routes panel.</summary>
    public ICommand OpenAsRouteCommand { get; }

    /// <summary>True when the dataset is an S-421 route on disk, which Open as route opens.</summary>
    public bool CanOpenAsRoute =>
        _item is { IsGroupHeader: false } item
        && string.Equals(item.Item.ProductSpec, "S-421", StringComparison.OrdinalIgnoreCase)
        && (item.CanLoad || item.IsLoadedNow);

    /// <summary>
    /// Re-reads the weather run's source, downloads its latest run (replacing
    /// the last unless pinned) and loads it (#1075).
    /// </summary>
    public ICommand LoadLatestRunCommand { get; }

    /// <summary>Keeps the weather run's downloaded copy beside the latest, so the next run doesn't replace it.</summary>
    public ICommand PinRunCommand { get; }

    /// <summary>Deletes the pinned run.</summary>
    public ICommand UnpinRunCommand { get; }

    /// <summary>True when the Timeline has a view time, so the Timeline actions apply.</summary>
    public bool HasViewTime => _viewTime?.CurrentTime is not null && _viewTime.IsActive;

    /// <summary>True when the dataset has a time window (forecast run or time coverage).</summary>
    public bool HasValidWindow => _item?.ValidWindow is not null;

    /// <summary>Shows the dataset's window on the Timeline (#711, handoff G3).</summary>
    public ICommand ShowOnTimelineCommand { get; }

    /// <summary>Moves the Timeline's view time to the start of the dataset's run or window.</summary>
    public ICommand GoToRunStartCommand { get; }

    /// <summary>Raised with a window the Timeline should show.</summary>
    public event EventHandler<(DateTime Start, DateTime End)>? ShowOnTimelineRequested;

    /// <summary>Raised with a time the Timeline should move to.</summary>
    public event EventHandler<DateTime>? GoToTimeRequested;

    /// <summary>Raised when the user asks to zoom the map to a dataset's bounds.</summary>
    public event EventHandler<GeoBounds>? ZoomRequested;

    /// <summary>Raised when the user asks to centre the map on a dataset without changing the zoom.</summary>
    public event EventHandler<GeoPosition>? CenterRequested;

    /// <summary>
    /// Centres the map on the dataset (its bounds' centre), keeping the zoom —
    /// e.g. when its row is double-clicked and it may be off screen.
    /// </summary>
    public void CenterOnItem()
    {
        if (_item is { IsGroupHeader: false, Item.Bounds: { } bounds })
            CenterRequested?.Invoke(this, CenterOf(bounds));
    }

    /// <summary>The centre of <paramref name="bounds"/>, across the antimeridian when they cross it.</summary>
    internal static GeoPosition CenterOf(GeoBounds bounds)
    {
        var east = bounds.CrossesAntimeridian ? bounds.East + 360 : bounds.East;
        return new GeoPosition((bounds.South + bounds.North) / 2, GeoBounds.NormalizeLongitude((bounds.West + east) / 2));
    }

    /// <summary>
    /// For a selected S-111 tile, its S-102 twin in the same grid cell, when an
    /// S-102 collection lists it (#685, handoff B7); otherwise <see langword="null"/>.
    /// </summary>
    public LibraryPairing? Pairing
    {
        get => _pairing;
        private set
        {
            if (SetProperty(ref _pairing, value))
                OnPropertyChanged(nameof(HasPairing));
        }
    }

    /// <summary>True when <see cref="Pairing"/> is shown.</summary>
    public bool HasPairing => _pairing is not null;

    private void UpdatePairing()
    {
        if (_item is not { IsForecast: true, IsModelHeader: false } tile
            || ForecastRuns.BathymetryTwinOf(tile.Item.Name) is not { } twin)
        {
            Pairing = null;
            return;
        }

        var match = _library.Collections
            .SelectMany(c => c.Sources)
            .Where(s => s.Definition is S100CatalogueFeedSource && s.Index is not null)
            .SelectMany(s => s.Index!.Items)
            .FirstOrDefault(i => string.Equals(i.Name, twin, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Pairing = null;
            return;
        }

        var local = _downloader.Localize(match).Location is LocalItemLocation;
        var size = (match.Location as RemoteItemLocation)?.SizeBytes;
        var detail = string.Join(" · ", new[]
        {
            $"{match.ProductSpec} {match.Name}",
            Strings.Library_Pairing_SameCell,
            local ? Strings.Library_Availability_Local : Strings.Library_Availability_Online,
            size is { } bytes && !local ? LibraryItemViewModel.FormatBytes(bytes) : null,
        }.OfType<string>());
        Pairing = new LibraryPairing(Strings.Library_Pairing_Bathymetry, detail, local,
            new AsyncRelayCommand(async () =>
            {
                await _downloader.DownloadAsync([match]).ConfigureAwait(true);
                UpdatePairing();
            }, () => !local && _downloader.CanDownload(match)));
    }

    private void ZoomTo()
    {
        if (_item?.Item.Bounds is { } bounds)
            ZoomRequested?.Invoke(this, bounds);
    }

    private Task LoadAsync() =>
        _item is { } item ? _open.RunAsync([item.State], defer: false) : Task.CompletedTask;

    private async Task DownloadAsync(bool load)
    {
        if (_item is not { } row)
            return;
        var outcome = await _download.RunAsync([(row.Item, row.Source)], load).ConfigureAwait(true);
        if (outcome.Download.Downloaded > 0 && outcome.ReindexedSources.Count == 0)
            row.RefreshAvailability();
    }

    private async Task OpenAsRouteAsync()
    {
        if (!CanOpenAsRoute || _item is not { } item)
            return;
        await _openAsRoute.RunAsync(item.State).ConfigureAwait(true);
    }

    private Task LoadLatestRunAsync() =>
        _item is { CanLoadLatestRun: true } row
            ? _loadLatest.RunAsync(row.Source.Id, [row.Item.Key])
            : Task.CompletedTask;

    private void PinRun()
    {
        if (_item is { CanPinRun: true } row)
            _pinRun.Pin(row.Item, row.Source.Id);
    }

    private void UnpinRun()
    {
        if (_item is { IsPinnedRun: true } row)
            _pinRun.Unpin(row.Item, row.Source.Id);
    }
}
