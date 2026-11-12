using EncDotNet.S100.Collections.KnownSources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// What the "Add online source" directory last showed, kept for the app's
/// lifetime (not persisted), so reopening the dialog returns to the same
/// category and source until the app restarts (design note Q5).
/// </summary>
internal sealed class CatalogueDirectorySession
{
    /// <summary>
    /// The id of the source last selected in each category, so switching back
    /// to a category selects it again. Empty before the dialog was first used.
    /// </summary>
    public Dictionary<KnownCatalogueCategory, string> LastSourceIds { get; } = [];

    /// <summary>The category last open, or null before the dialog was first used.</summary>
    public KnownCatalogueCategory? LastCategory { get; set; }
}
