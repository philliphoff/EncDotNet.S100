namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// A symbol, line style or area fill of the portrayal catalogue, or a
/// family of symbols a rule assembles from numbers (design note D3).
/// </summary>
public sealed class PortrayalItemEntry : ReferenceEntry
{
    /// <summary>
    /// The catalogue file name, such as <c>BCNLAT21.svg</c>, or
    /// <see langword="null"/> for a family.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// The symbol's <c>iho:source</c> SVG metadata, such as
    /// <c>S52Preslib4.0</c> or <c>new</c>, or <see langword="null"/> when
    /// the item isn't a symbol or doesn't say. Feeds the "New in S-101"
    /// badge.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// <see langword="true"/> for a family entry, whose
    /// <see cref="ReferenceEntry.Code"/> is the prefix shared by its
    /// <see cref="Members"/>, such as <c>SOUNDG</c>.
    /// </summary>
    public bool IsFamily { get; init; }

    /// <summary>The ids of a family's members; empty for an item.</summary>
    public IReadOnlyList<string> Members { get; init; } = [];

    /// <summary>
    /// The prefix of the family the item belongs to, when a rule assembles
    /// it from numbers, or <see langword="null"/>.
    /// </summary>
    public string? Family { get; init; }

    /// <summary>
    /// The codes of the feature types whose rules draw the item, directly,
    /// through a shared rule, or through its family.
    /// </summary>
    public IReadOnlyList<string> UsedBy { get; init; } = [];

    /// <summary>
    /// The rule files, without their extensions, that name the item or its
    /// family.
    /// </summary>
    public IReadOnlyList<string> NamedByRules { get; init; } = [];

    /// <summary>
    /// The line styles and area fills that place the symbol: along a line
    /// (a line style's <c>&lt;symbol reference&gt;</c>) or as a fill's
    /// pattern. Empty for a line style, an area fill or a family.
    /// </summary>
    public IReadOnlyList<ReferenceKey> UsedIn { get; init; } = [];

    /// <summary>
    /// The ids of the symbols a line style places along the line or an area
    /// fill tiles as its pattern; empty for a symbol or a family.
    /// </summary>
    public IReadOnlyList<string> Places { get; init; } = [];

    /// <summary>
    /// <see langword="true"/> when the item is never drawn: no rule names
    /// it or its family, and no line style or area fill that a rule names
    /// places it (listed under "Not used by a rule", design note D3).
    /// </summary>
    public bool IsUnreached { get; init; }
}
