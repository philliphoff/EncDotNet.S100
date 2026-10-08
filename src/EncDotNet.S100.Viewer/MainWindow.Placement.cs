using Avalonia;
using Avalonia.Controls;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer;

// Startup size / position and its persistence across sessions (#825).
public partial class MainWindow
{
    private ViewerSettings? _placementSettings;
    private DebouncedSettingsSaver? _placementSaver;
    private PixelPoint _normalPosition;
    private Size _normalSize;
    private bool _wasMaximized;

    /// <summary>
    /// Sizes and positions the window before it is shown and, for a normal
    /// interactive run, remembers its placement as it changes.
    /// </summary>
    /// <remarks>
    /// Automation runs (<c>--ephemeral</c>, or MCP enabled from the command
    /// line) keep the fixed XAML default and never read or write the saved
    /// placement, so an agent's window — and its <c>capture_app_screenshot</c>
    /// size — does not depend on the user's last session or screen.
    /// Everything else restores the saved placement, falling back to
    /// <see cref="WindowPlacementPolicy"/>'s first-run rule.
    /// </remarks>
    private void InitializeWindowPlacement(ViewerCommandSettings? options, ViewerSettings settings)
    {
        if (options?.Ephemeral == true || settings.IsReadOnly || settings.McpConfiguredFromCommandLine) return;

        var screens = Screens?.All
            .Select(s => new PlacementScreen(s.WorkingArea, s.Scaling, s.IsPrimary))
            .ToArray() ?? [];
        if (WindowPlacementPolicy.Resolve(settings.MainWindowPlacement, screens) is { } placement)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = placement.Position;
            Width = placement.Size.Width;
            Height = placement.Size.Height;
            // Maximize after the normal bounds are set so the window
            // maximizes on the right screen and un-maximizes back to them.
            WindowState = placement.State;
            _normalPosition = placement.Position;
            _normalSize = placement.Size;
            _wasMaximized = placement.State == WindowState.Maximized;
        }
        else
        {
            _normalPosition = Position;
            _normalSize = new Size(Width, Height);
        }

        // Saved as it changes (debounced), not only on close, so a crash or
        // kill still reopens where the user left the window.
        _placementSettings = settings;
        _placementSaver = new DebouncedSettingsSaver(
            save: () => { try { settings.Save(); } catch { /* best-effort */ } },
            dispatch: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        PositionChanged += (_, e) => TrackNormalBounds(e.Point, ClientSize);
        Resized += (_, e) => TrackNormalBounds(Position, e.ClientSize);
        PropertyChanged += (_, e) =>
        {
            // A minimized window keeps whether it was maximized before.
            if (e.Property == WindowStateProperty && WindowState is WindowState.Normal or WindowState.Maximized)
            {
                _wasMaximized = WindowState == WindowState.Maximized;
                RecordWindowPlacement();
            }
        };
        Closing += (_, _) =>
        {
            RecordWindowPlacement();
            _placementSaver.Flush();
        };
        Closed += (_, _) => _placementSaver.Dispose();
    }

    // Only normal-state bounds are remembered; maximized / full-screen /
    // minimized geometry is never what the user wants to restore to.
    private void TrackNormalBounds(PixelPoint position, Size clientSize)
    {
        if (WindowState != WindowState.Normal) return;
        if (clientSize.Width <= 0 || clientSize.Height <= 0) return;
        _normalPosition = position;
        _normalSize = clientSize;
        RecordWindowPlacement();
    }

    private void RecordWindowPlacement()
    {
        if (_placementSettings is not { } settings || _placementSaver is null) return;
        if (_normalSize.Width <= 0 || _normalSize.Height <= 0) return;
        var placement = new WindowPlacement
        {
            X = _normalPosition.X,
            Y = _normalPosition.Y,
            Width = Math.Round(_normalSize.Width),
            Height = Math.Round(_normalSize.Height),
            IsMaximized = _wasMaximized,
        };
        if (settings.MainWindowPlacement is { } current
            && (current.X, current.Y, current.Width, current.Height, current.IsMaximized)
                == (placement.X, placement.Y, placement.Width, placement.Height, placement.IsMaximized))
        {
            return;
        }
        settings.MainWindowPlacement = placement;
        _placementSaver.RequestSave();
    }
}
