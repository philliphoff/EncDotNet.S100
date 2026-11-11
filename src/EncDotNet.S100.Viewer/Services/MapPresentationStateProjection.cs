using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Projects the current Viewer settings into a renderer-neutral presentation
/// snapshot.
/// </summary>
internal sealed class MapPresentationStateProjection
{
    private readonly SettingsViewModel _settings;
    private readonly EcdisDisplayState _ecdisDisplay;
    private readonly IMarinerSettingsProvider _marinerSettings;
    private readonly TimeProvider _time;
    private readonly IAmlClbPresence _amlClb;

    public MapPresentationStateProjection(
        SettingsViewModel settings,
        EcdisDisplayState ecdisDisplay,
        IMarinerSettingsProvider marinerSettings,
        TimeProvider time,
        IAmlClbPresence amlClb)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ecdisDisplay);
        ArgumentNullException.ThrowIfNull(marinerSettings);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(amlClb);

        _settings = settings;
        _ecdisDisplay = ecdisDisplay;
        _marinerSettings = marinerSettings;
        _time = time;
        _amlClb = amlClb;
    }

    /// <summary>
    /// Captures the current Viewer inputs as one immutable presentation state,
    /// with the zone chart times are written in: UTC, or the machine's zone
    /// when the Local/UTC preference is Local, and the safety contour's halo
    /// while AML CLB is drawn (AML design handoff decision 22).
    /// </summary>
    public MapPresentationState CreateSnapshot() => new MapPresentationState(
        _settings.SelectedPalette,
        _settings.SymbolScale,
        _settings.TextScale,
        _ecdisDisplay.Snapshot(),
        _marinerSettings.Current)
        .WithDisplayTimeZone(_settings.SelectedTimeFormat == TimeFormat.Utc ? TimeZoneInfo.Utc : _time.LocalTimeZone)
        .WithSafetyContourHalo(_amlClb.IsClbDrawn);
}
