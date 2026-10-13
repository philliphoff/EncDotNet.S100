using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Shows the Clearance tab (#902) only while Check passage is on, bridging
/// <see cref="RoutesService.CheckModeChanged"/>.
/// </summary>
internal sealed class CheckModeVisibilitySource : ITabVisibilitySource, IDisposable
{
    private readonly RoutesService _routes;

    public CheckModeVisibilitySource(RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes;
        _routes.CheckModeChanged += OnCheckModeChanged;
    }

    public bool IsVisible => _routes.IsCheckModeActive;

    public event Action<bool>? VisibilityChanged;

    private void OnCheckModeChanged(object? sender, EventArgs e) => VisibilityChanged?.Invoke(_routes.IsCheckModeActive);

    public void Dispose() => _routes.CheckModeChanged -= OnCheckModeChanged;
}
