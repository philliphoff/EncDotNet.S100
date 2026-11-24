using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Mirror;
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

    /// <summary>Downloading the list, from the mirror or NOAA (see <see cref="TideStationListService.DownloadingFrom"/>).</summary>
    Downloading,

    /// <summary>Asking the mirror, or NOAA, whether there's newer data than the cached list.</summary>
    Checking,
}

/// <summary>Where <see cref="TideStationListService"/> downloads the station list from.</summary>
internal enum TideStationListSource
{
    /// <summary>The mirror of NOAA's catalogue: one file, a few seconds.</summary>
    Mirror,

    /// <summary>NOAA's metadata API: about 3,400 requests, about ten minutes.</summary>
    Noaa,
}

/// <summary>
/// The NOAA tide and current station list SoundCharts predicts from (tide
/// predictions design D15): the cached file, read once and shared by the
/// tide source switch, the station overlays and the Library; its download
/// and refresh; and whether there's newer data than it holds.
/// </summary>
/// <remarks>
/// <para>
/// The list is the <c>tide-stations</c> file at
/// <see cref="ViewerDataPaths.TideStationsFilePath"/>, the same file
/// <c>s100 tides update</c> writes. It's read on first use, not at start-up.
/// </para>
/// <para>
/// It's downloaded from a mirror of NOAA's catalogue (#1674): one file,
/// checked against the mirror's manifest, in a few seconds. When the mirror
/// can't be used, it's downloaded from NOAA itself, which takes about ten
/// minutes. A refresh never replaces the list with an older one: when the
/// mirror has nothing newer, the list is kept and
/// <see cref="IsUpToDate"/> says so.
/// </para>
/// </remarks>
internal sealed class TideStationListService
{
    private readonly string _path;
    private readonly Func<HttpClient> _httpFactory;
    private readonly TideCatalogueMirrorClientOptions? _mirror;
    private readonly NoaaTideStationClientOptions? _noaa;
    private readonly TimeProvider _time;
    private readonly Action<Action> _post;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private TideStationCatalogue? _catalogue;
    private Task<TideStationCatalogue?>? _loading;
    private CancellationTokenSource? _downloadCts;
    private bool? _hasNewerData;

    /// <summary>Creates the service.</summary>
    /// <param name="path">The cached station list.</param>
    /// <param name="httpFactory">Makes the client the list is downloaded with.</param>
    /// <param name="time">The clock, for the offline staleness rule.</param>
    /// <param name="post">Runs an action on the UI thread, where <see cref="Changed"/> is raised.</param>
    /// <param name="logger">Logs a list that can't be read or downloaded; <c>null</c> for none.</param>
    /// <param name="mirror">The mirror to download from first; <c>null</c> to download from NOAA only.</param>
    /// <param name="noaa">The options for downloads from NOAA; <c>null</c> for the defaults.</param>
    public TideStationListService(
        string path,
        Func<HttpClient> httpFactory,
        TimeProvider time,
        Action<Action> post,
        ILogger<TideStationListService>? logger,
        TideCatalogueMirrorClientOptions? mirror = null,
        NoaaTideStationClientOptions? noaa = null)
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
        _mirror = mirror;
        _noaa = noaa;
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

    /// <summary>Where the list is being downloaded from while <see cref="Activity"/> is <see cref="TideStationListActivity.Downloading"/>; otherwise <c>null</c>.</summary>
    public TideStationListSource? DownloadingFrom { get; private set; }

    /// <summary>
    /// A download from NOAA's progress while <see cref="Activity"/> is
    /// <see cref="TideStationListActivity.Downloading"/>; <c>null</c> for a
    /// download from the mirror, which is one file.
    /// </summary>
    public NoaaDownloadProgress? DownloadProgress { get; private set; }

    /// <summary>Why the last download failed, or <c>null</c>.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Whether the last refresh found nothing newer than the list, so kept
    /// it. Cleared by the next download or read.
    /// </summary>
    public bool IsUpToDate { get; private set; }

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
    /// Whether the list is stale (D15, D21): the mirror, or NOAA when the
    /// mirror can't be reached, has newer data than it holds, or, when no
    /// check has run, it's older than
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

