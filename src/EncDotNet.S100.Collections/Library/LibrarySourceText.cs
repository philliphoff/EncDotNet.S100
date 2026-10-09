using System.Globalization;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Manifests;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// The words used when a Library source is being added (#792): titles,
/// catalogue details, scope summaries and suggested names, shared by the
/// viewer's Add-to-Library dialog and headless hosts.
/// </summary>
public static class LibrarySourceText
{
    /// <summary>The title of an add: the known catalogue's name, else the kind's.</summary>
    /// <param name="kind">What is being added.</param>
    /// <param name="known">The known catalogue being added, if any.</param>
    /// <param name="editingManifest">True when a manifest source's groups are being chosen again.</param>
    public static string Title(LibrarySourceKind kind, KnownCatalogueSource? known, bool editingManifest = false) =>
        known?.Name ?? kind switch
        {
            LibrarySourceKind.NoaaFeed => LibraryText.Get("Library_AddNoaaTitle"),
            LibrarySourceKind.UsaceFeed => LibraryText.Get("Library_AddUsaceTitle"),
            LibrarySourceKind.LocalManifest => LibraryText.Get(editingManifest ? "Manifest_ChooseGroupsTitle" : "Manifest_DialogTitle"),
            _ => LibraryText.Get("Library_AddTitle"),
        };

    /// <summary>The online catalogue being read: the known catalogue's, else the kind's default.</summary>
    /// <param name="kind">What is being added.</param>
    /// <param name="known">The known catalogue being added, if any.</param>
    public static Uri CatalogUri(LibrarySourceKind kind, KnownCatalogueSource? known) =>
        known?.CatalogUri ?? (kind == LibrarySourceKind.UsaceFeed
            ? UsaceIencFeedSource.RiversCatalogUri
            : NoaaEncFeedSource.DefaultCatalogUri);

    /// <summary>"host · catalogue dated 2026-09-17", or just the host until the catalogue is read.</summary>
    /// <param name="catalogUri">The catalogue's URL.</param>
    /// <param name="date">The date the catalogue declares, once read.</param>
    public static string CatalogueDetail(Uri catalogUri, DateOnly? date)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        return date is { } d
            ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_DatedFormat"), catalogUri.Host,
                d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            : catalogUri.Host;
    }

    /// <summary>True when a catalogue is more than a year old (it may no longer be maintained).</summary>
    /// <param name="date">The date the catalogue declares.</param>
    /// <param name="now">The current time.</param>
    public static bool IsStale(DateOnly? date, DateTimeOffset now) =>
        date is { } d && DateOnly.FromDateTime(now.UtcDateTime).DayNumber - d.DayNumber > 365;

    /// <summary>
    /// The summary under the choices: what is included, a reminder that ticks
    /// are kept while everything is included, or a prompt to tick something.
    /// </summary>
    /// <param name="includeAll">True when the whole catalogue is included.</param>
    /// <param name="selectedCount">How many choices are ticked.</param>
    /// <param name="selectionSummary">What the ticks (or, with none, the whole catalogue) amount to.</param>
    public static string ScopeSummary(bool includeAll, int selectedCount, string selectionSummary) =>
        includeAll
            ? selectedCount > 0
                ? string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_KeptPicksFormat"), selectedCount)
                : selectionSummary
            : selectedCount > 0 ? selectionSummary : LibraryText.Get("Wizard_TickAtLeastOne");

    /// <summary>The summary for a catalogue whose datasets are listed, not downloaded: "… · nothing is downloaded".</summary>
    /// <param name="selectionSummary">What the selection amounts to.</param>
    public static string NothingDownloads(string selectionSummary) =>
        string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_NothingDownloadsFormat"), selectionSummary);

    /// <summary>The default collection name for an online catalogue (not a manifest), or null for a local source.</summary>
    /// <param name="kind">What is being added.</param>
    /// <param name="known">The known catalogue being added, if any.</param>
    public static string? FeedName(LibrarySourceKind kind, KnownCatalogueSource? known) => known?.Name ?? kind switch
    {
        LibrarySourceKind.NoaaFeed => LibraryText.Get("Library_NoaaFeed"),
        LibrarySourceKind.UsaceFeed => LibraryText.Get("Library_UsaceFeed"),
        _ => null,
    };

    /// <summary>The default collection name for a local path: its file or folder name (a manifest's without its suffix).</summary>
    /// <param name="path">The path, or null.</param>
    public static string DefaultName(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = trimmed.EndsWith(CollectionManifest.FileSuffix, StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(trimmed)[..^CollectionManifest.FileSuffix.Length]
            : Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    /// <summary>The suggested name that follows the selection: "NOAA ENC — Alaska", or the base name for everything.</summary>
    /// <param name="baseName">The feed's name.</param>
    /// <param name="unscoped">True when everything is included.</param>
    /// <param name="describe">Describes the selection.</param>
    public static string SuggestedName(string baseName, bool unscoped, Func<string> describe)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(describe);
        return unscoped ? baseName : $"{baseName} — {describe()}";
    }

    /// <summary>"A, B, C", or "A, B and 3 more" past three.</summary>
    /// <param name="labels">The labels.</param>
    public static string? List(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        return labels.Count switch
        {
            0 => null,
            <= 3 => string.Join(", ", labels),
            _ => string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Library_AndMoreFormat"), string.Join(", ", labels.Take(2)), labels.Count - 2),
        };
    }

    /// <summary>"N cells · X MB" (or the "all" form) for a cell selection.</summary>
    /// <param name="all">True when the selection is the whole catalogue.</param>
    /// <param name="count">The cells selected.</param>
    /// <param name="bytes">Their download size.</param>
    public static string Cells(bool all, int count, long bytes) =>
        string.Format(CultureInfo.CurrentCulture,
            LibraryText.Get(all ? "Library_NoaaSelectionAllFormat" : "Library_NoaaSelectionFormat"),
            count, LibraryTextFormat.Bytes(bytes));

    /// <summary>"12,345 cells · 1.2 GB" for a whole catalogue.</summary>
    /// <param name="count">The catalogue's cells.</param>
    /// <param name="bytes">Their download size.</param>
    public static string EverythingCells(int count, long bytes) =>
        string.Format(CultureInfo.CurrentCulture, LibraryText.Get("Wizard_EverythingCellsFormat"), count, LibraryTextFormat.Bytes(bytes));
}
