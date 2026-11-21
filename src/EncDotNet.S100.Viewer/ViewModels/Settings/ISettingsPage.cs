namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A page of the Settings panel (#1398). Pages are registered in DI and the
/// Settings shell lists them by <see cref="Order"/>, so adding a page touches
/// no constructor.
/// </summary>
internal interface ISettingsPage
{
    /// <summary>Which page this is.</summary>
    SettingsCategory Category { get; }

    /// <summary>The page's name in the category list.</summary>
    string Title { get; }

    /// <summary>Where the page sits in the category list: lower comes first.</summary>
    int Order { get; }

    /// <summary>The automation id of the page's entry in the category list.</summary>
    string AutomationId { get; }

    /// <summary>A line about the page's state under its name, or <see langword="null"/>.</summary>
    string? StatusText { get; }

    /// <summary>The page was selected.</summary>
    void OnShown();
}