            return _hasNewerData is { } newer
                ? newer
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
    /// list: from the mirror, or from NOAA itself when the mirror can't be
    /// used. With a list already cached, the mirror's manifest is read first,
    /// and a mirror with nothing newer leaves the list as it is
    /// (<see cref="IsUpToDate"/>). A second call while one runs returns at once.
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
            DownloadingFrom = _mirror is null ? TideStationListSource.Noaa : TideStationListSource.Mirror;
            DownloadProgress = null;
            LastError = null;
            IsUpToDate = false;
        });
        try
        {
            using var http = _httpFactory();
            if (_mirror is not null)
            {
                try
                {
                    return await Task.Run(() => DownloadFromMirrorAsync(http, cts.Token), cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsMirrorFailure(ex, cts.Token))
                {
                    _logger?.LogWarning(ex, "The tide station mirror {Mirror} can't be used; downloading from NOAA", _mirror.ManifestAddress);
                    Raise(() => DownloadingFrom = TideStationListSource.Noaa);
                }
            }

            var client = new NoaaTideStationClient(http, _noaa, TimeProvider.System);
            var progress = new Progress<NoaaDownloadProgress>(p => Raise(() => DownloadProgress = p));
            var catalogue = await Task.Run(() => client.DownloadCatalogueAsync(progress, cts.Token), cts.Token).ConfigureAwait(false);
            await TideStationCatalogueFile.SaveAsync(_path, catalogue, cts.Token).ConfigureAwait(false);
            Replace(catalogue);
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
                DownloadingFrom = null;
                DownloadProgress = null;
            });
        }
    }

    /// <summary>Downloads from the mirror unless it has nothing newer than the cached list.</summary>
    /// <returns><c>true</c> when the list was replaced.</returns>
    private async Task<bool> DownloadFromMirrorAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var client = new TideCatalogueMirrorClient(http, _mirror, TimeProvider.System);
        if (Catalogue is { } current)
        {
            var check = await client.CheckForUpdatesAsync(current, cancellationToken).ConfigureAwait(false);
            if (!check.Manifest.IsSupported)
            {
                throw new NotSupportedException($"The mirror's catalogue is in format '{check.Manifest.CatalogueFormat}' version {check.Manifest.CatalogueVersion}, which this version of SoundCharts can't read.");
            }

            if (!check.HasNewerData)
            {
                Raise(() =>
                {
                    _hasNewerData = false;
                    IsUpToDate = true;
                });
                return false;
            }
        }

        var download = await client.DownloadCatalogueAsync(_path, cancellationToken).ConfigureAwait(false);
        Replace(download.Catalogue);
        return true;
    }

    private void Replace(TideStationCatalogue catalogue)
    {
        lock (_gate)
        {
            _catalogue = catalogue;
            _loading = Task.FromResult<TideStationCatalogue?>(catalogue);
        }

        Raise(() => _hasNewerData = null);
    }

    /// <summary>Whether an exception means the mirror can't be used, not that the download was cancelled.</summary>
    private static bool IsMirrorFailure(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or InvalidDataException or NotSupportedException or FormatException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false,
    };

    /// <summary>Stops a download in progress.</summary>
    public void CancelDownload()
    {
        lock (_gate)
        {
            _downloadCts?.Cancel();
        }
    }

    /// <summary>
    /// Asks whether there's newer data than the cached list (D21): the
    /// mirror's manifest, one request, or NOAA, a few requests, when the
    /// mirror can't be used. A failed check leaves the offline rule in force.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>Whether there's newer data, or <c>null</c> when there's no list or neither can be reached.</returns>
    public async Task<bool?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
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
            bool? newer = null;
            if (_mirror is not null)
            {
                try
                {
                    var check = await new TideCatalogueMirrorClient(http, _mirror, TimeProvider.System)
                        .CheckForUpdatesAsync(catalogue, cancellationToken).ConfigureAwait(false);
                    newer = check.Manifest.IsSupported ? check.HasNewerData : null;
                }
                catch (Exception ex) when (IsMirrorFailure(ex, cancellationToken))
                {
                    _logger?.LogInformation(ex, "Checking the tide station mirror {Mirror} failed; checking NOAA", _mirror.ManifestAddress);
                }
            }

            if (newer is null)
            {
                var client = new NoaaTideStationClient(http, _noaa, TimeProvider.System);
                newer = (await client.CheckForUpdatesAsync(catalogue, cancellationToken).ConfigureAwait(false)).HasNewerData;
            }

            Raise(() => _hasNewerData = newer);
            return newer;
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
            _hasNewerData = null;
            IsUpToDate = false;
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
