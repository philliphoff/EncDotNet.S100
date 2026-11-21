using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Noaa;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>What <see cref="TideStationListService"/> is doing.</summary>
internal enum TideStationListActivity
{
    /// <summary>Nothing.</summary>
    Idle,

    /// <summary>Reading the cached list.</summary>
    Loading,

    /// <summary>Downloading the list from NOAA.</summary>
    Downloading,

    /// <summary>Asking NOAA whether it has newer data than the cached list.</summary>
    Checking,
}

/// <summary>
/// The NOAA tide and current station list SoundCharts predicts from (tide
/// predictions design D15): the cached file, read once and shared by the
/// tide source switch, the station overlays and the Library; its download
/// and refresh; and whether NOAA has newer data than it holds.
/// </summary>
/// <remarks>
/// The list is the <c>tide-stations</c> file at
/// <see cref="ViewerDataPaths.TideStationsFilePath"/>, the same file
/// <c>s100 tides update</c> writes. It's read on first use, not at start-up.
/// </remarks>
internal sealed class TideStationListService
{
    private readonly string _path;
    private readonly Func<HttpClient> _httpFactory;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private TideStationCatalogue? _catalogue;
    private Task<TideStationCatalogue?>? _loading;
    private CancellationTokenSource? _downloadCts;

    /// <summary>Creates the service.</summary>
    /// <param name="path">The cached station list.</param>
    /// <param name="httpFactory">Makes the client NOAA is downloaded with.</param>
    /// <param name="time">The clock, for the offline staleness rule.</param>
    /// <param name="post">Runs an action on the UI thread, where <see cref="Changed"/> is raised.</param>
    /// <param name="logger">Logs a list that can't be read or downloaded; <c>null</c> for none.</param>
    public TideStationListService(
        string path,
        Func<HttpClient> httpFactory,
        TimeProvider time,
        Action<Action> post,
        ILogger<TideStationListService>? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(httpFactory);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(post);
        _path = path;
        _httpFactory = httpFactory;
        _time = time;
        _post = post;
        _logger = logger;
    }

    /// <summary>The cached list's path.</summary>
    public string Path => _path;

    /// <summary>The station list, or <c>null</c> until it's read or when there's none.</summary>
    public TideStationCatalogue? Catalogue
    {
        get
        {
            lock (_gate)
            {
                return _catalogue;
            }
        }
    }

    /// <summary>What the service is doing.</summary>
    public TideStationListActivity Activity { get; private set; }

    /// <summary>The download's progress while <see cref="Activity"/> is <see cref="TideStationListActivity.Downloading"/>.</summary>
    public NoaaDownloadProgress? DownloadProgress { get; private set; }

    /// <summary>Why the last download or check failed, or <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>The last check for newer NOAA data, or <c>null</c> when none has run since the list was read.</summary>
    public TideCatalogueUpdateCheck? LastCheck { get; private set; }

    /// <summary>The cached file's size in bytes, or <c>null</c> when there's none.</summary>
    public long? FileSize
    {
        get
        {
            try
            {
                var info = new FileInfo(_path);
                return info.Exists ? info.Length : null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Whether the list is stale (D15, D21): NOAA has published newer data
    /// than it holds, or, when no check has run, it's older than
    /// <see cref="TideStationCatalogue.OfflineStaleAfter"/>.
    /// </summary>
    public bool IsStale
    {
        get
        {
            var catalogue = Catalogue;
            if (catalogue is null)
            {
                return false;
            }

            return LastCheck is { } check
                ? check.HasNewerData
                : catalogue.IsOlderThan(TideStationCatalogue.OfflineStaleAfter, _time.GetUtcNow());
        }
    }

    /// <summary>Raised on the UI thread when the list, the activity or the staleness changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Reads the cached list if it hasn't been read yet.</summary>
    /// <returns>The list, or <c>null</c> when there's none or it can't be read.</returns>
    public Task<TideStationCatalogue?> EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_catalogue is not null)
            {
                return Task.FromResult<TideStationCatalogue?>(_catalogue);
            }

            return _loading ??= LoadAsync();
        }
    }

