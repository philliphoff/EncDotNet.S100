namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// What an expanded Chart reference row shows (#1637), until the Reference
/// tab exists: the definition, the display categories (D6), the Chart 1
/// section and viewing group, and Drawn with or Used by.
/// </summary>
internal sealed class ChartReferenceRowDetails
{
    /// <summary>The catalogue's definition or description, or <see langword="null"/>.</summary>
    public string? Definition { get; init; }

    /// <summary>Whether there is a definition.</summary>
    public bool HasDefinition => !string.IsNullOrWhiteSpace(Definition);

    /// <summary>The display categories, one line per geometry or case when they differ (D6).</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Whether there are display categories.</summary>
    public bool HasCategories => Categories.Count > 0;

    /// <summary>The Chart 1 section, or <see langword="null"/>.</summary>
    public string? Section { get; init; }

    /// <summary>Whether there is a Chart 1 section.</summary>
    public bool HasSection => Section is not null;

    /// <summary>The viewing group's id and name, or <see langword="null"/>.</summary>
    public string? ViewingGroup { get; init; }

    /// <summary>Whether there is a viewing group.</summary>
    public bool HasViewingGroup => ViewingGroup is not null;

    /// <summary>A feature type's symbols, line styles and fills, each with its conditions in words.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> DrawnWith { get; init; } = [];

    /// <summary>Whether there is anything under Drawn with.</summary>
    public bool HasDrawnWith => DrawnWith.Count > 0;

    /// <summary>Whether the feature type has no symbol of its own.</summary>
    public bool NoSymbol { get; init; }

    /// <summary>A portrayal item's feature types.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> UsedBy { get; init; } = [];

    /// <summary>Whether any feature type uses the item.</summary>
    public bool HasUsedBy => UsedBy.Count > 0;

    /// <summary>The family a symbol belongs to, as a one-item list, or empty.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> Family { get; init; } = [];

    /// <summary>Whether the symbol belongs to a family.</summary>
    public bool HasFamily => Family.Count > 0;

    /// <summary>Short notes on a portrayal item: a family's size, "Not used by a rule", its source.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
