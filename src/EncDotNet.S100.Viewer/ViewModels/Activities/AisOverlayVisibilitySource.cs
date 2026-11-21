using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// <see cref="ITabVisibilitySource"/> that shows the Vessels tab only while
/// an AIS entry is loaded in the Datasets panel (data source design note D8),
/// mirroring <see cref="OwnShipTrackingVisibilitySource"/> for the Helm tab.
/// </summary>
internal sealed class AisOverlayVisibilitySource : ITabVisibilitySource, IDisposable
{
    private readonly AisPresence _presence;

    public AisOverlayVisibilitySource(AisPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        _presence = presence;
        _presence.Changed += OnChanged;
    }

    /// <inheritdoc />
    public bool IsVisible => _presence.IsLoaded;

    /// <inheritdoc />
    public event Action<bool>? VisibilityChanged;

    private void OnChanged(bool loaded) => VisibilityChanged?.Invoke(loaded);

    public void Dispose() => _presence.Changed -= OnChanged;
}
