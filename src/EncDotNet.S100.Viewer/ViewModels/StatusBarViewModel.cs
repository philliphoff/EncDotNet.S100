using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The status bar's readouts: the cursor's latitude and longitude, the map
/// scale, the overscale pill, the depth basis pill and the embedded MCP
/// server's status, including the port-conflict notification.
/// </summary>
internal sealed class StatusBarViewModel : ViewModelBase
{
    private readonly INotificationService _notifications;
    private readonly Action<Action> _post;
    private INotificationHandle? _mcpPortConflictNotification;

    /// <param name="mcpServerHost">The embedded MCP server's host, or <see langword="null"/> when there is none and the MCP status stays hidden.</param>
    /// <param name="notifications">Shows the MCP port-conflict notification.</param>
    /// <param name="post">Posts MCP server changes, which arrive on background threads, to the UI thread.</param>
    public StatusBarViewModel(McpServerHost? mcpServerHost, INotificationService notifications, Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(post);

        _notifications = notifications;
        _post = post;
        _mcpServerHost = mcpServerHost;

        if (mcpServerHost is { } host)
        {
            host.ServerChanged += (_, _) => AttachToMcpServer();
            host.McpPortConflict += OnMcpPortConflict;
            AttachToMcpServer();
        }
    }

    private string _mouseLatText = LatLonFormatter.Placeholder;
    /// <summary>
    /// Latitude component of the cursor readout (e.g. <c>"50°46.024'N"</c>),
    /// shown in its own fixed-width status-bar cell so the longitude and the
    /// adjacent scale text don't jitter as the value changes. Empty when the
    /// cursor is not over the map.
    /// </summary>
    public string MouseLatText
    {
        get => _mouseLatText;
        set
        {
            if (SetProperty(ref _mouseLatText, value))
                OnPropertyChanged(nameof(IsMouseLatLonVisible));
        }
    }

    private string _mouseLonText = LatLonFormatter.Placeholder;
    /// <summary>
    /// Longitude component of the cursor readout (e.g. <c>"001°15.558'W"</c>),
    /// shown in its own fixed-width status-bar cell. Empty when the cursor is
    /// not over the map.
    /// </summary>
    public string MouseLonText
    {
        get => _mouseLonText;
        set => SetProperty(ref _mouseLonText, value);
    }

    /// <summary>True when the cursor is over the map and a lat/long is shown.</summary>
    public bool IsMouseLatLonVisible => _mouseLatText.Length > 0;

    private string _mapScaleText = string.Empty;
    /// <summary>
    /// The current map scale denominator, formatted as <c>"1:180 000"</c>,
    /// shown in the status bar for issue-reporting and orientation. Empty
    /// when no scale is available (e.g. before the first viewport update).
    /// </summary>
    public string MapScaleText
    {
        get => _mapScaleText;
        set
        {
            if (SetProperty(ref _mapScaleText, value))
                OnPropertyChanged(nameof(IsMapScaleVisible));
        }
    }

    /// <summary>True when a map scale is available to display.</summary>
    public bool IsMapScaleVisible => _mapScaleText.Length > 0;

    private bool _isOverscaleVisible;
    /// <summary>
    /// True when at least one loaded cell in the current view is overscaled
    /// (the view is zoomed in past the cell's finest compilation scale). Drives
    /// the status-bar overscale pill (issue #441; S-52 / S-101 overscale
    /// indication).
    /// </summary>
    public bool IsOverscaleVisible
    {
        get => _isOverscaleVisible;
        private set => SetProperty(ref _isOverscaleVisible, value);
    }

    private string _overscaleFactorText = string.Empty;
    /// <summary>
    /// The worst-offender overscale factor formatted for the pill (e.g.
    /// <c>"4.6×"</c>). Empty when nothing in view is overscaled.
    /// </summary>
    public string OverscaleFactorText
    {
        get => _overscaleFactorText;
        private set => SetProperty(ref _overscaleFactorText, value);
    }

    private IReadOnlyList<OverscaleCellItemViewModel> _overscaleCells = [];
    /// <summary>
    /// The overscaled cells in view, worst first, shown in the pill's popup.
    /// Each carries the cell name and its formatted overscale factor.
    /// </summary>
    public IReadOnlyList<OverscaleCellItemViewModel> OverscaleCells
    {
        get => _overscaleCells;
        private set => SetProperty(ref _overscaleCells, value);
    }

    /// <summary>
    /// Applies a freshly-computed overscale <paramref name="report"/> to the
    /// status-bar indicator state. Called on every viewport change.
    /// </summary>
    /// <param name="report">The overscale report (never <see langword="null"/>).</param>
    public void UpdateOverscale(Rendering.Engine.OverscaleReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.IsOverscaled)
        {
            IsOverscaleVisible = false;
            OverscaleFactorText = string.Empty;
            OverscaleCells = [];
            return;
        }

