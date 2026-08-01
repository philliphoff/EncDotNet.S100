using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Projects Viewer settings services into the renderer-neutral presentation
/// snapshot consumed by dataset rendering.
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
        Current = CreateSnapshot();

        _settings.PaletteChanged += _ => Refresh();
        _settings.DisplayScaleChanged += Refresh;
        _ecdisDisplay.Changed += Refresh;
        _marinerSettings.Changed += _ => Refresh();
    }

    /// <summary>The current renderer-neutral presentation snapshot.</summary>
    public MapPresentationState Current { get; private set; }

    /// <summary>Raised after Viewer inputs produce a new snapshot.</summary>
    public event Action? Changed;

    private void Refresh()
    {
        Current = CreateSnapshot();
        Changed?.Invoke();
    }

    private MapPresentationState CreateSnapshot() => new(
        _settings.SelectedPalette,
        _settings.SymbolScale,
        _settings.TextScale,
        _ecdisDisplay.Snapshot(),
        _marinerSettings.Current);
}
