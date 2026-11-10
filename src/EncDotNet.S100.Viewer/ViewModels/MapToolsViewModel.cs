using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The map's interactive tools: Cursor Pick, Measure, Range &amp; bearing and
/// the route editor. Owns the <see cref="MapToolController"/> that keeps one
/// tool active at a time, the commands and shortcuts that switch between
/// them, and the cards and status-bar summary the active tool shows.
/// </summary>
internal sealed class MapToolsViewModel : ViewModelBase
{
    private readonly RoutesService _routes;
    private readonly MeasureSession _measure;
    private readonly RangeBearingTool _rangeBearingTool;

    public MapToolsViewModel(
        IMeasureOverlayAppearanceProvider measureAppearance,
        MeasureSession measure,
        MeasureCardViewModel measureCard,
        RangeBearingState rangeBearing,
        RoutesService routes,
        SettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(measureAppearance);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(measureCard);
        ArgumentNullException.ThrowIfNull(rangeBearing);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(settings);

        _routes = routes;
        _measure = measure;
        _measure.Unit = settings.DistanceUnit;
        settings.DistanceUnitChanged += unit => _measure.Unit = unit;

        MeasureCard = measureCard;
        MeasureCard.CloseRequested += (_, _) => Tools.Activate(null);
        _measure.ActivationRequested += (_, _) => Tools.Activate(MeasureTool.ToolId);

        Tools.Register(new PickTool());
        Tools.Register(new MeasureTool(measureAppearance, _measure));
        Tools.Register(new RouteEditTool(_routes));
        _rangeBearingTool = new RangeBearingTool(rangeBearing, measureAppearance, () => settings.DistanceUnit);
        Tools.Register(_rangeBearingTool);
        RangeBearing = new RangeBearingViewModel(
            rangeBearing,
            () => settings.DistanceUnit,
            () => Tools.Activate(null));
        settings.DistanceUnitChanged += _ =>
        {
            RangeBearing.OnUnitChanged();
            _rangeBearingTool.InvalidateOverlay();
        };
        // The MCP tools turn the tool on through the shared state, as
        // set_route_edit_mode does through RoutesService.
        rangeBearing.ToolActiveChanged += (_, _) =>
        {
            if (rangeBearing.IsToolActive != IsRangeBearingModeActive)
                Tools.Activate(rangeBearing.IsToolActive ? RangeBearingTool.ToolId : null);
        };

        TogglePickModeCommand = new RelayCommand(() => Tools.Toggle(PickTool.ToolId));
        ExitPickModeCommand = new RelayCommand(() => Tools.Activate(null));
        ToggleMeasureModeCommand = new RelayCommand(() => Tools.Toggle(MeasureTool.ToolId));
        ToggleRangeBearingModeCommand = new RelayCommand(() => Tools.Toggle(RangeBearingTool.ToolId));
        ToggleRouteEditModeCommand = new RelayCommand(() => Tools.Toggle(RouteEditTool.ToolId));
        EnterRouteEditModeCommand = new RelayCommand(() => Tools.Activate(RouteEditTool.ToolId));
        var promote = new RelayCommand(
            PromoteMeasurementToRoute,
            () => _measure.State.Waypoints.Count >= 2);
        PromoteMeasurementToRouteCommand = promote;
        _measure.Changed += (_, _) => promote.NotifyCanExecuteChanged();
        var commit = new RelayCommand(
            () => Tools.OnAction(MapToolAction.Commit),
            () => Tools.ActiveTool != null);
        var backstep = new RelayCommand(
            () => Tools.OnAction(MapToolAction.Backstep),
            () => Tools.ActiveTool != null);
        ToolCommitCommand = commit;
        ToolBackstepCommand = backstep;

        // Mirror the controller's active-tool changes onto the mode flags.
        // Also re-evaluate Commit/Backstep CanExecute so that the
        // window-level Enter/Backspace key bindings only consume those
        // keys while a tool is actually active — without this guard
        // typing Backspace in any TextBox is swallowed by the binding.
        Tools.ActiveToolChanged += _ =>
        {
            var newPick = Tools.IsActive(PickTool.ToolId);
            if (_isPickModeActive != newPick)
            {
                _isPickModeActive = newPick;
                OnPropertyChanged(nameof(IsPickModeActive));
            }
            OnPropertyChanged(nameof(IsMeasureModeActive));
            OnPropertyChanged(nameof(IsRouteEditModeActive));
            OnPropertyChanged(nameof(IsRangeBearingModeActive));
            OnPropertyChanged(nameof(IsToolSummaryVisible));
            // Switching away clears the tool (D19).
            if (!IsRangeBearingModeActive && RangeBearing.State.IsToolActive)
                RangeBearing.State.Reset();
            RangeBearing.State.IsToolActive = IsRangeBearingModeActive;
            commit.NotifyCanExecuteChanged();
            backstep.NotifyCanExecuteChanged();
            promote.NotifyCanExecuteChanged();
            _routes.IsEditModeActive = IsRouteEditModeActive;
            // A received route can't be edited: the tool turns back off and
            // the Routes panel offers Copy to edit instead (#960).
            if (IsRouteEditModeActive && !_routes.IsEditModeActive)
            {
                Tools.Activate(null);
                _routes.RequestEdit();
            }
        };

        // Route edit mode is shared state on RoutesService: the Routes panel's
        // Edit toggle, Ctrl+E (⌘E) and the MCP set_route_edit_mode tool set it
        // there, and the route edit tool follows.
        _routes.EditModeChanged += (_, _) =>
        {
            if (_routes.IsEditModeActive != IsRouteEditModeActive)
                Tools.Activate(_routes.IsEditModeActive ? RouteEditTool.ToolId : null);
        };
    }

