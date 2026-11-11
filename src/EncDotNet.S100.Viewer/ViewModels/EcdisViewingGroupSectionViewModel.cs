namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// A titled subsection of viewing-group checkboxes within a single
/// spec in the ECDIS display-controls panel. Sections group the
/// otherwise-flat viewing-group list into readable buckets (e.g.
/// "Depths, contours &amp; soundings"). A section whose
/// <see cref="Title"/> is <see langword="null"/> renders without a
/// header — used for specs that declare no curated sections, which
/// fall back to a single flat list.
/// </summary>
internal sealed class EcdisViewingGroupSectionViewModel : ViewModelBase
{
    public EcdisViewingGroupSectionViewModel(
        string? title,
        IReadOnlyList<EcdisViewingGroupViewModel> viewingGroups)
    {
        Title = title;
        ViewingGroups = viewingGroups;
    }

    /// <summary>Section heading, or <see langword="null"/> for an unsectioned list.</summary>
    public string? Title { get; }

    /// <summary>True when a heading should be shown.</summary>
    public bool HasTitle => !string.IsNullOrEmpty(Title);

    /// <summary>Viewing-group checkboxes in this section.</summary>
    public IReadOnlyList<EcdisViewingGroupViewModel> ViewingGroups { get; }

    /// <summary>Refreshes every viewing-group checkbox in this section.</summary>
    internal void Refresh()
    {
        foreach (var vg in ViewingGroups)
            vg.Refresh();
    }
}
