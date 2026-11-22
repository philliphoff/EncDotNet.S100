namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// An item of the Chart reference list: a section header or an entry row.
/// The list holds both, flat, so it can virtualise (S-401 has 926 items).
/// </summary>
internal abstract class ChartReferenceListItem : ViewModelBase
{
    /// <summary>The item's accessible name: the section's title or the entry's name.</summary>
    public abstract string Name { get; }

    /// <summary>Whether the item is an entry row, which can be selected, rather than a header.</summary>
    public abstract bool IsRow { get; }

    /// <summary>
    /// The list item's automation id: <c>ChartReference.Row</c> for a row,
    /// <c>ChartReference.Section</c> for a header.
    /// </summary>
    public string AutomationId => IsRow ? "ChartReference.Row" : "ChartReference.Section";
}
