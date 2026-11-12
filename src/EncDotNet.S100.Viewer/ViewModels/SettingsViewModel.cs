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
}
