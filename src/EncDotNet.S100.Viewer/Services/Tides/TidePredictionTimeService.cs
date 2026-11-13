using EncDotNet.S100.Viewer.Services.Depth;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// Gives the view time a meaning while tide predictions are on (tide
/// predictions design D14): while the source switch or a station overlay is
/// on, the Timeline and its HUD show even with no timed dataset, because
/// depths and the station glyphs then change with time.
/// </summary>
internal sealed class TidePredictionTimeService : IDisposable
{
    private readonly GlobalTimeService _time;
    private readonly IWaterLevelAdjustmentSettings _settings;
    private readonly TideStationSource _stations;
    private readonly CurrentStationSource _currents;

    /// <summary>Creates the service and applies the current state.</summary>
    /// <param name="time">The view time.</param>
    /// <param name="settings">The tide predictions source switch.</param>
    /// <param name="stations">The tide station overlay's source.</param>
    /// <param name="currents">The current station overlay's source.</param>
    public TidePredictionTimeService(
        GlobalTimeService time,
        IWaterLevelAdjustmentSettings settings,
        TideStationSource stations,
        CurrentStationSource currents)
    {
        ArgumentNullException.ThrowIfNull(currents);
        _currents = currents;
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(stations);
        _time = time;
        _settings = settings;
        _stations = stations;
        _settings.WaterLevelAdjustmentChanged += Apply;
        _stations.ActiveChanged += OnActiveChanged;
        _currents.ActiveChanged += OnActiveChanged;
        Apply();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _settings.WaterLevelAdjustmentChanged -= Apply;
        _stations.ActiveChanged -= OnActiveChanged;
        _currents.ActiveChanged -= OnActiveChanged;
    }

    private void OnActiveChanged(object? sender, EventArgs e) => UiThread.RunOrPost(Apply);

    private void Apply() => _time.SetOpenEndedTime(_settings.UseTidePredictions || _stations.IsActive || _currents.IsActive);
}