        OverscaleFactorText = FormatOverscaleFactor(report.WorstFactor);
        OverscaleCells = [.. report.OverscaledCells.Select(
            c => new OverscaleCellItemViewModel(c.Name, FormatOverscaleFactor(c.Factor)))];
        IsOverscaleVisible = true;
    }

    /// <summary>
    /// The status bar's depth basis pill (#1191): what the depths and safety
    /// contour on the chart are relative to.
    /// </summary>
    public DepthBasisIndicatorViewModel DepthBasis { get; } = new();

    /// <summary>
    /// Applies a freshly-computed depth <paramref name="basis"/> to the
    /// status-bar indicator. Called whenever the depth basis service
    /// recomputes, and when the Local/UTC setting changes.
    /// </summary>
    /// <param name="basis">The basis (never <see langword="null"/>).</param>
    /// <param name="waterLevelLoaded">True when an active S-104 dataset is loaded, in view or not.</param>
    /// <param name="format">The user's Local/UTC setting.</param>
    public void UpdateDepthBasis(Services.Depth.DepthBasis basis, bool waterLevelLoaded, TimeFormat format) =>
        DepthBasis.Update(basis, waterLevelLoaded, format);

    private static string FormatOverscaleFactor(double factor)
        => string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Strings.Status_OverscaleFactor,
            factor);

    private readonly McpServerHost? _mcpServerHost;
    private EncDotNet.S100.Mcp.S100McpServer? _attachedMcpServer;

    /// <summary>True when the embedded MCP server is currently listening.</summary>
    public bool IsMcpRunning =>
        _mcpServerHost?.Server is { IsRunning: true };

    /// <summary>
    /// Status-bar text describing the MCP server. Empty when not running.
    /// </summary>
    public string McpStatusText
    {
        get
        {
            var server = _mcpServerHost?.Server;
            if (server is null || !server.IsRunning || server.Port is null)
                return string.Empty;
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Strings.Status_McpRunning,
                server.Port,
                server.ConnectionCount);
        }
    }

    /// <summary>Tooltip describing the MCP server endpoint.</summary>
    public string McpTooltipText
    {
        get
        {
            var server = _mcpServerHost?.Server;
            if (server?.Endpoint is null)
                return string.Empty;
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Strings.Tooltip_McpEndpoint,
                server.Endpoint) + " " + Strings.Tooltip_McpLoopback;
        }
    }

    private void AttachToMcpServer()
    {
        if (_attachedMcpServer is { } prev)
        {
            prev.StateChanged -= OnMcpServerChanged;
            prev.ConnectionsChanged -= OnMcpServerChanged;
        }
        _attachedMcpServer = _mcpServerHost?.Server;
        if (_attachedMcpServer is { } next)
        {
            next.StateChanged += OnMcpServerChanged;
            next.ConnectionsChanged += OnMcpServerChanged;
        }
        OnMcpServerChanged(null, EventArgs.Empty);
    }

    private void OnMcpServerChanged(object? sender, EventArgs e)
    {
        _post(() =>
        {
            OnPropertyChanged(nameof(IsMcpRunning));
            OnPropertyChanged(nameof(McpStatusText));
            OnPropertyChanged(nameof(McpTooltipText));
        });
    }

    private void OnMcpPortConflict(object? sender, McpPortConflictEventArgs e)
    {
        var conflictPort = e.Port;
        _post(() =>
        {
            _mcpPortConflictNotification = _notifications
                .Create(Strings.Toast_McpPortConflictTitle)
                .WithSeverity(NotificationSeverity.Error)
                .WithContent(string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.Toast_McpPortConflictBody,
                    conflictPort))
                .WithAction(
                    Strings.Toast_McpPortFindAnother,
                    () => _ = FindAnotherMcpPortAsync(),
                    dismissOnInvoke: false)
                .Persistent()
                .Show();
        });
    }

    private async Task FindAnotherMcpPortAsync()
    {
        if (_mcpServerHost is not { } host) return;

        int? newPort;
        try
        {
            newPort = await host.ResetPortAsync().ConfigureAwait(false);
        }
        catch
        {
            newPort = null;
        }

        if (newPort is { } port)
        {
            // Reuse the conflict notification: transition it in place to a
            // success message that auto-dismisses, rather than stacking a
            // second notification.
            var handle = _mcpPortConflictNotification;
            _mcpPortConflictNotification = null;
            if (handle is not null && !handle.IsDismissed)
            {
                handle.SetActions();
                handle.Update(
                    title: Strings.Toast_McpPortReassignedTitle,
                    message: string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.Toast_McpPortReassignedBody,
                        port),
                    severity: NotificationSeverity.Info);
                handle.ScheduleAutoDismiss(
                    NotificationService.DefaultDelayFor(NotificationSeverity.Info));
            }
            else
            {
                _notifications.Create(Strings.Toast_McpPortReassignedTitle)
                    .WithSeverity(NotificationSeverity.Info)
                    .WithContent(string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.Toast_McpPortReassignedBody,
                        port))
                    .Show();
            }
        }
    }
}
