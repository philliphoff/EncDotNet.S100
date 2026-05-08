using System;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Default <see cref="IMarinerSettingsProvider"/> implementation. Wraps
/// <see cref="SettingsViewModel"/> and rebuilds <see cref="Current"/>
/// whenever the view-model raises <c>MarinerChanged</c>.
/// </summary>
internal sealed class MarinerSettingsProvider : IMarinerSettingsProvider
{
    private readonly SettingsViewModel _settings;
    private MarinerSettings _current;

    public MarinerSettingsProvider(SettingsViewModel settings)
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
