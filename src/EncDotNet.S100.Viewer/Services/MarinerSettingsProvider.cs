using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IMarinerSettingsProvider"/> implementation. Wraps
/// <see cref="ViewerSettingsService"/> and rebuilds <see cref="Current"/>
/// whenever the service raises <c>MarinerChanged</c>.
/// </summary>
internal sealed class MarinerSettingsProvider : IMarinerSettingsProvider
{
    private readonly ViewerSettingsService _settings;
    private MarinerSettings _current;

    public MarinerSettingsProvider(ViewerSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _current = settings.BuildMarinerSettings();
        _settings.MarinerChanged += OnMarinerChanged;
    }

    public MarinerSettings Current => _current;

    public event Action<MarinerSettings>? Changed;

    private void OnMarinerChanged()
    {
        _current = _settings.BuildMarinerSettings();
        Changed?.Invoke(_current);
    }
}