    /// <summary>
    /// Reads the cached list, or downloads it when there's none (D15: the
    /// first time predictions or a station overlay are turned on).
    /// </summary>
    /// <returns>The list, or <c>null</c> when it can't be read or downloaded.</returns>
    public async Task<TideStationCatalogue?> EnsureAvailableAsync()
    {
        if (await EnsureLoadedAsync().ConfigureAwait(false) is { } catalogue)
        {
            return catalogue;
        }

        if (!File.Exists(_path))
        {
            await DownloadAsync().ConfigureAwait(false);
        }

        return Catalogue;
    }

    /// <summary>Reads the cached list again, after something else replaced it.</summary>
    /// <returns>The list, or <c>null</c>.</returns>
    public Task<TideStationCatalogue?> ReloadAsync()
    {
        lock (_gate)
        {
            _loading = LoadAsync();
            return _loading;
        }
    }

    /// <summary>
    /// Downloads NOAA's tide and current stations and replaces the cached
    /// list. It takes about ten minutes; a second call while one runs
    /// returns at once.
    /// </summary>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns><c>true</c> when the list was replaced.</returns>
    public async Task<bool> DownloadAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_downloadCts is not null)
            {
                return false;
            }

            _downloadCts = cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        Raise(() =>
        {
            Activity = TideStationListActivity.Downloading;
            DownloadProgress = null;
            LastError = null;
        });
        try
        {
            using var http = _httpFactory();
            var client = new NoaaTideStationClient(http, timeProvider: TimeProvider.System);
            var progress = new Progress<NoaaDownloadProgress>(p => Raise(() => DownloadProgress = p));
            var catalogue = await Task.Run(() => client.DownloadCatalogueAsync(progress, cts.Token), cts.Token).ConfigureAwait(false);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var temporary = _path + ".download";
            await TideStationCatalogueFile.SaveAsync(temporary, catalogue, cts.Token).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
            lock (_gate)
            {
                _catalogue = catalogue;
                _loading = Task.FromResult<TideStationCatalogue?>(catalogue);
            }

            Raise(() => LastCheck = null);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "Downloading the NOAA tide station list failed");
            Raise(() => LastError = ex.Message);
            return false;
        }
        finally
        {
            lock (_gate)
            {
                _downloadCts = null;
            }

            cts.Dispose();
            Raise(() =>
            {
                Activity = TideStationListActivity.Idle;
                DownloadProgress = null;
            });
        }
    }

    /// <summary>Stops a download in progress.</summary>
    public void CancelDownload()
    {
        lock (_gate)
        {
            _downloadCts?.Cancel();
        }
    }

    /// <summary>
    /// Asks NOAA whether it has newer data than the cached list (D21). A
    /// failed check leaves the offline rule in force.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The check, or <c>null</c> when there's no list or NOAA can't be reached.</returns>
    public async Task<TideCatalogueUpdateCheck?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var catalogue = await EnsureLoadedAsync().ConfigureAwait(false);
        if (catalogue is null || Activity == TideStationListActivity.Downloading)
        {
            return null;
        }

        Raise(() => Activity = TideStationListActivity.Checking);
        try
        {
            using var http = _httpFactory();
            var client = new NoaaTideStationClient(http, timeProvider: TimeProvider.System);
            var check = await client.CheckForUpdatesAsync(catalogue, cancellationToken).ConfigureAwait(false);
            Raise(() => LastCheck = check);
            return check;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            _logger?.LogInformation(ex, "Checking NOAA for a newer tide station list failed");
            return null;
        }
        finally
        {
            Raise(() =>
            {
                if (Activity == TideStationListActivity.Checking)
                {
                    Activity = TideStationListActivity.Idle;
                }
            });
        }
    }

    private async Task<TideStationCatalogue?> LoadAsync()
    {
        Raise(() => Activity = Activity == TideStationListActivity.Idle ? TideStationListActivity.Loading : Activity);
        TideStationCatalogue? catalogue = null;
        try
        {
            if (File.Exists(_path))
            {
                catalogue = await TideStationCatalogueFile.LoadAsync(_path).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException or ArgumentException or InvalidDataException)
        {
            _logger?.LogWarning(ex, "The tide station list {Path} can't be read", _path);
        }

        lock (_gate)
        {
            _catalogue = catalogue;
        }

        Raise(() =>
        {
            LastCheck = null;
            if (Activity == TideStationListActivity.Loading)
            {
                Activity = TideStationListActivity.Idle;
            }
        });
        return catalogue;
    }

    private void Raise(Action change) => _post(() =>
    {
        change();
        Changed?.Invoke(this, EventArgs.Empty);
    });
}
