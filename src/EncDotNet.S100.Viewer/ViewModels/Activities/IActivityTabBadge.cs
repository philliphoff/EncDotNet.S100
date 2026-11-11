namespace EncDotNet.S100.Viewer.ViewModels.Activities;

/// <summary>
/// Opt-in companion interface for a tab view-model whose activity-bar button
/// shows a count badge (the Navigational warnings tab's new warnings, #932).
/// Raise <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
/// for <see cref="BadgeCount"/> when it changes.
/// </summary>
internal interface IActivityTabBadge : System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>The count on the badge; 0 hides it.</summary>
    int BadgeCount { get; }

    /// <summary>The badge's accessible name, e.g. "3 new".</summary>
    string BadgeName { get; }
}
