namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// <see cref="ITabVisibilitySource"/> that shows the Helm tab only while
/// own-vessel tracking (the "Show own vessel" overlay) is enabled. Bridges
/// <see cref="SettingsViewModel.OwnShipOverlayEnabled"/> /
/// <see cref="SettingsViewModel.OwnShipOverlayEnabledChanged"/> to the
/// activity-tab visibility contract.
/// </summary>
internal sealed class OwnShipTrackingVisibilitySource : ITabVisibilitySource, IDisposable
{
    private readonly SettingsViewModel _settings;

    public OwnShipTrackingVisibilitySource(SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _settings.OwnShipOverlayEnabledChanged += OnOverlayEnabledChanged;
    }

    /// <inheritdoc />
    public bool IsVisible => _settings.OwnShipOverlayEnabled;

    /// <inheritdoc />
    public event Action<bool>? VisibilityChanged;

    private void OnOverlayEnabledChanged(bool enabled) => VisibilityChanged?.Invoke(enabled);

    public void Dispose()
        => _settings.OwnShipOverlayEnabledChanged -= OnOverlayEnabledChanged;
}