    /// <summary>
    /// Map-tool registry / dispatcher. The tools are registered by the
    /// constructor; <see cref="MapToolController.Initialize"/> is called by
    /// the view layer once the visual tree is up.
    /// </summary>
    public MapToolController Tools { get; } = new();

    private bool _isPickModeActive;
    /// <summary>
    /// True when the user has activated the ECDIS-style "Cursor Pick" tool.
    /// While active, single-taps on the map perform a pick and the
    /// double-tap-to-zoom gesture is suppressed. This is transient state
    /// and is not persisted between sessions.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="Tools"/>; reflects whether
    /// <see cref="MapToolController.ActiveToolId"/> is the pick tool's id.
    /// Setting this property activates / deactivates the pick tool in
    /// the controller (deactivating any other tool that was active).
    /// </remarks>
    public bool IsPickModeActive
    {
        get => _isPickModeActive;
        set
        {
            if (_isPickModeActive == value) return;
            // Drive the change through the controller so other tools are
            // properly deactivated; the controller's ActiveToolChanged
            // callback flips the backing flag.
            Tools.Activate(value ? PickTool.ToolId : null);
        }
    }

    public ICommand TogglePickModeCommand { get; }

    /// <summary>
    /// Exits the active map tool, whichever it is. Wired to the <c>Esc</c>
    /// key on the main window.
    /// </summary>
    public ICommand ExitPickModeCommand { get; }

    /// <summary>
    /// True when the Measure Mode tool is active. Mirrors
    /// <see cref="MapToolController.ActiveToolId"/> just like
    /// <see cref="IsPickModeActive"/>.
    /// </summary>
    public bool IsMeasureModeActive => Tools.IsActive(MeasureTool.ToolId);

    public ICommand ToggleMeasureModeCommand { get; }

    /// <summary>The measure card shown while Measure is on (#1130, D1).</summary>
    public MeasureCardViewModel MeasureCard { get; }

    /// <summary>The measure tool's shared state (path, geometry, unit).</summary>
    internal MeasureSession MeasureSession => _measure;

    /// <summary>
    /// True when the Range &amp; bearing tool is active (#1130 D11). Mirrors
    /// <see cref="MapToolController.ActiveToolId"/> like
    /// <see cref="IsMeasureModeActive"/>.
    /// </summary>
    public bool IsRangeBearingModeActive => Tools.IsActive(RangeBearingTool.ToolId);

