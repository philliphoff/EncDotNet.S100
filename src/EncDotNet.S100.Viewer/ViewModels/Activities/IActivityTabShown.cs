namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Opt-in companion interface for a bottom-dock tab view-model that needs to
/// know when the user can see it (the Logs tab clears its unread badge, #1080).
/// <see cref="MainViewModel"/> sets <see cref="IsTabShown"/> when the bottom
/// dock opens or closes, or its selected tab changes.
/// </summary>
internal interface IActivityTabShown
{
    /// <summary>Whether the tab is the selected tab of an open, shown dock.</summary>
    bool IsTabShown { get; set; }
}
