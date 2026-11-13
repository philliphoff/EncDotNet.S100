using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Services.Tides;

namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>Whether tide predictions feed the water level, and if not, why.</summary>
internal enum TidePredictionSourceState
{
    /// <summary>The switch is off.</summary>
    Off,

    /// <summary>The switch is on and the station list is being read or downloaded.</summary>
    Loading,

    /// <summary>The switch is on and predictions are in use where there's no S-104.</summary>
    InUse,

    /// <summary>The switch is on, but there's no station list to predict from.</summary>
    NoStationList,
}

/// <summary>
/// Turns harmonic tide predictions on and off as a water-level source (tide
/// predictions design D1): with the switch on, the station list is read (or,
/// on first use, downloaded) and handed to the route check's source cache, so
/// the Depth card, Depth over time, the route check and chart-wide adjustment
/// all use predictions where no S-104 covers.
/// </summary>
internal sealed class TidePredictionSourceService : IDisposable
{
    private readonly IWaterLevelAdjustmentSettings _settings;
    private readonly RouteCheckSourceCache _sources;
    private readonly TideStationListService _list;
    private Task _loading = Task.CompletedTask;
    private bool _requested;
    private volatile bool _attempted;
    private readonly Action<Action> _post;
    private bool _disposed;

    /// <summary>Creates the service and applies the current setting.</summary>
    /// <param name="settings">The switch.</param>
    /// <param name="sources">The route check's source cache, shared by every water-level reader.</param>
    /// <param name="list">The station list, shared with the station overlays.</param>
    /// <param name="post">Runs an action on the UI thread, where the sources' change is raised.</param>
    public TidePredictionSourceService(
        IWaterLevelAdjustmentSettings settings,
        RouteCheckSourceCache sources,
        TideStationListService list,
        Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(list);
        _settings = settings;
        _sources = sources;
        _list = list;
        _settings.WaterLevelAdjustmentChanged += Apply;
        _list.Changed += OnListChanged;
        Apply();
    }

    /// <summary>Whether predictions are in use, and if not, why.</summary>
    public TidePredictionSourceState State { get; private set; } = TidePredictionSourceState.Off;

    /// <summary>The station list in use, or <c>null</c>.</summary>
    public TideStationCatalogue? Catalogue => State == TidePredictionSourceState.InUse ? _list.Catalogue : null;

    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The read of the station list in progress, or a completed task.</summary>
    public Task Loading => _loading;

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _settings.WaterLevelAdjustmentChanged -= Apply;
        _list.Changed -= OnListChanged;
    }

    private void OnListChanged(object? sender, EventArgs e)
    {
        if (_settings.UseTidePredictions)
        {
            Apply();
        }
    }

    private void Apply()
    {
        if (_disposed)
        {
            return;
        }

        if (!_settings.UseTidePredictions)
        {
            _requested = false;
            _attempted = false;
            _sources.TidePredictions = null;
            SetState(TidePredictionSourceState.Off);
            return;
        }

        if (_list.Catalogue is { } catalogue)
        {
            _sources.TidePredictions = catalogue;
            SetState(TidePredictionSourceState.InUse);
            return;
        }

        _sources.TidePredictions = null;
        if (!_requested)
        {
            // Read the cached list, or download it on first use (D15). The
            // list raises Changed when it's done, which applies again.
            _requested = true;
            _loading = _list.EnsureAvailableAsync()
                .ContinueWith(
                    _ =>
                    {
                        _attempted = true;
                        _post(Apply);
                    },
                    TaskScheduler.Default);
            SetState(TidePredictionSourceState.Loading);
            return;
        }

        SetState(_attempted && _list.Activity == TideStationListActivity.Idle
            ? TidePredictionSourceState.NoStationList
            : TidePredictionSourceState.Loading);
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
