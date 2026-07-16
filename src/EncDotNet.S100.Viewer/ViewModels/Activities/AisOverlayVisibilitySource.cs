namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// <see cref="ITabVisibilitySource"/> that shows the Vessels tab only
/// while the AIS overlay is enabled. Bridges
/// <see cref="SettingsViewModel.AisEnabled"/> /
/// <see cref="SettingsViewModel.AisEnabledChanged"/> to the
/// activity-tab visibility contract, mirroring
/// <see cref="OwnShipTrackingVisibilitySource"/> for the Helm tab.
/// </summary>
internal sealed class AisOverlayVisibilitySource : ITabVisibilitySource, IDisposable
{
    private readonly SettingsViewModel _settings;

    public AisOverlayVisibilitySource(SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _settings.AisEnabledChanged += OnAisEnabledChanged;
    }

    /// <inheritdoc />
    public bool IsVisible => _settings.AisEnabled;

    /// <inheritdoc />
    public event Action<bool>? VisibilityChanged;

    private void OnAisEnabledChanged(bool enabled) => VisibilityChanged?.Invoke(enabled);

    public void Dispose()
        => _settings.AisEnabledChanged -= OnAisEnabledChanged;
}
