namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A section header in the Chart reference list: a Chart 1 section, "No
/// Chart 1 section" or "Not used by a rule" (design note D3).
/// </summary>
internal sealed class ChartReferenceHeaderViewModel : ChartReferenceListItem
{
    /// <summary>Creates a header.</summary>
    /// <param name="title">The section's title.</param>
    /// <param name="count">How many rows the section has.</param>
    public ChartReferenceHeaderViewModel(string title, int count)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
        Count = count;
    }

    /// <summary>The section's title.</summary>
    public string Title { get; }

    /// <summary>How many rows the section has.</summary>
    public int Count { get; }

    /// <inheritdoc />
    public override string Name => Title;

    /// <inheritdoc />
    public override bool IsRow => false;
}
