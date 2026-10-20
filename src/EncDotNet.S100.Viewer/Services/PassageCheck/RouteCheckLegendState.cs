namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// Whether the Check route map legend is open (#959, handoff C8), kept per
/// user in <see cref="ViewerSettings"/>. The legend control and the
/// <c>set_map_legend</c> / <c>get_map_legend</c> tools share it.
/// </summary>
internal sealed class RouteCheckLegendState
{
    private readonly ViewerSettings? _settings;
    private bool _isOpen;

    /// <param name="settings">Where the state persists; <c>null</c> keeps it for the session only.</param>
    public RouteCheckLegendState(ViewerSettings? settings = null)
    {
        _settings = settings;
        _isOpen = settings?.IsRouteCheckLegendOpen ?? true;
    }

    /// <summary>Raised when <see cref="IsOpen"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether the legend is open; folded it's a "Legend" chip.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (_isOpen == value)
                return;
            _isOpen = value;
            if (_settings is not null)
            {
                _settings.IsRouteCheckLegendOpen = value;
                _settings.Save();
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
