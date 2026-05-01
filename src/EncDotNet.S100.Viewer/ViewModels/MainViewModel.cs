using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Styling;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class MainViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;

    public FeatureCataloguesViewModel FeatureCatalogues { get; }
    public PortrayalCataloguesViewModel PortrayalCatalogues { get; }
    public DatasetsViewModel Datasets { get; }
    public SettingsViewModel Settings { get; }
    public PickReportViewModel PickReport { get; }

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
        ActivityKind.FeatureCatalogues => "FEATURE CATALOGUES",
        ActivityKind.PortrayalCatalogues => "PORTRAYAL CATALOGUES",
        ActivityKind.Datasets => "DATASETS",
        ActivityKind.Settings => "SETTINGS",
        _ => string.Empty,
    };

    public bool IsFeatureCataloguesSelected => _selectedActivity == ActivityKind.FeatureCatalogues;
    public bool IsPortrayalCataloguesSelected => _selectedActivity == ActivityKind.PortrayalCatalogues;
    public bool IsDatasetsSelected => _selectedActivity == ActivityKind.Datasets;
    public bool IsSettingsSelected => _selectedActivity == ActivityKind.Settings;

    public ICommand SelectFeatureCataloguesCommand { get; }
    public ICommand SelectPortrayalCataloguesCommand { get; }
    public ICommand SelectDatasetsCommand { get; }
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
    public bool IsPickModeActive
    {
        get => _isPickModeActive;
        set => SetProperty(ref _isPickModeActive, value);
    }

    public ICommand TogglePickModeCommand { get; }

    /// <summary>
    /// Convenience command that exits Pick Mode (no-op when already off).
    /// Wired to the <c>Esc</c> key on the main window.
    /// </summary>
    public ICommand ExitPickModeCommand { get; }

    private bool _isDarkTheme = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    public ICommand ToggleThemeCommand { get; }

    public MainViewModel(ViewerSettings settings, PortrayalCatalogueManager catalogueManager)
    {
        _settings = settings;

        FeatureCatalogues = new FeatureCataloguesViewModel(settings);
        PortrayalCatalogues = new PortrayalCataloguesViewModel(settings, catalogueManager);
        Datasets = new DatasetsViewModel();
        Settings = new SettingsViewModel(settings);
        PickReport = new PickReportViewModel();
        PickReport.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PickReportViewModel.HasPick))
                OnPropertyChanged(nameof(IsPickPanelVisible));
        };

        SelectFeatureCataloguesCommand = new RelayCommand(() => SelectedActivity = ActivityKind.FeatureCatalogues);
        SelectPortrayalCataloguesCommand = new RelayCommand(() => SelectedActivity = ActivityKind.PortrayalCatalogues);
        SelectDatasetsCommand = new RelayCommand(() => SelectedActivity = ActivityKind.Datasets);
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
            OnPropertyChanged(nameof(IsSettingsSelected));
        });

        _isStatusBarVisible = settings.IsStatusBarVisible;

        ToggleStatusBarCommand = new RelayCommand(() => IsStatusBarVisible = !IsStatusBarVisible);

        _isPickPanelEnabled = settings.IsPickPanelVisible;
        TogglePickPanelCommand = new RelayCommand(() => IsPickPanelEnabled = !IsPickPanelEnabled);

        TogglePickModeCommand = new RelayCommand(() => IsPickModeActive = !IsPickModeActive);
        ExitPickModeCommand = new RelayCommand(() => IsPickModeActive = false);

        ToggleThemeCommand = new RelayCommand(() =>
        {
            if (Application.Current is { } app)
            {
                var next = app.ActualThemeVariant == ThemeVariant.Dark
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark;
                app.RequestedThemeVariant = next;
                IsDarkTheme = next == ThemeVariant.Dark;
            }
        });

        // Restore last selected activity (set field directly to avoid re-saving)
        if (settings.LastSelectedActivity is { } last
            && Enum.TryParse<ActivityKind>(last, out var restored))
        {
            _selectedActivity = restored;
        }
    }
}
