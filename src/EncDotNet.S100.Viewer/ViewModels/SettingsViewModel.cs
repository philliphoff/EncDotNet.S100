namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Settings panel (#845): a category list beside the selected page. The
/// pages are registered in DI as <see cref="ISettingsPage"/> and listed by
/// their order (#1398); the settings themselves live in
/// <see cref="Services.ViewerSettingsService"/>.
/// </summary>
internal sealed class SettingsViewModel : ViewModelBase
{
    private ISettingsPage _selectedPage;

    /// <summary>Creates the panel.</summary>
    /// <param name="pages">Its pages, in any order.</param>
    public SettingsViewModel(IEnumerable<ISettingsPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        Pages = [.. pages.OrderBy(p => p.Order)];
        if (Pages.Count == 0)
            throw new ArgumentException("Settings needs at least one page.", nameof(pages));
        _selectedPage = Pages[0];
    }

    /// <summary>The pages, in the order the category list shows them.</summary>
    public IReadOnlyList<ISettingsPage> Pages { get; }

    /// <summary>The page the panel shows. Kept for the session, not persisted.</summary>
    public ISettingsPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            // The category list clears its selection while it's rebuilt.
            if (value is null)
                return;
            if (SetProperty(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(SelectedCategory));
                value.OnShown();
            }
        }
    }

    /// <summary>
    /// The category of <see cref="SelectedPage"/>. Setting a category that
    /// has no page leaves the selection as it is.
    /// </summary>
    public SettingsCategory SelectedCategory
    {
        get => _selectedPage.Category;
        set
        {
            if (Pages.FirstOrDefault(p => p.Category == value) is { } page)
                SelectedPage = page;
        }
    }

    /// <summary>
    /// The setting <see cref="ShowSetting"/> last asked for: the automation id
    /// of its control, "Settings.SimplifiedSymbols", or <see langword="null"/>.
    /// </summary>
    public string? RequestedSetting { get; private set; }

    /// <summary>
    /// Raised when <see cref="ShowSetting"/> asks for a setting: the view
    /// brings its control into view and focuses it.
    /// </summary>
    public event EventHandler<string>? SettingRequested;

    /// <summary>
    /// Shows <paramref name="category"/>'s page at one setting's control, as a
    /// display setting in Why this symbol (R11, #1680) or the Reference tab's
    /// <b>Change in Display settings</b> asks.
    /// </summary>
    /// <param name="category">The page.</param>
    /// <param name="setting">The setting, as its control's automation id ends: <c>SimplifiedSymbols</c>.</param>
    public void ShowSetting(SettingsCategory category, string setting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        SelectedCategory = category;
        RequestedSetting = "Settings." + setting;
        SettingRequested?.Invoke(this, RequestedSetting);
    }

    /// <summary>The view has shown <see cref="RequestedSetting"/>; a later view won't show it again.</summary>
    internal void SettingShown() => RequestedSetting = null;
}
