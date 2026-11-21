using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Services;
using ShadUI;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The window's app-level commands: the light/dark theme toggle and the
/// Report Feedback and About dialogs, shown through the ShadUI dialog manager.
/// </summary>
internal sealed class AppCommandsViewModel : ViewModelBase
{
    private readonly IThemeService _theme;
    private readonly ShadUI.DialogManager _dialogManager;
    private readonly Func<FeedbackDialogViewModel> _feedbackDialogFactory;
    private readonly Func<AboutDialogViewModel> _aboutDialogFactory;

    public AppCommandsViewModel(
        IThemeService themeService,
        ShadUI.DialogManager dialogManager,
        Func<FeedbackDialogViewModel> feedbackDialogFactory,
        Func<AboutDialogViewModel> aboutDialogFactory)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(dialogManager);
        ArgumentNullException.ThrowIfNull(feedbackDialogFactory);
        ArgumentNullException.ThrowIfNull(aboutDialogFactory);

        _theme = themeService;
        _dialogManager = dialogManager;
        _feedbackDialogFactory = feedbackDialogFactory;
        _aboutDialogFactory = aboutDialogFactory;
        DialogManager = dialogManager;
        _isDarkTheme = themeService.IsDarkTheme;

        ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = _theme.ToggleTheme());
        ShowFeedbackCommand = new AsyncRelayCommand(ShowFeedbackAsync);
        ShowAboutCommand = new AsyncRelayCommand(ShowAboutAsync);

        // Keep IsDarkTheme in sync when the theme is changed via paths
        // other than ToggleThemeCommand (e.g. the SettingsView chrome
        // selector, which routes through IThemeService.SetTheme).
        _theme.ThemeChanged += (_, _) => IsDarkTheme = _theme.IsDarkTheme;
    }

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    public ICommand ToggleThemeCommand { get; }

    /// <summary>
    /// ShadUI dialog manager bound by the main window's
    /// <c>DialogHost</c>; used to present the feedback dialog and any
    /// future modal dialogs.
    /// </summary>
    public ShadUI.DialogManager DialogManager { get; }

    /// <summary>
    /// Opens the "Report Feedback" modal dialog. Triggered from the
    /// app-bar button and the Help menu.
    /// </summary>
    public ICommand ShowFeedbackCommand { get; }

    /// <summary>
    /// Opens the "About" modal dialog (version info + update check).
    /// Triggered from the Help menu.
    /// </summary>
    public ICommand ShowAboutCommand { get; }

    /// <summary>
    /// Collects diagnostics + an optional screenshot, then presents the
    /// feedback dialog via the ShadUI dialog manager.
    /// </summary>
    private async Task ShowFeedbackAsync()
    {
        var dialog = _feedbackDialogFactory();
        await dialog.InitializeAsync();
        _dialogManager.CreateDialog(dialog)
            .Dismissible()
            .WithMaxWidth(620)
            .Show();
    }

    private async Task ShowAboutAsync()
    {
        var dialog = _aboutDialogFactory();
        _dialogManager.CreateDialog(dialog)
            .Dismissible()
            .WithMaxWidth(480)
            .Show();

        // Kick off the (throttled) update check after the dialog is shown so
        // the UI appears immediately and the network call resolves into it.
        await dialog.InitializeAsync().ConfigureAwait(true);
    }
}
