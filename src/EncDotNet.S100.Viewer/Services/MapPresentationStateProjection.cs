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

    public MapPresentationStateProjection(
        SettingsViewModel settings,
        EcdisDisplayState ecdisDisplay,
        IMarinerSettingsProvider marinerSettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ecdisDisplay);
        ArgumentNullException.ThrowIfNull(marinerSettings);

        _settings = settings;
        _ecdisDisplay = ecdisDisplay;
        _marinerSettings = marinerSettings;
    }

    /// <summary>
    /// Captures the current Viewer inputs as one immutable presentation state.
    /// </summary>
    public MapPresentationState CreateSnapshot() => new(
        _settings.SelectedPalette,
        _settings.SymbolScale,
        _settings.TextScale,
        _ecdisDisplay.Snapshot(),
        _marinerSettings.Current);
}
