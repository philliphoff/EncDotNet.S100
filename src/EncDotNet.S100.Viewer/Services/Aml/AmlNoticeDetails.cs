using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Aml;

/// <summary>
/// The AML notification's <em>Details</em> (#1371, decision 12): opens the
/// set's Library item, or the set in the Datasets panel when the Library
/// doesn't hold it.
/// </summary>
internal static class AmlNoticeDetails
{
    /// <summary>Opens the Library item of <paramref name="cell"/>, else selects <paramref name="header"/>.</summary>
    /// <param name="cell">The display name of one of the set's AML cells.</param>
    /// <param name="header">The set's header in the Datasets panel, or <see langword="null"/>.</param>
    /// <param name="library">The Library panel.</param>
    /// <param name="datasets">The Datasets panel.</param>
    /// <param name="layout">Brings a panel forward.</param>
    public static void Open(
        string cell,
        ExchangeSetHeader? header,
        LibraryPanelViewModel library,
        DatasetsViewModel datasets,
        ShellLayoutViewModel layout)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(layout);

        if (FindLibraryItem(library, cell) is { } found && library.Reveal(found.SourceId, found.Key))
            return;

        // Not in the Library: show the set in the Datasets panel instead.
        layout.SelectTab(ShellLayoutViewModel.DefaultTabId);
        if (header is not null)
            datasets.SelectedSourceNode = header;
    }

    /// <summary>The Library item of the AML cell named <paramref name="cell"/>, if the Library holds one.</summary>
    internal static (Guid SourceId, string Key)? FindLibraryItem(LibraryPanelViewModel library, string cell)
    {
        var stem = Path.GetFileNameWithoutExtension(cell);
        foreach (var (item, source) in library.Tree.Nodes.SelectMany(n => n.EnumerateItems()))
        {
            if (!string.Equals(item.ProductSpec, AmlDatasetInfo.SpecName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.Equals(item.Name, cell, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(item.Name), stem, StringComparison.OrdinalIgnoreCase))
            {
                return (source.Id, item.Key);
            }
        }

        return null;
    }
}
