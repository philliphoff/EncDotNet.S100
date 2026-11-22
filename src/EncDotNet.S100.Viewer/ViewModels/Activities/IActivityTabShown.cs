namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Opt-in companion interface for a tab view-model that needs to know when
/// the user can see it (the Logs tab clears its unread badge, #1080; Chart
/// reference ends a row's hover, #1655). <see cref="ShellLayoutViewModel"/>
/// sets <see cref="IsTabShown"/> when the tab's dock opens or closes, or its
/// selected tab changes.
/// </summary>
internal interface IActivityTabShown
{
    /// <summary>Whether the tab is the selected tab of an open, shown dock.</summary>
    bool IsTabShown { get; set; }
}