    /// <summary>Toggles the Range &amp; bearing tool (Ctrl+R, or ⌘R on macOS).</summary>
    public ICommand ToggleRangeBearingModeCommand { get; }

    /// <summary>The Range &amp; bearing card, shown while its tool is on.</summary>
    public RangeBearingViewModel RangeBearing { get; }

    /// <summary>Tooltip on the map's Range &amp; bearing button, naming its shortcut.</summary>
    public string RangeBearingModeTooltip => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Strings.Tooltip_RangeBearingMode,
        ShortcutText.Command("R"));

    /// <summary>
    /// True when the interactive route editor tool is active. Mirrors
    /// <see cref="MapToolController.ActiveToolId"/> like
    /// <see cref="IsMeasureModeActive"/>.
    /// </summary>
    public bool IsRouteEditModeActive => Tools.IsActive(RouteEditTool.ToolId);

    /// <summary>
    /// True when the tool-owned status summary should be shown — i.e. while
    /// Measure, Range &amp; bearing or the route editor is active.
    /// </summary>
    public bool IsToolSummaryVisible => IsMeasureModeActive || IsRouteEditModeActive || IsRangeBearingModeActive;

    /// <summary>Toggles the interactive route editor tool on/off.</summary>
    public ICommand ToggleRouteEditModeCommand { get; }

    /// <summary>
    /// Tooltip on the map's Route tool button, naming its shortcut
    /// (Ctrl+E, or ⌘E on macOS).
    /// </summary>
    public string RouteEditModeTooltip => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        Strings.Tooltip_RouteEditMode,
        ShortcutText.Command("E"));

    /// <summary>
    /// Activates the interactive route editor tool. Unlike
    /// <see cref="ToggleRouteEditModeCommand"/> this never turns the tool
    /// off, so it is safe to bind to an explicit "edit" affordance.
    /// </summary>
    public ICommand EnterRouteEditModeCommand { get; }

    /// <summary>
    /// Promotes the current Measure Mode path into a new editable route and
    /// switches to the route editor. Enabled only when the measurement has
    /// at least two waypoints.
    /// </summary>
    public ICommand PromoteMeasurementToRouteCommand { get; }

    /// <summary>
    /// Discrete tool actions wired to keyboard accelerators; current
    /// active tool decides what (if anything) to do.
    /// </summary>
    public ICommand ToolCommitCommand { get; }
    public ICommand ToolBackstepCommand { get; }

    /// <summary>
    /// Status-bar summary owned by the active tool (e.g. measure-leg /
    /// total). <c>null</c> when no tool is active or the tool has nothing
    /// to display.
    /// </summary>
    public string? MeasureSummary
    {
        get => _measureSummary;
        set => SetProperty(ref _measureSummary, value);
    }
    private string? _measureSummary;

    /// <summary>
    /// Copies the current Measure Mode path into a brand-new editable route,
    /// makes it the active route, and switches to the route editor tool so
    /// the user can immediately refine it. The legs keep the measure geometry:
    /// a great-circle measurement makes geodesic legs (#1130, D10).
    /// </summary>
    private void PromoteMeasurementToRoute()
    {
        var waypoints = _measure.State.Waypoints;
        if (waypoints.Count < 2)
            return;

        var name = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Strings.Routes_NewRouteNameFormat,
            _routes.Routes.Routes.Count + 1);
        var route = _routes.Routes.CreateRoute(name);
        foreach (var (lat, lon) in waypoints)
            route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(lat, lon));
        if (_measure.Geometry == EncDotNet.S100.Navigation.LegGeometry.GreatCircle)
        {
            foreach (var leg in route.Legs)
                leg.GeometryType = Routing.RouteLegGeometryType.Geodesic;
            route.NotifyChanged();
        }

        _routes.SelectedWaypointIndex = null;
        Tools.Activate(RouteEditTool.ToolId);
    }
}
