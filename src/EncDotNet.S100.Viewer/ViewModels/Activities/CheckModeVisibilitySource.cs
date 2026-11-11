using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Shows the Clearance tab (#902) only while Check route is on for a route
/// with a draft: without one there is no clearance to profile (#959, A2).
/// Bridges <see cref="RoutesService.CheckModeChanged"/> and route edits.
/// </summary>
internal sealed class CheckModeVisibilitySource : ITabVisibilitySource, IDisposable
{
    private readonly RoutesService _routes;
    private bool _isVisible;

    public CheckModeVisibilitySource(RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes;
        _isVisible = Compute();
        _routes.CheckModeChanged += OnChanged;
        _routes.Routes.Changed += OnChanged;
    }

    public bool IsVisible => _isVisible;

    public event Action<bool>? VisibilityChanged;

    /// <summary>Whether every leg of <paramref name="route"/> has a draft, which the clearance check needs.</summary>
    internal static bool HasDraft(Route? route)
    {
        if (route is not { Legs.Count: > 0 })
            return false;
        for (var i = 0; i < route.Legs.Count; i++)
        {
            if (route.GetEffectiveLegValue(i, RoutePassageField.Draft) is null)
                return false;
        }

        return true;
    }

    private bool Compute() => _routes.IsCheckModeActive && HasDraft(_routes.Routes.ActiveRoute);

    private void OnChanged(object? sender, EventArgs e)
    {
        var visible = Compute();
        if (visible == _isVisible)
            return;
        _isVisible = visible;
        VisibilityChanged?.Invoke(visible);
    }

    public void Dispose()
    {
        _routes.CheckModeChanged -= OnChanged;
        _routes.Routes.Changed -= OnChanged;
    }
}
