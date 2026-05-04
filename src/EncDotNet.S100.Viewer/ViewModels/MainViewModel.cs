using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class MainViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;
    private readonly IThemeService _theme;
    private readonly IRecentFilesService _recentFiles;

    public FeatureCataloguesViewModel FeatureCatalogues { get; }
    public PortrayalCataloguesViewModel PortrayalCatalogues { get; }
    public DatasetsViewModel Datasets { get; }
    public CatalogPanelViewModel CatalogPanel { get; }
    public SettingsViewModel Settings { get; }
    public PickReportViewModel PickReport { get; }
    public TimelineViewModel Timeline { get; }

    private ActivityKind? _selectedActivity;
    public ActivityKind? SelectedActivity
    {
        get => _selectedActivity;
        set
        {
            // Toggle: clicking the same activity again hides the pane
            if (_selectedActivity == value)
                value = null;

            if (SetProperty(ref _selectedActivity, value))
            {
                OnPropertyChanged(nameof(IsPaneVisible));
                OnPropertyChanged(nameof(PaneTitle));
                OnPropertyChanged(nameof(IsFeatureCataloguesSelected));
                OnPropertyChanged(nameof(IsPortrayalCataloguesSelected));
                OnPropertyChanged(nameof(IsDatasetsSelected));
                OnPropertyChanged(nameof(IsCatalogSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));

                // Persist the last selected activity (Settings is transient, don't remember it)
                _settings.LastSelectedActivity = value is ActivityKind.Settings ? null : value?.ToString();
                _settings.Save();
            }
        }
    }

    public bool IsPaneVisible => _selectedActivity.HasValue;

    public string PaneTitle => _selectedActivity switch
    {
        ActivityKind.FeatureCatalogues => Strings.Pane_FeatureCatalogues,
        ActivityKind.PortrayalCatalogues => Strings.Pane_PortrayalCatalogues,
        ActivityKind.Datasets => Strings.Pane_Datasets,
        ActivityKind.Catalog => Strings.Pane_Catalog,
        ActivityKind.Settings => Strings.Pane_Settings,
        _ => string.Empty,
    };

    public bool IsFeatureCataloguesSelected => _selectedActivity == ActivityKind.FeatureCatalogues;
    public bool IsPortrayalCataloguesSelected => _selectedActivity == ActivityKind.PortrayalCatalogues;
    public bool IsDatasetsSelected => _selectedActivity == ActivityKind.Datasets;
    public bool IsCatalogSelected => _selectedActivity == ActivityKind.Catalog;
    public bool IsSettingsSelected => _selectedActivity == ActivityKind.Settings;

    public ICommand SelectFeatureCataloguesCommand { get; }
    public ICommand SelectPortrayalCataloguesCommand { get; }
    public ICommand SelectDatasetsCommand { get; }
    public ICommand SelectCatalogCommand { get; }
    public ICommand SelectSettingsCommand { get; }
    public ICommand TogglePrimarySideBarCommand { get; }

    private string? _statusText;
    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isStatusBarVisible;
    public bool IsStatusBarVisible
    {
        get => _isStatusBarVisible;
        set
        {
            if (SetProperty(ref _isStatusBarVisible, value))
            {
                _settings.IsStatusBarVisible = value;
                _settings.Save();
            }
        }
    }

    public ICommand ToggleStatusBarCommand { get; }

    private bool _isTimelineVisible;
    /// <summary>
    /// User preference for whether the bottom timeline panel is
    /// shown. When false, the timeline is hidden regardless of
    /// whether time-varying datasets are loaded. Persisted to
    /// <see cref="ViewerSettings"/>.
    /// </summary>
    public bool IsTimelineVisible
    {
        get => _isTimelineVisible;
        set
        {
            if (SetProperty(ref _isTimelineVisible, value))
            {
                _settings.IsTimelineVisible = value;
                _settings.Save();
            }
        }
    }

    public ICommand ToggleTimelineCommand { get; }

    private string _mouseLatLonText = LatLonFormatter.Placeholder;
    /// <summary>
    /// Lat/long of the mouse cursor when it is over the map, formatted in
    /// degrees-decimal-minutes (mariner-friendly). Set to
    /// <see cref="LatLonFormatter.Placeholder"/> when the cursor is not over
    /// the map.
    /// </summary>
    public string MouseLatLonText
    {
        get => _mouseLatLonText;
        set => SetProperty(ref _mouseLatLonText, value);
    }

    private bool _isPickPanelEnabled;
    /// <summary>
    /// User preference for whether the pick panel is allowed to auto-open
    /// when a feature is picked. Persisted to <see cref="ViewerSettings"/>.
    /// </summary>
    public bool IsPickPanelEnabled
    {
        get => _isPickPanelEnabled;
        set
        {
            if (SetProperty(ref _isPickPanelEnabled, value))
            {
                _settings.IsPickPanelVisible = value;
                _settings.Save();
                OnPropertyChanged(nameof(IsPickPanelVisible));
            }
        }
    }

    /// <summary>
    /// True when the pick panel should be displayed: a pick is active and
    /// the user hasn't disabled the panel via the View menu.
    /// </summary>
    public bool IsPickPanelVisible => _isPickPanelEnabled && PickReport.HasPick;

    public ICommand TogglePickPanelCommand { get; }

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
            // callback below will flip our backing flag.
            Tools.Activate(value ? PickTool.ToolId : null);
        }
    }

    public ICommand TogglePickModeCommand { get; }

    /// <summary>
    /// Convenience command that exits the active map tool (pick or measure).
    /// Wired to the <c>Esc</c> key on the main window.
    /// </summary>
    public ICommand ExitPickModeCommand { get; }

    /// <summary>
    /// True when the Measure Mode tool is active. Mirrors
    /// <see cref="MapToolController.ActiveToolId"/> just like
    /// <see cref="IsPickModeActive"/>.
    /// </summary>
    public bool IsMeasureModeActive => Tools.IsActive(MeasureTool.ToolId);

    public ICommand ToggleMeasureModeCommand { get; }

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
    /// Map-tool registry / dispatcher. Tools (pick, measure) are
    /// registered from the constructor so view-model-level commands and
    /// tests work without the host having to register anything.
    /// <see cref="MapToolController.Initialize"/> is called by the view
    /// layer once the visual tree is up.
    /// </summary>
    public MapToolController Tools { get; } = new();

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    public ICommand ToggleThemeCommand { get; }

    /// <summary>
    /// Opens a dataset that the user has previously loaded. If the file is
    /// no longer at the recorded path the entry is dropped from the recent
    /// list and a status message is shown.
    /// </summary>
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }

    public MainViewModel(
        ViewerSettings settings,
        FeatureCataloguesViewModel featureCatalogues,
        PortrayalCataloguesViewModel portrayalCatalogues,
        DatasetsViewModel datasets,
        CatalogPanelViewModel catalogPanel,
        SettingsViewModel settingsViewModel,
        PickReportViewModel pickReport,
        TimelineViewModel timeline,
        IThemeService themeService,
        IRecentFilesService recentFiles,
        IMeasureOverlayAppearanceProvider measureAppearance)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(featureCatalogues);
        ArgumentNullException.ThrowIfNull(portrayalCatalogues);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(catalogPanel);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(pickReport);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(measureAppearance);

        _settings = settings;
        _theme = themeService;
        _recentFiles = recentFiles;
        _isDarkTheme = themeService.IsDarkTheme;

        Tools.Register(new PickTool());
        Tools.Register(new MeasureTool(measureAppearance));

        FeatureCatalogues = featureCatalogues;
        PortrayalCatalogues = portrayalCatalogues;
        Datasets = datasets;
        CatalogPanel = catalogPanel;
        Settings = settingsViewModel;
        PickReport = pickReport;
        Timeline = timeline;
        Timeline.CloseRequested += () => IsTimelineVisible = false;
        PickReport.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PickReportViewModel.HasPick))
                OnPropertyChanged(nameof(IsPickPanelVisible));
        };

        SelectFeatureCataloguesCommand = new RelayCommand(() => SelectedActivity = ActivityKind.FeatureCatalogues);
        SelectPortrayalCataloguesCommand = new RelayCommand(() => SelectedActivity = ActivityKind.PortrayalCatalogues);
        SelectDatasetsCommand = new RelayCommand(() => SelectedActivity = ActivityKind.Datasets);
        SelectCatalogCommand = new RelayCommand(() => SelectedActivity = ActivityKind.Catalog);
        SelectSettingsCommand = new RelayCommand(() => SelectedActivity = ActivityKind.Settings);

        TogglePrimarySideBarCommand = new RelayCommand(() =>
        {
            if (_selectedActivity.HasValue)
            {
                // Close the pane (set field directly, then notify)
                _selectedActivity = null;
            }
            else
            {
                // Re-open with Datasets as default, or the last persisted activity
                _selectedActivity = (settings.LastSelectedActivity is { } last
                    && Enum.TryParse<ActivityKind>(last, out var restored))
                    ? restored
                    : ActivityKind.Datasets;
            }

            OnPropertyChanged(nameof(SelectedActivity));
            OnPropertyChanged(nameof(IsPaneVisible));
            OnPropertyChanged(nameof(PaneTitle));
            OnPropertyChanged(nameof(IsFeatureCataloguesSelected));
            OnPropertyChanged(nameof(IsPortrayalCataloguesSelected));
            OnPropertyChanged(nameof(IsDatasetsSelected));
            OnPropertyChanged(nameof(IsCatalogSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
        });

        _isStatusBarVisible = settings.IsStatusBarVisible;

        ToggleStatusBarCommand = new RelayCommand(() => IsStatusBarVisible = !IsStatusBarVisible);

        _isTimelineVisible = settings.IsTimelineVisible;
        ToggleTimelineCommand = new RelayCommand(() => IsTimelineVisible = !IsTimelineVisible);

        _isPickPanelEnabled = settings.IsPickPanelVisible;
        TogglePickPanelCommand = new RelayCommand(() => IsPickPanelEnabled = !IsPickPanelEnabled);

        TogglePickModeCommand = new RelayCommand(() => Tools.Toggle(PickTool.ToolId));
        ExitPickModeCommand = new RelayCommand(() => Tools.Activate(null));
        ToggleMeasureModeCommand = new RelayCommand(() => Tools.Toggle(MeasureTool.ToolId));
        ToolCommitCommand = new RelayCommand(() => Tools.OnAction(MapToolAction.Commit));
        ToolBackstepCommand = new RelayCommand(() => Tools.OnAction(MapToolAction.Backstep));

        // Mirror the controller's active-tool changes onto the legacy
        // IsPickModeActive flag (kept so existing XAML/cursor wiring
        // continues to function) and the IsMeasureModeActive property.
        Tools.ActiveToolChanged += _ =>
        {
            var newPick = Tools.IsActive(PickTool.ToolId);
            if (_isPickModeActive != newPick)
            {
                _isPickModeActive = newPick;
                OnPropertyChanged(nameof(IsPickModeActive));
            }
            OnPropertyChanged(nameof(IsMeasureModeActive));
        };

        ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = _theme.ToggleTheme());

        OpenRecentCommand = new AsyncRelayCommand<string>(OpenRecentAsync);

        // Restore last selected activity (set field directly to avoid re-saving)
        if (settings.LastSelectedActivity is { } last
            && Enum.TryParse<ActivityKind>(last, out var restored))
        {
            _selectedActivity = restored;
        }
    }

    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!File.Exists(path))
        {
            StatusText = string.Format(Strings.Status_FileNoLongerExists, path);
            // Drop the missing entry so the menu reflects reality.
            _recentFiles.Remove(path);
            return;
        }

        SelectedActivity = ActivityKind.Datasets;
        await Datasets.LoadFromPathAsync(path);
    }
}
