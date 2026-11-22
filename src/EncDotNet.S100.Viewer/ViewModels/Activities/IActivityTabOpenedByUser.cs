namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Implemented by a tab view model that resets some state when the user opens
/// it from its activity bar button or a menu, as opposed to a link that opens
/// it at a given place: Chart reference turns In view off (symbol dictionary
/// §4.5, R5), which the Legend tab's Show all link turns on.
/// </summary>
internal interface IActivityTabOpenedByUser
{
    /// <summary>The user opened the tab from its activity bar button or a menu.</summary>
    void OnOpenedByUser();
}
