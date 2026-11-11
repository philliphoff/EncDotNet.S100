namespace EncDotNet.S100.Viewer.Services.PassageCheck;

/// <summary>
/// Whether the Check route map card is open or folded to its "Legend" chip
/// (#959, handoff C8; legends design note D6), kept per user in
/// <see cref="ViewerSettings"/>. The card's rows come from the route check's
/// Legend section (<see cref="RouteCheckLegendSource"/>).
/// </summary>
internal sealed class RouteCheckLegendState
{
    private readonly ViewerSettings? _settings;
    private bool _isOpen;

    /// <param name="settings">Where the state persists; <c>null</c> keeps it for the session only.</param>
    public RouteCheckLegendState(ViewerSettings? settings)
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
