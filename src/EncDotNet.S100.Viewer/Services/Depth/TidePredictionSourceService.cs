using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Tides.Stations;
using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>Whether tide predictions feed the water level, and if not, why.</summary>
internal enum TidePredictionSourceState
{
    /// <summary>The switch is off.</summary>
    Off,

    /// <summary>The switch is on and the station list is being read.</summary>
    Loading,

    /// <summary>The switch is on and predictions are in use where there's no S-104.</summary>
    InUse,

    /// <summary>The switch is on, but there's no station list to predict from.</summary>
    NoStationList,
}

/// <summary>
/// Turns harmonic tide predictions on and off as a water-level source (tide
/// predictions design D1): with the switch on, the cached station list is
/// read and handed to the route check's source cache, so the Depth card,
/// Depth over time, the route check and chart-wide adjustment all use
/// predictions where no S-104 covers.
/// </summary>
/// <remarks>
/// The station list is the <c>tide-stations</c> file at
/// <see cref="ViewerDataPaths.TideStationsFilePath"/>. It's read once and
/// kept; <see cref="ReloadAsync"/> reads it again after it's replaced.
/// </remarks>
internal sealed class TidePredictionSourceService : IDisposable
{
    private readonly IWaterLevelAdjustmentSettings _settings;
    private readonly RouteCheckSourceCache _sources;
    private readonly string _stationsPath;
    private readonly Action<Action> _post;
    private readonly ILogger? _logger;
    private readonly object _gate = new();
    private TideStationCatalogue? _catalogue;
    private Task? _loading;
    private bool _disposed;

    /// <summary>Creates the service and applies the current setting.</summary>
    /// <param name="settings">The switch.</param>
    /// <param name="sources">The route check's source cache, shared by every water-level reader.</param>
    /// <param name="stationsPath">The cached station list.</param>
    /// <param name="post">Runs an action on the UI thread, where the sources' change is raised.</param>
    /// <param name="logger">Logs a station list that can't be read; <c>null</c> for none.</param>
    public TidePredictionSourceService(
        IWaterLevelAdjustmentSettings settings,
        RouteCheckSourceCache sources,
        string stationsPath,
        Action<Action> post,
        ILogger<TidePredictionSourceService>? logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(stationsPath);
        ArgumentNullException.ThrowIfNull(post);
        _settings = settings;
        _post = post;
        _sources = sources;
        _stationsPath = stationsPath;
        _logger = logger;
        _settings.WaterLevelAdjustmentChanged += Apply;
        Apply();
    }

    /// <summary>Whether predictions are in use, and if not, why.</summary>
    public TidePredictionSourceState State { get; private set; } = TidePredictionSourceState.Off;

    /// <summary>The station list in use, or <c>null</c>.</summary>
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

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The read of the station list in progress, or a completed task.</summary>
    public Task Loading
    {
        get
        {
            lock (_gate)
            {
                return _loading ?? Task.CompletedTask;
            }
        }
    }

    /// <summary>Reads the station list again, after it has been replaced.</summary>
    /// <returns>A task that completes when it's read and applied.</returns>
    public Task ReloadAsync()
    {
        lock (_gate)
        {
            _catalogue = null;
            _loading = null;
        }

        Apply();
        return Loading;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _settings.WaterLevelAdjustmentChanged -= Apply;
    }

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        if (!_settings.UseTidePredictions)
        {
            _sources.TidePredictions = null;
            SetState(TidePredictionSourceState.Off);
            return;
        }

        TideStationCatalogue? catalogue;
        lock (_gate)
        {
            catalogue = _catalogue;
            if (catalogue is null && _loading is null)
            {
                _loading = Task.Run(LoadAsync);
            }
        }

        if (catalogue is not null)
        {
            _sources.TidePredictions = catalogue;
            SetState(TidePredictionSourceState.InUse);
        }
        else
        {
            SetState(TidePredictionSourceState.Loading);
        }
    }

    private async Task LoadAsync()
    {
        TideStationCatalogue? catalogue = null;
        try
        {
            if (File.Exists(_stationsPath))
            {
                catalogue = await TideStationCatalogueFile.LoadAsync(_stationsPath).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException or ArgumentException or InvalidDataException)
        {
            _logger?.LogWarning(ex, "The tide station list {Path} can't be read", _stationsPath);
        }

        lock (_gate)
        {
            _catalogue = catalogue;
        }

        _post(() =>
        {
            if (_disposed || !_settings.UseTidePredictions)
            {
                return;
            }

            if (catalogue is null)
            {
                SetState(TidePredictionSourceState.NoStationList);
                return;
            }

            _sources.TidePredictions = catalogue;
            SetState(TidePredictionSourceState.InUse);
        });
    }

    private void SetState(TidePredictionSourceState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
